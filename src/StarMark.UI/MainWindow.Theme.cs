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
/// MainWindow 的这一段——主题这一头：跟随系统/浅/深三档的切换与图标刷新。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    /// <summary>
    /// 重新应用毛玻璃材质（亚克力 / 云母 / 不透明）。
    /// 主窗口可选择是否跟随组件的同款材质（设置页「常规 → 外观」）；开启后根网格设为透明，
    /// 让霜化背景透出（与组件一致）。
    /// </summary>
    public void RefreshAppearance()
    {
        try
        {
            // 主窗口材质独立于组件（批次 J：拆「主窗口使用同一材质」为各自可选）。
            // 未选材质（None）即不透明，沿用旧的「开关关」语义；其余材质与组件走同一套用色逻辑。
            var kind = _settings.LoadMainWindowBackdrop();
            var translucent = kind != WidgetBackdropKind.None;
            // 优先取窗口"实际主题"（RootGrid 已被 ThemeManager 盖成具体 Light/Dark，且随系统/偏好翻转），
            // 仅在实际主题还是 Default（尚未落地）时才回落到按偏好推导。
            // 组件窗口一直是这么做的，故组件浅色正常、主窗曾出现浅色仍偏黑。
            var theme = RootGrid.ActualTheme == ElementTheme.Default
                ? TargetTheme(_themePref)
                : RootGrid.ActualTheme;
            WidgetAppearance.ApplyBackdrop(
                this, kind, _settings.LoadMainWindowOpacity(), theme);
            // 主题色一律按目标主题解析（ThemeBrush.For），不能取 Application.Current.Resources[key]
            // —— 应用级主题在窗口创建后冻结，那里解析出的永远是初始主题的画笔。
            // 表面画笔跟着材质走（与组件 ApplyAppearanceCore 同构，保证主界面/组件同款观感、同步切换）：
            // ① 背衬画笔已接管整窗（除实色外的亚克力/云母/纯色）时，RootGrid 透明让背衬单独透出着色，
            //    避免与背衬叠两层 alpha（发灰/过实）；② 实色或未生效时回落 MainWindowSurfaceBrush 实色，
            //    绝不变全透明幽灵窗。批次 K 起材质统一走扁平背衬，不再区分「原生控制器 / 纯色」两条路径。
            var backdropActive = translucent && WidgetAppearance.IsBackdropSurfaceActive(this);
            if (backdropActive)
            {
                RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (kind == WidgetBackdropKind.None)
            {
                // 实色（None）：不再交回框架 XAML `{ThemeResource ApplicationPageBackgroundThemeBrush}`——
                // 批次 O 曾假设它"浅色即浅色"，但真机在浅色下仍解析出深色/纯黑（该内置画笔的 ThemeDictionaries
                // 桶在运行期是 ResourceDictionaryThemeData，XAML 侧偶发命中深色 Default 桶）。
                // 改为按窗口"实际主题"显式铺主题化实色：浅色＝浅灰白、深色＝柔和深灰（非纯黑），且随
                // ActualThemeChanged（构造期已订阅）在切换时重铺。绝不依赖任何可能判错的框架画笔资源。
                var lightSurface = RootGrid.ActualTheme != ElementTheme.Dark;
                RootGrid.Background = new SolidColorBrush(lightSurface
                    ? Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3)
                    : Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
                // 节流：RefreshAppearance 在拖滑杆时按 350 ms 自动保存的节奏被调，这句会成串刷屏。
                StarLog.InfoThrottled($"main-solid:{_themePref}",
                    $"[材质诊断] 实色(None) 主窗底色按实际主题铺：ActualTheme={RootGrid.ActualTheme} pref={_themePref} → {(lightSurface ? "浅" : "深")}",
                    windowMs: 5000);
            }
            else
            {
                // 半透明材质但背衬未挂上（防御路径）：用按 isDark 直接算色的 SurfaceBrush，可靠不分主题。
                RootGrid.Background = WidgetAppearance.MainWindowSurfaceBrush(theme, kind, translucent, _settings.LoadMainWindowOpacity());
            }
            // 主题切换后代码构建的画笔重解析（来源按钮高亮、状态点）
            SetSourceButtonsHighlight(_currentSource);
            SetStatusDot(_statusKind);
            // 弹窗是独立顶层窗口：主窗只改自己的根主题传导不到它们，必须显式重刷，
            // 否则切深浅色时"仍打开的弹窗全部停在旧主题"（外观编辑器/预览/标签/确认框都在内）。
            CenteredDialog.ApplyThemeToOpenDialogs(_themePref);
        }
        catch (Exception ex)
        {
            StarLog.Error("应用主窗口半透明材质失败", ex);
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
        ThemeManager.Apply(this, pref);
        UpdateThemeIcon();
        RefreshAppearance();   // 主题画笔按窗口实际主题重新解析，否则一键切换后主界面背景色不跟随
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
        ThemeManager.Apply(this, pref);
        UpdateThemeIcon();
        RefreshAppearance();   // 设置页切换主题后同步刷新主界面背景
        ApplyTitleBarButtonColors();                 // 标题栏按钮高亮随主题
        _ = _widgetManager.ApplyThemeToAllAsync(pref); // 同步组件主题（组件是独立窗口，不会自动传导）
    }
}
