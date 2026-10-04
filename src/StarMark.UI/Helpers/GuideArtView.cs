#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Core.Guide;

namespace StarMark.UI.Helpers;

/// <summary>
/// 首启向导那几张<b>示意图</b>（批次 WJ-1）。
/// <para><b>为什么是矢量现画而不是截图</b>（用户裁决：图内一个汉字都不放）：截图会把界面文字焊死在图里——
/// 界面一改字它就过期，而且刚做的界面语言这条线一翻英文，图里那句中文就成了第二个出处（还是假话）。
/// 现画的骨架图只画"哪里有框、哪里有排、哪里有颗圆"，跟着主题换墨色，跟着尺寸缩放，不跟语言走。</para>
/// <para><b>几何只在这一处</b>（与工具条那颗 <c>BarGlyphs</c> 同一族纪律，坑表 #123 那一族）：
/// 名字表住在 Core 的 <see cref="GuideArt"/>，闸门两侧对账——某一步引了一张没画过的图，
/// 屏幕上的表现是一片空白而没有任何地方报错，所以这张对账必须有。</para>
/// </summary>
internal static class GuideArtView
{
    /// <summary>画布尺寸。向导每一行都夹在同一格里，所以六张图必须同尺寸，否则换步时内容会跳。</summary>
    public const double Side = 260;
    public const double Tall = 116;

    /// <summary>画某一张示意图。<paramref name="name"/> 取自 Core 的名字表；认不得时出声而不是留白。</summary>
    public static FrameworkElement Build(string? name, Brush ink, Brush accent)
        => name switch
        {
            GuideArt.Window => WindowFrame(ink, accent),
            GuideArt.Search => SearchFirst(ink, accent),
            GuideArt.Pages => PageRow(ink, accent),
            GuideArt.Widgets => DesktopWidgets(ink, accent),
            GuideArt.Capture => CaptureRect(ink, accent),
            GuideArt.Keys => KeyCaps(ink, accent),
            _ => Unnamed(ink, name),
        };

    // ────────── 图元（只画形状，不画字） ──────────

    private static Canvas Parts(params UIElement[] parts)
    {
        var canvas = new Canvas { Width = Side, Height = Tall };
        foreach (var part in parts) canvas.Children.Add(part);
        return canvas;
    }

    private static T At<T>(T part, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(part, x);
        Canvas.SetTop(part, y);
        return part;
    }

    private static Rectangle Box(Brush ink, double x, double y, double w, double h, double thickness = 1.4)
    {
        var box = new Rectangle { Width = w, Height = h, Stroke = ink, StrokeThickness = thickness, RadiusX = 4, RadiusY = 4 };
        return At(box, x, y);
    }

    private static Rectangle Bar(Brush ink, double x, double y, double w, double h)
        => At(new Rectangle { Width = w, Height = h, Fill = ink, RadiusX = 2, RadiusY = 2 }, x, y);

    private static Ellipse Dot(Brush ink, double x, double y, double size, double thickness = 0)
    {
        var dot = new Ellipse { Width = size, Height = size };
        if (thickness > 0) { dot.Stroke = ink; dot.StrokeThickness = thickness; } else dot.Fill = ink;
        return At(dot, x, y);
    }

    private static Line Rule(Brush ink, double x1, double y1, double x2, double y2, double thickness = 1.4)
        => new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ink, StrokeThickness = thickness };

    // ────────── 六张示意图 ──────────

    /// <summary>整扇窗的骨架：上面一条工具条、左边一列页面、右边是内容。</summary>
    private static Canvas WindowFrame(Brush ink, Brush accent) => Parts(
        Box(ink, 12, 6, 236, 104, 1.6),
        Rule(ink, 12, 30, 248, 30),
        Rule(ink, 78, 30, 78, 110),
        Bar(accent, 22, 13, 46, 9),                                  // 搜索框
        Bar(ink, 22, 42, 44, 6), Bar(ink, 22, 56, 44, 6), Bar(ink, 22, 70, 44, 6),
        Bar(ink, 90, 42, 140, 6), Bar(ink, 90, 56, 140, 6), Bar(ink, 90, 70, 110, 6));

    /// <summary>搜索框排第一：一根光标条＋下面按类型分块的结果。</summary>
    private static Canvas SearchFirst(Brush ink, Brush accent) => Parts(
        Box(accent, 14, 8, 232, 24, 1.8),
        Rule(accent, 26, 14, 26, 26, 2.2),                           // 光标
        Bar(ink, 36, 17, 74, 6),
        Box(ink, 14, 44, 110, 26), Box(ink, 136, 44, 110, 26),      // 两块结果
        Box(ink, 14, 78, 232, 26),
        Bar(ink, 24, 53, 60, 5), Bar(ink, 146, 53, 60, 5), Bar(ink, 24, 87, 120, 5));

    /// <summary>那一排页面＋右上角那颗「？」（画成一颗带点的圆，不写字）。</summary>
    private static Canvas PageRow(Brush ink, Brush accent) => Parts(
        Box(ink, 14, 10, 150, 16),
        Box(ink, 176, 10, 68, 16),
        Dot(ink, 244, 10, 16, 1.6), Dot(ink, 250, 21, 4),            // 「？」＝圆＋点
        Box(accent, 14, 40, 42, 16, 2.0),                            // 当前这一页
        Box(ink, 62, 40, 42, 16), Box(ink, 110, 40, 42, 16), Box(ink, 158, 40, 42, 16),
        Rule(ink, 14, 64, 246, 64),
        Bar(ink, 14, 74, 190, 6), Bar(ink, 14, 88, 150, 6));

    /// <summary>桌面组件：屏幕上一颗时钟卡＋一颗速记卡，角落是那排组件入口。</summary>
    private static Canvas DesktopWidgets(Brush ink, Brush accent) => Parts(
        Box(ink, 8, 6, 244, 104, 1.6),
        Box(accent, 150, 20, 62, 46, 1.8),
        Dot(ink, 160, 30, 26, 1.5), Rule(ink, 173, 43, 173, 34, 1.5), Rule(ink, 173, 43, 181, 46, 1.5),
        Bar(ink, 160, 60, 42, 5),
        Box(ink, 20, 24, 56, 40), Bar(ink, 28, 34, 40, 5), Bar(ink, 28, 44, 40, 5), Bar(ink, 28, 54, 26, 5),
        Dot(accent, 212, 82, 12), Dot(accent, 228, 82, 12));         // 组件那一排

    /// <summary>截图与画布：屏幕上的一块选区、贴着选区的工具条、一颗序号点。</summary>
    private static Canvas CaptureRect(Brush ink, Brush accent) => Parts(
        Box(ink, 8, 6, 244, 88, 1.6),
        Box(accent, 54, 20, 128, 46, 2.0),
        Box(ink, 54, 70, 96, 18),
        Dot(ink, 62, 76, 6), Dot(ink, 76, 76, 6), Dot(ink, 90, 76, 6), Dot(ink, 104, 76, 6),
        Dot(accent, 176, 14, 14, 1.8),
        Rule(ink, 14, 102, 120, 102), Rule(ink, 14, 102, 14, 94));   // 画布那一线

    /// <summary>按键：三颗键帽加一条长键（高亮那颗＝可改的那一颗）。</summary>
    private static Canvas KeyCaps(Brush ink, Brush accent) => Parts(
        Box(ink, 20, 20, 54, 40), Box(accent, 90, 20, 54, 40, 2.0), Box(ink, 160, 20, 54, 40),
        Bar(ink, 32, 34, 30, 4), Bar(accent, 102, 34, 30, 4), Bar(ink, 172, 34, 30, 4),
        Box(ink, 20, 74, 194, 24), Bar(ink, 84, 83, 66, 5));

    /// <summary>
    /// 认不得的名字：<b>画一个空框并出声</b>。
    /// <para>这一格正常走不到（Core 的名字表与这里的 switch 由闸门两侧对账），但"走不到"不等于"可以静默"：
    /// 一片空白在屏幕上读起来像"引导坏了"，而日志里那一句能直接指到写错的那个名字（坑表 #271 同族）。</para>
    /// </summary>
    private static Canvas Unnamed(Brush ink, string? name)
    {
        StarLog.Warn($"首启向导引用了一张没画过的示意图：{name}（那一步会只有一个空框）");
        return Parts(Box(ink, 8, 6, 244, 104, 1.6));
    }
}
