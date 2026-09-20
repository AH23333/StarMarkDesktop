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

    // ── D5：布局需保存每实例自定义配置；应用时"未修改"者跟随当前主题、"已改"者还原自身外观 ──

    [Fact]
    public void Layout_Entry_PersistsPerWidgetAppearanceOverride()
    {
        var store = Store();
        var layout = new WidgetLayout
        {
            Name = "工作",
            Entries =
            {
                // 已改过外观的组件：材质 + 背景色 + 文本缩放
                new WidgetLayoutEntry
                {
                    Kind = WidgetKind.Clock, Index = 0,
                    X = 100, Y = 120, Width = 260, Height = 180,
                    Appearance = new WidgetAppearanceOverride
                    {
                        Backdrop = StarMark.Abstractions.WidgetBackdropKind.Solid,
                        BackgroundColor = "#FFEFEFEF",
                        TextScale = 1.2,
                    },
                },
                // 未改过的组件：Appearance 保持 null → 应用时跟随当前全局主题
                new WidgetLayoutEntry
                {
                    Kind = WidgetKind.Todo, Index = 0,
                    X = 400, Y = 120, Width = 300, Height = 400,
                },
            },
        };
        store.SaveLayout(layout);

        var reloaded = store.FindLayout(layout.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Entries.Count);

        var modified = reloaded.Entries.First(e => e.Kind == WidgetKind.Clock);
        Assert.NotNull(modified.Appearance);
        Assert.Equal(StarMark.Abstractions.WidgetBackdropKind.Solid, modified.Appearance!.Backdrop);
        Assert.Equal("#FFEFEFEF", modified.Appearance.BackgroundColor);
        Assert.Equal(1.2, modified.Appearance.TextScale!.Value, 3);

        var unmodified = reloaded.Entries.First(e => e.Kind == WidgetKind.Todo);
        Assert.Null(unmodified.Appearance);
    }

    // ── #52：布局 = 纯模板（位置 + 尺寸 + 置顶 + 每组件外观配置），结构上绝不承载条目/待办/随记/入口数据 ──

    [Fact]
    public void Layout_Template_CarriesConfig_ButNeverItemData()
    {
        // 回归护栏：将来若有人往 WidgetLayoutEntry 塞条目/筛选字段，本用例即失败。
        // 数据快照属 #53「保存当前布局与数据」，不得混进纯模板。
        var layout = new WidgetLayout
        {
            Name = "模板",
            Entries =
            {
                new WidgetLayoutEntry
                {
                    Kind = WidgetKind.TagGrid, Index = 0,
                    X = 50, Y = 60, Width = 240, Height = 320, Topmost = true,
                    Appearance = new WidgetAppearanceOverride
                    {
                        Backdrop = StarMark.Abstractions.WidgetBackdropKind.Mica,
                    },
                },
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(layout);

        // 布局与外观配置在场
        Assert.Contains("\"X\":50", json);
        Assert.Contains("\"Y\":60", json);
        Assert.Contains("\"Topmost\":true", json);
        Assert.Contains("Backdrop", json);

        // 条目数据 / 筛选配置绝不在场
        foreach (var forbidden in new[]
                 { "Todos", "Notes", "Links", "SourceId", "GridTag", "GridQuery", "GridSort", "ItemKey", "PrivacyMode", "ChromeMode" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GridSort_RoundTrips_AndDefaultsToNull()
    {
        // 搜索结果格的排序键须与 GridQuery 一样按实例持久化（重启后回填），且 relevance 视为默认不落盘。
        var store = Store();
        var data = store.Load();
        var named = new WidgetInstanceConfig { Kind = WidgetKind.SearchResults, GridQuery = "stars>500", GridSort = "name" };
        var relevance = new WidgetInstanceConfig { Kind = WidgetKind.SearchResults, GridQuery = "rag", GridSort = null };
        data.Instances.Add(named);
        data.Instances.Add(relevance);
        store.Save(data);

        var reloaded = store.Load().Instances;
        Assert.Equal("name", reloaded[0].GridSort);
        Assert.Null(reloaded[1].GridSort);   // null=相关度默认，向后兼容、无迁移
    }

    [Fact]
    public void Save_WhenTargetLockedAtMoveTime_DoesNotThrow_AndLeavesNoTemp()
    {
        // 非降级态下（本次 Store 的 Load 未触发降级），Save 的写入/搬移若因目标被独占而失败
        // （OneDrive 同步 / 杀软实时扫描短暂锁定 widgets.json），必须被吞掉不外抛——
        // 它经 WidgetManager.OnUiAsync 在 UI 线程同步内联执行，抛出会直接闪退整个应用。
        // 用 FileShare.None 独占句柄锁住 _path：WriteAllText(.tmp) 成功、File.Move→_path 必抛 IOException。
        var store = Store();
        var data = store.Load();                                   // 文件此刻还不存在 → 空态、degraded=false
        data.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Clock });
        using var hold = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var ex = Record.Exception(() => store.Save(data));         // 关键：Save 不得抛出
        Assert.Null(ex);
        Assert.False(File.Exists(_path + ".tmp"));                 // 失败路径应清理临时文件，不留残渣
    }

    [Fact]
    public void TransientLockAtLoad_BlocksSubsequentSaveFromWipingDiskData()
    {
        // 真实数据丢失场景（R10#1）：widgets.json 已存在且合法，但某一刻被 OneDrive/杀软独占锁定。
        // Load 因 IOException 读不到 → 旧实现吞异常返回空、随后的 Save 会用**空数据覆盖真实配置**，全量组件被抹掉。
        // 修复：读到失败进入降级态 → Save 拒绝落盘（改动丢失远好于全量清空）；某次成功 Load 会自动复位、恢复写入。
        var seeded = Store();
        var seed = seeded.Load();
        seed.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Clock, X = 777 });
        seeded.Save(seed);
        var originalText = File.ReadAllText(_path);
        Assert.Contains("777", originalText);

        // 模拟"读到空 → 就地改动 → 保存"的同一实例链路（如 AppendSnapshot：Load()→mutate→Save(data)）。
        var store = Store();
        using (var hold = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var degraded = store.Load();                            // 读到被占用 → 降级、返回空
            Assert.Empty(degraded.Instances);
        }                                                           // 释放锁；store 仍停留在降级态（未再成功 Load）

        var wipingData = WidgetStorage.Normalize(null);            // 相当于那次降级读到的空数据
        var ex = Record.Exception(() => store.Save(wipingData));
        Assert.Null(ex);
        Assert.Equal(originalText, File.ReadAllText(_path));       // 磁盘原样保留，未被空数据抹掉

        // 成功 Load 复位后，同一实例的 Save 应恢复正常落盘（证明拒绝只是临时保护、不是永久锁死）。
        var healed = store.Load();
        Assert.Single(healed.Instances);
        healed.Instances[0].X = 999;
        store.Save(healed);
        Assert.Contains("999", File.ReadAllText(_path));           // 写入已恢复
    }

    // ── AQ-1：instances 数组含显式 null 元素不得把整份组件持久化反向锁死 ──

    [Fact]
    public void Normalize_DropsNullInstanceElements()
    {
        // 兄弟集合（Todos/Notes/Links、Layouts/Snapshots）都滤 null，唯独实例数组的元素没滤。
        // 旧实现在 foreach 里对 null 元素取 inst.Todos 抛 NRE。
        var data = WidgetStorage.Normalize(null);
        data.Instances.Add(null!);
        data.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Todo });

        var ex = Record.Exception(() => WidgetStorage.Normalize(data));
        Assert.Null(ex);                    // 旧实现此处 NRE
        Assert.Single(data.Instances);      // null 元素被剔除，合法实例保留
        Assert.NotNull(data.Instances[0]);
    }

    [Fact]
    public void NullInstanceElement_LoadDoesNotThrow_AndKeepsPersistenceUsable()
    {
        // 端到端：坏写入/手改/OneDrive 截断让 widgets.json 的 instances 含显式 null。
        // 旧实现 NRE 落在 Load 的 try 内、被 catch-all 当作「临时占用」→ _loadDegraded 永久置位，
        // 后续每次 Save 被静默跳过（持久化整体锁死），且每次 Load 恒返回空。
        File.WriteAllText(_path, """{ "Version": 3, "Instances": [ null ] }""");
        var store = Store();

        var data = store.Load();                                    // 不得抛
        Assert.Empty(data.Instances);                              // null 被剔除，且未误入降级态

        data.Instances.Add(new WidgetInstanceConfig { Kind = WidgetKind.Clock, X = 999 });
        store.Save(data);                                           // 若降级位被误置，这里会被跳过
        Assert.Contains("999", File.ReadAllText(_path));            // 证明持久化仍可用
    }
}

