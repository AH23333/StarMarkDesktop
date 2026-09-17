#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 组件 / 主窗口「macOS 风」外观：材质（亚克力 / 云母 / 不透明）+ 表面不透明度 + 材质浓度。
/// <para>
/// 直接照搬 DeskBox 的控制器方案：原生亚克力 / 云母用 <see cref="DesktopAcrylicController"/> /
/// <see cref="MicaController"/> 挂到 <see cref="ICompositionSupportsSystemBackdrop"/>，
/// 可精确控制 TintColor / TintOpacity / LuminosityOpacity / Kind，得到真正的霜化玻璃（而不是
/// 旧版用 72% 不透明实色盖住 SystemBackdrop 的「白盒子」观感）。内容背景在原生材质下设为透明，
/// 让霜化背景透出。
/// </para>
/// </summary>
public static class WidgetAppearance
{
    // 以下 getter 会在组件窗口构造函数、主窗口刷新、拖放/缩放等高频路径被调用，
    // 一律不得向外抛异常：读取失败时用默认值兜底并写日志（外观退化总好过整个窗口创建失败导致崩溃）。

    /// <summary>拖动 / 缩放时的边缘磁吸总开关（关闭 = 用户自由摆位，不做任何自动贴合）。</summary>
    public static bool SnapEnabled() => Try(() => new SettingsStore().LoadWidgetSnapEnabled(), true);

    public static WidgetBackdropKind Backdrop()
        => Try(() => new SettingsStore().LoadWidgetBackdrop(), WidgetBackdropKind.Acrylic);

    public static double Opacity()
        => Try(() => new SettingsStore().LoadWidgetOpacity(), DefaultOpacity);

    public static double MaterialIntensity()
        => Try(() => new SettingsStore().LoadWidgetMaterialIntensity(), WidgetMaterialVisualCalculator.DefaultWidgetMaterialIntensity);

    /// <summary>默认不透明度（0.72），与 SettingsStore 默认值保持一致。</summary>
    public const double DefaultOpacity = 0.72;

    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex)
        {
            StarLog.Error("读取组件外观设置失败，已回退默认值", ex);
            return fallback;
        }
    }

    // 每个窗口已有的控制器（用于切换材质时先拆掉旧的，避免泄漏原生合成资源 / DWM 句柄）
    private sealed class WindowBackdropState
    {
        public DesktopAcrylicController? Acrylic;
        public MicaController? Mica;
    }

    private static readonly ConditionalWeakTable<Window, WindowBackdropState> _states = new();

    /// <summary>
    /// 把毛玻璃材质真正挂到窗口上（构造期、设置变更后、DWM 主题翻转后共用）。
    /// 原生亚克力 / 云母：控制器接管背景，内容背景透明；不透明：控制器拆掉，内容背景回到实色。
    /// </summary>
    public static void ApplyBackdrop(Window window, WidgetBackdropKind kind, double surfaceOpacity, double intensity, ElementTheme theme)
    {
        try
        {
            var isDark = theme == ElementTheme.Dark;
            surfaceOpacity = Math.Clamp(surfaceOpacity, 0.0, 1.0);

            var state = _states.GetOrCreateValue(window);
            DetachControllers(state);

            if (kind == WidgetBackdropKind.None)
            {
                window.SystemBackdrop = null;
                WindowInterop.SetDwmSystemBackdropNone(window);
                return;
            }

            var target = window.As<ICompositionSupportsSystemBackdrop>();
            var config = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light,
            };

            if (kind == WidgetBackdropKind.Mica)
            {
                if (MicaController.IsSupported())
                {
                    var mica = new MicaController { Kind = MicaKind.Base };
                    mica.TintColor = WidgetMaterialVisualCalculator.BuildContentTintColor(isDark, WidgetMaterialVisualCalculator.DefaultAccentColor);
                    mica.FallbackColor = WidgetMaterialVisualCalculator.BuildMicaFallbackColor(isDark, useAlt: false);
                    var profile = WidgetMaterialVisualCalculator.CalculateMica(isDark, useAlt: false, intensity);
                    mica.TintOpacity = (float)profile.TintOpacity;
                    mica.LuminosityOpacity = (float)profile.LuminosityOpacity;
                    mica.SetSystemBackdropConfiguration(config);
                    if (mica.AddSystemBackdropTarget(target))
                    {
                        state.Mica = mica;
                        WindowInterop.SetDwmSystemBackdropNone(window);
                        return;
                    }
                    mica.Dispose();
                }
                // 不支持 Mica 时回落到亚克力
            }

            // 亚克力（默认材质）
            if (DesktopAcrylicController.IsSupported())
            {
                var acrylic = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
                acrylic.TintColor = WidgetMaterialVisualCalculator.BuildContentTintColor(isDark, WidgetMaterialVisualCalculator.DefaultAccentColor);
                acrylic.FallbackColor = acrylic.TintColor;
                var profile = WidgetMaterialVisualCalculator.CalculateAcrylic(isDark, useBase: true, surfaceOpacity, intensity);
                acrylic.TintOpacity = (float)profile.TintOpacity;
                acrylic.LuminosityOpacity = (float)profile.LuminosityOpacity;
                acrylic.SetSystemBackdropConfiguration(config);
                if (acrylic.AddSystemBackdropTarget(target))
                {
                    state.Acrylic = acrylic;
                    WindowInterop.SetDwmSystemBackdropNone(window);
                    return;
                }
                acrylic.Dispose();
            }

            // 平台不支持任何原生材质：回到实色（仍可半透明），保证至少有毛玻璃观感
            window.SystemBackdrop = null;
            WindowInterop.SetDwmSystemBackdropNone(window);
        }
        catch (Exception ex)
        {
            StarLog.Error($"应用毛玻璃材质失败 ({kind})", ex);
            try { window.SystemBackdrop = null; } catch { }
        }
    }

    private static void DetachControllers(WindowBackdropState state)
    {
        if (state.Acrylic is { } a)
        {
            try { a.RemoveAllSystemBackdropTargets(); a.Dispose(); } catch { }
            state.Acrylic = null;
        }
        if (state.Mica is { } m)
        {
            try { m.RemoveAllSystemBackdropTargets(); m.Dispose(); } catch { }
            state.Mica = null;
        }
    }

    /// <summary>
    /// 组件内容表面画笔：原生材质（亚克力 / 云母）下返回透明，让霜化背景透出；
    /// 不透明材质（None）返回用户不透明度的实色（深/浅随主题）。
    /// </summary>
    public static Brush SurfaceBrush(ElementTheme theme, WidgetBackdropKind kind)
    {
        if (kind != WidgetBackdropKind.None) return new SolidColorBrush(Colors.Transparent);
        var opacity = (byte)Math.Clamp((byte)(Opacity() * 255), (byte)0, (byte)255);
        var dark = theme == ElementTheme.Dark;
        var baseColor = dark ? Colors.Black : Colors.White;
        return new SolidColorBrush(ColorHelper.FromArgb(opacity, baseColor.R, baseColor.G, baseColor.B));
    }

    /// <summary>便捷重载：按当前设置读出材质。</summary>
    public static Brush SurfaceBrush(ElementTheme theme) => SurfaceBrush(theme, Backdrop());
}
