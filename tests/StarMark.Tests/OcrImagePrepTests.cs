#nullable enable
using System;
using System.Collections.Generic;
using Xunit;
using StarMark.Abstractions.Capture;
using StarMark.Core.Ocr;

namespace StarMark.Tests;

/// <summary>
/// 识字前预处理与"要不要换语言"的判据测试。
/// <para>
/// 这些数字会直接决定引擎看到什么：放大倍数选小了字还是糊的，选大了只是慢；
/// 对比拉伸的边界情况（整块同色）如果处理错，会把一块浅灰底拉成全黑，
/// 而贴图传进来的就是它正在显示的那份像素——识别一次，图就黑了。
/// </para>
/// </summary>
public sealed class OcrImagePrepTests
{
    private static byte[] Block(int width, int height, byte gray, byte alpha = 0)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = gray;
            bgra[i * 4 + 1] = gray;
            bgra[i * 4 + 2] = gray;
            bgra[i * 4 + 3] = alpha;
        }
        return bgra;
    }

    // ────────── 放大倍数 ──────────

    [Theory]
    [InlineData(1920, 1200, 1)]     // 整屏：长边已超过目标，再放大只是慢
    [InlineData(2000, 2000, 1)]
    [InlineData(1600, 900, 1)]      // 正好卡在目标上：×2 就超了
    [InlineData(800, 600, 2)]
    [InlineData(700, 700, 2)]
    [InlineData(300, 200, 4)]       // 小区域直接顶到倍数上限（档位在 4 处收住，防止无意义的大图）
    [InlineData(100, 50, 4)]
    [InlineData(0, 100, 1)]
    [InlineData(-5, 100, 1)]
    public void UpscaleFactor_PicksTheLargestIntegerThatFits(int width, int height, int expected)
        => Assert.Equal(expected, OcrImagePrep.UpscaleFactor(width, height));

    [Fact]
    public void UpscaleFactor_RespectsThePixelBudgetEvenWhenTheTargetAllowsMore()
    {
        // 长边放到 8000 时，1000×1000 只能 ×2（4M 像素上限），×3 就 9M 了
        Assert.Equal(2, OcrImagePrep.UpscaleFactor(1000, 1000, targetLongSide: 8000, maxPixels: 4_000_000));
        Assert.Equal(4, OcrImagePrep.UpscaleFactor(200, 200, targetLongSide: 8000, maxPixels: 4_000_000));
    }

    [Fact]
    public void UpscaleFactor_CeilsAtTheFactorThatMeasuredBest()
    {
        // 把另外两道限制都放开，剩下的就只有倍数上限——这条钉的是"4 是量出来的，不是顺手写的"。
        // 探针实测：14/18px 桌面文字按 4× 平均认对 89.9%，上限改 8× 反而掉到 81.4%（方块化的笔画被读成形近字）。
        Assert.Equal(4, OcrImagePrep.MaxUpscaleFactor);
        Assert.Equal(OcrImagePrep.MaxUpscaleFactor,
            OcrImagePrep.UpscaleFactor(100, 50, targetLongSide: 100_000, maxPixels: int.MaxValue));
    }

    [Fact]
    public void Upscale_FactorOne_ReturnsTheSameArray()
    {
        // 不复制是刻意的（整屏多拷一份就是十几 MB），所以调用方若要就地改像素必须先自己 Clone
        var pixels = Block(4, 3, 90);
        var (bgra, w, h) = OcrImagePrep.Upscale(pixels, 4, 3, 1);
        Assert.Same(pixels, bgra);
        Assert.Equal(4, w);
        Assert.Equal(3, h);
    }

    [Fact]
    public void Upscale_NearestNeighbourDuplicatesEveryPixelAndForcesOpaque()
    {
        var bgra = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };   // 2×1，alpha 全是 GDI 那种 0
        var (outBuf, w, h) = OcrImagePrep.Upscale(bgra, 2, 1, 2);
        Assert.Equal(4, w);
        Assert.Equal(2, h);
        Assert.Equal(new byte[]
        {
            10, 20, 30, 255, 10, 20, 30, 255, 40, 50, 60, 255, 40, 50, 60, 255,   // 第一行
            10, 20, 30, 255, 10, 20, 30, 255, 40, 50, 60, 255, 40, 50, 60, 255,   // 第二行＝第一行的复制
        }, outBuf);
    }

    [Fact]
    public void Upscale_RejectsShortBuffersAndAbsurdSizes()
    {
        Assert.Throws<ArgumentException>(() => OcrImagePrep.Upscale(new byte[10], 4, 4, 2));
        // 10000×10000 再 ×8 = 25.6GB：要在分配之前就说不行，而不是把进程 OOM 掉
        Assert.Throws<OutOfMemoryException>(() => OcrImagePrep.Upscale(Block(10000, 10000, 128), 10000, 10000, 8));
    }

    // ────────── 对比拉伸 ──────────

    [Fact]
    public void StretchContrast_ExpandsANarrowRangeToFullScale()
    {
        // 低对比度 UI 文字（100..150 那档灰）应被拉成 0..255。
        // 100 个像素里放一个亮点：1% 分位截断下它仍然成为上界，灰底整片压到纯黑。
        var pixels = Block(10, 10, 100);
        pixels[0] = 150; pixels[1] = 150; pixels[2] = 150;
        OcrImagePrep.StretchContrast(pixels, 10, 10);
        Assert.Equal(255, pixels[0]);        // 亮点成了纯白
        Assert.Equal(0, pixels[8]);          // 灰底成了纯黑（100 正好落在下界上）
        Assert.Equal(255, pixels[3]);        // alpha 一律写回不透明
        Assert.Equal(pixels[8], pixels[8 + 1]);
        Assert.Equal(pixels[8], pixels[8 + 2]);   // 灰必须三通道相同
    }

    [Fact]
    public void StretchContrast_LeavesAUniformBlockAlone()
    {
        // 整块同色没有对比可拉。硬映射会把它变成全黑——贴图传的就是它正在显示的那份像素，
        // 于是"点一下识字"会让一张浅灰截图变黑。这条断言钉的就是那个反直觉的边界。
        var pixels = Block(6, 6, 128, alpha: 255);
        OcrImagePrep.StretchContrast(pixels, 6, 6);
        Assert.Equal(128, pixels[0]);
        Assert.Equal(128, pixels[35 * 4 + 2]);
    }

    [Fact]
    public void StretchContrast_WritesGreyIntoAllThreeChannelsAndOpaqueAlpha()
    {
        var pixels = new byte[4 * 4];                 // 2×2 全 0
        for (var i = 0; i < 4; i++) { pixels[i * 4 + 2] = 200; pixels[i * 4 + 3] = 0; }
        OcrImagePrep.StretchContrast(pixels, 2, 2);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(pixels[i * 4], pixels[i * 4 + 1]);
            Assert.Equal(pixels[i * 4], pixels[i * 4 + 2]);
            Assert.Equal(255, pixels[i * 4 + 3]);     // 交出去必须是 alpha=255，否则等于又造一张透明图
        }
    }

    [Fact]
    public void StretchContrast_ToleratesBadInputWithoutThrowing()
    {
        OcrImagePrep.StretchContrast(Array.Empty<byte>(), 0, 0);
        OcrImagePrep.StretchContrast(new byte[8], 10, 10);      // 缓冲比声明的小 ⇒ 直接不动，别越界
    }

    // ────────── 补边 ──────────

    [Fact]
    public void Pad_ExpandsOnAllSides()
    {
        var padded = OcrImagePrep.Pad(new IntRect(100, 100, 50, 20), new IntRect(0, 0, 4000, 2000), pad: 8);
        Assert.Equal(new IntRect(92, 92, 66, 36), padded);
    }

    [Fact]
    public void Pad_ClampsToTheCapturedFrame()
    {
        // 屏角上的选区补边不能补到帧外：那样裁剪偏移会变负，读到的是上一行的像素
        var padded = OcrImagePrep.Pad(new IntRect(0, 0, 40, 20), new IntRect(0, 0, 100, 100), pad: 8);
        Assert.Equal(new IntRect(0, 0, 48, 28), padded);
    }

    [Fact]
    public void Pad_KeepsTheOriginalWhenTheExpandedBoxStillIntersectsNothing()
    {
        var selection = new IntRect(500, 500, 10, 10);
        Assert.Equal(selection, OcrImagePrep.Pad(selection, new IntRect(0, 0, 100, 100), pad: 8));
    }

    [Fact]
    public void Pad_RespectsNegativeOriginsOnSecondaryMonitors()
    {
        // 副屏在主屏左边时虚拟桌面原点就是负的，补边必须照样成立
        var padded = OcrImagePrep.Pad(new IntRect(-800, 100, 30, 30), new IntRect(-1920, 0, 1920, 1080), pad: 8);
        Assert.Equal(new IntRect(-808, 92, 46, 46), padded);
    }

    // ────────── 弱结果与换语言 ──────────

    [Theory]
    [InlineData(0, 5_000_000, true)]       // 一整屏却一个字没认出 ⇒ 大概率语种不对
    [InlineData(3, 4_096, true)]
    [InlineData(4, 5_000_000, false)]      // 够阈值就不再折腾
    [InlineData(0, 100, false)]            // 区域本来就小，换语言只是白等
    [InlineData(2, 4_095, false)]
    public void ShouldRetry_OnlyWhenTheRegionIsBigButEmpty(int chars, long pixels, bool expected)
        => Assert.Equal(expected, OcrImagePrep.ShouldRetryWithOtherLanguage(chars, pixels));

    [Fact]
    public void BestCandidate_PrefersMoreCharactersAndKeepsTheFirstOnTies()
    {
        Assert.Equal(-1, OcrImagePrep.BestCandidateIndex(Array.Empty<OcrImagePrep.OcrCandidate>()));
        var same = new[]
        {
            new OcrImagePrep.OcrCandidate("zh-Hans-CN", 10, "a"),
            new OcrImagePrep.OcrCandidate("en-US", 10, "b"),
        };
        Assert.Equal(0, OcrImagePrep.BestCandidateIndex(same));      // 并列 ⇒ 保持用户配置语言，不多此一举
        var later = new[]
        {
            new OcrImagePrep.OcrCandidate("zh-Hans-CN", 2, "a"),
            new OcrImagePrep.OcrCandidate("en-US", 80, "b"),
            new OcrImagePrep.OcrCandidate("ja", 30, "c"),
        };
        Assert.Equal(1, OcrImagePrep.BestCandidateIndex(later));
    }
}
