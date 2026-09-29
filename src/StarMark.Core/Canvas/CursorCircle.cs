#nullable enable
using System;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Canvas;

/// <summary>
/// 光标那块圆的<b>唯一几何与唯一显示判据</b>。
/// <para>
/// 批次 S4-⑥ 先把它合成"一块圆、两个读数"（从前幕布亮区按 160 DIP 算、光晕按荧光笔笔宽档算，
/// 同一帧上鼠标处有两个大小不同的圆，而两处各自乘一次缩放——记忆 ⑧：同一件事两处各写一份必然分岔）。
/// 批次 RN 按用户裁决再收两步：<b>档位从三档收成两档（关／任何工具常开，默认关）</b>，
/// <b>半径从代码常量搬进设置页</b>（这条正是 P-111 写好的重开条件：用户点名要自己调）。
/// </para>
/// <para>
/// 搬进设置页时留下一条硬约束：<b>幕布亮区与光晕共用同一个半径值</b>。分成两根滑杆＝把 S4-⑥ 合掉的分岔
/// 重新拆开，而且这次还能调出"亮区比光晕大"这种两值同时成立的形状。
/// </para>
/// </summary>
public static class CursorCircle
{
    /// <summary>
    /// 默认半径（<b>DIP</b>，不是物理像素）：用户没动过设置、或存档里那个数读不出来时用它。
    /// <para>160 是批次 S4-⑥ 定下并被真机接受的那个值——它比荧光笔笔宽档大得多，
    /// 因为这块圆的职责是"讲解时看得见光标在哪儿"，不是"笔多粗"。</para>
    /// </summary>
    public const double DefaultRadiusDip = 160d;

    /// <summary>下限：再小就退化成"笔尖旁边一个点"，穿透态下等于没有交代（同批次 WT 给序号圆点设下限那条理由）。</summary>
    public const double MinRadiusDip = 40d;

    /// <summary>上限：400 DIP 已是 1080p 上约 1/5 屏高；开销按半径平方走，再大就只是把一帧的像素量往上堆。</summary>
    public const double MaxRadiusDip = 400d;

    /// <summary>
    /// 把设置里读到的半径夹进合法区间。<b>存盘侧与读取侧都走这里</b>（两处各夹一次就会给出两个"合法值"）。
    /// <para>NaN／∞（损坏的 JSON 里读得出来）一律回默认，而不是回上下限：
    /// 拿 NaN 去乘缩放会得到 NaN 半径，最后 <c>PaintGlow</c> 里 <c>radius &lt;= 0</c> 那一臂判定不了它，症状是"屏幕上什么都没有"。</para>
    /// </summary>
    public static double ClampRadiusDip(double radiusDip)
        => double.IsFinite(radiusDip)
            ? Math.Min(MaxRadiusDip, Math.Max(MinRadiusDip, radiusDip))
            : DefaultRadiusDip;

    /// <summary>
    /// 半径换算成这块屏上的物理像素。<b>整条链只有这一处做这次换算</b>：
    /// 帧循环算脏区、提交时叠光、幕布挪亮区三处若各乘一次 <c>Scale</c>，四舍五入差一像素就是"光晕比亮区差一圈"。
    /// 缩放为 0／负／NaN（刚拔屏、DPI 还没读到）时回 1×，不许算出半径 0 的那块"什么都不叠"的圆。
    /// </summary>
    public static int RadiusInPixels(double scale, double radiusDip)
    {
        var safe = double.IsFinite(scale) && scale > 0 ? scale : 1d;
        return (int)Math.Round(ClampRadiusDip(radiusDip) * safe, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 那块圆<b>占的地方</b>（外接方框，未夹边）。两处读者必须用它，不许各自再写一次 <c>半径 * 2 + 1</c>：
    /// 帧循环拿它算"旧光晕那一块要复原"的脏区，<see cref="CanvasCompositor.PaintGlow"/> 拿它算提交的那一块——
    /// 两份公式一旦分岔，症状就是"光晕拖过去之后后面拖一条旧光"或"外缘缺一条"。
    /// </summary>
    public static IntRect BoxOf(PixelPoint center, int radius)
        => new(center.X - radius, center.Y - radius, radius * 2 + 1, radius * 2 + 1);

    /// <summary>
    /// 那一团光这一帧叠不叠。两档里"关"就是什么都不叠；<b>幕布开着时也不叠</b>——那块圆已经"不叠底"了，
    /// 再叠一团光就把亮区里的原色盖掉，两个读数糊成一个，哪个都读不出来。
    /// <para>判据写成 <c>!HasFocusHole(backdrop)</c>（性质）而不是 <c>backdrop == Transparent</c>（名字）：
    /// 将来多一种背景态，它自动落进正确那一臂（同坑表 #166）。</para>
    /// </summary>
    public static bool ShowsHalo(bool always, CanvasBackdrop backdrop)
        => always && !CanvasBackdropMath.HasFocusHole(backdrop);
}
