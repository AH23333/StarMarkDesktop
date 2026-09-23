#nullable enable
using System.Text.RegularExpressions;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 组件接入完整性守门（踩坑 #47「三处同步注册」的机检版本）。
/// <para>
/// 新组件要同时改三处：<c>WidgetKind</c> 枚举、<c>WidgetRegistry</c> 描述符、<c>WidgetContentFactory</c> 构建委托。
/// 前两处已有测试覆盖（<c>EveryEnumValue_HasDescriptor</c>），<b>第三处一直没有</b>——工厂在 <c>StarMark.UI</c>，
/// 测试工程引用不到它，于是漏登记的后果是运行时渲染出一块「"暂未实现"」文本，构建与测试全绿。
/// 本守门照 <c>ThemeRulesGateTests</c> 的做法<em>读源文件</em>来补上这一格。
/// </para>
/// </summary>
public sealed class WidgetFactoryGateTests
{
    private const string FactoryRelativePath = "src/StarMark.UI/Services/WidgetContentFactory.cs";

    private static string ReadFactorySource()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, FactoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException($"未找到 {FactoryRelativePath}（守门失效比红测更危险，故直接抛）");
    }

    [Fact]
    public void EveryCreatableKind_IsBuiltByTheFactory()
    {
        var source = ReadFactorySource();
        var registered = Regex.Matches(source, @"Register\(\s*WidgetKind\.(?<kind>\w+)")
            .Select(m => m.Groups["kind"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = WidgetRegistry.Default.GetWindowDescriptors()
            .Select(d => d.Kind.ToString())
            .Where(name => !registered.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0,
            "这些组件在注册表里可创建，却没有工厂构建委托（用户会看到「暂未实现」）：" + string.Join(", ", missing));
        // 守门自身也不能空转：注册表若被整批改没，上面那条断言会静默变绿
        Assert.True(registered.Count >= 12, $"工厂登记数异常偏少（{registered.Count}）——是被整批删掉了？");
    }

    [Fact]
    public void Factory_RegistersOnlyKnownKinds()
    {
        var source = ReadFactorySource();
        var unknown = Regex.Matches(source, @"Register\(\s*WidgetKind\.(?<kind>\w+)")
            .Select(m => m.Groups["kind"].Value)
            .Where(name => !Enum.TryParse<WidgetKind>(name, out var k) || !WidgetRegistry.Default.IsKnown(k))
            .ToList();

        Assert.True(unknown.Count == 0,
            "工厂登记了注册表里没有的类型（描述符缺失会让窗口取默认尺寸/标题时抛或回退）：" + string.Join(", ", unknown));
    }

    /// <summary>
    /// 类型清单是**按整数**落进 widgets.json 的，所以序号一旦中途变动，老用户的实例会静默变成别的组件。
    /// 这里把 0..12 全序列钉死（追加只能往后），与 TrayWidgetMenuTests 的抽查互补。
    /// </summary>
    [Fact]
    public void WidgetKind_WireValuesAreFrozenAndAppendOnly()
    {
        var expected = new (WidgetKind Kind, int Wire)[]
        {
            (WidgetKind.QuickLaunch, 0), (WidgetKind.Todo, 1), (WidgetKind.QuickNote, 2),
            (WidgetKind.Clock, 3), (WidgetKind.Search, 4), (WidgetKind.TagGrid, 5),
            (WidgetKind.Clipboard, 6), (WidgetKind.Activity, 7), (WidgetKind.Pinned, 8),
            (WidgetKind.Glance, 9), (WidgetKind.Weather, 10), (WidgetKind.Music, 11),
            (WidgetKind.Calc, 12), (WidgetKind.WorldClock, 13), (WidgetKind.Countdown, 14),
        };
        foreach (var (kind, wire) in expected) Assert.Equal(wire, (int)kind);

        // 追加的下一位必须紧接着，不能有人在中间插值
        Assert.Equal(expected.Length, Enum.GetValues<WidgetKind>().Length);
    }
}
