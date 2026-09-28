#nullable enable
namespace StarMark.Integrations.Canvas;

/// <summary>
/// 把<b>预乘</b>的墨叠在那块背景上（仅"有底"那条提交路用得到，见 <see cref="LayeredCanvasWindow.BackdropArgb"/>）。
/// <para>一条式子同时服务两块底：<c>out = ink + bg × (255 − inkA) ⁄ 255</c>，<b>alpha 也按同一条走</b>
/// （<c>outA = inkA + bgA × (255 − inkA) ⁄ 255</c>）。底不透明（bgA=255）时它自己就退化成"结果恒不透明"，
/// 所以<b>没有第二个"白板专用"混合式</b>——两份迟早分岔（一处偏色、一处不偏色）。</para>
/// <para>两个端点各自钉住（<c>CanvasBoardBackdropTests</c>）：<c>inkA ≤ 1</c>（没墨，1 是空白像素的命中测试哨兵）＝纯底色；
/// <c>inkA = 255</c>（实心墨）＝原样是墨、与底色无关。中间值＝按比例吃掉底色，且<b>四条通道共用同一个 alpha</b>。</para>
/// <para>除以 255 走 <c>(t + (t&gt;&gt;8) + 127) &gt;&gt; 8</c> 这条整数近似，<b>不是</b>直接 <c>&gt;&gt;8</c>：
/// 后者会系统性偏暗（<c>inv=127</c> 时少 1），整块板子像蒙了层灰，而"看着是干净的白纸"就是这块底的全部意义。</para>
/// </summary>
public static class BackdropBlend
{
    /// <summary>
    /// 预乘墨 <paramref name="ink"/> 叠在预乘底色 <paramref name="bg"/> 上（白板＝0xFFFFFFFF，幕布＝0x80000000）。
    /// <para><b>幕布那块跟着鼠标的亮区不走这里</b>：亮区是"整像素原样拷贝"（两个调用方都按
    /// <see cref="LayeredCanvasWindow"/> 的行区间跳过它）。传 0 进来会把空白像素算成 alpha=0，
    /// 而 alpha=0 在鼠标眼里不属于这块玻璃——亮区里就"点不动、也画不上"（批次 WO 那条坑的另一个入口）。</para>
    /// </summary>
    public static uint Over(uint ink, uint bg)
    {
        var a = (int)(ink >>> 24);
        // a≤1 都算"这里没有墨"：1 是空白像素的命中测试哨兵（<c>LayeredCanvasWindow.BlankPixel</c>），不是画上去的浓度。
        // 若只认 a==0，整块白板会停在 254 而不是 255——"这是一张干净的白纸"正是这块底的全部意义。
        if (a <= 1) return bg;
        if (a == 255) return ink | 0xFF000000u;                   // 实心墨：一点底都不掺（底是什么都一样）
        var inv = 255 - a;
        var b = Mix(ink & 0xFF, bg & 0xFF, inv);
        var g = Mix((ink >> 8) & 0xFF, (bg >> 8) & 0xFF, inv);
        var r = Mix((ink >> 16) & 0xFF, (bg >> 16) & 0xFF, inv);
        var alpha = Mix((uint)a, (bg >>> 24) & 0xFF, inv);
        return ((uint)alpha << 24) | (r << 16) | (g << 8) | b;
    }

    private static uint Mix(uint inkChannel, uint backdropChannel, int inv)
    {
        var t = backdropChannel * (uint)inv;
        return inkChannel + ((t + (t >> 8) + 127) >> 8);
    }
}
