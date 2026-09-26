#nullable enable

namespace StarMark.Core.Canvas;

/// <summary>
/// 规格 §16.5.2 的三态判据：<b>选中一支笔之后，这块画布该拦鼠标还是该穿透</b>。
/// <para>
/// 这条判据单独做成纯函数，而不是在编排里现写一个布尔表达式，是因为它的<b>方向只有在真机上才看得出来</b>：
/// <c>tool != Highlighter</c> 与 <c>tool == Highlighter</c> 都能编译、都能通过"看着像在测它"的闸门，
/// 而写反的后果是两种完全相反的故障——点画笔永远画不上（一直说自己是穿透态），
/// 以及拿荧光笔却把整块屏的鼠标吃掉（违反"按住才有、松开即透"）。批次 WF-1 就是这么撞上的。
/// </para>
/// </summary>
public static class CanvasModes
{
    /// <summary>
    /// 选完这支笔之后要不要<b>穿透</b>。
    /// 画笔／橡皮要在板上留痕、要能反复改 ⇒ <b>不穿透</b>（拦截，进入绘制态）；
    /// 荧光笔是"按住才有"的瞬时轨迹 ⇒ <b>保持穿透</b>，松开就把鼠标还给下层应用。
    /// </summary>
    public static bool IsClickThroughAfter(CanvasTool tool) => tool == CanvasTool.Highlighter;
}
