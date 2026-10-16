#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 世界时钟那一档与"相对日期"那一族的形状闸门（批次 WI-76）。
/// <para>
/// 这一族从前有两份<b>形状外</b>的中文：① <c>WorldClockPolicy.DefaultCities</c> 把四颗城名写成<b>记录的位置实参</b>，
/// 而那些字既被 <c>{x:Bind Name}</c> 念到屏幕上、又被 <c>WidgetStorage</c> 写进组件档——
/// 于是"库里那句是哪国话"取决于<b>装它那一刻系统的 UI 语言</b>（✅P-199 同族：会落盘的成品字）；
/// ② "明天／昨天"在 <c>WorldClockPolicy.DayLabel</c> 与 <c>LocalItemState.DescribeDue</c> <b>各写一份</b>。
/// 前一把按"成员形状"数的尺⛔ 看得见这两族（名册 304）⇒ 这颗闸门钉的就是那两个<b>形状</b>。
/// </para>
/// </summary>
public class WorldClockWordGateTests
{
    private const string Policy = "src/StarMark.Core/Widgets/WorldClockPolicy.cs";
    private const string State = "src/StarMark.Abstractions/LocalItemState.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/WorldClockViewModel.cs";
    private const string Widget = "src/StarMark.UI/Views/WorldClockWidget.xaml.cs";
    private const string Snapshot = "src/StarMark.UI/Services/WidgetManager.Snapshot.cs";
    private const string Todo = "src/StarMark.UI/Views/TodoWidget.xaml.cs";

    /// <summary>真正的「话」＝汉字或假名（全角标点⛔ 算，那颗归 ✅P-196 的连接位那一族）。</summary>
    private static readonly Regex Word = new(@"[぀-ヿ㐀-䶿一-鿿]", RegexOptions.Compiled);

    private static readonly string[] DayKeys =
    {
        "time.day.today",
        "time.day.tomorrow",
        "time.day.yesterday",
        "time.day.overdue",
        "time.day.ahead",
        "time.day.behind",
        "time.day.monthDay",
        "widget.clock.unnamedZone",
    };

    /// <summary>
    /// ① <b>那一档里⛔ 一枚成品汉字</b>（四颗城名＋日签四支＋兜底那颗都交出去了）。
    /// 同一把尺在<b>农历那一档</b>必须读出汉字，否则"没有"就是"看不见"（名册 289／304 同族）。
    /// 而默认四城仍恰是<b>四颗只带 Id 的记录</b>。
    /// </summary>
    [Fact]
    public void ThatPolicyFileHandsOutIdsAndKeysNotSentences()
    {
        Assert.Empty(WordLiterals(Policy));
        Assert.NotEmpty(WordLiterals("src/StarMark.Core/Widgets/GlanceCalendar.cs"));   // 正件：那档确有按语言各排的名表
        Assert.Equal(4, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Policy)), "new WorldClockCity(\""));
    }

    /// <summary>② <b>八颗键各一行、三列都非空</b>：表里少一行＝那一门语言念出键名本身（坑表 #280 那一族）。</summary>
    [Fact]
    public void TheEightDayWordsAreInTheTableWithThreeVoices()
    {
        var table = SourceGate.Code(SourceGate.ReadRepoFile(Table));
        foreach (var key in DayKeys)
        {
            Assert.Equal(1, SourceGate.Count(table, $"new Entry(\"{key}\","));
            var m = Regex.Match(table,
                "new Entry\\(\"" + Regex.Escape(key) + "\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\"\\)");
            Assert.True(m.Success, $"{key} 那颗 Entry 读不出三列（写法变了，守门要先跟上）");
            Assert.All(new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value },
                v => Assert.False(string.IsNullOrWhiteSpace(v), $"{key} 有一列是空的"));
        }
    }

    /// <summary>
    /// ③ <b>同一个词只许有一个出处</b>：日签与待办截止读数都念表里那颗键（各一次），
    /// 而<b>原话⛔ 回来</b>——旧写法是两处各写一份中文。那颗摘掉两家（<c>widget.todo.due*</c>）⛔ 再进表（死键）。
    /// </summary>
    [Fact]
    public void TheSameDayWordHasOneHomeTwoVoices()
    {
        var policy = SourceGate.Code(SourceGate.ReadRepoFile(Policy));
        var state = SourceGate.Code(SourceGate.ReadRepoFile(State));
        var table = SourceGate.ReadRepoFile(Table);
        foreach (var key in new[] { "time.day.tomorrow", "time.day.yesterday" })
        {
            Assert.Equal(1, SourceGate.Count(policy, $"Locale.T(\"{key}\")"));
            Assert.Equal(1, SourceGate.Count(state, $"Locale.T(\"{key}\")"));
        }
        Assert.Equal(1, SourceGate.Count(policy, "Locale.Fmt(\"time.day.ahead\""));
        Assert.Equal(1, SourceGate.Count(policy, "Locale.Fmt(\"time.day.behind\""));
        Assert.Equal(1, SourceGate.Count(state, "Locale.Fmt(\"time.day.overdue\""));
        Assert.Equal(1, SourceGate.Count(state, "Locale.Fmt(\"time.day.monthDay\""));
        foreach (var word in new[] { "\"明天\"", "\"昨天\"", "\"今天\"" })
        {
            Assert.Equal(0, SourceGate.Count(policy, word));
            Assert.Equal(0, SourceGate.Count(state, word));
        }
        Assert.Equal(0, SourceGate.Count(table, "widget.todo.dueToday"));
        Assert.Equal(0, SourceGate.Count(table, "widget.todo.dueTomorrow"));
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Todo)), "\"time.day.today\""));
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Todo)), "\"time.day.tomorrow\""));
    }

    /// <summary>
    /// ④ <b>名字由 Id 当场取，⛔ 从档里再读</b>：点位记录只剩 <c>ZoneId</c>，
    /// 装载／添加／落盘三处都交一颗 Id，换语言那一刻重取一次名字（订阅⛔ 成对＝P-198 那一族）。
    /// </summary>
    [Fact]
    public void TheZoneNameIsReresolvedInsteadOfRereadFromTheConfig()
    {
        var vm = SourceGate.Code(SourceGate.ReadRepoFile(ViewModel));
        var widget = SourceGate.Code(SourceGate.ReadRepoFile(Widget));
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Policy)),
                                          "public sealed record WorldClockCity(string ZoneId);"));
        Assert.Equal(0, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Policy)), "string ZoneId, string Name"));
        Assert.Equal(3, SourceGate.Count(vm, "WorldClockPolicy.DisplayNameOf("));
        Assert.Equal(2, SourceGate.Count(vm, "new WorldClockCity(r.ZoneId)"));
        Assert.Equal(2, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Snapshot)), "new WorldClockCity(c.ZoneId)"));
        Assert.Equal(1, SourceGate.Count(widget, "ViewModel.ReresolveNames();"));
        Assert.Equal(1, SourceGate.Count(widget, "Locale.Changed += RepasteZoneRows"));
        Assert.Equal(1, SourceGate.Count(widget, "Locale.Changed -= RepasteZoneRows"));
    }

    /// <summary>那一档里含汉字／假名的<b>字面量</b>（先抹注释，所以注释里的中文⛔ 算）。</summary>
    private static string[] WordLiterals(string rel)
        => FormatScanner.Scan(SourceGate.ReadRepoFile(rel)).Literals
             .Where(s => Word.IsMatch(s)).ToArray();
}
