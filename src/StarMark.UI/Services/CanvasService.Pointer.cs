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
/// CanvasService 的这一段——一按一放一拖怎么办：落笔分派、拖动补点、收笔落定，以及穿透态抢按与折线那套跨按状态。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

    // ────────── 输入 ──────────

    private static void OnPressed(Screen screen, CanvasPointer pointer)
    {
        if (screen.Window.IsClickThrough) return;          // 穿透态不该收到，真收到也不能画（鼠标本来要给下面的应用）
        // 这一按落在自家条子的矩形里 ⇒ 画布<b>不接</b>。正常情况下命中测试已经把那一格让开了
        // （<c>HTTRANSPARENT</c>，按下根本不会寄给我们）；走到这里说明有条子窗没登记进名册，
        // 或某个系统不认那个返回值。兜住它的价值是"绝不把轨迹画在自己的工具条上"这条底线
        // 不依赖任何 Win32 行为；日志留着当下一次归因的证据（反复出现＝让位那一环没生效）。
        if (LayerDirector.IsPointOnChrome(screen.Bounds.X + pointer.At.X, screen.Bounds.Y + pointer.At.Y))
        {
            StarLog.WarnThrottled("canvas:chrome-press",
                "[Canvas] 绘制态收到落在工具条矩形内的按下：画布没有接这一按（下一次点击会归条子）", windowMs: 5_000);
            return;
        }
        BeginPress(screen, pointer.At, _tool switch
        {
            CanvasTool.Highlighter => Press.Ephemeral,
            // 折线要跨按接段，所以按下时不能像别的图形那样"这一按就是一条"
            CanvasTool.PolyLine => Press.PolyLine,
            // 图形与画笔都吃"拦截态"那一按（穿透位由 Core 那张按态表给），但落笔方式不同：一次拖拽定形，不是跟着手走
            { } tool when tool.IsShape() => Press.Shape,
            _ => Press.Drawing,
        });
    }

    /// <summary>
    /// 落一笔。<paramref name="kind"/> 决定这一笔是什么，<b>不看 <c>_tool</c></b>：
    /// 穿透态下按住 Ctrl+Alt 圈画时选中的可能是荧光笔，但墨迹要落进持久层。
    /// </summary>
    private static void BeginPress(Screen screen, PixelPoint at, Press kind)
    {
        _press = kind;
        _pressScreen = screen;
        var colour = Palette[_colorIndex].Bgra;
        var now = Environment.TickCount64;
        if (kind == Press.Ephemeral)
        {
            var segment = screen.Trail.Begin(at, colour, CanvasWidths.At(_widthStep), now);
            screen.Dirty.Add(segment.Stroke.Bounds);
        }
        else if (kind == Press.Shape)
        {
            // 预览落在临时层，不落持久层：拖到一半取消、拖过头再拉回来，都不该留下一条撤不掉的笔迹
            _shapeFrom = at;
            screen.Dirty.Add(screen.Trail.SetPreview(ShapeStroke(at, at)));
        }
        else if (kind == Press.PolyLine)
        {
            // <b>折线不跨屏</b>：顶点表整份是"那一块屏自己的物理像素"（坐标系原点在那块屏左上角），
            // 在另一块屏上点第二下就会把两个坐标系的数写进同一条笔迹——症状是那一段飞到主屏的另一头。
            // 所以换屏先把手上这条收口（用户已经画成的那几段不能丢），再从这一点起一条新的。
            if (_polyScreen is not null && _polyScreen != screen) FinishOpenPolyLine();
            // 已经在勾就只是"接着拖下一段"——顶点由上一段的抬手定，这里不能再加一个（否则每段开头都多一个重点）
            _polyPoints ??= new List<PixelPoint> { at };
            if (_polyScreen is null)
            {
                _polyScreen = screen;
                _polyColour = colour;                       // 颜色与粗细在起勾那一刻定死，中途换设置不悄悄改它
                _polyWidth = WidthFor(CanvasTool.PolyLine);
            }
            ShowPolyPreview(at);
        }
        else
        {
            var tool = kind == Press.QuickPen ? CanvasTool.Pen : _tool;
            var stroke = new CanvasStroke(tool, colour, WidthFor(tool), at);
            screen.Drawing = stroke;
            screen.Dirty.Add(CanvasCompositor.Paint(
                screen.Persistent, screen.Window.Width, screen.Window.Height, stroke));
        }
        Flush(screen);
    }

    private static void OnMoved(Screen screen, CanvasPointer pointer)
    {
        var now = Environment.TickCount64;
        var dirty = false;
        if (_press == Press.Ephemeral)
        {
            if (screen.Trail.Extend(pointer.At, now))
            {
                // 只把"新走的那一小条"并进脏区（不是整条笔迹的包围盒）：这一句加上 Flush 的裁剪，
                // 才是"荧光笔拖动跟手"的全部凭据
                screen.Dirty.Add(screen.Trail.Segments[^1].Stroke.TailBounds);
                dirty = true;
            }
        }
        else if (_press == Press.Shape)
        {
            // 每一帧整份替换预览：脏区是"旧的那份 + 新的这份"，所以拖过去的那条影子会被擦回来
            screen.Dirty.Add(screen.Trail.SetPreview(ShapeStroke(_shapeFrom, pointer.At)));
            dirty = true;
        }
        else if (_polyPoints is not null && screen == _polyScreen)
        {
            // 折线<b>跨按</b>开着：没按住的时候那根橡皮筋也要跟着手走，否则"下一个顶点落在哪"全无预告，
            // 就只能凭感觉点。预览整份替换，脏区含旧那份，所以拖过的影子会被擦回来。
            ShowPolyPreview(pointer.At);
            dirty = true;
        }
        else if (screen.Drawing is { } stroke && stroke.AddPoint(pointer.At))
        {
            screen.Dirty.Add(CanvasCompositor.PaintTail(
                screen.Persistent, screen.Window.Width, screen.Window.Height, stroke));
            dirty = true;
        }
        if (dirty && now - screen.LastFlushMs >= DragThrottleMs) Flush(screen);
    }

    private static void OnReleased(Screen screen, CanvasPointer pointer)
    {
        FinishPress(screen, pointer.At);
        // 轮询接手的那两种按下（荧光笔按住 / Ctrl+Alt 圈画）是"临时摘掉穿透"换来的，
        // 抬起必须还回去——否则一次圈画之后整台机器的鼠标就被我们扣住了。
        if (AnnotationHub.QuickPressInFlight) EndTemporaryPress();
    }

    /// <summary>
    /// 收手上那一笔（不碰穿透态）：荧光段交给 TTL 淡出，持久笔迹要定形。
    /// <paramref name="at"/> 只有折线用——抬手那一点就是它刚拖出来的那个顶点。
    /// </summary>
    private static void FinishPress(Screen? screen, PixelPoint? at = null)
    {
        if (screen is null) { _press = Press.None; return; }
        if (_press == Press.Ephemeral) Flush(screen);          // 段留在 Trail 里按 TTL 淡，不进持久层
        else if (_press == Press.PolyLine)
        {
            // 抬手<b>不收口</b>：只是把这一段的终点定成顶点，折线还开着等下一按。
            // 所以这里既不 Commit 也不 Recomposite——它眼下整条都还活在预览槽里。
            // 只在折线自己那块屏上定顶点：顶点表是"那块屏自己的物理像素"，别的屏的坐标混进来，
            // 那一段就会飞到另一块屏的另一头去。
            if (screen == _polyScreen) AddVertex(at);
            ShowPolyPreview(null);
            Flush(screen);
        }
        else if (_press == Press.Shape)
        {
            // 定形＝把预览那份"换个归属"：同一串点从临时层挪进持久层，不重算几何。
            // 重算就会出现"预览一个样、落下另一个样"（拖的时候是圆的、松手变有角）。
            var preview = screen.Trail.Preview;
            screen.Dirty.Add(screen.Trail.DropPreview());
            if (preview is not null) screen.Ink.Add(preview.ToAnnotation());
            Recomposite(screen);
            Flush(screen);
        }
        else
        {
            // 这一条如果不够格留下来（橡皮点一下），它按下时已经烤进持久层的那一小片要单独交回去擦——
            // 否则屏幕上留下一个"没有任何笔迹对应、撤销里也没有"的洞
            var footprint = screen.Drawing?.Bounds ?? default;
            if (!CommitLiveStroke(screen)) Recomposite(screen, footprint);
            else Recomposite(screen);
            Flush(screen);
        }
        _press = Press.None;
        AnnotationHub.QuickPressInFlight = false;
    }

    /// <summary>
    /// 把手上那条落成模型里的一笔：<b>整份转成 <see cref="Annotation"/> 交给这块屏的 InkDoc</b>，
    /// 实时缓冲随即清空。够不够格留（橡皮点一下不算一笔）由笔迹自己说，留不下就返回 false，
    /// 调用方要把它的落点交回去擦——屏幕上不能留一片"没有笔迹对应、撤销里也没有"的洞（批次 WO）。
    /// </summary>
    private static bool CommitLiveStroke(Screen screen)
    {
        var stroke = screen.Drawing;
        screen.Drawing = null;
        if (stroke is null || !stroke.WorthKeeping) return false;
        screen.Ink.Add(stroke.ToAnnotation());
        return true;
    }

    /// <summary>
    /// 穿透态下的"按住即画"。规格 §16.5.2 要的是<b>零模式切换摩擦</b>：荧光笔按住才有、
    /// 松开即透；Ctrl+Alt+拖动直接圈画。可穿透态下我们收不到 <c>WM_LBUTTONDOWN</c>
    /// （那一次按下归了下层应用），所以只能每帧看按键状态——发现按下才临时摘掉穿透，
    /// 之后的移动与抬起才归我们。
    /// </summary>
    private static void PollPress(int cursorX, int cursorY)
    {
        var down = LayeredCanvasWindow.LeftButtonDown;

        // 抬起发生在我们还没接管的那一帧里（<33ms 的短按）：轮询补一次收尾，否则笔永远"没松"
        if (_press is Press.Ephemeral or Press.QuickPen && !down)
        {
            FinishPress(_pressScreen);
            EndTemporaryPress();
            return;
        }
        // 会话闸门（方案 §4 的 C2）：读态<b>只在 Board·穿透态</b>跑。绘制态收得到按下，再叠一套读态
        // 就是同一按两家用；截图期间这块玻璃连显示都不被允许，抢一次就把截图打断在别的程序手里。
        if (_press != Press.None || AnnotationHub.Stage != AnnotationStage.BoardPenetrating || !down) return;

        // 穿透态只保留一种抢按：<b>显式的 Ctrl+Alt 圈画</b>。2026-09-27 用户改判，删掉"当前选的是荧光笔
        // 就抢按"那一臂——他要"穿透态按下去就是下层应用的点击"，要画荧光笔得先关掉穿透
        // （那颗按钮或 canvas.through）。方向判据在 CanvasModes 一处，接线层不许自己现写布尔（WF-1 口径）。
        if (!CanvasModes.ClaimsPressInPenetrating(LayeredCanvasWindow.CtrlAltDown)) return;
        // 抢之前先问"这一点落在自家条子的矩形里吗"（§6.3）：光标停在工具条／快捷键面板上时那一按是<b>点按钮</b>，
        // 抢走它就长成"点菜单栏却画出一条轨迹、那颗按钮没反应"（真机原话）。问矩形而不问命中到谁——
        // 命中测试永远答最上面那一层，绘制态它就是玻璃自己（同一问题只有一个出处）。
        // 贴图不算 chrome：穿透态下在贴图上 Ctrl+Alt 圈注是要留的能力（§13 用例 5）。
        if (LayerDirector.IsPointOnChrome(cursorX, cursorY)) return;

        var screen = ScreenAt(new PixelPoint(cursorX, cursorY));
        if (screen is null) return;

        foreach (var s in Screens) { s.Window.SetClickThrough(false); s.Window.SetDrawCursor(true); }
        screen.Window.Capture();                                   // 那一次按下不会再来，抓取要自己补
        AnnotationHub.QuickPressInFlight = true;                   // 每帧对账这一刻要跳过（唯一合法的不一致）
        BeginPress(screen, new PixelPoint(cursorX - screen.Bounds.X, cursorY - screen.Bounds.Y), Press.QuickPen);
    }

    /// <summary>把临时摘掉的穿透还回去（<b>会话态没动过</b>，所以工具条那行字不会跳）。</summary>
    private static void EndTemporaryPress()
    {
        _press = Press.None;
        AnnotationHub.QuickPressInFlight = false;
        if (AnnotationHub.Stage != AnnotationStage.BoardPenetrating) return;    // 本来就在绘制态，不用恢复
        foreach (var s in Screens) { s.Window.SetClickThrough(true); s.Window.SetDrawCursor(false); }
    }
    private static Screen? ScreenAt(PixelPoint point)
        => Screens.FirstOrDefault(s => s.Bounds.X <= point.X && point.X < s.Bounds.Right
            && s.Bounds.Y <= point.Y && point.Y < s.Bounds.Bottom);

    private static int WidthFor(CanvasTool tool)
        => tool == CanvasTool.Eraser ? CanvasWidths.EraserDiameter : CanvasWidths.At(_widthStep);

    /// <summary>
    /// 按当前工具与粗细，把一次拖拽展成一条笔迹。<b>拖拽期间的预览与松手时的定形共用这一句</b>：
    /// 两处各算一遍几何，就会长成"拖的时候一个样、松手另一个样"。
    /// </summary>
    /// <remarks>
    /// 颜色与粗细<b>从当前设置里取</b>，不作参数：它们本就该跟着工具条上那颗走，
    /// 而把 <c>colour</c> 做成参数会让"预览用 A 色、定形用 B 色"这种错法编译得过（批次 WF 那条口径：
    /// 一个布尔/取值的含义只在一处时，接线处现写就是必然出错）。
    /// </remarks>
    private static CanvasStroke ShapeStroke(PixelPoint from, PixelPoint to)
    {
        var width = WidthFor(_tool);
        return CanvasStroke.FromPoints(_tool, Palette[_colorIndex].Bgra, width,
            CanvasShapes.Outline(_tool, from, to, width));
    }

    /// <summary>
    /// 折线的顶点：<b>抬手那一点只有在"真的拖出了一段"时才算一个顶点</b>。
    /// 原地按一下也定顶点的话，屏幕上会攒出一串看不见的重点，而它们两两之间是零长度段——
    /// 收口时就只剩"一个圆帽孤零零地留在板上"这种说不清的症状。
    /// </summary>
    private static void AddVertex(PixelPoint? at)
    {
        if (at is not { } p || _polyPoints is not { } points) return;
        if (points.Count > 0 && points[^1].Equals(p)) return;
        points.Add(p);
    }

    /// <summary>
    /// 换掉折线的预览：<b>已定形的顶点 + 伸向光标的那一段橡皮筋</b>（<paramref name="rubber"/> 为 null＝刚抬手，
    /// 只画已定形的部分）。点不足两个时丢掉预览而不是画一个点。
    /// <para>它只弄脏 <see cref="Screen.Dirty"/>，<b>不提交</b>——提交时机由调用方决定（拖拽期要走节流）。</para>
    /// </summary>
    private static void ShowPolyPreview(PixelPoint? rubber)
    {
        if (_polyScreen is not { } screen || _polyPoints is not { } points) return;
        var preview = CanvasShapes.PolyLinePreview(points, rubber);
        screen.Dirty.Add(preview.Count < 2
            ? screen.Trail.DropPreview()
            : screen.Trail.SetPreview(CanvasStroke.FromPoints(CanvasTool.PolyLine, _polyColour, _polyWidth, preview)));
    }

    /// <summary>
    /// 收口正在勾的折线：<b>整条作为一条笔迹</b>进持久层。
    /// <para>为什么不是每个顶点一条：撤销一格要退掉"刚才画的那条折线"，而不是它的一小段
    /// （用户按 Ctrl+Z 的心智单位是"我画的那个东西"）；而橡皮、存图、贴图全都按笔迹走，
    /// 拆成多条只会让同一件事有五种表现。</para>
    /// </summary>
    private static void FinishOpenPolyLine()
    {
        var screen = _polyScreen;
        var points = _polyPoints;
        _polyPoints = null;
        _polyScreen = null;
        if (screen is null || points is null) return;
        screen.Dirty.Add(screen.Trail.DropPreview());
        if (points.Count >= 2)
        {
            screen.Ink.Add(CanvasStroke.FromPoints(CanvasTool.PolyLine, _polyColour, _polyWidth, points).ToAnnotation());
            Recomposite(screen);
        }
        Flush(screen);
    }

    /// <summary>丢掉正在勾的折线且<b>不提交</b>（清空笔迹／退出画布：手上一半的东西不该落进结果里）。</summary>
    private static void CancelOpenPolyLine()
    {
        var screen = _polyScreen;
        _polyPoints = null;
        _polyScreen = null;
        if (screen is null) return;
        screen.Dirty.Add(screen.Trail.DropPreview());
        Flush(screen);
    }
}
