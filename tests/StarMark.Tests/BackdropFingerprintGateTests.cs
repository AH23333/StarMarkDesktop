#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 材质值指纹闸门（片244，扫源码不跑界面）。
/// <para>
/// 现场（starmark-20261108.log 18:48:21）：一趟外观值<b>一分没变</b>的去抖保存，把 13 扇窗全量重挂
/// ——节流注记「此前 5s 内同类 35 条未记」，每扇每次 3 个 DWM 跨进程互操作全在 UI 线程。
/// 指纹＝「同一扇窗、同一副值」即跳过整套互操作。这里守五半，缺一算没做：
/// ① 比对先于一切 DWM 互操作、命中即 return（省的是互操作本身，不是日志）；
/// ② 实色（None）与背衬材质两条腿都有指纹——切材质、切明暗必重挂；
/// ③ 指纹折叠全部视觉输入（材质/明暗/成色；成色已折叠染料、逐窗底色与纯色不透明度）；
/// ④ 「背衬仍挂着」按引用显式判——外部摘掉 SystemBackdrop 时指纹即刻失效；
/// ⑤ ReleaseBackdrop 摘表＝指纹随窗口生死，窗口重建后首挂必全量（⛔ 指纹跨窗）。
/// </para>
/// </summary>
public sealed class BackdropFingerprintGateTests
{
    private static string Appearance() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/WidgetAppearance.cs"));
    private static string ApplyBody() => SourceGate.MethodBody(Appearance(), "public static void ApplyBackdrop(");

    /// <summary>① 指纹命中必须省掉互操作本身：比对与 return 都在任何 WindowInterop.* 之前。</summary>
    [Fact]
    public void SkipHappensBeforeAnyDwmInterop()
    {
        var squashed = SourceGate.Squash(ApplyBody());
        var skip = squashed.IndexOf("state.HasFingerprint", StringComparison.Ordinal);
        var firstInterop = squashed.IndexOf("WindowInterop.", StringComparison.Ordinal);
        Assert.True(skip >= 0, "指纹比对不见了");
        Assert.True(firstInterop < 0 || skip < firstInterop, "指纹比对必须先于第一处 DWM 互操作");
        Assert.Contains("LogSkippedReapply(kind, isDark); return;", squashed);
    }

    /// <summary>② 两条腿都有指纹：实色 None 那条腿与背衬材质那条腿，值没变都跳、值变了都重挂。</summary>
    [Fact]
    public void BothSolidAndBackdropBranchesCarryTheFingerprint()
    {
        var squashed = SourceGate.Squash(ApplyBody());
        Assert.Contains(
            "state.HasFingerprint && state.FingerprintKind == WidgetBackdropKind.None && state.FingerprintDark == isDark && state.Tint is null",
            squashed);
        Assert.Contains(
            "state.HasFingerprint && state.FingerprintKind == kind && state.FingerprintDark == isDark",
            squashed);
    }

    /// <summary>③ 指纹折叠全部视觉输入：材质、明暗、成色（成色由取色那颗算出——染料/tint/滑杆都在它那一头）。</summary>
    [Fact]
    public void FingerprintFoldsEveryVisualInput()
    {
        var squashed = SourceGate.Squash(ApplyBody());
        Assert.Contains("state.FingerprintColor.Equals(color)", squashed);
        Assert.Contains("MaterialTint.BackdropColor(", squashed);   // 成色的唯一出处仍喂着指纹
    }

    /// <summary>④ 「背衬仍挂着」按引用判：外部摘掉/换掉 SystemBackdrop，指纹即刻失效必重挂。</summary>
    [Fact]
    public void StaleBackdropDefeatsTheFingerprint()
    {
        var squashed = SourceGate.Squash(ApplyBody());
        Assert.Contains("ReferenceEquals(window.SystemBackdrop, state.Tint)", squashed);
    }

    /// <summary>⑤ 指纹随窗口生死：ReleaseBackdrop 摘表，窗口重建后首挂必全量。</summary>
    [Fact]
    public void ReleaseResetsTheFingerprintPerWindow()
    {
        var release = SourceGate.Squash(
            SourceGate.MethodBody(Appearance(), "public static void ReleaseBackdrop(Window window)"));
        Assert.Contains("_states.Remove(window)", release);
    }

    /// <summary>组件窗与主窗共用同一咽喉：两处调用点都过 WidgetAppearance.ApplyBackdrop，
    /// 指纹一份实现覆盖两入口（主窗背景同样吃保存链全量重挂的亏）。</summary>
    [Fact]
    public void BothWindowKindsShareTheSingleChokePoint()
    {
        var main = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/MainWindow.Theme.cs"));
        Assert.Contains("WidgetAppearance.ApplyBackdrop(", main);
        var widget = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/WidgetWindow.Appearance.cs"));
        Assert.Contains("WidgetAppearance.ApplyBackdrop(", widget);
    }
}
