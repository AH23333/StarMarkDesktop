#nullable enable
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
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
public sealed class ItemRepository : IItemRepository
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

    // ===== 搜索（两阶段查询）=====

    public async Task<SearchResult> SearchAsync(string keyword, SearchFilter filter, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var conn = _factory.Open();
        // 阶段 1：FTS5 MATCH 先缩小文本范围（命中倒排索引，毫秒级）
        // 阶段 2：JOIN 主表做数值过滤 + 完整字段 hydration
        var orderBy = filter.Sort switch
        {
            "stars" => "i.stars_count DESC NULLS LAST, MIN(f.rank)",
            "name" => "i.title COLLATE NOCASE ASC, MIN(f.rank)",
            "recent" => "i.updated_at DESC, MIN(f.rank)",
            // 最近 Star：GitHubStar 用 extra_json 的 starredAt（无则退回 updated_at）。
            "starred" => "COALESCE(CAST(json_extract(i.extra_json, '$.StarredAt') AS INTEGER), i.updated_at) DESC, MIN(f.rank)",
            // 最近收藏：条目入库时间。
            "collected" => "i.created_at DESC, MIN(f.rank)",
            _ => "MIN(f.rank)",
        };
        var sql = @"
            WITH fts_hits AS (
                SELECT rowid, bm25(items_fts) AS rank
                FROM items_fts
                WHERE items_fts MATCH @keyword
                ORDER BY rank
                LIMIT @limit OFFSET @offset
            )
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM fts_hits f
            JOIN items i ON i.id = f.rowid
            WHERE (@type_filter IS NULL OR i.type = @type_filter)
              AND (@stars_min IS NULL OR i.stars_count >= @stars_min)
              AND (@date_from IS NULL OR i.updated_at >= @date_from)
              AND (@lang IS NULL OR json_extract(i.extra_json, '$.Language') = @lang)
              AND (@include_hidden = 1 OR i.hidden = 0)"
            + BuildTagClause(filter.Tags, "i") + @"
            GROUP BY i.id
            ORDER BY " + orderBy + ";";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@keyword", BuildFtsQuery(keyword));
        cmd.Parameters.AddWithValue("@limit", filter.MaxResults);
        cmd.Parameters.AddWithValue("@offset", filter.Offset);
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
        countCmd.Parameters.AddWithValue("@keyword", BuildFtsQuery(keyword));
        var totalObj = await countCmd.ExecuteScalarAsync(ct);
        var total = totalObj is long v ? (int)v : 0;

        return new SearchResult
        {
            Items = items,
            Total = total,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    // ===== 条目 CRUD =====

    public async Task<Item?> GetByIdAsync(long id, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i
            WHERE i.id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapItem(reader) : null;
    }

    public async Task UpsertAsync(IReadOnlyList<Item> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        foreach (var item in items)
        {
            await UpsertOne(conn, item, ct);
        }

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();   // 主界面/同步写入后，桌面组件要跟着变
    }

    /// <summary>
    /// 登记一条实时源虚拟条目（Everything 文件结果，Id=0）：走与 <see cref="UpsertAsync"/> 完全相同的
    /// <see cref="UpsertOne"/> 口径（按 (source, source_id) 幂等，保留既有 hidden/pinned/notes、重建 search_text），
    /// 故与后台全量索引天然合并。<b>只写这一条索引记录，不触碰磁盘上的实际文件。</b>
    /// UpsertOne 用 <c>INSERT … RETURNING id</c> 把真实 Id 回填到 <paramref name="item"/>；缺业务键时返回 0。
    /// </summary>
    public async Task<long> RecordItemAsync(Item item, CancellationToken ct)
    {
        if (item is null) return 0;
        if (string.IsNullOrEmpty(item.Source) || string.IsNullOrEmpty(item.SourceId)) return 0;

        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();
        await UpsertOne(conn, item, ct);
        await tx.CommitAsync(ct);
        DataChangeHub.Notify();   // 记录后置顶/标签/搜索等组件实时跟上
        return item.Id;
    }

    // ===== 标签 =====

    public async Task<IReadOnlyList<(string Name, int Count)>> GetAllTagsAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, COUNT(i.id) AS cnt
            FROM tags t
            LEFT JOIN item_tags it ON it.tag_id = t.id
            LEFT JOIN items i ON i.id = it.item_id AND i.hidden = 0
            GROUP BY t.id
            ORDER BY cnt DESC, t.name ASC;";
        var result = new List<(string, int)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetInt32(1)));
        }
        return result;
    }

    public async Task<List<string>> GetTagsForItemAsync(long itemId, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name FROM tags t
            JOIN item_tags it ON it.tag_id = t.id
            WHERE it.item_id = @id
            ORDER BY t.name;";
        cmd.Parameters.AddWithValue("@id", itemId);
        var tags = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tags.Add(reader.GetString(0));
        }
        return tags;
    }

    public async Task AddTagAsync(long itemId, string tagName, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        // 获取或创建标签
        long tagId;
        using (var findTag = conn.CreateCommand())
        {
            findTag.CommandText = "SELECT id FROM tags WHERE name = @name COLLATE NOCASE";
            findTag.Parameters.AddWithValue("@name", tagName);
            var existing = await findTag.ExecuteScalarAsync(ct);
            if (existing is long id)
            {
                tagId = id;
            }
            else
            {
                using var createTag = conn.CreateCommand();
                createTag.CommandText = "INSERT INTO tags(name, created_at) VALUES(@name, @now); SELECT last_insert_rowid();";
                createTag.Parameters.AddWithValue("@name", tagName);
                createTag.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                tagId = (long)(await createTag.ExecuteScalarAsync(ct))!;
            }
        }

        // 关联（幂等：INSERT OR IGNORE）
        using (var link = conn.CreateCommand())
        {
            link.CommandText = "INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at) VALUES(@item, @tag, @now)";
            link.Parameters.AddWithValue("@item", itemId);
            link.Parameters.AddWithValue("@tag", tagId);
            link.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await link.ExecuteNonQueryAsync(ct);
        }

        // 标签参与全文搜索：在同一事务内用 C# 侧 CJK 展开重建 search_text。
        await RebuildSearchTextAsync(conn, itemId, ct);

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();
    }

    public async Task RemoveTagAsync(long itemId, string tagName, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM item_tags
            WHERE item_id = @item
              AND tag_id = (SELECT id FROM tags WHERE name = @name COLLATE NOCASE);";
        cmd.Parameters.AddWithValue("@item", itemId);
        cmd.Parameters.AddWithValue("@name", tagName);
        await cmd.ExecuteNonQueryAsync(ct);
        // 删标签同样影响 search_text（标签词应随之从索引移除）。
        await RebuildSearchTextAsync(conn, itemId, ct);
        DataChangeHub.Notify();
    }

    // ===== 笔记 =====

    public async Task<string?> GetNoteAsync(long itemId, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT notes FROM items WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", itemId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result == DBNull.Value ? null : (string?)result;
    }

    public async Task SetNoteAsync(long itemId, string content, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // 更新 notes；search_text 由 RebuildSearchTextAsync 走 C# 侧 CJK 展开重建（不能在 SQL 里拼原文，
        // 否则 unicode61 把连续中文当单 token，中文子串检索失效——见 CjkTokenizer 类注释）。
        cmd.CommandText = "UPDATE items SET notes = @content WHERE id = @id;";
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@id", itemId);
        await cmd.ExecuteNonQueryAsync(ct);
        await RebuildSearchTextAsync(conn, itemId, ct);
        DataChangeHub.Notify();
    }

    /// <summary>
    /// 依据 items 行当前的 title/description/notes 与关联标签，用 <c>CjkTokenizer.ExpandForIndex</c> 重建 search_text。
    /// 与 <c>UpsertOne</c> 的拼接口径一致（title + description + notes + tags），确保中文/加删标签后全文检索不失效。
    /// 传入的连接若正处事务中，命令自动 enlist（与既有 <c>AddTagAsync</c> 内建命令同行为）。
    /// </summary>
    private static async Task RebuildSearchTextAsync(SqliteConnection conn, long itemId, CancellationToken ct)
    {
        string? title = null, description = null, notes = null;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT title, description, notes FROM items WHERE id = @id;";
            read.Parameters.AddWithValue("@id", itemId);
            await using var r = await read.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return;   // 条目已删，无需重建
            title = r.IsDBNull(0) ? null : r.GetString(0);
            description = r.IsDBNull(1) ? null : r.GetString(1);
            notes = r.IsDBNull(2) ? null : r.GetString(2);
        }

        var tags = new List<string>();
        using (var readTags = conn.CreateCommand())
        {
            readTags.CommandText = @"
                SELECT t.name FROM item_tags it JOIN tags t ON t.id = it.tag_id
                WHERE it.item_id = @id;";
            readTags.Parameters.AddWithValue("@id", itemId);
            await using var r = await readTags.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) tags.Add(r.GetString(0));
        }

        var raw = new StringBuilder();
        raw.Append(title).Append(' ');
        if (!string.IsNullOrEmpty(description)) raw.Append(description).Append(' ');
        if (!string.IsNullOrEmpty(notes)) raw.Append(notes).Append(' ');
        if (tags.Count > 0) raw.Append(string.Join(' ', tags));
        var expanded = StarMark.Abstractions.Text.CjkTokenizer.ExpandForIndex(raw.ToString());

        using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE items SET search_text = @s WHERE id = @id;";
        upd.Parameters.AddWithValue("@s", expanded);
        upd.Parameters.AddWithValue("@id", itemId);
        await upd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPinnedAsync(long itemId, bool pinned, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET pinned = @pinned WHERE id = @id";
        cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", itemId);
        await cmd.ExecuteNonQueryAsync(ct);
        DataChangeHub.Notify();   // 置顶条目组件、快捷启动格都靠这条实时跟上
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
        cmd.CommandText = @"
            SELECT DISTINCT trim(json_extract(i.extra_json, '$.Language')) AS lang
            FROM items i
            WHERE i.type = 'githubstar'
              AND json_extract(i.extra_json, '$.Language') IS NOT NULL
              AND trim(json_extract(i.extra_json, '$.Language')) <> ''
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

    // ===== 浏览模式 =====

    public async Task<IReadOnlyList<Item>> GetAllAsync(BrowseFilter filter, CancellationToken ct)
    {
        using var conn = _factory.Open();
        var where = new List<string> { "(@include_hidden = 1 OR i.hidden = 0)" };
        if (!string.IsNullOrEmpty(filter.TypeFilter))
            where.Add("i.type = @type_filter");
        if (!string.IsNullOrEmpty(filter.Language))
            where.Add("json_extract(i.extra_json, '$.Language') = @lang");

        // 标签过滤：AND 语义（必须同时具备全部标签）。
        // 原实现用 `JOIN + t.name IN (...) + GROUP BY` 实际是 OR —— 多选标签时结果反而变多，
        // 与「多选收窄」的预期相反，已改为与搜索一致的 EXISTS 逐条判定。

        var orderBy = filter.Sort switch
        {
            "stars" => "i.pinned DESC, i.stars_count DESC NULLS LAST",
            "name" => "i.pinned DESC, i.title COLLATE NOCASE ASC",
            "recent" or _ => "i.pinned DESC, i.updated_at DESC",
        };

        var sql = $@"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t2.name, char(31)) FROM item_tags it2
                    JOIN tags t2 ON t2.id = it2.tag_id
                    WHERE it2.item_id = i.id) AS tag_names
            FROM items i
            WHERE {string.Join(" AND ", where)}{BuildTagClause(filter.TagFilters, "i")}
            GROUP BY i.id
            ORDER BY {orderBy}
            LIMIT @limit;";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@include_hidden", filter.IncludeHidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@limit", filter.Limit);
        if (!string.IsNullOrEmpty(filter.TypeFilter))
            cmd.Parameters.AddWithValue("@type_filter", filter.TypeFilter);
        if (!string.IsNullOrEmpty(filter.Language))
            cmd.Parameters.AddWithValue("@lang", filter.Language);
        BindTagParams(cmd, filter.TagFilters);

        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(MapItem(reader));
        return items;
    }

    public async Task<IReadOnlyList<Item>> GetHiddenAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i WHERE i.hidden = 1 ORDER BY i.updated_at DESC;";
        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(MapItem(reader));
        return items;
    }

    public async Task SetHiddenAsync(long itemId, bool hidden, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET hidden = @hidden WHERE id = @id";
        cmd.Parameters.AddWithValue("@hidden", hidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", itemId);
        await cmd.ExecuteNonQueryAsync(ct);
        DataChangeHub.Notify();
    }

    public async Task<IReadOnlyList<Item>> GetRecentAsync(int limit, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i WHERE i.hidden = 0
            ORDER BY i.updated_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);
        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(MapItem(reader));
        return items;
    }

    public async Task<IReadOnlyList<Item>> GetPinnedAsync(int limit, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i WHERE i.hidden = 0 AND i.pinned = 1
            ORDER BY i.updated_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);
        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(MapItem(reader));
        return items;
    }

    // ===== 本地条目（待办/随记，统一 items 表）=====

    public async Task<IReadOnlyList<Item>> GetBySourceAsync(string source, ItemType? type = null, int limit = 1000, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        var typeClause = type.HasValue ? " AND i.type = @type" : "";
        cmd.CommandText = $@"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i
            WHERE i.source = @source AND i.hidden = 0{typeClause}
            ORDER BY i.updated_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@limit", limit);
        if (type.HasValue) cmd.Parameters.AddWithValue("@type", type.Value.ToString().ToLowerInvariant());
        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(MapItem(reader));
        return items;
    }

    public async Task DeleteBySourceIdAsync(string source, string sourceId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE source = @source AND source_id = @sid;";
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@sid", sourceId);
        await cmd.ExecuteNonQueryAsync(ct);
        DataChangeHub.Notify();
    }

    /// <summary>
    /// 写入本地条目（待办/随记）。与 <see cref="UpsertAsync"/> 不同：
    /// 不写活动流、不跑 <c>UriNormalizer</c>（source_id 是自定义编码）、不跑 <c>LanguageDetector</c>，
    /// 以免污染本地内容的 source_id 与完成态。
    /// </summary>
    public async Task UpsertLocalItemAsync(Item item, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // search_text 口径与 UpsertOne / ReplaceLocalItemsForInstanceAsync 完全一致（title + description +
        // notes + 标签），否则每次待办/随记 re-save 都会把笔记、描述、标签词从 FTS 索引里抹掉，
        // 表现为「明明写了却搜不到」。调用方（Todo/QuickNote VM）加载时已带 Tags，故此处直接用。
        var raw = new StringBuilder();
        raw.Append(item.Title).Append(' ');
        if (!string.IsNullOrEmpty(item.Description)) raw.Append(item.Description).Append(' ');
        if (!string.IsNullOrEmpty(item.Notes)) raw.Append(item.Notes).Append(' ');
        if (item.Tags is { Count: > 0 }) raw.Append(string.Join(' ', item.Tags));
        var searchText = StarMark.Abstractions.Text.CjkTokenizer.ExpandForIndex(raw.ToString());
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
                updated_at = excluded.updated_at,
                extra_json = excluded.extra_json,
                hidden = excluded.hidden,
                notes = excluded.notes
            RETURNING id;";
        cmd.Parameters.AddWithValue("@type", item.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@source", item.Source);
        cmd.Parameters.AddWithValue("@source_id", item.SourceId);
        cmd.Parameters.AddWithValue("@title", item.Title);
        cmd.Parameters.AddWithValue("@subtitle", item.Subtitle);
        cmd.Parameters.AddWithValue("@uri", item.Uri);
        cmd.Parameters.AddWithValue("@search_text", searchText);
        cmd.Parameters.AddWithValue("@description", (object?)item.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stars_count", DBNull.Value);
        cmd.Parameters.AddWithValue("@file_size", DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", item.CreatedAt == 0 ? now : item.CreatedAt);
        cmd.Parameters.AddWithValue("@updated_at", item.UpdatedAt == 0 ? now : item.UpdatedAt);
        cmd.Parameters.AddWithValue("@synced_at", DBNull.Value);
        cmd.Parameters.AddWithValue("@extra_json", (object?)item.ExtraJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hidden", item.Hidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@notes", (object?)item.Notes ?? DBNull.Value);
        var idObj = await cmd.ExecuteScalarAsync(ct);
        if (idObj is long newId) item.Id = newId;
        DataChangeHub.Notify();   // 待办/随记写入后，同类型的其它组件实例也要同步
    }

    // ===== 快照忠实捕获 / 还原（#53 V1）：按实例前缀读写，保全标签/置顶/隐藏/笔记等用户状态 =====

    public async Task<IReadOnlyList<Item>> GetLocalItemsForInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        var result = new List<Item>();
        if (string.IsNullOrEmpty(instanceId)) return result;
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // source_id 形如 "instanceId|localId"，用 LIKE 前缀精确圈定本实例。
        // instanceId 为十六进制 GUID，不含 LIKE 通配符（% _），无需转义；'|' 作为边界避免 "12" 命中 "123|…"。
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i
            WHERE i.source = @source AND i.source_id LIKE @prefix
            ORDER BY i.created_at ASC;";
        cmd.Parameters.AddWithValue("@source", ItemSources.Local);
        cmd.Parameters.AddWithValue("@prefix", instanceId + "|%");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(MapItem(reader));
        return result;
    }

    public async Task ReplaceLocalItemsForInstanceAsync(string instanceId, IReadOnlyList<Item> items, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(instanceId)) return;
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        // 1) 前缀删除本实例既有本地条目（连带 item_tags 级联）。只动本实例，其它实例不受影响。
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM items WHERE source = @source AND source_id LIKE @prefix;";
            del.Parameters.AddWithValue("@source", ItemSources.Local);
            del.Parameters.AddWithValue("@prefix", instanceId + "|%");
            await del.ExecuteNonQueryAsync(ct);
        }

        // 2) 逐条插入（含 pinned/hidden/notes/subtitle/uri/description）→ 回填新 id → 按名重挂标签。
        foreach (var item in items)
        {
            long newId;
            using (var ins = conn.CreateCommand())
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                // search_text 直接在此拼全（title+description+notes+tags 并 CJK 展开），
                // 与 UpsertOne / RebuildSearchTextAsync 的口径一致，故插入后无需再逐行重建。
                var raw = new StringBuilder();
                raw.Append(item.Title).Append(' ');
                if (!string.IsNullOrEmpty(item.Description)) raw.Append(item.Description).Append(' ');
                if (!string.IsNullOrEmpty(item.Notes)) raw.Append(item.Notes).Append(' ');
                if (item.Tags is { Count: > 0 }) raw.Append(string.Join(' ', item.Tags));
                var searchText = StarMark.Abstractions.Text.CjkTokenizer.ExpandForIndex(raw.ToString());

                ins.CommandText = @"
                    INSERT INTO items (type, source, source_id, title, subtitle, uri,
                                      search_text, description, stars_count, file_size,
                                      created_at, updated_at, synced_at, extra_json, hidden, pinned, notes)
                    VALUES (@type, @source, @source_id, @title, @subtitle, @uri,
                            @search_text, @description, NULL, NULL,
                            @created_at, @updated_at, NULL, @extra_json, @hidden, @pinned, @notes)
                    RETURNING id;";
                ins.Parameters.AddWithValue("@type", item.Type.ToString().ToLowerInvariant());
                ins.Parameters.AddWithValue("@source", ItemSources.Local);
                ins.Parameters.AddWithValue("@source_id", item.SourceId);
                ins.Parameters.AddWithValue("@title", item.Title ?? string.Empty);
                ins.Parameters.AddWithValue("@subtitle", item.Subtitle ?? string.Empty);
                ins.Parameters.AddWithValue("@uri", item.Uri ?? string.Empty);
                ins.Parameters.AddWithValue("@search_text", searchText);
                ins.Parameters.AddWithValue("@description", (object?)item.Description ?? DBNull.Value);
                ins.Parameters.AddWithValue("@created_at", item.CreatedAt == 0 ? now : item.CreatedAt);
                ins.Parameters.AddWithValue("@updated_at", item.UpdatedAt == 0 ? now : item.UpdatedAt);
                ins.Parameters.AddWithValue("@extra_json", (object?)item.ExtraJson ?? DBNull.Value);
                ins.Parameters.AddWithValue("@hidden", item.Hidden ? 1 : 0);
                ins.Parameters.AddWithValue("@pinned", item.Pinned ? 1 : 0);
                ins.Parameters.AddWithValue("@notes", (object?)item.Notes ?? DBNull.Value);
                newId = (long)(await ins.ExecuteScalarAsync(ct))!;
            }

            foreach (var tagName in item.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tagName))
                    await LinkTagByNameAsync(conn, newId, tagName.Trim(), ct);
            }
            item.Id = newId;
        }

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();
    }

    /// <summary>取或建标签并按 (item_id, tag_id) 幂等关联。在调用方的事务连接内执行（自动 enlist）。</summary>
    private static async Task LinkTagByNameAsync(Microsoft.Data.Sqlite.SqliteConnection conn, long itemId, string tagName, CancellationToken ct)
    {
        long tagId;
        using (var find = conn.CreateCommand())
        {
            find.CommandText = "SELECT id FROM tags WHERE name = @name COLLATE NOCASE";
            find.Parameters.AddWithValue("@name", tagName);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is long id)
            {
                tagId = id;
            }
            else
            {
                using var create = conn.CreateCommand();
                create.CommandText = "INSERT INTO tags(name, created_at) VALUES(@name, @now); SELECT last_insert_rowid();";
                create.Parameters.AddWithValue("@name", tagName);
                create.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                tagId = (long)(await create.ExecuteScalarAsync(ct))!;
            }
        }
        using var link = conn.CreateCommand();
        link.CommandText = "INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at) VALUES(@item, @tag, @now)";
        link.Parameters.AddWithValue("@item", itemId);
        link.Parameters.AddWithValue("@tag", tagId);
        link.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await link.ExecuteNonQueryAsync(ct);
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
            foreach (var tagName in item.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await UpsertTagLink(conn, item.Id, tagName, ct);
            }
        }

        // 活动流（#51 收束）：本方法（UpsertAsync/UpsertOne）只被后台来源同步、种子与备份还原调用，
        // 一律**不**记活动事件——「最近活动」要反映的是用户主动的增/删/改，而不是后台批量写入刷屏。
        // 用户主动增删改由 UI 交互路径显式调用 LogActivityAsync（ItemAdd/ItemModify/ItemDelete）落库。
    }

    // ===== 活动流（扩展对比方案 P1-3）=====

    /// <summary>写入一条活动事件。插入后裁剪环形缓冲，仅保留最近 500 条（>500 删最旧）。</summary>
    public async Task LogActivityAsync(ActivityKind kind, string? itemKey, string title, string? uri, CancellationToken ct)
    {
        using var conn = _factory.Open();
        await LogActivityOnConnection(conn, kind, itemKey, title, uri, ct);
        DataChangeHub.Notify();   // 活动事件本身也是「数据」：让常驻的最近活动格即时跟上（与业务写各自的 Notify 叠加，重载有去抖）
    }

    /// <summary>在给定连接上写入活动事件（供 <see cref="UpsertOne"/> 在已有事务内复用连接）。</summary>
    private static async Task LogActivityOnConnection(SqliteConnection conn, ActivityKind kind, string? itemKey, string title, string? uri, CancellationToken ct)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO activity(at, kind, item_key, title, uri)
                VALUES(@at, @kind, @item_key, @title, @uri);";
            cmd.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("@kind", kind.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@item_key", (object?)itemKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", title);
            cmd.Parameters.AddWithValue("@uri", (object?)uri ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 环形缓冲：仅保留最近 500 条
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) - 500 FROM activity;";
            var overflow = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
            if (overflow > 0)
            {
                using var prune = conn.CreateCommand();
                prune.CommandText = @"
                    DELETE FROM activity
                    WHERE id IN (
                        SELECT id FROM activity ORDER BY at ASC, id ASC LIMIT @n
                    );";
                prune.Parameters.AddWithValue("@n", overflow);
                await prune.ExecuteNonQueryAsync(ct);
            }
        }
    }

    /// <summary>读取最近的活动事件，按时间倒序。</summary>
    public async Task<IReadOnlyList<ActivityRecord>> GetActivityAsync(int limit, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, at, kind, item_key, title, uri
            FROM activity
            ORDER BY at DESC, id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<ActivityRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ActivityRecord
            {
                Id = reader.GetInt64(0),
                At = reader.GetInt64(1),
                Kind = Enum.TryParse<ActivityKind>(reader.GetString(2), ignoreCase: true, out var k)
                    ? k : ActivityKind.ItemDelete,
                ItemKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                Title = reader.GetString(4),
                Uri = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return list;
    }

    // ===== 同步状态（扩展对比方案 P1-4）=====

    /// <summary>读取 sync_state 键值；不存在返回 null。</summary>
    public async Task<string?> GetSyncStateAsync(string key, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj == null || obj == DBNull.Value ? null : (string?)obj;
    }

    /// <summary>幂等写入 sync_state 键值（用于 ETag / last_synced_at 等检查点）。</summary>
    public async Task SetSyncStateAsync(string key, string value, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state(key, value) VALUES(@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertTagLink(SqliteConnection conn, long itemId, string tagName, CancellationToken ct)
    {
        long tagId;
        using (var findTag = conn.CreateCommand())
        {
            findTag.CommandText = "SELECT id FROM tags WHERE name = @name COLLATE NOCASE";
            findTag.Parameters.AddWithValue("@name", tagName);
            var existing = await findTag.ExecuteScalarAsync(ct);
            if (existing is long id)
            {
                tagId = id;
            }
            else
            {
                using var createTag = conn.CreateCommand();
                createTag.CommandText = "INSERT INTO tags(name, created_at) VALUES(@name, @now); SELECT last_insert_rowid();";
                createTag.Parameters.AddWithValue("@name", tagName);
                createTag.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                tagId = (long)(await createTag.ExecuteScalarAsync(ct))!;
            }
        }

        using var link = conn.CreateCommand();
        link.CommandText = "INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at) VALUES(@item, @tag, @now)";
        link.Parameters.AddWithValue("@item", itemId);
        link.Parameters.AddWithValue("@tag", tagId);
        link.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await link.ExecuteNonQueryAsync(ct);
    }

    private static Item MapItem(SqliteDataReader reader)
    {
        var item = new Item
        {
            Id = reader.GetInt64(0),
            Type = Enum.Parse<ItemType>(reader.GetString(1), ignoreCase: true),
            Source = reader.GetString(2),
            SourceId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Title = reader.GetString(4),
            Subtitle = reader.GetString(5),
            Uri = reader.GetString(6),
            Description = reader.IsDBNull(7) ? null : reader.GetString(7),
            StarsCount = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            FileSize = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            CreatedAt = reader.GetInt64(10),
            UpdatedAt = reader.GetInt64(11),
            SyncedAt = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            ExtraJson = reader.IsDBNull(13) ? null : reader.GetString(13),
            Hidden = reader.GetInt32(14) != 0,
            // SELECT 列序：14 = hidden，15 = pinned，16 = notes，17 = tag_names
            Pinned = reader.FieldCount > 15 && !reader.IsDBNull(15) && reader.GetInt64(15) != 0,
            Notes = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : null,
        };

        // 标签：char(31)（单元分隔符，正常标签名不含）分隔的字符串 → List<string>
        if (reader.FieldCount > 17 && !reader.IsDBNull(17))
        {
            var tagStr = reader.GetString(17);
            if (!string.IsNullOrEmpty(tagStr))
            {
                item.Tags = tagStr.Split(new[] { (char)31 }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }
        return item;
    }

    /// <summary>
    /// 构建 FTS5 查询表达式。空格分隔的多个词 → AND 匹配。
    /// 词中含非字母数字 → 加引号作为短语。
    /// </summary>
    private static string BuildFtsQuery(string keyword)
    {
        var tokens = StarMark.Abstractions.Text.CjkTokenizer.SplitForQuery(keyword);
        if (tokens.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            if (sb.Length > 0) sb.Append(' ');   // 空格 = FTS5 隐式 AND

            if (StarMark.Abstractions.Text.CjkTokenizer.ContainsCjk(t))
            {
                // CJK 词元已是完整的二元组，加 '*' 会造成过度匹配；且不含 FTS5 特殊字符
                sb.Append(t);
            }
            else if (t.Any(c => !char.IsLetterOrDigit(c)))
            {
                // 简单转义：含特殊字符的词加引号
                sb.Append('"').Append(t.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                sb.Append(t).Append('*');  // 前缀匹配
            }
        }
        return sb.ToString();
    }
}
