#nullable enable
using System;
using StarMark.Abstractions.Capture;
using StarMark.Integrations.Canvas;

namespace StarMark.Core.Canvas;

/// <summary>
/// 画布"快照成贴"的几何判据（方案 §10 的 S4-⑤；2026-09-29 用户裁决：<b>只贴笔迹那一块，钉回原位</b>）。
/// <para>
/// 这一格从前只到"合成整块屏然后钉上去"，而那条形状在真机上的意思是：<b>按一次贴图，整块桌面被一张静止的图盖住</b>。
/// 贴图在 Z 序名册里排 3、画布玻璃在穿透态排 4（<see cref="Capture.LayerRules"/>），所以那张整屏图压在玻璃之上，
/// 又默认吃鼠标 ⇒ 之后每一按都落在这张图上，底下的窗口再也点不到。
/// 裁到笔迹那一块之后，它和截图链贴出来的那一张才是同一形态：看得见、能拖、能缩放、不占地方。
/// </para>
/// <para>
/// 三条判据都做成纯函数放在这里，而不是写进 <c>CanvasService</c>：留多少边、什么算"有墨"、怎么从帧里取子矩形，
/// 全要能逐像素机检（这个仓库里"全绿而功能坏"那几起，根都是判据住在 UI 里、测不到）。
/// UI 侧只补两件只有它知道的事：DIP→物理像素的换算，以及落点在虚拟桌面里的偏移。
/// </para>
/// </summary>
public static class CanvasSnapshotMath
{
    /// <summary>
    /// 笔迹那块的四周留多少 <b>DIP</b> 的边（不是物理像素：混屏时写死像素数，150% 屏上的边距就缩水一半，
    /// 与 <see cref="CursorCircle.RadiusDip"/> 同一条纪律）。
    /// <para>留边不是为了好看：墨贴着屏幕边缘时裁到边，得到的就是"被切掉半截的那一笔"，看起来像贴图把要的东西吃了。</para>
    /// </summary>
    public const double PaddingDip = 24d;

    /// <summary>
    /// "空白位"那一层的 alpha（读 <see cref="LayeredCanvasWindow.BlankPixel"/>，不另写一份 1）。
    /// 它是"这块玻璃在这里存在、但什么都没画"的记号，肉眼不可见（1/255）。
    /// </summary>
    private const int BlankAlpha = (int)(LayeredCanvasWindow.BlankPixel >> 24);

    /// <summary>
    /// 这块墨缓冲里<b>看得见的那些像素</b>占的最紧矩形；一个都看不见时返回 <see cref="CanvasCompositor.Nothing"/>。
    /// <para>
    /// 判据是 <b>alpha 高过空白位</b>，不是"与空白位不相等"：橡皮是按比例减 alpha 的，擦到见底那一点留下的是 0 或某个旧值，
    /// 它不一定正好等于空白位，却确实看不见——按"不等"判就会把一块透明的地方当成有墨，裁出来的块白长一圈。
    /// 反过来，真正画过的地方 alpha 必然高过 1（预乘缓冲里 alpha 就是这一层盖了多少）。
    /// </para>
    /// <para>
    /// <b>必须在叠底之前测</b>（<c>CompositeForSnapshot</c> 那一句之前）。幕布那块底是半透明的，叠过之后<b>整块缓冲</b>
    /// 每个像素的 alpha 都是 128，这里就会返回"整屏"——症状是"幕布开着时贴出来的仍是整屏、桌面照样被盖住"，
    /// 而单测全绿：它测的是这条判据本身，不是顺序。所以顺序由接线闸门钉住。
    /// </para>
    /// </summary>
    public static IntRect InkRegionOf(uint[] ink, int width, int height)
    {
        var x1 = int.MaxValue;
        var y1 = int.MaxValue;
        var x2 = int.MinValue;
        var y2 = int.MinValue;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if ((ink[row + x] >> 24) <= BlankAlpha) continue;
                if (x < x1) x1 = x;
                if (x > x2) x2 = x;
                if (y < y1) y1 = y;
                if (y > y2) y2 = y;
            }
        }
        return x2 < x1
            ? CanvasCompositor.Nothing
            : CanvasCompositor.Clamp(new IntRect(x1, y1, x2 - x1 + 1, y2 - y1 + 1), width, height);
    }

    /// <summary>
    /// 四周留边并夹回这块画面里。<b>空区域进得来、空区域出得去</b>（调用方据此还能再说一句"板上没笔迹"）。
    /// <para>夹取走 <see cref="CanvasCompositor.Clamp"/> 而不是 <c>Math.Clamp</c>：边比画面大时后者界限会颠倒并当场抛
    /// （批次 WQ 那条同一个形状）。</para>
    /// </summary>
    public static IntRect FrameOf(IntRect region, int width, int height, int padding)
        => region.IsEmpty ? CanvasCompositor.Nothing
        // 夹取在加边之后做一次：加完边越出画面的那一截由 Clamp 收回来，所以四边都能贴到画面边缘
        : CanvasCompositor.Clamp(new IntRect(
            region.X - padding, region.Y - padding,
            region.Width + padding * 2, region.Height + padding * 2), width, height);

    /// <summary>
    /// 从合成好的 BGRA 帧里裁出这一块。<b>源步长是帧宽、目标步长是裁块宽</b>——把两者混用会得到一张逐行错位的图
    /// （只有真机看得见，所以这条由"与源子矩形逐像素相同"的用例钉住）。
    /// <para>矩形必须已在帧内（<see cref="FrameOf"/> 负责夹好）。这里刻意不再夹一次：静悄悄把一个越界矩形改成更小的那块，
    /// 就等于"贴出来的比眼睛看到的少一圈"，而那正是这一格要修的病。</para>
    /// </summary>
    public static byte[] CropBgra(byte[] frame, int frameWidth, int frameHeight, IntRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return Array.Empty<byte>();
        if (rect.X < 0 || rect.Y < 0 || rect.Right > frameWidth || rect.Bottom > frameHeight)
            throw new ArgumentException($"裁块越出帧外（{rect.X},{rect.Y} {rect.Width}×{rect.Height} 对 {frameWidth}×{frameHeight}）");

        var pixels = new byte[rect.Width * rect.Height * 4];
        var rowBytes = rect.Width * 4;
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(frame, ((rect.Y + y) * frameWidth + rect.X) * 4, pixels, y * rowBytes, rowBytes);
        return pixels;
    }
}
