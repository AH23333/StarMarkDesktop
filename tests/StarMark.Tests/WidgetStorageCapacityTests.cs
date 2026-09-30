#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 组件档的<b>容量</b>纪律（P-10，批次 SL）：<b>读一次不该少几条</b>。
/// <para><see cref="WidgetStorage.Normalize"/> 同时被 <c>Load()</c> 与 <c>Save()</c> 调用，
/// 所以从前挂在那里的三道 `Take(200/100/100)` 实际是"读路径上的删除"：<c>Load()</c> 交出去的内存模型已经被裁，
/// 随后任何 <c>Load()→改→Save()</c> 都把超出部分当作从来不存在，而且<b>丢的时候一声不响</b>：
/// 常规 Save 不落 <c>.bak</c>、日志一个字都没有、界面上那条只是"没了"。
/// 唯一的挽回面是更早的一次<b>备份导出</b>（备份信封连着存 widgets.json 原文），且回档会把整套组件状态一起退回去。
/// 最现实的触发面是快捷启动格攒到 101 条之后，"发送到快捷启动"每多一条就静默删掉最旧一条。</para>
/// <para>所以这一族钉的是两件事：<b>①条数一条不丢</b>（含 Load→Save 真往返）、
/// <b>②排序与滤脏照旧</b>（不然"删限量"会被下一个人顺手连排序一起删掉，那是另一种静默改变行为）。
/// 真要给用户内容设顶，做的应是看得见的清理入口，而不是让一次读取替用户做决定。</para>
/// </summary>
public sealed class WidgetStorageCapacityTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public WidgetStorageCapacityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "starmark_capacity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "widgets.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static WidgetStoreData DataWith(int todos, int notes, int links)
    {
        var inst = new WidgetInstanceConfig
        {
            Kind = WidgetKind.QuickLaunch,
            Title = "快捷启动",
            Id = "inst-1",
        };
        // 故意把"最早那一条"排在下标 0：限量若还挂着，被删的就是它——断言它还在才算钉住"不丢"。
        for (var i = 0; i < todos; i++)
            inst.Todos.Add(new TodoItem { Id = 1000 + i, Text = $"待办 {i}", Done = i % 5 == 0, CreatedAt = 100 + i });
        for (var i = 0; i < notes; i++)
            inst.Notes.Add(new QuickNoteItem { Id = 2000 + i, Text = $"随记 {i}", CreatedAt = 100 + i });
        for (var i = 0; i < links; i++)
            inst.Links.Add(new LinkItem { Id = 3000 + i, Title = $"链接 {i}", Uri = $"file:///d/{i}", CreatedAt = 100 + i });
        return new WidgetStoreData { Instances = { inst } };
    }

    private static WidgetInstanceConfig Only(WidgetStoreData data) => Assert.Single(data.Instances);

    // ────────── 判据本体：一条不丢 ──────────

    [Fact]
    public void NormalizeKeepsEveryItemBeyondTheOldCaps()
    {
        var normalized = WidgetStorage.Normalize(DataWith(todos: 201, notes: 101, links: 101));
        var inst = Only(normalized);

        Assert.Equal(201, inst.Todos.Count);
        Assert.Equal(101, inst.Notes.Count);
        Assert.Equal(101, inst.Links.Count);
        // 最早那一条必须在（它正是从前被"最旧优先删掉"的那一条）
        Assert.Equal("待办 0", inst.Todos[^1].Text);              // 未完成在前 ⇒ 已完成的第 0 条落到末尾
        Assert.Equal("随记 0", inst.Notes[^1].Text);
        Assert.Equal("file:///d/0", inst.Links[^1].Uri);
    }

    [Fact]
    public void NormalizeStillFiltersAndOrdersTheSameWay()
    {
        var data = DataWith(todos: 0, notes: 0, links: 0);
        var inst = Only(data);
        inst.Todos.Add(new TodoItem { Text = "未完成-旧", Done = false, CreatedAt = 10 });
        inst.Todos.Add(new TodoItem { Text = "已完成-新", Done = true, CreatedAt = 999 });
        inst.Todos.Add(new TodoItem { Text = "未完成-新", Done = false, CreatedAt = 20 });
        inst.Todos.Add(null!);
        inst.Todos.Add(new TodoItem { Text = "   ", CreatedAt = 30 });     // 空白正文＝脏行，照旧滤掉
        inst.Notes.Add(new QuickNoteItem { Text = "", CreatedAt = 1 });    // 空正文滤掉
        inst.Links.Add(new LinkItem { Uri = "", CreatedAt = 1 });          // 没地址的链接滤掉

        var normalized = WidgetStorage.Normalize(data);
        var kept = Only(normalized);

        Assert.Equal(new[] { "未完成-新", "未完成-旧", "已完成-新" }, kept.Todos.Select(t => t.Text));
        Assert.Empty(kept.Notes);
        Assert.Empty(kept.Links);
    }

    [Fact]
    public void LoadThenSaveKeepsTheHundredAndFirstLink()
    {
        // 这一条才是 P-10 的形状：缺陷不在"某一处少显示一条"，而在"读→改→写"这一趟把数据洗掉。
        File.WriteAllText(_path, JsonSerializer.Serialize(DataWith(todos: 5, notes: 101, links: 101)));

        var loaded = new WidgetStorage(_path).Load();
        Assert.Equal(101, Only(loaded).Links.Count);
        Assert.Equal(101, Only(loaded).Notes.Count);

        // 用户动作：往快捷启动里再加一条（今天没有任何上限检查，见 WidgetManager.QuickLaunch 的 AddLinkAsync）
        Only(loaded).Links.Add(new LinkItem { Id = 4000, Title = "新一条", Uri = "file:///d/new", CreatedAt = 9999 });
        new WidgetStorage(_path).Save(loaded);

        Assert.Equal(102, RawArrayLength("Links"));
        Assert.Equal(101, RawArrayLength("Notes"));                 // 别的桶也不该被这一趟写盘顺手裁掉
        Assert.Equal(5, RawArrayLength("Todos"));
    }

    [Fact]
    public void ASecondRoundTripDoesNotShrinkItFurther()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(DataWith(todos: 201, notes: 5, links: 5)));

        var store = new WidgetStorage(_path);
        var first = store.Load();
        store.Save(first);
        var second = new WidgetStorage(_path).Load();

        Assert.Equal(201, Only(second).Todos.Count);
        Assert.Equal(201, Only(first).Todos.Count);
    }

    private int RawArrayLength(string property)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        return doc.RootElement.GetProperty("Instances")[0].GetProperty(property).GetArrayLength();
    }

    // ────────── 闸门：读路径上不许再有删除 ──────────

    [Fact]
    public void StorageLayerHasNoCountBasedCutAnywhere()
    {
        // 只钉 Normalize 的方法体是不够的：把 `Take(100)` 挪进隔壁一个私有方法、在 Load 里调它，
        // 方法体闸门照样绿。所以禁项按<b>整个存储层文件</b>扫（抹掉注释后的代码，#195/#203 同族口径）。
        var code = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Core/Widgets/WidgetStorage.cs"));

        foreach (var banned in new[] { "Take(", "Skip(", "RemoveRange(" })
            Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizationStillFiltersAndOrders()
    {
        // 上一条的反面：禁项不能靠"扫不到东西"来绿。规范化那一段还在、滤脏与排序还在，
        // 才说明"没有 Take"是因为不设顶，而不是因为整段被删了（#194/#199：空转的闸门不守东西）。
        var body = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Core/Widgets/WidgetStorage.cs"),
            "public static WidgetStoreData Normalize"));

        foreach (var required in new[] { ".Where(", ".OrderBy" })
            Assert.Contains(required, body, StringComparison.Ordinal);
        // 三份内容各自都还在滤 null 元素（曾被一个 null 永久锁死整条读路径，见方法体注释）。
        // 按"变量名 + is not null"数而不是数整句：既证明三段都在，又不会因为谓词改措辞就误伤。
        foreach (var bucket in new[] { "t", "n", "l" })
            Assert.Equal(1, SourceGate.Count(body, bucket + " is not null"));
    }

    [Fact]
    public void ReadAndWriteStillShareTheOneNormalizer()
    {
        // "读路径不裁"之所以成立，是因为读与写共用同一颗 Normalize：把裁量搬去"只挂 Save"
        // 会留下 Load→改→Save 的洗数据路径（正是本批消掉的那件事），所以这里钉"共用没散"。
        var file = SourceGate.ReadRepoFile("src/StarMark.Core/Widgets/WidgetStorage.cs");
        var load = SourceGate.Code(SourceGate.MethodBody(file, "public WidgetStoreData Load"));
        var save = SourceGate.Code(SourceGate.MethodBody(file, "public bool Save"));

        Assert.Equal(1, SourceGate.Count(load, "Normalize(data)"));
        Assert.Equal(1, SourceGate.Count(save, "Normalize(data)"));
        // 整文件只有这两处走"整档规范化"：出现第三处＝读侧或写侧另起了一份，下一次只改到其中一份
        Assert.Equal(2, SourceGate.Count(SourceGate.Code(file), "Normalize(data)"));
    }
}
