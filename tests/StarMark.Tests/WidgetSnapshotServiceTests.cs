#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 快照数据服务 (#53-L2) 的行为契约：捕获按实例隔离 / extra_json 状态原样带走 /
/// 还原为「整实例 Replace」且不伤及别的实例 / source_id 重新编码后可跨实例移植。
/// </summary>
public sealed class WidgetSnapshotServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;
    private readonly WidgetSnapshotService _svc;

    public WidgetSnapshotServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_snap_svc_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
        _svc = new WidgetSnapshotService(_repo);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<long> AddLocalAsync(string instanceId, ItemType type, string title, Action<Item>? mutate = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new Item
        {
            Type = type,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId(instanceId, WidgetStorage.NewId()),
            Title = title,
            CreatedAt = now,
            UpdatedAt = now,
        };
        mutate?.Invoke(item);
        await _repo.UpsertLocalItemAsync(item, CancellationToken.None);
        return item.Id;
    }

    [Fact]
    public async Task Capture_IsScopedToInstance_AndCoversTodoAndNote()
    {
        await AddLocalAsync("A", ItemType.Todo, "买牛奶");
        await AddLocalAsync("A", ItemType.Note, "随记一条");
        await AddLocalAsync("B", ItemType.Todo, "别实例的待办");

        var captured = await _svc.CaptureLocalItemsAsync("A", CancellationToken.None);
        Assert.Equal(2, captured.Count);
        Assert.Contains(captured, s => s.Type == ItemType.Todo && s.Title == "买牛奶");
        Assert.Contains(captured, s => s.Type == ItemType.Note && s.Title == "随记一条");
        Assert.DoesNotContain(captured, s => s.Title == "别实例的待办");
    }

    [Fact]
    public async Task Capture_PreservesExtraJsonState()
    {
        await AddLocalAsync("A", ItemType.Todo, "带状态", it =>
        {
            LocalItemState.SetDone(it, true);
            LocalItemState.SetColor(it, 3);
        });

        var captured = await _svc.CaptureLocalItemsAsync("A", CancellationToken.None);
        var s = Assert.Single(captured);
        Assert.Contains("\"done\":true", s.ExtraJson);
        Assert.Contains("\"color\":3", s.ExtraJson);
    }

    [Fact]
    public async Task Restore_ReplacesTargetInstance_LeavesOtherInstances()
    {
        await AddLocalAsync("A", ItemType.Todo, "旧的将被替换");
        await AddLocalAsync("B", ItemType.Todo, "别实例不动");

        var snapshot = new[]
        {
            new SnapshotLocalItem { Type = ItemType.Todo, Title = "快照里的待办" },
            new SnapshotLocalItem { Type = ItemType.Note, Title = "快照里的随记" },
        };
        await _svc.RestoreLocalItemsAsync("A", snapshot, CancellationToken.None);

        var a = await _svc.CaptureLocalItemsAsync("A", CancellationToken.None);
        Assert.Equal(2, a.Count);
        Assert.Contains(a, s => s.Title == "快照里的待办");
        Assert.DoesNotContain(a, s => s.Title == "旧的将被替换");

        // 实例 B 完全不受影响
        var b = await _svc.CaptureLocalItemsAsync("B", CancellationToken.None);
        Assert.Equal("别实例不动", Assert.Single(b).Title);
    }

    [Fact]
    public async Task Restore_ReEncodesSourceId_ToTargetInstance()
    {
        var snapshot = new[] { new SnapshotLocalItem { Type = ItemType.Todo, Title = "移植过来的" } };
        await _svc.RestoreLocalItemsAsync("TARGET", snapshot, CancellationToken.None);

        var stored = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, 1000, CancellationToken.None);
        var restored = Assert.Single(stored, i => i.Title == "移植过来的");
        // source_id 必须编码到目标实例，而非快照来源
        Assert.Equal("TARGET", LocalItemState.DecodeInstanceId(restored.SourceId));
    }

    [Fact]
    public async Task CaptureThenRestore_IsPortableAcrossInstances()
    {
        await AddLocalAsync("SRC", ItemType.Todo, "来自源实例", it => LocalItemState.SetColor(it, 2));
        await AddLocalAsync("SRC", ItemType.Note, "源实例随记");

        var captured = await _svc.CaptureLocalItemsAsync("SRC", CancellationToken.None);
        await _svc.RestoreLocalItemsAsync("DST", captured, CancellationToken.None);

        var dst = await _svc.CaptureLocalItemsAsync("DST", CancellationToken.None);
        Assert.Equal(2, dst.Count);
        var todo = Assert.Single(dst, s => s.Type == ItemType.Todo);
        Assert.Contains("\"color\":2", todo.ExtraJson);   // 状态跨实例移植不丢

        // 源实例内容保持不变（捕获非破坏性）
        var src = await _svc.CaptureLocalItemsAsync("SRC", CancellationToken.None);
        Assert.Equal(2, src.Count);
    }

    [Fact]
    public async Task Restore_WithEmptyItems_ClearsTargetInstance()
    {
        await AddLocalAsync("A", ItemType.Todo, "将被清空");
        await _svc.RestoreLocalItemsAsync("A", Array.Empty<SnapshotLocalItem>(), CancellationToken.None);

        Assert.Empty(await _svc.CaptureLocalItemsAsync("A", CancellationToken.None));
    }

    [Fact]
    public async Task Capture_PreservesFaithfulUserState()
    {
        var id = await AddLocalAsync("A", ItemType.Todo, "带全套字段", it =>
        {
            it.Subtitle = "副标题";
            it.Uri = "file:///D:/x";
            it.Description = "描述文字";
            it.Notes = "笔记内容";
            it.Hidden = true;
        });
        await _repo.SetPinnedAsync(id, true, CancellationToken.None);
        await _repo.AddTagAsync(id, "重要", CancellationToken.None);
        await _repo.AddTagAsync(id, "工作", CancellationToken.None);

        var captured = await _svc.CaptureLocalItemsAsync("A", CancellationToken.None);
        var s = Assert.Single(captured);
        Assert.Equal("副标题", s.Subtitle);
        Assert.Equal("file:///D:/x", s.Uri);
        Assert.Equal("描述文字", s.Description);
        Assert.Equal("笔记内容", s.Notes);
        Assert.True(s.Hidden);
        Assert.True(s.Pinned);
        Assert.Equal(new[] { "工作", "重要" }, s.Tags?.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task RestoreFaithful_RoundTrip_KeepsTagsPinHiddenNotesAcrossInstances()
    {
        var id = await AddLocalAsync("SRC", ItemType.Note, "原样搬走", it =>
        {
            it.Subtitle = "副";
            it.Uri = "http://u";
            it.Description = "描";
            it.Notes = "笔";
            it.Hidden = true;
        });
        await _repo.SetPinnedAsync(id, true, CancellationToken.None);
        await _repo.AddTagAsync(id, "标签甲", CancellationToken.None);

        var captured = await _svc.CaptureLocalItemsAsync("SRC", CancellationToken.None);
        await _svc.RestoreLocalItemsAsync("DST", captured, CancellationToken.None);

        var dst = await _svc.CaptureLocalItemsAsync("DST", CancellationToken.None);
        var s = Assert.Single(dst);
        Assert.Equal("副", s.Subtitle);
        Assert.Equal("http://u", s.Uri);
        Assert.Equal("描", s.Description);
        Assert.Equal("笔", s.Notes);
        Assert.True(s.Hidden);
        Assert.True(s.Pinned);
        Assert.Contains("标签甲", s.Tags ?? new List<string>());
    }
}
