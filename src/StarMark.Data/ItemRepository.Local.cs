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
/// 本地条目（待办／随记）——统一落在 items 表里的那一套。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>
/// （partial）——按 SQLite 访问面分文件，不是一个新抽象层。
/// </summary>
public sealed partial class ItemRepository
{
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

    /// <summary>
    /// 一次连接、只读 <c>source_id</c> 一列、<b>不带 LIMIT</b>：RSS 页要用它把"已经收过的"标出来，
    /// 而任何行数窗口都会让窗口外的已收藏条目被当成没收藏（按钮说假话，且看起来完全正常）。
    /// 前缀只允许取自 <c>RssEntryIdentity.BookmarkSourcePrefix</c>，这里不再抄一份字面量。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCollectedRssLinksAsync(CancellationToken ct = default)
    {
        var links = new List<string>();
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT source_id FROM items
            WHERE source = @source AND type = @type AND source_id LIKE @prefix;";
        cmd.Parameters.AddWithValue("@source", ItemSources.Local);
        cmd.Parameters.AddWithValue("@type", ItemType.Bookmark.ToString().ToLowerInvariant());
        // 前缀只含 ASCII 字母、连字符与冒号，不含 LIKE 通配符（% 与 _），故无需转义。
        cmd.Parameters.AddWithValue("@prefix", StarMark.Abstractions.Feed.RssEntryIdentity.BookmarkSourcePrefix + "%");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            links.Add(reader.GetString(0)[StarMark.Abstractions.Feed.RssEntryIdentity.BookmarkSourcePrefix.Length..]);
        return links;
    }

    /// <summary>
    /// 由被写/被删的那一行自己拼出活动记录。<b>主体字段一律不让调用方填</b>：
    /// 组件那边只说"这一笔算什么"（新增/删除），于是时间线里不可能出现一条与库里内容对不上的标题或键。
    /// </summary>
    private static (ActivityKind Kind, string? Key, string Title, string? Uri) ActivityRow(ActivityKind kind, Item row)
        => (kind, row.Source + ":" + row.SourceId, row.Title, string.IsNullOrEmpty(row.Uri) ? null : row.Uri);

    /// <summary>
    /// 按 id 读一条本地条目（<b>限定 <c>source='local'</c> 且类型相符</b>）。
    /// 不限定的话，一个来自别处的 id 就能被组件改走一整行。
    /// </summary>
    private static async Task<Item?> ReadLocalItemAsync(
        SqliteConnection conn, SqliteTransaction? tx, long id, ItemType type, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes,
                   (SELECT GROUP_CONCAT(t.name, char(31)) FROM item_tags it
                    JOIN tags t ON t.id = it.tag_id
                    WHERE it.item_id = i.id) AS tag_names
            FROM items i
            WHERE i.id = @id AND i.source = @source AND i.type = @type;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@source", ItemSources.Local);
        cmd.Parameters.AddWithValue("@type", type.ToString().ToLowerInvariant());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapItem(reader) : null;
    }

    /// <summary>按 id 取一条本地条目。语义（含"为什么必须在 SQL 里限定作用域"）见接口注释。</summary>
    public async Task<Item?> GetLocalItemAsync(long id, ItemType type, CancellationToken ct = default)
    {
        if (id <= 0) return null;                          // 虚拟行（Id=0）本来就不在库里，不必开库问
        using var conn = _factory.Open();
        return await ReadLocalItemAsync(conn, null, id, type, ct);
    }

    /// <summary>
    /// 一次连接问出"哪些实例名下还有本地条目"。<b>只读 source_id 一列</b>：这条的目的就是把
    /// "为了一台空实例开一次库、还把每行的标签拼一遍"那种花销省掉。
    /// 实例归属用 <see cref="LocalItemState.DecodeInstanceId"/> 解，编码规则因此只有一份。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetInstancesWithLocalItemsAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT source_id FROM items WHERE source = @source;";
        cmd.Parameters.AddWithValue("@source", ItemSources.Local);
        var owners = new SortedSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var owner = LocalItemState.DecodeInstanceId(reader.GetString(0));
            if (!string.IsNullOrEmpty(owner)) owners.Add(owner);   // 没有分隔符的（非组件编码）一律忽略
        }
        return owners.ToList();
    }

    /// <summary>
    /// 删一条本地条目 +（可选）在同一事务里记一笔活动，返回被删掉的那行；
    /// 没删到就返回 null，且<b>什么都不写、也不通知</b>。
    /// 同事务的理由见接口注释：条目没了而时间线里找不到这一笔，是最难向用户解释的缺口。
    /// </summary>
    public async Task<Item?> DeleteLocalItemAsync(
        long id, ItemType type, ActivityKind? activity = null, CancellationToken ct = default)
    {
        if (id <= 0) return null;
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();

        var row = await ReadLocalItemAsync(conn, tx, id, type, ct);
        if (row is null) return null;

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM items WHERE id = @id AND source = @source AND type = @type;";
            del.Parameters.AddWithValue("@id", id);
            del.Parameters.AddWithValue("@source", ItemSources.Local);
            del.Parameters.AddWithValue("@type", type.ToString().ToLowerInvariant());
            await del.ExecuteNonQueryAsync(ct);
        }

        if (activity is { } kind)
            await LogActivitiesOnConnection(conn, tx, new[] { ActivityRow(kind, row) }, ct);

        await tx.CommitAsync(ct);
        DataChangeHub.Notify();
        return row;
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
    /// 默认不写活动流，也不跑 <c>UriNormalizer</c>（source_id 是自定义编码）与 <c>LanguageDetector</c>，
    /// 以免污染本地内容的 source_id 与完成态。<b>带上 <paramref name="activity"/> 时，那一笔活动
    /// 与本次写落在同一事务里</b>——调用方因此不必为了"删除要记一笔"再开一次库。
    /// </summary>
    public async Task UpsertLocalItemAsync(Item item, CancellationToken ct = default, ActivityKind? activity = null)
    {
        using var conn = _factory.Open();
        // 带上活动时开一个事务：写条目与记活动要么都成立、要么都不成立。
        // 不带活动时不开（一次写入本来就是一条语句，BEGIN/COMMIT 只是白付两次往返）。
        using SqliteTransaction? tx = activity is null ? null : conn.BeginTransaction();
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
        if (activity is { } kind)
            await LogActivitiesOnConnection(conn, tx!, new[] { ActivityRow(kind, item) }, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        DataChangeHub.Notify();
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

}
