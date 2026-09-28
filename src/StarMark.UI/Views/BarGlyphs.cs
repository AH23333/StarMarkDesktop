#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

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
}
