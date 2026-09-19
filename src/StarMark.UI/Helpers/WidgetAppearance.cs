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

            // 纯色（Solid）：照搬 DeskBox 的 TransparentTintBackdrop 观感 —— 整窗玻璃化 +
            // 半透明染色表面。窗口玻璃化后桌面从客户区透出，内容表面（见 SurfaceBrush）按「背景不透明度」
            // 调 Alpha，于是「背景不透明度」直接决定组件对桌面的透明程度：调低就能看见壁纸，
            // 调高则是实色面板。这就是 DeskBox「纯色材质下通过背景不透明度达成透明」的效果。
            // 不挂原生控制器（避免再把霜化层叠一层），只靠玻璃化 + 染色表面两层叠加。
            if (kind == WidgetBackdropKind.Solid)
            {
                DetachAcrylic(state);
                DetachMica(state);
                window.SystemBackdrop = null;
                WindowInterop.SetDwmSystemBackdropNone(window);
                WindowInterop.ApplyFullWindowFrame(window);
                WindowInterop.SetImmersiveDarkMode(window, isDark);
                return;
            }

            // 不透明（None）：什么都不挂，内容表面铺实色（见 SurfaceBrush）。关掉整窗玻璃化，避免透明。
            if (kind == WidgetBackdropKind.None)
            {
                DetachAcrylic(state);
                DetachMica(state);
                window.SystemBackdrop = null;
                WindowInterop.SetDwmSystemBackdropNone(window);
                WindowInterop.ClearFullWindowFrame(window);
                WindowInterop.SetImmersiveDarkMode(window, isDark);
                return;
            }

            // 整窗玻璃化（DeskBox 的 ApplyFullWindowFrame）：原生霜化层能透出来的前提。
            // 缺这一步时窗口客户区由系统按不透明底色绘制，控制器再怎么调也看不见。
            WindowInterop.ApplyFullWindowFrame(window);
            WindowInterop.SetImmersiveDarkMode(window, isDark);

            var accent = AccentColor();
            var tint = WidgetMaterialVisualCalculator.BuildContentTintColor(isDark, accent);

            state.Target ??= window.As<ICompositionSupportsSystemBackdrop>();
            state.Config ??= new SystemBackdropConfiguration();
            state.Config.IsInputActive = true;
            state.Config.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            // 主题翻转后必须重新下发配置：控制器仅在「首次挂载」时 SetSystemBackdropConfiguration，
            // 若之后切换浅/深色却不再下发，霜化层会停留在旧主题观感 —— 这正是「浅色模式材质不正确」的根因之一。
            if (state.AcrylicAttached) state.Acrylic?.SetSystemBackdropConfiguration(state.Config);
            if (state.MicaAttached) state.Mica?.SetSystemBackdropConfiguration(state.Config);

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
    /// 组件 / 主窗口的内容表面画笔（两者共用同一套，保证「设置里怎么调、两边就怎么变」）。
    /// <list type="bullet">
    /// <item>原生材质（亚克力 / 云母）→ <see cref="WidgetMaterialVisualCalculator.BuildNativeSurfaceColor"/>：
    /// Alpha 吃「背景不透明度」、染色浓度吃「材质浓度」；
    /// 早先这里一律返回透明，于是原生材质的背景完全由控制器说了算，
    /// 一旦霜化层没透出来（缺整窗玻璃化），两个滑块就彻底失效。</item>
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
        var dark = theme == ElementTheme.Dark;
        var opacity = Math.Clamp(opacityOverride ?? Opacity(), 0.0, 1.0);

        if (kind is WidgetBackdropKind.Acrylic or WidgetBackdropKind.AcrylicBase
            or WidgetBackdropKind.Mica or WidgetBackdropKind.MicaAlt)
        {
            return new SolidColorBrush(WidgetMaterialVisualCalculator.BuildNativeSurfaceColor(
                dark, AccentColor(), opacity, MaterialIntensity()));
        }

        if (kind == WidgetBackdropKind.Solid)
        {
            // 与 DeskBox 的 ContentWidgetWindow.ApplySurfaceStyle 同一套取色：
            // BuildContentSolidSurfaceColor 内部已按 surfaceOpacity（背景不透明度）调整 Alpha，
            // 配合窗口整窗玻璃化（ApplyBackdrop 的 Solid 分支），背景不透明度直接决定组件对桌面的透明程度。
            // 强调色必须取系统强调色（DeskBox 走 ThemeService.GetEffectiveAccentColor），
            // 用固定蓝会让纯色和 DeskBox 明显不是一个色。
            var solid = WidgetMaterialVisualCalculator.BuildContentSolidSurfaceColor(
                dark, AccentColor(), opacity);
            return new SolidColorBrush(solid);
        }

        // 不透明（None）：完全实色，不受「背景不透明度」滑杆影响
        // （该滑杆只作用于有透明的材质：Solid / 亚克力 / 云母）。浅/深主题各自给纯黑/纯白实色。
        var baseColor = dark ? Colors.Black : Colors.White;
        return new SolidColorBrush(baseColor);
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
        // 未开启主窗口材质：老老实实铺主题实色（不受不透明度滑杆影响，避免正文可读性被拖累）
        if (!translucent)
            return ThemeBrush.For(theme, "ApplicationPageBackgroundThemeBrush")
                   ?? SurfaceBrush(theme, WidgetBackdropKind.None, 1.0);

        // 开启后与组件走同一套表面色：两个滑块对主界面与组件必须是同一套观感，
        // 各写一套正是「主界面调了没反应 / 组件调了才变」的来源。
        return SurfaceBrush(theme, kind);
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
