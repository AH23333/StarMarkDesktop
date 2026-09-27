#nullable enable

namespace StarMark.Core.Canvas;

/// <summary>
/// 穿透态那一下按下该不该被画布抢走。<b>方向判据单独做成纯函数</b>，是因为它的方向只有在真机上才看得出来：
/// <c>ctrlAlt</c> 与 <c>!ctrlAlt</c>、<c>tool == Highlighter</c> 与 <c>tool != Highlighter</c> 都能编译、
/// 都能通过"看起来在测它"的闸门，写反的后果却是两种相反的故障（批次 WF-1 的原地教训）。
/// <para>
/// <b>2026-09-27 用户改判</b>：从前这里"选中荧光笔＝保持穿透，按住就画"（§16.5.2 的零摩擦快画）。
/// 他实机要的是另一套：<b>换工具与"能不能画"解耦</b>——穿透态就是穿透态，画一律要先关掉穿透
/// （工具条那颗或 <c>canvas.through</c>）。留着"按当前工具抢按"会带来两个他都撞到的毛病：
/// ① 荧光笔在穿透态长按仍能画（与"穿透＝鼠标归下层"这个说法自相矛盾）；
/// ② 抢按不看光标落点，把点工具条那一下也吃掉（"点菜单栏却画出一条轨迹"）。
/// 于是穿透态只保留一种抢按：<b>显式的 Ctrl+Alt 圈画</b>——修饰键按住了才抢，
/// 且抢之前仍要先问落点是不是自家的条子（<c>LayerDirector.IsPointOnChrome</c>：是则不吃，条子照旧可点）。
/// </para>
/// </summary>
public static class CanvasModes
{
    /// <summary>
    /// 穿透态这一次按下是否归画布抢走：<b>只认 Ctrl+Alt，与当前选的是哪支笔完全无关</b>。
    /// 参数里没有工具不是遗漏——正是"换工具不改鼠标归属"这条改判的形状。
    /// </summary>
    public static bool ClaimsPressInPenetrating(bool ctrlAltDown) => ctrlAltDown;
}
