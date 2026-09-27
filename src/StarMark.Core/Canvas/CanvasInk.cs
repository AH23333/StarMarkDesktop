#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Canvas;

/// <summary>
/// 画布上能画的几种东西。<b>三种笔是"跟着手走"的，四种图形是"一次拖拽定形"的</b>：
/// 图形不是新的渲染层——它被展成一条点列（<see cref="CanvasShapes.Outline"/>）后走同一支笔的合成、
/// 同一条撤销、同一块橡皮，所以分层与生命周期口径（§16.3）一点没动。
/// <para>
/// 为什么图形值得进来（发起人点名"给画布加类似截图/贴图的图形编辑"）：讲课时圈一个重点、
/// 指一条边界，用自由手抖三下不如一次拖出一个矩形。而"这一按归谁"与选哪支工具<b>无关</b>
/// （2026-09-27 用户改判，准绳 §14.1）：图形与画笔一样，只在绘制态落墨、在穿透态让给下层。
/// </para>
/// </summary>
public enum CanvasTool
{
    /// <summary>实色笔：留下来的持久笔迹。</summary>
    Pen,
    /// <summary>荧光笔：宽 + 外圈光晕，盖在字上仍看得见底下的字。</summary>
    Highlighter,
    /// <summary>橡皮：只作用于<b>持久层</b>——荧光段自己会到期，擦它没有意义（规格 §16.6 的语义分离）。</summary>
    Eraser,
    /// <summary>矩形：拖拽的两个对角定形，四条边闭口。</summary>
    Rectangle,
    /// <summary>椭圆：拖拽的矩形内切，按笔宽采样成一条连续轮廓。</summary>
    Ellipse,
    /// <summary>直线：起点到终点一段。</summary>
    Line,
    /// <summary>折线：一次拖拽只定一段，接在已定的顶点后面（讲解时勾一个轮廓比连画五条直线省事）。</summary>
    PolyLine,
    /// <summary>箭头：直线 + 终点一个开口头（指向拖拽结束那一端）。</summary>
    Arrow,
}

/// <summary>
/// 工具分组与名字。<b>分组与名字都只有一处出处</b>（方案 §3.4：工具语义一处重定义）：
/// 画布这八颗就是截图标注那张表的一个子集，名字与"哪几颗算图形"都从 <see cref="Capture.AnnotationTools"/> 要答案。
/// <para>
/// 从前这里自己列两份（<c>Shapes</c>／<c>Brushes</c>）并自己写中文名，于是同一颗工具在两条栏上
/// 一个叫「荧光笔」一个叫「荧光」、一个叫「橡皮」一个叫「橡皮擦」，而加一种工具要记得改四处——
/// 批次 WM 那条"文字不许写死在 XAML"的同一种病，只是搬到了模型里。
/// </para>
/// </summary>
public static class CanvasTools
{
    /// <summary>
    /// 画布那八颗与截图那十一颗的<b>同一套词汇</b>之间唯一的一份映射。
    /// 两边同名的工具语义完全一致（同一支笔、同一种形状）；截图独有的（文字／序号／打码）画布今天没有，
    /// 所以 <see cref="Supports"/> 是这一侧的门槛，而它只被用来过滤那张总表。
    /// </summary>
    public static Capture.AnnotationTool ToAnnotation(this CanvasTool tool) => tool switch
    {
        CanvasTool.Pen => Capture.AnnotationTool.Pen,
        CanvasTool.Highlighter => Capture.AnnotationTool.Highlighter,
        CanvasTool.Eraser => Capture.AnnotationTool.Eraser,
        CanvasTool.Rectangle => Capture.AnnotationTool.Rectangle,
        CanvasTool.Ellipse => Capture.AnnotationTool.Ellipse,
        CanvasTool.Line => Capture.AnnotationTool.Line,
        CanvasTool.PolyLine => Capture.AnnotationTool.PolyLine,
        CanvasTool.Arrow => Capture.AnnotationTool.Arrow,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), $"画布工具 {tool} 没有对应的标注语义"),
    };

    /// <summary>画布这一侧认不认这颗工具（<b>只用来从总表里投影，不许拿它另列一份清单</b>）。</summary>
    private static bool Supports(Capture.AnnotationTool tool)
        => Enum.GetValues<CanvasTool>().Any(t => t.ToAnnotation() == tool);

    /// <summary>五种图形，顺序＝那张总表里的顺序（＝工具条上按钮的顺序）。</summary>
    public static readonly CanvasTool[] Shapes =
        Capture.AnnotationTools.Shapes.Select(FromAnnotation).ToArray();

    /// <summary>三支笔，顺序同上（图形不算在内：它们走另一排）。</summary>
    public static readonly CanvasTool[] Brushes =
        Capture.AnnotationTools.Brushes.Where(Supports).Select(FromAnnotation).ToArray();

    /// <summary>
    /// 反向映射：只有 <see cref="Supports"/> 认的那些才走得回来。
    /// <para>公开是因为画布的持久层现在存的是模型那一份（<see cref="Capture.Annotation"/>），
    /// 渲放时要从标注还原出笔刷形状——这条反向路也只有这里一份。</para>
    /// </summary>
    public static CanvasTool FromAnnotation(Capture.AnnotationTool tool)
        => Enum.GetValues<CanvasTool>().First(t => t.ToAnnotation() == tool);

    public static bool IsShape(this CanvasTool tool)
        => Capture.AnnotationTools.IsShapeTool(tool.ToAnnotation());

    /// <summary>条上与状态行用的中文名——<b>整条链只在这里问一次那张表</b>。</summary>
    public static string Name(this CanvasTool tool) => Capture.Annotation.ToolName(tool.ToAnnotation());
}

/// <summary>
/// 画布的尺寸档位。<b>与截图标注的「细/中/粗」不是同一张表</b>：画布是给站着讲的课用的，
/// 笔迹要能在几米外看见，所以整体比截图粗一档，而橡皮必须比最粗的笔还宽才擦得动。
/// </summary>
public static class CanvasWidths
{
    /// <summary>三档笔宽（物理像素直径）。</summary>
    public static readonly int[] Steps = { 4, 9, 18 };

    public static readonly string[] StepLabels = { "细", "中", "粗" };

    public static int At(int stepIndex) => Steps[Math.Clamp(stepIndex, 0, Steps.Length - 1)];

    /// <summary>默认那一档（"中"）：与截图标注同口径，用户没挑过之前不该是极细或极粗。</summary>
    public const int DefaultStepIndex = 1;

    /// <summary>橡皮的直径：比最粗的笔还宽，否则擦一条粗笔迹要来回拖好几下。</summary>
    public const int EraserDiameter = 28;

    /// <summary>
    /// 这条笔迹占到多大一片（半径）。<b>荧光笔的外圈光晕是笔宽的 3 倍</b>（规格 §16.4 的三层描边），
    /// 所以它的包围盒必须按光晕算——按笔芯算会让脏区漏掉那一圈，表现为"光晕画不出来／擦不干净"。
    /// </summary>
    public static int RadiusFor(CanvasTool tool, int width)
        => tool switch
        {
            CanvasTool.Highlighter => width * 3 / 2 + 1,
            CanvasTool.Eraser => EraserDiameter / 2,
            _ => Math.Max(1, width / 2),
        };
}

/// <summary>
/// 一条画布笔迹：一串按时间到达的点 + 一种颜色 + 一个宽度。
/// <para>
/// <b>坐标系是本屏的物理像素，原点在那块屏的左上角</b>——分层窗的后备位图就是这个坐标系，
/// 中间任何一次 DIP 换算都会在混合 DPI 的多屏上画偏（踩坑 #55 那一族）。
/// </para>
/// </summary>
public sealed class CanvasStroke
{
    /// <summary>相邻两次采样近于此距离就合并（规格 §16.4："相邻点距离合并 &lt;2px"）。</summary>
    public const int MinPointDistance = 2;

    /// <summary>
    /// 这一笔是<b>全机第几笔</b>——序号来自那唯一的一份时钟 <see cref="Capture.InkOrder"/>，不在这里自己数。
    /// <para>撤销要退的是"最后落的那一笔"，而笔迹是分屏各存一叠的：没有共同时序，编排那边只能
    /// "每块屏各退一条"（双屏真机症状：按一次撤销，两块屏同时各掉一笔，掉的两条还不是同一时刻画的）。</para>
    /// </summary>
    public long Order { get; private set; } = Capture.InkOrder.Next();

    private readonly List<PixelPoint> _points = new();
    private readonly int _radius;

    public CanvasTool Tool { get; }

    /// <summary>非预乘的源色（<c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c>，与 <c>Annotation.Opaque</c> 同一位序）。</summary>
    public int ColorBgra { get; }

    /// <summary>
    /// 真正画出去的颜色：<b>荧光笔强制半透明</b>，与截图那侧同一个口径（<c>Annotation.EffectiveColorBgra</c>）。
    /// 浓度这件事只该有一个地方说了算——在界面上"挑颜色"和在板子上"定浓度"分成两处写，
    /// 迟早会长成"选了个不透明的荧光笔，画出来一块看不穿的光斑"。
    /// </summary>
    public int EffectiveColorBgra
        => Tool == CanvasTool.Highlighter
            ? Capture.Annotation.WithAlpha(ColorBgra, Capture.Annotation.HighlighterAlpha)
            : ColorBgra;

    public int Width { get; }

    public IReadOnlyList<PixelPoint> Points => _points;

    /// <summary>笔还没落下（只有一个点）时也有一条包围盒：单点落笔要能画出一个圆点。</summary>
    public IntRect Bounds { get; private set; }

    /// <summary>
    /// <b>最后一步</b>扫过的那一小片（不含之前走过的地方）。
    /// <para>
    /// 拖动中每一帧真正需要重算的就是这一块。拿 <see cref="Bounds"/> 当脏区等于"每帧把整条笔迹
    /// 从几何重画一遍"——笔迹越长越慢，4K 粗档实测一路涨到几百毫秒一帧（真机症状："荧光笔绘制过程非常卡"）。
    /// </para>
    /// <para>
    /// 边界与 <c>CanvasCompositor.Walk</c> 返回的那块<b>用同一条式子</b>（半径 +1 是那条覆盖度斜坡，
    /// 少一像素就留一圈残影）；两处不一致就会表现为"笔迹边缘有一圈画不上／擦不掉"。
    /// </para>
    /// </summary>
    public IntRect TailBounds
    {
        get
        {
            if (_points.Count < 2) return Bounds;
            var a = _points[^2];
            var b = _points[^1];
            return new IntRect(
                Math.Min(a.X, b.X) - _radius - 1,
                Math.Min(a.Y, b.Y) - _radius - 1,
                Math.Abs(b.X - a.X) + _radius * 2 + 3,
                Math.Abs(b.Y - a.Y) + _radius * 2 + 3);
        }
    }

    public CanvasStroke(CanvasTool tool, int colorBgra, int width, PixelPoint first)
    {
        Tool = tool;
        ColorBgra = colorBgra;
        Width = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width), "笔宽必须为正");
        _radius = CanvasWidths.RadiusFor(tool, width);
        _points.Add(first);
        Bounds = RectAround(first);
    }

    /// <summary>
    /// 用一整串<b>已经算好的</b>点建一条笔迹（四种图形走这里）。
    /// <para>
    /// 刻意不复用 <see cref="AddPoint"/> 的"相邻点 &lt;2px 合并"：椭圆的采样点是按笔宽刻意排开的，
    /// 再被那道过滤削一次就会露出棱角（症状："画出来的是八角形不是圆"）；而矩形/箭头的顶点
    /// 本来就是要落准的转折点，不是采样噪声。
    /// </para>
    /// </summary>
    public static CanvasStroke FromPoints(CanvasTool tool, int colorBgra, int width, IReadOnlyList<PixelPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException("笔迹至少要有一个点", nameof(points));
        var stroke = new CanvasStroke(tool, colorBgra, width, points[0]);
        for (var i = 1; i < points.Count; i++)
        {
            stroke._points.Add(points[i]);
            stroke.Bounds = Union(stroke.Bounds, stroke.RectAround(points[i]));
        }
        return stroke;
    }

    /// <summary>
    /// 追加一个采样点。<b>太近的点直接丢</b>：不然一条慢慢拖的笔迹会在同一像素上叠几十次，
    /// 荧光笔的"重复涂抹不变深"就靠这个 + 合成时的取大规则一起成立。
    /// </summary>
    /// <returns>真的加上了才为真（调用方据此决定要不要重画脏区）。</returns>
    public bool AddPoint(PixelPoint p)
    {
        var last = _points[^1];
        var dx = p.X - last.X;
        var dy = p.Y - last.Y;
        if (dx * dx + dy * dy < MinPointDistance * MinPointDistance) return false;
        _points.Add(p);
        Bounds = Union(Bounds, RectAround(p));
        return true;
    }

    /// <summary>收笔时够不够格留下来：橡皮拖了不到两个点＝用户按了一下什么都没擦，别塞进撤销栈。</summary>
    public bool WorthKeeping => Tool != CanvasTool.Eraser || _points.Count >= 2;

    /// <summary>
    /// 落成模型里的那一条标注（方案 §3.4：画布的持久笔迹归 <see cref="Capture.InkDoc"/>，
    /// <see cref="CanvasStroke"/> 从此只是<b>渲放层的输入形状</b>，不再是另一个世界）。
    /// <para>点序<b>整份复制</b>而不是把活的列表交出去：这条笔迹在按下之后还在被追加，
    /// 模型里存着一份会跟着变的引用，就成了"撤销之后屏幕上还多出一截"那一族缺陷的温床。</para>
    /// <para>序号带过去（不是新建时重新领一个）：撤销比的是"全机第几笔"，
    /// 换一次形状不该换一次时间位置。</para>
    /// </summary>
    public Capture.Annotation ToAnnotation()
        => new(Tool.ToAnnotation(), _points.ToArray(), ColorBgra, Width) { Order = Order };

    /// <summary>
    /// 从模型里那一条标注还原渲放要的笔迹。<b>走 <see cref="FromPoints"/> 而不是 <see cref="AddPoint"/></b>：
    /// 存下来的点序就是当时真的画出去的那一份，再被"相邻 &lt;2px 合并"削一次，椭圆就会露出棱角
    /// （那条采样规则是给实时抖动用的，对已定形的点串是纯粹的破坏）。
    /// </summary>
    public static CanvasStroke FromAnnotation(Capture.Annotation ink)
    {
        var stroke = FromPoints(CanvasTools.FromAnnotation(ink.Tool), ink.ColorBgra, ink.Thickness, ink.Points);
        stroke.Order = ink.Order;
        return stroke;
    }

    private IntRect RectAround(PixelPoint p)
    {
        // 半径 +1：覆盖度那条斜坡（"圆边缘那圈给部分 alpha"）会多染一像素，脏区少了它就留残影
        var pad = _radius + 1;
        var x = p.X - pad;
        var y = p.Y - pad;
        return new IntRect(x, y, pad * 2 + 1, pad * 2 + 1);
    }

    private static IntRect Union(IntRect a, IntRect b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new IntRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }
}
