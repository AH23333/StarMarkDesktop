#nullable enable
using System;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「那张图的文件不在了」这一族话的逐字契约（批次 SI，P-131 清单 #6）。
/// <para>为什么这一批能逐字钉：句子本体从 <c>StarMark.UI</c> 搬进了 Abstractions——
/// 测试工程引用不到 UI（#184），所以此前这几句只有"源码里含不含这串字"的形状闸门，
/// 而形状闸门看不见读数：改了措辞它红，改错了字面它可能反而绿。搬进来之后读数有了真测。</para>
/// <para>钉住的东西：① 事实那半句只有一个出处；② 每个宿主各带自己那份出口（P-54——坏消息要给出口，
/// 而"可置顶或删除"与"可直接删掉这一条"分别是那一页与那一格的出口，并成一句就会少一半）。</para>
/// <para>批次 WI-65 片5a 起这三颗政策句交的是<b>还没取词的</b>「键＋实参」⇒ 本类的读数断言一律
/// 先 <c>.Render()</c>，并且⛔ 拿两颗 <see cref="LocMessage"/> 对象比相等／不相等：那颗 record 的第二颗
/// 成员是数组，而数组按<b>引用</b>比 ⇒ 两条永不相等，"不许并成一句"那条会静默空转（下面的断言都只比读数）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class ClipboardMissingImageTextTests : IDisposable
{
    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    /// <summary>事实那半句逐字：它同时是「复制图片」交出去的原因，真机点验单认的就是这几个字。</summary>
    [Fact]
    public void TheClauseIsTheBareFactWithoutSubjectOrCause()
        => Assert.Equal("文件已不在本机", ClipboardPolicy.MissingFileClause.Render());

    [Fact]
    public void ReuseStatusSaysTheFactThenTheConsequenceThenAnExit()
        => Assert.Equal("这条图片的文件已不在本机，复制不回去（条目仍保留，可置顶或删除）",
            ClipboardPolicy.DescribeMissingImageForReuse().Render());

    [Fact]
    public void BannerSaysTheFactThenKeepsTheEntryAndTheExit()
        => Assert.Equal("图片文件已不在本机（条目仍保留，可直接删掉这一条；这一张复制不回去）",
            ClipboardPolicy.DescribeMissingImageBanner().Render());

    /// <summary>两句都含那一颗事实——这是"并成一处"的全部含义：改事实，四处一起改（逐字读数由上面三条钉）。</summary>
    [Fact]
    public void BothHostSentencesCarryTheOneClauseVerbatim()
    {
        Assert.Contains(ClipboardPolicy.MissingFileClause.Render(),
            ClipboardPolicy.DescribeMissingImageForReuse().Render(), StringComparison.Ordinal);
        Assert.Contains(ClipboardPolicy.MissingFileClause.Render(),
            ClipboardPolicy.DescribeMissingImageBanner().Render(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 两句<b>不许并成一句</b>：各自那句出口是那一格宿主独有的信息（状态行要说"还能置顶或删除"，
    /// 卡片那一格要说"这一条可以直接删"）。并成一句就会有一处的出口消失——那是要往回退的改法。
    /// </summary>
    [Fact]
    public void TheTwoHostSentencesStayTwoBecauseTheirExitsDiffer()
    {
        var reuse = ClipboardPolicy.DescribeMissingImageForReuse().Render();
        var banner = ClipboardPolicy.DescribeMissingImageBanner().Render();
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
                     ClipboardPolicy.DescribeMissingImageForReuse().Render(),
                     ClipboardPolicy.DescribeMissingImageBanner().Render(),
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
        Assert.DoesNotContain("Exception", ClipboardPolicy.DescribeMissingImageForReuse().Render(), StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", ClipboardPolicy.DescribeMissingImageBanner().Render(), StringComparison.Ordinal);
    }

    /// <summary>句子是纯函数：⛔ 碰盘、⛔ 看时钟，同一个输入永远同一句。</summary>
    [Fact]
    public void TheSentencesDoNotReadTheDisk()
    {
        Assert.Equal(ClipboardPolicy.DescribeMissingImageForReuse().Render(),
                     ClipboardPolicy.DescribeMissingImageForReuse().Render());
        Assert.Equal(ClipboardPolicy.DescribeMissingImageBanner().Render(),
                     ClipboardPolicy.DescribeMissingImageBanner().Render());
    }

    /// <summary>
    /// ✅WI-65 片5a 的落点：政策层交出来的是<b>还没取词的那句</b>。
    /// <para>三件事一起钉，缺一件都算没落地：① 外层那颗的 <c>{0}</c> 槽里放的必须是<b>键</b>
    /// （<see cref="LocMessage"/>），⛔ 已经念完的字符串——那是 #393／P-196 那一族"塞进槽的那半句停在造串那一刻"；
    /// ② 先造好、<b>再</b>换语言、然后贴：整句（外层与槽里那半句）要一起跟着新语言走；
    /// ③ 换语言之后再新造一颗，读数必须与②那颗<b>逐字相同</b>——不相同就是"同一句话两个主人"。</para>
    /// <para>⚠ ②③ 的次序是这一格唯一的证人价值所在（反向台架 <c>bench_65g_r1</c> 的 A1 就是这么被抓出来的）：
    /// 每换一次语言<b>再</b>现造一句，那么"槽里塞回成品串"的坏形也照样每句都对（造串那一刻语言已经是对的），
    /// 这一格就变成一颗永远绿的空闸门。必须<b>造在前、换在中、贴在后</b>。</para>
    /// </summary>
    [Fact]
    public void ThePolicyHandsOutTheSentenceBeforeItIsReadAndBothLayersFollowTheLanguage()
    {
        var banner = ClipboardPolicy.DescribeMissingImageBanner();
        Assert.Equal("clip.miss.banner", banner.Key);
        var inner = Assert.IsType<LocMessage>(Assert.Single(banner.Args!));
        Assert.Equal("clip.miss.clause", inner.Key);

        Locale.TrySet(AppLanguage.Chinese);
        var builtWhileChinese = ClipboardPolicy.DescribeMissingImageForReuse();   // 造串在这一拍
        var zh = builtWhileChinese.Render();
        Assert.Contains("文件已不在本机", zh, StringComparison.Ordinal);

        Locale.TrySet(AppLanguage.English);                                        // 换语言发生在贴之前
        var en = builtWhileChinese.Render();                                       // 还是那颗旧对象
        Assert.Contains("no longer on this device", en, StringComparison.Ordinal);
        Assert.DoesNotContain("文件", en, StringComparison.Ordinal);               // 槽里塞回成品串时，这里就是半中半英
        Assert.Equal(en, ClipboardPolicy.DescribeMissingImageForReuse().Render()); // 旧那颗与新造那颗同形＝话只有一个主人

        Locale.TrySet(AppLanguage.Japanese);
        Assert.Equal(3, new[] { zh, en, builtWhileChinese.Render() }.Distinct().Count());
    }
}
