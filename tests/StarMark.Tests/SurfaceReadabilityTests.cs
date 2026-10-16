#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using StarMark.Abstractions;
using StarMark.Core.Appearance;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-82：那一扇<b>点了底色之后的字还念不念得出</b>——判据下沉到 Core 之后的行为契约测。
/// <para>
/// <b>为什么现在才有这一档</b>：真机量到的那一格是"深色主题 + 组件外观里点了白色"＝
/// <b>整扇内容从屏幕上消失</b>（现读 tint=#FFFFFFFF 实色，而字仍是主题给的白字）。
/// 全仓此前⛔ 有任何一处按亮度判过字与底配不配（只有 OCR 预处理量过对比度）——
/// 所以那一格⛔ 是"没人想到"，而是"没人装尺子"（坑表 #580 那一族：判"上屏"要读到实际值）。
/// </para>
/// <para>
/// 最硬的那一格（<see cref="PickedSurfaceNeverDropsBelowWcagAA"/>）⛔ 逐颗钉"白底必须黑字"这种点值，
/// 而是把<b>六档材质 × 若干点定色 × 若干不透明度</b>全矩阵都问一遍"自动字色与它压着的那颗底，
/// 对比度够不够 AA"——它由判据本身保证（取对比度更高的那颗 ⇒ 最坏落在交叉点 4.58），
/// 因此换一颗新材质、加一颗新预设色都自动在射程内。
/// </para>
/// </summary>
public sealed class SurfaceReadabilityTests
{
    private static readonly (string Name, Color Color)[] Picked =
    {
        ("白", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
        ("浅灰", Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3)),
        ("中灰", Color.FromArgb(0xFF, 0x80, 0x80, 0x80)),
        ("黄", Color.FromArgb(0xFF, 0xEA, 0xB3, 0x08)),
        ("深灰", Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B)),
        ("黑", Color.FromArgb(0xFF, 0x00, 0x00, 0x00)),
    };

    private static readonly WidgetBackdropKind[] AllTiers = Enum.GetValues<WidgetBackdropKind>();

    /// <summary>①现场那一格：纯白实色底 ⇒ 必须翻成黑字（从前它给的是白字＝白压白）。</summary>
    [Fact]
    public void WhiteSurfaceTakesBlackText()
    {
        var white = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        Assert.Equal(SurfaceReadability.OnLight, SurfaceReadability.ReadableOn(white));
        Assert.True(SurfaceReadability.Contrast(SurfaceReadability.OnLight, white) > 20.0);
    }

    /// <summary>②深底那几颗⛔ 被"自动"多事：黑/近黑/暗板上仍取白字，与主题默认一致。</summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x1F)]
    [InlineData(0x40)]
    public void DarkSurfacesKeepWhiteText(byte level)
    {
        var dark = Color.FromArgb(0xFF, level, level, level);
        Assert.Equal(SurfaceReadability.OnDark, SurfaceReadability.ReadableOn(dark));
    }

    /// <summary>
    /// ③那颗"该翻脸了吗"的中间调：判据⛔ 一颗写死的阈值，而是<b>取对比度更高的那颗</b>——
    /// 这一格钉的是"选出来的那颗，对比度必须真的≥没选的那颗"（把规则本身量出来）。
    /// </summary>
    [Theory]
    [InlineData(0x60)]
    [InlineData(0x80)]
    [InlineData(0xA0)]
    [InlineData(0xC0)]
    public void ThePickedTextAlwaysWinsTheContrastContest(byte level)
    {
        var surface = Color.FromArgb(0xFF, level, level, level);
        var picked = SurfaceReadability.ReadableOn(surface);
        var other = picked == SurfaceReadability.OnLight ? SurfaceReadability.OnDark : SurfaceReadability.OnLight;
        Assert.True(
            SurfaceReadability.Contrast(picked, surface) >= SurfaceReadability.Contrast(other, surface),
            $"#{level:X2} 那颗底上选了对比度更低的一颗字色");
    }

    /// <summary>④src-over 那三档边界：全透＝照抄下面那颗，全实＝照抄上面那颗，半透取中。</summary>
    [Fact]
    public void OverBlendsByAlphaOnly()
    {
        var black = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        var white = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        Assert.Equal(black, SurfaceReadability.Over(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), black));
        Assert.Equal(white, SurfaceReadability.Over(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), black));

        var half = SurfaceReadability.Over(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), black);
        Assert.Equal(0xFF, half.A);                       // 合成结果永远是实的一颗
        Assert.InRange(half.R, 127, 129);                 // 半透白压黑 ≈ 中灰（⛔ 取整方向由这里钉）
        Assert.Equal(half.R, half.G);
        Assert.Equal(half.G, half.B);
    }

    /// <summary>⑤亮度尺本身会动：两端为 0 与 1，且随单通道抬升严格变大（⛔ 一把读不出差的尺）。</summary>
    [Fact]
    public void LuminanceEndsAndOrderArePinned()
    {
        Assert.Equal(0.0, SurfaceReadability.Luminance(Color.FromArgb(0xFF, 0, 0, 0)), 3);
        Assert.Equal(1.0, SurfaceReadability.Luminance(Color.FromArgb(0xFF, 255, 255, 255)), 3);
        Assert.True(SurfaceReadability.Luminance(Color.FromArgb(0xFF, 0x80, 0x80, 0x80))
                  > SurfaceReadability.Luminance(Color.FromArgb(0xFF, 0x7F, 0x7F, 0x7F)));
        Assert.Equal(1.0, SurfaceReadability.Contrast(Color.FromArgb(0xFF, 9, 9, 9),
                                                     Color.FromArgb(0xFF, 9, 9, 9)), 3);
    }

    /// <summary>
    /// ⑥<b>主题基色那一档保守性</b>：薄薄一层白（低 alpha）压在深色基色上仍判"深底"⇒ 字⛔ 被翻成黑。
    /// 这一格钉的是"自动插手只在该插手时插手"——否则换一颗浅色预设就把用户的白字抢走了。
    /// </summary>
    [Fact]
    public void ThinTintOverDarkBaseDoesNotFlipTheText()
    {
        var thin = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);       // 20% 白
        var composed = SurfaceReadability.Over(thin, SurfaceReadability.BaseFor(isDark: true));
        Assert.True(SurfaceReadability.Luminance(composed) < 0.18, "合成色已被抬到浅色档，保守性不成立");
        Assert.Equal(SurfaceReadability.OnDark, SurfaceReadability.ReadableOn(composed));
    }

    /// <summary>
    /// ⑦全矩阵（本批最硬的一格）：<b>六档材质 × 六颗点定色 × 五档不透明度 × 深浅两套基色</b>，
    /// 每一格都问"这颗自动字色与现场真正合成出来的那颗底，对比度够不够 WCAG AA(4.5)"。
    /// <para>输入取的是 <see cref="MaterialTint.BackdropColor"/> 的实际输出（⛔ 手打一颗合成色），
    /// 所以这一格同时也在证批次 WI-81 那条不变式：换了颜色，那一档材质的 alpha 一个字⛔ 变。</para>
    /// </summary>
    [Fact]
    public void PickedSurfaceNeverDropsBelowWcagAA()
    {
        var cases = 0;
        foreach (var dark in new[] { false, true })
            foreach (var tier in AllTiers)
                foreach (var opacity in new[] { 0.0, 0.3, 0.5, 0.8, 1.0 })
                    foreach (var (name, picked) in Picked)
                    {
                        var backdrop = MaterialTint.BackdropColor(
                            dark, MaterialTint.FallbackAccent, tier, opacity, picked);
                        var surface = SurfaceReadability.Over(backdrop, SurfaceReadability.BaseFor(dark));
                        var text = SurfaceReadability.ReadableOn(surface);
                        var ratio = SurfaceReadability.Contrast(text, surface);
                        Assert.True(ratio >= 4.5,
                            $"{(dark ? "深" : "浅")}基色 + {tier} + 点了「{name}」@{opacity:F2} ⇒ 对比度只有 {ratio:F2}");
                        cases++;
                    }
        Assert.Equal(2 * AllTiers.Length * 5 * Picked.Length, cases);   // 矩阵⛔ 少跑一格
    }

    /// <summary>⑧那颗基色本身：深色取面板黑、浅色取纯白，且⛔ 一颗中性灰（它会把判据推到交叉点上）。</summary>
    [Fact]
    public void BaseColorsAreTheThemePanelsAndNotAMiddleGrey()
    {
        Assert.Equal(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F), SurfaceReadability.BaseFor(isDark: true));
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), SurfaceReadability.BaseFor(isDark: false));
    }

    /// <summary>
    /// ⑨那颗亮度尺量的是<b>线性化后</b>的 sRGB（WCAG 的定义），⛔ 直接拿字节加权平均。
    /// <para>反向台架的第一跑在这里撞出一颗<b>从没红过的格</b>：把 <c>Linear()</c> 摘掉之后，
    /// ①~⑧ 每格照绿——因为那颗近似值仍是"两端 0/1、单调、且与 Contrast 自洽"，
    /// 于是整套判据对"尺子换了一把"完全无感（名册 298 那一族：一把看着在量、其实会瞎的尺）。
    /// 这一格钉的是<b>数</b>：#808080 的 WCAG 亮度≈0.2159，而字节近似给 0.502。</para>
    /// </summary>
    [Fact]
    public void LuminanceIsTheLinearisedSrgbNotTheRawByte()
    {
        var mid = Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
        var l = SurfaceReadability.Luminance(mid);
        Assert.InRange(l, 0.20, 0.23);
        Assert.True(l < 0.30, "亮度尺读成了字节近似（0.502 那一族）——那会把'该黑该白'整体判偏");
        // 半字节在中段被线性化压得很低：#404040 的 WCAG 亮度只有 0.051
        Assert.InRange(SurfaceReadability.Luminance(Color.FromArgb(0xFF, 0x40, 0x40, 0x40)), 0.04, 0.06);
    }
}
