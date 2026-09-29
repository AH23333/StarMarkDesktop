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
/// CanvasService 的这一段——板上已有之物怎么改：撤销、重做、清空、丢弃手上那条、把整层重烤一遍。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

    /// <summary>
    /// 撤销<b>全机最后落的那一笔</b>（方案 §13 用例 9 的 LIFO，§7 的"栈全局化、按 surface 归属"）。
    /// <para>从前这里是 <c>foreach (screen) screen.Ink.Undo()</c>——一次按键把<b>每块屏各退一条</b>：
    /// 双屏上按一次撤销，两块屏同时各掉一笔，而掉的那两条根本不是同一时刻画的（"撤错东西"比"撤不动"更糟）。</para>
    /// </summary>
    public static void Undo()
    {
        if (_polyPoints is { } open)
        {
            // 勾到一半时"撤销"退的是<b>最后一个顶点</b>，不是板上那条旧笔迹：
            // 用户眼睛正盯着这条折线，撤错对象比多按一次难受得多。退到只剩起点仍然开着
            // （还能从那一点接着拖），一个点都不剩才整条丢掉。
            var where = _polyScreen;
            open.RemoveAt(open.Count - 1);
            if (open.Count == 0) CancelOpenPolyLine();
            else { ShowPolyPreview(null); if (where is not null) Flush(where); }
            StateChanged?.Invoke();
            return;
        }
        var target = Screens.Where(s => s.Ink.Count > 0)
            .OrderByDescending(s => s.Ink.LastOrder)
            .FirstOrDefault();
        if (target is null || !target.Ink.Undo()) return;
        // 只重烤这一块屏：它自己的落点与"曾经烤过的那一片"由 Recomposite 一起算（批次 WO），
        // 别的屏的墨一笔没动，不该跟着重烤一遍（4K 上一帧是几十兆）。
        Recomposite(target);
        Flush(target);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 重做：把刚被撤销掉的那一笔放回去，<b>只放一块屏</b>。
    /// <para>与 <see cref="Undo"/> 同一个形状：目标用的是 <see cref="InkDoc.NextOrder"/>
    /// （这一叠"重做会回来的那一笔"是全机第几笔），不是 <c>LastOrder</c>——撤销之后
    /// <c>LastOrder</c> 指的是"还留着的那一笔"，两块屏都撤销过时会挑错那一叠。</para>
    /// <para>没有可重做的东西时安静地什么都不做（工具条那颗与「撤销」对称，不灰、不弹说明）；
    /// 全局热键那一路由 <see cref="RequireRunning"/> 负责"板子没开着"的可见原因。</para>
    /// </summary>
    public static void Redo()
    {
        var target = Screens.Where(s => s.Ink.CanRedo)
            .OrderByDescending(s => s.Ink.NextOrder)
            .FirstOrDefault();
        if (target is null || !target.Ink.Redo()) return;
        Recomposite(target);
        Flush(target);
        StateChanged?.Invoke();
    }

    public static void ClearAll()
    {
        CancelOpenPolyLine();               // 勾到一半的折线不能被"清空"顺手提交出去
        foreach (var screen in Screens)
        {
            // 连手上那条一起丢：清空之后鼠标还动着的话，那条"幽灵笔迹"会在清完的板子上补出一截
            // （以前那个容器把清空与丢笔迹做成了一次，拆成"模型 + 实时缓冲"两层之后必须自己说清楚）
            screen.Drawing = null;
            screen.Ink.Clear();
            DropTrail(screen);
            Recomposite(screen);
        }
        FlushAll();
    }

    /// <summary>
    /// 丢掉这块屏上所有还活着的荧光段。<b>必须先把它们占过的地方并进脏区</b>：段一从 list 里消失就再没人
    /// 画它，而它贴过的那一层像素还留在分层窗缓冲里——症状是"清空之后屏幕上留着一块擦不掉的光"。
    /// </summary>
    private static void DropTrail(Screen screen)
    {
        var was = screen.Trail.LiveBounds;
        screen.Trail.Clear();
        if (!was.IsEmpty) screen.Dirty.Add(was);
    }

    /// <summary>
    /// 画布里的 Esc：<b>两级</b>。正在勾折线时先收口这一条（板子继续开着），没有手上一半的东西才退出画布。
    /// <para>只有一级"Esc＝退出画布"的话，勾到一半想停下就得整块板子一起没——而那条折线也没画成。
    /// 真机症状会是"按 Esc 之后我的笔迹全没了"（退出即丢弃）。</para>
    /// </summary>
    public static void Escape() => AnnotationHub.EscapeBoard();

    /// <summary>Esc 的第一级：收掉手上那半件事（勾到一半的折线），板子继续开着。</summary>
    public static void CloseWorkInProgress()
    {
        if (_polyPoints is null) return;
        FinishOpenPolyLine();
        RaiseStateChanged();
    }

    /// <summary>
    /// 持久层重算：清掉"<b>现在还剩的笔迹</b> ∪ <b>曾经烤出去的那一片</b> ∪ <paramref name="alsoErase"/>"
    /// 再按顺序全部重画一遍。
    /// <para>为什么不能只清"现在还剩的"：<see cref="Screen.Composited"/> 那条——撤销一条笔迹之后，
    /// 它占过的地方如果不在剩余笔迹的包围盒里，就<b>没有任何一步会去擦它</b>，屏幕上留下半只椭圆，
    /// 而撤销栈里已经没有东西能把它退掉（真机反馈的"只能撤销图形的一半"）。</para>
    /// <para>区域也不取整块屏幕（那是 33MB 的提交）。橡皮那条不幂等（按比例减 alpha，走两次擦过头），
    /// 所以必须一次画成，不能增量补。</para>
    /// </summary>
    private static void Recomposite(Screen screen, IntRect alsoErase = default)
    {
        var width = screen.Window.Width;
        var height = screen.Window.Height;
        // 要擦的 = 上一次烤过的那一片 ∪ 这次被丢掉的那一条（区域算法与"为什么不能只擦剩下的"都在 Core 那条注释里）
        var toErase = CanvasCompositor.Union(new[] { screen.Composited, alsoErase }, width, height);
        var erase = CanvasCompositor.Rebake(screen.Persistent, width, height, Brushes(screen),
            toErase, out var remaining);
        screen.Composited = remaining;
        if (!erase.IsEmpty) screen.Dirty.Add(erase);
    }

    /// <summary>
    /// 把这一叠模型还原成渲放要的笔迹串。<b>每次重烤现算，不留第二份缓存</b>：
    /// 缓存一份"笔迹形状"就等于把同一叠墨存两处，撤销／清空之后两边对不对得上线程说了算（R2 双引擎的老路）。
    /// 重烤只发生在落笔/撤销/清空这类离散动作上，一叠几百条的换算代价换掉的是"两份真值"这一整类缺陷。
    /// </summary>
    private static List<CanvasStroke> Brushes(Screen screen)
    {
        var marks = screen.Ink.Marks;
        var strokes = new List<CanvasStroke>(marks.Count);
        for (var i = 0; i < marks.Count; i++) strokes.Add(CanvasStroke.FromAnnotation(marks[i]));
        return strokes;
    }

    /// <summary>换工具/换粗细之前先把手上那条收掉，免得它接到新设置下去（症状："画着画着笔自己变粗了"）。</summary>
    private static void CommitOpenStroke()
    {
        // 轮询抢来的那一按（荧光笔/Ctrl+Alt 圈画）也要在这里收口：换工具时它还挂着的话，
        // 抬起事件会被新工具吃掉，屏幕上就留下一条"永远在画"的笔迹
        if (_press is Press.Ephemeral or Press.QuickPen or Press.Shape or Press.PolyLine)
        {
            FinishPress(_pressScreen);
            EndTemporaryPress();
        }
        // 折线单独收：它按定义就是"跨按还开着"的那一条，上面那个 switch 只收了手上这一段
        FinishOpenPolyLine();
        var changed = false;
        foreach (var screen in Screens)
            if (screen.Drawing is not null)
            {
                CommitLiveStroke(screen);
                Recomposite(screen);
                changed = true;
            }
        if (changed) FlushAll();
    }
}
