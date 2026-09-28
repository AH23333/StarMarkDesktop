#nullable enable
using System;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Canvas;

/// <summary>
/// 光标那一块圆什么时候发光（批次 S4-⑥，2026-09-29 用户裁的三条之一）。
/// <para><b>默认仍是"只荧光笔态"</b>：那是批次 WF 之后定下的观感（拿着画笔却满屏跟着一团颜色是噪音），
/// 加的是两头——关掉它，以及"讲解时任何工具都常开"（这一档给的是"光标在哪儿"，不是"我拿着什么笔"）。</para>
/// </summary>
public enum HaloMode
{
    /// <summary>关：光标处什么都不叠。</summary>
    Off,

    /// <summary>只荧光笔态（默认）：那团光是"模式还开着"的提示，穿透态收不到鼠标事件时它是唯一的交代。</summary>
    HighlighterOnly,

    /// <summary>常开：拿哪支笔都跟着一团光，讲解时用来指出光标位置。</summary>
    Always,
}

/// <summary>
/// 光标处那块圆的<b>唯一几何与唯一显示判据</b>（批次 S4-⑥；2026-09-29 用户裁："合并成一块圆，两个读数"）。
/// <para>
/// 从前它有两份：幕布那块亮区的半径是 <c>160 DIP</c>，光晕的半径是<b>荧光笔的笔宽档</b>。
/// 两份半径住在两个类里、谁也不知道对方存在，于是同一帧上鼠标处可能同时有一个不叠底的圆和一团按笔宽算的光——
/// "光晕比亮区小一圈"这种两值同时成立的形状，只有眼睛能看出来（记忆 ⑧：同一件事两处各写一份必然分岔）。
/// 现在是一块圆、两个读数：<b>幕布开着 ⇒ 那一块是"不叠底"的亮区；幕布关着 ⇒ 同一块是那一团光</b>。
/// </para>
/// <para>
/// 半径先进代码、不进设置页（用户裁决）：<b>一个数、一处出处</b>；参照实现 ppInk 把它做成屏宽百分比滑杆
/// （<c>FormOptions.cs:1452</c>），等真机看过手感再谈要不要那根滑杆，别提前铺设置项。
/// </para>
/// </summary>
public static class CursorCircle
{
    /// <summary>
    /// 那块圆的半径（<b>DIP</b>，不是物理像素）：混屏时按每块屏自己的缩放换算，
    /// 写死像素数就是在 150% 屏上小一半（与幕布亮区原来那条纪律同源）。
    /// </summary>
    public const double RadiusDip = 160d;

    /// <summary>
    /// 半径换算成这块屏上的物理像素。<b>整条链只有这一处做这次换算</b>：
    /// 帧循环算脏区、提交时叠光、幕布挪亮区三处若各乘一次 <c>Scale</c>，四舍五入差一像素就是"光晕比亮区差一圈"。
    /// 缩放为 0／负（刚拔屏、DPI 还没读到）时回 1×，不许算出半径 0 的那块什么都不叠的圆。
    /// </summary>
    public static int RadiusInPixels(double scale)
    {
        // NaN／∞（上一状态被算坏）与 0／负（刚拔屏、DPI 还没读到）都回 1×：
        // 半径 0 的那块圆在两条读者路上都是"什么都不叠"，症状就成了"档位开着，屏幕上什么都没有"。
        var safe = double.IsFinite(scale) && scale > 0 ? scale : 1d;
        return (int)Math.Round(RadiusDip * safe, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 这一档<b>开着没有</b>（不看手上的笔、也不看背景态）——条上那颗按钮亮不亮读的是它。
    /// <para>与 <see cref="ShowsHalo"/> 分开是因为它们回答的是两个问题："用户有没有要它"与"这一帧该不该叠"。
    /// 判据只有一份：<c>ShowsHalo</c> 也走这里，界面上再写一遍 <c>!= Off</c> 就是第二真值（记忆 ⑥）。</para>
    /// </summary>
    public static bool IsOn(HaloMode mode) => mode != HaloMode.Off;

    /// <summary>
    /// 那块圆<b>占的地方</b>（外接方框，未夹边）。两处读者必须用它，不许各自再写一次 <c>半径 * 2 + 1</c>：
    /// 帧循环拿它算"旧光晕那一块要复原"的脏区，<see cref="CanvasCompositor.PaintGlow"/> 拿它算提交的那一块——
    /// 两份公式一旦分岔，症状就是"光晕拖过去之后后面留一条旧光"或"外缘缺一条"。
    /// </summary>
    public static IntRect BoxOf(PixelPoint center, int radius)
        => new(center.X - radius, center.Y - radius, radius * 2 + 1, radius * 2 + 1);

    /// <summary>
    /// 那一团光这一帧叠不叠。<b>幕布开着时永远不叠</b>：那块圆已经"不叠底"了，再叠一团光就是两团，
    /// 而且光会把亮区里那块原色盖掉——两个读数各自都要能看见，重叠在一起就都读不出来。
    /// </summary>
    public static bool ShowsHalo(HaloMode mode, CanvasTool tool, CanvasBackdrop backdrop)
        => IsOn(mode)
        && !CanvasBackdropMath.HasFocusHole(backdrop)
        && (mode == HaloMode.Always || tool == CanvasTool.Highlighter);

    /// <summary>
    /// 条上那颗按下去落到哪一档：<b>关 → 只荧光笔 → 常开 → 关</b>。
    /// <para>循环而不是"点当前＝取消"：这一颗有三个状态，二值的交互语言在这里给不出中间那一档。
    /// 但循环的<b>起点</b>仍是默认那档，所以"再点一次回到原样"这件事没有变（三次点回原处）。</para>
    /// </summary>
    public static HaloMode Next(HaloMode from) => from switch
    {
        HaloMode.Off => HaloMode.HighlighterOnly,
        HaloMode.HighlighterOnly => HaloMode.Always,
        _ => HaloMode.Off,
    };

    /// <summary>那一档叫什么（状态行与 tooltip 的唯一出处；界面不许自己再写一份中文）。</summary>
    public static string NameOf(HaloMode mode) => mode switch
    {
        HaloMode.Off => "关",
        HaloMode.Always => "常开（任何工具）",
        _ => "只荧光笔",
    };
}
