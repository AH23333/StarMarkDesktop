#nullable enable
using System.Collections.Generic;
using StarMark.Core.Capture;

namespace StarMark.Core.Hotkeys;

/// <summary>
/// 全局热键注册表的<b>投影</b>：哪些动作此刻真的该向系统注册（方案 §6.1）。
/// <para>
/// 投影是 f(总开关, 会话态)。之前只有第一个自变量：画布总开关关掉时摘掉九条 <c>canvas.*</c>。
/// 第二个自变量补上的是<b>截图会话期间</b>——那九条键此时按下去会真的把画布叫到截图上面来，
/// 症状是"截图中一切换工具，遮罩就没了"（C4 会话串键）。
/// </para>
/// <para>
/// <b>为什么只摘九条、留住开关那一条</b>：全摘之后用户在截图里按画布键会"什么都不知道地没反应"
/// （哑键最坏）。留住 <c>canvas.toggle</c> 就能给一句"截图进行中"的回执，与总开关那条先例同一句法。
/// 截图那三条（F1/F3/识字）不摘：<see cref="ScreenshotService"/> 自己有会话唯一闸门，
/// 摘掉它们反而让用户以为键坏了——它们该给的回执由那一条链自己说。
/// </para>
/// </summary>
public static class HotkeyGate
{
    /// <summary>
    /// 此刻要不要为这条动作注册全局热键。
    /// <para>三个臂都要单独测：<b>注册中</b>（开关开着且不在截图）、<b>被开关摘掉</b>、
    /// <b>被会话摘掉</b>；而开关那条（<c>canvas.toggle</c>）在后两个臂里都<b>继续注册</b>——
    /// 它是给出可见原因的唯一入口。</para>
    /// </summary>
    public static bool ShouldRegister(string action, bool canvasEnabled, AnnotationStage stage)
    {
        if (!HotkeyActions.IsCanvasAction(action)) return true;
        if (action == HotkeyActions.CanvasToggle) return true;
        return canvasEnabled && !stage.SuppressesCanvasHotkeys();
    }

    /// <summary>
    /// 这条动作此刻按下去<b>改变不了任何东西</b>时，给一句看得见的原因；真的在生效时返回 null。
    /// <para><b>会话这一臂排在注册判断之前</b>：<c>canvas.toggle</c> 在截图期间是<b>故意继续注册着</b>的
    /// （留住它才说得出这句话），若先问 <see cref="ShouldRegister"/> 就会被短路成"没有原因"，
    /// 用户按下去仍然是"什么都不知道地没反应"——那正是这条设计要消掉的哑键。</para>
    /// <para>开关排在会话之后：截图进行中按画布开关，正确回答是"等截图结束"，
    /// 而不是把用户支使去设置页打开一个本来就开着的功能。</para>
    /// <para>画布开着但这一条动作此刻没东西可做（板子没在场）那句不在这里——它带键位提示，
    /// 由 UI 侧的 <c>RequireRunning</c> 拼；这里只回答两道闸（会话、总开关）。</para>
    /// </summary>
    public static string? ReasonNotRunning(string action, bool canvasEnabled, AnnotationStage stage)
    {
        if (!HotkeyActions.IsCanvasAction(action)) return null;
        if (stage.SuppressesCanvasHotkeys())
            return "截图进行中，这条快捷键要等截图结束才归画布";
        if (!canvasEnabled)
            return "要先在 设置 → 拓展功能 里打开「屏幕画布」";
        return null;
    }

    /// <summary>
    /// 会话期间要摘掉的那一批（<c>canvas.*</c> 里除开关）。给闸门数数量用，
    /// 也防止"新增一条画布动作却忘了它该不该在截图里停"——新动作自动落进这一批。
    /// </summary>
    public static IReadOnlyList<string> SuppressedBySheet()
    {
        var list = new List<string>();
        foreach (var action in HotkeyActions.Canvas)
            if (action != HotkeyActions.CanvasToggle) list.Add(action);
        return list;
    }
}
