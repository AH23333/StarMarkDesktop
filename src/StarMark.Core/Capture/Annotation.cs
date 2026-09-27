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
    /// <summary>序号：每点一下放一个带编号的圆点（1、2、3…），Snipaste 同款。</summary>
    Number,
    /// <summary>橡皮擦：拖过已有标注时整条擦掉（同一次拖拭合并为一个撤销步）。</summary>
    Eraser,
}

/// <summary>按下去那一下想改的是哪一样（<see cref="Annotation.GrabAt"/> 的返回值）。
/// <para>住在模型里而不是界面里：它决定"用户以为自己在拖什么"，而界面里那份 if/else 一条都断言不到——
/// "拖动一行字结果字号变了"这种缺陷就是这么漏出去的。</para></summary>
public enum AnnotationGrab
{
    /// <summary>没按在任何把手上，也没按在这条标注上。</summary>
    None,
    /// <summary>框内＝移动位置。</summary>
    Move,
    /// <summary>角上＝改大小（文字＝改字号）。</summary>
    Scale,
    /// <summary>顶上的圆点＝转方向。</summary>
    Rotate,
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

    /// <summary>"笔"那一组：直接按在画布上走，各占一颗按钮（画法彼此差得远，收进浮层反而多一次点击）。
    /// 顺序对齐 Snipaste：画笔 / 荧光 / 文字 / 序号 / 打码 / 橡皮。</summary>
    public static readonly AnnotationTool[] Brushes =
    {
        AnnotationTool.Pen, AnnotationTool.Highlighter, AnnotationTool.Text,
        AnnotationTool.Number, AnnotationTool.Mosaic, AnnotationTool.Eraser,
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
/// 不需要任何反向操作。<b>之后可以整条移动 / 缩放 / 旋转</b>（批次 RE-3）：这两样改的都是
/// <see cref="Rotation"/>/<see cref="Scale"/> 两个变换量（轴点由 <see cref="Origin"/> 现算），
/// <b>不改点集本身</b>——于是"撤销一条变换"与"撤销一条标注"走的是同一条快照路径，同样不需要反向运算。
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

    /// <summary>序号标注上的编号（从 1 开始，仅 <see cref="AnnotationTool.Number"/> 使用）。</summary>
    public int Number { get; init; }

    /// <summary>
    /// 这一笔是<b>全机第几笔</b>（<see cref="InkOrder"/>，创建那一刻领到，之后不再变；<c>with</c> 会带着它走）。
    /// <para>它是"撤销退哪一笔"要跨叠比较的唯一依据：笔迹分家住在好几叠里（画布每块屏一叠、截图一叠、
    /// 每张贴图又一叠），而用户那句"退掉我最后画的那一笔"里的"最后"是跨这几叠比的。</para>
    /// <para><b>它参与记录判等是有意的</b>：一条标注是"用户画过的那一次"，不是那组数的集合。
    /// 从前两条几何完全相同的标注互相判等，橡皮的 <c>HashSet</c> 于是把"擦掉其中一条"实现成"两条一起没了"
    /// ——重影在矩形这类工整的工具上是真的画得出来的。各自唯一的序号把那两条分回两条。</para>
    /// </summary>
    public long Order { get; init; } = InkOrder.Next();

    /// <summary>
    /// 序号圆点的三档<b>半径</b>（物理像素，与 <see cref="ThicknessSteps"/> 同序）。
    /// <para>序号没有"线"可粗，所以它那一档表达的是点多大。中档 15 就是这条链一直以来的那颗点
    /// ——换档必须看得见变化（真机反馈："序号功能更改粗细无效果"），而默认观感不许跟着变。</para>
    /// </summary>
    public static readonly int[] NumberRadii = { 11, 15, 22 };

    /// <summary>
    /// 变换的轴点（可空）。<b>没指定时：几何类＝第一个点，文字＝字块自己的中心</b>（见 <see cref="Origin"/>）。
    /// <para>留一个可空槽而不是写死，是因为"绕哪一点缩放"要跟着拖动语义走：抓住某一头的把手时该钉住它对面那头，
    /// 那一档由 <see cref="WithScalePivotTowards"/> 在按下把手时填上（模型自己填，不让调用方各算一遍）。</para>
    /// </summary>
    public PixelPoint? Pivot { get; init; }

    /// <summary>绕 <see cref="Origin"/> 旋转的角度（度，顺时针）。0＝不转。</summary>
    public double Rotation { get; init; }

    /// <summary>绕 <see cref="Origin"/> 的等比缩放倍数（1＝原样）。文字靠它改字号。</summary>
    public double Scale { get; init; } = 1d;

    /// <summary>
    /// <b>旋转</b>的轴点。几何类＝<b>第一个点</b>；文字＝<b>字块自己的中心</b>（见 <see cref="TextCentre"/>）。
    /// <para>文字为什么单独：转一行字期望的是"这行字原地转一下"。绕左上角转会把它甩出去——
    /// 真机反馈的"转一下就跑到画外、和框对不上"就是这么来的。<b>缩放不走这里，走 <see cref="ScalePivot"/></b>：
    /// 那一条要跟着"用户按住了哪一头"。</para>
    /// </summary>
    public PixelPoint Origin => Tool == AnnotationTool.Text
        ? TextCentre
        : Pivot ?? (Points.Count > 0 ? Points[0] : default);

    /// <summary>
    /// <b>缩放</b>绕哪一点。几何类一直是 <see cref="Origin"/>（第一个点）；
    /// 文字默认绕字块中心，只有按在缩放把手上时才由 <see cref="WithScalePivotTowards"/> 改到被抓那一头的对面。
    /// <para>旋转仍走 <see cref="Origin"/>（文字＝字块中心）：那一条是 RF-2 实测出来的，别跟着这次一起动。</para>
    /// </summary>
    public PixelPoint ScalePivot => Tool == AnnotationTool.Text ? Pivot ?? TextCentre : Origin;

    /// <summary>
    /// 抓住某一头去改大小时，<b>把"钉住不动"的那一点设到它的对面</b>：手指那一端跟着走，另一端一步都不该挪。
    /// <para>真机反馈"缩放文字后，位置与文字框都偏移了"：默认那套绕字块中心缩放，放大时字块两头<b>同时</b>往外长，
    /// 于是左上角被推向左上方——用户看到的是"一缩放整行字就跑"。字块中心那条留着不改（改字号往四周均匀长），
    /// 只把"拖把手"这一条路指到对面那一头。几何类这一步先不接线：它今天绕 <see cref="Points"/> 的第一点，
    /// 拖右下角时本来就是对角，没有反馈支撑就不动已工作的代码。</para>
    /// <para>文字用的是<b>局部（未旋转）那一框</b>的角：转过的字，包围盒的角落在字外面的空处，
    /// 钉在那儿等于什么都没钉。</para>
    /// </summary>
    public Annotation WithScalePivotTowards(PixelPoint grabbed)
    {
        if (Tool != AnnotationTool.Text) return this;
        var current = TransformedPoints();
        if (current.Count == 0) return this;
        var topLeft = current[0];
        var (width, height) = StarMark.Integrations.Capture.GdiTextDrawer.Measure(Text ?? string.Empty, DrawFontHeight);
        var centre = new PixelPoint(topLeft.X + width / 2, topLeft.Y + height / 2);
        var pivot = new PixelPoint(
            grabbed.X > centre.X ? topLeft.X : topLeft.X + width,
            grabbed.Y > centre.Y ? topLeft.Y : topLeft.Y + height);
        // 顺手把"此刻这一份"折成基线（字高＝现在真画出来的那个，锚点＝现在的左上角，倍数归 1）。
        // <para>不折会怎样：轴点是按<b>当前</b>那一框挑的，而缩放公式是按<b>基线</b>那一框算位似的——
        // 已经放大过的字两个框不重合，第二次换头去拖就会把字钉回旧位置（"先拉左边再拉右边"连两下就跳，
        // 真机撞得到）。折过之后每次抓把手都等于第一次抓，公式只剩一种情形。</para>
        // <para>绝对上下限仍然守得住：写进基线的字高本身就是夹过的 6–200，倍数那一档只约束这一次手势。</para>
        return this with
        {
            Points = new[] { topLeft },
            FontHeight = DrawFontHeight,
            Scale = 1d,
            Pivot = pivot,
        };
    }

    /// <summary>字块中心＝锚点（这一次缩放的左上角）+ <b>基线那份字模</b>的一半。
    /// 基线会被 <see cref="WithScalePivotTowards"/> 换档，所以这里说的是"当前这一档"，不是最初打出来那一档。</summary>
    private PixelPoint TextCentre
    {
        get
        {
            var anchor = Points.Count > 0 ? Points[0] : default;
            var (width, height) = StarMark.Integrations.Capture.GdiTextDrawer.Measure(Text ?? string.Empty, FontHeight);
            return new PixelPoint(anchor.X + width / 2, anchor.Y + height / 2);
        }
    }

    /// <summary>缩放上下限。缩到 0 附近会"再也点不中"，放大到几百倍会直接把画布撑爆——两端都要有墙。</summary>
    public const double MinScale = 0.2;
    public const double MaxScale = 6d;

    /// <summary>容差的基准（物理像素）。把手只有几像素大，不容差就等于"看得见点不中"。</summary>
    public const int HandleSlop = 12;

    /// <summary>
    /// 角点（＝缩放）的容差<b>随包围盒的短边收缩</b>。
    /// <para>固定 12 像素放在一行字上（高约 22 像素）会把整行字都盖成"角点"，于是每一次拖动都变成缩放：
    /// 字号越拖越大、字还往右下方滑——真机反馈的"可拖动但位置四窜、文字脱离文字框、并且增大"就是这么来的。
    /// 按短边 1/4 取之后，四角各占住两端，中间那一大片仍然归"移动"。</para>
    /// <para>这条判据住在模型里而不是界面里，是因为它<b>可测</b>：任何尺寸下两端的容差带都不能在中间接上
    /// （接上了就等于这一档根本没有"移动"），所以还要再夹一道 <c>(短边-1)/2</c>。</para>
    /// </summary>
    public static int HandleSlopFor(IntRect box)
    {
        var shortSide = Math.Min(box.Width, box.Height);
        return Math.Min(Math.Min(HandleSlop, shortSide / 4), (shortSide - 1) / 2);
    }

    /// <summary>有没有带变换。没变换时 <see cref="TransformedPoints"/> 直接给原列表，不复制。</summary>
    public bool HasTransform => Rotation != 0 || Scale != 1d;

    /// <summary>两点足够近（容差是物理像素）。<b>界面的抓取判定与这里的测试共用这一个式子</b>，
    /// 免得两处各写一遍绝对值比较而哪天改出不一样。</summary>
    public static bool Near(PixelPoint a, PixelPoint b, int slop)
        => Math.Abs(a.X - b.X) <= slop && Math.Abs(a.Y - b.Y) <= slop;

    /// <summary>这一点算不算按在<b>缩放把手</b>上。
    /// <para><b>文字只认包围盒之外的那半个角。</b>一行字只有二十来像素高，<see cref="HandleSlopFor"/>
    /// 给四角各让出 5 像素之后，那几个 5×5 的小方块正好落在字的<b>两端</b>——而用户抓一行字要挪，
    /// 手就放在那一头：于是"拖动位置"被读成"拖角改字号"，字越拖越大（真机反馈）。
    /// 把手本来就画在角上、有一半露在框外，所以缩放这个动作仍然留着，只是不再和移动抢同一块地方。</para>
    /// <para>几何类不受这条影响：它们的框远大于角点区，内侧那一圈本来就是改大小的正常落点。</para></summary>
    public bool OnCorner(PixelPoint at, int slop)
    {
        if (!ScalesFromOutsideOnly) return Corners().Any(corner => Near(corner, at, slop));
        var box = Bounds();
        var inside = at.X >= box.X && at.X < box.Right && at.Y >= box.Y && at.Y < box.Bottom;
        return !inside && Corners().Any(corner => Near(corner, at, slop));
    }

    /// <summary>
    /// 按下去的那一点该改的是什么——<b>整条判定住在模型里</b>，因为它决定"用户以为自己在动哪一下"，
    /// 而界面里那份 if/else 是测不到的（批次 NF 同一课：测不到的判据＝改坏了没人知道）。
    /// <para>顺序是刻意的：旋转把手最优先（它是一颗画在框外的小点，用户看得见才会去按），
    /// 然后缩放把手，最后才是"在框内＝移动"。</para>
    /// </summary>
    public AnnotationGrab GrabAt(PixelPoint at, PixelPoint rotateHandleAt, int moveSlop)
    {
        if (Near(rotateHandleAt, at, HandleSlop)) return AnnotationGrab.Rotate;
        // 容差随短边收缩那条，是为了不让四角把"一行字"整条吃掉；框外那一半本来就不与移动抢地方，
        // 所以文字在<b>框外</b>拿满 HandleSlop——否则一行 22px 的字只剩 5×5，看得见却按不着。
        var cornerSlop = ScalesFromOutsideOnly ? HandleSlop : HandleSlopFor(Bounds());
        if (OnCorner(at, cornerSlop)) return AnnotationGrab.Scale;
        if (Contains(at, moveSlop)) return AnnotationGrab.Move;
        return AnnotationGrab.None;
    }

    /// <summary>只在<b>包围盒之外</b>认缩放把手的工具（文字）。见 <see cref="OnCorner"/>。</summary>
    public bool ScalesFromOutsideOnly => Tool == AnnotationTool.Text;

    /// <summary>
    /// 拖动这一改落成了什么：<paramref name="grab"/> 说"按住的是哪一路"，<paramref name="anchor"/> 是按下那一点，
    /// <paramref name="local"/> 是此刻（或松手）那一点。<b>整条算式住在模型里，而且只吃参数</b>。
    /// <para>它原来长在遮罩窗的一个 <c>switch (_grab)</c> 方法里，于是出了真机反馈的那个缺陷：
    /// <c>EndDrag</c> 先把拖动状态清零再算结果，<c>switch</c> 读到的是 <c>None</c>，落进 <c>default</c>＝缩放分支，
    /// 倍数按"按下点到松手点"算 ⇒ 拖一下位置，松手的瞬间字放大到上限（日志原件：
    /// <c>[AnnoGrab] Move</c> 后面跟着 <c>[AnnoDrag] Move font 22→132 scale 1.000→6.000</c>）。
    /// 拖动过程中显示是对的，所以"看预览"验不出来，模型用例也管不到那一步——只有把它变成纯函数才能钉住。</para>
    /// <para><c>None</c> 现在明确"什么都不改"，不再与缩放共用兜底分支：兜底哪个都不该像缩放。</para>
    /// </summary>
    public Annotation DraggedBy(PixelPoint anchor, PixelPoint local, AnnotationGrab grab) => grab switch
    {
        AnnotationGrab.Move => MovedBy(local.X - anchor.X, local.Y - anchor.Y),
        AnnotationGrab.Rotate => RotatedBy(AngleTowards(local) - AngleTowards(anchor)),
        AnnotationGrab.Scale => ScaleTowards(anchor, local),
        _ => this,
    };

    /// <summary>绕缩放轴点按"离它多远"改倍数。按下点几乎就在轴点上时比值没有意义（分母为 0）：保持原样，别让形状瞬间炸开。</summary>
    private Annotation ScaleTowards(PixelPoint anchor, PixelPoint local)
    {
        var pivot = ScalePivot;
        var start = Distance(anchor, pivot);
        return start < 1 ? this : ScaledBy(Distance(local, pivot) / start);
    }

    /// <summary>这一点相对旋转轴点的方位角（度）。</summary>
    private double AngleTowards(PixelPoint at)
    {
        var pivot = Origin;
        return Math.Atan2(at.Y - pivot.Y, at.X - pivot.X) * 180d / Math.PI;
    }

    private static double Distance(PixelPoint a, PixelPoint b)
        => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// 画出去的那一组点：把 <see cref="Rotation"/> 与 <see cref="Scale"/> 作用上去
    /// （旋转绕 <see cref="Origin"/>，缩放绕 <see cref="ScalePivot"/>）。
    /// <para>放在模型里而不是绘制里，是因为<b>选择框、命中测试、四个落点的渲染都要用同一组点</b>——
    /// 各算一遍就会出现"选择框框住的是原位置，字已经转走了"。</para>
    /// <para>文字单独一条：它只有一个点，那个点是"字块左上角"，而字块是绕<b>它自己的中心/被钉住的那一头</b>变的，
    /// 所以左上角要按当前字模重新算回去。把通用的"点绕轴转"直接套在左上角上，等于把位置也转了一次——
    /// 一转就跑到画外、和框对不上，正是真机反馈的那两下。</para>
    /// </summary>
    public IReadOnlyList<PixelPoint> TransformedPoints()
    {
        if (Points.Count == 0) return Points;
        if (Tool == AnnotationTool.Text)
        {
            if (Scale == 1d) return Points;                 // 转方向不改左上角（绕中心转），字高也没变 ⇒ 原样
            var topLeft = Points[0];
            var (baseWidth, baseHeight) = StarMark.Integrations.Capture.GdiTextDrawer.Measure(Text ?? string.Empty, FontHeight);
            var (width, height) = StarMark.Integrations.Capture.GdiTextDrawer.Measure(Text ?? string.Empty, DrawFontHeight);
            var scalePivot = ScalePivot;
            // 位似：轴点取字框的一个角时，缩放后那个角分毫不动，只有手指那一头在长。
            // 比例用"真画出来那份字模"量出的宽高，而不是 Scale 本身——字体宽度对倍数不是完全线性的（取整、
            // 换档后重新排版），拿 Scale 推会让本该钉住的那一角跑掉一两像素，而 Bounds() 与绘制吃的是量出来的那份。
            var rx = baseWidth == 0 ? 1d : (double)width / baseWidth;
            var ry = baseHeight == 0 ? 1d : (double)height / baseHeight;
            return new[]
            {
                new PixelPoint(
                    scalePivot.X + (int)Math.Round((topLeft.X - scalePivot.X) * rx, MidpointRounding.AwayFromZero),
                    scalePivot.Y + (int)Math.Round((topLeft.Y - scalePivot.Y) * ry, MidpointRounding.AwayFromZero)),
            };
        }
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

    /// <summary>整条平移（＝拖动位置）：点集与轴点一起走，角度与倍数不变（文字的轴由锚点推出来，跟着一起走）。</summary>
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
    /// <para>
    /// 文字的「粗细」实际是<b>字号</b>（真机反馈：输入时的文字大小要为编辑后的文字大小——
    /// 给用户一个看得见、选得到的字号档，输入框与烤出去的字都按这一档走，所见即所得才有抓手）。
    /// </para>
    /// <para>
    /// 序号的「粗细」是<b>圆点半径</b>（<see cref="NumberRadii"/>）。从前这里 fallthrough 到 2/4/8，
    /// 而绘制端的半径是一个常量，<b>这一档从来没被任何人读过</b>——条上点了粗档，序号一个像素都没变。
    /// </para>
    /// </summary>
    public static int ThicknessFor(AnnotationTool tool, int stepIndex)
    {
        var index = Math.Clamp(stepIndex, 0, ThicknessSteps.Length - 1);
        // 序号点的直径就是它的"粗细"，不能拿线宽那一档去量
        if (tool == AnnotationTool.Number) return NumberRadii[index];
        var step = ThicknessSteps[index];
        return tool switch
        {
            AnnotationTool.Highlighter => step * 4,
            AnnotationTool.Mosaic => step * 6,
            AnnotationTool.Text => step switch { 2 => 16, 4 => 22, 8 => 32, _ => step * 4 },
            _ => step,
        };
    }

    /// <summary>
    /// 那一档在某个工具上<b>量的到底是什么</b>——条上把数字报给用户时得带上单位，
    /// 否则"序号 ≈ 22 像素"会被读成直径（它是半径），而马赛克那一档量的是格子边长。
    /// </summary>
    public static string ThicknessUnit(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Number => "圆点半径",
        AnnotationTool.Text => "字号",
        AnnotationTool.Mosaic => "格子边长",
        AnnotationTool.Highlighter => "笔刷直径",
        _ => "线宽",
    };

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
    public static int MinPoints(AnnotationTool tool)
        => tool is AnnotationTool.Text or AnnotationTool.Number ? 1 : 2;

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
        if (Tool == AnnotationTool.Eraser) return "橡皮擦不是可绘制的标注";
        if (Points is null || Points.Count < MinPoints(Tool))
            return $"{ToolName(Tool)}至少需要 {MinPoints(Tool)} 个点";
        if (Tool == AnnotationTool.Text && string.IsNullOrEmpty(Text))
            return "文字是空的，没有可写上去的内容";
        // 文字不看 Thickness（字号是它自己的那条），序号看的正是 Thickness（＝圆点半径，批次 WR），
        // 所以只有文字这一项豁免范围检查
        if (Tool != AnnotationTool.Text
            && (Thickness < MinThickness || Thickness > MaxThickness))
            return $"{ToolName(Tool)}的{ThicknessUnit(Tool)} {Thickness} 不在 {MinThickness}–{MaxThickness} 之间";
        if (FontHeight is < 6 or > 200)
            return $"文字高度 {FontHeight} 太离谱（只接受 6–200 物理像素）";
        if (Scale is < MinScale or > MaxScale)
            return $"缩放倍数 {Scale:0.##} 超出 {MinScale:0.##}–{MaxScale:0.##}（缩到底会再也点不中，放到最大会撑破画面）";
        if (EffectiveColorBgra >>> 24 == 0)
            return "颜色是全透明的，画上去等于没画";
        return null;
    }

    /// <summary>
    /// 中文名（工具条与失败提示共用；枚举名直接进提示等于让用户读代码）。
    /// <para><b>全机只有这一张名字表</b>（方案 §3.4：工具语义一处重定义）：画布那条栏也问它。
    /// "荧光／橡皮擦"与画布那边的"荧光笔／橡皮"从前各写一份，同一条链的两根栏上叫法就不一样。
    /// 这里取两边都讲得通、且更完整的那一个。</para>
    /// </summary>
    public static string ToolName(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => "矩形",
        AnnotationTool.Ellipse => "椭圆",
        AnnotationTool.Line => "直线",
        AnnotationTool.PolyLine => "折线",
        AnnotationTool.Arrow => "箭头",
        AnnotationTool.Pen => "画笔",
        AnnotationTool.Highlighter => "荧光笔",
        AnnotationTool.Mosaic => "打码",
        AnnotationTool.Text => "文字",
        AnnotationTool.Number => "序号",
        AnnotationTool.Eraser => "橡皮",
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
        AnnotationTool.Text => "点一下选区就开始打字，Enter 换行，Esc 结束编辑；点已写好的字可以接着改",
        AnnotationTool.Number => "每点一下放一个带编号的圆点（1、2、3…），可拖动改位置",
        AnnotationTool.Eraser => "按住拖过已有标注，整条擦掉（一次擦除可一次撤销）",
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
            // 转出去的字占的是<b>斜着那一块的外接框</b>：只报未旋转的宽高，框就会横在原地而字站出去
            // （真机反馈的"文字显著脱离文字框范围内"）。数学与绘制端共用 TextGeometry 那一处。
            var box = StarMark.Integrations.Capture.TextGeometry.RotatedBox(minX, minY, width, height, Rotation);
            return new IntRect(box.Left, box.Top, Math.Max(1, box.Right - box.Left), Math.Max(1, box.Bottom - box.Top));
        }
        if (Tool == AnnotationTool.Number)
        {
            // 点＝圆心，半径＝条上那一档"粗细"（<see cref="NumberRadii"/>），再随整体缩放（旋转绕圆心，外接框不变）
            var radius = (int)Math.Round(Thickness * Math.Clamp(Scale, MinScale, MaxScale), MidpointRounding.AwayFromZero);
            var c = points[0];
            return new IntRect(c.X - radius, c.Y - radius, Math.Max(1, radius * 2), Math.Max(1, radius * 2));
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
