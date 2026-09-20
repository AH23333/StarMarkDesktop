#nullable enable
using Windows.UI;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 桌面组件 / 主窗口「macOS 风」材质的取色计算（源自 DeskBox 的 WidgetMaterialVisualCalculator，
/// 但批次 K 起只保留「扁平背衬着色」这一条可靠路径所需的映射）。
/// <para>
/// 每个材质产出一个 <see cref="Color"/>（含 alpha）交给 <c>TransparentTintBackdrop</c> 铺满整窗：
/// 纯色（Solid）的 alpha 由用户「背景不透明度」驱动，其余材质用各自固定 alpha（见
/// <see cref="BuildMaterialBackdropColor"/>）。「材质浓度」滑杆已废弃，内部统一取
/// <see cref="DefaultWidgetMaterialIntensity"/>，仅影响染色浓淡、不再对外可调。
/// </para>
/// </summary>
internal static class WidgetMaterialVisualCalculator
{
    public const double MinWidgetMaterialIntensity = 0.0;
    public const double MaxWidgetMaterialIntensity = 1.0;
    public const double DefaultWidgetMaterialIntensity = 0.65;

    /// <summary>默认强调色（用于给底色掺一点点彩，呈 macOS 那种淡冷调）。</summary>
    public static readonly Color DefaultAccentColor = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);

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

    /// <summary>
    /// 半透明材质（亚克力 / 云母）的<b>背衬</b>取色：
    /// <list type="bullet">
    /// <item>Alpha 由 <paramref name="surfaceOpacity"/> 在该材质专属区间内插值——越低越能透出壁纸；</item>
    /// <item>强调色浓淡由 <paramref name="materialIntensity"/> 决定。</item>
    /// </list>
    /// <b>按 kind 分化</b>（批次 G2）：四种原生材质各给不同的 Alpha 区间与强调色偏向（亚克力薄最通透、
    /// 亚克力厚略实、云母偏实且低染色、云母 Alt 最取壁纸调），避免「不同材质呈现同一效果」。
    /// </summary>
    public static Color BuildNativeSurfaceColor(
        bool isDark,
        Color accentColor,
        double surfaceOpacity,
        double materialIntensity,
        WidgetBackdropKind kind)
    {
        double intensity = NormalizeMaterialIntensity(materialIntensity);
        double opacity = NormalizeOpacity(surfaceOpacity);

        // 各材质的 (Alpha 下限, Alpha 上限, 强调色偏向)。
        var (alphaFloor, alphaCeiling, accentBias) = kind switch
        {
            WidgetBackdropKind.AcrylicBase => (0.12, 0.92, +0.06),  // 厚亚克力：比薄略实、略染色
            WidgetBackdropKind.Mica        => (0.30, 0.98, -0.02),  // 云母：偏实、克制染色（高级哑光）
            WidgetBackdropKind.MicaAlt     => (0.38, 0.99, +0.08),  // 云母 Alt：最取壁纸/强调色调
            _                              => (0.06, 0.80,  0.00),  // 薄亚克力：最通透、苹果味
        };

        var tinted = BuildAccentSurfaceColor(
            isDark,
            accentColor,
            BuildContentTintColor(isDark, accentColor),
            accentMix: Math.Clamp(Lerp(0.06, 0.40, intensity) + accentBias, 0.0, 1.0),
            overlayMix: Lerp(0.02, 0.12, intensity));

        return ApplySurfaceOpacity(tinted, Lerp(alphaFloor, alphaCeiling, opacity));
    }

    /// <summary>
    /// 材质整窗背衬的统一取色（批次 K）。每种材质给一个<b>固定透明度性格</b>，只有纯色（Solid）吃用户的
    /// 「背景不透明度」滑杆——于是「不透明度只对纯色有效」成为字面事实，且各材质在任何机器上都必然
    /// 可见、互不相同、绝不全黑（背衬是合成的 CompositionColorBrush，不依赖 DWM 霜化合成）。
    /// <para>固定 alpha 性格：亚克力薄最通透 &lt; 亚克力厚 &lt; 云母 Alt &lt; 云母（偏实哑光）。</para>
    /// </summary>
    public static Color BuildMaterialBackdropColor(
        bool isDark,
        Color accentColor,
        WidgetBackdropKind kind,
        double solidOpacity)
        => kind switch
        {
            WidgetBackdropKind.Solid => BuildContentSolidSurfaceColor(isDark, accentColor, solidOpacity),
            WidgetBackdropKind.AcrylicBase => BuildNativeSurfaceColor(isDark, accentColor, 0.62, DefaultWidgetMaterialIntensity, kind),
            WidgetBackdropKind.Mica => BuildNativeSurfaceColor(isDark, accentColor, 0.90, DefaultWidgetMaterialIntensity, kind),
            WidgetBackdropKind.MicaAlt => BuildNativeSurfaceColor(isDark, accentColor, 0.82, DefaultWidgetMaterialIntensity, kind),
            WidgetBackdropKind.None => isDark ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            _ /* Acrylic 薄 */ => BuildNativeSurfaceColor(isDark, accentColor, 0.46, DefaultWidgetMaterialIntensity, kind),
        };

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
            NormalizeOpacity(surfaceOpacity));
    }

    private static double NormalizeMaterialIntensity(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinWidgetMaterialIntensity, MaxWidgetMaterialIntensity)
            : DefaultWidgetMaterialIntensity;

    /// <summary>
    /// 背景不透明度归一。注意 <see cref="Math.Clamp(double,double,double)"/> 对 NaN 两比较皆 false 会<b>原样返回 NaN</b>，
    /// 随后 Lerp→(byte)Math.Round(NaN*255) 得 0 → 面板 alpha 0 完全隐形。非法/NaN 一律回落 1.0（最不透明、可见）。
    /// </summary>
    private static double NormalizeOpacity(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 1.0;

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
