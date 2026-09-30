#nullable enable
using System;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「那张图的文件不在了」这一族话的逐字契约（批次 SI，P-131 清单 #6）。
/// <para>为什么这一批终于能逐字钉：句子本体从 <c>StarMark.UI</c> 搬进了 Abstractions——
/// 测试工程引用不到 UI（#184），所以此前这几句只有"源码里含不含这串字"的形状闸门，
/// 而形状闸门看不见读数：改了措辞它红，改错了字面它可能反而绿。搬进来之后读数有了真测。</para>
/// <para>钉住的东西：① 事实那半句只有一个出处；② 每个宿主各带自己那份出口（P-54——坏消息要给出口，
/// 而"可置顶或删除"与"可直接删掉这一条"分别是那一页与那一格的出口，并成一句就会少一半）。</para>
/// </summary>
public sealed class ClipboardMissingImageTextTests
{
    /// <summary>事实那半句逐字：它同时是「复制图片」交出去的原因，真机点验单认的就是这几个字。</summary>
    [Fact]
    public void TheClauseIsTheBareFactWithoutSubjectOrCause()
        => Assert.Equal("文件已不在本机", ClipboardPolicy.MissingFileClause);

    [Fact]
    public void ReuseStatusSaysTheFactThenTheConsequenceThenAnExit()
        => Assert.Equal("这条图片的文件已不在本机，复制不回去（条目仍保留，可置顶或删除）",
            ClipboardPolicy.DescribeMissingImageForReuse());

    [Fact]
    public void BannerSaysTheFactThenKeepsTheEntryAndTheExit()
        => Assert.Equal("图片文件已不在本机（条目仍保留，可直接删掉这一条；这一张复制不回去）",
            ClipboardPolicy.DescribeMissingImageBanner());

    /// <summary>两句都含那一颗事实——这是"并成一处"的全部含义：改事实，四处一起改（逐字读数由上面两条钉）。</summary>
    [Fact]
    public void BothHostSentencesCarryTheOneClauseVerbatim()
    {
        Assert.Contains(ClipboardPolicy.MissingFileClause,
            ClipboardPolicy.DescribeMissingImageForReuse(), StringComparison.Ordinal);
        Assert.Contains(ClipboardPolicy.MissingFileClause,
            ClipboardPolicy.DescribeMissingImageBanner(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 两句<b>不许并成一句</b>：各自那句出口是那一格宿主独有的信息（状态行要说"还能置顶或删除"，
    /// 卡片那一格要说"这一条可以直接删"）。并成一句就会有一处的出口消失——那是要往回退的改法。
    /// </summary>
    [Fact]
    public void TheTwoHostSentencesStayTwoBecauseTheirExitsDiffer()
    {
        var reuse = ClipboardPolicy.DescribeMissingImageForReuse();
        var banner = ClipboardPolicy.DescribeMissingImageBanner();
        Assert.NotEqual(reuse, banner);
        Assert.Contains("可置顶或删除", reuse, StringComparison.Ordinal);
        Assert.Contains("可直接删掉这一条", banner, StringComparison.Ordinal);
    }

    /// <summary>坏消息不许推回给用户去做什么（P-54）：不许"请…/重试/重启/确认"。</summary>
    [Fact]
    public void NeitherSentenceAsksTheUserToDoSomething()
    {
        foreach (var sentence in new[]
                 {
                     ClipboardPolicy.DescribeMissingImageForReuse(),
                     ClipboardPolicy.DescribeMissingImageBanner(),
                 })
        {
            Assert.DoesNotContain("请", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("重试", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("重启", sentence, StringComparison.Ordinal);
        }
    }

    /// <summary>也不许漏出系统原话：那两句在界面上都像程序坏了（与读取侧"先 stat 再读"同一条理由）。</summary>
    [Fact]
    public void NeitherSentenceLeaksASystemMessage()
    {
        Assert.DoesNotContain("Exception", ClipboardPolicy.DescribeMissingImageForReuse(), StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", ClipboardPolicy.DescribeMissingImageBanner(), StringComparison.Ordinal);
    }

    /// <summary>句子是纯函数：同一个输入永远同一句（没有文化、没有时态、没有当场盘查）。</summary>
    [Fact]
    public void TheSentencesDoNotReadTheDiskOrCulture()
    {
        Assert.Equal(ClipboardPolicy.DescribeMissingImageForReuse(), ClipboardPolicy.DescribeMissingImageForReuse());
        Assert.Equal(ClipboardPolicy.DescribeMissingImageBanner(), ClipboardPolicy.DescribeMissingImageBanner());
    }
}
