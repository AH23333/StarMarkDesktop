#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Integrations.Canvas;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// CanvasService 的这一段——条上那颗按下去之后发生什么：选笔／选色／选粗细／交出鼠标／换背景态／光晕换档。
/// <para>这里只发事件与存档位，<b>不判方向</b>——方向判据全在 Core。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

    // ────────── 工具与动作（工具条／热键／托盘都走这里）──────────

    /// <summary>
    /// 选工具＝顺带决定这一态拦不拦鼠标，<b>但方向判据不在这儿也不在 Hub 现写</b>：
    /// 由 <see cref="AnnotationSessions.BoardAfterToolSelect"/> 给（Core 纯函数，三臂单测）。
    /// 这里只把"选了哪支笔"这个事实报给状态机，穿透位由状态机反过来指挥窗口。
    /// </summary>
    public static void SelectTool(CanvasTool tool)
    {
        CommitOpenStroke();                       // 先收手上那条：不然它会接到新工具的设置上
        _tool = tool;
        // 2026-09-27 用户改判：<b>换工具不再翻穿透态</b>（从前选荧光笔留在穿透、选画笔进绘制，是 §16.5.2
        // 的"零摩擦"，也是"点一下工具结果鼠标归属变了"的来源）。要画就用那颗「穿透」按钮或 canvas.through
        // 关掉穿透，两态各管各的。所以这里没有 Raise：不产生迁移，只把当前态该长的样子重放一遍
        // （高亮、光标、状态行都要跟着换笔走——Raise 在态没变时短路，不重放就会出现"点了没反应"）。
        if (AnnotationHub.Stage == AnnotationStage.Idle)
        {
            // 唯一的例外：板子还没开着时按那三条工具热键（画笔／荧光笔／橡皮）不该是哑键——
            // 这一按的意思是"我要开始标注了"，所以把板子开起来（默认落穿透态，由转移表保证）。
            AnnotationHub.Raise(SessionEvent.ToggleBoard);
        }
        ApplyStage(AnnotationHub.Stage);
    }

    /// <summary>再点当前选中的笔＝收笔回穿透态（与截图/贴图那条"再点取消选择"同一交互语言）。</summary>
    public static void ToggleTool(CanvasTool tool)
    {
        // 换工具本身不再改穿透态（见 SelectTool）；这里只剩"再点当前那一支＝取消选择"，
        // 它等价于把鼠标还给下层应用——所以仍然是一次真正的会话事件，不是特例。
        if (_tool == tool && !ClickThroughHere) AnnotationHub.Raise(SessionEvent.GivePointerBack);
        else SelectTool(tool);
    }

    public static void SelectColor(int index)
    {
        if (index < 0 || index >= Palette.Count) return;
        _colorIndex = index;
        RaiseStateChanged();
    }

    public static void SelectWidth(int step)
    {
        CommitOpenStroke();
        _widthStep = Math.Clamp(step, 0, CanvasWidths.Steps.Length - 1);
        RaiseStateChanged();
    }

    /// <summary>
    /// 鼠标穿透。<b>开启后画布收不到任何鼠标事件</b>，所以出口不能只有工具条上那一颗：
    /// 全局热键、托盘、以及"工具条自己始终能被点"三条一起兜着（§16.6 点名的"找不回"）。
    /// <para>这一句现在只是<b>把意图折成事件递给 Hub</b>。以前它自己改那一位再逐屏写样式，
    /// 于是"用户按穿透按钮"与"另一个程序占了那一层"和"截图结束复位"三条路各写一次同一位——
    /// 那就是状态与样式分岔的三个来源（批次 WO）。收口之后只有一条路能改这一位。</para>
    /// </summary>
    public static void SetClickThrough(bool on)
        => AnnotationHub.Raise(on ? SessionEvent.GivePointerBack : SessionEvent.TakePointer);

    /// <summary>
    /// 「光晕」那颗：关 ↔ 任何工具常开（两档，判据在 Core 的 <see cref="CursorCircle"/>，这里只存那一位）。
    /// <para>这一位<b>不持久化</b>，与工具/颜色/粗细同口径（批次 WB-⑤：这块板子的语义是"讲完就擦"）；
    /// 持久化的是<b>半径</b>——用户裁"默认关 + 半径可调"，开关是每次讲课现翻的，圆多大是他定一次就够的事。</para>
    /// </summary>
    public static void ToggleHalo()
    {
        _haloAlways = !_haloAlways;
        RaiseStateChanged();
    }

    /// <summary>
    /// 把背景态落到每块屏的那扇玻璃上。<b>只由 <see cref="AnnotationHub"/> 调用</b>（事件那一侧）：
    /// 宿主不自己翻这块底，否则"按钮说白板开着、屏幕却还是桌面"就有了第二个书写点（§3.2 同一课）。
    /// <para>逐屏换底之后各窗自己整块重交一次（<c>SetBackdrop</c> 里做），因为 DIB 里存的是上一次合成好的结果，
    /// 只补脏区会把脏区之外那些行留在旧底上——屏幕上就留下一块洗不掉的白或一块没有底的透明。</para>
    /// </summary>
    public static void ApplyBackdrop(CanvasBackdrop backdrop)
    {
        var argb = CanvasBackdropMath.ArgbOf(backdrop);
        foreach (var screen in Screens) screen.Window.SetBackdrop(argb);
        RaiseStateChanged();
    }

    /// <summary>
    /// 幕布那块亮区跟着鼠标走：把"旧圈 ∪ 新圈"那一小片并进脏区——只报新位置的话，旧位置那一圈就赖在"亮"上暗不回来。
    /// <para><b>这块圆与光标光晕是同一块</b>（批次 S4-⑥，用户裁"合并成一块圆，两个读数"）：半径与换算只有
    /// <see cref="CursorCircle.RadiusInPixels"/> 一份，两处各乘一次 <c>Scale</c> 迟早差一像素。
    /// RN 把半径搬进设置页之后，这条更硬了——两处读的是<b>同一根滑杆</b>，谁再自己乘一次就当场看得见。</para>
    /// </summary>
    private static void UpdateCurtainFocus(Screen screen, int cursorX, int cursorY)
    {
        if (!CanvasBackdropMath.HasFocusHole(AnnotationHub.Backdrop)) return;
        var radius = CursorCircle.RadiusInPixels(screen.Scale, CursorCircleRadiusDip);
        var touched = screen.Window.SetFocus(new PixelPoint(cursorX - screen.Bounds.X, cursorY - screen.Bounds.Y), radius);
        if (!touched.IsEmpty) screen.Dirty.Add(touched);
    }

    /// <summary>白板底是否在场（工具条那颗据此高亮；读的是会话态，宿主不再记一份）。</summary>
    public static bool IsWhiteboard => AnnotationHub.Backdrop == CanvasBackdrop.Whiteboard;

    /// <summary>幕布是否在场（压暗 + 鼠标那块亮区）。</summary>
    public static bool IsCurtain => AnnotationHub.Backdrop == CanvasBackdrop.Curtain;

    /// <summary>「白板」那颗与 <c>canvas.board</c>：折成事件交给 Hub，这里不判方向。</summary>
    public static void ToggleWhiteboard() => AnnotationHub.Raise(SessionEvent.ToggleWhiteboard);

    /// <summary>「幕布」那颗与 <c>canvas.curtain</c>：同上。</summary>
    public static void ToggleCurtain() => AnnotationHub.Raise(SessionEvent.ToggleCurtain);
}
