#nullable enable
using System;
using System.IO;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 桌面组件存储测试点（DeskBox 式独立组件窗口的持久化契约）：
/// 全新安装默认不启用任何组件 / 启用集合与窗口配置持久化 / 待办·随记·入口增删与排序规范 /
/// v1 单面板迁移 / 损坏文件容错 / 原子写入。
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
        Assert.Equal(2, data.Version);
        Assert.Empty(data.Enabled);            // 全新安装不弹任何组件，由用户主动添加
        Assert.Empty(data.Todos);
        Assert.Empty(data.Notes);
        Assert.Empty(data.Links);
        // 未保存过的组件取类型默认尺寸
        var cfg = Store().GetConfig(data, WidgetKind.QuickLaunch, 0);
        Assert.Equal(320, cfg.Width);
        Assert.False(cfg.Topmost);
    }

    [Fact]
    public void EnabledAndWindowConfig_RoundTrip()
    {
        var store = Store();
        var data = store.Load();
        data.Enabled.Add(WidgetKind.Clock);
        data.Enabled.Add(WidgetKind.QuickLaunch);
        data.WindowConfigs[WidgetKind.Clock.ToString()] = new WidgetConfig
        {
            X = 480, Y = 260, Width = 260, Height = 180, Topmost = true,
        };
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(2, reloaded.Enabled.Count);
        Assert.Contains(WidgetKind.Clock, reloaded.Enabled);
        Assert.Equal(new[] { WidgetKind.QuickLaunch, WidgetKind.Clock }, reloaded.Enabled);
        var clock = reloaded.WindowConfigs[WidgetKind.Clock.ToString()];
        Assert.Equal(480, clock.X);
        Assert.True(clock.Topmost);
    }

    [Fact]
    public void Enabled_Normalized_DeduplicatedAndSorted()
    {
        var store = Store();
        var data = store.Load();
        data.Enabled.Add(WidgetKind.Todo);
        data.Enabled.Add(WidgetKind.Todo);
        data.Enabled.Add(WidgetKind.Clock);
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(new[] { WidgetKind.Todo, WidgetKind.Clock }, reloaded.Enabled);
    }

    [Fact]
    public void Todos_PersistSortedAndFiltered()
    {
        var store = Store();
        var data = store.Load();
        data.Todos.Add(new TodoItem { Id = 1, Text = "done old", Done = true, CreatedAt = 100 });
        data.Todos.Add(new TodoItem { Id = 2, Text = "open new", CreatedAt = 300 });
        data.Todos.Add(new TodoItem { Id = 3, Text = "done new", Done = true, CreatedAt = 200 });
        data.Todos.Add(new TodoItem { Id = 4, Text = "   " }); // 空文本应被剔除
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(3, reloaded.Todos.Count);
        // 未完成在前（按时间倒序），已完成在后（按时间倒序）
        Assert.Equal("open new", reloaded.Todos[0].Text);
        Assert.Equal("done new", reloaded.Todos[1].Text);
        Assert.Equal("done old", reloaded.Todos[2].Text);
    }

    [Fact]
    public void Notes_PersistNewestFirst()
    {
        var store = Store();
        var data = store.Load();
        data.Notes.Add(new QuickNoteItem { Text = "第一条", CreatedAt = 100 });
        data.Notes.Add(new QuickNoteItem { Text = "第二条", CreatedAt = 200 });
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(2, reloaded.Notes.Count);
        Assert.Equal("第二条", reloaded.Notes[0].Text);
    }

    [Fact]
    public void Links_PersistNewestFirstAndDropInvalid()
    {
        var store = Store();
        var data = store.Load();
        data.Links.Add(new LinkItem { Title = "a", Uri = "https://a", CreatedAt = 100 });
        data.Links.Add(new LinkItem { Title = "b", Uri = "https://b", CreatedAt = 200 });
        data.Links.Add(new LinkItem { Title = "bad", Uri = "   " });
        store.Save(data);

        var reloaded = store.Load();
        Assert.Equal(2, reloaded.Links.Count);
        Assert.Equal("https://b", reloaded.Links[0].Uri);
    }

    [Fact]
    public void LegacyV1_ShowOnStartup_MigratesToAllKindsEnabled()
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
        Assert.Equal(2, data.Version);
        Assert.Equal(WidgetStorage.AllKinds.Count, data.Enabled.Count);
        // 旧面板位置迁移给快捷启动格
        Assert.Equal(100, data.WindowConfigs[WidgetKind.QuickLaunch.ToString()].X);
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
        Assert.Equal(WidgetStorage.AllKinds.Count, data.Enabled.Count);
        Assert.Equal(100, data.WindowConfigs[WidgetKind.QuickLaunch.ToString()].X);
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
        Assert.Empty(data.Enabled);
    }

    [Fact]
    public void CorruptedFile_FallsBackToDefaults()
    {
        File.WriteAllText(_path, "{ not valid json !!!");
        var data = Store().Load();
        Assert.Empty(data.Enabled);
        Assert.Equal(2, data.Version);
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
