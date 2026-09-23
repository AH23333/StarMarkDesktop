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

    /// <summary>同时允许的贴图窗数。贴图按位图常驻内存，4K 全屏一张就几十 MB，必须有上限。</summary>
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

    /// <summary>贴图当前的提示文案（缩放徽标与右键菜单里的那个百分比）。</summary>
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
}
