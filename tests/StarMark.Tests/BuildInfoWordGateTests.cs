#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 诊断面板那一行构建读数的形状闸门（批次 WI-74，P-210 落地腿第一刀）。
/// <para>
/// <b>为什么钉这一格</b>：<c>BuildInfo.Display</c> 的兜底句「未知（读不到可执行文件的写入时间）」是<b>汉字写死在
/// Abstractions 里</b>，由 <c>DiagnosticsService</c> 塞进 <c>DiagnosticEntry.Fact(…)</c> 直接上屏 ⇒ 英日界面下
/// 「你刚才跑的那次含不含最新改动」那一格会给出一句⛔ 认得的字。它躲过了历次数汉字的尺（射程＝<c>src/StarMark.UI</c>），
/// 也躲过了死键闸门（那一句⛔ 在表里，所以没有"某行没人问"可报）——正是 ✅P-197 那一族的形状（<c>bin/WI72/rows_72.py</c>
/// 第一次扫整层时把它读成"上屏"，见 ✅P-210 的现读名单）。
/// </para>
/// <para>取词时机＝每次采集当场读（<c>Locale.T</c>），与 <c>DateTimeText</c>、那颗 <c>hotkey.unbound</c> 同一口径；
/// ⛔ 把键名提前念完再冻住（P-184 那条）。</para>
/// </summary>
public class BuildInfoWordGateTests
{
    private const string Build = "src/StarMark.Abstractions/BuildInfo.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Service = "src/StarMark.Core/Diagnostics/DiagnosticsService.cs";
    private const string Key = "diag.value.buildUnknown";

    private static readonly Regex Word = new(@"[぀-ヿ㐀-䶿一-鿿]", RegexOptions.Compiled);

    /// <summary>
    /// ① 那一档的<b>字符串字面量</b>里一颗汉字／假名也没有。同一格递进一枚<b>正件</b>：同一把尺在
    /// <c>RssFeed.cs</c> 必须读出「RSS订阅」那颗汉字，否则"没有"其实是"看不见"（名册 289 同族）。
    /// </summary>
    [Fact]
    public void TheBuildFallbackHandstOverASentenceNoLonger()
    {
        Assert.Empty(WordLiterals(Build));
        Assert.NotEmpty(WordLiterals("src/StarMark.Abstractions/Feed/RssFeed.cs"));
    }

    /// <summary>② 兜底话交的是键，而那颗键<b>恰被问一次</b>（问两次＝一颗键两个主人，坑表 #290 那一族）。</summary>
    [Fact]
    public void TheFallbackLineAsksTheTableOnce()
    {
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Build)), $"Locale.T(\"{Key}\")"));
        Assert.Equal(0, SourceGate.Count(SourceGate.ReadRepoFile(Build), "未知（"));
    }

    /// <summary>③ 表里那一行有三张 voice（少一列＝那一门语言念出键名本身），且消费点一⛔ 掉。</summary>
    [Fact]
    public void TheKeyIsInTheTableWithThreeVoicesAndStillConsumed()
    {
        var table = SourceGate.Code(SourceGate.ReadRepoFile(Table));
        Assert.Equal(1, SourceGate.Count(table, $"new Entry(\"{Key}\","));
        var m = Regex.Match(table,
            "new Entry\\(\"" + Regex.Escape(Key) + "\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\"\\)");
        Assert.True(m.Success, $"{Key} 那颗 Entry 读不出三列（写法变了，守门要先跟上）");
        Assert.All(new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value },
            v => Assert.False(string.IsNullOrWhiteSpace(v), $"{Key} 有一列是空的"));
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Service)),
            "DiagnosticEntry.Fact(StarMark.Abstractions.BuildInfo.Display)"));
    }

    private static string[] WordLiterals(string rel)
        => FormatScanner.Scan(SourceGate.ReadRepoFile(rel)).Literals.Where(s => Word.IsMatch(s)).ToArray();
}
