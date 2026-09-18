#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 桌面组件存储测试点（DeskBox 式独立组件窗口的持久化契约，v3 多实例模型）：
/// 全新安装默认不启用任何实例 / 实例集合与窗口配置持久化 / 同类型可重复添加多个实例 /
/// 待办·随记·入口增删与排序规范（按实例隔离）/ v1 单面板迁移（迁移为多个实例）/
/// 损坏文件容错 / 原子写入。
/// </summary>
public sealed class WidgetStorageTests : IDisposable
{
    private readonly string _path;

    public WidgetStorageTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"starmark_widgets_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose() { try { File.Delete(_path); } catch { } }

    private WidgetStorage Store() => new(_path);

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var data = Store().Load();
        Assert.Equal(3, data.Version);
        Assert.Empty(data.Instances);          // 全新安装不弹任何组件，由用户主动添加
        // 未保存过的组件取类型默认尺寸
        Assert.Equal(320, WidgetStorage.DefaultWidth(WidgetKind.QuickLaunch));
        Assert.Equal(460, WidgetStorage.DefaultHeight(WidgetKind.QuickLaunch));
    }

    [Fact]
    public void Instances_RoundTrip()
    {
        var store = Store();
        var data = store.Load();
        var clock = new WidgetInstanceConfig { Kind = WidgetKind.Clock, X = 480, Y = 260, Width = 260, Height = 180, Topmost = true };
        var quick = new WidgetInstanceConfig { Kind = WidgetKind.QuickLaunch };
        data.Instances.Add(clock);
        data.Instances.Add(quick);
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(2, reloaded.Instances.Count);
        var clockReloaded = reloaded.Instances.First(i => i.Kind == WidgetKind.Clock);
        Assert.Equal(480, clockReloaded.X);
        Assert.True(clockReloaded.Topmost);
        Assert.Contains(reloaded.Instances, i => i.Kind == WidgetKind.QuickLaunch);
    }

    [Fact]
    public void Instance_PrivacyMode_DefaultsFalseAndRoundTrips()
    {
        var store = Store();
        // 全新实例默认 false（旧实例缺字段零迁移）
        var inst = new WidgetInstanceConfig { Kind = WidgetKind.Todo };
        Assert.False(inst.PrivacyMode);

        var data = store.Load();
        data.Instances.Add(inst);
        store.Save(data);

        var reloaded = store.Load().Instances[0];
        Assert.False(reloaded.PrivacyMode);

        // 开启隐私模式后落盘往返保持 true
        reloaded.PrivacyMode = true;
        var d2 = store.Load();
        d2.Instances[0].PrivacyMode = true;
        store.Save(d2);
        Assert.True(store.Load().Instances[0].PrivacyMode);
    }

    [Fact]
    public void Instances_AllowMultipleOfSameKind()
    {
        var store = Store();
        var data = store.Load();
        data.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Todo });
        data.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Todo });
        data.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Clock });
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(3, reloaded.Instances.Count);
        Assert.Equal(2, reloaded.Instances.Count(i => i.Kind == WidgetKind.Todo));
        Assert.Single(reloaded.Instances, i => i.Kind == WidgetKind.Clock);
    }

    [Fact]
    public void Todos_PersistSortedAndFiltered()
    {
        var store = Store();
        var data = store.Load();
        var inst = new WidgetInstanceConfig { Kind = WidgetKind.Todo };
        inst.Todos.Add(new TodoItem { Id = 1, Text = "done old", Done = true, CreatedAt = 100 });
        inst.Todos.Add(new TodoItem { Id = 2, Text = "open new", CreatedAt = 300 });
        inst.Todos.Add(new TodoItem { Id = 3, Text = "done new", Done = true, CreatedAt = 200 });
        inst.Todos.Add(new TodoItem { Id = 4, Text = "   " }); // 空文本应被剔除
        data.Instances.Add(inst);
        store.Save(data);

        var reloaded = store.Load();
        var rt = reloaded.Instances[0].Todos;
        Assert.Equal(3, rt.Count);
        // 未完成在前（按时间倒序），已完成在后（按时间倒序）
        Assert.Equal("open new", rt[0].Text);
        Assert.Equal("done new", rt[1].Text);
        Assert.Equal("done old", rt[2].Text);
    }

    [Fact]
    public void Notes_PersistNewestFirst()
    {
        var store = Store();
        var data = store.Load();
        var inst = new WidgetInstanceConfig { Kind = WidgetKind.QuickNote };
        inst.Notes.Add(new QuickNoteItem { Text = "第一条", CreatedAt = 100 });
        inst.Notes.Add(new QuickNoteItem { Text = "第二条", CreatedAt = 200 });
        data.Instances.Add(inst);
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(2, reloaded.Instances[0].Notes.Count);
        Assert.Equal("第二条", reloaded.Instances[0].Notes[0].Text);
    }

    [Fact]
    public void Links_PersistNewestFirstAndDropInvalid()
    {
        var store = Store();
        var data = store.Load();
        var inst = new WidgetInstanceConfig { Kind = WidgetKind.QuickLaunch };
        inst.Links.Add(new LinkItem { Title = "a", Uri = "https://a", CreatedAt = 100 });
        inst.Links.Add(new LinkItem { Title = "b", Uri = "https://b", CreatedAt = 200 });
        inst.Links.Add(new LinkItem { Title = "bad", Uri = "   " });
        data.Instances.Add(inst);
        store.Save(data);

        var reloaded = store.Load();
        var rl = reloaded.Instances[0].Links;
        Assert.Equal(2, rl.Count);
        Assert.Equal("https://b", rl[0].Uri);
    }

    [Fact]
    public void LegacyV1_ShowOnStartup_MigratesToAllKindsAsInstances()
    {
        File.WriteAllText(_path, """
        {
          "Version": 1,
          "Config": { "X": 100, "Y": 200, "Width": 320, "Height": 620, "ShowOnStartup": true },
          "ShowOnStartup": true,
          "Todos": [],
          "Notes": []
        }
        """);
        var data = Store().Load();
        Assert.Equal(3, data.Version);
        Assert.Equal(WidgetStorage.AllKinds.Count, data.Instances.Count);
        // 每种组件各迁移出一个实例
        foreach (var kind in WidgetStorage.AllKinds)
            Assert.Contains(data.Instances, i => i.Kind == kind);
        // 旧面板位置迁移给快捷启动格实例
        Assert.Equal(100, data.Instances.First(i => i.Kind == WidgetKind.QuickLaunch).X);
        // 遗留字段已清空
        Assert.Null(data.LegacyShowOnStartup);
        Assert.Null(data.LegacyConfig);
    }

    [Fact]
    public void LegacyV1_ShowOnStartupInsideConfig_Migrates()
    {
        // 真实 v1 文件的开关嵌在 Config 内
        File.WriteAllText(_path, """
        {
          "Version": 1,
          "Config": { "X": 100, "Y": 200, "Width": 320, "Height": 620, "ShowOnStartup": true },
          "Todos": [],
          "Notes": []
        }
        """);
        var data = Store().Load();
        Assert.Equal(WidgetStorage.AllKinds.Count, data.Instances.Count);
        Assert.Equal(100, data.Instances.First(i => i.Kind == WidgetKind.QuickLaunch).X);
    }

    [Fact]
    public void LegacyV1_ShowOnStartupFalse_StaysEmpty()
    {
        File.WriteAllText(_path, """
        {
          "Version": 1,
          "Config": { "X": 1, "Y": 2, "Width": 320, "Height": 620 },
          "ShowOnStartup": false,
          "Todos": [],
          "Notes": []
        }
        """);
        var data = Store().Load();
        Assert.Empty(data.Instances);
    }

    [Fact]
    public void CorruptedFile_FallsBackToDefaults()
    {
        File.WriteAllText(_path, "{ not valid json !!!");
        var data = Store().Load();
        Assert.Empty(data.Instances);
        Assert.Equal(3, data.Version);
    }

    [Fact]
    public void Save_IsAtomic_LeavesNoTempFile()
    {
        var store = Store();
        store.Save(store.Load());
        store.Save(store.Load()); // 覆盖已有文件
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }
}
