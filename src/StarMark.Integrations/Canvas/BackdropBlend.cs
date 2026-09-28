#nullable enable
namespace StarMark.Integrations.Canvas;

/// <summary>
/// 把<b>预乘</b>的墨像素叠在不透明底色上（仅白板底那条提交路用得到，见 <see cref="LayeredCanvasWindow.BackdropArgb"/>）。
/// <para>逐通道 <c>out = ink + board × (255 − inkA) ⁄ 255</c>，输出 alpha 恒为 255：
/// 墨本来就按预乘存，所以这里既不用反预乘、也不用在像素循环里判断用什么混合律。</para>
/// <para>三个端点各自钉住（<c>BackdropBlendTests</c>）：<c>a=0</c>（没画过）＝纯底色；<c>a=255</c>＝原样是墨；
/// 中间值＝按比例吃掉底色，且<b>三条通道共用同一个 alpha</b>——白底偏色是最容易看出来的脏。</para>
/// <para>除以 255 走 <c>(t + (t&gt;&gt;8) + 127) &gt;&gt; 8</c> 这条整数近似，<b>不是</b>直接 <c>&gt;&gt;8</c>：
/// 后者会系统性偏暗（<c>inv=127</c> 时少 1），整块板子像蒙了层灰，而"看着是干净的白纸"就是这块底的全部意义。</para>
/// </summary>
public static class BackdropBlend
{
    public static uint OverOpaque(uint ink, uint board)
    {
        var a = (int)(ink >>> 24);
        // a≤1 都算"这里没有墨"：1 是空白像素的命中测试哨兵（<c>LayeredCanvasWindow.BlankPixel</c>），
        // 不是画上去的浓度。若只认 a==0，整块白板会停在 254 而不是 255——"这是一张干净的白纸"正是这块底的全部意义。
        if (a <= 1) return board;
        if (a == 255) return ink | 0xFF000000u;                   // 实心墨：一点底都不掺
        var inv = 255 - a;
        var b = Mix(ink & 0xFF, board & 0xFF, inv);
        var g = Mix((ink >> 8) & 0xFF, (board >> 8) & 0xFF, inv);
        var r = Mix((ink >> 16) & 0xFF, (board >> 16) & 0xFF, inv);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    private static uint Mix(uint inkChannel, uint boardChannel, int inv)
    {
        var t = boardChannel * (uint)inv;
        return inkChannel + ((t + (t >> 8) + 127) >> 8);
    }
}
