#nullable enable
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// <b>"两条链各有逐像素参照"这条前提的守门</b>（批次 S2-d 第二刀改判时立）。
/// <para>
/// 准绳 §3.4 要求渲放合并"字节一致"，而这句话只有在<b>两边各有一份逐像素参照实现</b>时才是可执行的验收；
/// 否则"一致"就退化成"看起来一样"（批次 XU／S2-d 第一刀反复遇到的同一类问题）。
/// 第二刀读完两条链之后定的结论是<b>不在 S2 合并</b>（四条轴无共享量，理由写在 §14.1 的改判条目里），
/// 但那份前提必须活到 S3——所以这里钉住它，别让"清理测试文件"顺手把那次合并的最后一点保险拆掉。
/// </para>
/// </summary>
public sealed class PixelReferenceGateTests
{
    private const string CanvasFile = "tests/StarMark.Tests/CanvasKernelIdentityTests.cs";
    private const string CaptureFile = "tests/StarMark.Tests/CaptureKernelIdentityTests.cs";

    /// <summary>画布那份参照还在，并且真在整张缓冲上逐像素比（`AssertSame(want, got, …)`）。</summary>
    [Fact]
    public void TheCanvasChainKeepsItsPixelReference()
    {
        var code = ReadRepoFile(CanvasFile);
        Assert.True(Count(code, "AssertSame(want, got,") >= 4,
            "整张缓冲的逐像素比较少于 4 处 ⇒ 这份文件大概已经不做对差分了");
        Assert.True(Count(code, "Ref(") >= 5, "参照实现（…Ref 那几颗函数）不见了＝对差分成了自映射");
    }

    /// <summary>截图那份参照同样在，且比较的是整份字节缓冲。</summary>
    [Fact]
    public void TheCaptureChainKeepsItsPixelReference()
    {
        var code = ReadRepoFile(CaptureFile);
        Assert.True(Count(code, "Assert.Equal(expected, actual)") >= 2, "没在比整张缓冲");
        Assert.True(Count(code, "Ref(") >= 6, "参照实现（…Ref 那几颗函数）不见了＝对差分成了自映射");
    }

    /// <summary>
    /// 截图那份参照不许被"顺手优化"：它复刻的是今天的算式，包括硬边判据、三通道整数混合、
    /// 半透明笔"每像素只混合一次"的掩码，以及椭圆按周长估的采样数。
    /// 哪天参照被改了，<see cref="CaptureKernelIdentityTests"/> 就变成两份新代码互相印证，等于没有证据。
    /// </summary>
    [Fact]
    public void TheCaptureReferenceKeepsTheFormulasItIsSupposedToReproduce()
    {
        var code = ReadRepoFile(CaptureFile);
        Assert.Contains("if (ddx * ddx + ddy * ddy > r2) continue;", code);
        Assert.Contains("bgra[p + channel] = (byte)((bgra[p + channel] * keep + src * alpha) / 255);", code);
        Assert.Contains("if (painted[cell] != 0) continue;", code);
        Assert.Contains("Math.Clamp((int)(Math.PI * Math.Sqrt(2 * (rx * rx + ry * ry))), 64, 4000)", code);
    }

    /// <summary>
    /// 生产侧仍是两份逐像素渲染（第二刀改判的现状登记）。<b>哪天真去合并了，这条要连同 §14.1 那条改判一起改</b>——
    /// 它存在的意义就是让那次合并留下痕迹，而不是悄悄发生。
    /// </summary>
    [Fact]
    public void TheTwoRenderersAreStillTwoUntilS3DoesTheMerge()
    {
        Assert.Contains("private static void Disc(",
            ReadRepoFile("src/StarMark.Core/Capture/AnnotationPainter.cs"));
        Assert.Contains("private static void PaintDisc(",
            ReadRepoFile("src/StarMark.Core/Canvas/CanvasCompositor.cs"));
        // 两条链共享的只有步进这一处（S2-d 第一刀的落点）：渲染器里不许再出现第二份内联步进。
        Assert.DoesNotContain("Math.Max(Math.Abs",
            ReadRepoFile("src/StarMark.Core/Capture/AnnotationPainter.cs"));
        Assert.DoesNotContain("Math.Max(Math.Abs",
            ReadRepoFile("src/StarMark.Core/Canvas/CanvasCompositor.cs"));
    }
}
