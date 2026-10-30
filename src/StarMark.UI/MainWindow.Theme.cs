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

/// <summary>MainWindow 的主题切换、材质与图标刷新。</summary>
public sealed partial class MainWindow
{
    private bool _refreshingAppearance;

    /// <summary>按主题偏好刷新主窗口主题与材质，自定义颜色仅作用于背景。</summary>
    public void RefreshAppearance()
    {
        // RequestedTheme 会触发 ActualThemeChanged，阻止递归刷新。
        if (_refreshingAppearance) return;
        _refreshingAppearance = true;
        try
        {
            var baseTheme = TargetTheme(_themePref);
            var kind = _settings.LoadMainWindowBackdrop();
            var opacity = _settings.LoadMainWindowOpacity();
            var translucent = kind != WidgetBackdropKind.None;
            var customTint = ThemeAccent.Custom;
            // 自定义颜色只修改背景，按钮和选项区域仍由主题偏好控制。
            if (RootGrid.RequestedTheme != baseTheme)
                RootGrid.RequestedTheme = baseTheme;

            WidgetAppearance.ApplyBackdrop(this, kind, opacity, baseTheme, customTint);
            // 背衬生效时只铺一层；实色和背衬失效时由根背景接管。
            var backdropActive = translucent && WidgetAppearance.IsBackdropSurfaceActive(this);
            if (backdropActive)
            {
                RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (kind == WidgetBackdropKind.None)
            {
                // 实色显式取色，避免框架背景画笔命中冻结的应用主题。
                var lightSurface = baseTheme != ElementTheme.Dark;
                if (customTint is { } tint)
                {
                    RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, tint.R, tint.G, tint.B));
                }
                else
                {
                    RootGrid.Background = new SolidColorBrush(lightSurface
                        ? Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3)
                        : Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
                }
                StarLog.InfoThrottled($"main-solid:{_themePref}",
                    $"[材质诊断] 实色(None) 主窗底色：baseTheme={baseTheme} pref={_themePref} customTint={customTint}",
                    windowMs: 5000);
            }
            else
            {
                RootGrid.Background = WidgetAppearance.MainWindowSurfaceBrush(baseTheme, kind, translucent, opacity, customTint);
            }
            ApplyTitleBarButtonColors();
            SetSourceButtonsHighlight(_currentSource);
            SetStatusDot(_statusKind);
            // 独立弹窗不会继承主窗口主题，需显式同步主题偏好。
            CenteredDialog.ApplyThemeToOpenDialogs(_themePref);
            // 画板那几扇同理（片147）：引擎那颗 theme prop 只有宿主递得进去，
            // 而它⛔ 在主窗的 RequestedTheme 底下——不推一次＝换主题时画板停在旧的那一档。
            StarMark.UI.Boards.BoardsWindows.ReapplyAppearance();
        }
        catch (Exception ex)
        {
            StarLog.Error("应用主窗口半透明材质失败", ex);
        }
        finally
        {
            _refreshingAppearance = false;
        }
    }

    /// <summary>把主题偏好推导为可用于 <see cref="ThemeBrush.For"/> 的元素主题（Default 跟随系统）。</summary>
    private static ElementTheme TargetTheme(ThemePreference pref) => pref switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ThemeManager.IsSystemDark() ? ElementTheme.Dark : ElementTheme.Light,
    };

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themePref = _themePref switch
        {
            ThemePreference.Default => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            _ => ThemePreference.Default,
        };
        ApplyThemePreference(_themePref);
    }

    /// <summary>
    /// 套用一个主题偏好并把它传导到所有该知道的地方：主窗、标题栏按钮、组件、设置页。
    /// <b>标题栏一键循环与托盘直接指定都走这里</b>——这条链有 6 步，写两遍迟早有一遍漏掉某一步
    /// （漏掉"组件主题"就是当初"切主题后组件不跟"那一类缺陷的形状）。
    /// </summary>
    private void ApplyThemePreference(ThemePreference pref)
    {
        _themePref = pref;
        _settings.SaveTheme(pref);
        UpdateThemeIcon();
        RefreshAppearance();   // 同步主题偏好与背景材质
        ApplyTitleBarButtonColors();                 // 标题栏按钮高亮随主题
        _ = _widgetManager.ApplyThemeToAllAsync(pref); // 同步组件主题（组件是独立窗口，不会自动传导）
        // 通知设置页同步主题选择（设置页已打开时尤其关键）
        ThemePreferenceQuickSwitched?.Invoke(pref);
    }

    private void UpdateThemeIcon()
    {
        ThemeIcon.Glyph = _themePref switch
        {
            ThemePreference.Light => "\uE706", // 太阳
            ThemePreference.Dark => "\uE708",  // 月亮
            _ => "\uE895",                     // 自动/同步
        };
        ToolTipService.SetToolTip(ThemeButton, _themePref switch
        {
            ThemePreference.Light => "浅色模式（点击切换）",
            ThemePreference.Dark => "深色模式（点击切换）",
            _ => "跟随系统（点击切换）",
        });
    }

    public void RefreshThemeIcon(ThemePreference pref)
    {
        _themePref = pref;
        UpdateThemeIcon();
        RefreshAppearance();   // 设置页切换主题后同步刷新主界面背景
        ApplyTitleBarButtonColors();                 // 标题栏按钮高亮随主题
        _ = _widgetManager.ApplyThemeToAllAsync(pref); // 同步组件主题（组件是独立窗口，不会自动传导）
    }
}
