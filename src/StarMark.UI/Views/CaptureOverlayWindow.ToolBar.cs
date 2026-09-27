#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

namespace StarMark.UI.Views;

/// <summary>
/// 工具条与它的矢量图标：按钮、颜色、粗细一律按 Core 的模型生成。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 工具条（按钮、图标、颜色、粗细一律按 Core 的模型生成）──────────────────

    /// <summary>图标边长与按钮尺寸（DIP）。整条上每个按钮都由这几个数决定：
    /// 加一种画法就多一颗点或浮层里多一项，而不是把条撑成第二行（浮层多长一行就盖住用户正要标的东西）。</summary>
    private const double IconSide = 16;
    private const double ButtonWidth = 27;
    private const double ButtonHeight = 23;

    /// <summary>图标用的笔色。<b>写死在这里而不是交给主题</b>：条底是一条固定的暗色，
    /// 跟着系统主题走会在浅色模式下把按钮刷成白底，图标就糊在底上了。</summary>
    private static readonly Brush Ink = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush InkDim = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarNormal = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarChecked = new SolidColorBrush(Color.FromArgb(0x66, 0x4C, 0xA0, 0xFF));

    /// <summary>浮层里圆点用的实心白（<see cref="Ink"/> 是画笔，带浓度，点出来会像"没选中"）。</summary>
    private static readonly int SolidWhite = Annotation.Opaque(0xFF, 0xFF, 0xFF);

    private Button _shapeButton = null!;
    private readonly List<Button> _brushButtons = new();
    private Button _brushButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _clearButton = null!;
    private Button _copyButton = null!;
    private bool _pickerOpen;

    /// <summary>正在点的折线（顶点＝选区内物理像素）；null＝没有正在画的折线。</summary>
    private List<PixelPoint>? _polyLine;
    private PixelPoint _hoverLocal;                 // 折线的"橡皮筋"拖到哪儿

    /// <summary>
    /// 生成整条工具条：一颗「图形」（矩形/椭圆/直线/折线/箭头收在同一层里，见
    /// <see cref="AnnotationTools.Shapes"/>）+ 四颗各占一位的笔（画笔/荧光/打码/文字）
    /// +「当前这支笔」+ 撤销/重做/清空 + 四个动作。条上只有图标，说明写在下面那一行
    /// （悬停哪颗就出现哪颗的说明——批次 WI：贴图态条子住独立小窗，ToolTip 那种弹出层会被钉在那扇窗的边界内）。
    /// <para>真机反馈两轮把它推到了这个形状：先嫌"带文字的下拉太大"（于是全上图标），
    /// 再嫌"图形一种占一颗太铺开"（于是图形收进一个选择栏，新增折线也不再撑长条）。</para>
    /// <para>颜色与粗细收在「笔」那颗点开的那一栏里（就在图标下面一行）：
    /// 藏起来的是选择，不是状态——条上那颗点始终看得出当前颜色与粗细。</para>
    /// </summary>
    private void BuildToolBar()
    {
        _shapeButton = IconButton(ToolIcon(AnnotationTool.Rectangle), ShapeButtonText());
        _shapeButton.Click += (_, _) => ShowShapePicker(_shapeButton);
        BarRow.Children.Add(_shapeButton);

        foreach (AnnotationTool tool in AnnotationTools.Brushes)
        {
            var button = IconButton(ToolIcon(tool),
                $"{Annotation.ToolName(tool)}：{Annotation.ToolHint(tool)}（点开换颜色和粗细；Esc 收笔）");
            button.Tag = tool;
            button.Click += BrushTool_Click;
            button.RightTapped += BrushTool_RightTapped;   // 换颜色/粗细从"再点一次"挪到这里（点按那条按用户要求让给"取消选中"）
            _brushButtons.Add(button);
            BarRow.Children.Add(button);
        }

        _brushButton = IconButton(BrushIcon(), "当前这支笔：点开换颜色和粗细");
        _brushButton.Click += (_, _) => ToggleBrushPicker(_brushButton);
        BarRow.Children.Add(_brushButton);

        BarRow.Children.Add(Separator());
        _undoButton = IconButton(ArrowIcon(left: true), "撤销上一条（Ctrl+Z）");
        _undoButton.Click += Undo_Click;
        _redoButton = IconButton(ArrowIcon(left: false), "重做上一条（Ctrl+Y）");
        _redoButton.Click += Redo_Click;
        _clearButton = IconButton(EraserIcon(), "清空全部标注（还能撤销回来）");
        _clearButton.Click += Clear_Click;
        BarRow.Children.Add(_undoButton);
        BarRow.Children.Add(_redoButton);
        BarRow.Children.Add(_clearButton);

        BarRow.Children.Add(Separator());
        _copyButton = IconButton(CopyIcon(), "把带标注的画面复制进剪贴板（Enter）");
        _copyButton.Click += Copy_Click;
        var save = IconButton(SaveIcon(), "存成 PNG（Ctrl+S）");
        save.Click += Save_Click;
        // 贴图态不再显示"再钉一张"这颗：这张本来就钉着，多一颗只会让人猜那份去哪了。
        if (!_pinned)
        {
            var pin = IconButton(PinIcon(), "钉在桌面上（同 F3），带上刚画的标注");
            pin.Click += Pin_Click;
            BarRow.Children.Add(pin);
        }
        var ocr = IconButton(OcrIcon(), "认出这块画面的文字并复制");
        ocr.Click += Ocr_Click;
        BarRow.Children.Add(_copyButton);
        BarRow.Children.Add(save);
        BarRow.Children.Add(ocr);
        if (_pinned)
        {
            // 这颗按下去动的是<b>所有</b>贴图（名册的既定口径：收不到鼠标的窗只能靠全局通道救回来），
            // 说明写错范围会让人以为只有自己手上这张被点穿。
            var through = IconButton(ThroughIcon(), "鼠标穿透：让所有贴图都不再收鼠标（再用这一颗或按 F5 恢复）");
            through.Click += Through_Click;
            BarRow.Children.Add(through);
        }
        var cancel = IconButton(CrossIcon(), _pinned ? "关闭这张（Esc）" : "结束这一屏（Esc）");
        cancel.Click += Cancel_Click;
        BarRow.Children.Add(cancel);

        _undoButton.IsEnabled = _redoButton.IsEnabled = _clearButton.IsEnabled = false;
        SyncTools();
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
        Margin = new Thickness(3, 2, 3, 2),
    };

    private Button IconButton(UIElement icon, string tip)
    {
        var button = new Button
        {
            Content = icon,
            Width = ButtonWidth,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = BarNormal,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tip);
        // 悬停说明<b>不写进条子</b>（用户裁决）：那一行几百像素宽，而窗宽＝内容宽，
        // 悬停哪颗整条就变宽、左边缘跟着跑——贴图态那颗居中的条子会来回跳。
        // 文案仍然只有一份，就是上面 ToolTip 那一句（选区阶段那扇全屏窗里它本来就显示得下）。
        return button;
    }

    private void BrushTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AnnotationTool tool } button) return;
        // 用户裁决（批次 WD-6，推翻批次 PV 那版"收笔交给 Esc"）：<b>再点当前这支笔＝收笔</b>，
        // 回到"改框 / 改已画内容的位置大小"那一态。画完想立刻挪动它，人在鼠标上，不该逼他去摸键盘。
        // 浮层只在"选中那一下"弹：再点这一下是收笔，把颜色/粗细浮层又摆回来等于否认他刚做的选择。
        if (tool == _tool)
        {
            SetTool(null);
            HidePicker();
            return;
        }
        SetTool(tool);
        ShowBrushPicker(button);
    }

    /// <summary>右键当前已选中的那支笔＝换颜色和粗细（这颗按钮本来就是"笔"，浮层留在它身上最省事）。</summary>
    private void BrushTool_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not Button { Tag: AnnotationTool tool } button || tool != _tool) return;
        ToggleBrushPicker(button);
        e.Handled = true;
    }

    /// <summary>换工具。<b>所有换工具的入口都必须走这里</b>：先落笔（正在打的那行字、正在点的折线），
    /// 否则"换了个工具，刚画的东西凭空消失"。传 null＝取消选中（回到改框那一态）。</summary>
    private void SetTool(AnnotationTool? tool)
    {
        EndTextEditing(commit: true);
        FinishPolyLine(commit: true);
        _tool = tool;
        // 收笔那一按必须连那一栏一起收掉：以前 Esc 之后 Flyout 会"点到外面自己没"，
        // 现在那一栏是条子的一部分，不显式收就会一直占着第二行。
        if (tool is null) HidePicker();
        SyncTools();
        ApplyCursor();
    }

    /// <summary>
    /// 光标跟着"这一按是画还是改框"走：<b>没选工具＝十字箭头</b>（用户要的"进入拖动模式"的可见信号），
    /// 选了工具＝十字准线（要落笔的地方得看得清）。没有这一步，用户只能靠点一下才知道自己在哪一态。
    /// 悬停中的细形状（框内/边/角/框外）由 <see cref="UpdateHoverCursor"/> 按 PointerMoved 逐帧给。
    /// </summary>
    private void ApplyCursor()
    {
        if (_mode != CaptureMode.Toolbar) return;
        _lastCursor = null;                   // 状态变了：下一次悬停判定必须重算形状
        // 正在改哪条边就用哪条边的双向箭头；没在改：选了笔＝十字准线，一支都没选＝十字箭头（可拖框）
        InputSystemCursorShape? shape = _adjust switch
        {
            CaptureGeometry.SelectionEdge.Left or CaptureGeometry.SelectionEdge.Right => InputSystemCursorShape.SizeWestEast,
            CaptureGeometry.SelectionEdge.Top or CaptureGeometry.SelectionEdge.Bottom => InputSystemCursorShape.SizeNorthSouth,
            CaptureGeometry.SelectionEdge.TopLeft or CaptureGeometry.SelectionEdge.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
            CaptureGeometry.SelectionEdge.TopRight or CaptureGeometry.SelectionEdge.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
            CaptureGeometry.SelectionEdge.Move => InputSystemCursorShape.SizeAll,
            _ => null,
        };
        Root.Cursor = Microsoft.UI.Input.InputSystemCursor.Create(
            shape ?? (Armed ? InputSystemCursorShape.Cross : InputSystemCursorShape.SizeAll));
    }

    private string ShapeButtonText()
        => _tool is { } tool
            ? $"图形：{Annotation.ToolName(tool)}（点开换矩形 / 椭圆 / 直线 / 折线 / 箭头；Esc 收笔）"
            // 贴图态没有"框"可改：未选笔那一按是移动整张图，说明要按它真正的行为写
            : _pinned
                ? "没选工具＝移动这张图（十字箭头：按住可拖走）。点图标开始画"
                : "没选工具＝改框那一态（十字箭头：可拖动选区、可改边缘大小）。点图标开始画";

    /// <summary>把"当前用的是哪种图形、哪一支笔"画出来（图标上没有文字，只能靠底色与那颗点说）。</summary>
    private void SyncTools()
    {
        var shape = _tool is { } current && AnnotationTools.IsShapeTool(current) ? current : AnnotationTool.Rectangle;
        _shapeButton.Content = ToolIcon(shape);
        // 没选工具时图形那颗不许看起来"被选中"：底色是这条链上唯一的"我在哪一态"信号
        _shapeButton.Background = _tool is { } armed && AnnotationTools.IsShapeTool(armed) ? BarChecked : BarNormal;
        ToolTipService.SetToolTip(_shapeButton, ShapeButtonText());
        foreach (var button in _brushButtons)
            button.Background = (AnnotationTool)button.Tag! == _tool ? BarChecked : BarNormal;
        _brushButton.Content = BrushIcon();
        if (_tool is not { } tool)
        {
            ToolTipService.SetToolTip(_brushButton,
                "没选工具：这一按是改框（十字箭头）。点上面任一支笔开始画并选颜色；Esc 收笔");
            return;
        }
        var sizes = string.Join(" / ", Enumerable.Range(0, Annotation.ThicknessSteps.Length)
            .Select(index => Annotation.ThicknessFor(tool, index)));
        ToolTipService.SetToolTip(_brushButton,
            $"当前：{Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Name}"
            + $" · {Annotation.ThicknessNames[Math.Clamp(_weightIndex, 0, Annotation.ThicknessNames.Count - 1)]}"
            + $"（{Annotation.ToolName(tool)} 的细 / 中 / 粗 ≈ {sizes} 像素）。点这支笔＝换颜色和粗细；Esc 收笔");
    }

    // ────────── 图标：矢量图元画的，不用字体字形 ──────────
    // 为什么不用图标字体：缺字会显示成方块，而遮罩窗一按 Esc 就没了，"这九个字形到底长什么样"
    // 没人能在真机上一眼逐个确认。画出来的图元至少几何是自己算出来的，撞了车也能在断言里看出来。

    private static Canvas Icon(params UIElement[] parts)
    {
        var canvas = new Canvas { Width = IconSide, Height = IconSide };
        foreach (var part in parts) canvas.Children.Add(part);
        return canvas;
    }

    private static Line Seg(double x1, double y1, double x2, double y2, double thickness = 1.6, Brush? brush = null)
        => new()
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush ?? Ink,
            StrokeThickness = thickness,
        };

    private static Polyline Curve(Brush? brush, params (double X, double Y)[] pts)
    {
        var line = new Polyline { Stroke = brush ?? Ink, StrokeThickness = 1.5 };
        foreach (var p in pts) line.Points.Add(new Point(p.X, p.Y));
        return line;
    }

    private static T Placed<T>(T part, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(part, x);
        Canvas.SetTop(part, y);
        return part;
    }

    private static Rectangle Out(double x, double y, double w, double h, double thickness = 1.5, Brush? brush = null)
        => Placed(new Rectangle { Width = w, Height = h, Stroke = brush ?? Ink, StrokeThickness = thickness }, x, y);

    private static Rectangle Fill(double x, double y, double w, double h, Brush brush)
        => Placed(new Rectangle { Width = w, Height = h, Fill = brush }, x, y);

    private static Ellipse Ring(double x, double y, double w, double h, double thickness = 1.5)
        => Placed(new Ellipse { Width = w, Height = h, Stroke = Ink, StrokeThickness = thickness }, x, y);

    /// <summary>浮层里的一颗点：选中＝白圈加粗，未选中＝细灰圈（两种都要在暗底上看得出来）。</summary>
    private static UIElement DotIcon(int bgra, bool selected, double? diameter = null)
    {
        var side = diameter ?? 11d;
        return Icon(Placed(new Ellipse
        {
            Width = side,
            Height = side,
            Fill = new SolidColorBrush(ToColor(bgra)),
            Stroke = new SolidColorBrush(selected ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = selected ? 2 : 1,
        }, (IconSide - side) / 2, (IconSide - side) / 2));
    }

    /// <summary>某个工具长什么样。<b>每种工具的差异必须只看图形就分得开</b>：条上没有文字，
    /// 图标撞车就等于把两个功能摆成同一个按钮（直线与折线的差别刻意做成"一段 / 两段带顶点"）。</summary>
    private static UIElement ToolIcon(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => Icon(Out(2.5, 4, 11, 8)),                        // 空心方框
        AnnotationTool.Ellipse => Icon(Ring(2.5, 4, 11, 8)),                         // 空心椭圆
        AnnotationTool.Line => Icon(Seg(3, 13, 13, 3)),                              // 就是一条斜线，没头没尾
        // 两段折 + 顶点小方块：一眼看得出"这是点出来的多段线"，不是一条直线
        AnnotationTool.PolyLine => Icon(Curve(null, (2.5, 13), (7, 5.5), (13.5, 9.5)),
            Fill(5.6, 4.1, 2.8, 2.8, Ink), Fill(12.1, 8.1, 2.8, 2.8, Ink)),
        AnnotationTool.Arrow => Icon(Seg(3, 13, 12, 4),                              // 斜线 + 终点一个开口头
            Seg(12, 4, 7.6, 4.4), Seg(12, 4, 11.6, 8.4)),
        AnnotationTool.Pen => Icon(Curve(null, (2.5, 13), (5.5, 6.5), (8.5, 10.5), (13.5, 2.5))),
        AnnotationTool.Highlighter => Icon(Fill(2.5, 6.5, 11, 5.5,                   // 粗而半透明的一横
            new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))),
            Seg(2.5, 13.5, 13.5, 13.5, 1.2, InkDim)),
        AnnotationTool.Mosaic => Icon(Fill(2.5, 2.5, 5, 5, Ink), Fill(8, 2.5, 5, 5, InkDim),   // 2×2 格子
            Fill(2.5, 8, 5, 5, InkDim), Fill(8, 8, 5, 5, Ink)),
        // 用线段拼出的 "A"：文字工具的通用记号，且不依赖任何字体（字形缺了就是个方块）
        AnnotationTool.Text => Icon(Curve(null, (2.5, 13), (8, 2.5), (13.5, 13)), Seg(5, 9.5, 11, 9.5)),
        // 圆里一个 1：序号工具
        AnnotationTool.Number => Icon(Ring(3, 3, 10, 10, 1.6), Seg(8, 5.5, 8, 11), Seg(6.8, 6.6, 8, 5.5)),
        // 倾斜的橡皮块＋中间一道分界线
        AnnotationTool.Eraser => Icon(Seg(5, 5, 11, 5), Seg(11, 5, 12.5, 11), Seg(12.5, 11, 6.5, 11),
            Seg(6.5, 11, 5, 5), Seg(5.7, 8, 11.8, 8)),
        _ => Icon(Out(2.5, 4, 11, 8)),
    };

    /// <summary>「当前这支笔」：实心点的颜色＝正在用的颜色，点的直径＝正在用的粗细。</summary>
    private UIElement BrushIcon() => DotIcon(ColourBgra, selected: false,
        diameter: 3 + Math.Clamp(_weightIndex, 0, Annotation.ThicknessSteps.Length - 1) * 3);

    /// <summary>撤销 / 重做：同一支箭头只差方向（形状不一样就会被看成两个不同的动作）。</summary>
    private static UIElement ArrowIcon(bool left)
    {
        double X(double v) => left ? v : IconSide - v;
        return Icon(Seg(X(13), 8, X(3.5), 8), Seg(X(3.5), 8, X(7.5), 4.2), Seg(X(3.5), 8, X(7.5), 11.8));
    }

    private static UIElement EraserIcon()
        => Icon(Placed(new Polygon
        {
            Fill = InkDim,
            Points = new PointCollection { new(3, 12), new(9, 12), new(13.5, 4), new(7.5, 3) },
        }, 0, 0), Seg(3, 13.5, 13.5, 13.5, 1.2, InkDim));

    private static UIElement CrossIcon() => Icon(Seg(3.5, 3.5, 12.5, 12.5), Seg(12.5, 3.5, 3.5, 12.5));

    /// <summary>穿透那颗：一个方框被一支箭头穿过——"鼠标会从它身上走过去"这件事得看得出来。</summary>
    private static UIElement ThroughIcon() => Icon(
        Ring(4.5, 3, 8.5, 9, 1.4), Seg(1.5, 14, 14.5, 1.5, 1.8),
        Seg(10.5, 1.5, 14.5, 1.5, 1.8), Seg(14.5, 1.5, 14.5, 5.5, 1.8));

    private static UIElement CopyIcon()
        => Icon(Out(2, 2.5, 8, 9, 1.3, InkDim), Out(6, 5, 8, 9, 1.3));

    private static UIElement SaveIcon()
        => Icon(Seg(8, 1.5, 8, 9.5), Seg(8, 9.5, 4.8, 6.3), Seg(8, 9.5, 11.2, 6.3), Seg(2.5, 13, 13.5, 13));

    private static UIElement PinIcon()
        => Icon(Ring(4.5, 2, 7, 5.5), Seg(8, 7.5, 8, 13.5), Seg(5, 13.5, 11, 13.5));

    private static UIElement OcrIcon() => Icon(Ring(2.5, 2.5, 8, 8, 1.6), Seg(9.8, 9.8, 14, 14, 1.8));

    private int ColourBgra => Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Bgra;

    private int ThicknessForTool => Annotation.ThicknessFor(_strokeTool, _weightIndex);

    private static Color ToColor(int bgra) => Color.FromArgb(
        (byte)(bgra >>> 24), (byte)(bgra >> 16 & 0xFF), (byte)(bgra >> 8 & 0xFF), (byte)(bgra & 0xFF));

}
