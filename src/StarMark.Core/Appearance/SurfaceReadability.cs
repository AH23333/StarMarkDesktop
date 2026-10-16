#nullable enable
using System;
using Windows.UI;

namespace StarMark.Core.Appearance;

/// <summary>
/// 用户给某一扇组件<b>点了底色</b>之后，那一扇上的字要还念得出声——这条判据只住在这里。
/// <para>批次 WI-82：材质与颜色各自都对了，可「深色主题 + 点了白色」那一格量到的是
/// <b>整扇内容从屏幕上消失</b>（现读 tint=#FFFFFFFF 实色，而字仍是主题给的白字＝白压白）。
/// 从前全仓没有任何一处按亮度判过字与底是否配得起来（只有 OCR 预处理那一族量过对比度），
/// 所以这一格⛔ 是"没人想到"，而是"没人装尺子"。</para>
/// <para>⚠ 这里<b>⛔ 替用户改他点定的颜色</b>：动的那一颗永远是<b>字色</b>，而且只在
/// <b>他自己⛔ 点文本色</b>的时候插手——他点了文本色就以他为准（见调用点那颗优先级）。</para>
/// </summary>
public static class SurfaceReadability
{
    /// <summary>浅底上那一颗字色（纯黑，与 WinUI「强调色上的字」同源，⛔ 引入第三颗灰）。</summary>
    public static readonly Color OnLight = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    /// <summary>深底上那一颗字色（纯白）。</summary>
    public static readonly Color OnDark = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// 材质背后那一层"默认是什么"：深色取面板黑，浅色取纯白。
    /// <para>半透明那一档真正压着的是<b>桌面壁纸</b>，而程序看不见别人的壁纸——取主题基色是
    /// <b>刻意保守</b>：它让"薄薄一层白"仍然判成深底（字留白），只有当那颗底色<b>自己</b>
    /// 已经把表面抬到接近浅色的地方才翻成黑字。</para>
    /// </summary>
    public static Color BaseFor(bool isDark)
        => isDark ? Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>src-over：把带 alpha 的那一层压到下面那层上，得出现场真正合成出来的那颗色。</summary>
    public static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(a * t + (1.0 - a) * b);
        return Color.FromArgb(0xFF, Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    /// <summary>WCAG 相对亮度（sRGB 线性化后加权）——⛔ 用 (0.299R+0.587G+0.114B)/255 那种近似，
    /// 它在中间调上会把"该黑该白"判反。</summary>
    public static double Luminance(Color c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>那颗字与那颗底的 WCAG 对比度（永远 ≥1，与谁在上无关）。</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>
    /// 那一扇该念黑字还是白字：<b>取对比度更高的那颗</b>（⛔ 拍一颗"亮度大于多少算浅"的阈值——
    /// 阈值与对比度在 l≈0.179 处给同一个答案，但只有后者说得清"为什么在这一格翻脸"）。
    /// </summary>
    public static Color ReadableOn(Color surface)
        => Contrast(OnLight, surface) >= Contrast(OnDark, surface) ? OnLight : OnDark;
}
