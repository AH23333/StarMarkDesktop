#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「布局与数据快照」(#53) 的存储契约测试：
/// 快照带组件数据（快捷入口 / 待办·随记 / 条目格查询）并完整往返 /
/// **不可变**（同 Id 二次追加永不覆盖，与 SaveLayout 的就地覆盖相对）/
/// 名称自动去重 / 新点在前排序 / Summary 计数。
/// </summary>
public sealed class WidgetSnapshotTests : IDisposable
{
    private readonly string _path;

    public WidgetSnapshotTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"starmark_snapshots_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose() { try { File.Delete(_path); } catch { } }

    private WidgetStorage Store() => new(_path);

    [Fact]
    public void Snapshot_CarriesData_AndRoundTrips()
    {
        var store = Store();
        var snap = new WidgetSnapshot
        {
            Name = "上线前",
            CreatedAt = 1_000,
            Entries =
            {
                new WidgetSnapshotEntry
                {
                    Kind = WidgetKind.QuickLaunch, Index = 0,
                    X = 100, Y = 120, Width = 320, Height = 460, Topmost = true,
                    Links = { new LinkItem { Id = 7, Title = "GitHub", Uri = "https://github.com", CreatedAt = 50 } },
                },
                new WidgetSnapshotEntry
                {
                    Kind = WidgetKind.Todo, Index = 0,
                    X = 440, Y = 120, Width = 300, Height = 400,
                    LocalItems =
                    {
                        new SnapshotLocalItem { Type = ItemType.Todo, Title = "买牛奶", ExtraJson = """{"done":true,"color":3}""", CreatedAt = 10, UpdatedAt = 20 },
                    },
                },
                new WidgetSnapshotEntry
                {
                    Kind = WidgetKind.TagGrid, Index = 0,
                    GridTag = "工作",
                    Appearance = new WidgetAppearanceOverride { Backdrop = WidgetBackdropKind.Mica },
                },
            },
        };

        var returned = store.AppendSnapshot(snap);
        var reloaded = store.FindSnapshot(returned.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("上线前", reloaded!.Name);

        var quick = reloaded.Entries.First(e => e.Kind == WidgetKind.QuickLaunch);
        Assert.True(quick.Topmost);
        var link = Assert.Single(quick.Links);
        Assert.Equal("https://github.com", link.Uri);

        var todo = reloaded.Entries.First(e => e.Kind == WidgetKind.Todo);
        var local = Assert.Single(todo.LocalItems);
        Assert.Equal(ItemType.Todo, local.Type);
        Assert.Equal("买牛奶", local.Title);
        Assert.Contains("\"done\":true", local.ExtraJson);

        var tag = reloaded.Entries.First(e => e.Kind == WidgetKind.TagGrid);
        Assert.Equal("工作", tag.GridTag);
        Assert.Equal(WidgetBackdropKind.Mica, tag.Appearance?.Backdrop);
    }

    [Fact]
    public void Append_IsImmutable_NeverOverwritesSameId()
    {
        var store = Store();
        // 与 SaveLayout 的关键区别：SaveLayout 按 Id 就地覆盖；快照点是不可变历史。
        var first = new WidgetSnapshot { Name = "点一", CreatedAt = 100 };
        store.AppendSnapshot(first);

        // 复用同一 Id 再追加：不得替换既有点，而是生成新 Id 后并存。
        var second = new WidgetSnapshot { Id = first.Id, Name = "点二", CreatedAt = 200 };
        store.AppendSnapshot(second);

        var all = store.GetSnapshots();
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, s => s.Id == first.Id && s.Name == "点二");
        Assert.Contains(all, s => s.Name == "点一");
        Assert.Contains(all, s => s.Name == "点二");
        Assert.NotEqual(first.Id, second.Id);   // 冲突 Id 被重新生成
    }

    [Fact]
    public void Append_DedupsNames()
    {
        var store = Store();
        store.AppendSnapshot(new WidgetSnapshot { Name = "备份", CreatedAt = 1 });
        var second = store.AppendSnapshot(new WidgetSnapshot { Name = "备份", CreatedAt = 2 });
        var third = store.AppendSnapshot(new WidgetSnapshot { Name = "备份", CreatedAt = 3 });

        Assert.Equal("备份", store.GetSnapshots().First(s => s.CreatedAt == 1).Name);
        Assert.Equal("备份 (2)", second.Name);
        Assert.Equal("备份 (3)", third.Name);
    }

    [Fact]
    public void GetSnapshots_OrdersNewestFirst()
    {
        var store = Store();
        store.AppendSnapshot(new WidgetSnapshot { Name = "旧", CreatedAt = 10 });
        store.AppendSnapshot(new WidgetSnapshot { Name = "新", CreatedAt = 30 });
        store.AppendSnapshot(new WidgetSnapshot { Name = "中", CreatedAt = 20 });

        var names = store.GetSnapshots().Select(s => s.Name).ToList();
        Assert.Equal(new[] { "新", "中", "旧" }, names);
    }

    [Fact]
    public void DeleteSnapshot_RemovesOnlyTarget()
    {
        var store = Store();
        var a = store.AppendSnapshot(new WidgetSnapshot { Name = "A", CreatedAt = 1 });
        store.AppendSnapshot(new WidgetSnapshot { Name = "B", CreatedAt = 2 });

        Assert.True(store.DeleteSnapshot(a.Id));
        Assert.False(store.DeleteSnapshot(a.Id));   // 二次删除失败
        var rest = Assert.Single(store.GetSnapshots());
        Assert.Equal("B", rest.Name);
    }

    [Fact]
    public void Summary_CountsWidgetsLocalAndLinks()
    {
        var snap = new WidgetSnapshot
        {
            Entries =
            {
                new WidgetSnapshotEntry
                {
                    Kind = WidgetKind.QuickLaunch,
                    Links = { new LinkItem { Uri = "https://a" }, new LinkItem { Uri = "https://b" } },
                },
                new WidgetSnapshotEntry
                {
                    Kind = WidgetKind.Todo,
                    LocalItems = { new SnapshotLocalItem { Type = ItemType.Todo, Title = "x" }, new SnapshotLocalItem { Type = ItemType.Note, Title = "y" } },
                },
            },
        };
        Assert.Equal("2 个组件 · 2 条待办/随记 · 2 个入口", snap.Summary);
        Assert.Equal("空快照", new WidgetSnapshot().Summary);
    }

    [Fact]
    public void MakeUniqueName_AppendsSuffix()
    {
        var existing = new[] { new WidgetSnapshot { Name = "快照" } };
        Assert.Equal("别的", WidgetSnapshotCollection.MakeUniqueName(existing, "别的"));
        Assert.Equal("快照 (2)", WidgetSnapshotCollection.MakeUniqueName(existing, "快照"));
    }

    [Fact]
    public void EmptySnapshots_AreOmittedFromJson()
    {
        // Normalize 会把 null 归一化为空列表；无快照时字段按 WhenWritingNull 不落盘，
        // 保证给只认旧结构的消费者零噪音。
        var json = JsonSerializer.Serialize(new WidgetStoreData());
        Assert.DoesNotContain("Snapshots", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Layouts", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Snapshot_PreservesFaithfulLocalItemFields()
    {
        // #53 V1：快照须忠实带走标签/置顶/隐藏/笔记/子标题/URI/描述，跨实例还原才不丢用户状态。
        var store = Store();
        var snap = new WidgetSnapshot
        {
            Name = "忠实",
            CreatedAt = 5,
            Entries =
            {
                new WidgetSnapshotEntry
                {
                    Kind = WidgetKind.Todo,
                    LocalItems =
                    {
                        new SnapshotLocalItem
                        {
                            Type = ItemType.Todo, Title = "t",
                            Subtitle = "副", Uri = "u", Description = "d", Notes = "n",
                            Tags = new() { "甲", "乙" },
                            Hidden = true, Pinned = true,
                            ExtraJson = """{"done":true}""",
                        },
                    },
                },
            },
        };

        var reloaded = store.FindSnapshot(store.AppendSnapshot(snap).Id);
        var it = Assert.Single(Assert.Single(reloaded!.Entries).LocalItems);
        Assert.Equal("副", it.Subtitle);
        Assert.Equal("u", it.Uri);
        Assert.Equal("d", it.Description);
        Assert.Equal("n", it.Notes);
        Assert.Equal(new[] { "甲", "乙" }, it.Tags);
        Assert.True(it.Hidden);
        Assert.True(it.Pinned);
        Assert.Contains("\"done\":true", it.ExtraJson);
    }

    [Fact]
    public void Normalize_GuardsNullLinksAndLocalItems()
    {
        // 显式 null（OneDrive 截断 / 手改 JSON 反序列化会覆盖初始化器）经 Normalize 兜底成空集合，
        // 否则 Summary / 捕获 / 应用路径上的 .Count 与 foreach 会 NRE。
        var snap = new WidgetSnapshot
        {
            Name = "脏",
            CreatedAt = 1,
            Entries = { new WidgetSnapshotEntry { Kind = WidgetKind.Todo, Links = null!, LocalItems = null! } },
        };

        var norm = WidgetSnapshotCollection.Normalize(new[] { snap });
        var e = Assert.Single(norm.Single().Entries);
        Assert.NotNull(e.Links);
        Assert.Empty(e.Links);
        Assert.NotNull(e.LocalItems);
        Assert.Empty(e.LocalItems);
        Assert.Contains("1 个组件", snap.Summary);
    }
}
