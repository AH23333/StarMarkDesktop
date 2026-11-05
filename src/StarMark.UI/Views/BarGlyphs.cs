#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace StarMark.UI.Views;

/// <summary>
/// 两条工具条共用的<b>矢量图元与同一族形状</b>（批次 WS）。
/// <para>画布那条（<see cref="CanvasToolbarWindow"/>）与截图/贴图那条（<c>CaptureOverlayWindow.ToolBar</c>）
/// 摆的是同一族图形，从前各写一份 <c>Icon/Seg/Curve/Out/Ring/Fill</c> 与各五个形状的坐标。
/// 两份数字一开始相同，但从"各写一份"那一刻起，任何一边的调整都不会传给另一边——
/// 那就是 WM/WQ 记过的分岔：同一个工具在两条上长得不一样，用户在一边认得的记号到另一边换了画法。</para>
/// <para>这里只负责"这颗形状长什么样"，<b>墨色由调用方给</b>（两条的底色/弱化色不同，那是宿主的事，不是形状的）。</para>
/// </summary>
internal static class BarGlyphs
{
    /// <summary>一颗图标的边长。两条共用同一个数，才排得进同一排按钮。</summary>
    public const double Side = 16;

    // ────────── 图元 ──────────

    public static Canvas Icon(params UIElement[] parts)
    {
        var canvas = new Canvas { Width = Side, Height = Side };
        foreach (var part in parts) canvas.Children.Add(part);
        return canvas;
    }

    public static Line Seg(Brush ink, double x1, double y1, double x2, double y2, double thickness = 1.6)
        => new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ink, StrokeThickness = thickness };

    public static Polyline Curve(Brush ink, params (double X, double Y)[] pts)
    {
        var line = new Polyline { Stroke = ink, StrokeThickness = 1.5 };
        foreach (var p in pts) line.Points.Add(new Windows.Foundation.Point(p.X, p.Y));
        return line;
    }

    public static Rectangle Out(Brush ink, double x, double y, double w, double h, double thickness = 1.5)
        => Placed(new Rectangle { Width = w, Height = h, Stroke = ink, StrokeThickness = thickness }, x, y);

    public static Ellipse Ring(Brush ink, double x, double y, double w, double h, double thickness = 1.5)
        => Placed(new Ellipse { Width = w, Height = h, Stroke = ink, StrokeThickness = thickness }, x, y);

    public static Rectangle Fill(Brush ink, double x, double y, double w, double h)
        => Placed(new Rectangle { Width = w, Height = h, Fill = ink }, x, y);

    public static T Placed<T>(T part, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(part, x);
        Canvas.SetTop(part, y);
        return part;
    }

    // ────────── 同一族形状：两条上必须一模一样 ──────────

    public static UIElement RectangleGlyph(Brush ink) => Icon(Out(ink, 2.5, 4, 11, 8));                     // 空心方框

    public static UIElement EllipseGlyph(Brush ink) => Icon(Ring(ink, 2.5, 4, 11, 8));                     // 空心椭圆

    public static UIElement LineGlyph(Brush ink) => Icon(Seg(ink, 3, 13, 13, 3));                          // 就是一条斜线，没头没尾

    /// <summary>两段折 + 顶点小方块：一眼看得出"这是点出来的多段线"，不是一条直线（顶点才是两者的分界）。</summary>
    public static UIElement PolyLineGlyph(Brush ink) => Icon(
        Curve(ink, (2.5, 13), (7, 5.5), (13.5, 9.5)), Fill(ink, 5.6, 4.1, 2.8, 2.8), Fill(ink, 12.1, 8.1, 2.8, 2.8));

    public static UIElement ArrowGlyph(Brush ink) => Icon(                                                // 斜线 + 终点一个开口头
        Seg(ink, 3, 13, 12, 4), Seg(ink, 12, 4, 7.6, 4.4), Seg(ink, 12, 4, 11.6, 8.4));

    // ────────── 同一族动作记号：两条上必须一模一样（P-288 工具条图标化收编） ──────────
    // 从前只有五个形状共库，两条上其余的记号各画各的——同族分岔的账批次 WS 已经算过一次，
    // 这里把「两条都摆的那颗动作」一并收进来。墨色仍由调用方给（两条的底色不同，是宿主的事）；
    // 唯一例外见 <see cref="HighlighterGlyph"/>：荧光笔身的半透是那支笔的身份，不是哪条底色的事。

    /// <summary>「选择」：一支带尾巴的光标箭头。与 <see cref="ArrowGlyph"/>（一条斜线＋开口头）刻意不同形：
    /// 那颗是"画一根箭头"，这颗是"我指东西"——条上没有文字，撞了形就等于摆两颗猜不出作用的按钮。</summary>
    public static UIElement SelectGlyph(Brush ink) => Icon(Placed(new Polygon
    {
        Fill = ink,
        Points = new PointCollection { new(2.5, 1.5), new(2.5, 12.5), new(5.3, 9.9),
            new(7.4, 14.2), new(9.4, 13.2), new(7.2, 9), new(11, 9) },
    }, 0, 0));

    /// <summary>画笔：一条甩开起落笔的曲线。</summary>
    public static UIElement PenGlyph(Brush ink) => Icon(
        Curve(ink, (2.5, 13), (5.5, 6.5), (8.5, 10.5), (13.5, 2.5)));

    /// <summary>荧光笔：粗而半透明的一横。笔身固定是 0x88 半透明白——那半透是这支笔的画法（叠上去发亮），
    /// 不是哪条底色的事；只有底线交给调用方的弱墨。</summary>
    public static UIElement HighlighterGlyph(Brush inkDim) => Icon(
        Fill(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), 2.5, 6.5, 11, 5.5),
        Seg(inkDim, 2.5, 13.5, 13.5, 13.5, 1.2));

    /// <summary>橡皮工具：倾斜的橡皮块＋中间一道分界线。</summary>
    public static UIElement EraserGlyph(Brush ink) => Icon(
        Seg(ink, 5, 5, 11, 5), Seg(ink, 11, 5, 12.5, 11), Seg(ink, 12.5, 11, 6.5, 11),
        Seg(ink, 6.5, 11, 5, 5), Seg(ink, 5.7, 8, 11.8, 8));

    /// <summary>「清空」：一块橡皮擦过、留下一道底线。与 <see cref="EraserGlyph"/> 刻意不同形
    /// （一个是"这支笔"，一个是"这个动作"），撞了形就是两颗猜不出作用的按钮。</summary>
    public static UIElement ClearGlyph(Brush inkDim) => Icon(Placed(new Polygon
    {
        Fill = inkDim,
        Points = new PointCollection { new(3, 12), new(9, 12), new(13.5, 4), new(7.5, 3) },
    }, 0, 0), Seg(inkDim, 3, 13.5, 13.5, 13.5, 1.2));

    /// <summary>撤销 / 重做：同一支箭头只差方向（形状不一样就会被看成两个不同的动作）。</summary>
    public static UIElement UndoGlyph(Brush ink, bool left)
    {
        double X(double v) => left ? v : Side - v;
        return Icon(Seg(ink, X(13), 8, X(3.5), 8),
            Seg(ink, X(3.5), 8, X(7.5), 4.2), Seg(ink, X(3.5), 8, X(7.5), 11.8));
    }

    /// <summary>「穿透」：一个方框被一支箭头穿过——"鼠标会从它身上走过去"这件事得看得出来。</summary>
    public static UIElement ThroughGlyph(Brush ink) => Icon(
        Ring(ink, 4.5, 3, 8.5, 9, 1.4), Seg(ink, 1.5, 14, 14.5, 1.5, 1.8),
        Seg(ink, 10.5, 1.5, 14.5, 1.5, 1.8), Seg(ink, 14.5, 1.5, 14.5, 5.5, 1.8));

    /// <summary>「复制」：两个描边方框错位。</summary>
    public static UIElement CopyGlyph(Brush ink, Brush inkDim) => Icon(
        Out(inkDim, 2, 2.5, 8, 9, 1.3), Out(ink, 6, 5, 8, 9, 1.3));

    /// <summary>「存图」：一支向下的箭头落进一道托底。</summary>
    public static UIElement SaveGlyph(Brush ink) => Icon(
        Seg(ink, 8, 1.5, 8, 9.5), Seg(ink, 8, 9.5, 4.8, 6.3),
        Seg(ink, 8, 9.5, 11.2, 6.3), Seg(ink, 2.5, 13, 13.5, 13));

    /// <summary>「贴图」：一枚图钉。</summary>
    public static UIElement PinGlyph(Brush ink) => Icon(
        Ring(ink, 4.5, 2, 7, 5.5), Seg(ink, 8, 7.5, 8, 13.5),
        Seg(ink, 5, 13.5, 11, 13.5));

    /// <summary>「✕」：两道交叉。</summary>
    public static UIElement CrossGlyph(Brush ink) => Icon(
        Seg(ink, 3.5, 3.5, 12.5, 12.5), Seg(ink, 12.5, 3.5, 3.5, 12.5));

    /// <summary>「放上画板」：一块边框（那扇窗）里一道折线（接着要画的东西）。</summary>
    public static UIElement BoardGlyph(Brush ink, Brush inkDim) => Icon(
        Out(ink, 1.5, 3, 13, 9.5, 1.3),
        Seg(inkDim, 4, 9.5, 6.8, 6, 1.4), Seg(inkDim, 6.8, 6, 9.6, 8.5, 1.4),
        Seg(inkDim, 9.6, 8.5, 11.8, 5.5, 1.4));
}
