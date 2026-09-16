#nullable enable
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;

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
                   (SELECT GROUP_CONCAT(t.name, ',') FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM fts_hits f
            JOIN items i ON i.id = f.rowid
            WHERE (@type_filter IS NULL OR i.type = @type_filter)
              AND (@stars_min IS NULL OR i.stars_count >= @stars_min)
              AND (@date_from IS NULL OR i.updated_at >= @date_from)
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
                   (SELECT GROUP_CONCAT(t.name, ',') FROM item_tags it
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
    }

    // ===== 标签 =====

    public async Task<IReadOnlyList<(string Name, int Count)>> GetAllTagsAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, COUNT(it.item_id) AS cnt
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

        await tx.CommitAsync(ct);
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
        // 更新 notes 字段；同步重建 search_text（笔记 + 标签都参与全文搜索）
        cmd.CommandText = @"
            UPDATE items
            SET notes = @content,
                search_text = title || ' ' || COALESCE(description, '') || ' ' || @content || ' ' ||
                    COALESCE((SELECT GROUP_CONCAT(t2.name, ' ') FROM item_tags it2
                              JOIN tags t2 ON t2.id = it2.tag_id
                              WHERE it2.item_id = items.id), '')
            WHERE id = @id;";
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@id", itemId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPinnedAsync(long itemId, bool pinned, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET pinned = @pinned WHERE id = @id";
        cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", itemId);
        await cmd.ExecuteNonQueryAsync(ct);
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

    // ===== 浏览模式 =====

    public async Task<IReadOnlyList<Item>> GetAllAsync(BrowseFilter filter, CancellationToken ct)
    {
        using var conn = _factory.Open();
        var where = new List<string> { "(@include_hidden = 1 OR i.hidden = 0)" };
        if (!string.IsNullOrEmpty(filter.TypeFilter))
            where.Add("i.type = @type_filter");

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
                   (SELECT GROUP_CONCAT(t2.name, ',') FROM item_tags it2
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
                   (SELECT GROUP_CONCAT(t.name, ',') FROM item_tags it
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
    }

    public async Task<IReadOnlyList<Item>> GetRecentAsync(int limit, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, ',') FROM item_tags it
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
                   (SELECT GROUP_CONCAT(t.name, ',') FROM item_tags it
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
    // ===== 内部辅助 =====

    private static async Task UpsertOne(SqliteConnection conn, Item item, CancellationToken ct)
    {
        // 用户状态（hidden / pinned / notes）是本地编辑，源侧同步不应覆盖。
        // 先读旧值合并进 item：既保住状态，又让 search_text 计算包含用户笔记。
        using (var existing = conn.CreateCommand())
        {
            existing.CommandText = @"
                SELECT hidden, notes, pinned FROM items
                WHERE source = @src AND source_id = @sid LIMIT 1;";
            existing.Parameters.AddWithValue("@src", item.Source);
            existing.Parameters.AddWithValue("@sid", (object?)item.SourceId ?? DBNull.Value);
            await using var reader = await existing.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                item.Hidden = reader.GetInt64(0) != 0;
                item.Notes = reader.IsDBNull(1) ? null : reader.GetString(1);
                item.Pinned = !reader.IsDBNull(2) && reader.GetInt64(2) != 0;
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

        // 标签：逗号分隔的字符串 → List<string>
        if (reader.FieldCount > 17 && !reader.IsDBNull(17))
        {
            var tagStr = reader.GetString(17);
            if (!string.IsNullOrEmpty(tagStr))
            {
                item.Tags = tagStr.Split(',').ToList();
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
