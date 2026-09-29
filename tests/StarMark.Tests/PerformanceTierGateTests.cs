#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 性能档位（内存预算 / 缓存条数）的<b>范围与默认只许有一个主人</b>的形状闸门（批次 SA，P-123 第 3 条）。
/// <para>
/// <b>这批收的不是"重复代码"，是一次已经发生的事实分岔</b>：同一个旋钮的量程，
/// 仓储写 <c>32–4096</c>、<see cref="StarMark.Core.Performance.PerformanceSettingsPolicy"/> 的预设写 384/512/160/128、
/// 设置页 VM 的兜底写 200/256（字段初值又写一遍），而 XAML 那两根滑杆写 <c>32–2048</c> 与 <c>16–1024</c>。
/// ⇒ 存档里一个<b>合法且真在生效</b>的预算（3000 MB）在设置页<b>表达不出来</b>；界面与真值各拿一份范围，
/// 漂移只是时间问题（#175）。判据收进 <c>PerformanceSettingsPolicy</c> 之后，本闸门钉住"别人不再自己写一个"。
/// </para>
/// <para>
/// 与 RZ 同一条限制：判据在 Core、被测的写法在 UI 工程，而 <c>StarMark.Tests</c> 不引用 <c>StarMark.UI</c>（#184）
/// ⇒ 这里全是<b>形状判据</b>；真行为那半（<c>Normalize*</c> 的边界、预设落在量程内）在
/// <see cref="PerformanceTests"/>，那颗判据在 Core 所以出得了真测。
/// </para>
/// </summary>
public sealed class PerformanceTierGateTests
{
    private const string JudgeFile = "src/StarMark.Core/Performance/PerformanceSettingsPolicy.cs";
    private const string StoreFile = "src/StarMark.UI/Helpers/SettingsStore.Performance.cs";
    private const string ViewModelFile = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string XamlFile = "src/StarMark.UI/Views/SettingsPage.xaml";
    private static readonly string[] BoundSliders = ["ViewModel.CacheBudgetMb", "ViewModel.MaxCacheCount"];

    // ────────── ① 仓储不再自己写边界 ──────────

    /// <summary>
    /// 读档与落盘都要走 <c>Normalize*</c>。<b>为什么禁 <c>Math.Clamp</c> 而不是禁某个数字</b>：
    /// 数字改一个字就漂移，而"这里有没有第二份夹取逻辑"才是这批要防的动作形状。
    /// </summary>
    [Fact]
    public void StoreClampsThroughTheJudgeNotItsOwnMath()
    {
        var raw = ReadRepoFile(StoreFile);
        Assert.DoesNotContain("Math.Clamp(", raw, StringComparison.Ordinal);
        Assert.Contains("NormalizeBudgetMb", raw, StringComparison.Ordinal);
        Assert.Contains("NormalizeCacheCount", raw, StringComparison.Ordinal);
        // 反空转：兜底也必须是判据那颗，不能留一个字面量默认值
        Assert.DoesNotContain("return 200", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("return 256", raw, StringComparison.Ordinal);
    }

    // ────────── ② 界面上那两根滑杆不许自带量程 ──────────

    /// <summary>
    /// 只钉<b>绑到这两个属性</b>的 Slider（设置页里还有别处 Slider，它们的量程是另一件事，不在这批范围）。
    /// <para>抽取本身由 <see cref="SliderExtractorSeesTheBoundOnesOnly"/> 自证——
    /// 抽不到的闸门会靠"零命中"冒充绿灯（#161）。</para>
    /// </summary>
    [Fact]
    public void BoundSlidersTakeTheirRangeFromTheJudge()
    {
        var offenders = SliderElements()
            .Where(s => BoundSliders.Any(b => s.Contains(b, StringComparison.Ordinal)))
            .SelectMany(s => new[] { "Minimum", "Maximum", "StepFrequency" }
                .Select(a => (Slider: s, Attr: a, Value: AttrValue(s, a))))
            .Where(x => x.Value is not null && !x.Value!.StartsWith("{x:Bind", StringComparison.Ordinal))
            .Select(x => $"{x.Attr}=\"{x.Value}\"")
            .ToList();

        Assert.True(offenders.Count == 0,
            "性能档位那两根滑杆又自己写量程了（该绑 PerformanceSettingsPolicy 转发的属性）：\n" + string.Join("\n", offenders));
    }

    /// <summary>抽取器的自证：正好抓到那两根、且不把别的 Slider 算进来。</summary>
    [Fact]
    public void SliderExtractorSeesTheBoundOnesOnly()
    {
        var sliders = SliderElements();
        Assert.True(sliders.Length >= 6, $"只抽到 {sliders.Length} 个 Slider——抽取器失效了（设置页今天有 8 根）");
        Assert.Equal(2, sliders.Count(s => BoundSliders.Any(b => s.Contains(b, StringComparison.Ordinal))));
        Assert.NotNull(AttrValue(sliders.First(s => s.Contains("ViewModel.BudgetMbFloor", StringComparison.Ordinal)), "Minimum"));
        Assert.Null(AttrValue("<Slider Value=\"0\"/>", "Minimum"));   // 没写这个属性就是没写，不许当成违规
    }

    // ────────── ③ VM 的初值与兜底都转发自判据 ──────────

    [Fact]
    public void ViewModelDefaultsComeFromTheJudge()
    {
        var lines = ReadRepoFile(ViewModelFile).Split('\n').Select(l => l.Trim()).ToList();
        // 字段初值那两行前面挂着 `[ObservableProperty]`，所以按"包含"锚，不按行首（按行首会一条都找不到＝假绿）
        foreach (var anchor in new[] { "private double _cacheBudgetMb = ", "private int _maxCacheCount = " })
        {
            var line = Assert.Single(lines, l => l.Contains(anchor, StringComparison.Ordinal));
            Assert.Contains("PerformanceSettingsPolicy.", line, StringComparison.Ordinal);
        }
        foreach (var anchor in new[] { "Safe(_settings.LoadCacheBudgetMb,", "Safe(_settings.LoadMaxImageCacheCount," })
        {
            var line = Assert.Single(lines, l => l.Contains(anchor, StringComparison.Ordinal));
            Assert.Contains("PerformanceSettingsPolicy.", line, StringComparison.Ordinal);
        }
    }

    // ────────── ④ 判据不空壳，且量程数字不进 UI 工程 ──────────

    /// <summary>八颗常数 + 两颗 Normalize：少了任何一颗，上面那三条闸门会因为"大家都不再提它"而假绿。</summary>
    [Fact]
    public void TheJudgeHoldsEveryTierNumber()
    {
        var raw = ReadRepoFile(JudgeFile);
        Assert.Equal(8, Regex.Matches(raw, @"public const (double|int) (BudgetMb|CacheCount)(Floor|Ceiling|Default|Step) =")
            .Count);
        Assert.Contains("NormalizeBudgetMb", raw, StringComparison.Ordinal);
        Assert.Contains("NormalizeCacheCount", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量程的两个上限（4096）只许住在判据里。<b>为什么单挑这两个数</b>：
    /// 它们是这批真正漂移过的那一对（界面 2048 / 1024 vs 仓储 4096），也是唯一"写错就静默改用户设置"的数。
    /// 下限与默认（32/16/200/256）不在这里禁——那几个数字在别处是无关的量（缓冲区、像素、超时），禁了就是天天误伤（#144）。
    /// </summary>
    [Fact]
    public void CeilingNumbersLiveOnlyInTheJudge()
    {
        var offenders = FormatScanner.SourcesUnder("src/StarMark.UI")
            .SelectMany(f => Regex.Matches(f.Code, @"(?<![\w.])4096(\.0+)?(?![\w.])")
                .Select(m => $"{f.Path}: {m.Value}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "UI 工程里又出现了自定义量程的上限（该绑判据）：\n" + string.Join("\n", offenders));
    }

    // ─────────────────────────  共用小工具  ─────────────────────────

    private static string[] _sliders = null!;

    private static string[] SliderElements()
        => _sliders ??= Regex.Matches(Xaml(), @"<Slider\b[^>]*?/>", RegexOptions.Singleline)
            .Select(m => m.Value).ToArray();

    private static string Xaml()
        => File.ReadAllText(Path.Combine(RepoRoot(), XamlFile.Replace('/', Path.DirectorySeparatorChar)));

    private static string? AttrValue(string element, string attr)
    {
        var m = Regex.Match(element, $@"{attr}\s*=\s*""([^""]*)""");
        return m.Success ? m.Groups[1].Value : null;
    }
}
