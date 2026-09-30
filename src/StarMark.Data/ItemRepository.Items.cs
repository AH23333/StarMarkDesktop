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
/// 条目 CRUD 与浏览模式（按文件夹/类型浏览的读路径）。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>
/// （partial）——按 SQLite 访问面分文件，不是一个新抽象层。
/// </summary>
public sealed partial class ItemRepository
{
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

    /// <summary>
    /// 一次事务登记多条（一次拖入 N 个文件时的那一圈写库）。逐条 <see cref="RecordItemAsync"/> 的话
    /// N 个文件要开 N 次库、跑 N×3 遍 PRAGMA、通知组件 N 次；这里全部压成一次。
    /// 单条语义原样保留：缺业务键的不写、同一 (source, source_id) 幂等合并、保留用户态、重建 search_text。
    /// 同一批里重复的业务键只登记一次（返回登记条数，不是入参条数）。
    /// </summary>
    public async Task<int> RecordItemsAsync(IReadOnlyList<Item> items, CancellationToken ct = default)
    {
        if (items is not { Count: > 0 }) return 0;
        var writable = new List<Item>(items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrEmpty(item.Source) || string.IsNullOrEmpty(item.SourceId)) continue;
            // 同一批里重复的业务键只写一次：一次拖放很容易带进同一个文件两遍，
            // 第二次 upsert 不落任何新事实，却白重建一次全文索引。
            if (!seen.Add(item.Source + ":" + item.SourceId)) continue;
            writable.Add(item);
        }
        if (writable.Count == 0) return 0;               // 整批都没业务键：不开库、不通知

        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();
        foreach (var item in writable) await UpsertOne(conn, item, ct);
        await tx.CommitAsync(ct);
        DataChangeHub.Notify();   // 整批一次：置顶/标签/搜索等组件跟着刷一遍，而不是 N 遍
        return writable.Count;
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
            where.Add($"(i.type = 'githubstar' AND json_extract({ExtraJsonGuard.Safe("i.extra_json")}, '$.Language') = @lang)");

        // 标签过滤：AND 语义（必须同时具备全部标签）。
        // 原实现用 `JOIN + t.name IN (...) + GROUP BY` 实际是 OR —— 多选标签时结果反而变多，
        // 与「多选收窄」的预期相反，已改为与搜索一致的 EXISTS 逐条判定。

        var orderBy = filter.Sort switch
        {
            "stars" => "i.pinned DESC, i.stars_count DESC NULLS LAST",
            "name" => "i.pinned DESC, i.title COLLATE NOCASE ASC",
            // 最近 Star / 最近收藏：与 SearchAsync 的同名分支口径一致（starredAt 取 extra_json，无则退
            // updated_at；collected 取入库时间）。此前浏览模式漏了这两支，落入 default 变成「最近更新」。
            "starred" => $"i.pinned DESC, COALESCE(CAST(json_extract({ExtraJsonGuard.Safe("i.extra_json")}, '$.StarredAt') AS INTEGER), i.updated_at) DESC",
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

}
