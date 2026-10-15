#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-67 片9c 第四刀（P-188 第八刀）的闸门：<b>页面上那几句固定话⛔ 再写在代码里</b>。
/// <para>射程＝六扇：音乐（<c>MusicWidget</c> 两档）、待办（<c>TodoWidgetViewModel</c> ＋ <c>TodoWidget</c>）、
/// 快捷启动（<c>QuickLaunchWidget.xaml(.cs)</c>）、截图框选（<c>CaptureOverlayWindow.xaml(.cs)</c>）、
/// RSS（<c>RssPage.xaml.cs</c> ＋ <c>RssPageViewModel.cs</c>）、快照详情（<c>SnapshotPage.xaml.cs</c>）。</para>
/// <para>⚠ 与①那一格（<c>EmptyHintWordGateTests</c>）的分工：那颗数的是"同一句话有没有第二个主人"，
/// 这一颗数的是"这些档的正文里⛔ 再有汉字"＋"新键各只被问一次"＋"三语各说各的"。
/// 剩下那 12 档的汉字全在<b>只进日志／只进启动分段表／只进异常断言</b>那一族（✅P-191 他裁「文字可删去」），
/// 所以本档⛔ 把 <c>TodoWidgetViewModel</c>／<c>QuickLaunchWidget.xaml.cs</c>／<c>CaptureOverlayWindow.xaml.cs</c>
/// 拉进来判 <c>ScreenWords == 0</c>——那是把仪器自己的瞎处当缺陷（坑表 #531 同族）。</para>
/// <para>尺＝<see cref="SourceGate.ScreenWords"/>＋<see cref="SourceGate.Count"/>＋<see cref="UiStrings"/>，
/// 计数型判据逐颗点名，⛔ 一个"看起来差不多"的总数（规则 #115 的仪器面）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class PageWordGateTests : IDisposable
{
    private const string MusicVm = "src/StarMark.UI/Views/MusicWidget.xaml.cs";
    private const string TodoVm = "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs";
    private const string TodoCs = "src/StarMark.UI/Views/TodoWidget.xaml.cs";
    private const string RssCs = "src/StarMark.UI/Views/RssPage.xaml.cs";
    private const string RssVm = "src/StarMark.UI/ViewModels/RssPageViewModel.cs";
    private const string SnapCs = "src/StarMark.UI/Views/SnapshotPage.xaml.cs";

    private static readonly string[] Words =
    [
        "widget.music.emptyTitle", "widget.music.emptyArtist", "widget.music.noSession", "widget.music.systemBlocked",
        "widget.music.unknownTitle", "widget.music.unknownArtist", "widget.music.followSystem", "widget.music.playingSuffix",
        "widget.todo.filterAll", "widget.todo.filterActive", "widget.todo.filterCompleted",
        "widget.todo.allDone", "widget.todo.remaining", "widget.todo.deleted",
        "widget.todo.colorNone", "widget.todo.colorRed", "widget.todo.colorOrange", "widget.todo.colorYellow",
        "widget.todo.colorGreen", "widget.todo.colorBlue", "widget.todo.colorPurple",
        "widget.todo.dueToday", "widget.todo.dueTomorrow", "widget.todo.dueClear",
        "widget.ql.dragHint", "widget.ql.elevatedHint",
        "capture.hintToolbar", "capture.hintPin", "capture.hintOcr", "capture.selectionTip",
        "rss.rowTip",
        "snapshot.detailLinks", "snapshot.detailLocal", "snapshot.detailTag", "snapshot.detailNoData",
        "snapshot.detailSep", "snapshot.detailLine",
    ];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    /// <summary>语料⛔ 把表自己算进来（否则"这颗键没人问"会被表里那一行自己满足＝仪器骗人，规则 #95/#102）。</summary>
    private static string AskedCorpus()
        => string.Join("\n", ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => f.Text));

    /// <summary>
    /// <b>标记那一侧也有一份语料</b>：<c>ReadRepoUnder</c> 只扫 <c>.cs</c>，而这一刀的三句话住在 <c>.xaml</c> 的标记上
    /// （<c>widget.ql.dragHint</c> 与 <c>capture.selectionTip</c>）。⛔ 拿"代码里问了几次"去判它们＝读成 0 次没人问，
    /// 那是仪器瞎，不是代码坏（坑表 #422 那一族：判错的方向比判错更贵）。
    /// </summary>
    private static string AskedMarkup()
        => string.Concat(ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml"),
                         ReadRepoFile("src/StarMark.UI/Views/QuickLaunchWidget.xaml"));

    // ────────── ① 这六档的正文里⛔ 留一句写死的汉字 ──────────

    [Theory]
    [InlineData(MusicVm)]
    [InlineData(TodoCs)]
    [InlineData(RssCs)]
    [InlineData(RssVm)]
    [InlineData(SnapCs)]
    public void TheseSliceHostsKeptNoHardCodedWords(string rel)
    {
        var words = ScreenWords(CodeOf(rel));
        Assert.True(words.Count == 0, $"{rel} 仍留着非日志的屏幕上汉字片段 {words.Count} 处：{string.Join(" / ", words.Take(5))}");
    }

    // ────────── ② 取词时机：VM⛔ 自己念，交的是「键＋实参」 ──────────

    /// <summary>
    /// <b>待办那颗 VM 交出的是没取过词的那句</b>：里头⛔ 有 <c>$"剩余 {…}"</c>／<c>$"已删除「{…}」"</c>，
    /// 而换语言那一刻由宿主那一拍重贴（③那一格）——一旦"顺手先把句子念好"，语言就被冻在算出它的那一刻（坑表 #276 那一族，
    /// 而形状闸门①那一格看不见，因为它只数汉字）。
    /// </summary>
    [Fact]
    public void TheTodoViewModelHandsOverTheKeyNotTheSentence()
    {
        var vm = CodeOf(TodoVm);
        Assert.Equal(1, Count(vm, "public LocMessage? SummaryMessage =>"));
        Assert.Equal(1, Count(vm, "public LocMessage? UndoMessage"));
        Assert.Equal(0, Count(vm, "public string SummaryText"));
        Assert.Equal(0, Count(vm, "public string UndoText"));
        Assert.Equal(0, Count(vm, "$\"剩余 {"));
        Assert.Equal(0, Count(vm, "$\"已删除「"));
        // 「没话了」交 null，⛔ 留一句空字符串当"有话"——那是把旧句子留在重贴名单里（坑表 #276 的反向）。
        Assert.Equal(1, Count(vm, "UndoMessage = null;"));
        Assert.Equal(0, Count(vm, "UndoMessage = string.Empty"));
        // ③的取词面：那颗 VM⛔ 自己念（念＝冻语言），也⛔ 引 <c>Locale</c>。
        Assert.Equal(0, Count(vm, ".Render()") + Count(vm, "Locale.T(") + Count(vm, "Locale.Fmt("));
    }

    // ────────── ③ 贴上去那一刻才念：门只认得 LocText 那三颗 ──────────

    /// <summary>
    /// <b>宿主那一侧全部走 <c>LocText</c> 那三颗门</b>（正文／带读数／悬停说明），⛔ 自己拼一句再赋给 <c>.Text</c>：
    /// 赋上去的那一刻是什么语言，那一行就永远是什么语言——换语言后屏幕上留下一句旧话（P-175 缺口②的页级形状）。
    /// </summary>
    [Fact]
    public void EachHostPastesThroughTheOneDoor()
    {
        var todo = CodeOf(TodoCs);
        Assert.Equal(3, Count(todo, "LocText.SetKey(Filter"));                       // 三颗分段按钮＝固定那句＋当场读数
        Assert.Equal(1, Count(todo, "LocText.SetMessage(SummaryBlock, ViewModel.SummaryMessage)"));
        Assert.Equal(1, Count(todo, "LocText.SetMessage(UndoTextBlock, ViewModel.UndoMessage)"));
        Assert.Equal(0, Count(todo, "SummaryBlock.Text ="));                           // ⛔ 绕过那颗门直接赋串
        Assert.Equal(0, Count(todo, "UndoTextBlock.Text ="));

        var music = CodeOf(MusicVm);
        Assert.Equal(6, Count(music, "LocText.SetMessage("));                          // 两句空态＋两句失败＋两句兜底名
        Assert.Equal(3, Count(music, "LocText.SetKey("));
        // 三颗：「跟随系统」＋"· 播放中"那颗带读数的＋"只在播的没在播"那一支先交 null 把注册项摘掉（⛔ 换语言把播放器名盖成表里的话）。
        Assert.Equal(1, Count(music, "LocText.SetKey(item, string.Empty); item.Text = option.DisplayName;"));
        Assert.Equal(0, Count(music, "TitleBlock.Text = \"") + Count(music, "ArtistBlock.Text = \""));

        var rss = CodeOf(RssCs);
        Assert.Equal(1, Count(rss, "LocText.SetToolTipKey(row, \"rss.rowTip\", section.Url, section.FolderPath);"));
        Assert.Equal(0, Count(rss, "ToolTipService.SetToolTip(row,"));                 // 提示词也归那颗门（换语言才重贴）

        var ql = CodeOf("src/StarMark.UI/Views/QuickLaunchWidget.xaml.cs");
        Assert.Equal(1, Count(ql, "LocText.SetMessage(Hint, LocMessage.Of(\"widget.ql.elevatedHint\"));"));
        Assert.Equal(0, Count(ql, "Hint.Text ="));

        var shot = CodeOf("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        // 三档提示各一颗键，全部交进那颗门（⛔ 默认那句留在标记上：那一行元素还要被代码写，挂两颗主人＝换语言盖掉真话）。
        Assert.Equal(1, Count(shot, "LocText.SetMessage(HintText, mode switch"));
        Assert.Equal(1, Count(shot, "CaptureMode.Pin => LocMessage.Of(\"capture.hintPin\")"));
        Assert.Equal(1, Count(shot, "CaptureMode.Ocr => LocMessage.Of(\"capture.hintOcr\")"));
        Assert.Equal(1, Count(shot, "_ => LocMessage.Of(\"capture.hintToolbar\")"));
        Assert.Equal(0, Count(shot, "LocText.SetMessage(HintText, null)"));
    }

    // ────────── ④ 菜单／模板那两侧：名字交键，⛔ 抄句子 ──────────

    /// <summary>
    /// 颜色七档与截止三档<b>只这一份</b>，交的是键；念出来发生在每次点开那张菜单的那一刻（与条目右键同一口径）。
    /// ⛔ 在数据里存中文标签——那正是"换语言后菜单还是上一门语言"的形状。
    /// </summary>
    [Fact]
    public void TheColorAndDueTablesCarryKeysNotLabels()
    {
        var todo = CodeOf(TodoCs);
        Assert.Equal(1, Count(todo, "(int Tier, string NameKey)[] ColorTiers"));
        Assert.Equal(1, Count(todo, "(int? Offset, string NameKey)[] DueTiers"));
        Assert.Equal(10, Count(todo, "\"widget.todo.color") + Count(todo, "\"widget.todo.due"));   // 七档颜色＋三档日期＝十颗键，一颗不多一颗不少
        Assert.Equal(2, Count(todo, "new MenuFlyoutItem { Text = Locale.T(nameKey) }"));           // 两处循环各念一次，⛔ 把名字存进表里那份数据
        foreach (var word in new[] { "红（紧急）", "橙（重要）", "黄（提醒）", "绿（常规）", "蓝（稍后）", "紫（灵感）", "今天", "明天" })
            Assert.Equal(0, Count(todo, "\"" + word + "\""));
    }

    // ────────── ⑤ 每一颗新键：只有一个主人，三语各说各的 ──────────

    [Theory]
    [InlineData("widget.music.emptyTitle")]
    [InlineData("widget.music.emptyArtist")]
    [InlineData("widget.music.noSession")]
    [InlineData("widget.music.systemBlocked")]
    [InlineData("widget.music.unknownTitle")]
    [InlineData("widget.music.unknownArtist")]
    [InlineData("widget.music.followSystem")]
    [InlineData("widget.music.playingSuffix")]
    [InlineData("widget.todo.filterAll")]
    [InlineData("widget.todo.filterActive")]
    [InlineData("widget.todo.filterCompleted")]
    [InlineData("widget.todo.allDone")]
    [InlineData("widget.todo.remaining")]
    [InlineData("widget.todo.deleted")]
    [InlineData("widget.todo.colorNone")]
    [InlineData("widget.todo.colorRed")]
    [InlineData("widget.todo.colorOrange")]
    [InlineData("widget.todo.colorYellow")]
    [InlineData("widget.todo.colorGreen")]
    [InlineData("widget.todo.colorBlue")]
    [InlineData("widget.todo.colorPurple")]
    [InlineData("widget.todo.dueToday")]
    [InlineData("widget.todo.dueTomorrow")]
    [InlineData("widget.todo.dueClear")]
    [InlineData("widget.ql.dragHint")]
    [InlineData("widget.ql.elevatedHint")]
    [InlineData("capture.hintToolbar")]
    [InlineData("capture.hintPin")]
    [InlineData("capture.hintOcr")]
    [InlineData("capture.selectionTip")]
    [InlineData("rss.rowTip")]
    [InlineData("snapshot.detailLinks")]
    [InlineData("snapshot.detailLocal")]
    [InlineData("snapshot.detailTag")]
    [InlineData("snapshot.detailNoData")]
    [InlineData("snapshot.detailSep")]
    [InlineData("snapshot.detailLine")]
    public void EveryNewWordIsAskedForOnceAndSaysItDifferentlyInEachLanguage(string key)
    {
        Assert.True(Words.Length == 37, "本档点名的键数与语料那一串必须相等（现在是 " + Words.Length + "）");
        Assert.Contains(key, Words, StringComparer.Ordinal);
        // 一颗键一个主人＝<b>代码与标记两边合计恰好一次</b>（⛔ 一句字两个主人，也⛔ 只扫 .cs 把标记那颗读成"没人问"）。
        Assert.Equal(1, Count(AskedCorpus(), @"""" + key + @"""") + Count(AskedMarkup(), @"""" + key + @""""));
        Assert.Equal(0, Count(AskedCorpus(), "new Entry(\"" + key + "\""));               // ⛔ 把表自己算进语料
        Assert.True(UiStrings.TryGet(key, out var row), "表里没有这颗键：" + key);
        Assert.Empty(Cjk.Matches(row.En).Cast<Match>());                                  // "没翻"看着像翻了＝坑表 #526
        Assert.NotEqual(row.Zh, row.En);
        Assert.NotEqual(row.Zh, row.Ja);
        Assert.NotEqual(row.En, row.Ja);
        var said = new List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var one = LocMessage.Of(key, "甲", "乙", "丙").Render();
            Assert.DoesNotContain("{0}", one, StringComparison.Ordinal);                  // 填空没做＝屏幕上挂着键的形状
            Assert.Equal(said.Count + 1, said.Append(one).Distinct(StringComparer.Ordinal).Count());
            said.Add(one);
        }
        Assert.Equal(3, said.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <b>那颗带读数的槽：换个实参就换个句子</b>——三颗计数槽（入口／待办随记／标签）各钉自己那颗，
    /// ⛔ 把上一行的读数留在下一行（跨行串读数＝屏幕上两行说同一件事）。
    /// </summary>
    [Fact]
    public void TheSnapshotDetailSlotsCarryTheirOwnReadings()
    {
        Assert.Equal(1, RowSlots("snapshot.detailLinks"));
        Assert.Equal(1, RowSlots("snapshot.detailLocal"));
        Assert.Equal(1, RowSlots("snapshot.detailTag"));
        Assert.Equal(3, RowSlots("snapshot.detailLine"));
        Locale.TrySet(AppLanguage.Chinese);
        var links = LocMessage.Of("snapshot.detailLinks", 7).Render();
        var local = LocMessage.Of("snapshot.detailLocal", 7).Render();
        Assert.DoesNotContain("入口", local, StringComparison.Ordinal);
        Assert.DoesNotContain("待办", links, StringComparison.Ordinal);
        var line = LocMessage.Of("snapshot.detailLine", "时钟", "入口 7", "10,20 300×200").Render();
        Assert.Contains("时钟", line, StringComparison.Ordinal);
        Assert.Contains("10,20 300×200", line, StringComparison.Ordinal);
        Assert.DoesNotContain("{2}", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>删掉那颗没人绑的读数</b>（与片9b 那颗 <c>StatusText</c> 同一族）：语料里⛔ 再出现 <c>CountText</c>——
    /// 留着它就留着"$"共 N 条"这一族的第二个入口，下一次有人绑上去就会重新长出"换语言不换读数"那一坑。
    /// </summary>
    [Fact]
    public void TheRssCountTextOutletIsGone()
    {
        Assert.Equal(0, Count(AskedCorpus(), "CountText"));
        Assert.Equal(0, Count(AskedCorpus(), "共 {Rows.Count} 条"));
    }
}
