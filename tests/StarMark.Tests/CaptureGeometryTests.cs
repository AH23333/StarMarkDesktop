#nullable enable
using System;
using System.Globalization;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 截图 / 贴图的几何与上限判据（批次 MO）。
/// <para>
/// 捕获本身（GDI）测不了，但<b>从"用户拖出的两个点"到"从哪一行第几字节开始拷"这条链上每一步算错什么</b>
/// 全在这里。跨屏的负原点、反向拖动、150% 缩放下的舍入方向是这一族最常见的三个错。
/// </para>
/// </summary>
public sealed class CaptureGeometryTests
{
    // ────────── 选区成形 ──────────

    [Fact]
    public void Normalize_HandlesEveryDragDirectionAndNegativeOrigin()
    {
        var right = CaptureGeometry.Normalize(10, 20, 130, 260);
        // 从右下往左上拖（最常见）必须与反向拖得到同一个矩形
        Assert.Equal(right, CaptureGeometry.Normalize(130, 260, 10, 20));
        Assert.Equal(new IntRect(10, 20, 120, 240), right);

        // 副屏在主屏左边 ⇒ 坐标本身是负的，归一化不能把它"修正"成正数
        var second = CaptureGeometry.Normalize(-1900, -20, -1500, 380);
        Assert.Equal(new IntRect(-1900, -20, 400, 400), second);
    }

    [Fact]
    public void Normalize_ZeroSizeIsADegenerateClickNotANegativeRect()
    {
        var point = CaptureGeometry.Normalize(50, 60, 50, 60);
        Assert.Equal(0, point.Width);
        Assert.Equal(0, point.Height);
        Assert.True(point.IsEmpty);
    }

    [Fact]
    public void Intersect_PartialOverlapAndTouchingEdges()
    {
        var screen = new IntRect(0, 0, 1920, 1080);
        var clipped = CaptureGeometry.Intersect(new IntRect(-100, -50, 300, 200), screen);
        Assert.NotNull(clipped);
        Assert.Equal(new IntRect(0, 0, 200, 150), clipped!.Value);

        // 只在边上相接：交集面积为 0，要报"不相交"而不是给一个 0 宽矩形
        Assert.Null(CaptureGeometry.Intersect(new IntRect(1920, 0, 100, 100), screen));
        Assert.Null(CaptureGeometry.Intersect(new IntRect(-500, -500, 100, 100), screen));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]                                    // 完全重合
    [InlineData(0, 0, 1920, 1080)]                              // 恰好是整屏
    [InlineData(1919, 1079, 1, 1)]                              // 右下角 1×1
    public void Intersect_NeverExtendsBeyondBounds(int x, int y, int w, int h)
    {
        var bounds = new IntRect(0, 0, 1920, 1080);
        var clipped = CaptureGeometry.Intersect(new IntRect(x, y, w, h), bounds) ?? default;
        Assert.True(clipped.X >= bounds.X && clipped.Y >= bounds.Y);
        Assert.True(clipped.Right <= bounds.Right && clipped.Bottom <= bounds.Bottom);
    }

    // ────────── DIP ⇄ 物理像素 ──────────

    [Fact]
    public void ToPhysicalPixels_100PercentIsIdentity()
        => Assert.Equal(new IntRect(10, 20, 300, 400),
            CaptureGeometry.ToPhysicalPixels(new IntRect(10, 20, 300, 400), 1.0));

    /// <summary>
    /// 150% 下必须<b>四舍五入</b>：截断会让左上与右下各吃掉不到 1 像素，
    /// 表现为"截出来的图比框选看到的小一圈"（R-P1-9 那族非 100% DPI 问题）。
    /// </summary>
    [Fact]
    public void ToPhysicalPixels_RoundsAwayFromZeroAtBothEdges()
    {
        var physical = CaptureGeometry.ToPhysicalPixels(new IntRect(1, 1, 100, 50), 1.5);
        Assert.Equal(new IntRect(2, 2, 150, 75), physical);      // 1*1.5=1.5→2，101*1.5=151.5→152
        Assert.Equal(152 - 2, physical.Width);
        Assert.Equal(Math.Round(151.5, MidpointRounding.AwayFromZero) - 2, physical.Width);
    }

    [Fact]
    public void ToPhysicalPixels_KeepsNegativeOffsetsForSecondaryMonitors()
    {
        var physical = CaptureGeometry.ToPhysicalPixels(new IntRect(-500, 0, 400, 300), 1.25);
        Assert.Equal(-625, physical.X);
        Assert.Equal(500, physical.Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToPhysicalPixels_RejectsNonPositiveScale(double scale)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => CaptureGeometry.ToPhysicalPixels(new IntRect(0, 0, 10, 10), scale));

    [Fact]
    public void CropOffset_MeasuresFromTheFrameOriginNotFromZero()
    {
        // 帧覆盖虚拟桌面（原点 -1920），选区在主屏上 ⇒ 裁剪偏移必须是正的
        var frame = new IntRect(-1920, 0, 3840, 1080);
        var selection = new IntRect(100, 200, 50, 50);
        Assert.Equal((2020, 200), CaptureGeometry.CropOffset(selection, frame));
        Assert.Null(CaptureGeometry.CropProblem(selection, frame));
    }

    [Fact]
    public void CropProblem_RejectsSelectionsOutsideTheFrame()
    {
        var frame = new IntRect(-1920, 0, 1920, 1080);          // 只有左半屏被截到
        Assert.NotNull(CaptureGeometry.CropProblem(new IntRect(1000, 0, 100, 100), frame));
        Assert.NotNull(CaptureGeometry.CropProblem(new IntRect(0, 0, 0, 0), frame));   // 空帧
    }

    // ────────── 可用性判定 ──────────

    [Fact]
    public void SelectionProblem_TooSmallSaysWhatToDo()
    {
        var bounds = new IntRect(0, 0, 1920, 1080);
        var text = CaptureGeometry.SelectionProblem(new IntRect(10, 10, 2, 2), CaptureGeometry.Intersect(new IntRect(10, 10, 2, 2), bounds));
        Assert.NotNull(text);
        Assert.Contains("拖动", text);

        // 长条也太小：短边不达标就不该截（用户多半是想框一条线）
        Assert.NotNull(CaptureGeometry.SelectionProblem(new IntRect(0, 0, 1000, 2),
            CaptureGeometry.Intersect(new IntRect(0, 0, 1000, 2), bounds)));
        Assert.Null(CaptureGeometry.SelectionProblem(new IntRect(0, 0, CaptureGeometry.MinSelectionSide, 900),
            CaptureGeometry.Intersect(new IntRect(0, 0, CaptureGeometry.MinSelectionSide, 900), bounds)));
    }

    [Fact]
    public void SelectionProblem_OffScreenIsNotReportedAsTooSmall()
    {
        var reason = CaptureGeometry.SelectionProblem(new IntRect(5000, 5000, 400, 400), null);
        Assert.NotNull(reason);
        Assert.Contains("显示器之外", reason);
    }

    [Fact]
    public void FormatSize_HasNoDigitGroupingSeparators()
    {
        // 只查"不许出现千分位分隔符"：1280 在无分组文化下才是这个形状，
        // 断言小数点字符会随机器文化而假红，所以不在这里管
        // 只查"不许出现千分位分隔符"（有些文化会写成 1,280）；乘号两侧的空格是设计
        var text = CaptureGeometry.FormatSize(1280, 720);
        Assert.DoesNotContain(",", text);
        Assert.Contains("1280", text);
        Assert.Contains("720", text);
    }

    // ────────── 贴图缩放与上限 ──────────

    [Fact]
    public void ClampZoom_BoundsAndBadInput()
    {
        Assert.Equal(CaptureGeometry.MinPinZoom, CaptureGeometry.ClampZoom(0.0001));
        Assert.Equal(CaptureGeometry.MaxPinZoom, CaptureGeometry.ClampZoom(999));
        Assert.Equal(1.0, CaptureGeometry.ClampZoom(double.NaN));         // 上一状态被算坏 ⇒ 回 1×，不把 NaN 传给布局
        Assert.Equal(1.0, CaptureGeometry.ClampZoom(double.PositiveInfinity));
        Assert.Equal(2.0, CaptureGeometry.ClampZoom(2.0));
    }

    [Fact]
    public void NextZoom_DirectionsZeroAndSaturation()
    {
        Assert.True(CaptureGeometry.NextZoom(1, 120) > 1);
        Assert.True(CaptureGeometry.NextZoom(1, -120) < 1);
        Assert.Equal(1.0, CaptureGeometry.NextZoom(1, 0));
        // 端点上继续滚不该越界，也不该"回头"
        for (var i = 0; i < 40; i++) Assert.True(CaptureGeometry.NextZoom(1, 120) <= CaptureGeometry.MaxPinZoom + 1e-9);
        Assert.Equal(CaptureGeometry.MaxPinZoom, CaptureGeometry.NextZoom(CaptureGeometry.MaxPinZoom, 120));
        Assert.Equal(CaptureGeometry.MinPinZoom, CaptureGeometry.NextZoom(CaptureGeometry.MinPinZoom, -120));
        Assert.Equal(CaptureGeometry.MinPinZoom, CaptureGeometry.NextZoom(0.001, -120));   // 越界输入先夹回来
    }

    /// <summary>反复滚只能留在区间里：越界会让贴图窗尺寸算成 0 或天文数字。</summary>
    [Fact]
    public void NextZoom_NeverLeavesTheIntervalHoweverYouScroll()
    {
        var zoom = 1.0;
        for (var i = 0; i < 200; i++) zoom = CaptureGeometry.NextZoom(zoom, i % 2 == 0 ? 120 : -120);
        Assert.InRange(zoom, CaptureGeometry.MinPinZoom, CaptureGeometry.MaxPinZoom);
    }

    [Fact]
    public void ZoomedLength_NeverZeroAndNeverNaN()
    {
        Assert.Equal(2.0, CaptureGeometry.ZoomedLength(10, 0.01));         // 缩放先夹到端点 0.2×
        Assert.Equal(1.0, CaptureGeometry.ZoomedLength(1, 0.01));          // 1 像素的源再小也留 1 像素
        Assert.Equal(2.0, CaptureGeometry.ClampZoom(2.0));
        Assert.Equal(20.0, CaptureGeometry.ZoomedLength(10, 2.0));
        Assert.False(double.IsNaN(CaptureGeometry.ZoomedLength(10, double.NaN)));
    }

    [Fact]
    public void PinLimitProblem_UnderAndAtTheCap()
    {
        Assert.Null(CaptureGeometry.PinLimitProblem(0));
        Assert.Null(CaptureGeometry.PinLimitProblem(CaptureGeometry.MaxPins - 1));
        var text = CaptureGeometry.PinLimitProblem(CaptureGeometry.MaxPins);
        Assert.NotNull(text);
        Assert.Contains(CaptureGeometry.MaxPins.ToString(CultureInfo.InvariantCulture), text);
        Assert.Contains("关掉", text);                                     // 到上限要给下一步怎么做，不只是"不行"
    }

    [Fact]
    public void PinLimitsConstantsAreDeliberate()
    {
        Assert.Equal(12, CaptureGeometry.MaxPins);
        Assert.Equal(3, CaptureGeometry.MinSelectionSide);
        Assert.True(CaptureGeometry.ZoomStep > 1);
        Assert.True(CaptureGeometry.MaxPinZoom > CaptureGeometry.MinPinZoom);
    }

    // ────────── 文件名 ──────────

    [Fact]
    public void BuildFileName_PatternCollisionAndExtensionForms()
    {
        var now = new DateTimeOffset(2026, 9, 23, 22, 41, 7, TimeSpan.FromHours(8));
        Assert.Equal("StarMark 2026-09-23 224107.png", CaptureGeometry.BuildFileName(now, "png"));
        Assert.Equal("StarMark 2026-09-23 224107.png", CaptureGeometry.BuildFileName(now, ".PNG"));
        Assert.Equal("StarMark 2026-09-23 224107.png", CaptureGeometry.BuildFileName(now, ""));   // 空扩展名兜底
        Assert.Equal("StarMark 2026-09-23 224107 (1).png", CaptureGeometry.BuildFileName(now, "png", 1));
        Assert.Equal("StarMark 2026-09-23 224107.png", CaptureGeometry.BuildFileName(now, "png", -1));
    }

    /// <summary>同一秒内连按两次热键也要拿到两个不同文件（不覆盖上一张）。</summary>
    [Fact]
    public void BuildFileName_CollisionsAreAllDistinct()
    {
        var now = DateTimeOffset.Now;
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 20; i++) Assert.True(names.Add(CaptureGeometry.BuildFileName(now, "png", i)));
    }

    // ────────── 像素缓冲 ──────────

    [Fact]
    public void MakeOpaque_SetsAlphaOnEveryPixelAndLeavesColorAlone()
    {
        var buffer = new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0 };
        PixelBuffers.MakeOpaque(buffer);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255 }, buffer);
    }

    [Fact]
    public void MakeOpaque_ToleratesEmptyAndTruncatedBuffers()
    {
        PixelBuffers.MakeOpaque(Array.Empty<byte>());
        var tail = new byte[] { 1, 2, 3, 0, 9 };                  // 末尾不足一像素：不能越界
        PixelBuffers.MakeOpaque(tail);
        Assert.Equal(255, tail[3]);
        Assert.Equal(9, tail[4]);
    }
}
