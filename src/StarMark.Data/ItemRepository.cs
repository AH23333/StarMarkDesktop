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
/// 仓储实现。所有 SQLite 访问通过此类，调用方使用 <see cref="IItemRepository"/>。
/// 实现：
/// - 两阶段查询（FTS5 MATCH → JOIN items 数值过滤）— 技术文档 §4.6
/// - 标签 / 笔记 CRUD
/// - 条目类型计数
/// </summary>
public sealed partial class ItemRepository : IItemRepository
{
    private readonly DbConnectionFactory _factory;

    public ItemRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    // ===== 标签 AND 过滤辅助 =====

    /// <summary>
    /// 生成标签 AND 过滤的 SQL 片段。每个标签一个 EXISTS 子查询（可走 idx_item_tags_tag），
    /// 语义为「必须同时具备全部标签」，对齐浏览器扩展的 <c>opts.tags.every(...)</c>。
    /// </summary>
    /// <param name="tags">标签集合；空集合返回空串。</param>
    /// <param name="itemAlias">items 表别名。</param>
    private static string BuildTagClause(IReadOnlyList<string>? tags, string itemAlias)
    {
        if (tags is not { Count: > 0 }) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < tags.Count; i++)
        {
            sb.Append(" AND EXISTS (SELECT 1 FROM item_tags itf").Append(i)
              .Append(" JOIN tags tf").Append(i).Append(" ON tf").Append(i).Append(".id = itf").Append(i).Append(".tag_id")
              .Append(" WHERE itf").Append(i).Append(".item_id = ").Append(itemAlias).Append(".id")
              .Append(" AND tf").Append(i).Append(".name = @tagfilter").Append(i).Append(')');
        }
        return sb.ToString();
    }

    /// <summary>绑定 <see cref="BuildTagClause"/> 生成的参数。标签为空时不添加任何参数。</summary>
    private static void BindTagParams(SqliteCommand cmd, IReadOnlyList<string>? tags)
    {
        if (tags is not { Count: > 0 }) return;
        for (int i = 0; i < tags.Count; i++)
            cmd.Parameters.AddWithValue($"@tagfilter{i}", tags[i]);
    }

    // ===== 内部辅助 =====

    private static async Task UpsertOne(SqliteConnection conn, Item item, CancellationToken ct)
    {
        // URL 归一化：http(s) 源的 source_id 归一为标准形，避免同一资源的不同变体
        // 分裂成多条记录（见扩展对比方案 P1-5）。幂等：非 http(s)（file:// 等）原样返回。
        item.SourceId = StarMark.Abstractions.UriNormalizer.Normalize(item.SourceId);

        // 用户状态（hidden / pinned / notes）是本地编辑，源侧同步不应覆盖。
        // 先读旧值合并进 item：既保住状态，又让 search_text 计算包含用户笔记。
        using (var existing = conn.CreateCommand())
        {
            existing.CommandText = @"
                SELECT hidden, notes, pinned,
                       COALESCE((SELECT GROUP_CONCAT(t.name, char(31))
                                 FROM item_tags it JOIN tags t ON t.id = it.tag_id
                                 WHERE it.item_id = items.id), '')
                FROM items
                WHERE source = @src AND source_id = @sid LIMIT 1;";
            existing.Parameters.AddWithValue("@src", item.Source);
            existing.Parameters.AddWithValue("@sid", (object?)item.SourceId ?? DBNull.Value);
            await using var reader = await existing.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                item.Hidden = reader.GetInt64(0) != 0;
                item.Notes = reader.IsDBNull(1) ? null : reader.GetString(1);
                item.Pinned = !reader.IsDBNull(2) && reader.GetInt64(2) != 0;
                // D4：同步不应把用户已加的标签从索引里冲掉——内存未带标签时用库中既有标签兜底，
                // 否则 AddTagAsync 刚重建好的 search_text 会在下一次 re-sync 被空 Tags 覆盖。
                if (item.Tags.Count == 0 && !reader.IsDBNull(3))
                {
                    var dbTagsRaw = reader.GetString(3);
                    if (!string.IsNullOrEmpty(dbTagsRaw))
                        item.Tags.AddRange(dbTagsRaw.Split(new[] { (char)31 }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        // 搜索文本 = title + description + notes + tags（标签参与全文搜索）
        var searchText = new StringBuilder();
        searchText.Append(item.Title).Append(' ');
        if (!string.IsNullOrEmpty(item.Description)) searchText.Append(item.Description).Append(' ');
        if (!string.IsNullOrEmpty(item.Notes)) searchText.Append(item.Notes).Append(' ');
        if (item.Tags.Count > 0) searchText.Append(string.Join(' ', item.Tags));
        // CJK 展开：unicode61 把连续中文视为单个 token，不展开则中文子串查询全部落空。
        // 详见 CjkTokenizer 类注释。只影响索引列，不影响任何展示文本。
        item.SearchText = StarMark.Abstractions.Text.CjkTokenizer.ExpandForIndex(searchText.ToString());

        // 语言识别（受 DeskBox 启发）：GitHub 主语言已标注直接归一；书签/网页按 TLD/CJK 推断，
        // 统一以 extra_json.Language 落盘，供主界面语言下拉过滤。
        item.ExtraJson = LanguageDetector.EnsureLanguage(item.ExtraJson, item.Uri, item.Title, item.Description);

        // UPSERT（基于 source+source_id 唯一索引）；UPDATE 集不包含 hidden/pinned/notes
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO items (type, source, source_id, title, subtitle, uri,
                              search_text, description, stars_count, file_size,
                              created_at, updated_at, synced_at, extra_json, hidden, notes)
            VALUES (@type, @source, @source_id, @title, @subtitle, @uri,
                    @search_text, @description, @stars_count, @file_size,
                    @created_at, @updated_at, @synced_at, @extra_json, @hidden, @notes)
            ON CONFLICT(source, source_id) DO UPDATE SET
                type = excluded.type,
                title = excluded.title,
                subtitle = excluded.subtitle,
                uri = excluded.uri,
                search_text = excluded.search_text,
                description = excluded.description,
                stars_count = excluded.stars_count,
                file_size = excluded.file_size,
                updated_at = excluded.updated_at,
                synced_at = excluded.synced_at,
                extra_json = excluded.extra_json
            RETURNING id;";
        cmd.Parameters.AddWithValue("@type", item.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@source", item.Source);
        cmd.Parameters.AddWithValue("@source_id", (object?)item.SourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", item.Title);
        cmd.Parameters.AddWithValue("@subtitle", item.Subtitle);
        cmd.Parameters.AddWithValue("@uri", item.Uri);
        cmd.Parameters.AddWithValue("@search_text", item.SearchText);
        cmd.Parameters.AddWithValue("@description", (object?)item.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stars_count", (object?)item.StarsCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@file_size", (object?)item.FileSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", item.CreatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : item.CreatedAt);
        cmd.Parameters.AddWithValue("@updated_at", item.UpdatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : item.UpdatedAt);
        cmd.Parameters.AddWithValue("@synced_at", (object?)item.SyncedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@extra_json", (object?)item.ExtraJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hidden", item.Hidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@notes", (object?)item.Notes ?? DBNull.Value);

        var idObj = await cmd.ExecuteScalarAsync(ct);
        if (idObj is long newId)
        {
            item.Id = newId;
        }

        // 标签同步：合并而非清空重建。保留用户手动添加/移除以外的影响最小化——
        // 源侧标签 INSERT OR IGNORE；用户标签保留。删除仅针对源侧已不再声明的标签
        // 无法区分归属，MVP 折衷：源侧声明标签始终存在，用户标签不因同步丢失（见 §十一）。
        if (item.Tags.Count > 0 && item.Id > 0)
        {
            // 与本地写入路径（上面的 LinkTagByNameAsync 分支）同口径：Trim 后剔除空白再去重。
            // UpsertTagLink 是「按名 get-or-create」且 tags.name 无 CHECK 约束——源侧标签里混入的空串/
            // 纯空白（书签匿名文件夹经 BookmarksFileParser.GetString 得到 ""）会建成一条真实的空白标签行，
            // " work"/"work" 也因 NOCASE 只并大小写、不并空白而裂成两行。先归一再 Distinct 消除这两类脏标签。
            foreach (var tagName in item.Tags
                         .Select(t => t?.Trim())
                         .Where(t => !string.IsNullOrWhiteSpace(t))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await UpsertTagLink(conn, item.Id, tagName!, ct);
            }
        }

        // 活动流（#51 收束）：本方法（UpsertAsync/UpsertOne）只被后台来源同步、种子与备份还原调用，
        // 一律**不**记活动事件——「最近活动」要反映的是用户主动的增/删/改，而不是后台批量写入刷屏。
        // 用户主动增删改由 UI 交互路径显式调用 LogActivityAsync（ItemAdd/ItemModify/ItemDelete）落库。
    }

}
