#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;

namespace StarMark.Core.Ocr;

/// <summary>
/// 交给 OCR 引擎之前的图像预处理，以及"这次结果到底算不算太弱"的判据。
/// <para>
/// 为什么要预处理：<c>Windows.Media.Ocr</c> 在**小字号**上错得最多，而桌面截图里绝大部分文字正是
/// 12–14px 的小字；贴边的字形也会被切掉半笔。三件事能实测地改善：补边、按目标长边整数倍放大、
/// 灰度并把对比度拉到全量程（屏幕文字常有低对比度的浅灰底）。
/// </para>
/// <para>
/// 全部是纯像素/整数运算，没有 WinRT 依赖 ⇒ 可单测。放大用最近邻：这里要的是"同样的笔画变粗"，
/// 不是"看着更顺眼的插值"，双线性会把边缘抹糊反而更难认。
/// </para>
/// </summary>
public static class OcrImagePrep
{
    /// <summary>放大后想达到的长边（经验值：再大对识别没好处，只是更慢）。</summary>
    public const int TargetLongSide = 1600;

    /// <summary>放大后的像素总量上限。4M 像素 ≈ BGRA 16MB，一次识别的合理内存上界。</summary>
    public const int MaxScaledPixels = 4_000_000;

    /// <summary>
    /// 最多放大几倍。<b>4 不是猜的</b>：冒烟探针（<c>StarMark.SmokeTest ocr</c>）拿"知道正确答案的"
    /// 14/18px 桌面文字量过——按 4× 平均认对 89.9%，把上限提到 8× 反而掉到 81.4%
    /// （最近邻放得过大，笔画变成方块，引擎的分词开始把「设」读成「浯」这类形近字）。
    /// 也就是说小字号要靠放大救，但**不是越大越好**。
    /// </summary>
    public const int MaxUpscaleFactor = 4;

    /// <summary>选区四周补多少物理像素：够躲开"字被切边"，又不至于把邻居行的字卷进来。</summary>
    public const int EdgePadPixels = 8;

    /// <summary>少于这么多有内容的字符，就认为这次识别可能选错了语言，值得换一种再试。</summary>
    public const int WeakCharThreshold = 4;

    /// <summary>小到这里没必要换语言重试（本来就没几个字，重试只是白等）。</summary>
    public const long RetryWorthwhilePixels = 4096;

    /// <summary>
    /// 该放大几倍。取"长边不超过 <see cref="TargetLongSide"/> 且总像素不超过 <see cref="MaxScaledPixels"/>"
    /// 允许的最大整数倍，下限 1 倍、上限 <see cref="MaxUpscaleFactor"/> 倍。<b>只用整数倍</b>：非整数倍会引入插值假边，而这里没有插值的余地。
    /// </summary>
    public static int UpscaleFactor(int width, int height, int targetLongSide = TargetLongSide, int maxPixels = MaxScaledPixels)
    {
        if (width <= 0 || height <= 0) return 1;
        var longSide = Math.Max(width, height);
        var factor = 1;
        while (factor < MaxUpscaleFactor
               && (long)longSide * (factor + 1) <= targetLongSide
               && (long)width * (factor + 1) * ((long)height * (factor + 1)) <= maxPixels)
            factor++;
        return factor;
    }

    /// <summary>
    /// 最近邻整数倍放大。factor ≤ 1 时<b>返回原数组本身</b>（不复制：调用方常传整屏，多拷一份就是十几 MB）。
    /// </summary>
    public static (byte[] Bgra, int Width, int Height) Upscale(byte[] bgra, int width, int height, int factor)
    {
        if (factor <= 1) return (bgra, width, height);
        if (bgra is null || bgra.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲比声明的尺寸短，放大将读到越界数据", nameof(bgra));

        var ow = width * factor;
        var oh = height * factor;
        var need = (long)ow * oh * 4;
        if (need > 512L * 1024 * 1024)
            throw new OutOfMemoryException($"放大到 {ow} × {oh} 需要 {need / 1024 / 1024}MB，超出单次识别的内存上界");
        var output = new byte[(int)need];
        for (var y = 0; y < oh; y++)
        {
            var srcRow = y / factor * width * 4;
            var dstRow = y * ow * 4;
            for (var x = 0; x < ow; x++)
            {
                var s = srcRow + x / factor * 4;
                var d = dstRow + x * 4;
                output[d] = bgra[s];
                output[d + 1] = bgra[s + 1];
                output[d + 2] = bgra[s + 2];
                output[d + 3] = 255;      // 放大后必须仍是不透明：GDI 抓来的 alpha 是 0
            }
        }
        return (output, ow, oh);
    }

    /// <summary>
    /// 就地转灰度并做百分位对比拉伸（各削 1% 的最暗/最亮像素，避免一小块纯黑/纯白把量程吃掉）。
    /// 输出写成 BGR 三通道相同的"彩色灰"，因为引擎按 Bgra8 收，且省掉一次格式转换的边界情况。
    /// </summary>
    public static void StretchContrast(byte[] bgra, int width, int height)
    {
        if (bgra is null || width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4) return;
        var count = width * height;
        var luma = new byte[count];
        var histogram = new int[256];
        for (var i = 0; i < count; i++)
        {
            var p = i * 4;
            // Rec.601（与显示侧一致）：绿权重最大，灰底细线不会被算成"几乎全黑"
            var v = (byte)((bgra[p] * 114 + bgra[p + 1] * 587 + bgra[p + 2] * 299) / 1000);
            luma[i] = v;
            histogram[v]++;
        }

        var cut = count / 100;                       // 1% 分位
        var lo = 0;
        var seen = 0;
        while (lo < 255 && (seen += histogram[lo]) < cut) lo++;
        var hi = 255;
        seen = 0;
        while (hi > lo && (seen += histogram[hi]) < cut) hi--;
        // 整块同色（纯色底、空白区）时没什么可拉伸的：直接返回。
        // 硬映射会把一块 128 灰"拉"成全黑——引擎不会因此多认出字，贴图却会因此变黑。
        if (hi <= lo) return;
        var span = hi - lo;

        for (var i = 0; i < count; i++)
        {
            var mapped = (luma[i] - lo) * 255 / span;
            if (mapped < 0) mapped = 0; else if (mapped > 255) mapped = 255;
            var value = (byte)mapped;
            var p = i * 4;
            bgra[p] = value;
            bgra[p + 1] = value;
            bgra[p + 2] = value;
            bgra[p + 3] = 255;
        }
    }

    /// <summary>把选区向四周补边并夹回可截范围（贴边字形被切掉半笔是引擎读错的首要来源之一）。</summary>
    public static IntRect Pad(IntRect selection, IntRect bounds, int pad = EdgePadPixels)
    {
        var expanded = new IntRect(selection.X - pad, selection.Y - pad, selection.Width + pad * 2, selection.Height + pad * 2);
        return CaptureGeometry.Intersect(expanded, bounds) ?? selection;
    }

    /// <summary>
    /// 这次结果是否弱到值得换一种语言再试一次。
    /// <b>只在画面足够大、却几乎没认出字时才换</b>：截两个字的区域本来就少，重试是白等；
    /// 而截了一整屏却只出 2 个字，多半是引擎语言与文字语种不符。
    /// </summary>
    public static bool ShouldRetryWithOtherLanguage(int meaningfulChars, long sourcePixels)
        => meaningfulChars < WeakCharThreshold && sourcePixels >= RetryWorthwhilePixels;

    /// <summary>
    /// 多语言候选里挑一个：取"有内容的字符最多"的那个，并列时先来的赢（=优先用户配置语言）。
    /// 没有别的度量可用——引擎不输出置信度，字数是唯一能算的粗尺子。
    /// </summary>
    public static int BestCandidateIndex(IReadOnlyList<OcrCandidate> candidates)
    {
        var best = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (best < 0 || candidates[i].MeaningfulChars > candidates[best].MeaningfulChars) best = i;
        }
        return best;
    }

    /// <summary>一次识别的候选（语言 + 它认出的字数 + 拼好的文本）。</summary>
    public sealed record OcrCandidate(string? Language, int MeaningfulChars, string Text);
}
