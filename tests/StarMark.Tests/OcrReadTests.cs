#nullable enable
using StarMark.Core.Ocr;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「这次是怎么认的」那句话的逐字契约。
/// <para>
/// 它是托盘提示里唯一能解释"为什么认错了字"的东西，所以三种情形必须各钉一个：
/// 什么都没折腾 ⇒ <b>整段为空</b>（调用方据此不画空括号）；放大了 ⇒ 只多这一段；换过语言 ⇒ 说清换了。
/// </para>
/// </summary>
public sealed class OcrReadTests
{
    private static OcrRead Read(string? lang = "zh-Hans-CN", int upscale = 1, bool swapped = false)
        => new() { Ok = true, Language = lang, Upscale = upscale, SwappedLanguage = swapped };

    [Fact]
    public void NothingUnusual_SaysNothingAtAll()
    {
        // 按用户配置语言、没放大、没换语言 ⇒ 没有可交代的：不能留下"（按 …）"这种自相矛盾的壳
        var read = Read(upscale: 1);
        Assert.Equal("按 zh-Hans-CN", read.How);
        Assert.Equal("（按 zh-Hans-CN）", read.HowParenthesized);
        Assert.Equal(string.Empty, Read(lang: null, upscale: 1).How);
        Assert.Equal(string.Empty, Read(lang: null, upscale: 1).HowParenthesized);
    }

    [Theory]
    [InlineData(1, "按 zh-Hans-CN")]
    [InlineData(2, "按 zh-Hans-CN，放大 2×")]
    [InlineData(4, "按 zh-Hans-CN，放大 4×")]
    public void UpscaleOnlyMentionsItWhenItActuallyHappened(int upscale, string expected)
        => Assert.Equal(expected, Read(upscale: upscale).How);

    [Fact]
    public void LanguageSwapIsNamedTogetherWithTheLanguageThatWon()
    {
        Assert.Equal("按 en-US，放大 2×，已换语言", Read("en-US", 2, swapped: true).How);
        // 换过语言却没放大：不能凭空冒出"放大"那一段
        Assert.Equal("按 ja，已换语言", Read("ja", 1, swapped: true).How);
    }

    /// <summary>
    /// 分隔符由拼接处放一次，⛔ 写在每颗片段开头（批次 WI-46b 把它交进 `ocr.how.join` 时才改的这个形状）。
    /// <para>旧写法下"引擎没报语言、又放大了"那一格会输出「，放大 2×」——屏幕上开头多一颗逗号，
    /// 而那一格以前没有任何测试，所以它一直活着。</para>
    /// </summary>
    [Fact]
    public void SeparatorsBelongToTheJoin_NotToTheFragments()
    {
        Assert.Equal("放大 2×", Read(lang: null, upscale: 2).How);
        Assert.Equal("（放大 2×）", Read(lang: null, upscale: 2).HowParenthesized);
        Assert.Equal("已换语言", Read(lang: null, upscale: 1, swapped: true).How);
    }

    [Fact]
    public void FailKeepsTheReasonAndTheLanguageSeparate()
    {
        var read = OcrRead.Fail("这台 Windows 没有安装任何文字识别（OCR）语言包", "zh-Hans-CN");
        Assert.False(read.Ok);
        Assert.Equal("这台 Windows 没有安装任何文字识别（OCR）语言包", read.Error);
        Assert.Equal("按 zh-Hans-CN", read.How);
        Assert.Equal(string.Empty, read.Text);
        Assert.Equal(0, read.Chars);
    }
}
