#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 快照「采集 ⇄ 还原」字段对称守门（批次 MM 新增）。
/// <para>
/// 给组件加一份按实例持久化的内容或配置时，要同时改三处：<c>WidgetInstanceConfig</c> 字段、
/// <c>WidgetSnapshotEntry</c> 字段、以及 <c>WidgetManager</c> 里的采集与还原两句。
/// <b>漏掉还原那一句的现象是"应用快照后这项设置没回到当时"</b>——不崩、不报错，构建与既有测试全绿，
/// 只有用户会在还原后发现"番茄钟时长还是现在这个"。番茄钟的时长当初就是这么漏掉一次的。
/// </para>
/// <para>
/// 与工厂守门同一做法：测试工程引用不到 <c>StarMark.UI</c>，所以读源文件比对两侧的字段名集合。
/// </para>
/// </summary>
public sealed class WidgetSnapshotSymmetryGateTests
{
    private const string ManagerRelativePath = "src/StarMark.UI/Services/WidgetManager.cs";

    /// <summary>
    /// 不参与"采集 ⇄ 还原"比对的字段，各有明说得出口的理由：
    /// <list type="bullet">
    /// <item><c>Kind</c>：用来找对应实例的身份，不是要写回的内容；</item>
    /// <item><c>X/Y/Width/Height/Topmost/Appearance</c>：几何与外观由 <c>ApplyGeometryCore</c> 统一落位
    /// （与"纯模板"布局共用同一条路径），所以出现在那边而不是这里——那是分工，不是漏网。</item>
    /// </list>
    /// </summary>
    private static readonly string[] ComparedElsewhere =
    [
        nameof(StarMark.Core.Widgets.WidgetSnapshotEntry.Kind),
        "X", "Y", "Width", "Height",
        nameof(StarMark.Core.Widgets.WidgetInstanceConfig.Topmost),
        nameof(StarMark.Core.Widgets.WidgetSnapshotEntry.Appearance),
    ];

    private static string ReadManagerSource()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, ManagerRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException($"未找到 {ManagerRelativePath}（守门失效比红测更危险，故直接抛）");
    }

    [Fact]
    public void Snapshot_CapturesExactlyWhatRestoreApplies()
    {
        var source = ReadManagerSource();

        // 只认"行首的赋值"：采集那一侧是对象初始化器（Links = inst.Links.Select(...)），
        // 还原那一侧是逐条语句（inst.Links = entry.Links.Select(...)）——不加行首锚定的话
        // instanceIds.Add(inst.Id) 这类调用会被当成"采集了 Id"，守门就变成误报制造机。
        var captured = Regex.Matches(source, @"^[ 	]*(?<field>[A-Za-z]\w*) = inst\.(?<target>[A-Za-z]\w*)", RegexOptions.Multiline)
            .Select(m => m.Groups["target"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var restored = Regex.Matches(source, @"^[ 	]*inst\.(?<target>[A-Za-z]\w*) = entry\.(?<field>[A-Za-z]\w*)", RegexOptions.Multiline)
            .Select(m => m.Groups["target"].Value)
            .ToHashSet(StringComparer.Ordinal);

        captured.ExceptWith(ComparedElsewhere);
        restored.ExceptWith(ComparedElsewhere);

        // 正向对照：两侧都真的扫到了字段，否则"零差异"只是扫描失效
        Assert.True(captured.Count >= 8, $"采集侧只扫到 {captured.Count} 个字段，扫描可能已失效：{string.Join(",", captured)}");
        Assert.True(restored.Count >= 8, $"还原侧只扫到 {restored.Count} 个字段，扫描可能已失效：{string.Join(",", restored)}");

        var onlyCaptured = captured.Where(f => !restored.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var onlyRestored = restored.Where(f => !captured.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(onlyCaptured.Count == 0 && onlyRestored.Count == 0,
            "快照采集与还原的字段不再对称（漏一句＝应用快照后该项悄悄不回落）。\n"
            + $"  只采集不还原：{string.Join(",", onlyCaptured)}\n"
            + $"  只还原不采集：{string.Join(",", onlyRestored)}");
    }
}
