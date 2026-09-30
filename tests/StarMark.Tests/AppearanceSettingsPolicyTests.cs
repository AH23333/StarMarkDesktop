#nullable enable
using System;
using StarMark.Core.Appearance;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="AppearanceSettingsPolicy"/> 的真行为测（批次 SD，P-123 清单 #4）。
/// <para>这颗判据住在 Core ⇒ 出得了真测（#184）；界面上那三根滑杆"绑没绑它"只能靠形状闸门，见
/// <see cref="AppearanceRangeGateTests"/>。</para>
/// <para>这批的验收口径是<b>读数逐字不变</b>：夹取的范围、默认值、百分比文本全部与收口前一致，
/// 所以下面既有"边界臂"的用例，也有专门钉住今天读数的用例——后者不是凑数，
/// 它保证下一次动默认值的人必须连带改这条断言，而不是悄悄改一个数字。</para>
/// </summary>
public sealed class AppearanceSettingsPolicyTests
{
    // ─────────────── ① 不变量：默认在量程内、步进推得动 ───────────────

    /// <summary>四组默认值都落在自己的量程里（含端点）。写反一个边界，设置页就会开在"指不到"的位置。</summary>
    [Theory]
    [InlineData(AppearanceSettingsPolicy.OpacityFloor, AppearanceSettingsPolicy.OpacityDefault, AppearanceSettingsPolicy.OpacityCeiling)]
    [InlineData(AppearanceSettingsPolicy.SnapStrengthFloor, AppearanceSettingsPolicy.SnapStrengthDefault, AppearanceSettingsPolicy.SnapStrengthCeiling)]
    [InlineData(AppearanceSettingsPolicy.SnapSpacingFloor, AppearanceSettingsPolicy.SnapSpacingDefault, AppearanceSettingsPolicy.SnapSpacingCeiling)]
    [InlineData(AppearanceSettingsPolicy.MaterialIntensityFloor, AppearanceSettingsPolicy.MaterialIntensityDefault, AppearanceSettingsPolicy.MaterialIntensityCeiling)]
    public void DefaultSitsInsideItsOwnRange(double floor, double def, double ceiling)
    {
        Assert.True(floor <= ceiling, $"下限 {floor} 比上限 {ceiling} 还大");
        Assert.True(def >= floor && def <= ceiling, $"默认值 {def} 落在 [{floor}, {ceiling}] 之外");
    }

    /// <summary>
    /// 步进必须是正数、且<b>不比整个量程粗</b>——否则滑杆一步跨过头，中间那些档位表达不出来
    /// （钉的是不变量，不是"步进等于 2"这种一改就红的数）。
    /// </summary>
    [Theory]
    [InlineData(AppearanceSettingsPolicy.OpacityFloor, AppearanceSettingsPolicy.OpacityCeiling, AppearanceSettingsPolicy.OpacityStep)]
    [InlineData(AppearanceSettingsPolicy.SnapStrengthFloor, AppearanceSettingsPolicy.SnapStrengthCeiling, AppearanceSettingsPolicy.SnapStrengthStep)]
    [InlineData(AppearanceSettingsPolicy.SnapSpacingFloor, AppearanceSettingsPolicy.SnapSpacingCeiling, AppearanceSettingsPolicy.SnapSpacingStep)]
    public void StepIsPositiveAndCoarserThanNothing(double floor, double ceiling, double step)
    {
        Assert.True(step > 0, "步进为 0 或负 ⇒ 滑杆推不动");
        Assert.True(step <= ceiling - floor, $"步进 {step} 比量程 {ceiling - floor} 还宽，档位会塌成一格");
    }

    // ─────────────── ② 今天的读数逐字钉住（这批只改出处，不改显示） ───────────────

    [Fact]
    public void TodayNumbers_ReadExactlyLikeBeforeTheBatch()
    {
        Assert.Equal(0.72, AppearanceSettingsPolicy.OpacityDefault);
        Assert.Equal(24, AppearanceSettingsPolicy.SnapStrengthDefault);
        Assert.Equal(8, AppearanceSettingsPolicy.SnapSpacingDefault);
        Assert.Equal(0.65, AppearanceSettingsPolicy.MaterialIntensityDefault);
        Assert.Equal(0.3, AppearanceSettingsPolicy.OpacityFloor);
        Assert.Equal(1.0, AppearanceSettingsPolicy.OpacityCeiling);
        Assert.Equal(4, AppearanceSettingsPolicy.SnapStrengthFloor);
        Assert.Equal(64, AppearanceSettingsPolicy.SnapStrengthCeiling);
        Assert.Equal(0, AppearanceSettingsPolicy.SnapSpacingFloor);
        Assert.Equal(40, AppearanceSettingsPolicy.SnapSpacingCeiling);
    }

    /// <summary>
    /// 滑块右边那行百分比文本的算法是 <c>(int)Math.Round(opacity * 100)</c>（VM 里）。
    /// 这里不测 VM（测不到，#184），只钉住它喂进去的那个数会打成 "72%"——默认值被人动一格，这行就会红。
    /// </summary>
    [Fact]
    public void OpacityDefault_PrintsSeventyTwoPercent()
        => Assert.Equal("72%", $"{(int)Math.Round(AppearanceSettingsPolicy.OpacityDefault * 100)}%");

    // ─────────────── ③ 三颗夹取的边界臂 ───────────────

    [Theory]
    [InlineData(-1.0, 0.3)]          // 越下界
    [InlineData(0.29, 0.3)]
    [InlineData(0.3, 0.3)]           // 正好在下界：不许多夹
    [InlineData(0.72, 0.72)]         // 默认值原样穿过
    [InlineData(1.0, 1.0)]           // 正好在上界
    [InlineData(1.5, 1.0)]           // 越上界
    public void OpacityClampsBothEnds_IncludingTheEndpointsThemselves(double input, double expected)
        => Assert.Equal(expected, AppearanceSettingsPolicy.NormalizeOpacity(input), 10);

    [Theory]
    [InlineData(-10, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(24, 24)]
    [InlineData(64, 64)]
    [InlineData(65, 64)]
    public void SnapStrengthClampsBothEnds(int input, int expected)
        => Assert.Equal(expected, AppearanceSettingsPolicy.NormalizeSnapStrength(input));

    /// <summary>
    /// 间距的<b>下限 0 是有意开放的一档</b>（0＝边边紧贴，用户真会要）：
    /// 曾经有实现把它当"至少留点缝"顶成 1，这里钉住不许。
    /// </summary>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(8, 8)]
    [InlineData(40, 40)]
    [InlineData(41, 40)]
    public void SnapSpacingAllowsZeroGap_AndClampsBothEnds(int input, int expected)
        => Assert.Equal(expected, AppearanceSettingsPolicy.NormalizeSnapSpacing(input));

    [Theory]
    [InlineData(-0.1, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.65, 0.65)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void MaterialIntensityClampsBothEnds(double input, double expected)
        => Assert.Equal(expected, AppearanceSettingsPolicy.NormalizeMaterialIntensity(input), 10);

    // ─────────────── ④ 判据管什么、不管什么（写清楚，防误以为它兜住一切） ───────────────

    /// <summary>
    /// 判据只做<b>范围</b>夹取，<b>不做有限性检查</b>：NaN 一路穿过（<c>Math.Clamp</c> 的两个比较都不成立）。
    /// <para>这不是遗漏而是分工——"非有限值 ⇒ 整窗面板隐形"那层守卫在
    /// <c>WidgetMaterialVisualCalculator.NormalizeMaterialIntensity</c>（批次 10 真机踩过的坑）。
    /// 记成断言是为了让下一个想"给判据加个 NaN 兜底"的人先看见这句话管的是哪一层。</para>
    /// </summary>
    [Fact]
    public void JudgeGuardsRangeNotFiniteness_NanPassesThrough()
    {
        Assert.True(double.IsNaN(AppearanceSettingsPolicy.NormalizeOpacity(double.NaN)));
        Assert.True(double.IsNaN(AppearanceSettingsPolicy.NormalizeMaterialIntensity(double.NaN)));
    }
}
