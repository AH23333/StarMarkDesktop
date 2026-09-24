#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Capture;

/// <summary>截图上能画的几种东西。<b>顺序就是选择栏里从左到右的顺序</b>
/// （"哪些算图形、哪些是笔"另见 <see cref="IsShapeTool"/>，条上摆哪一颗由那两个判据说，不在界面里写死）。</summary>
public enum AnnotationTool
{
    Rectangle,
    Ellipse,
    Line,
    /// <summary>折线：点几下钉几个顶点，Enter 或双击收口。直线只能一段，
    /// 而"沿着一条边界描一圈"是截图标注里最常见的一种指示（真机反馈点名缺它）。</summary>
    PolyLine,
    Arrow,
    Pen,
    Highlighter,
    Mosaic,
    Text,
}

/// <summary>工具的分组。<b>这条判据住在模型里</b>：工具条上"哪几个收进一个选择栏、哪几个各占一颗"
/// 与折线该按几个点收尾都从它推；界面里各写一遍就会长成"加了工具而条上没有"那种只有跑起来才看得见的错。</summary>
public static class AnnotationTools
{
    /// <summary>"图形"那一组：都是<b>框出一段几何</b>的东西，收进同一颗按钮的选择栏里。</summary>
    public static readonly AnnotationTool[] Shapes =
    {
        AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Line,
        AnnotationTool.PolyLine, AnnotationTool.Arrow,
    };

    /// <summary>"笔"那一组：直接按在画布上走，各占一颗按钮（画法彼此差得远，收进浮层反而多一次点击）。</summary>
    public static readonly AnnotationTool[] Brushes =
    {
        AnnotationTool.Pen, AnnotationTool.Highlighter, AnnotationTool.Mosaic, AnnotationTool.Text,
    };

    public static bool IsShapeTool(AnnotationTool tool) => Array.IndexOf(Shapes, tool) >= 0;
}

/// <summary>工具条上给用户挑的一个颜色。</summary>
/// <param name="Bgra">按 <c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c> 摆好的整数——像素缓冲就是这个字节序，
/// 中间再来一次「颜色对象→字节」的换算就是多一处会写反的地方。</param>
public readonly record struct AnnotationColor(string Name, int Bgra);

/// <summary>
/// 一条标注。
/// <para>
/// <b>坐标系是「选区内的物理像素」，原点在选区左上角</b>：与最终要交出去的那份 BGRA 缓冲完全同一个坐标系。
/// 屏幕坐标或 DIP 都不进模型——那样每加一个落点（复制／存图／贴图／识字）就要重算一次换算，
/// 而「预览对、存出来偏了」这类 bug 恰恰都长在多次换算上。
/// </para>
/// <para>
/// 一条标注是<b>画完即定形</b>的（点、颜色、粗细都在创建时给定），所以「撤销」就是弹掉列表末尾，
/// 不需要任何反向操作。<b>之后可以整条移动 / 缩放 / 旋转</b>（批次 RE-3）：这三样改的都是
/// <see cref="Pivot"/>/<see cref="Rotation"/>/<see cref="Scale"/> 三个变换量，<b>不改点集本身</b>——
/// 于是"撤销一条变换"与"撤销一条标注"走的是同一条快照路径，同样不需要反向运算。
/// 逐顶点改形（拖一个角把矩形拉歪）仍然不做：那要的是每类工具各自的顶点语义。
/// </para>
/// </summary>
public sealed record Annotation(
    AnnotationTool Tool,
    IReadOnlyList<PixelPoint> Points,
    int ColorBgra,
    int Thickness)
{
    /// <summary>文字内容（只有 <see cref="AnnotationTool.Text"/> 用得上）。刻意做成 init 属性而不是
    /// 构造参数：位置参数的默认值必须能在类型体外面写死，而 <see cref="DefaultFontHeight"/> 是这个类型自己的数。</summary>
    public string? Text { get; init; }

    /// <summary>
    /// 变换的轴点（<b>没显式指定＝第一个点</b>，见 <see cref="Origin"/>）。
    /// 单独存一个可空值而不是直接写死 Points[0]：拖动改位置时点集整体平移，
    /// 轴点跟着走才转得对；而"绕自己转"是用户的直觉，绕画布原点转会把形状甩出视野。
    /// </summary>
    public PixelPoint? Pivot { get; init; }

    /// <summary>绕 <see cref="Origin"/> 旋转的角度（度，顺时针）。0＝不转。</summary>
    public double Rotation { get; init; }

    /// <summary>绕 <see cref="Origin"/> 的等比缩放倍数（1＝原样）。文字靠它改字号。</summary>
    public double Scale { get; init; } = 1d;

    /// <summary>变换轴点：没指定就是第一个点。</summary>
    public PixelPoint Origin => Pivot ?? (Points.Count > 0 ? Points[0] : default);

    /// <summary>缩放上下限。缩到 0 附近会"再也点不中"，放大到几百倍会直接把画布撑爆——两端都要有墙。</summary>
    public const double MinScale = 0.2;
    public const double MaxScale = 6d;

    /// <summary>有没有带变换。没变换时 <see cref="TransformedPoints"/> 直接给原列表，不复制。</summary>
    public bool HasTransform => Rotation != 0 || Scale != 1d;

    /// <summary>
    /// 画出去的那一组点：把 <see cref="Rotation"/> 与 <see cref="Scale"/> 绕 <see cref="Origin"/> 作用上去。
    /// <para>放在模型里而不是绘制里，是因为<b>选择框、命中测试、四个落点的渲染都要用同一组点</b>——
    /// 各算一遍就会出现"选择框框住的是原位置，字已经转走了"。</para>
    /// </summary>
    public IReadOnlyList<PixelPoint> TransformedPoints()
    {
        if (!HasTransform) return Points;
        var pivot = Origin;
        var radians = Rotation * Math.PI / 180d;
        var cos = Math.Cos(radians) * Scale;
        var sin = Math.Sin(radians) * Scale;
        var moved = new List<PixelPoint>(Points.Count);
        foreach (var p in Points)
        {
            var dx = p.X - pivot.X;
            var dy = p.Y - pivot.Y;
            moved.Add(new PixelPoint(
                pivot.X + (int)Math.Round(dx * cos - dy * sin, MidpointRounding.AwayFromZero),
                pivot.Y + (int)Math.Round(dx * sin + dy * cos, MidpointRounding.AwayFromZero)));
        }
        return moved;
    }

    /// <summary>文字实际字高（缩放之后）。夹回 <see cref="Problem"/> 认的那个区间，免得画出去才发现越界。</summary>
    public int DrawFontHeight => Math.Clamp((int)Math.Round(FontHeight * Scale, MidpointRounding.AwayFromZero), 6, 200);

    /// <summary>整条平移（＝拖动位置）：点与轴点一起走，角度与倍数不变。</summary>
    public Annotation MovedBy(int dx, int dy) => this with
    {
        Points = Points.Select(p => new PixelPoint(p.X + dx, p.Y + dy)).ToList(),
        Pivot = Pivot is { } pivot ? new PixelPoint(pivot.X + dx, pivot.Y + dy) : null,
    };

    /// <summary>绕轴点缩放（倍数夹在上下限之间）。文字改的是字号，几何改的是点距。</summary>
    public Annotation ScaledBy(double factor) => this with { Scale = Math.Clamp(Scale * factor, MinScale, MaxScale) };

    /// <summary>绕轴点转过 <paramref name="angle"/> 度（归一到 0–360，负角与超过一圈都不该改变形状）。</summary>
    public Annotation RotatedBy(double angle) => this with { Rotation = NormalizeAngle(Rotation + angle) };

    /// <summary>角度归一：0 ≤ a &lt; 360。转两圈与转一圈半是同一件事，不归一会让撤销栈里出现"看着不同其实一样"的两条。</summary>
    public static double NormalizeAngle(double degrees)
    {
        var wrapped = degrees % 360d;
        return wrapped < 0 ? wrapped + 360d : wrapped;
    }

    /// <summary>
    /// 这一条能不能转。<b>文字不行</b>：GDI 那条路是"把字写进一块与文字框同样大小的临时画布再搬回去"，
    /// 转起来要换世界变换矩阵、还要重算被裁切的包围盒，风险全在像素层而这里断言不到。
    /// 所以界面对文字只给"移动 + 缩放"两颗把手——<b>把手不给，比给了不生效诚实</b>（P-97 记着这条取舍）。
    /// </summary>
    public bool SupportsRotation => Tool != AnnotationTool.Text;

    /// <summary>字高（物理像素）。文字标注不看 <see cref="Thickness"/>——字号就是它的粗细。</summary>
    public int FontHeight { get; init; } = DefaultFontHeight;

    /// <summary>
    /// 粗细下限与上限（物理像素）。<b>上限由最宽的笔刷决定</b>（马赛克的粗档），而不是由线条决定：
    /// 线条有三档就够，而涂敏感信息的笔刷太窄会留缝。
    /// </summary>
    public const int MinThickness = 1;
    public const int MaxThickness = 48;

    /// <summary>三档线宽：细/中/粗。给的是「点一下就能选到」的档位，不是滑杆（截图标注没人微调）。</summary>
    public static readonly int[] ThicknessSteps = { 2, 4, 8 };

    /// <summary>三档的名字，与 <see cref="ThicknessSteps"/> 同序（条数不一致就是给错了标签，有测试钉着）。</summary>
    public static IReadOnlyList<string> ThicknessNames { get; } = new[] { "细", "中", "粗" };

    /// <summary>
    /// 第 <paramref name="stepIndex"/> 档在某个工具上实际是多少像素粗。
    /// <para>
    /// 荧光笔与马赛克<b>按比例放大</b>而不是共用 2/4/8：它们的「粗细」是笔刷直径，
    /// 拿 2–8 像素去涂敏感信息会出现一条条漏缝——那是安全问题，不是难看。
    /// </para>
    /// </summary>
    public static int ThicknessFor(AnnotationTool tool, int stepIndex)
    {
        var step = ThicknessSteps[Math.Clamp(stepIndex, 0, ThicknessSteps.Length - 1)];
        return tool switch
        {
            AnnotationTool.Highlighter => step * 4,
            AnnotationTool.Mosaic => step * 6,
            _ => step,
        };
    }

    /// <summary>切到某个工具时给用户的默认粗细（＝中间那一档）。</summary>
    public static int DefaultThickness(AnnotationTool tool) => ThicknessFor(tool, 1);

    /// <summary>文字默认高度（物理像素）。桌面正文约 14–18px，标注要<b>比被标注的字更大</b>才看得清。</summary>
    public const int DefaultFontHeight = 22;

    /// <summary>马赛克格子边长（物理像素）。再小就变成「糊成一片噪点」，反而比原图更难读。</summary>
    public const int MosaicBlockSize = 12;

    /// <summary>荧光笔的浓度：盖在字上还能看见底下的字，才算荧光笔。</summary>
    public const int HighlighterAlpha = 0x70;

    /// <summary>
    /// 出厂颜色。刻意含黑与白：截图深浅底都有，只有彩色的一组在深色截图上会整条看不见。
    /// </summary>
    public static IReadOnlyList<AnnotationColor> Palette { get; } = new[]
    {
        new AnnotationColor("红", Opaque(0x23, 0x11, 0xE8)),
        new AnnotationColor("黄", Opaque(0x00, 0xB9, 0xFF)),
        new AnnotationColor("绿", Opaque(0x3E, 0x89, 0x10)),
        new AnnotationColor("蓝", Opaque(0xD4, 0x78, 0x00)),
        new AnnotationColor("白", Opaque(0xFF, 0xFF, 0xFF)),
        new AnnotationColor("黑", Opaque(0x11, 0x11, 0x11)),
    };

    /// <summary>不透明色。<b>全仓库只有这一处「字节→BGRA 整数」的拼法</b>（绘制那边也调它），
    /// 免得出现两套位序。</summary>
    public static int Opaque(byte blue, byte green, byte red) => blue | (green << 8) | (red << 16) | unchecked(0xFF << 24);

    /// <summary>取一个 alpha 不同的同色（荧光笔要在颜色上盖浓度，而不是另配一套颜色表）。</summary>
    public static int WithAlpha(int bgra, int alpha) => (bgra & 0x00FFFFFF) | (alpha << 24);

    /// <summary>这条标注实际画出去的颜色（荧光笔强制半透明）。</summary>
    public int EffectiveColorBgra => Tool == AnnotationTool.Highlighter ? WithAlpha(ColorBgra, HighlighterAlpha) : ColorBgra;

    /// <summary>画这条标注至少要几个点；不够就是「用户点了一下还没拖」，不该画。</summary>
    public static int MinPoints(AnnotationTool tool) => tool == AnnotationTool.Text ? 1 : 2;

    /// <summary>这一类工具的形状<b>完全由「按下那一点」与「放开那一点」决定</b>（矩形/椭圆看对角，
    /// 直线/箭头看两端），拖动中途经过的采样点只是痕迹。
    /// <para>界面上据此<b>覆盖而不是追加</b>，绘制那边据此取末尾一点当另一端：两处必须同一个口径，
    /// 否则"按第二个点取端点"就会画出 1–2 像素的小框——按下后第一次鼠标移动就是它的第二个点。
    /// 真机反馈的"松手后图形变得非常小"正是这个错位：预览取的是最后一点，落笔取的是第二点。</para></summary>
    public static bool IsTwoPointTool(AnnotationTool tool)
        => tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Line or AnnotationTool.Arrow;

    /// <summary>这一条的另一端（两点点工具用）：<b>永远是最后采到的那一点</b>，不是第二个元素。</summary>
    public PixelPoint EndPoint => Points[^1];

    /// <summary>
    /// 这条标注画不出来时的原因，能画则 null。
    /// <para>
    /// 给的是<b>原因</b>而不是 bool：工具条上「点下去什么都没发生」是最难自查的一类缺陷，
    /// 而这里的判据（点数够不够、文字是不是空的、粗细在不在范围内）每一条都对应一种真实的输入。
    /// </para>
    /// </summary>
    public string? Problem()
    {
        if (Points is null || Points.Count < MinPoints(Tool))
            return $"{ToolName(Tool)}至少需要 {MinPoints(Tool)} 个点";
        if (Tool == AnnotationTool.Text && string.IsNullOrEmpty(Text))
            return "文字是空的，没有可写上去的内容";
        if (Tool != AnnotationTool.Text && (Thickness < MinThickness || Thickness > MaxThickness))
            return $"粗细 {Thickness} 不在 {MinThickness}–{MaxThickness} 之间";
        if (FontHeight is < 6 or > 200)
            return $"文字高度 {FontHeight} 太离谱（只接受 6–200 物理像素）";
        if (Scale is < MinScale or > MaxScale)
            return $"缩放倍数 {Scale:0.##} 超出 {MinScale:0.##}–{MaxScale:0.##}（缩到底会再也点不中，放到最大会撑破画面）";
        if (Tool == AnnotationTool.Text && Rotation != 0)
            return "文字不支持旋转（只能移动与改字号）";
        if (EffectiveColorBgra >>> 24 == 0)
            return "颜色是全透明的，画上去等于没画";
        return null;
    }

    /// <summary>中文名（工具条与失败提示共用；枚举名直接进提示等于让用户读代码）。</summary>
    public static string ToolName(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => "矩形",
        AnnotationTool.Ellipse => "椭圆",
        AnnotationTool.Line => "直线",
        AnnotationTool.PolyLine => "折线",
        AnnotationTool.Arrow => "箭头",
        AnnotationTool.Pen => "画笔",
        AnnotationTool.Highlighter => "荧光",
        AnnotationTool.Mosaic => "打码",
        AnnotationTool.Text => "文字",
        _ => tool.ToString(),
    };

    /// <summary>工具条上每个按钮的悬停说明：只说「这一下会发生什么」，不复述按钮名字。</summary>
    public static string ToolHint(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => "拖一个方框圈出来",
        AnnotationTool.Ellipse => "拖一个椭圆圈出来",
        AnnotationTool.Line => "拖一条直线（做指示线、划掉内容）",
        AnnotationTool.PolyLine => "点几下钉几个顶点（沿一条边界描一圈），Enter 或双击收口，Esc 丢掉这一条",
        AnnotationTool.Arrow => "从起点指到终点，箭头在终点",
        AnnotationTool.Pen => "按住划出任意线条",
        AnnotationTool.Highlighter => "半透明高亮：盖在字上还能读原来的字",
        AnnotationTool.Mosaic => "涂过的地方变成不可读的色块（发图前遮敏感信息）",
        AnnotationTool.Text => "点一下选区开始打字，Enter 落笔、Esc 丢掉这一行",
        _ => string.Empty,
    };

    /// <summary>
    /// 箭头两翼的落点（顺序＝翼一、终点、翼二）。
    /// <para>放在模型里而不是绘制里，是因为<b>拖动中的预览也要算同一套几何</b>：
    /// 两处各写一遍"翼长多少、张角多大"，就会出现预览是个样子、存出来是另一个样子。</para>
    /// </summary>
    public static IReadOnlyList<PixelPoint> ArrowBarbs(PixelPoint from, PixelPoint to, int thickness)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0) return new[] { to, to, to };        // 杆都没有方向，两翼更无从算起
        var radius = Math.Max(0, (thickness - 1) / 2);
        var head = Math.Max(8, radius * 5);
        // 极角转 180°±30°，从箭头顶端往回掠出两翼
        var angle = Math.Atan2(dy, dx);
        const double spread = Math.PI / 6;
        var barbs = new List<PixelPoint>(2);
        for (var sign = -1; sign <= 1; sign += 2)
        {
            var barb = angle + Math.PI - sign * spread;
            barbs.Add(new PixelPoint(
                to.X + (int)Math.Round(head * Math.Cos(barb), MidpointRounding.AwayFromZero),
                to.Y + (int)Math.Round(head * Math.Sin(barb), MidpointRounding.AwayFromZero)));
        }
        return new[] { barbs[0], to, barbs[1] };
    }

    /// <summary>
    /// 这条标注<b>真正占住的那一块</b>（含线宽外沿，且已作用旋转/缩放），不做裁剪——交给绘制方按缓冲尺寸夹。
    /// <para>椭圆与矩形用两个对角点；画笔/荧光笔/马赛克用整条折线的外接框；
    /// 文字按 <b>GDI 实测</b>宽高——这块框除了当刷新范围，现在还要当"点哪里算选中它"和"选择框画在哪"，
    /// 按"字数 × 字高"估会在中英混排那行上偏出一截，用户看到的就是"我点这行字，框跑到别处去了"。</para>
    /// </summary>
    public IntRect Bounds()
    {
        if (Points.Count == 0) return default;
        var points = TransformedPoints();
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        if (Tool == AnnotationTool.Text)
        {
            var (width, height) = StarMark.Integrations.Capture.GdiTextDrawer.Measure(Text ?? string.Empty, DrawFontHeight);
            return new IntRect(minX, minY, Math.Max(1, width), Math.Max(1, height));
        }
        var pad = Tool == AnnotationTool.Mosaic ? Thickness / 2 + MosaicBlockSize : Thickness;
        return new IntRect(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);
    }

    /// <summary>包围盒的四个角（"拖动改大小"的把手位置）。顺序＝左上、右上、右下、左下。</summary>
    public IReadOnlyList<PixelPoint> Corners()
    {
        var box = Bounds();
        return new[]
        {
            new PixelPoint(box.X, box.Y), new PixelPoint(box.Right, box.Y),
            new PixelPoint(box.Right, box.Bottom), new PixelPoint(box.X, box.Bottom),
        };
    }

    /// <summary>
    /// 这一点算不算点中这一条。<paramref name="slop"/> 是给"手指头点不准"的容差（物理像素）：
    /// 细线只有两三个像素宽，不容差就等于告诉用户"能选中"其实选不中。
    /// </summary>
    public bool Contains(PixelPoint at, int slop)
    {
        var box = Bounds();
        return at.X >= box.X - slop && at.X <= box.Right + slop
            && at.Y >= box.Y - slop && at.Y <= box.Bottom + slop;
    }
}
