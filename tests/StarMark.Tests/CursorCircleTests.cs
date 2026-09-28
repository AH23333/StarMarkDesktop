#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S4-⑥「光标那一块圆」的判据（<see cref="CursorCircle"/>）。
/// <para>
/// 用户裁的三条里两条落在这里：<b>一块圆、两个读数</b>（幕布开着 ⇒ 那块是"不叠底"的亮区；
/// 幕布关着 ⇒ 同一块是那一团光）与<b>光晕三档</b>（关 / 只荧光笔 / 常开，默认仍是"只荧光笔"）。
/// 第三条（半径先进代码一份常量）也在这里钉：它必须<b>只有一处</b>做 DIP→物理的换算——
/// 从前光晕按笔宽档算、亮区按一个 DIP 常量算，两处各乘一次缩放，四舍五入就能差一像素。
/// </para>
/// <para>ppInk 参照（只读）：它只有 spotlight 一块圆（<c>Root.cs:460-461</c> 半透明橙 alpha=128 + 半径 200，
/// 屏宽百分比可调 <c>FormOptions.cs:1452</c>）——"另加一圈光晕"在它那儿不存在，本项目现在也不该有。</para>
/// </summary>
public sealed class CursorCircleTests
{
    // ────────── 一块圆：半径只有一处换算 ──────────

    [Fact]
    public void TheRadiusIsStatedInDipAndIsBigEnoughToReadAsACursorHint()
        => Assert.InRange(CursorCircle.RadiusDip, 80d, 400d);

    /// <summary>150% 屏上必须跟着变大：<b>DIP 常量 × 该屏缩放</b>，写死像素数就是"在这块屏上小一半"。</summary>
    [Theory]
    [InlineData(1.0, 160)]
    [InlineData(1.25, 200)]
    [InlineData(1.5, 240)]
    [InlineData(2.0, 320)]
    public void TheRadiusScalesWithTheScreenItIsDrawnOn(double scale, int expected)
        => Assert.Equal(expected, CursorCircle.RadiusInPixels(scale));

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
        => Assert.Equal(160, CursorCircle.RadiusInPixels(scale));

    // ────────── 两个读数：那一团光什么时候叠 ──────────

    [Fact]
    public void OffNeverDrawsTheHalo_RegardlessOfToolAndBackdrop()
    {
        foreach (var tool in Enum.GetValues<CanvasTool>())
            foreach (var backdrop in Enum.GetValues<CanvasBackdrop>())
                Assert.False(CursorCircle.ShowsHalo(HaloMode.Off, tool, backdrop));
    }

    /// <summary>默认档＝只荧光笔（这条是批次 WF 之后的既有裁决，三档化之后它只是中间那一档）。</summary>
    [Theory]
    [InlineData(CanvasTool.Highlighter, true)]
    [InlineData(CanvasTool.Pen, false)]
    [InlineData(CanvasTool.Eraser, false)]
    [InlineData(CanvasTool.Rectangle, false)]
    [InlineData(CanvasTool.PolyLine, false)]
    public void TheDefaultGearFollowsOnlyTheHighlighter(CanvasTool tool, bool shows)
        => Assert.Equal(shows, CursorCircle.ShowsHalo(HaloMode.HighlighterOnly, tool, CanvasBackdrop.Transparent));

    /// <summary>常开档：拿哪支笔都跟着——这一档要的是"看得见光标在哪儿"，不是"我拿着什么"。</summary>
    [Fact]
    public void TheAlwaysGearCoversEveryTool()
    {
        foreach (var tool in Enum.GetValues<CanvasTool>())
            Assert.True(CursorCircle.ShowsHalo(HaloMode.Always, tool, CanvasBackdrop.Transparent));
    }

    /// <summary>
    /// <b>幕布开着时光晕不叠</b>：那块圆已经"不叠底"了，再叠一团光就把亮区里的原色盖掉——
    /// 两个读数各自都要能看见，重叠在一起就都读不出来（这正是"合并成一块圆"的全部含义）。
    /// </summary>
    [Fact]
    public void TheCurtainHoleIsTheOnlyReadingWhileTheCurtainIsOn()
    {
        foreach (var mode in new[] { HaloMode.HighlighterOnly, HaloMode.Always })
            foreach (var tool in Enum.GetValues<CanvasTool>())
                Assert.False(CursorCircle.ShowsHalo(mode, tool, CanvasBackdrop.Curtain));
    }

    /// <summary>白板底不遮光标位置这件事：白底上那一团红反而最清楚，所以它<b>不</b>算"亮区"。</summary>
    [Theory]
    [InlineData(HaloMode.HighlighterOnly, CanvasTool.Highlighter, true)]
    [InlineData(HaloMode.Always, CanvasTool.Pen, true)]
    public void TheWhiteboardStillShowsTheHalo(HaloMode mode, CanvasTool tool, bool shows)
        => Assert.Equal(shows, CursorCircle.ShowsHalo(mode, tool, CanvasBackdrop.Whiteboard));

    /// <summary>
    /// "开着没有"与"这一帧叠不叠"是<b>两个</b>问题：幕布开着时光晕不叠，但那颗按钮必须还是亮的
    /// （用户选的那一档没变，变的是背景态）。把两者合成一个读数，幕布一开按钮就灭，看起来像档位被取消了。
    /// </summary>
    [Theory]
    [InlineData(HaloMode.Off, false)]
    [InlineData(HaloMode.HighlighterOnly, true)]
    [InlineData(HaloMode.Always, true)]
    public void TheButtonReadsTheGearNotWhatThisFramePainted(HaloMode mode, bool on)
    {
        Assert.Equal(on, CursorCircle.IsOn(mode));
        var shows = CursorCircle.ShowsHalo(mode, CanvasTool.Highlighter, CanvasBackdrop.Curtain);
        Assert.False(shows);
        if (on) Assert.NotEqual(CursorCircle.IsOn(mode), shows);   // 开着的那一档此刻不叠光：两个读数必须分得开
    }

    // ────────── 那块圆占的地方：两个读者一块表 ──────────

    /// <summary>
    /// 帧循环算脏区用的那块方框，与合成核叠完之后报的那块，<b>必须是同一个数夹出来的</b>。
    /// <para>为什么单独钉这一条：两处从前各写一次 <c>半径 * 2 + 1</c>，改动其中一处（比如给外缘留一圈淡出）
    /// 编译不会红、旧测试也不会红，症状是"光晕拖过去后面拖着一条旧光"——脏区没盖到画到的地方。</para>
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

    // ────────── 那颗按钮的循环 ──────────

    /// <summary>三档循环：点三下回到原处，且每一档都被经过（漏一档＝那颗按钮点半天没变化）。</summary>
    [Fact]
    public void TheButtonCyclesThroughEveryGearExactlyOnce()
    {
        var seen = new System.Collections.Generic.List<HaloMode>();
        var mode = HaloMode.HighlighterOnly;                       // 起点＝默认档
        for (var i = 0; i < 3; i++)
        {
            mode = CursorCircle.Next(mode);
            seen.Add(mode);
        }
        Assert.Equal(HaloMode.HighlighterOnly, mode);              // 三次点回原处
        Assert.Equal(3, seen.Distinct().Count());
        Assert.Contains(HaloMode.Off, seen);
        Assert.Contains(HaloMode.Always, seen);
    }

    /// <summary>每一档都有名字且互不相同：状态行只有"光晕××"这一处交代，重名就等于没说。</summary>
    [Fact]
    public void EveryGearHasItsOwnName()
    {
        var names = Enum.GetValues<HaloMode>().Select(CursorCircle.NameOf).ToList();
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
