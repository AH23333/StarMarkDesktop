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

    /// <summary>按钮尺寸（DIP）。整条上每个按钮都由这几个数决定：加一种画法就多一颗点或浮层里多一项，
    /// 而不是把条撑成第二行（浮层多长一行就盖住用户正要标的东西）。图标自己的边长在 <see cref="BarGlyphs.Side"/>。</summary>
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
    private Button _groupButton = null!;      // 只有贴图态才建（选区态那扇窗上没有"这一张"可言）
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
            // 「组」只在这条带子上出现：能无歧义说清"哪一张"的地方就是这一张自己的条子，
            // 托盘那侧只负责按组下达命令（整组隐藏后条子跟着没了，出口必须是热键与托盘）。
            _groupButton = IconButton(GroupIcon(), GroupButtonText());
            _groupButton.Click += (_, _) => ToggleGroupPicker(_groupButton);
            BarRow.Children.Add(_groupButton);

            // 这颗按下去动的是<b>所有</b>贴图（想只动一组用旁边那颗「组」）。
            // 说明写错范围会让人以为只有自己手上这张被点穿（批次 WF-1 那族极性事故）。
            var through = IconButton(ThroughIcon(), "鼠标穿透：让所有贴图都不再收鼠标（再用这一颗或按 F5 恢复；只动一组请点「组」）");
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
        // 悬停哪颗整条就变宽、边缘跟着跑（贴图态那颗条子因此跳位置）。
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

    /// <summary>
    /// 「组」那颗的说明：把"这一张现在在不在组里、那一组里有几张"直接写在句子里。
    /// <para>条上没有文字（批次 WP 的取舍：窗宽＝内容宽，带字的说明会让整条在悬停时变宽跳位），
    /// 所以这句只能待在 ToolTip 里——而贴图态那扇条子窗放不下长句，<b>这一句是短的、且只说这一张的事实</b>。
    /// 组号与张数都从名册现取，不在界面上留第二份（并组之后 tooltip 立刻跟着变，见 <see cref="SyncTools"/>）。</para>
    /// </summary>
    private string GroupButtonText()
        => PinManager.GroupOf(this) is { } group
            ? $"已在「{group.Name}」（{group.Members.Count} 张）。点开＝换组或移出；整组动作在托盘「贴图组」"
            : "分组：把这一张单独分成一组，或并进已有的一组（整组隐藏 / 忽略鼠标 / 关闭在托盘「贴图组」里）";

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
        // 「组」那颗的说明跟着名册走：并组/移出之后不重建整条，但说明必须立刻是新的那一组
        //（留着旧的组号，用户就会以为并组没生效而再点一次，第二次会真的又建一组）。
        if (_pinned) ToolTipService.SetToolTip(_groupButton, GroupButtonText());
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
            + $"（{Annotation.ToolName(tool)} 的{Annotation.ThicknessUnit(tool)}：细 / 中 / 粗 ≈ {sizes} 像素）。点这支笔＝换颜色和粗细；Esc 收笔");
    }

    // ────────── 图标：矢量图元画的，不用字体字形 ──────────
    // 为什么不用图标字体：缺字会显示成方块，而遮罩窗一按 Esc 就没了，"这九个字形到底长什么样"
    // 没人能在真机上一眼逐个确认。画出来的图元至少几何是自己算出来的，撞了车也能在断言里看出来。
    //
    // 图元与"同一族形状"住在 BarGlyphs（批次 WS）：画布那条工具条要的是<b>同一份</b>，
    // 从前两边各写一份 Icon/Seg/Curve/Out/Ring 与各五个形状的坐标，任何一边改数字都不会传给另一边。
    // 墨色（Ink / InkDim）留在这里——那是这条带的底色，不是形状的一部分。

    /// <summary>浮层里的一颗点：选中＝白圈加粗，未选中＝细灰圈（两种都要在暗底上看得出来）。</summary>
    private static UIElement DotIcon(int bgra, bool selected, double? diameter = null)
    {
        var side = diameter ?? 11d;
        return BarGlyphs.Icon(BarGlyphs.Placed(new Ellipse
        {
            Width = side,
            Height = side,
            Fill = new SolidColorBrush(ToColor(bgra)),
            Stroke = new SolidColorBrush(selected ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = selected ? 2 : 1,
        }, (BarGlyphs.Side - side) / 2, (BarGlyphs.Side - side) / 2));
    }

    /// <summary>某个工具长什么样。<b>每种工具的差异必须只看图形就分得开</b>：条上没有文字，
    /// 图标撞车就等于把两个功能摆成同一个按钮（直线与折线的差别刻意做成"一段 / 两段带顶点"）。</summary>
    private static UIElement ToolIcon(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => BarGlyphs.RectangleGlyph(Ink),
        AnnotationTool.Ellipse => BarGlyphs.EllipseGlyph(Ink),
        AnnotationTool.Line => BarGlyphs.LineGlyph(Ink),
        AnnotationTool.PolyLine => BarGlyphs.PolyLineGlyph(Ink),
        AnnotationTool.Arrow => BarGlyphs.ArrowGlyph(Ink),
        AnnotationTool.Pen => BarGlyphs.Icon(
            BarGlyphs.Curve(Ink, (2.5, 13), (5.5, 6.5), (8.5, 10.5), (13.5, 2.5))),
        AnnotationTool.Highlighter => BarGlyphs.Icon(                             // 粗而半透明的一横
            BarGlyphs.Fill(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), 2.5, 6.5, 11, 5.5),
            BarGlyphs.Seg(InkDim, 2.5, 13.5, 13.5, 13.5, 1.2)),
        AnnotationTool.Mosaic => BarGlyphs.Icon(                                 // 2×2 格子
            BarGlyphs.Fill(Ink, 2.5, 2.5, 5, 5), BarGlyphs.Fill(InkDim, 8, 2.5, 5, 5),
            BarGlyphs.Fill(InkDim, 2.5, 8, 5, 5), BarGlyphs.Fill(Ink, 8, 8, 5, 5)),
        // 用线段拼出的 "A"：文字工具的通用记号，且不依赖任何字体（字形缺了就是个方块）
        AnnotationTool.Text => BarGlyphs.Icon(
            BarGlyphs.Curve(Ink, (2.5, 13), (8, 2.5), (13.5, 13)), BarGlyphs.Seg(Ink, 5, 9.5, 11, 9.5)),
        AnnotationTool.Number => BarGlyphs.Icon(BarGlyphs.Ring(Ink, 3, 3, 10, 10, 1.6),   // 圆里一个 1：序号工具
            BarGlyphs.Seg(Ink, 8, 5.5, 8, 11), BarGlyphs.Seg(Ink, 6.8, 6.6, 8, 5.5)),
        AnnotationTool.Eraser => BarGlyphs.Icon(                                 // 倾斜的橡皮块＋中间一道分界线
            BarGlyphs.Seg(Ink, 5, 5, 11, 5), BarGlyphs.Seg(Ink, 11, 5, 12.5, 11), BarGlyphs.Seg(Ink, 12.5, 11, 6.5, 11),
            BarGlyphs.Seg(Ink, 6.5, 11, 5, 5), BarGlyphs.Seg(Ink, 5.7, 8, 11.8, 8)),
        _ => BarGlyphs.RectangleGlyph(Ink),
    };

    /// <summary>「当前这支笔」：实心点的颜色＝正在用的颜色，点的直径＝正在用的粗细。</summary>
    private UIElement BrushIcon() => DotIcon(ColourBgra, selected: false,
        diameter: 3 + Math.Clamp(_weightIndex, 0, Annotation.ThicknessSteps.Length - 1) * 3);

    /// <summary>撤销 / 重做：同一支箭头只差方向（形状不一样就会被看成两个不同的动作）。</summary>
    private static UIElement ArrowIcon(bool left)
    {
        double X(double v) => left ? v : BarGlyphs.Side - v;
        return BarGlyphs.Icon(BarGlyphs.Seg(Ink, X(13), 8, X(3.5), 8),
            BarGlyphs.Seg(Ink, X(3.5), 8, X(7.5), 4.2), BarGlyphs.Seg(Ink, X(3.5), 8, X(7.5), 11.8));
    }

    private static UIElement EraserIcon()
        => BarGlyphs.Icon(BarGlyphs.Placed(new Polygon
        {
            Fill = InkDim,
            Points = new PointCollection { new(3, 12), new(9, 12), new(13.5, 4), new(7.5, 3) },
        }, 0, 0), BarGlyphs.Seg(InkDim, 3, 13.5, 13.5, 13.5, 1.2));

    private static UIElement CrossIcon() => BarGlyphs.Icon(
        BarGlyphs.Seg(Ink, 3.5, 3.5, 12.5, 12.5), BarGlyphs.Seg(Ink, 12.5, 3.5, 3.5, 12.5));

    /// <summary>穿透那颗：一个方框被一支箭头穿过——"鼠标会从它身上走过去"这件事得看得出来。</summary>
    /// <summary>
    /// 「组」那颗：上面两块小图、下面一道把它们拢在一起的托架。
    /// <para>刻意与「打码」（2×2 实心格）和「复制」（两个描边方框错位）都不同形——这条带子上撞了形状
    /// 就等于摆了颗用户猜不出作用的按钮（批次 WM 的图标口径：只看图形就要分得开）。</para>
    /// </summary>
    private static UIElement GroupIcon() => BarGlyphs.Icon(
        BarGlyphs.Fill(Ink, 2.5, 2.5, 4.5, 4.5), BarGlyphs.Fill(InkDim, 9, 2.5, 4.5, 4.5),
        BarGlyphs.Seg(Ink, 4.7, 8, 4.7, 10.5, 1.2), BarGlyphs.Seg(Ink, 11.2, 8, 11.2, 10.5, 1.2),
        BarGlyphs.Seg(Ink, 4.7, 10.5, 11.2, 10.5, 1.2));

    private static UIElement ThroughIcon() => BarGlyphs.Icon(
        BarGlyphs.Ring(Ink, 4.5, 3, 8.5, 9, 1.4), BarGlyphs.Seg(Ink, 1.5, 14, 14.5, 1.5, 1.8),
        BarGlyphs.Seg(Ink, 10.5, 1.5, 14.5, 1.5, 1.8), BarGlyphs.Seg(Ink, 14.5, 1.5, 14.5, 5.5, 1.8));

    private static UIElement CopyIcon()
        => BarGlyphs.Icon(BarGlyphs.Out(InkDim, 2, 2.5, 8, 9, 1.3), BarGlyphs.Out(Ink, 6, 5, 8, 9, 1.3));

    private static UIElement SaveIcon()
        => BarGlyphs.Icon(BarGlyphs.Seg(Ink, 8, 1.5, 8, 9.5), BarGlyphs.Seg(Ink, 8, 9.5, 4.8, 6.3),
            BarGlyphs.Seg(Ink, 8, 9.5, 11.2, 6.3), BarGlyphs.Seg(Ink, 2.5, 13, 13.5, 13));

    private static UIElement PinIcon()
        => BarGlyphs.Icon(BarGlyphs.Ring(Ink, 4.5, 2, 7, 5.5), BarGlyphs.Seg(Ink, 8, 7.5, 8, 13.5),
            BarGlyphs.Seg(Ink, 5, 13.5, 11, 13.5));

    private static UIElement OcrIcon() => BarGlyphs.Icon(
        BarGlyphs.Ring(Ink, 2.5, 2.5, 8, 8, 1.6), BarGlyphs.Seg(Ink, 9.8, 9.8, 14, 14, 1.8));

    private int ColourBgra => Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Bgra;

    private int ThicknessForTool => Annotation.ThicknessFor(_strokeTool, _weightIndex);

    private static Color ToColor(int bgra) => Color.FromArgb(
        (byte)(bgra >>> 24), (byte)(bgra >> 16 & 0xFF), (byte)(bgra >> 8 & 0xFF), (byte)(bgra & 0xFF));

}
