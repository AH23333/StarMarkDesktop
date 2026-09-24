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
            "starred" => "COALESCE(CAST(json_extract(i.extra_json, '$.StarredAt') AS INTEGER), i.updated_at) DESC, MIN(f.rank)",
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
        var filteredWhere = @"
            WHERE items_fts MATCH @keyword
              AND (@type_filter IS NULL OR i.type = @type_filter)
              AND (@stars_min IS NULL OR i.stars_count >= @stars_min)
              AND (@date_from IS NULL OR i.updated_at >= @date_from)
              AND (@lang IS NULL OR i.type = 'file' OR json_extract(i.extra_json, '$.Language') = @lang)
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
        // 用 INNER JOIN 驱动标签集合：只列出仍有 item_tags 关联的标签。全库没有 DELETE FROM tags，
        // RemoveTagAsync/删条目/备份 Replace 都只清 item_tags，tags 行会残留成孤儿；用 LEFT JOIN 会让
        // 这些零引用孤儿以「(标签, 0)」幽灵形式永久挂在列表里（含备份还原后未重新挂接的历史标签）。
        // 注意：COUNT(i.id) 仍只计非隐藏条目（W-A 口径），故「条目全被隐藏」的标签因 item_tags 仍在而保留，
        // 本改动只剔除「无任何关联」的孤儿标签，不改动隐藏语义。
        cmd.CommandText = @"
            SELECT t.name, COUNT(i.id) AS cnt
            FROM tags t
            JOIN item_tags it ON it.tag_id = t.id
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

    /// <summary>
    /// 一次事务给一批条目追加标签。<b>存在的理由是把往返压下来</b>：逐条 <see cref="AddTagAsync"/>
    /// 时"500 条各加 2 个标签"要开 1000 次连接、起 1000 个事务、把同一条目的 search_text 重建 2 遍、
    /// 通知 1000 次；这里压成 1 次连接、每条目最多重建 1 次、整批通知 1 次。
    /// <para>
    /// <b>语义与逐条调用严格一致</b>：只追加不替换、已关联的跳过、标签名按 NOCASE 复用同一行、
    /// 发生变化的条目各写一条「修改」活动。两点是有意的差别：
    /// ① 全批都没有可加的东西时<b>一条语句都不多发</b>（幂等重放不该在活动流里留下足迹）；
    /// ② 条目在读取后已被删除，则整条跳过——不给一个不存在的条目建关联（外键会拒，但那时已经花了语句）。
    /// </para>
    /// </summary>
    public async Task<int> TagItemsAsync(IReadOnlyList<ItemTagAssignment> assignments, CancellationToken ct)
    {
        if (assignments is null || assignments.Count == 0) return 0;

        // 入参先收口：同一条目重复出现要并起来，否则后面按差集算会漏
        var wanted = new Dictionary<long, List<string>>();
        foreach (var assignment in assignments)
        {
            if (assignment is null || assignment.ItemId <= 0 || assignment.Tags is null) continue;
            if (!wanted.TryGetValue(assignment.ItemId, out var names)) wanted[assignment.ItemId] = names = new List<string>();
            foreach (var raw in assignment.Tags)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var name = raw.Trim();
                if (!names.Any(has => string.Equals(has, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
            }
        }
        if (wanted.Count == 0) return 0;

        var ids = wanted.Keys.ToList();
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        // ① 一次读回这批条目已有的标签（大小写不敏感比对用）
        var existing = new Dictionary<long, HashSet<string>>();
        foreach (var chunk in Chunks(ids))
        {
            using var read = conn.CreateCommand();
            read.Transaction = tx;
            read.CommandText = @$"
                SELECT it.item_id, t.name FROM item_tags it JOIN tags t ON t.id = it.tag_id
                WHERE it.item_id IN ({InParams(chunk, read)});";
            await using var r = await read.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt64(0);
                if (!existing.TryGetValue(id, out var set)) existing[id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(r.GetString(1));
            }
        }

        // ② 算差集
        var additions = new Dictionary<long, List<string>>();
        foreach (var (id, names) in wanted)
        {
            var has = existing.TryGetValue(id, out var set) ? set : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fresh = names.Where(name => !has.Contains(name)).ToList();
            if (fresh.Count > 0) additions[id] = fresh;
        }
        if (additions.Count == 0) return 0;      // 没有任何可加的：直接结束，不写不通知

        // ③ 条目现在还在不在（批量整理跑几分钟，期间用户删掉某条是完全正常的）
        var rows = new Dictionary<long, (string Title, string? Uri, string Source, string SourceId, string? Description, string? Notes)>();
        foreach (var chunk in Chunks(additions.Keys.ToList()))
        {
            using var read = conn.CreateCommand();
            read.Transaction = tx;
            read.CommandText = @$"
                SELECT id, title, uri, source, source_id, description, notes FROM items
                WHERE id IN ({InParams(chunk, read)});";
            await using var r = await read.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows[r.GetInt64(0)] = (
                    r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? string.Empty : r.GetString(3),
                    r.IsDBNull(4) ? string.Empty : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetString(6));
        }

        var live = additions.Where(pair => rows.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (live.Count == 0) return 0;

        // ④ 标签行：已知的名字一次查回来，缺的才建（RETURNING 省掉一次 last_insert_rowid）
        var neededNames = live.Values.SelectMany(names => names).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tagIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in Chunks(neededNames))
        {
            using var find = conn.CreateCommand();
            find.Transaction = tx;
            find.CommandText = $"SELECT id, name FROM tags WHERE name COLLATE NOCASE IN ({InParams(chunk, find, "tn")});";
            await using var r = await find.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) tagIds[r.GetString(1)] = r.GetInt64(0);
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var name in neededNames)
        {
            if (tagIds.ContainsKey(name)) continue;
            using var create = conn.CreateCommand();
            create.Transaction = tx;
            create.CommandText = "INSERT INTO tags(name, created_at) VALUES(@name, @now) RETURNING id;";
            create.Parameters.AddWithValue("@name", name);
            create.Parameters.AddWithValue("@now", now);
            tagIds[name] = (long)(await create.ExecuteScalarAsync(ct))!;
        }

        // ⑤ 建关联：每个 (条目, 新标签) 一条 INSERT OR IGNORE（幂等，靠的是唯一索引）
        foreach (var (id, names) in live)
        {
            foreach (var name in names)
            {
                using var link = conn.CreateCommand();
                link.Transaction = tx;
                link.CommandText = "INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at) VALUES(@item, @tag, @now);";
                link.Parameters.AddWithValue("@item", id);
                link.Parameters.AddWithValue("@tag", tagIds[name]);
                link.Parameters.AddWithValue("@now", now);
                await link.ExecuteNonQueryAsync(ct);
            }
        }

        // ⑥ 索引重建：每个变动的条目一次（标签并进 search_text 是全文检索的前提，见 EP/AM 两批的教训）。
        //    已有的 + 刚加的合起来就是最终状态，不必再回库里读一遍。
        foreach (var (id, names) in live)
        {
            var finalTags = new List<string>(existing.TryGetValue(id, out var has) ? has : Enumerable.Empty<string>());
            finalTags.AddRange(names);
            var row = rows[id];
            await WriteSearchTextAsync(conn, id, row.Title, row.Description, row.Notes, finalTags, ct);
        }

        // ⑦ 活动流水：整批一次多行 INSERT + 一次裁剪（逐条写的话光活动就两倍语句数）
        var activityRows = live.Keys.OrderBy(id => id)
            .Select(id => (Kind: ActivityKind.ItemModify,
                           Key: rows[id].Source + ":" + rows[id].SourceId,
                           Title: rows[id].Title,
                           Uri: rows[id].Uri))
            .ToList();
        await LogActivitiesOnConnection(conn, tx, activityRows, ct);

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();       // 整批只通知一次：每条都通知会让所有常驻组件各重读一遍库
        return live.Count;
    }

    /// <summary>IN (…) 的参数个数上限。SQLite 默认变量上限是 999（老版本）——留足余量分块，
    /// 免得"条目多了"变成一个看不懂的 SqliteException。</summary>
    private const int InChunkSize = 400;

    private static IEnumerable<List<T>> Chunks<T>(IReadOnlyList<T> items)
    {
        for (var i = 0; i < items.Count; i += InChunkSize)
            yield return items.Skip(i).Take(InChunkSize).ToList();
    }

    /// <summary>给命令挂上 @<paramref name="prefix"/>0…n 并返回 SQL 里的占位串。<b>只用参数化</b>：
    /// 拼字符串的那条路一旦被复用就是注入面。</summary>
    private static string InParams<T>(List<T> values, SqliteCommand command, string prefix = "in")
    {
        var names = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var name = "@" + prefix + i;
            command.Parameters.AddWithValue(name, values[i]!);
            names.Add(name);
        }
        return string.Join(",", names);
    }

    /// <summary>批量写活动流水。<b>与 <see cref="LogActivityOnConnection"/> 同一套环形缓冲口径</b>（只留最近 500 条），
    /// 区别只在于一次多行 INSERT、最后裁剪一次，而不是每条都数一遍总数。</summary>
    private static async Task LogActivitiesOnConnection(
        SqliteConnection conn, SqliteTransaction tx,
        IReadOnlyList<(ActivityKind Kind, string Key, string Title, string? Uri)> rows, CancellationToken ct)
    {
        for (var start = 0; start < rows.Count; start += InChunkSize)
        {
            var slice = rows.Skip(start).Take(InChunkSize).ToList();
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            var valueGroups = new List<string>(slice.Count);
            for (var i = 0; i < slice.Count; i++)
            {
                valueGroups.Add($"(@at{i}, @kind{i}, @key{i}, @title{i}, @uri{i})");
                insert.Parameters.AddWithValue($"@at{i}", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                insert.Parameters.AddWithValue($"@kind{i}", slice[i].Kind.ToString().ToLowerInvariant());
                insert.Parameters.AddWithValue($"@key{i}", slice[i].Key);
                insert.Parameters.AddWithValue($"@title{i}", slice[i].Title);
                insert.Parameters.AddWithValue($"@uri{i}", (object?)slice[i].Uri ?? DBNull.Value);
            }
            insert.CommandText = "INSERT INTO activity(at, kind, item_key, title, uri) VALUES " + string.Join(",", valueGroups) + ";";
            await insert.ExecuteNonQueryAsync(ct);
        }

        using var countCmd = conn.CreateCommand();
        countCmd.Transaction = tx;
        countCmd.CommandText = "SELECT COUNT(*) - 500 FROM activity;";
        var overflow = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
        if (overflow <= 0) return;
        using var prune = conn.CreateCommand();
        prune.Transaction = tx;
        prune.CommandText = "DELETE FROM activity WHERE id IN (SELECT id FROM activity ORDER BY at ASC, id ASC LIMIT @n);";
        prune.Parameters.AddWithValue("@n", overflow);
        await prune.ExecuteNonQueryAsync(ct);
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

    /// <summary>
    /// 标签编辑器的唯一写入出口：读现状 → 算差集 → 一次事务里删/建/关联 → 重建索引一次 → 记一笔活动 → 通知一次。
    /// <para>
    /// <b>search_text 只在最终状态下重建一次</b>是这里比逐条调用更重要的差别：
    /// 逐条调用时中间每次重建用的都是半成品标签集，最后一次才是对的——
    /// 中途被查询命中就会拿到一个"少了刚加的标签"的索引。
    /// </para>
    /// </summary>
    public async Task<TagEditResult> SetItemTagsAsync(long itemId, IReadOnlyList<string> desired, CancellationToken ct = default)
    {
        if (itemId <= 0) return TagEditResult.Unchanged;

        // 目标集合先收口（去空白、去重、大小写不敏感）：编辑器那边已经做一次，这里再做一次是因为
        // 存档/撤销/批量路径都可能把同一个名字给两遍，差集算错会写出重复关联。
        var target = new List<string>();
        if (desired is not null)
        {
            foreach (var raw in desired)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var name = raw.Trim();
                if (!target.Any(has => string.Equals(has, name, StringComparison.OrdinalIgnoreCase))) target.Add(name);
            }
        }

        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        var row = await ReadItemShellAsync(conn, tx, itemId, ct);
        if (row is null) return TagEditResult.Unchanged;      // 条目已经不在了：编辑动作没有落点

        var current = await ReadTagNamesAsync(conn, tx, itemId, ct);
        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var targetSet = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);

        var removed = current.Where(name => !targetSet.Contains(name)).ToList();
        var added = target.Where(name => !currentSet.Contains(name)).ToList();
        if (removed.Count == 0 && added.Count == 0)
            return new TagEditResult(false, 0, 0, current);   // 原样保存：不发写语句、不记活动

        if (removed.Count > 0) await DetachTagsAsync(conn, tx, itemId, removed, ct);
        if (added.Count > 0) await AttachTagsAsync(conn, tx, itemId, added, ct);

        var finalTags = targetSet.Count == 0 ? new List<string>() : target;
        await WriteSearchTextAsync(conn, itemId, row.Value.Title, row.Value.Description, row.Value.Notes, finalTags, ct);
        await LogActivityOnConnection(conn, ActivityKind.ItemModify,
            row.Value.Source + ":" + row.Value.SourceId, row.Value.Title, row.Value.Uri, ct);

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();
        return new TagEditResult(true, added.Count, removed.Count, finalTags);
    }

    /// <summary>条目上那几个"重建索引/记活动"要用的字段，一次读回。</summary>
    private static async Task<(string Title, string? Description, string? Notes, string? Uri, string Source, string SourceId)?>
        ReadItemShellAsync(SqliteConnection conn, SqliteTransaction tx, long itemId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT title, description, notes, uri, source, source_id FROM items WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", itemId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5));
    }

    private static async Task<List<string>> ReadTagNamesAsync(
        SqliteConnection conn, SqliteTransaction tx, long itemId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT t.name FROM item_tags it JOIN tags t ON t.id = it.tag_id
            WHERE it.item_id = @id ORDER BY t.name;";
        cmd.Parameters.AddWithValue("@id", itemId);
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names;
    }

    /// <summary>解绑一批标签。<b>只删关联、不删 tags 行</b>：与 <see cref="RemoveTagAsync"/> 同口径
    /// （全库没有 DELETE FROM tags，孤儿由 GetAllTagsAsync 的 INNER JOIN 挡掉）。</summary>
    private static async Task DetachTagsAsync(
        SqliteConnection conn, SqliteTransaction tx, long itemId, IReadOnlyList<string> names, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"
            DELETE FROM item_tags
            WHERE item_id = @item
              AND tag_id IN (SELECT id FROM tags WHERE name COLLATE NOCASE IN ({InParams(names.ToList(), cmd, "dn")}));";
        cmd.Parameters.AddWithValue("@item", itemId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>挂上一批标签：名字已存在的复用同一行（NOCASE），缺的才建。</summary>
    private static async Task AttachTagsAsync(
        SqliteConnection conn, SqliteTransaction tx, long itemId, IReadOnlyList<string> names, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var name in names)
        {
            long tagId;
            using (var find = conn.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = "SELECT id FROM tags WHERE name = @name COLLATE NOCASE;";
                find.Parameters.AddWithValue("@name", name);
                var existing = await find.ExecuteScalarAsync(ct);
                if (existing is long known) tagId = known;
                else
                {
                    using var create = conn.CreateCommand();
                    create.Transaction = tx;
                    create.CommandText = "INSERT INTO tags(name, created_at) VALUES(@name, @now) RETURNING id;";
                    create.Parameters.AddWithValue("@name", name);
                    create.Parameters.AddWithValue("@now", now);
                    tagId = (long)(await create.ExecuteScalarAsync(ct))!;
                }
            }

            using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at) VALUES(@item, @tag, @now);";
            link.Parameters.AddWithValue("@item", itemId);
            link.Parameters.AddWithValue("@tag", tagId);
            link.Parameters.AddWithValue("@now", now);
            await link.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// 拖动排序的写入出口：读现状 → 只改 order → 只写变化的行。
    /// <para><b>为什么不用 <see cref="UpsertLocalItemAsync"/> 逐条写</b>：那条路径按整行覆盖并重建全文索引，
    /// 而顺序既不在索引里也不该影响别的列——一次 20 条的拖动会变成 20 次开库 + 20 次无谓的索引重建，
    /// 还把"整行覆盖"的风险引进一个只该改一个数字的动作里。</para>
    /// </summary>
    public async Task<int> ReorderLocalItemsAsync(IReadOnlyList<long> orderedIds, CancellationToken ct = default)
    {
        if (orderedIds is null || orderedIds.Count == 0) return 0;

        var ids = orderedIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return 0;

        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        var current = new Dictionary<long, string?>();
        foreach (var chunk in Chunks(ids))
        {
            using var read = conn.CreateCommand();
            read.Transaction = tx;
            read.CommandText = $"SELECT id, extra_json FROM items WHERE id IN ({InParams(chunk, read, "ro")});";
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                current[reader.GetInt64(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changed = 0;
        for (var position = 0; position < ids.Count; position++)
        {
            var id = ids[position];
            if (!current.TryGetValue(id, out var extraJson)) continue;     // 已经不在了：跳过，不新建行

            // 顺序的编码只在 LocalItemState 一处定义，这里借它算目标写法（不自己拼 JSON）
            var probe = new Item { Id = id, ExtraJson = extraJson };
            LocalItemState.SetOrder(probe, position);
            if (string.Equals(probe.ExtraJson, extraJson, StringComparison.Ordinal)) continue;

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE items SET extra_json = @extra, updated_at = @at WHERE id = @id;";
            upd.Parameters.AddWithValue("@extra", (object?)probe.ExtraJson ?? DBNull.Value);
            upd.Parameters.AddWithValue("@at", now);
            upd.Parameters.AddWithValue("@id", id);
            await upd.ExecuteNonQueryAsync(ct);
            changed++;
        }

        if (changed == 0) return 0;                                       // 拖回原位：不写、不通知
        await tx.CommitAsync(ct);
        DataChangeHub.Notify();                                           // 整批一次：待办组件与列表各自重读一遍就够
        return changed;
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

        await WriteSearchTextAsync(conn, itemId, title, description, notes, tags, ct);
    }

    /// <summary>
    /// 拼出并写入某条目的 search_text。<b>拆成"算"与"写"两半是给批量路径用的</b>：
    /// 那边一次把整批的标题/描述/标签读回来了，再按条目各读一遍就等于把省下的往返又加回去。
    /// 拼接口径仍是唯一一份（title + description + notes + tags，再过 CJK 展开）。
    /// </summary>
    private static async Task WriteSearchTextAsync(
        SqliteConnection conn, long itemId, string? title, string? description, string? notes,
        IReadOnlyList<string> tags, CancellationToken ct)
    {
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
            // 语言下拉的语义是「按编程语言筛选 star」（见 LanguageDetector / FolderTreePage 注释）。
            // 但 LanguageDetector.EnsureLanguage 会给任意来源（含本地文件/书签）按扩展名兜底打 Language，
            // 「所有来源」视图下若不加 type 闸门，一个 .py 本地文件会冒充 Python star 混入结果集。
            where.Add("(i.type = 'githubstar' AND json_extract(i.extra_json, '$.Language') = @lang)");

        // 标签过滤：AND 语义（必须同时具备全部标签）。
        // 原实现用 `JOIN + t.name IN (...) + GROUP BY` 实际是 OR —— 多选标签时结果反而变多，
        // 与「多选收窄」的预期相反，已改为与搜索一致的 EXISTS 逐条判定。

        var orderBy = filter.Sort switch
        {
            "stars" => "i.pinned DESC, i.stars_count DESC NULLS LAST",
            "name" => "i.pinned DESC, i.title COLLATE NOCASE ASC",
            // 最近 Star / 最近收藏：与 SearchAsync 的同名分支口径一致（starredAt 取 extra_json，无则退
            // updated_at；collected 取入库时间）。此前浏览模式漏了这两支，落入 default 变成「最近更新」。
            "starred" => "i.pinned DESC, COALESCE(CAST(json_extract(i.extra_json, '$.StarredAt') AS INTEGER), i.updated_at) DESC",
            "collected" => "i.pinned DESC, i.created_at DESC",
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

    /// <summary>
    /// 一次连接取回"还没有任何标签"的条目。<b>NOT EXISTS 直接下推</b>：
    /// 反过来（先取一批再在 C# 里筛）会带上一个窗口，而窗口会<b>静默少报</b>——
    /// 最近几千条都有标签时，早期没标签的一条都不会被列出，界面却说"没有待整理的条目"。
    /// <para>顺序与浏览页 "recent" 档一致（置顶优先，再按更新时间倒序），
    /// 同一件事在两处排出不同顺序，用户会以为看到的是两份数据。</para>
    /// </summary>
    public async Task<IReadOnlyList<Item>> GetUntaggedAsync(IReadOnlyList<ItemType> types, int limit, CancellationToken ct = default)
    {
        if (types is null || types.Count == 0 || limit <= 0) return Array.Empty<Item>();

        var names = types.Select(type => type.ToString().ToLowerInvariant()).Distinct().ToList();
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();

        var placeholders = new List<string>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var name = "@ty" + i;
            cmd.Parameters.AddWithValue(name, names[i]);
            placeholders.Add(name);
        }
        cmd.Parameters.AddWithValue("@limit", limit);

        // tag_names 直接给 NULL：候选的定义就是"没有标签"，再为每行跑一次关联子查询是白跑
        cmd.CommandText = $@"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   NULL AS tag_names
            FROM items i
            WHERE i.hidden = 0
              AND i.type IN ({string.Join(",", placeholders)})
              AND NOT EXISTS (SELECT 1 FROM item_tags it WHERE it.item_id = i.id)
            ORDER BY i.pinned DESC, i.updated_at DESC
            LIMIT @limit;";

        var items = new List<Item>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(MapItem(reader));
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

    /// <summary>
    /// 记录一条剪贴板历史：幂等 upsert + 复制次数累加 + 用户状态保留 + 按上限轮转。
    /// <para>
    /// 与 <see cref="UpsertLocalItemAsync"/> 同族（都不走 UriNormalizer / LanguageDetector / 活动流）
    /// 但<b>刻意不复用</b>，三处语义不同：
    /// ① 同一段文本再次复制必须"移回最近 + 次数 +1"，而不是原地覆盖；
    /// ② 必须保住用户在这条历史上加的<b>置顶 / 隐藏 / 笔记 / 标签</b>——采集是后台行为，
    ///    反过来吃掉用户的手动状态就是"我用着用着我标星的东西没了"；
    /// ③ 落库后轮转，且<b>置顶条目豁免</b>（用户明确要留的东西不该被"后来又复制了 500 次"挤掉）。
    /// </para>
    /// <para>
    /// 活动流刻意不记：剪贴板是被动、高频事件，写进「最近活动」只会把用户真正的增删改刷没
    /// （与 <c>UpsertOne</c> 对后台批量写入的同一口径）。
    /// </para>
    /// </summary>
    /// <param name="maxEntries">未置顶条目的保留上限，默认 <see cref="ClipboardPolicy.MaxEntries"/>；
    /// 测试与非默认策略可传更小值。小于 1 按 1 处理（至少留下刚写的这条）。</param>
    public async Task<Item> RecordClipboardAsync(Item draft, CancellationToken ct = default, int maxEntries = ClipboardPolicy.MaxEntries)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        // 轮转是按 source 圈定的，写错来源会把别的来源裁掉 ⇒ 直接拒，不做"尽力而为"。
        if (draft.Source != ItemSources.Clipboard)
            throw new ArgumentException($"剪贴板写入只接受 source={ItemSources.Clipboard}，收到「{draft.Source}」", nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.SourceId)) return draft;   // 没键就没法幂等，直接放弃这条

        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        // ① 读旧行：次数要累加、用户状态要原样带回去
        string? existingExtra = null;
        bool hidden = draft.Hidden, pinned = draft.Pinned;
        string? notes = draft.Notes;
        var known = false;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT extra_json, hidden, pinned, notes FROM items WHERE source = @s AND source_id = @sid;";
            read.Parameters.AddWithValue("@s", draft.Source);
            read.Parameters.AddWithValue("@sid", draft.SourceId);
            await using var r = await read.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                known = true;
                existingExtra = r.IsDBNull(0) ? null : r.GetString(0);
                hidden = r.GetInt64(1) != 0;
                pinned = r.GetInt64(2) != 0;
                notes = r.IsDBNull(3) ? null : r.GetString(3);
            }
        }

        var copyCount = known ? ClipboardEntry.CopyCountOf(existingExtra) + 1 : 1;
        var extraJson = known ? ClipboardEntry.MergeForReplay(existingExtra, draft, copyCount) : draft.ExtraJson;

        long id;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO items (type, source, source_id, title, subtitle, uri,
                                  search_text, description, created_at, updated_at,
                                  extra_json, hidden, pinned, notes)
                VALUES (@type, @source, @source_id, @title, @subtitle, @uri,
                        '', @description, @created_at, @updated_at,
                        @extra_json, @hidden, @pinned, @notes)
                ON CONFLICT(source, source_id) DO UPDATE SET
                    title = excluded.title,
                    subtitle = excluded.subtitle,
                    description = excluded.description,
                    updated_at = excluded.updated_at,
                    extra_json = excluded.extra_json
                RETURNING id;";
            cmd.Parameters.AddWithValue("@type", draft.Type.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@source", draft.Source);
            cmd.Parameters.AddWithValue("@source_id", draft.SourceId);
            cmd.Parameters.AddWithValue("@title", draft.Title);
            cmd.Parameters.AddWithValue("@subtitle", draft.Subtitle);
            cmd.Parameters.AddWithValue("@uri", draft.Uri);
            cmd.Parameters.AddWithValue("@description", (object?)draft.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_at", draft.CreatedAt);
            cmd.Parameters.AddWithValue("@updated_at", draft.UpdatedAt);
            cmd.Parameters.AddWithValue("@extra_json", (object?)extraJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hidden", hidden ? 1 : 0);
            cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            // UPDATE 集刻意不含 hidden/pinned/notes：用户状态不因"又被复制了一次"而回退。
            id = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        }

        // ② search_text 走唯一口径重建（title + description + notes + 标签，再 CJK 展开）。
        //     复用 RebuildSearchTextAsync 而不是自己再拼一遍：这里必须把①读回来的旧 notes 与旧标签
        //     一起算进去，否则"给某条历史写了笔记→再次复制→笔记词从索引里消失"。
        await RebuildSearchTextAsync(conn, id, ct);

        // ③ 轮转：只裁未置顶的，按"最近复制"倒排留 maxEntries 条。
        using (var prune = conn.CreateCommand())
        {
            prune.CommandText = @"
                DELETE FROM items
                WHERE source = @source AND pinned = 0
                  AND id NOT IN (
                      SELECT id FROM items WHERE source = @source AND pinned = 0
                      ORDER BY updated_at DESC, id DESC LIMIT @keep);";
            prune.Parameters.AddWithValue("@source", draft.Source);
            prune.Parameters.AddWithValue("@keep", Math.Max(1, maxEntries));
            await prune.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();

        draft.Id = id;
        draft.ExtraJson = extraJson;
        draft.Hidden = hidden;
        draft.Pinned = pinned;
        draft.Notes = notes;
        return draft;
    }

    /// <summary>
    /// 清空全部剪贴板历史（<b>含置顶</b>），返回删除条数。
    /// <para>之所以连置顶一起删：用户点"清空"要的是"这台机器上不再留着我复制过的东西"，
    /// 留一堆"豁免项"既不符合直觉也违背这个功能的隐私目的。UI 侧必须在确认框里写明含多少条置顶。</para>
    /// </summary>
    public async Task<int> ClearClipboardHistoryAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE source = @source;";
        cmd.Parameters.AddWithValue("@source", ItemSources.Clipboard);
        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        DataChangeHub.Notify();
        return deleted;
    }

    /// <summary>
    /// 删除<b>一条</b>剪贴板历史，返回是否真的删掉了一行（0 行＝那条已不在）。
    /// <para>WHERE 里 <b>id 与 source 两个条件缺一不可</b>：只按 id 删的方法签名无法阻止调用方
    /// 传进书签/Star/待办的 Id，而那种误用的表现是"用户点了删除一条复制记录，结果丢了一条不可重建的条目"。
    /// 标签关联与 FTS 索引不需要这里处理：<c>item_tags</c> 等表是 <c>ON DELETE CASCADE</c>，
    /// <c>items_fts</c> 有 <c>AFTER DELETE</c> 触发器（Schema.sql）。</para>
    /// </summary>
    public async Task<bool> DeleteClipboardEntryAsync(long itemId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE id = @id AND source = @source;";
        cmd.Parameters.AddWithValue("@id", itemId);
        cmd.Parameters.AddWithValue("@source", ItemSources.Clipboard);
        var removed = await cmd.ExecuteNonQueryAsync(ct);
        if (removed > 0) DataChangeHub.Notify();
        return removed > 0;
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
            // 与 BackupRepository.MapItem 同口径用 TryParse 兜底：items.type 无 CHECK 约束，
            // 降级/备份恢复/手工修正可留 Enum.Parse 抛 ArgumentException 的未知值；而 MapItem 是所有读路径
            // （Search/GetAll/GetPinned/GetHidden/…）的水合入口，一行坏值即整页查询抛异常（非只坏那一行）。
            Type = Enum.TryParse<ItemType>(reader.GetString(1), ignoreCase: true, out var itemType) ? itemType : ItemType.Bookmark,
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
                // AND/OR/NOT/NEAR 是 FTS5 大写布尔算符：裸 "AND*" 会被语法解析器当算符、
                // 对尾随 '*' 报 "fts5: syntax error"（语句级、整条 MATCH 崩）。本项目的检索语义
                // 是"空格=全部 AND"、不支持布尔算符，故把这几个词降级为普通检索词（小写化去掉
                // 算符身份），仍走前缀匹配——既不崩、又能按字面召回含该词的条目。
                var term = IsFts5Operator(t) ? t.ToLowerInvariant() : t;
                sb.Append(term).Append('*');  // 前缀匹配
            }
        }
        return sb.ToString();
    }

    private static bool IsFts5Operator(string token) =>
        token.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("NOT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("NEAR", StringComparison.OrdinalIgnoreCase);
}
