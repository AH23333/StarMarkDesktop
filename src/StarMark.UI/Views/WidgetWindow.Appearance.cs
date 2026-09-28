#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Health;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——外观投影这一头：材质/圆角/前景色/字号缩放，以及"样式只需上一次"的那些缓存。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetWindow
{

    private void ApplyTopmost()
    {
        var dark = RootBorder.ActualTheme == ElementTheme.Dark;
        if (_config.Topmost)
        {
            // 置顶：必须是普通顶层窗口（脱离桌面层），再用 WS_EX_TOPMOST 真正常驻最前。
            if (_layerAttached)
            {
                WidgetLayerService.DetachFromDesktopLayer(WindowInterop.GetHwnd(this));
                _layerAttached = false;
            }
            WindowInterop.SetTopmost(this, true);
            // 按钮始终可见：置顶时高亮强调色
            PinIcon.Foreground = ThemeBrush.Resolve(dark, "AppAccentBrush");
            PinButton.Background = ThemeBrush.Resolve(dark, "AppAccentSoftBrush");
            ToolTipService.SetToolTip(PinButton, "取消置顶");
        }
        else
        {
            WindowInterop.SetTopmost(this, false);
            // 默认未开启：挂到桌面层（贴在桌面上）；按钮仍清晰可见（次级前景色，不再透明不可见）。
            if (!_layerAttached) AttachToDesktopLayer();
            PinIcon.Foreground = ThemeBrush.Resolve(dark, "TextFillColorSecondaryBrush");
            PinButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ToolTipService.SetToolTip(PinButton, "置顶显示");
        }
    }

    /// <summary>
    /// 应用「毛玻璃材质 + 表面不透明度 + 每实例外观覆盖」（构造时与设置变更后共用）。
    /// 每实例可在 <see cref="WidgetInstanceConfig.Appearance"/> 覆盖材质/背景/前景/边框/圆角/文本缩放，
    /// 任一字段为 null 即回退到全局设置（设置页「常规 → 外观」）。
    /// </summary>
    private void ApplyAppearanceCore()
    {
        try
        {
            var ov = _config.Appearance;
            var globalKind = WidgetAppearance.Backdrop();

            // 自定义背景色优先覆盖材质（实色铺满会盖住霜化背景，故强制 None）
            var useCustomBg = !string.IsNullOrWhiteSpace(ov?.BackgroundColor);
            var kind = useCustomBg ? StarMark.Abstractions.WidgetBackdropKind.None : (ov?.Backdrop ?? globalKind);
            StartupProfile.Measure($"组件材质挂载 {_kind}", () => WidgetAppearance.ApplyBackdrop(
                this, kind, WidgetAppearance.Opacity(), RootBorder.ActualTheme), logWhenMs: 10);

            Brush surface = useCustomBg
                ? (WidgetAppearance.ParseColorBrush(ov!.BackgroundColor!) ?? WidgetAppearance.SurfaceBrush(RootBorder.ActualTheme, globalKind))
                : WidgetAppearance.SurfaceBrush(RootBorder.ActualTheme, kind);

            // 背衬画笔已接管整窗着色（除实色 None 外的所有材质：亚克力/云母/纯色）时，内容表面必须透明，
            // 让这一层扁平背衬单独着色——若再叠一层内容实色就会两次 alpha 叠加导致过实/发灰。
            // 背衬未生效（理论上不会，实色 None 走 else）时保留 SurfaceBrush 的实色兜底，绝不变透明幽灵窗。
            if (!useCustomBg &&
                kind != StarMark.Abstractions.WidgetBackdropKind.None &&
                WidgetAppearance.IsBackdropSurfaceActive(this))
            {
                surface = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            RootBorder.Background = surface;
            DragBar.Background = surface;

            // 前景（文本）色 + 文本缩放
            ApplyTextAppearanceCore();

            // 边框色 / 粗细
            RootBorder.BorderBrush = !string.IsNullOrWhiteSpace(ov?.BorderColor)
                ? WidgetAppearance.ParseColorBrush(ov.BorderColor!) ?? RootBorder.BorderBrush
                : (ThemeBrush.For(RootBorder.ActualTheme, "WidgetBorderBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Gray));
            RootBorder.BorderThickness = ov?.BorderThickness is { } bt
                ? new Thickness(Math.Clamp(bt, 0, 12))
                : new Thickness(1);

            // 圆角：内部 Border 跟随圆角（内容裁进圆角矩形），同时把「窗口本身」用 SetWindowRgn
            // 裁成圆角矩形——这样圆角改变的是组件真实外形（半径 0 即直角窗口），而不只是内部形状。
            var radius = ov?.CornerRadius is { } cr ? Math.Clamp(cr, 0, 48) : 8;
            _currentCornerRadius = radius;
            RootBorder.CornerRadius = new CornerRadius(radius);
            if (DragBar is not null)
                DragBar.CornerRadius = new CornerRadius(radius, radius, 0, 0);
            if (ContentScroll is not null)
                ContentScroll.CornerRadius = new CornerRadius(radius);
            ApplyRoundedWindow();   // 真正圆化窗口（任意模式下都生效）

            // 文本缩放：改为「直接缩放文本字号」（文本本身缩放），而非整块内容相对中心放缩，
            // 故放大时文本仍留在布局内、ScrollViewer 可滚动查看，不会因超出组件范围被裁切而消失。
            // 实际遍历在 ApplyForeground 的 SetFg 中与前景色一起套用（二者共享一次可视树遍历）。
        }
        catch (Exception ex)
        {
            // 兜底：外观相关的任何异常都不能冒出构造函数——组件窗口在构造期抛错会让
            // 「全部显示」「恢复组件」等操作直接演变成 UI 线程未处理异常（应用卡死后崩溃）。
            StarLog.Error($"应用组件外观失败 ({_kind})", ex);
            try
            {
                var fallback = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                RootBorder.Background = fallback;
                DragBar.Background = fallback;
            }
            catch { }
        }
    }

    /// <summary>
    /// 只套用「文本相关」的外观（前景色 + 文本缩放），<b>不碰材质与背景</b>。
    /// <para>
    /// 调文本缩放时若走整块 <see cref="ApplyAppearanceCore"/>，会顺带把材质/背景重挂一遍 ——
    /// 用户看到的就是「我只是改了字号，背景颜色却变了」，且在材质设置未即时生效时尤为明显。
    /// 编辑器里拖字号滑杆只走这条路径。
    /// </para>
    /// </summary>
    private void ApplyTextAppearanceCore()
    {
        try { ApplyForeground(_config.Appearance); }
        catch (Exception ex) { StarLog.Error($"应用组件文本外观失败 ({_kind})", ex); }
    }

    /// <summary>
    /// 应用每实例前景（文本）色 + 文本缩放。关键约束（踩坑 #60）：WinUI 3 中任何“非 TextElement 容器”
    /// （Border / Panel / StackPanel 等）直接调用 SetValue/ClearValue(TextElement.ForegroundProperty)
    /// 都会触发原生 AccessViolation（0xc0000005，Corrupted-State，try/catch 捕获不到，直接杀进程）。
    /// 因此：
    ///  - 有前景色覆盖时：安全地遍历 ContentHost 可视树，仅对真正的文本元素（TextBlock 等 TextElement 子类）
    ///    通过其标准 Foreground setter 上色——绝不触碰容器的 TextElement.ForegroundProperty 附加属性；
    ///  - 无前景色覆盖时（恢复全局）：仅清除我们此前显式上过色的文本（_coloredMarker），让它们回到样式自带前景，
    ///    绝不误动未改过的文本（如 MutedText 的灰度）；
    ///  - 文本缩放：直接改 TextBlock.FontSize（文本本身缩放），而非对内容区做 RenderTransform（否则放大后文本
    ///    相对组件中心放缩、超出组件范围被裁切而消失）。基准字号记在文本元素自身（Helpers/WidgetTextScale），
    ///    保证反复套用不累加、系数回到 1.0 时能精确还原到初始加载大小。
    /// 延迟到 Loaded 之后执行，确保内容子元素已生成。
    /// </summary>
    private void ApplyForeground(WidgetAppearanceOverride? ov)
    {
        if (ContentHost is null) return;
        var panel = ContentHost;
        void SetFg()
        {
            try
            {
                if (WidgetAppearance.ParseColorBrush(ov?.ForegroundColor) is { } fg)
                {
                    SetForegroundDeep(panel, fg);
                }
                else
                {
                    // 恢复全局前景：清掉我们此前列过前景的文本，回到样式默认（ClearValue 安全，TextBlock.Foreground 是标准属性）
                    foreach (var (tb, _) in EnumerateColored())
                        tb.ClearValue(TextBlock.ForegroundProperty);
                    _coloredMarker.Clear();
                }
                // 文本缩放（与前景共享一次遍历）
                _textScale = ov?.TextScale is { } ts ? Math.Clamp(ts, 0.6, 1.8) : 1.0;
                ApplyTextScale(panel, _textScale);
            }
            catch (Exception ex)
            {
                StarLog.Error($"应用组件前景色/文本缩放失败 ({_kind})", ex);
            }
        }
        if (panel.IsLoaded) SetFg();
        else if (!_fgLoadedHooked) { _fgLoadedHooked = true; panel.Loaded += (_, _) => SetFg(); }
    }

    /// <summary>枚举曾被我们显式上过前景色的文本（跳过已回收的弱引用）。</summary>
    private IEnumerable<(TextBlock Tb, object _)> EnumerateColored()
    {
        // ConditionalWeakTable 无枚举 API，改用存活的弱引用快照（内容重建后旧引用自然失效）
        foreach (var weak in _coloredRefs.ToArray())
            if (weak.TryGetTarget(out var tb)) yield return (tb, null!);
    }

    /// <summary>
    /// 递归遍历可视树，仅给文本元素（TextBlock 等 TextElement 子类）设置前景色并记录标记
    /// （用于恢复全局时精准清除）。TextBlock.Foreground 是标准安全 setter，不会像容器上的
    /// SetValue(TextElement.ForegroundProperty) 那样 AV。
    /// </summary>
    private void SetForegroundDeep(DependencyObject parent, Brush fg)
    {
        var n = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb)
            {
                tb.Foreground = fg;
                if (!_coloredMarker.TryGetValue(tb, out _))
                {
                    _coloredMarker.AddOrUpdate(tb, new object());
                    _coloredRefs.Add(new WeakReference<TextBlock>(tb));
                }
            }
            SetForegroundDeep(child, fg);
        }
    }

    /// <summary>
    /// 递归遍历可视树，对文本元素按「基准字号 × 系数」设置 FontSize（文本本身缩放，留在布局内、可滚动）。
    /// 基准字号首次见到的 TextBlock 时记录（用其当前有效字号），后续均基于基准计算，故反复套用不累加。
    /// 时钟组件（ClockWidget）自行管理时间/日期字号（自适应 + 文本缩放系数），故在此跳过其内部文本、
    /// 直接把系数下发给它的 <see cref="ClockWidget.TextScale"/>，避免被这里再乘一次导致双重缩放。
    /// </summary>
    private void ApplyTextScale(DependencyObject parent, double scale)
    {
        var n = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ClockWidget cw)
            {
                // 时钟时间字号由组件自适应计算，缩放只通过系数下发，不在此直接改其内部 TextBlock
                cw.TextScale = scale;
                continue;
            }
            if (child is TextBlock tb)
            {
                // 基准只在此元素首次出现时记录一次；之后无论套用多少遍都基于同一个基准，
                // 因此不会出现「缩放后的值被当成新基准」这种越调越偏的问题。
                WidgetTextScale.CaptureBase(tb);
                WidgetTextScale.Apply(tb, scale);
            }
            ApplyTextScale(child, scale);
        }
    }

    /// <summary>
    /// 给「晚到」的文本补一次缩放套用。
    /// <para>
    /// 组件的不少文本是异步建出来的：天气的指标格与逐时格要等数据回来，速览的常看按钮要等查库，
    /// 列表项容器更是虚拟化、滚动到才创建。首次套用时它们根本不在树上，于是永远保持原始字号——
    /// 用户看到的就是「同一块组件里字号忽大忽小。
    /// </para>
    /// <para>
    /// 只在系数<b>不等于 1.0</b> 时才需要补扫：默认系数下新文本本就是原始字号，不扫也正确，
    /// 白扫反而会在拖动/缩放的布局抖动期反复遍历可视树。
    /// </para>
    /// </summary>
    private void ContentHost_LayoutUpdated(object? sender, object e)
    {
        if (ContentHost is null) return;
        if (WidgetTextScale.IsDefault(_textScale)) return;

        var now = DateTimeOffset.Now;
        if ((now - _lastTextPassAt) < TimeSpan.FromMilliseconds(400)) return;
        _lastTextPassAt = now;

        try { ApplyTextScale(ContentHost, _textScale); }
        catch (Exception ex) { StarLog.Error($"补套用文本缩放失败 ({_kind})", ex); }
    }

    /// <summary>设置变更后重新套用外观（材质 / 不透明度），由 WidgetManager 统一调用。</summary>
    public void RefreshAppearance()
    {
        if (!_styled) return;   // 尚未完成首次样式化的窗口（未 Show）无需刷
        ApplyAppearanceCore();
    }

    /// <summary>
    /// 主窗口切换主题后，把同一主题偏好下发到本组件并立即重挂材质/表面。
    /// 组件是独立窗口，主窗口 <c>ThemeManager.Apply</c> 不会自动传导；若只改材质而不改主题，
    /// 组件的 <see cref="RefreshAppearance"/> 仍会用旧的实际主题 → 组件主题与主界面不同步。
    /// </summary>
    public void ApplyTheme(StarMark.UI.Helpers.ThemePreference pref)
    {
        try { ThemeManager.Apply(this, pref); } catch { }
        RefreshAppearance();
    }

    /// <summary>
    /// 布局方案下发的"每实例自定义配置"：把外观覆盖写回窗口缓存的 config 并立即重挂材质/表面。
    /// 与 <see cref="ApplyBounds"/> 同理 —— ApplyLayoutCore 改的是磁盘侧新 Load 的对象，
    /// 窗口 _config 是另一份引用，必须显式下发，否则"应用布局后自定义外观不生效"。
    /// appearance 为 null 表示清除覆盖、回退当前全局设置主题。
    /// </summary>
    public void ApplyAppearance(WidgetAppearanceOverride? appearance)
    {
        if (ReferenceEquals(_config.Appearance, appearance)) return;
        _config.Appearance = appearance;
        // 材质套用是启动里头号嫌疑（日志里 5 秒内出现过 38 次 backdrop 应用），超过 30 ms 就留下证据
        StarMark.Abstractions.StartupProfile.Measure($"组件材质套用 {InstanceId}", RefreshAppearance, logWhenMs: 30);
    }
}
