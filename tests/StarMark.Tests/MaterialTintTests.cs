#nullable enable
using System;
using Windows.UI;
using StarMark.Abstractions;
using StarMark.Core.Appearance;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-81 的取色判据（用户两条：①「选透明材质后改组件颜色会变成实色，要保留透明」
/// ②「主题色只能选两种，加一颗自定义，⛔ 动材质」）。
/// <para>
/// <b>为什么这些断言住在 Core 而不是设置页</b>：要钉的是"那一档材质的 alpha 有没有跟着颜色一起变"，
/// 那是颗纯算术；测试工程引用不到 <c>StarMark.UI</c>（坑表 #184），所以取色整族先下沉到
/// <see cref="MaterialTint"/>／<see cref="AppPalette"/>，UI 只负责把结果交给背衬。
/// </para>
/// </summary>
public sealed class MaterialTintTests
{
    private static readonly Color Blue = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);
    private static readonly Color Red = Color.FromArgb(0xFF, 0xDC, 0x26, 0x26);

    /// <summary>除「实色 None」外的五档：透明度有性格可言的那一族（None 没有透明可保留）。</summary>
    private static readonly WidgetBackdropKind[] TranslucentTiers =
    [
        WidgetBackdropKind.Acrylic, WidgetBackdropKind.AcrylicBase,
        WidgetBackdropKind.Mica, WidgetBackdropKind.MicaAlt, WidgetBackdropKind.Solid,
    ];

    /// <summary>
    /// ① <b>换颜色⛔ 换材质</b>：点了底色之后，那一档材质的 alpha 与没点时<b>逐位相等</b>，
    /// 而 RGB 真的跟着变了（两半各钉一次：只钉 alpha 会放过"颜色根本没落上去"，
    /// 只钉 RGB 会放过本批修掉的那条缺陷）。
    /// </summary>
    [Theory]
    [InlineData(WidgetBackdropKind.Acrylic)]
    [InlineData(WidgetBackdropKind.AcrylicBase)]
    [InlineData(WidgetBackdropKind.Mica)]
    [InlineData(WidgetBackdropKind.MicaAlt)]
    [InlineData(WidgetBackdropKind.Solid)]
    public void ACustomTintChangesTheRgbButNeverTheAlpha(WidgetBackdropKind kind)
    {
        foreach (var dark in new[] { false, true })
        {
            var plain = MaterialTint.BackdropColor(dark, Blue, kind, 0.72);
            var tinted = MaterialTint.BackdropColor(dark, Blue, kind, 0.72, Red);
            Assert.Equal(plain.A, tinted.A);                                  // 材质效果一字不动
            Assert.NotEqual((plain.R, plain.G, plain.B), (tinted.R, tinted.G, tinted.B));   // 那颗色真落上去了
            Assert.Equal((Red.R, Red.G, Red.B), (tinted.R, tinted.G, tinted.B));
        }
    }

    /// <summary>②「实色（None）」那一档⛔ 有透明可保留：点了底色就是实色铺满，alpha 恒 FF。</summary>
    [Fact]
    public void TheOpaqueTierStaysOpaqueUnderATint()
    {
        foreach (var dark in new[] { false, true })
        {
            Assert.Equal((byte)0xFF, MaterialTint.BackdropAlpha(WidgetBackdropKind.None, 0.30));
            Assert.Equal((byte)0xFF, MaterialTint.BackdropColor(dark, Blue, WidgetBackdropKind.None, 0.30, Red).A);
        }
    }

    /// <summary>
    /// ③ 六档的<b>透明度性格</b>钉在实测读数上（本批搬迁前后同一颗 ⇒ 谁改了性格，这一格先红）。
    /// <para>推导：<c>round(Lerp(下限, 上限, 该档固定的不透明度) × 255)</c> ⇒
    /// 薄亚克力 <c>0.06+0.74×0.46=0.4004→102</c>；厚 <c>0.12+0.80×0.62=0.616→157</c>；
    /// 云母 <c>0.30+0.68×0.90=0.912→233</c>；云母 Alt <c>0.38+0.61×0.82=0.8802→224</c>；实色 <c>255</c>；
    /// 纯色吃滑杆，默认 0.72 → <c>183.6→184</c>。</para>
    /// </summary>
    [Fact]
    public void TheSixTiersAlphaCharacterIsPinnedAtTheMeasuredNumbers()
    {
        Assert.Equal((byte)0x66, MaterialTint.BackdropAlpha(WidgetBackdropKind.Acrylic, 0.72));
        Assert.Equal((byte)0x9D, MaterialTint.BackdropAlpha(WidgetBackdropKind.AcrylicBase, 0.72));
        Assert.Equal((byte)0xE9, MaterialTint.BackdropAlpha(WidgetBackdropKind.Mica, 0.72));
        Assert.Equal((byte)0xE0, MaterialTint.BackdropAlpha(WidgetBackdropKind.MicaAlt, 0.72));
        Assert.Equal((byte)0xFF, MaterialTint.BackdropAlpha(WidgetBackdropKind.None, 0.30));
        Assert.Equal((byte)0xB8, MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, 0.72));
    }

    /// <summary>
    /// ④ <b>不透明度只对纯色有效</b>（批次 K 的口径）在本批⛔ 跟着变色一起漂走：
    /// 四档半透明材质的 alpha 对滑杆完全不敏感，只有纯色跟着走。
    /// </summary>
    [Fact]
    public void OnlyTheSolidTierListensToTheOpacitySlider()
    {
        foreach (var kind in new[] { WidgetBackdropKind.Acrylic, WidgetBackdropKind.AcrylicBase,
                                     WidgetBackdropKind.Mica, WidgetBackdropKind.MicaAlt })
            Assert.Equal(MaterialTint.BackdropAlpha(kind, 0.30), MaterialTint.BackdropAlpha(kind, 1.0));

        Assert.True(MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, 0.30)
            < MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, 1.0));
        Assert.Equal((byte)0xFF, MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, 1.0));
    }

    /// <summary>
    /// ⑤ <b>NaN 那一坑的旧解</b>（批次 10 真机：NaN 浓度 ⇒ 面板 alpha 0 整窗隐形）搬进 Core 之后仍然成立：
    /// 非有限值一律回落"最不透明"，⛔ 传出 0。范围外的有限值走<b>夹取</b>（那是判据本来的职责，⛔ 当成 NaN 那一族）。
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteOpacityNeverProducesAnInvisiblePanel(double opacity)
    {
        foreach (var kind in TranslucentTiers)
            Assert.True(MaterialTint.BackdropAlpha(kind, opacity) > 0,
                $"{kind} 在 opacity={opacity} 上算出了 alpha 0（面板会整窗隐形）");
        Assert.Equal((byte)0xFF, MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, double.NaN));
    }

    [Fact]
    public void OutOfRangeOpacityIsClampedRatherThanPassedThrough()
    {
        Assert.Equal((byte)0x00, MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, -5.0));
        Assert.Equal((byte)0xFF, MaterialTint.BackdropAlpha(WidgetBackdropKind.Solid, 9.0));
    }

    /// <summary>
    /// ⑥ <b>逐位对拍旧算式</b>（已定死⑧：把改动前的算式原样留一份当参照）。
    /// 这一格证明"搬进 Core⛔ 搬坏观感"：六档 × 深浅 × 三档不透明度，整颗颜色全等。
    /// ⛔ 顺手优化下面那颗 <see cref="LegacyBackdropColor"/>——它就是 HEAD 上那份实现。
    /// </summary>
    [Fact]
    public void TheMovedFormulaProducesByteIdenticalColours()
    {
        var cases = 0;
        foreach (var kind in new[] { WidgetBackdropKind.Acrylic, WidgetBackdropKind.Mica, WidgetBackdropKind.None,
                                     WidgetBackdropKind.MicaAlt, WidgetBackdropKind.AcrylicBase, WidgetBackdropKind.Solid })
            foreach (var dark in new[] { false, true })
                foreach (var opacity in new[] { 0.30, AppearanceSettingsPolicy.OpacityDefault, 1.0 })
                {
                    Assert.Equal(LegacyBackdropColor(dark, Blue, kind, opacity),
                                 MaterialTint.BackdropColor(dark, Blue, kind, opacity));
                    cases++;
                }
        Assert.Equal(36, cases);        // 六档 × 两主题 × 三档不透明度，一格都没跳过
    }

    // ────────────── 参照实现：HEAD 上 <c>WidgetMaterialVisualCalculator</c> 原样一份 ──────────────

    private static Color LegacyBackdropColor(bool isDark, Color accent, WidgetBackdropKind kind, double solidOpacity)
        => kind switch
        {
            WidgetBackdropKind.Solid => LegacyAlpha(LegacyAccentSurface(isDark, accent,
                isDark ? Color.FromArgb(0xFF, 0x21, 0x24, 0x2A) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                0.18, isDark ? 0.15 : 0.04), LegacyNormalize(solidOpacity)),
            WidgetBackdropKind.AcrylicBase => LegacyNative(isDark, accent, 0.62, kind),
            WidgetBackdropKind.Mica => LegacyNative(isDark, accent, 0.90, kind),
            WidgetBackdropKind.MicaAlt => LegacyNative(isDark, accent, 0.82, kind),
            WidgetBackdropKind.None => isDark ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            _ => LegacyNative(isDark, accent, 0.46, kind),
        };

    private static Color LegacyNative(bool isDark, Color accent, double surfaceOpacity, WidgetBackdropKind kind)
    {
        var (alphaFloor, alphaCeiling, accentBias) = kind switch
        {
            WidgetBackdropKind.AcrylicBase => (0.12, 0.92, +0.06),
            WidgetBackdropKind.Mica => (0.30, 0.98, -0.02),
            WidgetBackdropKind.MicaAlt => (0.38, 0.99, +0.08),
            _ => (0.06, 0.80, 0.00),
        };
        double intensity = AppearanceSettingsPolicy.MaterialIntensityDefault;
        var tinted = LegacyAccentSurface(isDark, accent, LegacyContentTint(isDark, accent),
            Math.Clamp(LegacyLerp(0.06, 0.40, intensity) + accentBias, 0.0, 1.0),
            LegacyLerp(0.02, 0.12, intensity));
        return LegacyAlpha(tinted, LegacyLerp(alphaFloor, alphaCeiling, surfaceOpacity));
    }

    private static Color LegacyContentTint(bool isDark, Color accent) => LegacyAccentSurface(
        isDark, accent,
        isDark ? Color.FromArgb(0xFF, 0x20, 0x22, 0x26) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        isDark ? 0.08 : 0.16, isDark ? 0.04 : 0.08);

    private static Color LegacyAccentSurface(bool isDark, Color accent, Color baseColor, double accentMix, double overlayMix)
    {
        var mixed = LegacyBlend(baseColor, accent, accentMix);
        var overlay = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2F, 0x36) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        return LegacyBlend(mixed, overlay, overlayMix);
    }

    private static Color LegacyBlend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromArgb(0xFF,
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static Color LegacyAlpha(Color color, double opacity) => Color.FromArgb(
        (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255), color.R, color.G, color.B);

    private static double LegacyLerp(double start, double end, double progress) =>
        start + ((end - start) * Math.Clamp(progress, 0.0, 1.0));

    private static double LegacyNormalize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 1.0;
}

/// <summary>
/// 「主题色」那颗染料的来源优先级与设置档写法（批次 WI-81 的另一半）。
/// </summary>
public sealed class AppPaletteTests
{
    private static readonly Color System = Color.FromArgb(0xFF, 0x00, 0x67, 0xC0);
    private static readonly Color Custom = Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A);

    /// <summary>① 自定义赢；没自定义时<b>逐位等于旧行为</b>（系统那颗 alpha 为 0 才落兜底蓝）。</summary>
    [Fact]
    public void CustomAccentWinsAndNothingElseMoves()
    {
        Assert.Equal(Custom, AppPalette.ResolveAccent(Custom, System));
        Assert.Equal(System, AppPalette.ResolveAccent(null, System));
        Assert.Equal(MaterialTint.FallbackAccent, AppPalette.ResolveAccent(null, Color.FromArgb(0x00, 0x11, 0x22, 0x33)));
    }

    /// <summary>
    /// ② <b>严格</b>解析：认不出来就交 null（⛔ 凑一颗错色）。
    /// 那族脏输入正是 <c>docs/前端主题与材质切换审查报告.md</c> 第 136 行登记过的形状——
    /// 旧那颗 <c>ParseColorBrush</c> 会把 <c>#GGHHII</c>"解析成功"成一颗错色，调用方按非 null 采用而不再回退主题。
    /// </summary>
    [Theory]
    [InlineData("#GGHHII")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#红红红红红红")]
    [InlineData("#3B 82F6")]
    [InlineData("#-1-2-3-4-5-6")]
    [InlineData("#3B82F6x")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnreadableHexTextProducesNoColour(string? text) => Assert.Null(AppPalette.ParseArgb(text));

    [Theory]
    [InlineData("#3B82F6", 0x3B, 0x82, 0xF6)]
    [InlineData("3B82F6", 0x3B, 0x82, 0xF6)]
    [InlineData("#3b82f6", 0x3B, 0x82, 0xF6)]
    [InlineData(" #3B82F6 ", 0x3B, 0x82, 0xF6)]
    public void ReadableHexTextRoundTrips(string text, byte r, byte g, byte b)
    {
        var parsed = AppPalette.ParseArgb(text);
        Assert.NotNull(parsed);
        Assert.Equal(Color.FromArgb(0xFF, r, g, b), parsed!.Value);
        // 写进设置档的形态是 #RRGGBB（那颗染料按定义是实色），读回来与写进去的那颗逐位相等
        Assert.Equal($"#{r:X2}{g:X2}{b:X2}", AppPalette.ToHexText(parsed.Value));
        Assert.Equal(parsed.Value, AppPalette.ParseArgb(AppPalette.ToHexText(parsed.Value)));
    }

    /// <summary>③ 八位形态仍认（手改过的设置档⛔ 被读成"没设过"），但写出去的那颗恒实色。</summary>
    [Fact]
    public void EightDigitFormIsStillReadable()
    {
        Assert.Equal(Color.FromArgb(0x80, 0x3B, 0x82, 0xF6), AppPalette.ParseArgb("#803B82F6"));
        Assert.Equal("#3B82F6", AppPalette.ToHexText(Color.FromArgb(0x80, 0x3B, 0x82, 0xF6)));
    }

    /// <summary>④ 那两颗淡底＝<b>同一颗 RGB，只换 alpha</b>（⛔ 让自定义色把淡底变成实色）。</summary>
    [Fact]
    public void TheDerivedBrushesKeepTheRgbAndOnlyMoveTheAlpha()
    {
        foreach (var dark in new[] { false, true })
        {
            var soft = AppPalette.Soft(Custom, dark);
            Assert.Equal(dark ? AppPalette.SoftAlphaDark : AppPalette.SoftAlphaLight, soft.A);
            Assert.Equal((Custom.R, Custom.G, Custom.B), (soft.R, soft.G, soft.B));
        }
        var hint = AppPalette.DropHint(Custom);
        Assert.Equal(AppPalette.DropHintAlpha, hint.A);
        Assert.Equal((Custom.R, Custom.G, Custom.B), (hint.R, hint.G, hint.B));
        // 与 XAML 里那两颗旧淡底的 alpha 同值：换颜色时⛔ 顺手把"淡"变成"实"
        Assert.Equal((byte)0x1A, AppPalette.SoftAlphaLight);
        Assert.Equal((byte)0x2E, AppPalette.SoftAlphaDark);
        Assert.Equal((byte)0x33, AppPalette.DropHintAlpha);
    }
}
