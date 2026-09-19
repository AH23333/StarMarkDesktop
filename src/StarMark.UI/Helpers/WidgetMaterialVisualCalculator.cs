#nullable enable
using Windows.UI;

namespace StarMark.UI.Helpers;

/// <summary>
/// 桌面组件 / 主窗口「macOS 风」毛玻璃的视觉参数计算（直接照搬 DeskBox 的 WidgetMaterialVisualCalculator）。
/// <para>
/// 原生亚克力 / 云母的强度由 <see cref="DesktopAcrylicController"/> / <see cref="MicaController"/> 的
/// TintOpacity / LuminosityOpacity 控制，这里给出「主题 + 表面不透明度 + 材质浓度」到这些值的映射。
/// </para>
/// </summary>
internal static class WidgetMaterialVisualCalculator
{
    // DeskBox 的 SettingsService 区间常量（StarMark 直接内联为常量）
    public const double MinWidgetMaterialIntensity = 0.0;
    public const double MaxWidgetMaterialIntensity = 1.0;
    public const double DefaultWidgetMaterialIntensity = 0.65;

    /// <summary>默认强调色（用于给底色掺一点点彩，呈 macOS 那种淡冷调）。</summary>
    public static readonly Color DefaultAccentColor = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);

    public static WidgetMaterialOpacityProfile CalculateAcrylic(
        bool isDark,
        bool useBase,
        double surfaceOpacity,
        double materialIntensity)
    {
        double intensity = NormalizeMaterialIntensity(materialIntensity);
        double surfaceStrength = Lerp(0.08, 1.0, Math.Clamp(surfaceOpacity, 0.0, 1.0));
        double tintOpacity = useBase
            ? Lerp(isDark ? 0.18 : 0.12, isDark ? 0.72 : 0.62, intensity)
            : Lerp(isDark ? 0.04 : 0.02, isDark ? 0.42 : 0.34, intensity);
        double luminosityOpacity = useBase
            ? Lerp(isDark ? 0.38 : 0.46, isDark ? 0.82 : 0.90, intensity)
            : Lerp(isDark ? 0.16 : 0.22, isDark ? 0.56 : 0.64, intensity);

        return new WidgetMaterialOpacityProfile(
            Math.Clamp(tintOpacity * surfaceStrength, 0.0, 1.0),
            Math.Clamp(luminosityOpacity * surfaceStrength, 0.0, 1.0));
    }

    public static double CalculateLegacyAcrylicOpacity(
        bool useBase,
        double surfaceOpacity,
        double materialIntensity)
    {
        double surface = Math.Clamp(surfaceOpacity, 0.0, 1.0);
        double intensity = NormalizeMaterialIntensity(materialIntensity);
        double maximumOpacity = useBase ? 0.90 : 0.72;
        double opacity = Lerp(0.01, maximumOpacity, surface);

        // Win10 的 accent policy 只暴露一个 tint-alpha 控制（不像 Desktop Acrylic 有独立的
        // tint/luminosity），这里让 intensity 也能调节最终浓度，避免不透明度滑块失效。
        return Math.Clamp(opacity * Lerp(0.58, 1.0, intensity), 0.0, 1.0);
    }

    public static Color BuildLegacyAcrylicSurfaceOverlayColor(
        bool isDark,
        Color accentColor,
        bool useBase,
        double surfaceOpacity,
        double materialIntensity)
    {
        Color tintColor = BuildContentTintColor(isDark, accentColor);
        double legacyOpacity = CalculateLegacyAcrylicOpacity(useBase, surfaceOpacity, materialIntensity);

        // accent policy 在 Win10/VM/RDP 下可能不模糊也不染色，这里用一个轻量 XAML 染色兜底，
        // 让两个滑块在真实亚克力失败时仍然可见。
        double overlayOpacity = legacyOpacity * (useBase ? 0.72 : 0.62);
        return ApplySurfaceOpacity(tintColor, overlayOpacity);
    }

    /// <summary>
    /// 云母的强度剖面。<paramref name="surfaceOpacity"/>（背景不透明度）必须参与：
    /// 早先这里只吃「材质浓度」，于是在云母/云母 Alt 下拖动「背景不透明度」滑杆毫无变化 ——
    /// 用户报「部分材质下两个滑杆对所有组件都失效」正是这一条。
    /// 与亚克力同口径：先按主题+浓度算出目标强度，再乘表面强度因子。
    /// </summary>
    public static WidgetMaterialOpacityProfile CalculateMica(
        bool isDark,
        bool useAlt,
        double surfaceOpacity,
        double materialIntensity)
    {
        double intensity = NormalizeMaterialIntensity(materialIntensity);
        double surfaceStrength = Lerp(0.08, 1.0, Math.Clamp(surfaceOpacity, 0.0, 1.0));
        double tintOpacity = useAlt
            ? Lerp(0.28, 0.82, intensity)
            : Lerp(0.04, 0.46, intensity);
        double luminosityOpacity = useAlt
            ? Lerp(isDark ? 0.34 : 0.42, isDark ? 0.72 : 0.76, intensity)
            : Lerp(isDark ? 0.78 : 0.82, isDark ? 0.94 : 0.96, intensity);

        return new WidgetMaterialOpacityProfile(
            Math.Clamp(tintOpacity * surfaceStrength, 0.0, 1.0),
            Math.Clamp(luminosityOpacity * surfaceStrength, 0.0, 1.0));
    }

    public static Color BuildContentTintColor(bool isDark, Color accentColor)
    {
        var baseColor = isDark
            ? Color.FromArgb(0xFF, 0x20, 0x22, 0x26)
            : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        return BuildAccentSurfaceColor(
            isDark,
            accentColor,
            baseColor,
            accentMix: isDark ? 0.08 : 0.16,
            overlayMix: isDark ? 0.04 : 0.08);
    }

    public static Color BuildMicaFallbackColor(bool isDark, bool useAlt)
    {
        return useAlt
            ? isDark
                ? Color.FromArgb(0xFF, 0x16, 0x18, 0x1D)
                : Color.FromArgb(0xFF, 0xE8, 0xEA, 0xEF)
            : isDark
                ? Color.FromArgb(0xFF, 0x20, 0x22, 0x26)
                : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    }

    public static Color BuildContentSolidSurfaceColor(
        bool isDark,
        Color accentColor,
        double surfaceOpacity)
    {
        return ApplySurfaceOpacity(
            BuildAccentSurfaceColor(
                isDark,
                accentColor,
                isDark
                    ? Color.FromArgb(0xFF, 0x21, 0x24, 0x2A)
                    : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                accentMix: 0.18,
                overlayMix: isDark ? 0.15 : 0.04),
            Math.Clamp(surfaceOpacity, 0.0, 1.0));
    }

    private static double NormalizeMaterialIntensity(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinWidgetMaterialIntensity, MaxWidgetMaterialIntensity)
            : DefaultWidgetMaterialIntensity;

    private static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * Math.Clamp(progress, 0.0, 1.0));

    private static Color BuildAccentSurfaceColor(
        bool isDark,
        Color accentColor,
        Color baseColor,
        double accentMix,
        double overlayMix)
    {
        var mixed = BlendColors(baseColor, accentColor, accentMix);
        var overlay = isDark
            ? Color.FromArgb(0xFF, 0x2B, 0x2F, 0x36)
            : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        return BlendColors(mixed, overlay, overlayMix);
    }

    private static Color ApplySurfaceOpacity(Color color, double opacity) =>
        Color.FromArgb(
            (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255),
            color.R,
            color.G,
            color.B);

    private static Color BlendColors(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromArgb(
            0xFF,
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }
}

/// <summary>亚克力 / 云母材质的不透明度剖面（与 DeskBox 同名结构体对应）。</summary>
internal readonly record struct WidgetMaterialOpacityProfile(double TintOpacity, double LuminosityOpacity);
