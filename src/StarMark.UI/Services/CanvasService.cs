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
/// 屏幕画布的编排（规格 §16）：热键进入 → 每屏一块透明玻璃 → 画笔／荧光笔／橡皮 → 快照或退出。
/// <para>
/// 三条设计线：
/// ① <b>持久笔迹与荧光段分两张缓冲</b>——持久层（模型那一份是 <see cref="InkDoc"/>，缓冲是
/// <c>Screen.Persistent</c>）留在
/// <c>Screen.Persistent</c> 里，屏幕上那块 <c>Window.Pixels</c> 每次提交由"铺持久层 → 叠荧光段 →
/// 叠光晕"重算。合成到一处写的代价是淡出/擦除时回不到"原来那块地方画了什么"。
/// ② <b>只提交脏区</b>：4K 全屏整帧提交是 33MB/帧，60fps 下根本不可能（§16.7）。
/// ③ <b>退出只走显式动作</b>（工具条 ✕／Esc／热键／托盘），没有"过一会儿自己关掉"——
/// 讲到一半板子自己消失，比没有板子更糟。
/// </para>
/// </summary>
public static partial class CanvasService
{
    /// <summary>渲染循环的节拍。30fps 足够让淡出与光晕看着连续，空闲时开销可忽略。</summary>
    private static readonly TimeSpan FrameGap = TimeSpan.FromMilliseconds(33);

    /// <summary>拖动期间的提交节流：鼠标事件比帧密，每个点都提交等于把带宽花在中间态上。</summary>
    private const long DragThrottleMs = 16;

    /// <summary>一块屏上的画布：窗口 + 它自己的两层墨迹 + 待提交区域。</summary>
    private sealed class Screen
    {
        public required LayeredCanvasWindow Window { get; init; }
        public required IntRect Bounds { get; init; }

        /// <summary>这块屏自己的缩放。工具条的尺寸与摆位要用它，混屏时才不会半截在屏外。</summary>
        public required double Scale { get; init; }
        public required uint[] Persistent { get; init; }

        /// <summary>
        /// 这块屏上<b>留下来的</b>笔迹与它自己的历史——类型是两侧共用的那一个 <see cref="InkDoc"/>，
        /// 归属标成 <see cref="SurfaceRole.Board"/>＋屏号（方案 §3.4／§7：渲放宿主只是 InkDoc 的窗口）。
        /// </summary>
        public required InkDoc Ink { get; init; }

        /// <summary>
        /// 手上正在拖的那一条（<b>还没落定</b>）。它刻意不住在 <see cref="Ink"/> 里：
        /// 那一叠是"用户已经画成的东西"，而这条还在每个移动事件里变——把半成品放进历史载体，
        /// 就会出现"撤销退到一半的椭圆"那一类只有真机看得见的错（批次 WO）。
        /// 收手时整份 <c>ToAnnotation()</c> 落进 InkDoc，从此它只是模型里的一条。
        /// </summary>
        public CanvasStroke? Drawing { get; set; }

        public required EphemeralInk Trail { get; init; }

        public List<IntRect> Dirty { get; } = new();

        /// <summary>
        /// <b>曾经被烤进持久层的那一片</b>（不是"现在还剩什么"）。撤销与丢弃一笔时，
        /// <see cref="Recomposite"/> 必须把它连同剩下的笔迹一起擦掉——只按剩下的算区域，
        /// 被撤掉那条自己占过、而剩余笔迹没覆盖到的地方就永远没人清。
        /// <para>真机反馈："撤销只能撤销绘制图形的部分（比如只撤销一个完整椭圆的一半）"：
        /// 椭圆是一条笔迹，撤掉它之后屏幕上还留半只，因为那半只在<b>剩余</b>笔迹的包围盒之外。</para>
        /// </summary>
        public IntRect Composited { get; set; }

        /// <summary>
        /// 上一帧那团光标光晕占的地方。<b>只有它需要无条件复原</b>：光晕跟着鼠标走，
        /// 旧位置不擦就成一坨赖着不走的光斑。
        /// <para>
        /// 这里刻意<b>不再</b>记"上一帧所有荧光段叠过的地方"——那是批次 WG 之前的做法，
        /// 它让每一帧的脏区等于整条笔迹的包围盒，而脏区一大，"从持久层重铺 + 整段重画"就跟着变大，
        /// 一条长笔迹就能把 UI 线程钉到几百毫秒一帧。哪一帧该重算哪一块，现在由
        /// <see cref="EphemeralInk.Segment.PaintScale"/> 与 <see cref="CanvasStroke.TailBounds"/> 分别说。
        /// </para>
        /// </summary>
        public IntRect LastGlow { get; set; }

        /// <summary>这一帧要不要在光标处叠一团光晕、叠在哪（由帧循环按光标落在哪块屏决定）。</summary>
        public PixelPoint? GlowAt { get; set; }

        public long LastFlushMs { get; set; }
    }

    private static readonly List<Screen> Screens = new();
    private static CanvasToolbarWindow? _toolbar;
    private static DispatcherQueueTimer? _frame;
    private static bool _busy;

    private static CanvasTool _tool = CanvasTool.Pen;
    private static int _colorIndex;
    private static int _widthStep = CanvasWidths.DefaultStepIndex;

    /// <summary>
    /// 穿不穿透<b>不在这里存</b>（架构方案 §3.2：宿主不持模式布尔位）。
    /// 状态机在 <see cref="AnnotationHub"/>，这一句只是给工具条那类旧接线留的读法——
    /// 它读的是会话态，不是第二份旗标，所以"工具条说绘制中、窗口却带着穿透位"这种分岔
    /// 少了一个来源（剩下的那一个由 <see cref="LayerDirector.ReconcileStyles"/> 每帧兜）。
    /// </summary>
    private static bool ClickThroughHere => !AnnotationHub.Stage.GlassTakesPointer();
    private static HaloMode _halo = HaloMode.HighlighterOnly;

    /// <summary>
    /// 手上一按是什么性质。<b>穿透态收不到 WM_LBUTTONDOWN</b>（那一次按下归了下层应用），
    /// 所以"荧光笔按住即画 / Ctrl+Alt 快速圈画"只能由帧循环轮询按键状态发现，
    /// 发现后临时摘掉穿透、自己补一次 SetCapture，抬起再恢复。
    /// </summary>
    private enum Press { None, Drawing, Ephemeral, QuickPen, Shape, PolyLine }

    private static Press _press;

    /// <summary>拖图形时那一按的起点（本屏物理像素）。终点就是当前光标，所以只留起点。</summary>
    private static PixelPoint _shapeFrom;

    /// <summary>
    /// 正在勾的<b>折线</b>：已经定形的顶点（本屏物理像素）。null＝没在勾。
    /// <para>它与"一次拖拽定形"的四种图形不同，是<b>跨按</b>的：一次按下拖一段，抬手把终点定成顶点，
    /// 折线还开着；收口由"再点当前工具／Esc／换工具／换粗细"负责。所以它必须有一份
    /// 自己记下的颜色与粗细——中途换了设置，正在勾的这条不能悄悄变浓变细（症状："画着画着笔自己变了"）。</para>
    /// </summary>
    private static List<PixelPoint>? _polyPoints;
    private static Screen? _polyScreen;
    private static int _polyColour;
    private static int _polyWidth;

    /// <summary>手上有笔时它属于哪块屏（抬起/读态收尾都要用它，光标可能已经飘到别的屏）。</summary>
    private static Screen? _pressScreen;

    /// <summary>画布模式是否开着——问的是"有没有玻璃在场"，不再另存一份旗标。</summary>
    public static bool IsRunning => Screens.Count > 0;

    /// <summary>当前是不是鼠标穿透态（工具条据此画那颗按钮的高亮）。</summary>
    public static bool IsClickThrough => ClickThroughHere;

    /// <summary>手上正有一笔吗（每帧验层要跳过它：那一笔已经归画布画完）。</summary>
    public static bool PressInFlight => _press != Press.None;

    /// <summary>有没有"半件事"在场（勾到一半的折线）——Esc 的第一级退的就是它。</summary>
    public static bool HasWorkInProgress => _polyPoints is { Count: > 0 };

    public static CanvasTool Tool => _tool;

    public static int ColorIndex => _colorIndex;

    public static int WidthStep => _widthStep;

    /// <summary>那一档开着没有（按钮亮不亮读它；"这一帧叠不叠"是另一件事，见 <see cref="CursorCircle.ShowsHalo"/>）。</summary>
    public static bool HaloEnabled => CursorCircle.IsOn(_halo);

    /// <summary>那一档叫什么（状态行与 tooltip 的唯一读者从这里取，界面不自己写中文）。</summary>
    public static string HaloName => CursorCircle.NameOf(_halo);

    /// <summary>颜色表沿用截图标注那一份（一条事实一个出处：两处色表迟早分岔）。</summary>
    public static IReadOnlyList<AnnotationColor> Palette => Annotation.Palette;

    /// <summary>状态变了（工具/颜色/粗细/穿透/光晕）——工具条订阅它刷新高亮。</summary>
    public static event Action? StateChanged;

    /// <summary>广播状态（工具/颜色/粗细/穿透/光晕/让位原因）——工具条的状态行是唯一读者。</summary>
    public static void RaiseStateChanged() => StateChanged?.Invoke();

    /// <summary>
    /// 快捷键面板（工具条上那颗「⌨」调出来）。<b>它跟工具条一样不参与穿透</b>，
    /// 定序时排在"工具条之下、画布之上"——它是来看一眼的，不该挡住手边的按钮。
    /// </summary>
    private static CanvasHotkeyPanelWindow? _panel;

    private static void Report(string title, string message)
    {
        var shown = false;
        try { shown = App.MainWindow?.TryShowTrayNotification(title, message) == true; }
        catch (Exception ex) { StarLog.Warn($"[Canvas] 回报没送出去：{ex.Message}"); }
        if (!shown)
        {
            try { App.MainWindow?.ShowError(title, message); }
            catch (Exception ex) { StarLog.Warn($"[Canvas] 主窗提示条也没能显示：{ex.Message}"); }
        }
        StarLog.Info($"[Canvas] {title}：{message}");
    }
}
