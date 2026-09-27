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
    /// <para><b>这里刻意不碰 clip 目录</b>：WHERE 里钉死了 <c>source=local</c>，剪贴板行根本不可能
    /// 从这里被删掉；给一条走不到的路径加"尽力删文件"，只会让下一个读代码的人以为剪贴板删除有两条路。</para>
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
        // 只有剪贴板那一路的键背后有文件，所以先按来源决定要不要读 extra（书签/Star/Ditto 的删除路径
        // 一次都不该去碰 clip 目录——它们和"复制过的一张图"没有关系）。
        string? extra = null;
        if (source == ItemSources.Clipboard)
        {
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT extra_json FROM items WHERE source = @source AND source_id = @sid;";
            read.Parameters.AddWithValue("@source", source);
            read.Parameters.AddWithValue("@sid", sourceId);
            extra = (await read.ExecuteScalarAsync(ct)) as string;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM items WHERE source = @source AND source_id = @sid;";
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@sid", sourceId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        TryDeleteClipFiles(new[] { extra });
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
    /// <param name="maxEntries"><b>非图片那一路</b>（文本/文件列表）的保留上限，默认
    /// <see cref="ClipboardPolicy.MaxEntries"/>；测试与非默认策略可传更小值。小于 1 按 1 处理
    /// （至少留下刚写的这条）。<b>名字沿用不改</b>：这是既有文本测试的调用点，P1 的验收级前提就是
    /// "文本那条一字不动仍全绿"。</param>
    /// <param name="imageMaxEntries">图片那一路的上限（§4：默认 200，与文本分开——一张 4K PNG 常有几百 KB）。</param>
    /// <remarks>
    /// <b>轮转分桶，而且桶的判据只有一份</b>：<c>clipFormat</c> 是不是 <see cref="ClipboardEntry.FormatImage"/>。
    /// SQL 里那个值走 <c>@image_format</c> 参数（由 C# 递进去），C# 侧问
    /// <see cref="ClipboardEntry.IsImageOf"/>——两边各写一个字面量"image"的话，改天统一叫法就会只改一处，
    /// 症状是"图片按文本的上限被裁掉"或反之，而这两种都只在用户删了图之后才看得见。
    /// 分桶还顺带钉住一件事：文本记满 500 条不会把用户的截图裁到 500 之外，反之亦然。
    /// </remarks>
    public async Task<Item> RecordClipboardAsync(Item draft, CancellationToken ct = default,
        int maxEntries = ClipboardPolicy.MaxEntries, int imageMaxEntries = ClipboardPolicy.DefaultImageMaxEntries)
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

        // ③ 轮转：只裁未置顶的，按"最近复制"倒排留上限条。图片与文本各裁各的桶（判据见方法注释）。
        var isImage = ClipboardEntry.IsImageOf(extraJson);
        var keep = Math.Max(1, isImage ? imageMaxEntries : maxEntries);
        // 图片桶要先读后删：DELETE 一旦执行，extra_json 里的文件名就没了，"尽力删文件"只能删个空气，
        // 磁盘上留下一堆没人认领的 PNG——而那正是 §3-Q6 要避免的第二类脏。
        var prunedExtras = isImage
            ? await ReadPrunedClipExtrasAsync(conn, draft.Source, keep, ct)
            : Array.Empty<string?>();
        using (var prune = conn.CreateCommand())
        {
            prune.CommandText = $@"
                DELETE FROM items
                WHERE source = @source AND pinned = 0 {ClipBucketClause}
                  AND id NOT IN (
                      SELECT id FROM items WHERE source = @source AND pinned = 0 {ClipBucketClause}
                      ORDER BY updated_at DESC, id DESC LIMIT @keep);";
            AddClipBucketParameters(prune, draft.Source, isImage);
            prune.Parameters.AddWithValue("@keep", keep);
            await prune.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        // 文件删除放在提交<b>之后</b>（先读到的名字还攥在手里）：反过来做的话，提交一旦失败回滚，
        // 就成了"行还在、文件已经没了"——那正是 §3-Q6 第一类里最不该由我们自己造成的一种。
        if (isImage) TryDeleteClipFiles(prunedExtras);
        DataChangeHub.Notify();

        draft.Id = id;
        draft.ExtraJson = extraJson;
        draft.Hidden = hidden;
        draft.Pinned = pinned;
        draft.Notes = notes;
        return draft;
    }

    /// <summary>
    /// 剪贴板轮转的分桶判据（<b>唯一出处</b>）。
    /// <para>图片与文本必须分开裁：§4 给了两条独立上限（200 / 500），一起裁的话"复制 500 段文字"
    /// 会把用户的截图裁出历史，反过来"开了一天图片采集"会把文字历史挤掉——两种都是用户看不见成因的丢数据。</para>
    /// <para><b>两层兜底各挡一种坏法：</b>
    /// ① <c>json_valid</c>——<c>json_extract</c> 遇到<b>不合法 JSON 是抛错</b>而不是返回 NULL，
    /// 于是一行被手改过的 <c>extra_json</c> 就能让整条轮转语句失败 ⇒ 剪贴板从此一条都记不上
    /// （ClipIMG-1e 的对账测试实测踩到：坏 JSON 那一行让 <c>GetClipboardImageAssetsAsync</c> 整个抛出来）。
    /// ② <c>COALESCE(...,0)</c>——<c>extra_json</c> 为空/没有 clipFormat 的行，与那个格式值比出来的结果是
    /// <b>NULL</b> 而不是 false，直接写进 WHERE 会让这些行既不进图片桶也不进文本桶 ⇒ 永远裁不掉。</para>
    /// <para>读不出格式的一律按<b>文本桶</b>处理：它仍然可被裁掉，不会因为"归不进任何桶"而长生不老。</para>
    /// </summary>
    private const string ClipBucketClause =
        "AND COALESCE(json_extract(CASE WHEN json_valid(extra_json) THEN extra_json ELSE '{}' END, "
        + "'$.clipFormat') = @image_format, 0) = @is_image";

    /// <summary>桶参数的唯一拼装处（<c>@image_format</c> 由 <see cref="ClipboardEntry.FormatImage"/> 递进去，SQL 里不写字面量"image"）。</summary>
    private static void AddClipBucketParameters(SqliteCommand cmd, string source, bool isImage)
    {
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@image_format", ClipboardEntry.FormatImage);
        cmd.Parameters.AddWithValue("@is_image", isImage ? 1 : 0);
    }

    /// <summary>这次轮转将裁掉的图片行的 <c>extra_json</c>（<b>必须在 DELETE 之前读</b>，之后名字就没了）。</summary>
    private static async Task<IReadOnlyList<string?>> ReadPrunedClipExtrasAsync(
        SqliteConnection conn, string source, int keep, CancellationToken ct)
    {
        var list = new List<string?>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT extra_json FROM items
            WHERE source = @source AND pinned = 0 {ClipBucketClause}
              AND id NOT IN (
                  SELECT id FROM items WHERE source = @source AND pinned = 0 {ClipBucketClause}
                  ORDER BY updated_at DESC, id DESC LIMIT @keep);";
        AddClipBucketParameters(cmd, source, isImage: true);
        cmd.Parameters.AddWithValue("@keep", keep);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.IsDBNull(0) ? null : r.GetString(0));
        return list;
    }

    /// <summary>这个库里<b>所有</b>图片行的 <c>extra_json</c>（含置顶——"清空"连置顶一起删，文件也要跟着一起没）。</summary>
    private static async Task<IReadOnlyList<string?>> ReadClipImageExtrasAsync(SqliteConnection conn, CancellationToken ct)
    {
        var list = new List<string?>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT extra_json FROM items WHERE source = @source {ClipBucketClause};";
        AddClipBucketParameters(cmd, ItemSources.Clipboard, isImage: true);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.IsDBNull(0) ? null : r.GetString(0));
        return list;
    }

    /// <summary>
    /// 删行之后<b>尽力</b>删掉它记着的主图与缩略图（§3-Q5"同生同死" ⇒ 一行两个文件；§3-Q6"尽力删 + 对账"）。
    /// <para><b>只删这一行自己记着的那两个名字</b>：目录里用户拷进来的、或行还没落库的文件一律不碰——
    /// "清空历史"要的是"不再留着我复制过的东西"，不是"清空那个目录"。</para>
    /// <para>删不掉不抛：那一档失败多半是资源管理器正打开着预览或杀毒软件抓着，
    /// 不该让用户点"删除这一条"变成一次失败的操作；剩下的孤儿由启动对账数出来。</para>
    /// </summary>
    private static void TryDeleteClipFiles(IEnumerable<string?> extras)
    {
        foreach (var extra in extras)
        {
            var (main, thumb) = ClipboardEntry.ClipFileNamesOf(extra);
            DeleteClipFile(main);
            if (thumb != main) DeleteClipFile(thumb);   // 旧坏数据里缩略图名回落成主图名，不重复删
        }
    }

    private static void DeleteClipFile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (ClipAssets.FullPathOf(name) is not { } path)
        {
            // 不安全＝这条来自手改过的备份或坏 extra。不出声的话，磁盘上就会永远留着一个没人说得清的件。
            StarLog.Warn($"剪贴板图片文件名不安全，未删除：{name}");
            return;
        }
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[剪贴板] 图片文件没删掉（{name}）：{ex.Message}");
        }
    }

    /// <summary>
    /// 清空全部剪贴板历史（<b>含置顶</b>），返回删除条数。
    /// <para>之所以连置顶一起删：用户点"清空"要的是"这台机器上不再留着我复制过的东西"，
    /// 留一堆"豁免项"既不符合直觉也违背这个功能的隐私目的。UI 侧必须在确认框里写明含多少条置顶。</para>
    /// <para>图片文件按<b>行记着的名字</b>删（含置顶行），目录里那些没有行认领的文件不碰——
    /// 那是 §3-Q6 第二类的孤儿，只能由对账数出来、由用户点"清理孤儿"处理，不由"清空"顺手代劳。</para>
    /// </summary>
    public async Task<int> ClearClipboardHistoryAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var imageExtras = await ReadClipImageExtrasAsync(conn, ct);
        int deleted;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM items WHERE source = @source;";
            cmd.Parameters.AddWithValue("@source", ItemSources.Clipboard);
            deleted = await cmd.ExecuteNonQueryAsync(ct);
        }
        TryDeleteClipFiles(imageExtras);
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
        // 名字要在删之前拿到手：删完再查就查不到了，而"这条历史我删了"在用户那边等于"这张图不在我机器上了"。
        string? extra = null;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT extra_json FROM items WHERE id = @id AND source = @source;";
            read.Parameters.AddWithValue("@id", itemId);
            read.Parameters.AddWithValue("@source", ItemSources.Clipboard);
            extra = (await read.ExecuteScalarAsync(ct)) as string;
        }

        int removed;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM items WHERE id = @id AND source = @source;";
            cmd.Parameters.AddWithValue("@id", itemId);
            cmd.Parameters.AddWithValue("@source", ItemSources.Clipboard);
            removed = await cmd.ExecuteNonQueryAsync(ct);
        }
        if (removed > 0)
        {
            // 一行没删掉（0 行＝那条本来就不在）时<b>不碰文件</b>：那种文件是没有行认领的孤儿，
            // 归 §3-Q6 第二类，只能由对账数出来给用户一个"清理孤儿"的按钮，不该由这次点击顺手删。
            TryDeleteClipFiles(new[] { extra });
            DataChangeHub.Notify();
        }
        return removed > 0;
    }

    /// <summary>
    /// 对账要的"所有图片行"（批次 ClipIMG-1e）。<b>刻意不带行数窗口</b>：
    /// 借 <c>GetBySourceAsync</c> 那份默认 1000 的 limit 来对账，窗口之外的行会被当成"没人认领的文件"，
    /// 于是<b>一个本来完好的目录被报成一堆孤儿</b>——少报与错报一样难被发现（RSS 收藏键踩过同一坑）。
    /// 也不带 description：图片行没有正文，而把 2000 行的正文捞进内存只为比一个文件名，是白付的代价。
    /// </summary>
    public async Task<IReadOnlyList<ClipboardEntry.ClipAssetRow>> GetClipboardImageAssetsAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, extra_json FROM items WHERE source = @source {ClipBucketClause};";
        AddClipBucketParameters(cmd, ItemSources.Clipboard, isImage: true);
        var list = new List<ClipboardEntry.ClipAssetRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ClipboardEntry.AssetOf(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1)));
        return list;
    }

    /// <summary>
    /// 批量置 / 清 <c>clipMissing</c>，返回<b>真正改动</b>的行数。
    /// <para>用 <c>json_set</c>/<c>json_remove</c> 就地改一个键，而不是"读出来—反序列化—整列写回"：
    /// 后者会为改一个布尔把 extra 里其它键一起过一遍手，任何一处读写口径不一致都会静默丢字段。</para>
    /// <para><b><c>json_valid</c> 那道守卫不是可选项</b>：坏 JSON 喂给 <c>json_set</c> 得到的不是报错而是
    /// <b>NULL</b>，那一行的 extra 会被整列抹掉——用户下次看到的就是"这条历史连尺寸都没了"。
    /// 所以坏数据宁可这次不标（对账下一轮还会再来），也不能顺手清一遍用户的库。</para>
    /// <para>WHERE 里 <c>source='clipboard'</c> 与 id 两个条件缺一不可（同 <see cref="DeleteClipboardEntryAsync"/>）。</para>
    /// </summary>
    public async Task<int> SetClipboardMissingFlagsAsync(
        IReadOnlyList<long> markMissing, IReadOnlyList<long> clearMissing, CancellationToken ct = default)
    {
        var changed = 0;
        changed += await ApplyMissingFlagAsync(markMissing, add: true, ct);
        changed += await ApplyMissingFlagAsync(clearMissing, add: false, ct);
        return changed;
    }

    private async Task<int> ApplyMissingFlagAsync(IReadOnlyList<long> ids, bool add, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return 0;

        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        var placeholders = new List<string>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var name = "@i" + i;
            placeholders.Add(name);
            cmd.Parameters.AddWithValue(name, ids[i]);
        }

        cmd.CommandText = $@"
            UPDATE items SET extra_json =
                {(add ? "json_set(extra_json, '$.clipMissing', json('true'))"
                      : "json_remove(extra_json, '$.clipMissing')")}
            WHERE source = @source AND json_valid(extra_json) AND id IN ({string.Join(",", placeholders)})
              AND COALESCE(json_extract(extra_json, '$.clipMissing'), 0) <> {(add ? 1 : 0)};";
        cmd.Parameters.AddWithValue("@source", ItemSources.Clipboard);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows > 0) DataChangeHub.Notify();
        return rows;
    }

}
