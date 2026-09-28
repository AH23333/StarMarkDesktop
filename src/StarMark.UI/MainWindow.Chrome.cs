#nullable enable
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Abstractions.Language;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemTray;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI;

/// <summary>
/// MainWindow 的这一段——窗口外壳：沉浸式标题栏、按钮配色与可拖拽区。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    /// <summary>Win11 风格沉浸式标题栏：内容扩展到标题栏区域，标题栏按钮透明融合。</summary>
    private void SetupImmersiveTitleBar()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // 悬停/按下高亮由 ApplyTitleBarButtonColors 按主题设置；之前被设为全透明 → 最小化/窗口化/关闭
        // 按钮悬停无任何反馈。这里不再写死透明，改在 ApplyTitleBarButtonColors 内给出主题化中性色。
        ApplyTitleBarButtonColors();

        ApplyDragRects();
        SizeChanged += (_, _) => ApplyDragRects();
    }

    /// <summary>
    /// 系统标题栏按钮（最小化 / 窗口化 / 关闭）的悬停与按下高亮。
    /// 之前三个按钮的 Hover/Pressed 背景被写死为 <see cref="Colors.Transparent"/>，导致悬停无反馈；
    /// 这里按当前主题给出低透明度中性色（浅色压暗、深色提亮），让悬停/按下有可见高亮，对齐 WinUI 原生标题栏。
    /// 主题切换时调用，保证深浅色下都正确。
    /// </summary>
    private void ApplyTitleBarButtonColors()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            var isDark = TargetTheme(_themePref) == ElementTheme.Dark;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)   // 深色：约 20% 白
                : Windows.UI.Color.FromArgb(0x1F, 0x00, 0x00, 0x00);   // 浅色：约 12% 黑
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)   // 深色：约 33% 白
                : Windows.UI.Color.FromArgb(0x33, 0x00, 0x00, 0x00);   // 浅色：约 20% 黑
        }
        catch { }
    }

    private void ApplyDragRects()
    {
        // 拖拽区 = 顶栏中段（logo + 状态），右侧留给交互按钮与系统窗口按钮。
        // AppWindow.Size 与 SetDragRectangles 均以物理像素为单位，而 ActualHeight/ActualWidth 是 DIP；
        // 非 100% DPI 下不换算会让拖拽区高度不足、右边界压到按钮（低分屏拖不动、高分屏误触按钮）。
        var scale = WindowInterop.GetScale(this);
        var width = AppWindow.Size.Width;
        var heightDip = TopBar.ActualHeight > 0 ? TopBar.ActualHeight : 52;
        var height = (int)Math.Round(heightDip * scale);
        var buttonsRightPad = (int)Math.Round(150 * scale);   // 顶栏右侧 padding（避开系统窗口按钮）
        var buttonsWidthDip = TopBarButtons.ActualWidth > 0 ? TopBarButtons.ActualWidth : 150;
        var gap = (int)Math.Round(8 * scale);
        var dragW = Math.Max(0, width - buttonsRightPad - (int)Math.Round(buttonsWidthDip * scale) - gap);
        AppWindow.TitleBar.SetDragRectangles(new RectInt32[]
        {
            new() { X = 0, Y = 0, Width = dragW, Height = height },
        });
    }

    // ===== 主题切换 =====

    /// <summary>主窗口右上角快捷切换主题后触发（参数为新主题偏好）。设置页据此把自身的主题选择
    /// 同步过来，否则离开设置页时其兜底保存会用过期的 ThemeIndex 把主题强制切回旧值。</summary>
    public static event Action<ThemePreference>? ThemePreferenceQuickSwitched;
}
