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
/// 布局与数据快照的忠实捕获／还原（按实例前缀读写）。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>
/// （partial）——按 SQLite 访问面分文件，不是一个新抽象层。
/// </summary>
public sealed partial class ItemRepository
{
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
}
