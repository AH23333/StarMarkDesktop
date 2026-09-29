#nullable enable
using System;

namespace StarMark.Core.Hotkeys;

/// <summary>被挡住的原因。<b>不是异常</b>：它是"这条链今天按设计不干活"，调用方要把它变成一句可见的话。</summary>
public enum CaptureBlock { None, Disabled, SessionBusy }

/// <summary>
/// 截屏这一族（截图 / 贴图 / 识字 + 两条贴图组管理）的<b>闸门</b>：功能总开关与"已经有一次框选在场"两个条件合起来判。
/// <para>
/// 与 <see cref="HotkeyGate"/> 同一族的做法：<b>判据在 Core（纯函数、逐臂单测），接线层只准调用</b>——
/// 在 UI 里现写 <c>mode != Pin || !busy</c> 这类布尔就是本项目定案禁止的写法（批次 WF-1，两次真机事故）。
/// </para>
/// <para>
/// <b>这一族的分工线要划清楚</b>：三条"发起框选"的（<c>screen.capture / screen.pin / screen.ocr</c>）
/// 与两条"管理已经贴在那里的图"的（<c>screen.pinhidden / screen.pinthrough</c>）不是一件事。
/// 关掉总开关只收前者；后者继续放行——贴图窗可能还钉在桌面上，而 <c>screen.pinhidden</c> 是
/// "贴图忽然点不动了（穿透态收不到任何点击）"唯一的键盘出口。把它一起摘掉＝在最需要出口的时刻把出口拿走。
/// </para>
/// </summary>
public static class CaptureGate
{
    /// <summary>
    /// 这条动作是不是"发起一次框选"。<b>按动作号点名判，不按 <c>screen.</c> 前缀判</b>：
    /// 前缀里混着两条管理动作，用前缀就等于把它们也当成入口关掉（上面那条分工线）。
    /// </summary>
    public static bool IsSelectionAction(string action)
        => action is HotkeyActions.ScreenCapture or HotkeyActions.ScreenPin or HotkeyActions.ScreenOcr;

    /// <summary>
    /// 此刻要不要为这条动作<b>向系统注册全局热键</b>。
    /// <para>关掉总开关时三条入口<b>整条不注册</b>。这与画布那条先例<b>形状相同、理由不同</b>，值得写清楚：
    /// 画布留 <c>canvas.toggle</c> 继续注册，因为它是带修饰键的组合（多占一条无妨，还能给一句"要先打开"）；
    /// 这里三条里两条是<b>裸功能键</b>（F1／F3／F4），替一个关掉的功能继续占着它们，
    /// 等于让所有软件永久失去 Help／截图键——那比"按了没反应"更糟。
    /// 关掉之后的可见答复不靠留哑键：<b>托盘里那三项整条消失</b>（没有入口＝不需要解释），
    /// 而设置页那句说明会讲清去哪打开。</para>
    /// </summary>
    public static bool RegistersHotkey(string action, bool enabled) => enabled || !IsSelectionAction(action);

    /// <summary>
    /// 托盘／主窗里这一项<b>还该不该出现</b>（与 <see cref="RegistersHotkey"/> 同一个判据，两个读者）。
    /// <para>两处必须同判：只摘键不摘菜单＝"托盘里有截图，点了没反应"；只摘菜单不摘键＝按 F1 静默。</para>
    /// </summary>
    public static bool ShowsTrayItem(string action, bool enabled) => enabled || !IsSelectionAction(action);

    /// <summary>
    /// 发起一次框选被什么挡住（<b>总开关排在会话之前</b>）。
    /// <para>顺序与 <see cref="HotkeyGate.ReasonNotRunning"/> 相反是有原因的：那条问的是"<b>画布</b>的键此刻按下去会怎样"
    /// （截图压着画布 ⇒ 会话优先），这里问的是"<b>截图自己</b>能不能发起"。会话进行中而功能已被关掉时，
    /// 正确回答是"这个功能关着了"，而不是把用户支使去等一次根本没在跑的截图。</para>
    /// </summary>
    public static CaptureBlock BlockOf(bool enabled, bool sessionBusy)
        => !enabled ? CaptureBlock.Disabled : sessionBusy ? CaptureBlock.SessionBusy : CaptureBlock.None;

    /// <summary>
    /// 挡住时给的一句话；没挡住返回 <b>null</b>（调用方靠 null 判"放行"，返回空字符串会让"被挡住"与"放行"在界面上长得一样）。
    /// <para>措辞给的是<b>下一步动作</b>（去哪打开），不是"失败"。</para>
    /// </summary>
    public static string? ReasonFor(CaptureBlock block) => block switch
    {
        CaptureBlock.Disabled => "要先在 设置 → 拓展功能 里打开「截屏（截图 / 贴图 / 识字）」",
        CaptureBlock.SessionBusy => "已经有一次截图在场，先把它结束（Esc 取消）再截下一张",
        _ => null,
    };
}
