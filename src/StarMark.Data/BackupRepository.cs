#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;

namespace StarMark.Data;

/// <summary>
/// <see cref="IBackupRepository"/> 的 SQLite 实现。
/// </summary>
/// <remarks>
/// 全部关联一律走 <c>(source, IFNULL(source_id,''))</c>——这是 items 表的业务唯一键，
/// 行 id 在导出/导入之间不保真，不能用作关联依据。
/// </remarks>
public sealed class BackupRepository : IBackupRepository
{
    private readonly DbConnectionFactory _factory;

    public BackupRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    // ==================== 导出 ====================

    public Task<IReadOnlyList<Item>> ExportItemsAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.type, i.source, i.source_id, i.title, i.subtitle, i.uri,
                   i.description, i.stars_count, i.file_size, i.created_at, i.updated_at,
                   i.synced_at, i.extra_json, i.hidden, i.pinned, i.notes
            FROM items i;";
        var list = new List<Item>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(MapItem(reader));
        return Task.FromResult<IReadOnlyList<Item>>(list);
    }

    public Task<IReadOnlyList<UserStateRecord>> ExportUserStateAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT source, IFNULL(source_id, ''), hidden, pinned, notes
            FROM items
            WHERE hidden <> 0 OR pinned <> 0 OR (notes IS NOT NULL AND notes <> '');";
        var list = new List<UserStateRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UserStateRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.GetInt64(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return Task.FromResult<IReadOnlyList<UserStateRecord>>(list);
    }

    public Task<IReadOnlyList<TagRecord>> ExportTagsAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, color, extra_json FROM tags;";
        var list = new List<TagRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return Task.FromResult<IReadOnlyList<TagRecord>>(list);
    }

    public Task<IReadOnlyList<ItemTagLink>> ExportItemTagLinksAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.source, IFNULL(i.source_id, ''), t.name
            FROM item_tags it
            JOIN items i ON i.id = it.item_id
            JOIN tags  t ON t.id = it.tag_id;";
        var list = new List<ItemTagLink>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ItemTagLink(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return Task.FromResult<IReadOnlyList<ItemTagLink>>(list);
    }

    // ==================== 导入 ====================

    public async Task ImportItemsAsync(IReadOnlyList<Item> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        // 复用主仓储的 Upsert：用户状态合并且 search_text 按当前 CJK 规则重算
        var repo = new ItemRepository(_factory);
        const int batch = 200;
        for (int i = 0; i < items.Count; i += batch)
        {
            await repo.UpsertAsync(Slice(items, i, batch), ct);
        }
    }

    public Task ImportUserStateAsync(IReadOnlyList<UserStateRecord> states, CancellationToken ct)
    {
        if (states.Count == 0) return Task.CompletedTask;
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE items
               SET hidden = @hidden, pinned = @pinned, notes = @notes
             WHERE source = @src AND IFNULL(source_id, '') = @sid;";
        var pHidden = cmd.Parameters.Add("@hidden", SqliteType.Integer);
        var pPinned = cmd.Parameters.Add("@pinned", SqliteType.Integer);
        var pNotes = cmd.Parameters.Add("@notes", SqliteType.Text);
        var pSrc = cmd.Parameters.Add("@src", SqliteType.Text);
        var pSid = cmd.Parameters.Add("@sid", SqliteType.Text);

        foreach (var s in states)
        {
            pHidden.Value = s.Hidden ? 1L : 0L;
            pPinned.Value = s.Pinned ? 1L : 0L;
            pNotes.Value = (object?)s.Notes ?? DBNull.Value;
            pSrc.Value = s.Source;
            pSid.Value = s.SourceId;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    public Task ImportTagsAsync(IReadOnlyList<TagRecord> tags, CancellationToken ct)
    {
        if (tags.Count == 0) return Task.CompletedTask;
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        // name 上已有 UNIQUE COLLATE NOCASE；已存在则只补 color/extra_json 的空缺
        cmd.CommandText = @"
            INSERT INTO tags(name, color, extra_json, created_at)
            VALUES (@name, @color, @extra, @now)
            ON CONFLICT(name) DO UPDATE SET
                color      = COALESCE(tags.color, excluded.color),
                extra_json = COALESCE(tags.extra_json, excluded.extra_json);";
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pColor = cmd.Parameters.Add("@color", SqliteType.Text);
        var pExtra = cmd.Parameters.Add("@extra", SqliteType.Text);
        var pNow = cmd.Parameters.Add("@now", SqliteType.Integer);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            pName.Value = t.Name.Trim();
            pColor.Value = (object?)t.Color ?? DBNull.Value;
            pExtra.Value = (object?)t.ExtraJson ?? DBNull.Value;
            pNow.Value = now;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    public Task ImportItemTagLinksAsync(IReadOnlyList<ItemTagLink> links, CancellationToken ct)
    {
        if (links.Count == 0) return Task.CompletedTask;
        using var conn = _factory.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO item_tags(item_id, tag_id, created_at)
            SELECT i.id, t.id, @now
              FROM items i, tags t
             WHERE i.source = @src AND IFNULL(i.source_id, '') = @sid
               AND t.name = @tag
               AND NOT EXISTS (SELECT 1 FROM item_tags x WHERE x.item_id = i.id AND x.tag_id = t.id);";
        var pSrc = cmd.Parameters.Add("@src", SqliteType.Text);
        var pSid = cmd.Parameters.Add("@sid", SqliteType.Text);
        var pTag = cmd.Parameters.Add("@tag", SqliteType.Text);
        var pNow = cmd.Parameters.Add("@now", SqliteType.Integer);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var l in links)
        {
            if (string.IsNullOrWhiteSpace(l.TagName)) continue;
            pSrc.Value = l.Source;
            pSid.Value = l.SourceId;
            pTag.Value = l.TagName.Trim();
            pNow.Value = now;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    public Task ClearItemsAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // items_ad_fts 触发器会同步清理 FTS；item_tags 由外键 ON DELETE CASCADE 清理
        cmd.CommandText = "DELETE FROM items;";
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task ClearItemTagLinksAsync(CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM item_tags;";
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    // ==================== 内部 ====================

    private static IReadOnlyList<Item> Slice(IReadOnlyList<Item> source, int offset, int count)
    {
        int n = Math.Min(count, source.Count - offset);
        var result = new List<Item>(n);
        for (int i = 0; i < n; i++) result.Add(source[offset + i]);
        return result;
    }

    private static Item MapItem(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Type = Enum.TryParse<ItemType>(r.GetString(1), ignoreCase: true, out var t) ? t : ItemType.Bookmark,
        Source = r.GetString(2),
        SourceId = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Title = r.GetString(4),
        Subtitle = r.IsDBNull(5) ? string.Empty : r.GetString(5),
        Uri = r.IsDBNull(6) ? string.Empty : r.GetString(6),
        Description = r.IsDBNull(7) ? null : r.GetString(7),
        StarsCount = r.IsDBNull(8) ? null : r.GetInt64(8),
        FileSize = r.IsDBNull(9) ? null : r.GetInt64(9),
        CreatedAt = r.GetInt64(10),
        UpdatedAt = r.GetInt64(11),
        SyncedAt = r.IsDBNull(12) ? null : r.GetInt64(12),
        ExtraJson = r.IsDBNull(13) ? null : r.GetString(13),
        Hidden = r.GetInt64(14) != 0,
        Pinned = r.IsDBNull(15) ? false : r.GetInt64(15) != 0,
        Notes = r.IsDBNull(16) ? null : r.GetString(16),
    };
}
