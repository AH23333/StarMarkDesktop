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
/// CanvasService 的这一段——「选择」工具：点选、框选、拖动、落定（P-288）。
/// <para>
/// 交互与截图那一条的选择同语言：点一条＝选中它；点在已选中的那组里＝拖整组；
/// 从空白处拖＝橡皮筋框选；松手把拖出来的偏移<b>一步</b>写回模型（<see cref="InkDoc.ReplaceMany"/>：
/// 挪一组只算一步历史，撤一次整组回来）。
/// </para>
/// <para>
/// 渲放走持久层那条现成的路：拖动期间 <c>Brushes</c> 把选中的那几条按偏移画（墨跟手走），
/// 落定时偏移写回模型、归零——同一份墨从头到尾只有一个真值。选中框与橡皮筋<b>不进持久层</b>，
/// 只画在 <c>Window.Pixels</c> 上（快照、存图、贴图都不带走它）。
/// </para>
/// <para>
/// 这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，
/// 这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。
/// </para>
/// </summary>
public static partial class CanvasService
{

    /// <summary>点选的容差（物理像素）：笔迹太细，逐像素命中几乎点不中。</summary>
    private const int SelectSlop = 8;

    /// <summary>选中框／橡皮筋外面留的呼吸边（像素）：框贴着墨画会把细笔迹盖掉一截。</summary>
    private const int ChromeExpand = 2;

    /// <summary>
    /// 选中框的颜色：与工具条高亮同一颗蓝。字节序是这块板子统一的
    /// <b>BGRA 预乘</b>（<c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c>），不透明色预乘前后同值。
    /// </summary>
    private const uint SelectionBlue = 0xFF2D6EC4;

    /// <summary>「选择」那颗（工具条）：再点一次＝取消，与三支笔同一交互语言。</summary>
    public static void ToggleSelectTool()
    {
        CommitOpenStroke();
        _selectTool = !_selectTool;
        if (!_selectTool) DropSelection();
        RaiseStateChanged();
    }

    /// <summary>「选择」工具在场吗（工具条据此画那颗的高亮）。</summary>
    public static bool IsSelectTool => _selectTool;

    /// <summary>
    /// 「选择」工具下的那一按：点在笔迹上＝准备拖它，点在已选中的那组里＝拖整组；
    /// 点在空白处＝起一条橡皮筋。命不命中、容差多少由 <see cref="AnnotationPainter"/> 说，
    /// 这里只按结论分派。
    /// </summary>
    private static void BeginSelectPress(Screen screen, PixelPoint at)
    {
        _pressScreen = screen;
        var marks = screen.Ink.Marks;
        if (AnnotationPainter.HitTest(marks, at, SelectSlop) is { } index && index >= 0 && index < marks.Count)
        {
            var mark = marks[index];
            // 已在这一组里＝保住整组（拖组）；不在组里、或换了屏＝换成这一条
            if (screen != _selectScreen || _selected is not { } picked || !picked.Contains(mark))
                SetSelection(screen, new[] { mark });
            _press = Press.SelectMove;
            _selectOrigin = at;
            _selectOffset = default;
        }
        else
        {
            _press = Press.SelectBand;
            _bandFrom = at;
            _bandTo = at;
        }
        Flush(screen);
    }

    /// <summary>
    /// 拖动选中的那几条：节流拍上推进偏移，新旧两拍都弄脏，重烤持久层——
    /// 持久层烤到哪一拍，框就画到哪一拍，两者永远同一拍。
    /// </summary>
    private static void DragSelection(Screen screen, PixelPoint at)
    {
        if (screen != _pressScreen) return;
        if (Environment.TickCount64 - screen.LastFlushMs < DragThrottleMs) return;
        var before = SelectionFootprint();
        _selectOffset = new PixelPoint(at.X - _selectOrigin.X, at.Y - _selectOrigin.Y);
        var after = SelectionFootprint();
        screen.Dirty.Add(before);
        screen.Dirty.Add(after);
        Recomposite(screen);
        Flush(screen);
    }

    /// <summary>拖橡皮筋：节流拍上挪 <see cref="_bandTo"/>。框画在 Window.Pixels 上，持久层不动。</summary>
    private static void DragBand(Screen screen, PixelPoint at)
    {
        if (screen != _pressScreen) return;
        if (Environment.TickCount64 - screen.LastFlushMs < DragThrottleMs) return;
        var before = BandFootprint();
        _bandTo = at;
        var after = BandFootprint();
        screen.Dirty.Add(before);
        screen.Dirty.Add(after);
        Flush(screen);
    }

    /// <summary>
    /// 松手落定：把拖出来的偏移<b>一步</b>写回模型（<see cref="InkDoc.ReplaceMany"/>，
    /// 挪一组只算一步历史）。原地松手（一个像素都没拖）不入历史，只把拖动期间
    /// 烤进持久层的偏移复原回来，选中保留。
    /// </summary>
    private static void CommitSelectionMove(Screen screen, PixelPoint at)
    {
        var dx = at.X - _selectOrigin.X;
        var dy = at.Y - _selectOrigin.Y;
        var before = SelectionFootprint();
        if (dx == 0 && dy == 0)
        {
            _selectOffset = default;
            screen.Dirty.Add(before);
            Recomposite(screen);
            Flush(screen);
            return;
        }
        var picked = _selected;
        var pairs = new List<(int Index, Annotation Mark)>();
        var marks = screen.Ink.Marks;
        // 按实例比对，不按下标找：下标在撤销/清空之后会失效，实例比对天然免疫
        for (var i = 0; i < marks.Count; i++)
            if (picked is not null && picked.Contains(marks[i]))
                pairs.Add((i, marks[i].MovedBy(dx, dy)));
        if (pairs.Count == 0)
        {
            DropSelection();                // 选中集在拖动中被别的动作动了：框不能再画
            return;
        }
        screen.Ink.ReplaceMany(pairs);
        var moved = pairs.Select(p => p.Mark).ToList();
        var boxes = moved.Select(m => m.Bounds()).ToList();
        _selected = moved;
        _selectionBoxes = boxes;
        _selectOffset = default;
        _selectOrigin = at;
        var after = SelectionFootprint();
        screen.Dirty.Add(before);
        screen.Dirty.Add(after);
        Recomposite(screen);
        Flush(screen);
    }

    /// <summary>
    /// 松手收橡皮筋：框住的都进选中集，一条都没框住＝取消选择（点空白也走这条）。
    /// 空拾取那一刻 <see cref="_bandTo"/> <b>故意不清</b>：余下的框连同脏区
    /// 一起交给 <see cref="DropSelection"/> 收，先清了它会滞留一帧。
    /// </summary>
    private static void CommitBand(Screen screen)
    {
        if (_bandTo is not { } to) return;
        var band = CaptureGeometry.Normalize(_bandFrom.X, _bandFrom.Y, to.X, to.Y);
        var picked = new List<Annotation>();
        var marks = screen.Ink.Marks;
        for (var i = 0; i < marks.Count; i++)
            if (CaptureGeometry.Overlaps(band, marks[i].Bounds())) picked.Add(marks[i]);
        if (picked.Count == 0)
        {
            DropSelection();
            return;
        }
        var bandPrint = BandFootprint();
        _bandTo = null;
        screen.Dirty.Add(bandPrint);
        SetSelection(screen, picked);
        Flush(screen);
    }

    /// <summary>
    /// 换一组选中：<b>先收旧的</b>（旧屏的框要擦、拖动偏移要复原），再记新组并缓存
    /// 每条的包围盒——<see cref="Annotation.Bounds"/> 对文字标注走 GDI 实测，逐帧现算养不起。
    /// 提交由调用方负责。
    /// </summary>
    private static void SetSelection(Screen screen, IReadOnlyList<Annotation> marks)
    {
        DropSelection();
        _selected = marks;
        _selectScreen = screen;
        _selectionBoxes = marks.Select(m => m.Bounds()).ToList();
        screen.Dirty.Add(SelectionFootprint());
    }

    /// <summary>
    /// 收掉选中那一套：框、橡皮筋、拖动偏移全部归位，各自占过的地方交回脏区。
    /// 两块屏（旧选中在新屏、橡皮筋在旧屏）各提交各的。拖动中被打断（Esc、历史操作）
    /// 也走这里，所以 <see cref="_press"/> 的「选择」两态一并复位——
    /// 之后迟到的移动空转、迟到的那次松手无害。
    /// </summary>
    private static void DropSelection()
    {
        var boxScreen = _selectScreen;
        var boxPrint = _selected is { Count: > 0 } ? SelectionFootprint() : default;
        var hadOffset = _selectOffset.X != 0 || _selectOffset.Y != 0;
        var bandScreen = _bandTo is not null ? _pressScreen : null;
        var bandPrint = _bandTo is not null ? BandFootprint() : default;
        _selected = null;
        _selectionBoxes = null;
        _selectScreen = null;
        _selectOrigin = default;
        _selectOffset = default;
        _bandFrom = default;
        _bandTo = null;
        if (_press is Press.SelectMove or Press.SelectBand) _press = Press.None;
        if (boxScreen is not null)
        {
            if (!boxPrint.IsEmpty) boxScreen.Dirty.Add(boxPrint);
            if (hadOffset) Recomposite(boxScreen);     // 拖动烤进持久层的偏移复原回原位
            Flush(boxScreen);
        }
        if (bandScreen is not null && bandScreen != boxScreen)
        {
            if (!bandPrint.IsEmpty) bandScreen.Dirty.Add(bandPrint);
            Flush(bandScreen);
        }
    }

    /// <summary>选中的那几条此刻占的屏上区域（含偏移、含框的呼吸边；原点允许为负——拖出屏也是"占着"）。</summary>
    private static IntRect SelectionFootprint()
    {
        if (_selectionBoxes is not { } boxes) return default;
        var union = default(IntRect);
        for (var i = 0; i < boxes.Count; i++)
        {
            var moved = new IntRect(boxes[i].X + _selectOffset.X, boxes[i].Y + _selectOffset.Y,
                boxes[i].Width, boxes[i].Height);
            union = RawUnion(union, moved);
        }
        if (union.IsEmpty) return default;
        return new IntRect(union.X - ChromeExpand, union.Y - ChromeExpand,
            union.Width + ChromeExpand * 2, union.Height + ChromeExpand * 2);
    }

    /// <summary>橡皮筋此刻占的屏上区域（含呼吸边）。</summary>
    private static IntRect BandFootprint()
    {
        if (_bandTo is not { } to) return default;
        var band = CaptureGeometry.Normalize(_bandFrom.X, _bandFrom.Y, to.X, to.Y);
        return new IntRect(band.X - ChromeExpand, band.Y - ChromeExpand,
            band.Width + ChromeExpand * 2, band.Height + ChromeExpand * 2);
    }

    /// <summary>
    /// 未夹取并（原点允许为负）：<see cref="CanvasCompositor.Union"/> 会把矩形夹进缓冲，
    /// 拖到屏外的那一段在那里会被裁掉——脏区得先在"模型的世界"里并完整，夹不夹是提交那一步的事。
    /// </summary>
    private static IntRect RawUnion(IntRect a, IntRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new IntRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    /// <summary>
    /// 选中框与橡皮筋：画在提交缓冲上（持久层之上、光晕之前）。
    /// <b>每次提交都画</b>：提交那一块先被 CopyRect 从持久层铺回来，框上的像素随之被冲掉，
    /// 不补画的话，外来脏区（光晕、荧光段）扫过框就会啃掉一块。
    /// </summary>
    private static void PaintSelectionChrome(Screen screen, IntRect rect)
    {
        if (screen == _selectScreen && _selected is { Count: > 0 } && _selectionBoxes is { } boxes)
            for (var i = 0; i < boxes.Count; i++)
                ChromeRect(screen.Window.Pixels, screen.Window.Width, screen.Window.Height,
                    new IntRect(boxes[i].X + _selectOffset.X - 1, boxes[i].Y + _selectOffset.Y - 1,
                        boxes[i].Width + 2, boxes[i].Height + 2), rect);
        if (screen == _pressScreen && _bandTo is { } to)
            ChromeRect(screen.Window.Pixels, screen.Window.Width, screen.Window.Height,
                CaptureGeometry.Normalize(_bandFrom.X, _bandFrom.Y, to.X, to.Y), rect);
    }

    /// <summary>
    /// 画一个空心矩形（1 像素边）。<b>四条边各自与 <paramref name="clip"/> 求交</b>，
    /// 不是先把矩形与 clip 求交、再画交出来的那四条边：clip 是"这一帧重铺过的区域"，
    /// 它常常只盖住框的中间——对交出来的矩形画边会在框内部画出一条假线，
    /// 而真正该补的那段边反而漏画。
    /// </summary>
    private static void ChromeRect(uint[] pixels, int width, int height, IntRect box, IntRect clip)
    {
        var xFrom = Math.Max(box.X, clip.X);
        var yFrom = Math.Max(box.Y, clip.Y);
        var xTo = Math.Min(box.Right, clip.Right);
        var yTo = Math.Min(box.Bottom, clip.Bottom);
        if (xTo <= xFrom || yTo <= yFrom) return;
        for (var side = 0; side < 2; side++)
        {
            var y = side == 0 ? box.Y : box.Bottom - 1;
            if (y < yFrom || y >= yTo || y < 0 || y >= height) continue;
            for (var x = xFrom; x < xTo; x++) pixels[y * width + x] = SelectionBlue;
        }
        for (var side = 0; side < 2; side++)
        {
            var x = side == 0 ? box.X : box.Right - 1;
            if (x < xFrom || x >= xTo || x < 0 || x >= width) continue;
            for (var y = yFrom; y < yTo; y++) pixels[y * width + x] = SelectionBlue;
        }
    }
}
