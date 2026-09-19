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

    /// <summary>
    /// 系统强调色（DeskBox 取 <c>ThemeService.GetEffectiveAccentColor()</c>）。
    /// 拿不到时回落到 <see cref="WidgetMaterialVisualCalculator.DefaultAccentColor"/> ——
    /// 纯色材质掺的就是这个色，用错会让纯色和 DeskBox 明显不同。
    /// </summary>
    public static Windows.UI.Color AccentColor() => Try(() =>
    {
        var settings = new Windows.UI.ViewManagement.UISettings();
        var c = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        return c.A == 0
            ? WidgetMaterialVisualCalculator.DefaultAccentColor
            : Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B);
    }, WidgetMaterialVisualCalculator.DefaultAccentColor);

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

    /// <summary>
    /// 每个窗口的控制器状态。照搬 DeskBox 的**复用**策略：
    /// 切换材质族（亚克力↔云母）时只 <c>RemoveAllSystemBackdropTargets()</c> 摘掉挂载点，
    /// 控制器本身留在手里等下次复用 —— 每次都 Dispose + new 会漏原生合成内存与 DWM 句柄，
    /// 而这类泄漏 GC 与工作集修剪都收不回来。
    /// </summary>
    private sealed class WindowBackdropState
    {
        public DesktopAcrylicController? Acrylic;
        public bool AcrylicAttached;
        public MicaController? Mica;
        public bool MicaAttached;
        public SystemBackdropConfiguration? Config;
        public ICompositionSupportsSystemBackdrop? Target;
    }

    private static readonly ConditionalWeakTable<Window, WindowBackdropState> _states = new();

    /// <summary>
    /// 把毛玻璃材质真正挂到窗口上（构造期、设置变更后、DWM 主题翻转后共用）。
    /// 原生亚克力 / 云母：控制器接管背景，内容背景透明；不透明 / 纯色：控制器摘掉，内容表面铺实色。
    /// </summary>
    public static void ApplyBackdrop(Window window, WidgetBackdropKind kind, double surfaceOpacity, double intensity, ElementTheme theme)
    {
        try
        {
            var isDark = theme == ElementTheme.Dark;
            surfaceOpacity = Math.Clamp(surfaceOpacity, 0.0, 1.0);

            var state = _states.GetOrCreateValue(window);

            // 无材质 / 纯色：照搬 DeskBox —— 两者都不挂控制器，
            // 区别只在内容表面铺什么（None 跟主题黑白实色，Solid 铺带强调色的实色，见 SurfaceBrush）。
            if (kind == WidgetBackdropKind.None || kind == WidgetBackdropKind.Solid)
            {
                DetachAcrylic(state);
                DetachMica(state);
                window.SystemBackdrop = null;
                WindowInterop.SetDwmSystemBackdropNone(window);
                return;
            }

            var accent = AccentColor();
            var tint = WidgetMaterialVisualCalculator.BuildContentTintColor(isDark, accent);

            state.Target ??= window.As<ICompositionSupportsSystemBackdrop>();
            state.Config ??= new SystemBackdropConfiguration();
            state.Config.IsInputActive = true;
            state.Config.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

            var ok = kind is WidgetBackdropKind.Mica or WidgetBackdropKind.MicaAlt
                ? ApplyMica(state, isDark, tint, kind == WidgetBackdropKind.MicaAlt, surfaceOpacity, intensity)
                  || ApplyAcrylic(state, isDark, tint, false, surfaceOpacity, intensity)   // 不支持云母 → 回落亚克力
                : ApplyAcrylic(state, isDark, tint, kind == WidgetBackdropKind.AcrylicBase, surfaceOpacity, intensity)
                  || ApplyMica(state, isDark, tint, false, surfaceOpacity, intensity);      // 不支持亚克力 → 回落云母

            // 控制器接管时必须关掉 DWM 自带背景，否则 DWM 在控制器之上再叠一层默认亚克力（DeskBox 同款处理）
            window.SystemBackdrop = null;
            WindowInterop.SetDwmSystemBackdropNone(window);
            if (!ok) StarLog.Error($"当前平台不支持 {kind} 材质，已退化为实色表面");
        }
        catch (Exception ex)
        {
            StarLog.Error($"应用毛玻璃材质失败 ({kind})", ex);
            try { window.SystemBackdrop = null; } catch { }
        }
    }

    private static bool ApplyMica(WindowBackdropState state, bool isDark, Windows.UI.Color tint,
        bool useAlt, double surfaceOpacity, double intensity)
    {
        if (!MicaController.IsSupported()) return false;

        try
        {
            DetachAcrylic(state);
            // Kind 是可变属性：Base ↔ BaseAlt 复用同一个控制器即可
            state.Mica ??= new MicaController();
            if (!state.MicaAttached)
            {
                if (!state.Mica.AddSystemBackdropTarget(state.Target!)) return false;
                state.MicaAttached = true;
                state.Mica.SetSystemBackdropConfiguration(state.Config!);
            }

            state.Mica.Kind = useAlt ? MicaKind.BaseAlt : MicaKind.Base;
            state.Mica.TintColor = tint;
            state.Mica.FallbackColor = WidgetMaterialVisualCalculator.BuildMicaFallbackColor(isDark, useAlt);
            var profile = WidgetMaterialVisualCalculator.CalculateMica(isDark, useAlt, surfaceOpacity, intensity);
            state.Mica.TintOpacity = (float)profile.TintOpacity;
            state.Mica.LuminosityOpacity = (float)profile.LuminosityOpacity;
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("应用云母材质失败", ex);
            return false;
        }
    }

    private static bool ApplyAcrylic(WindowBackdropState state, bool isDark, Windows.UI.Color tint,
        bool useBase, double surfaceOpacity, double intensity)
    {
        if (!DesktopAcrylicController.IsSupported()) return false;

        try
        {
            DetachMica(state);
            state.Acrylic ??= new DesktopAcrylicController();
            if (!state.AcrylicAttached)
            {
                if (!state.Acrylic.AddSystemBackdropTarget(state.Target!)) return false;
                state.AcrylicAttached = true;
                state.Acrylic.SetSystemBackdropConfiguration(state.Config!);
            }

            state.Acrylic.Kind = useBase ? DesktopAcrylicKind.Base : DesktopAcrylicKind.Thin;
            state.Acrylic.TintColor = tint;
            state.Acrylic.FallbackColor = tint;
            var profile = WidgetMaterialVisualCalculator.CalculateAcrylic(isDark, useBase, surfaceOpacity, intensity);
            state.Acrylic.TintOpacity = (float)profile.TintOpacity;
            state.Acrylic.LuminosityOpacity = (float)profile.LuminosityOpacity;
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("应用亚克力材质失败", ex);
            return false;
        }
    }

    /// <summary>摘掉亚克力挂载点（控制器留着复用）。</summary>
    private static void DetachAcrylic(WindowBackdropState state)
    {
        if (state.Acrylic is null || !state.AcrylicAttached) return;
        try { state.Acrylic.RemoveAllSystemBackdropTargets(); } catch { }
        state.AcrylicAttached = false;
    }

    /// <summary>摘掉云母挂载点（控制器留着复用）。</summary>
    private static void DetachMica(WindowBackdropState state)
    {
        if (state.Mica is null || !state.MicaAttached) return;
        try { state.Mica.RemoveAllSystemBackdropTargets(); } catch { }
        state.MicaAttached = false;
    }

    /// <summary>窗口关闭时彻底释放（不释放会漏原生合成资源）。</summary>
    public static void ReleaseBackdrop(Window window)
    {
        if (!_states.TryGetValue(window, out var state)) return;
        try
        {
            if (state.Acrylic is { } a) { try { a.RemoveAllSystemBackdropTargets(); a.Dispose(); } catch { } }
            if (state.Mica is { } m) { try { m.RemoveAllSystemBackdropTargets(); m.Dispose(); } catch { } }
        }
        catch { }
        finally
        {
            state.Acrylic = null;
            state.Mica = null;
            state.AcrylicAttached = false;
            state.MicaAttached = false;
            _states.Remove(window);
        }
    }

    /// <summary>
    /// 组件内容表面画笔：
    /// <list type="bullet">
    /// <item>原生材质（亚克力 / 云母）→ 透明，让霜化背景透出；</item>
    /// <item><see cref="WidgetBackdropKind.None"/> → 跟随主题的黑白实色（按用户不透明度调 Alpha）；</item>
    /// <item><see cref="WidgetBackdropKind.Solid"/>（照搬 DeskBox）→ 「主题基色 + 强调色」混合后再按
    /// 用户不透明度调整 Alpha 的实色，即**纯色材质吃「背景不透明度」、不吃「材质浓度」**。</item>
    /// </list>
    /// </summary>
    public static Brush SurfaceBrush(ElementTheme theme, WidgetBackdropKind kind)
        => SurfaceBrush(theme, kind, null);

    /// <param name="opacityOverride">显式指定「背景不透明度」（主窗口用它强制不透明 / 跟随滑杆）。</param>
    public static Brush SurfaceBrush(ElementTheme theme, WidgetBackdropKind kind, double? opacityOverride)
    {
        if (kind is not (WidgetBackdropKind.None or WidgetBackdropKind.Solid))
            return new SolidColorBrush(Colors.Transparent);

        var dark = theme == ElementTheme.Dark;
        var opacity = Math.Clamp(opacityOverride ?? Opacity(), 0.0, 1.0);

        if (kind == WidgetBackdropKind.Solid)
        {
            // 与 DeskBox 的 ContentWidgetWindow.ApplySurfaceStyle 同一套取色：
            // BuildContentSolidSurfaceColor 内部已按 surfaceOpacity 调整 Alpha，
            // 故此处不再二次叠加（否则纯色会比预期更淡/更实）。
            // 强调色必须取系统强调色（DeskBox 走 ThemeService.GetEffectiveAccentColor），
            // 用固定蓝会让纯色和 DeskBox 明显不是一个色。
            var solid = WidgetMaterialVisualCalculator.BuildContentSolidSurfaceColor(
                dark, AccentColor(), opacity);
            return new SolidColorBrush(solid);
        }

        var baseColor = dark ? Colors.Black : Colors.White;
        var alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        return new SolidColorBrush(ColorHelper.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    /// <summary>便捷重载：按当前设置读出材质。</summary>
    public static Brush SurfaceBrush(ElementTheme theme) => SurfaceBrush(theme, Backdrop());

    /// <summary>
    /// 主窗口的表面画笔（照搬 DeskBox「材质表面」分层，但按主窗口的可读性做了适配）。
    /// <para>
    /// 为什么不能直接用 <see cref="SurfaceBrush"/>：主窗口的顶栏 / NavigationView / 页面
    /// 各自带不透明背景，原生材质即便挂上了也几乎被盖住 —— 用户拖「背景不透明度」看不出任何变化，
    /// 于是报「两个滑杆对主界面失效」。这里改为在原生材质之上再压一层**按不透明度调 Alpha 的主题色**，
    /// 让「背景不透明度」字面生效（越高越实、越低越透出霜化背景），与主窗口的实际观感一致。
    /// </para>
    /// </summary>
    public static Brush MainWindowSurfaceBrush(ElementTheme theme, WidgetBackdropKind kind, bool translucent)
    {
        var dark = theme == ElementTheme.Dark;

        // 未开启主窗口材质：老老实实铺主题实色（不受不透明度滑杆影响，避免正文可读性被拖累）
        if (!translucent)
            return ThemeBrush.For(theme, "ApplicationPageBackgroundThemeBrush")
                   ?? SurfaceBrush(theme, WidgetBackdropKind.None, 1.0);

        var opacity = Math.Clamp(Opacity(), 0.0, 1.0);

        if (kind == WidgetBackdropKind.Solid)
            return SurfaceBrush(theme, WidgetBackdropKind.Solid, opacity);

        if (kind == WidgetBackdropKind.None)
            return SurfaceBrush(theme, WidgetBackdropKind.None, opacity);

        // 原生材质（亚克力 / 云母）：霜化背景在窗口上，表面只压一层半透明主题色。
        // 不透明度 = 这层主题色的 Alpha：1.0 完全遮住霜化，0.3 几乎全透。
        var tint = WidgetMaterialVisualCalculator.BuildContentTintColor(dark, AccentColor());
        var alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        return new SolidColorBrush(ColorHelper.FromArgb(alpha, tint.R, tint.G, tint.B));
    }

    /// <summary>解析 #RRGGBB / #AARRGGBB 为实色画笔；格式非法或空返回 null（调用方据此回退主题）。</summary>
    public static SolidColorBrush? ParseColorBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex!.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return null;
        var a = (byte)((HexDig(s[0]) << 4) | HexDig(s[1]));
        var r = (byte)((HexDig(s[2]) << 4) | HexDig(s[3]));
        var g = (byte)((HexDig(s[4]) << 4) | HexDig(s[5]));
        var b = (byte)((HexDig(s[6]) << 4) | HexDig(s[7]));
        return new SolidColorBrush(ColorHelper.FromArgb(a, r, g, b));
    }

    private static int HexDig(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };
}
