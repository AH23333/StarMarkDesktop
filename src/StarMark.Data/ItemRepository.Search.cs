#nullable enable
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.Language;
using ActivityKind = StarMark.Abstractions.ActivityKind;

namespace StarMark.Data;

/// <summary>
/// 搜索与计数：两阶段 FTS 查询 ＋ 条目类型计数。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>
/// （partial）——按 SQLite 访问面分文件，不是一个新抽象层。
/// </summary>
public sealed partial class ItemRepository
{
    // ===== 搜索（两阶段查询）=====

    public async Task<SearchResult> SearchAsync(string keyword, SearchFilter filter, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // 纯空白关键词 → FTS 表达式为空串；直接返回空结果，避免把 MATCH '' 交给 FTS5 触发语句级语法错误。
        var ftsQuery = BuildFtsQuery(keyword);
        if (ftsQuery.Length == 0)
            return new SearchResult { Items = Array.Empty<Item>(), Total = 0, ElapsedMs = sw.ElapsedMilliseconds };

        using var conn = _factory.Open();
        // 阶段 1：FTS5 MATCH 先缩小文本范围（命中倒排索引，毫秒级）
        // 阶段 2：JOIN 主表做数值过滤 + 完整字段 hydration
        var orderBy = filter.Sort switch
        {
            "stars" => "i.stars_count DESC NULLS LAST, MIN(f.rank)",
            "name" => "i.title COLLATE NOCASE ASC, MIN(f.rank)",
            "recent" => "i.updated_at DESC, MIN(f.rank)",
            // 最近 Star：GitHubStar 用 extra_json 的 starredAt（无则退回 updated_at）。
            "starred" => $"COALESCE(CAST(json_extract({ExtraJsonGuard.Safe("i.extra_json")}, '$.StarredAt') AS INTEGER), i.updated_at) DESC, MIN(f.rank)",
            // 最近收藏：条目入库时间。
            "collected" => "i.created_at DESC, MIN(f.rank)",
            _ => "MIN(f.rank)",
        };
        // 语言闸门只约束 GitHubStar：LanguageDetector.EnsureLanguage 会给任意来源（含本地文件）
        // 按扩展名兜底打 Language，所以 @lang 若不加豁免，选中语言就会把已入库的本地文件行
        // 一起筛掉（.mp3 被当成"非 C#"消失）。产品口径：语言对本地文件无效，见 SearchService 同类豁免。
        //
        // P-49 修复：**所有谓词都在 LIMIT 之前生效**。原写法把 type/lang/hidden/标签谓词留在外层
        // WHERE，而 LIMIT 在 CTE 内先按相关度截断 ⇒ 被过滤掉的行照样占名额：结果页偏短，分页时
        // HasMore 还会提前判 false（用户读作"才 60 条就到底了"）。谓词下进 CTE 后，LIMIT 数的是
        // 真正通过筛选的行。代价是 join+谓词要跑在全部 FTS 命中上——但 bm25 排序本来就要给每条
        // 命中算 rank 并排序，这里只是常数级增加，且标签 EXISTS 走 idx_item_tags_tag。
        var filteredWhere = $@"
            WHERE items_fts MATCH @keyword
              AND (@type_filter IS NULL OR i.type = @type_filter)
              AND (@stars_min IS NULL OR i.stars_count >= @stars_min)
              AND (@date_from IS NULL OR i.updated_at >= @date_from)
              AND (@lang IS NULL OR i.type = 'file' OR json_extract({ExtraJsonGuard.Safe("i.extra_json")}, '$.Language') = @lang)
              AND (@include_hidden = 1 OR i.hidden = 0)"
            + BuildTagClause(filter.Tags, "i");

        var sql = @"
            WITH fts_hits AS (
                SELECT items_fts.rowid AS rowid, bm25(items_fts) AS rank
                FROM items_fts
                JOIN items i ON i.id = items_fts.rowid"
            + filteredWhere + @"
                ORDER BY rank
                LIMIT @limit
            )
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM fts_hits f
            JOIN items i ON i.id = f.rowid
            GROUP BY i.id
            ORDER BY " + orderBy + ";";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@keyword", ftsQuery);
        // 分页语义＝合并后切片（P-42 路线 D）：本腿只认 MaxResults、从 0 取，不在 SQL 里 OFFSET
        // （SearchService 会把窗口加宽到"本页末"，再在合并去重之后 Skip）。原先的 OFFSET @offset
        // 已移除——两条腿各偏移 + 合并侧再截断 = 每翻一页永久跳过一批未展示过的条目。
        cmd.Parameters.AddWithValue("@limit", filter.MaxResults);
        cmd.Parameters.AddWithValue("@type_filter", (object?)filter.Type?.ToString().ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stars_min", (object?)filter.StarsMin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_from", (object?)filter.DateFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lang", (object?)filter.Language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@include_hidden", filter.IncludeHidden ? 1 : 0);
        BindTagParams(cmd, filter.Tags);

        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(MapItem(reader));
        }

        // 总数估算：FTS 命中数（不应用数值过滤前的总数，简化实现）
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = @"
            SELECT COUNT(*) FROM items_fts WHERE items_fts MATCH @keyword;";
        countCmd.Parameters.AddWithValue("@keyword", ftsQuery);
        var totalObj = await countCmd.ExecuteScalarAsync(ct);
        var total = totalObj is long v ? (int)v : 0;

        return new SearchResult
        {
            Items = items,
            Total = total,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    // ===== 状态统计 =====

    public async Task<Dictionary<ItemType, int>> GetCountsByTypeAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT type, COUNT(*) FROM items
            WHERE hidden = 0
            GROUP BY type;";
        var result = new Dictionary<ItemType, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var typeStr = reader.GetString(0);
            var count = reader.GetInt32(1);
            if (Enum.TryParse<ItemType>(typeStr, ignoreCase: true, out var t))
            {
                result[t] = count;
            }
        }
        return result;
    }

    /// <summary>
    /// 列出 star 条目里<b>实际存在</b>的编程语言（去重并归一化为目录规范名）。
    /// 主界面语言下拉据此渲染，确保只显示真实出现在 star 项目中的语言，
    /// 绝不会出现当前 star 项目不存在的语言选项。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetStarLanguagesAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // 守卫一次拼好、四处复用：这一列里混进坏 JSON 时，取不出语言 ≠ 整条语句失败（P-17）。
        var langOf = $"json_extract({ExtraJsonGuard.Safe("i.extra_json")}, '$.Language')";
        cmd.CommandText = $@"
            SELECT DISTINCT trim({langOf}) AS lang
            FROM items i
            WHERE i.type = 'githubstar'
              AND {langOf} IS NOT NULL
              AND trim({langOf}) <> ''
            ORDER BY lang;";
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0)) continue;
            var raw = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            list.Add(LanguageCatalog.Normalize(raw.Trim()));
        }
        return list
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

}
