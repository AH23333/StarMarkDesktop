#nullable enable
using System;
using System.Globalization;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Capture;

/// <summary>
/// 截图 / 贴图的几何与上限判据。
/// <para>
/// 这里全是从"哪一步会算错"倒推出来的纯函数：反向拖动、跨屏负坐标、DIP↔物理像素的舍入方向、
/// 选区超出源图、贴图缩放与数量上限、文件名撞车。捕获本身在
/// <c>StarMark.Integrations/Capture</c>（那边只负责"把像素搬回来、搬不回来就说原因"）。
/// </para>
/// </summary>
public static class CaptureGeometry
{
    /// <summary>选区最小边长：小于它说明用户只是点了一下没拖动，不该当成一张 1×1 的图。</summary>
    public const int MinSelectionSide = 3;

    /// <summary>
    /// 同时允许的贴图窗数，必须有上限：贴图要常驻像素。批次 PN 之后一张贴图＝编辑器本身，
    /// 常驻的不止一份（底图 + 已合成的预览 + 那份位图缓冲 ≈ 3 份），4K 全屏一张就是上百 MB。
    /// </summary>
    public const int MaxPins = 12;

    /// <summary>贴图缩放区间（D3 口径里的"能放大到当便签看、能缩小到不挡地方"）。</summary>
    public const double MinPinZoom = 0.2;
    public const double MaxPinZoom = 5.0;

    /// <summary>滚轮一档的倍率（乘法而不是加法：小图上调一档不该跳一大步）。</summary>
    public const double ZoomStep = 1.1;

    /// <summary>
    /// 两点确定的矩形归一化。用户在屏幕上通常是<b>从右下往左上</b>拖，
    /// 不做这一步就会把负宽高的选区一路传进裁剪与像素步长计算里。
    /// </summary>
    public static IntRect Normalize(int x0, int y0, int x1, int y1)
        => new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));

    /// <summary>
    /// 与边界求交；<b>交集不足一个像素时返回 null</b>（不是空矩形）——"完全在屏外 / 只贴到边"与
    /// "真有一块可截"必须能分开判，否则遮罩窗会在 0 宽选区上继续往下走。
    /// 输入须是宽高非负的矩形（见 <see cref="IntRect"/> 的不变量与 <see cref="Normalize"/>）。
    /// </summary>
    public static IntRect? Intersect(IntRect rect, IntRect bounds)
    {
        var left = Math.Max(rect.X, bounds.X);
        var top = Math.Max(rect.Y, bounds.Y);
        var right = Math.Min(rect.Right, bounds.Right);
        var bottom = Math.Min(rect.Bottom, bounds.Bottom);
        if (right <= left || bottom <= top) return null;
        return new IntRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// DIP 矩形 → 物理像素。用 <see cref="MidpointRounding.AwayFromZero"/> 而不是截断：
    /// 150% 缩放下截断会让每条边各少一像素，表现为"截出来的比框选看到的小一圈"
    /// （与 R-P1-9 那条非 100% DPI 错位是同一族问题）。
    /// </summary>
    public static IntRect ToPhysicalPixels(IntRect dipRect, double dpiScale)
    {
        if (dpiScale <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI 缩放因子必须为正");
        static int Map(double value, double scale) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        var left = Map(dipRect.X, dpiScale);
        var top = Map(dipRect.Y, dpiScale);
        var right = Map(dipRect.Right, dpiScale);
        var bottom = Map(dipRect.Bottom, dpiScale);
        return new IntRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 选区能不能用。返回 null＝可以；否则是给界面直接显示的中文原因。
    /// <para>注意"超出可截范围"不当错误处理：跨屏拖选时选区大半在屏外是正常操作，
    /// 交得上一块就行，所以这里只拦"与可截范围完全不相交"和"太小"。</para>
    /// </summary>
    public static string? SelectionProblem(IntRect selection, IntRect? intersectedBounds)
    {
        if (intersectedBounds is null) return "选区落在所有显示器之外，没有可截取的内容";
        var box = intersectedBounds.Value;
        if (box.Width < MinSelectionSide || box.Height < MinSelectionSide)
            return $"选区太小（{box.Width} × {box.Height}），请按住的拖动再放开";
        return null;
    }

    /// <summary>
    /// 从已捕获的一帧里裁剪这块选区是否安全（<paramref name="frameBounds"/> 用虚拟桌面坐标，
    /// 因此原点可以是负的）。返回 null＝可裁；裁剪偏移由 <see cref="CropOffset"/> 给出。
    /// </summary>
    public static string? CropProblem(IntRect selection, IntRect frameBounds)
    {
        if (frameBounds.IsEmpty) return "本机没有可用的显示器画面";
        if (Intersect(selection, frameBounds) is null) return "选区不在已捕获的画面范围内";
        return null;
    }

    /// <summary>选区相对帧左上角的像素偏移（裁剪时读这一段的起点）。</summary>
    public static (int X, int Y) CropOffset(IntRect selection, IntRect frameBounds)
        => (selection.X - frameBounds.X, selection.Y - frameBounds.Y);

    /// <summary>选区尺寸提示（遮罩窗上跟随鼠标显示的那串）。</summary>
    public static string FormatSize(int width, int height)
        => width.ToString(CultureInfo.InvariantCulture) + " × " + height.ToString(CultureInfo.InvariantCulture);

    /// <summary>缩放夹进区间；NaN/∞（上一状态被算坏）回落到 1×，而不是把 NaN 传给布局。</summary>
    public static double ClampZoom(double zoom)
        => double.IsNaN(zoom) || double.IsInfinity(zoom) ? 1.0 : Math.Clamp(zoom, MinPinZoom, MaxPinZoom);

    /// <summary>
    /// 滚轮缩放。上滚放大一档、下滚缩小一档，端点上不再变化（返回原值）。
    /// 已在区间外（例如配置里存进一个 0.05）时先夹回来，避免一档之后跳出区间。
    /// </summary>
    public static double NextZoom(double current, int wheelDelta)
    {
        var clamped = ClampZoom(current);
        if (wheelDelta == 0) return clamped;
        return wheelDelta > 0
            ? ClampZoom(clamped * ZoomStep)
            : ClampZoom(clamped / ZoomStep);
    }

    /// <summary>贴图缩放后的显示边长（至少 1 像素：0 宽的图会让 WinUI 的布局直接不渲染，而不是"看不见"）。</summary>
    public static double ZoomedLength(double sourceLength, double zoom)
        => Math.Max(1, sourceLength * ClampZoom(zoom));

    /// <summary>
    /// 贴图窗左上角该落在哪儿（滚轮缩放与拖动共用同一条判据）。
    /// <para>
    /// 要治的是真机反馈的"过度放大之后贴图跑出屏幕外，再也看不见了"。原来的缩放<b>绕窗口中心</b>，
    /// 于是中心一旦被拖到屏外（贴图允许拖出屏），往下缩只是"围着那个屏外的中心收拢"，整块永远回不来。
    /// </para>
    /// <para>
    /// 口径按 Snipaste：<b>缩放钉住左上角</b>，再用这条判据收边——
    /// ① 整块塞得下工作区时，必须整块留在屏内（左上角在屏外就会被自动拉回屏幕边缘，正是用户要的重置）；
    /// ② 放不下时（图比屏还大）不强求整块可见，但<b>至少留 <paramref name="minVisible"/> 像素在屏内</b>，
    ///    左右上下哪一方向都不许整块丢光。
    /// </para>
    /// <para>退化输入都不许抛、也不许把窗甩到别处：工作区拿不到（0 尺寸）、尺寸非正、
    /// 或工作区窄到 <c>min</c> 会大于 <c>max</c>（<c>Math.Clamp</c> 在这种情况下直接抛异常）。</para>
    /// </summary>
    public static (int X, int Y) PinOrigin(int x, int y, int width, int height, IntRect workArea, int minVisible = 48)
    {
        if (workArea.IsEmpty) return (x, y);            // 拿不到工作区：宁可什么都不做，也不要把窗丢到(0,0)
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var keep = Math.Max(1, minVisible);

        int Clamp(int value, int lo, int hi) => hi < lo ? lo : Math.Clamp(value, lo, hi);

        if (w <= workArea.Width && h <= workArea.Height)
        {
            // 塞得下 ⇒ 整块留 inside：这一条就把"左上角在屏外 + 缩到能看见"自动收敛回屏幕边缘
            return (Clamp(x, workArea.X, workArea.Right - w), Clamp(y, workArea.Y, workArea.Bottom - h));
        }

        // 塞不下 ⇒ 只保证每个方向都还有一条 keep 宽的像素在屏内（贴图仍可拖出屏去看不想看的部分）。
        // 下界＝让右边露出 keep（x + w ≥ X + keep）；上界＝让左边露出 keep（x ≤ Right - keep）。
        return (Clamp(x, workArea.X + keep - w, workArea.Right - keep),
                Clamp(y, workArea.Y + keep - h, workArea.Bottom - keep));
    }

    /// <summary>工具条与它服务的那块画面之间的缝隙（选区阶段是 DIP，贴图态按屏的缩放换算过再传）。</summary>
    public const int BarGap = 6;

    /// <summary>工具条离屏边的最小距离：夹住它，条子就不会被顶到任务栏后面或屏外面去。</summary>
    public const int BarMargin = 4;

    /// <summary>
    /// 工具条左上角该落在哪儿。<b>截图（选区）阶段与贴图阶段共用这一条判据</b>（批次 WQ）：
    /// 贴图那条曾经自己写了一份"水平居中于画面"，于是同一个工具在两个阶段停在两个地方，
    /// 而且居中还带来一个额外毛病——那扇窗的宽度＝内容实测宽度，条子一变宽左边缘就跟着跑。
    /// <para>口径（与用户熟悉的截图条一致）：① <b>右缘对齐</b>到画面的右缘；② 优先放在画面<b>下方</b>，
    /// 下方放不下才放上方；③ 整条夹回屏内（<paramref name="margin"/> 那一圈留给任务栏与屏边）。</para>
    /// <para>
    /// <paramref name="sideHeight"/> 与 <paramref name="barHeight"/> 分开是贴图态那条具体的坑：点开"图形选择栏"
    /// 时条子会长出第二行，用<b>长高之后</b>的高度去判上下，会在用户刚把手伸向那一栏的瞬间把整条翻到画面
    /// 另一侧。所以判"放哪一侧"只看常驻的那一行（按钮行），摆放仍按整条的实际高度夹。
    /// 选区阶段两者相同（条子住在全屏遮罩窗里，长高不外溢）。
    /// </para>
    /// <para>退化输入不许抛、也不许把条子甩到看不见的地方：拿不到屏（<paramref name="bounds"/> 为空）时
    /// 照公式放在画面下方、不做夹取；条子比屏还宽/还高时贴到 <paramref name="margin"/> 处，
    /// 而不是让 <c>Math.Clamp</c> 因为 min&gt;max 直接抛异常。</para>
    /// </summary>
    public static (int X, int Y) BarOrigin(
        IntRect target, IntRect bounds, int barWidth, int barHeight, int sideHeight,
        int gap = BarGap, int margin = BarMargin)
    {
        var width = Math.Max(1, barWidth);
        var height = Math.Max(1, barHeight);
        var side = Math.Max(1, sideHeight);
        if (bounds.IsEmpty) return (target.Right - width, target.Bottom + gap);

        int Clamp(int value, int lo, int hi) => hi < lo ? lo : Math.Clamp(value, lo, hi);

        var x = Clamp(target.Right - width, bounds.X + margin, bounds.Right - width - margin);
        var below = target.Bottom + gap + side <= bounds.Bottom - margin;
        var y = below ? target.Bottom + gap : target.Y - height - gap;
        return (x, Clamp(y, bounds.Y + margin, bounds.Bottom - height - margin));
    }

    /// <summary>
    /// 选区上"这一点按下去要干什么"的答案：中间＝整块移动，八条边/四个角＝改大小，
    /// 完全在外面＝<see cref="SelectionEdge.None"/>（放行给"重新框一块"）。
    /// </summary>
    public enum SelectionEdge
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>
    /// 这一按落在选区的哪个部位。<b>角点先于边、边先于内部</b>：角上那 8×8 同时属于两条边，
    /// 判成"左"还是"上"会让用户拖出与他预期相反的那一条。
    /// <paramref name="slop"/> 是给把手留的容差（把手画在框外一侧，不放宽就点不着）。
    /// </summary>
    public static SelectionEdge SelectionEdgeAt(IntRect selection, PixelPoint at, int slop)
    {
        var onLeft = Math.Abs(at.X - selection.X) <= slop;
        var onRight = Math.Abs(at.X - selection.Right) <= slop;
        var onTop = Math.Abs(at.Y - selection.Y) <= slop;
        var onBottom = Math.Abs(at.Y - selection.Bottom) <= slop;

        if (onLeft && onTop) return SelectionEdge.TopLeft;
        if (onRight && onTop) return SelectionEdge.TopRight;
        if (onLeft && onBottom) return SelectionEdge.BottomLeft;
        if (onRight && onBottom) return SelectionEdge.BottomRight;
        if (onLeft) return SelectionEdge.Left;
        if (onRight) return SelectionEdge.Right;
        if (onTop) return SelectionEdge.Top;
        if (onBottom) return SelectionEdge.Bottom;

        return at.X >= selection.X && at.X < selection.Right && at.Y >= selection.Y && at.Y < selection.Bottom
            ? SelectionEdge.Move
            : SelectionEdge.None;
    }

    /// <summary>
    /// 移动选区：<b>由"按下时的矩形 + 总位移"直接算</b>，不累加每帧增量（贴图拖动那条抖动教训同一口径），
    /// 且整块不许离开工作区——选区决定截到哪块画面，出一半屏就等于把已画的标注丢到看不见的地方。
    /// </summary>
    public static IntRect MoveSelection(IntRect start, int dx, int dy, IntRect workArea)
    {
        int Clamp(int value, int lo, int hi) => hi < lo ? lo : Math.Clamp(value, lo, hi);
        return new IntRect(
            Clamp(start.X + dx, workArea.X, workArea.Right - start.Width),
            Clamp(start.Y + dy, workArea.Y, workArea.Bottom - start.Height),
            start.Width, start.Height);
    }

    /// <summary>
    /// 改选区大小：拖哪条边就只动那条边（对面钉住），角点同时动两条。<b>最小边长夹住</b>
    /// （0 或负尺寸的选区会让后面的整条链算出无意义的东西：底图 0 宽、把手位置反向），
    /// 并且每条边都不得越过本屏工作区。
    /// </summary>
    public static IntRect ResizeSelection(IntRect start, SelectionEdge edge, int dx, int dy, IntRect workArea, int minSide)
    {
        int Clamp(int value, int lo, int hi) => hi < lo ? lo : Math.Clamp(value, lo, hi);
        var min = Math.Max(1, minSide);
        var (left, top, right, bottom) = (start.X, start.Y, start.Right, start.Bottom);

        if (edge is SelectionEdge.Left or SelectionEdge.TopLeft or SelectionEdge.BottomLeft)
            left = Clamp(start.X + dx, workArea.X, right - min);
        if (edge is SelectionEdge.Right or SelectionEdge.TopRight or SelectionEdge.BottomRight)
            right = Clamp(start.Right + dx, left + min, workArea.Right);
        if (edge is SelectionEdge.Top or SelectionEdge.TopLeft or SelectionEdge.TopRight)
            top = Clamp(start.Y + dy, workArea.Y, bottom - min);
        if (edge is SelectionEdge.Bottom or SelectionEdge.BottomLeft or SelectionEdge.BottomRight)
            bottom = Clamp(start.Bottom + dy, top + min, workArea.Bottom);

        return new IntRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 再开一张贴图行不行。到上限时的文案要带上怎么办（关掉不用的那张），
    /// 而不是只说"不行"——这是 P-54 那条"提示与阻碍"的口径。
    /// </summary>
    public static string? PinLimitProblem(int currentPins)
        => currentPins >= MaxPins
            ? $"已经有 {currentPins} 张贴图，最多 {MaxPins} 张——先关掉不用的再贴（贴图会把那块画面常驻在内存里）"
            : null;

    /// <summary>
    /// 对"所有贴图"下指令（隐藏 / 穿透 / 全关）时可不可行。
    /// 一张都没有时必须回原因：这三条指令在空集上都是"什么都没发生"，
    /// 静默返回就等于让用户以为按错了键（热键回调没有任何别的反馈渠道）。
    /// </summary>
    public static string? PinCommandProblem(int currentPins)
        => currentPins == 0 ? "现在没有贴图，这一条没有可操作的对象" : null;

    /// <summary>贴图当前的倍率文案（右上角那颗角标用的百分比）。</summary>
    public static string FormatZoom(double zoom)
        => ((int)Math.Round(ClampZoom(zoom) * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 贴图窗该占多少<b>物理像素</b>。源图就是按物理像素截下来的，所以 1× 时窗口的物理尺寸
    /// 与当初屏幕上那块区域完全一致——<b>不必也不该再乘 DPI 缩放</b>，否则 150% 屏上贴图会比原物大一半。
    /// 舍入用 AwayFromZero（截断会让 1.1× 之类的档位每档都少一像素，多档之后肉眼可见地缩水）。
    /// </summary>
    public static (int Width, int Height) PinPixelSize(int sourceWidth, int sourceHeight, double zoom)
    {
        static int Map(int source, double factor)
            => Math.Max(1, (int)Math.Round(source * ClampZoom(factor), MidpointRounding.AwayFromZero));
        return (Map(sourceWidth, zoom), Map(sourceHeight, zoom));
    }

    /// <summary>
    /// 截图文件名（不含目录）。同一秒内连拍时靠 <paramref name="collisionIndex"/> 递增，
    /// 而不是把时间戳加粗到毫秒——文件名里出现毫秒只是噪声，而撞车的概率只发生在"一次热键连按"时。
    /// </summary>
    public static string BuildFileName(DateTimeOffset now, string extension, int collisionIndex = 0)
    {
        var stem = $"StarMark {now.LocalDateTime:yyyy-MM-dd HHmmss}";
        var suffix = extension.StartsWith('.') ? extension[1..] : extension;
        var suffixText = string.IsNullOrWhiteSpace(suffix) ? "png" : suffix.Trim().ToLowerInvariant();
        return collisionIndex <= 0
            ? $"{stem}.{suffixText}"
            : $"{stem} ({collisionIndex}).{suffixText}";
    }
    /// <summary>从 center 到 p 的方位角（度，0＝向右，逆时针为正）。角度换算只在模型里写一次。</summary>
    public static double AngleDegrees(PixelPoint center, PixelPoint p)
        => Math.Atan2(p.Y - center.Y, p.X - center.X) * 18d / Math.PI;
}
