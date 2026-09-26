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
/// 标签与笔记：条目用户状态的读写。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>
/// （partial）——按 SQLite 访问面分文件，不是一个新抽象层。
/// </summary>
public sealed partial class ItemRepository
{
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
        var activityRows = new List<(ActivityKind Kind, string? Key, string Title, string? Uri)>();
        foreach (var id in live.Keys.OrderBy(id => id))
        {
            var row = rows[id];
            activityRows.Add((ActivityKind.ItemModify, row.Source + ":" + row.SourceId, row.Title, row.Uri));
        }
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
        IReadOnlyList<(ActivityKind Kind, string? Key, string Title, string? Uri)> rows, CancellationToken ct)
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
                insert.Parameters.AddWithValue($"@key{i}", (object?)slice[i].Key ?? DBNull.Value);
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

    /// <summary>
    /// 一次连接写入一批活动事件（拖入 N 个快捷入口时的那一圈写库：逐条 <see cref="LogActivityAsync"/>
    /// 就是 N 次开库 + N 次数总数 + N 次裁剪）。环形缓冲口径与单条完全一致：整批插完只裁一次，仍留最近 500 条。
    /// </summary>
    public async Task LogActivitiesAsync(IReadOnlyList<ActivityDraft> events, CancellationToken ct = default)
    {
        if (events is not { Count: > 0 }) return;             // 空批不开库：没有"什么都没记"还要惊动组件的道理
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();
        var rows = new List<(ActivityKind Kind, string? Key, string Title, string? Uri)>(events.Count);
        foreach (var e in events) rows.Add((e.Kind, e.ItemKey, e.Title, e.Uri));
        await LogActivitiesOnConnection(conn, tx, rows, ct);
        await tx.CommitAsync(ct);
        DataChangeHub.Notify();   // 活动事件本身也是「数据」：让常驻的最近活动格即时跟上，整批一次
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

}
