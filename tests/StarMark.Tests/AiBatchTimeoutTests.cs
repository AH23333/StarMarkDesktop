#nullable enable
using StarMark.Core.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 单批超时的上下界与"坏值回默认"（批次 R4）。<b>判据只住在 <see cref="ClassifyRunner"/> 一处</b>，
/// 存档层与设置页都只是它的读者：这一格用户可以手改 settings.json，而静默采纳 0 秒等于"每批一发出就超时"——
/// 那是"改了没落盘"里最难自查的一种，所以回退必须是一个能断言的纯函数，不是界面里的一句 if。
/// </summary>
public sealed class AiBatchTimeoutTests
{
    [Theory]
    [InlineData(10)]        // 下界本身要算合法：能填到自己写的那个数，"自选"才不是句空话
    [InlineData(45)]
    [InlineData(180)]
    [InlineData(900)]       // 上界同上
    public void WithinBand_KeptAsIs(int? seconds)
        => Assert.Equal(seconds!.Value, ClassifyRunner.NormalizeTimeoutSeconds(seconds));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]         // 差一秒到不了下界：回默认，而不是夹到 10（夹边只用在"想要但够不着"那一类）
    [InlineData(901)]
    [InlineData(100_000)]
    public void OutOfBand_FallsBackToDefault(int seconds)
        => Assert.Equal(ClassifyRunner.DefaultTimeoutSeconds, ClassifyRunner.NormalizeTimeoutSeconds(seconds));

    [Fact]
    public void NotSet_FallsBackToDefault()
        => Assert.Equal(ClassifyRunner.DefaultTimeoutSeconds, ClassifyRunner.NormalizeTimeoutSeconds(null));

    [Fact]
    public void Band_ContainsItsOwnDefault()
    {
        // 默认值必须落在自己划的区间里：否则"留空走默认"与"能填的档位"是两套口径，界面上迟早分岔
        Assert.InRange(ClassifyRunner.DefaultTimeoutSeconds,
            ClassifyRunner.MinTimeoutSeconds, ClassifyRunner.MaxTimeoutSeconds);
        Assert.True(ClassifyRunner.MinTimeoutSeconds < ClassifyRunner.MaxTimeoutSeconds);
    }
}
