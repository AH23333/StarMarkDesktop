#nullable enable
using System.Linq;
using Xunit;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;

namespace StarMark.Tests;

/// <summary>
/// 贴图那套判据的可机检部分：缩放后占多少物理像素、上限与空集的回报、百分比文案、出厂键位。
/// <para>
/// 这些数字全部要写死：贴图窗的尺寸直接喂给 <c>SetWindowPos</c>，一进一出没有第二次机会修正；
/// 而 F1/F3/F4 是用户点名要跟 Snipaste 一致的东西，改了键位等于改产品承诺。
/// </para>
/// </summary>
public sealed class PinGeometryTests
{
    // ────────── 尺寸 ──────────

    [Fact]
    public void PinPixelSize_AtOneTimes_IsExactlyTheSourcePixels()
    {
        // 源图就是按物理像素截的，所以 1× 必须原样：再乘一次 DPI 缩放会让 150% 屏上的贴图比原物大一半
        Assert.Equal((1920, 1080), CaptureGeometry.PinPixelSize(1920, 1080, 1.0));
        Assert.Equal((200, 120), CaptureGeometry.PinPixelSize(200, 120, 1.0));
    }

    [Theory]
    [InlineData(100, 50, 2.0, 200, 100)]
    [InlineData(13, 13, 0.2, 3, 3)]     // 2.6 → 3（AwayFromZero；截断会让每一档都少一像素）
    [InlineData(11, 11, 0.2, 2, 2)]     // 2.2 → 2
    [InlineData(10, 10, 5.0, 50, 50)]
    [InlineData(1, 1, 0.2, 1, 1)]       // 1 像素的源缩到 0.2× 只剩 0.2 → 舍入为 0：至少要留 1 像素，0 宽会让 WinUI 整块不渲染
    [InlineData(1, 1, 0.001, 1, 1)]     // 越界缩放先夹回端点再算
    [InlineData(10, 10, -3.0, 2, 2)]    // 负数同样夹到 0.2×，不能算出负尺寸
    public void PinPixelSize_ScalesRoundsAndNeverCollapsesToZero(
        int source, int sourceHeight, double zoom, int expectedWidth, int expectedHeight)
    {
        var (w, h) = CaptureGeometry.PinPixelSize(source, sourceHeight, zoom);
        Assert.Equal(expectedWidth, w);
        Assert.Equal(expectedHeight, h);
    }

    [Fact]
    public void PinPixelSize_BrokenZoom_FallsBackToOneTimesInsteadOfNaN()
    {
        // NaN 传进 SetWindowPos 不会抛，会得到一个尺寸不明的窗（然后再也点不到它）
        Assert.Equal((10, 10), CaptureGeometry.PinPixelSize(10, 10, double.NaN));
        Assert.Equal((10, 10), CaptureGeometry.PinPixelSize(10, 10, double.PositiveInfinity));
    }

    [Fact]
    public void PinPixelSize_KeepsAspectRatioWithinOnePixel()
    {
        var (w, h) = CaptureGeometry.PinPixelSize(1001, 999, 1.1);
        Assert.Equal(1101, w);      // 1101.1 → 1101
        Assert.Equal(1099, h);      // 1098.9 → 1099
    }

    // ────────── 百分比文案 ──────────

    [Theory]
    [InlineData(1.0, "100%")]
    [InlineData(0.2, "20%")]
    [InlineData(5.0, "500%")]
    [InlineData(1.1, "110%")]       // 110.00000000000001 必须显示成整数，否则每滚一档角标都在抖
    [InlineData(1.0 / 1.1, "91%")]  // 下滚一档 0.909… → 91%
    [InlineData(0.0, "20%")]        // 越界输入报的是"实际生效的倍率"，不是那个坏数
    [InlineData(99.0, "500%")]
    public void FormatZoom_ShowsTheEffectiveRatio(double zoom, string expected)
        => Assert.Equal(expected, CaptureGeometry.FormatZoom(zoom));

    // ────────── 上限与空集 ──────────

    [Fact]
    public void PinLimitProblem_OnlyAtTheBoundary()
    {
        Assert.Null(CaptureGeometry.PinLimitProblem(CaptureGeometry.MaxPins - 1));
        Assert.NotNull(CaptureGeometry.PinLimitProblem(CaptureGeometry.MaxPins));
        Assert.NotNull(CaptureGeometry.PinLimitProblem(CaptureGeometry.MaxPins + 5));
    }

    [Fact]
    public void PinLimitProblem_TellsTheUserWhatToDo()
    {
        var text = CaptureGeometry.PinLimitProblem(CaptureGeometry.MaxPins)!;
        Assert.Contains("关掉", text);                       // P-54：阻碍要说清出路，不能只说不行
        Assert.Contains(CaptureGeometry.MaxPins.ToString(), text);
    }

    [Fact]
    public void PinCommandProblem_OnlyRefusesTheEmptySet()
    {
        Assert.NotNull(CaptureGeometry.PinCommandProblem(0));
        Assert.Contains("没有", CaptureGeometry.PinCommandProblem(0)!);
        Assert.Null(CaptureGeometry.PinCommandProblem(1));
        Assert.Null(CaptureGeometry.PinCommandProblem(CaptureGeometry.MaxPins));
    }

    // ────────── 出厂键位（D3：与 Snipaste 一致） ──────────

    [Fact]
    public void Defaults_PinKeys_MatchSnipasteAndCarryNoModifiers()
    {
        var defaults = HotkeyBindings.Defaults();

        Assert.Equal(0x70u, defaults[HotkeyActions.ScreenCapture].VirtualKey);   // F1
        Assert.Equal(0x72u, defaults[HotkeyActions.ScreenPin].VirtualKey);       // F3
        Assert.Equal(0x73u, defaults[HotkeyActions.ScreenPinToggleHidden].VirtualKey);   // F4
        Assert.Equal(0x74u, defaults[HotkeyActions.ScreenPinClickThrough].VirtualKey);   // F5（穿透必须有键盘出口）
        foreach (var action in new[]
        {
            HotkeyActions.ScreenCapture, HotkeyActions.ScreenPin,
            HotkeyActions.ScreenPinToggleHidden, HotkeyActions.ScreenPinClickThrough,
        })
            Assert.True(defaults[action].Modifiers.HasFlag(HotkeyModifiers.NoRepeat), $"{action} 应为裸键且不重复触发");
    }

    [Fact]
    public void Defaults_OcrIsDeliberatelyUnbound_ButStillListedForBinding()
    {
        // 识字是低频动作且入口已有三处（动作条、贴图右键、托盘）⇒ 不占默认键，
        // 但必须出现在设置页里，否则想绑的人找不到地方（"只提示不阻碍"之外还要"够得着"）
        Assert.False(HotkeyBindings.Defaults().ContainsKey(HotkeyActions.ScreenOcr));
        Assert.Contains(HotkeyActions.ScreenOcr, HotkeyActions.All());
        // 穿透反过来：它有默认键，因为穿透中的窗除了全局键没有任何自救路径
        Assert.True(HotkeyBindings.Defaults().ContainsKey(HotkeyActions.ScreenPinClickThrough));
    }

    [Fact]
    public void Defaults_ScreenKeys_DoNotCollideWithEachOtherOrTheMainWindow()
    {
        var defaults = HotkeyBindings.Defaults();
        var keys = defaults.Values.Select(HotkeyGesture.GestureKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void ScreenActions_AllShareOneCategory_AndAppearInOrder()
    {
        var screen = new[]
        {
            HotkeyActions.ScreenCapture, HotkeyActions.ScreenPin,
            HotkeyActions.ScreenPinToggleHidden, HotkeyActions.ScreenPinClickThrough, HotkeyActions.ScreenOcr,
        };
        foreach (var action in screen)
        {
            Assert.Equal("截图 / 贴图 / 识字", HotkeyActions.CategoryOf(action));
            Assert.Contains(action, HotkeyActions.All());
        }
        Assert.Contains("截图 / 贴图 / 识字", HotkeyActions.CategoryOrder);
        // 每个动作都要有自己的中文名：设置页逐行渲染，漏一个就显示成英文 id
        foreach (var action in screen)
        {
            var name = HotkeyActions.DisplayName(action);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.NotEqual(action, name);
        }
    }
}
