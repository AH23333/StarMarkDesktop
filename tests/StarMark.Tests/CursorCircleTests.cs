#nullable enable
using System;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「光标那一块圆」的判据（<see cref="CursorCircle"/>）。
/// <para>
/// 批次 S4-⑥ 定下的是<b>一块圆、两个读数</b>：幕布开着 ⇒ 那块是"不叠底"的亮区；幕布关着 ⇒ 同一块是那一团光。
/// 批次 RN 按用户裁决再收两步：<b>档位从三档收成两档（关／任何工具常开，默认关）</b>、
/// <b>半径从代码常量搬进设置页（一根滑杆，两块圆共用同一个数）</b>。
/// </para>
/// <para>
/// 这批之后这里最值钱的三条：① <b>换算只有一处</b>（两处各乘一次缩放，四舍五入就差一像素）；
/// ② <b>"开着没有"与"这一帧叠不叠"是两个读数</b>（合成一个，幕布一开按钮就灭，看起来像设置被取消了）；
/// ③ <b>半径的合法区间只由 Core 说</b>（读取侧与存盘侧过同一个夹法，两处各写一遍上下限迟早分岔）。
/// </para>
/// <para>ppInk 参照（只读）：它只有 spotlight 一块圆（半透明橙 alpha=128 + 半径 200，屏宽百分比可调）——
/// "另加一圈光晕"在它那儿不存在，本项目现在也不该有。</para>
/// </summary>
public sealed class CursorCircleTests
{
    // ────────── 一块圆：半径只有一处换算 ──────────

    /// <summary>默认值必须落在滑杆够得到的区间里——它是"用户从没动过滑杆"时屏幕上那一圈的实际大小。</summary>
    [Fact]
    public void TheDefaultRadiusIsInsideTheRangeTheSliderCanReach()
        => Assert.InRange(CursorCircle.DefaultRadiusDip, CursorCircle.MinRadiusDip, CursorCircle.MaxRadiusDip);

    /// <summary>
    /// 150% 屏上必须跟着变大：<b>设置里那个 DIP × 该屏缩放</b>。
    /// 滑杆上写的数若是像素，两块不同缩放的屏就得各调一次才一样大——而"两块屏一根滑杆"根本调不平。
    /// </summary>
    [Theory]
    [InlineData(1.0, 160d, 160)]
    [InlineData(1.25, 160d, 200)]
    [InlineData(1.5, 160d, 240)]
    [InlineData(2.0, 100d, 200)]
    [InlineData(1.5, 300d, 450)]
    public void TheRadiusScalesWithTheScreenItIsDrawnOn(double scale, double radiusDip, int expected)
        => Assert.Equal(expected, CursorCircle.RadiusInPixels(scale, radiusDip));

    /// <summary>
    /// 缩放读不到（0／负／NaN）时回 1×，<b>不许算出 0</b>：半径 0 的圆在 <c>PaintGlow</c> 与
    /// <c>SetFocus</c> 两条路上都是"什么都不叠"，症状＝"光晕那颗开着，屏幕上却什么都没有"。
    /// </summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnreadableScaleFallsBackToOneHundredPercent(double scale)
        => Assert.Equal(160, CursorCircle.RadiusInPixels(scale, 160d));

    /// <summary>
    /// 半径<b>读不出</b>（NaN／∞：损坏的 JSON、旧版本档）时回<b>默认</b>而不是回上下限：
    /// 那种输入没有"想要多大"的意图可言，猜哪一端都是编；而拿 NaN 去乘缩放会算出 NaN 半径，
    /// 最后 <c>PaintGlow</c> 里 <c>radius &lt;= 0</c> 那一臂判定不了它，症状是"屏幕上什么都没有"。
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AnUnreadableRadiusFallsBackToTheDefault(double radiusDip)
        => Assert.Equal(160d, CursorCircle.ClampRadiusDip(radiusDip));

    /// <summary>
    /// <b>读得出但越界</b>（0／负数／十万元）则夹到<b>那一端</b>，不退回默认：
    /// 那种输入是"想要更小／更大"的意图，只是超出了这块圆还成立的范围。
    /// 下限不是 0——批次 WT 给序号圆点设下限那条理由同样适用：再小就画不出也看不见。
    /// </summary>
    [Theory]
    [InlineData(0d, 40d)]
    [InlineData(-50d, 40d)]
    [InlineData(401d, 400d)]
    [InlineData(100000d, 400d)]
    public void AReadableButOutOfBoundsRadiusClampsToThatEnd(double radiusDip, double expected)
        => Assert.Equal(expected, CursorCircle.ClampRadiusDip(radiusDip));

    /// <summary>上下限<b>本身</b>不许被夹动（闭区间）：滑杆拖到两端时，界面上的读数与叠出去的圆必须是同一个数。</summary>
    [Theory]
    [InlineData(40d)]
    [InlineData(400d)]
    public void TheTwoEndsOfTheSliderSurviveTheClamp(double radiusDip)
        => Assert.Equal(radiusDip, CursorCircle.ClampRadiusDip(radiusDip));

    /// <summary>换算<b>先夹再乘</b>，且夹的那一步与滑杆用的是同一对上下限（两处各写一遍迟早差一格）。</summary>
    [Fact]
    public void TheRadiusIsClampedBeforeItIsScaled()
    {
        Assert.Equal(800, CursorCircle.RadiusInPixels(2d, 99999d));
        Assert.Equal(80, CursorCircle.RadiusInPixels(2d, 1d));
    }

    // ────────── 两档：那一团光什么时候叠 ──────────

    /// <summary>「关」这一档＝无论什么工具、什么背景都不叠（两档里它是默认那一档）。</summary>
    [Fact]
    public void OffNeverDrawsTheHalo_RegardlessOfToolAndBackdrop()
    {
        foreach (var backdrop in Enum.GetValues<CanvasBackdrop>())
            Assert.False(CursorCircle.ShowsHalo(false, backdrop));
    }

    /// <summary>
    /// 「开」这一档<b>不看手上的工具</b>（RN 的用户裁决：只留"关／任何工具常开"两档，
    /// "只荧光笔"那一档删掉了）。判据里再也没有 tool 这个入参——留着一个没人问的参数，
    /// 下一轮就会有人以为它还有意义。
    /// </summary>
    [Theory]
    [InlineData(CanvasBackdrop.Transparent, true)]
    [InlineData(CanvasBackdrop.Whiteboard, true)]
    [InlineData(CanvasBackdrop.Curtain, false)]
    public void TheOnGearIgnoresWhichToolIsHeld(CanvasBackdrop backdrop, bool shows)
    {
        foreach (var tool in Enum.GetValues<CanvasTool>())
            Assert.Equal(shows, CursorCircle.ShowsHalo(true, backdrop));
    }

    /// <summary>
    /// <b>幕布开着时光晕不叠</b>：那块圆已经"不叠底"了，再叠一团光就把亮区里的原色盖掉——
    /// 两个读数各自都要能看见，重叠在一起就都读不出来（这正是"合并成一块圆"的全部含义）。
    /// <para>判据问的是性质（那块有没有亮区）而不是背景态的名字：将来多一种背景态，它自动落进正确那一臂。</para>
    /// </summary>
    [Fact]
    public void TheCurtainHoleIsTheOnlyReadingWhileTheCurtainIsOn()
    {
        Assert.False(CursorCircle.ShowsHalo(true, CanvasBackdrop.Curtain));
        Assert.True(CursorCircle.ShowsHalo(true, CanvasBackdrop.Whiteboard));
    }

    /// <summary>
    /// "开着没有"与"这一帧叠不叠"是<b>两个</b>问题：幕布开着时光晕不叠，但按钮必须还是亮的
    /// （用户翻的那个开关没变，变的是背景态）。把两者合成一个读数，幕布一开按钮就灭，看起来像开关被取消了。
    /// <para>RN 之后这一条更容易踩，因为开关只剩一位：<c>CanvasService.HaloEnabled</c> 读那一位，绝不许读 <c>ShowsHalo</c>。</para>
    /// </summary>
    [Fact]
    public void TheButtonReadsTheSwitchNotWhatThisFramePainted()
    {
        Assert.False(CursorCircle.ShowsHalo(true, CanvasBackdrop.Curtain));   // 开着，但这一帧不叠
    }

    // ────────── 那块圆占的地方：两个读者一块表 ──────────

    /// <summary>
    /// 帧循环算脏区用的那块方框，与合成核叠完之后报的那块，<b>必须是同一个数夹出来的</b>。
    /// <para>为什么单独钉这一条：两处从前各写一次 <c>半径 * 2 + 1</c>，改动其中一处（比如给外缘留一圈淡出）
    /// 编译不会红、旧测试也不会红，症状是"光晕拖过去后面拖着一条旧光"——脏区没盖到画到的地方。
    /// RN 之后这件事更当真：半径现在会被用户当场拖动，两处公式分岔的窗口从"改代码"缩成了"每帧"。</para>
    /// </summary>
    [Theory]
    [InlineData(50, 50, 16)]
    [InlineData(0, 0, 40)]
    [InlineData(99, 99, 40)]
    [InlineData(50, 50, 300)]              // 比整块画布还大：两处都要塌成"整幅"
    [InlineData(500, 500, 20)]             // 完全在画布外：两处都必须是"没有脏区"
    public void TheBoxTheFrameLoopDirtiesIsTheBoxTheKernelReports(int x, int y, int radius)
    {
        var buffer = new uint[100 * 100];
        Array.Fill(buffer, StarMark.Integrations.Canvas.LayeredCanvasWindow.BlankPixel);
        var center = new PixelPoint(x, y);
        var painted = CanvasCompositor.PaintGlow(buffer, 100, 100, center, radius, unchecked((int)0xFFE81123));
        Assert.Equal(CanvasCompositor.Clamp(CursorCircle.BoxOf(center, radius), 100, 100), painted);
    }

    /// <summary>那块方框按<b>直径 + 1</b> 算，且随半径一起长：滑杆拖到底却还占着默认那一圈＝脏区漏一圈。</summary>
    [Theory]
    [InlineData(40)]
    [InlineData(160)]
    [InlineData(400)]
    public void TheBoxGrowsWithTheRadiusTheUserChose(int radius)
    {
        var box = CursorCircle.BoxOf(new PixelPoint(1000, 1000), radius);
        Assert.Equal(radius * 2 + 1, box.Width);
        Assert.Equal(radius * 2 + 1, box.Height);
    }

    /// <summary>
    /// 滑杆那个数<b>必须落到屏幕上看得见的东西上</b>（批次 WR 立下的口径："用户可选的量"要有一条打到最终产物的测，
    /// 否则"改了没反应"可以一路全绿）：半径一档比一档大，画出来的非背景像素也必须一档比一档多。
    /// <para>默认那一档同时钉在 <b>≈πr²</b> 那一条带上（r=160 → 约 8.1 万点）：
    /// 这条带的用处不是精度，是拦住"把常量搬进设置页"时顺手把默认观感改掉——
    /// 半块圆（漏乘缩放）与两倍大（乘了两次）都会掉出带外，而"和以前一样"用肉眼在 160 DIP 上看不出差。</para>
    /// </summary>
    [Fact]
    public void AChosenRadiusPaintsMorePixelsThanTheOneBelowIt()
    {
        var small = LitPixels(CursorCircle.MinRadiusDip);
        var standard = LitPixels(CursorCircle.DefaultRadiusDip);
        var large = LitPixels(CursorCircle.MaxRadiusDip);

        Assert.True(small < standard, $"下限应比默认小：{small} vs {standard}");
        Assert.True(standard < large, $"上限应比默认大：{standard} vs {large}");
        Assert.InRange(standard, 78_000, 84_000);
    }

    /// <summary>在 900×900 的空白底上叠一团光，数它盖掉了多少个像素（上限 400 也放得下，不会被裁）。</summary>
    private static int LitPixels(double radiusDip)
    {
        const int width = 900, height = 900;
        var buffer = new uint[width * height];
        Array.Fill(buffer, StarMark.Integrations.Canvas.LayeredCanvasWindow.BlankPixel);
        CanvasCompositor.PaintGlow(buffer, width, height, new PixelPoint(450, 450),
            CursorCircle.RadiusInPixels(1d, radiusDip), unchecked((int)0xFFE81123));

        var lit = 0;
        foreach (var pixel in buffer)
            if (pixel != StarMark.Integrations.Canvas.LayeredCanvasWindow.BlankPixel) lit++;
        return lit;
    }
}
