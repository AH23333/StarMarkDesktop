#nullable enable
using System;
using Windows.UI;
using StarMark.Abstractions;

namespace StarMark.Core.Appearance;

/// <summary>
/// 桌面组件 / 主窗口「macOS 风」材质<b>取色</b>的唯一主人（批次 WI-81 从 <c>WidgetMaterialVisualCalculator</c> 整颗搬下来）。
/// <para>
/// <b>为什么住在 Core</b>：这批要钉的判据是「换颜色⛔ 换材质」——选了自定义颜色之后，那一档材质的
/// <b>alpha 必须与不选时逐位相等</b>。那是颗纯算术，而测试工程引用得到 Core、引用不到 <c>StarMark.UI</c>
/// （坑表 #184：写下判据的那一刻就得确定它由哪个测试断言，否则只能出形状闸门）。
/// </para>
/// <para>
/// <b>两种"颜色"是两件事，⛔ 混成一颗</b>：
/// <list type="bullet">
/// <item><paramref name="accent"/>＝<b>给底色掺的染料</b>（整窗共用的一颗主题色，见 <see cref="AppPalette"/>）；</item>
/// <item><paramref name="tintOverride"/>＝某个组件「外观」里点定的<b>那一扇窗的底色</b>，它<b>整颗顶掉</b>
/// 掺了染料的底色，但⛔ 顶掉那一档材质的 alpha——「选透明材质后改颜色变成实色」正是把这两件事写成了一件事
/// （批次 WI-81 修掉的那条缺陷，名册 302 那一族：判"上屏"要读到 <c>return</c> 的实际值，判"透明"要读到 alpha 的实际值）。</item>
/// </list>
/// </para>
/// </summary>
public static class MaterialTint
{
    /// <summary>拿不到系统强调色、也没自定义时的兜底染料（macOS 那种淡冷蓝）。</summary>
    public static readonly Color FallbackAccent = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);

    /// <summary>
    /// 背衬整窗取色。除<b>实色（None）</b>外的每一档都产出一层带 alpha 的扁平着色，交给
    /// <c>TransparentTintBackdrop</c> 铺满整窗；只有<b>纯色（Solid）</b>吃用户的「背景不透明度」滑杆，
    /// 其余材质用各自固定的透明度性格（批次 K 定下的口径，这颗⛔ 跟着 <paramref name="tintOverride"/> 变）。
    /// </summary>
    /// <param name="tintOverride">那一扇窗点定的底色；<c>null</c>＝按主题基色＋染料算。</param>
    public static Color BackdropColor(
        bool isDark,
        Color accent,
        WidgetBackdropKind kind,
        double solidOpacity,
        Color? tintOverride = null)
        => WithAlpha(
            tintOverride is { } custom ? Opaque(custom) : SurfaceRgb(isDark, accent, kind, solidOpacity),
            BackdropAlpha(kind, solidOpacity));

    /// <summary>
    /// 那一档材质的<b>透明度性格</b>（背衬的 alpha）。
    /// <para>固定数值的来历：薄亚克力最通透 &lt; 厚亚克力 &lt; 云母 Alt &lt; 云母（偏实哑光），
    /// 与 <see cref="SurfaceRgb"/> 里各档的 (Alpha 下限, 上限, 强调色偏向) 同表——<b>只许住在这一颗</b>，
    /// 写两遍就会出现「换了颜色顺手换了材质」。</para>
    /// </summary>
    public static byte BackdropAlpha(WidgetBackdropKind kind, double solidOpacity) => kind switch
    {
        WidgetBackdropKind.Solid => AlphaOf(NormalizeOpacity(solidOpacity)),
        WidgetBackdropKind.AcrylicBase => AlphaOf(Lerp(0.12, 0.92, OpacityOfAcrylicBase)),
        WidgetBackdropKind.Mica => AlphaOf(Lerp(0.30, 0.98, OpacityOfMica)),
        WidgetBackdropKind.MicaAlt => AlphaOf(Lerp(0.38, 0.99, OpacityOfMicaAlt)),
        WidgetBackdropKind.None => 0xFF,                          // 实色：没有透明可保留
        _ => AlphaOf(Lerp(0.06, 0.80, OpacityOfAcrylicThin)),     // 薄亚克力
    };

    /// <summary>不掺染料时那一档材质的 RGB（alpha 由 <see cref="BackdropAlpha"/> 单独决定）。</summary>
    private static Color SurfaceRgb(bool isDark, Color accent, WidgetBackdropKind kind, double solidOpacity) => kind switch
    {
        WidgetBackdropKind.Solid => SolidRgb(isDark, accent, solidOpacity),
        WidgetBackdropKind.AcrylicBase => Tinted(isDark, accent, OpacityOfAcrylicBase, kind),
        WidgetBackdropKind.Mica => Tinted(isDark, accent, OpacityOfMica, kind),
        WidgetBackdropKind.MicaAlt => Tinted(isDark, accent, OpacityOfMicaAlt, kind),
        WidgetBackdropKind.None => isDark ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        _ => Tinted(isDark, accent, OpacityOfAcrylicThin, kind),
    };

    // ─────────────── 各档材质固定的透明度输入（批次 K 的性格表） ───────────────

    private const double OpacityOfAcrylicThin = 0.46;
    private const double OpacityOfAcrylicBase = 0.62;
    private const double OpacityOfMica = 0.90;
    private const double OpacityOfMicaAlt = 0.82;

    /// <summary>半透明那一族的染色底：主题基色 → 掺染料 → 覆一层同族灰。</summary>
    private static Color Tinted(bool isDark, Color accent, double surfaceOpacity, WidgetBackdropKind kind)
    {
        // 各材质的 (Alpha 下限, Alpha 上限, 强调色偏向)。alpha 的两端只服务于 <see cref="BackdropAlpha"/>，
        // 这里取的是那颗「强调色偏向」——两半同表，加一档材质时⛔ 只改一处。
        var (_, _, accentBias) = RangeFor(kind);
        return BuildAccentSurfaceColor(
            isDark,
            accent,
            ContentTintColor(isDark, accent),
            accentMix: Math.Clamp(Lerp(0.06, 0.40, AppearanceSettingsPolicy.MaterialIntensityDefault) + accentBias, 0.0, 1.0),
            overlayMix: Lerp(0.02, 0.12, AppearanceSettingsPolicy.MaterialIntensityDefault));
    }

    private static Color SolidRgb(bool isDark, Color accent, double solidOpacity) => BuildAccentSurfaceColor(
        isDark,
        accent,
        isDark ? Color.FromArgb(0xFF, 0x21, 0x24, 0x2A) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        accentMix: 0.18,
        overlayMix: isDark ? 0.15 : 0.04);

    /// <summary>主题基色里掺一点点染料，呈 macOS 那种淡冷调。</summary>
    private static Color ContentTintColor(bool isDark, Color accent) => BuildAccentSurfaceColor(
        isDark,
        accent,
        isDark ? Color.FromArgb(0xFF, 0x20, 0x22, 0x26) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        accentMix: isDark ? 0.08 : 0.16,
        overlayMix: isDark ? 0.04 : 0.08);

    private static (double AlphaFloor, double AlphaCeiling, double AccentBias) RangeFor(WidgetBackdropKind kind) => kind switch
    {
        WidgetBackdropKind.AcrylicBase => (0.12, 0.92, +0.06),   // 厚亚克力：比薄略实、略染色
        WidgetBackdropKind.Mica => (0.30, 0.98, -0.02),          // 云母：偏实、克制染色（高级哑光）
        WidgetBackdropKind.MicaAlt => (0.38, 0.99, +0.08),       // 云母 Alt：最取壁纸/强调色调
        _ => (0.06, 0.80, 0.00),                                 // 薄亚克力：最通透、苹果味
    };

    // ─────────────── 与旧实现同形的算式（批次 K 起没动过，⛔ 顺手优化） ───────────────

    private static Color BuildAccentSurfaceColor(bool isDark, Color accent, Color baseColor, double accentMix, double overlayMix)
    {
        var mixed = BlendColors(baseColor, accent, accentMix);
        var overlay = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2F, 0x36) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        return BlendColors(mixed, overlay, overlayMix);
    }

    private static Color BlendColors(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromArgb(
            0xFF,
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Opaque(Color color) => Color.FromArgb(0xFF, color.R, color.G, color.B);

    private static byte AlphaOf(double opacity) => (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);

    private static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * Math.Clamp(progress, 0.0, 1.0));

    /// <summary>
    /// 背景不透明度归一。注意 <see cref="Math.Clamp(double,double,double)"/> 对 NaN 两比较皆 false 会<b>原样返回 NaN</b>，
    /// 随后 Lerp→(byte)Math.Round(NaN*255) 得 0 → 面板 alpha 0 完全隐形（批次 10 真机踩过）。
    /// 非法/NaN 一律回落 1.0（最不透明、可见）。
    /// </summary>
    private static double NormalizeOpacity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 1.0;
}
