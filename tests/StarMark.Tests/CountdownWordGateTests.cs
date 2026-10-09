#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Feed;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-54（P-182a 剩半边·倒计时/纪念日那一族）的闸门：<b>那一扇屏幕上每句话只许表里一份出处，而且 Core 交出的是键</b>。
/// <para>
/// 射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝倒计时组件那一扇：Core 那把 <c>CountdownPolicy</c> 的算术与措辞、
/// 它那颗 VM、它的 <c>.xaml</c> 与 code-behind。天气那一族（✅WI-53）是同一条 P 的另一颗宿主，⛔ 并到这一档里读；
/// 今日速览那一行节日倒计时＋<c>GlanceCalendar</c> 的名表也是另一颗宿主（账本里另批登记）。
/// </para>
/// <para>⚠ 与 ✅WI-53 同处分两类证人：<b>源码形状</b>（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>，坑表 #370）
/// 与<b>能真跑的 Core 读数</b>——后者在本档里不是点缀：<b>本批第一颗红就是这么来的</b>
/// （<c>widget.cd.nameAnniv</c> 两个槽、代码只递一颗已经拼好「6-6」的实参 ⇒ <c>FormatException</c>，
/// 而形状尺一条都不红 ⇒ 台架规则 #131 说的那格"按代码实际给的实参渲染一次"在这里落地，坑表 #393）。</para>
/// <para>尺＝抹注释后逐行取连续汉字片段（坑表 #375／#378：按调用形状数会漏掉内插串里的三元与标记里的 <c>Content=""</c>）；
/// 日志行按 #368 那条界线豁免（不上屏幕）。</para>
/// </summary>
public sealed class CountdownWordGateTests
{
    private const string PolicyCs = "src/StarMark.Core/Widgets/CountdownPolicy.cs";
    private const string ViewModelCs = "src/StarMark.UI/ViewModels/CountdownViewModel.cs";
    private const string WidgetCs = "src/StarMark.UI/Views/CountdownWidget.xaml.cs";
    private const string WidgetXaml = "src/StarMark.UI/Views/CountdownWidget.xaml";
    private const string RssCacheCs = "src/StarMark.Core/Feed/RssFeedCache.cs";

    private static readonly Regex Cjk = new(@"[一-鿿]+", RegexOptions.Compiled);
    private static readonly Regex Slots = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>本批交表的 20 颗：17 颗这一扇宿主自己的话＋3 颗与 RSS 节奏共用的单位词（改名只落在调用点）。</summary>
    private static readonly string[] Shipped =
    {
        "widget.cd.remainDays", "widget.cd.remainClock", "widget.cd.underMinute", "widget.cd.past",
        "widget.cd.dueAt", "widget.cd.annivAt", "widget.cd.thisYearAt", "widget.cd.expiredAt",
        "widget.cd.annivCount", "widget.cd.errDate", "widget.cd.errTimeRequired", "widget.cd.errTime",
        "widget.cd.errUnknown", "widget.cd.limit", "widget.cd.empty", "widget.cd.nameAnniv",
        "widget.cd.nameDue",
        "time.unit.minutes", "time.unit.hours", "time.unit.days",
    };

    /// <summary>住在 VM、测试工程点不着的三颗（由 <see cref="TheVmHandsOutKeysAndTheOneOwnerSentencesStaySingle"/> 覆盖）。</summary>
    private static readonly string[] UiSide = ["widget.cd.errUnknown", "widget.cd.limit", "widget.cd.empty"];

    private static CountdownItem Item(string title, DateTime at, bool yearly) => new()
    {
        Title = title, At = at, ZoneId = "China Standard Time", Yearly = yearly,
    };

    // 北京 2026-06-06 09:00 整＝"正好落在本轮那一刻"那一格用的时刻
    private static readonly DateTimeOffset ExactRound = new(2026, 6, 6, 9, 0, 0, TimeSpan.FromHours(8));

    /// <summary>
    /// 把 Core 这一侧<b>真跑得到的每一颗键</b>各配一个<b>真的会走到</b>的场景。
    /// <para>⚠ <c>widget.cd.annivCount</c>（"第 N 个年头"）只有<b>正好落在本轮那一刻</b>才出现：每年项的
    /// <c>NextOccurrence</c> 定义上返回"今年或明年"那个<b>未来</b>时刻，过了那一刻就直接跳下一年倒数。
    /// 这一格因此必须钉到 <c>now == occurrence</c>，⛔ 随手给个时刻——那会把"这行永远走不到"读成"走到了"
    /// （台架规则 #111：键族穷举可达性、双向对账）。</para>
    /// </summary>
    private static LocMessage[] RenderedByThePolicy()
    {
        var oneShot = Item("交稿", new DateTime(2026, 12, 31, 18, 0, 0), yearly: false);
        var yearly = Item("生日", new DateTime(2000, 6, 6, 9, 0, 0), yearly: true);
        var thisYear = Item("本年那场", new DateTime(2026, 6, 6, 9, 0, 0), yearly: true);
        var beforeDue = new DateTimeOffset(2026, 12, 28, 12, 0, 0, TimeSpan.FromHours(8));
        var justAfterDue = new DateTimeOffset(2026, 12, 31, 18, 30, 0, TimeSpan.FromHours(8));
        var beforeAnniv = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(8));

        var messages = new List<LocMessage>
        {
            CountdownPolicy.FormatRemaining(TimeSpan.FromDays(3) + TimeSpan.FromHours(5)),   // 天＋钟点
            CountdownPolicy.FormatRemaining(TimeSpan.FromMinutes(30)),                        // 只有钟点
            CountdownPolicy.FormatRemaining(TimeSpan.FromSeconds(20)),                        // 不足一分钟
            CountdownPolicy.FormatDuration(TimeSpan.FromMinutes(40)),                         // 分钟
            CountdownPolicy.FormatDuration(TimeSpan.FromHours(6)),                            // 小时
            CountdownPolicy.FormatDuration(TimeSpan.FromDays(4)),                             // 天
            CountdownPolicy.Describe(oneShot, beforeDue).Headline,
            CountdownPolicy.Describe(oneShot, beforeDue).Subline,                             // 到期
            CountdownPolicy.Describe(oneShot, justAfterDue).Headline,                         // 已过（把上面那颗单位句嵌进来）
            CountdownPolicy.Describe(oneShot, justAfterDue).Subline,                          // 已到期
            CountdownPolicy.Describe(yearly, beforeAnniv).Subline,                            // N 周年
            CountdownPolicy.Describe(thisYear, beforeAnniv).Subline,                          // 今年
            CountdownPolicy.Describe(yearly, ExactRound).Subline,                             // 第 N 个年头（正落在那一刻）
            CountdownPolicy.TitleMessage(oneShot.At, yearly: false),                          // 默认名：截止
            CountdownPolicy.TitleMessage(yearly.At, yearly: true),                            // 默认名：纪念日
        };
        CountdownPolicy.TryParseInput("明年某天", "10:00", true, out _, out var errDate);
        CountdownPolicy.TryParseInput("2026-12-31", "", true, out _, out var errTimeRequired);
        CountdownPolicy.TryParseInput("2026-12-31", "25:00", true, out _, out var errTime);
        messages.Add(errDate!);
        messages.Add(errTimeRequired!);
        messages.Add(errTime!);
        // RSS 那一族问的是同三颗单位键：它必须一起进这一格，否则"共用一份"只是注释里的一句话
        messages.Add(RssFeedCache.GapMessageFor(TimeSpan.FromMinutes(5)));
        messages.Add(RssFeedCache.GapMessageFor(TimeSpan.FromHours(5)));
        messages.Add(RssFeedCache.GapMessageFor(TimeSpan.FromDays(5)));
        return messages.ToArray();
    }

    /// <summary>
    /// <b>每一颗交出去的键都得按代码实际给的实参渲染得出来</b>：槽数与实参数对不上，<c>string.Format</c> 当场抛
    /// <c>FormatException</c>，界面上是那一行<b>整格空白</b>（一个字都不剩）——而通用闸门只数"键在不在表里"，⛔ 看得见这一族。
    /// <para>两头都钉：键必须登记（⛔ 表里留颗死行），槽数必须与实参数相等（嵌进去的那颗 <see cref="LocMessage"/> 算一个槽）。
    /// 期望那一侧取的是<b>表里那一行</b>本身（⛔ 在测试里抄一句措辞，规则 #130）。</para>
    /// </summary>
    [Fact]
    public void EveryKeyThePolicyHandsOutRendersWithTheArgsItActuallyHands()
    {
        var handed = RenderedByThePolicy();
        var coreSide = Shipped.Where(key => !UiSide.Contains(key, StringComparer.Ordinal)).ToArray();
        Assert.Equal(17, coreSide.Length);

        var unreachable = coreSide.Where(key => !handed.Any(m => m.Key == key)).ToList();
        Assert.True(unreachable.Count == 0,
            "这些键本批交了表⛔ 真跑里没有一格走到它（要么场景少了＝证人空转，要么那一行永远念不出来）：\n  "
            + string.Join("\n  ", unreachable));

        foreach (var message in handed)
        {
            Assert.True(UiStrings.TryGet(message.Key, out var row), $"交出的键没登记在表里：{message.Key}");
            var slots = Slots.Matches(row.Zh).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();
            var args = message.Args?.Length ?? 0;
            Assert.True(slots == args,
                $"{message.Key} 中文列有 {slots} 个槽，代码递了 {args} 颗实参（对不上⇒那一格屏幕上空白。"
                + "⛔ 把分隔符预先拼进实参凑成一颗——那正是这个形状，坑表 #371／#393）");
            var rendered = message.Render();
            Assert.False(string.IsNullOrWhiteSpace(rendered), $"{message.Key} 渲染出来是空的");
            Assert.DoesNotContain("{", rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 这一族的三档代码宿主⛔ 留一颗自己的汉字面字（日志除外）。
    /// <para>⚠ <b>一般式</b>（规则 #131）：钉的是"这一档里有汉字的行"，⛔ 钉上次看到的那几种写法——
    /// 下一手换成内插串里的三元、标记里的 <c>Content=""</c>、或递进公共对话框工厂的实参，都照样落在这一格里。</para>
    /// </summary>
    [Fact]
    public void TheCountdownFamilyCarriesNoWordsOfItsOwn()
    {
        foreach (var rel in new[] { PolicyCs, ViewModelCs, WidgetCs })
        {
            var left = CodeOf(rel).Replace("\r\n", "\n").Split('\n')
                .Where(line => Cjk.IsMatch(line) && !line.Contains("StarLog", StringComparison.Ordinal))
                .Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
            Assert.True(left.Length == 0, $"{rel} 还剩 {left.Length} 行自己的汉字面字：\n  " + string.Join("\n  ", left));
        }
    }

    /// <summary>
    /// <b>Core 交出的是那颗键，⛔ 是已经取好词的那句</b>，而且 Core 自己⛔ 取词（✅WI-37-C 那颗闹钟／✅WI-53 那把码表同一条口径）。
    /// <para>为什么不是洁癖：这一族每秒都被问一遍，而语言是用户随时改的——交字符串＝把语言冻在算出它的那一刻，
    /// 换语言那一刻没人回去重算。</para>
    /// </summary>
    [Fact]
    public void ThePolicyHandsOutKeysAndNeverFetchesAWord()
    {
        var code = CodeOf(PolicyCs);
        Assert.DoesNotContain("public static string FormatRemaining(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public static string FormatDuration(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public static string TitleOf(", code, StringComparison.Ordinal);   // 那颗"存成品名"的出口⛔ 回来
        Assert.Contains("public static LocMessage FormatRemaining(TimeSpan until)", code, StringComparison.Ordinal);
        Assert.Contains("public static LocMessage TitleMessage(DateTime wallClock, bool yearly)", code, StringComparison.Ordinal);
        Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));
        // 下界⛔ 精确计数：多问一颗必须绿（规则 #108 的对照臂 C3 就靠这一条），而 17 颗真数过（本档里 17 处交键）
        // ⇒ 掉到 15 以下才是"交键那条路塌了/整句被拼回片段"。逐颗有没有人问由 <see cref="EveryRowThisBatchShippedIsActuallyAskedFor"/> 拦。
        Assert.True(SourceGate.Count(code, "LocMessage.Of(") >= 15,
            $"Core 交键的出口只剩 {SourceGate.Count(code, "LocMessage.Of(")} 处（这一族现场数过 17 处）");
    }

    /// <summary>
    /// <b>单位词只许一份出处</b>：倒计时的"已过 3 天"与 RSS 的"5 分钟没更新"共用 <c>time.unit.*</c> 三颗，
    /// ⛔ 为倒计时再开三颗同义行（同一句话两个主人＝改一处带动另一处，✅WI-46c 那颗组名／✅WI-49 那块橡皮同口径）。
    /// </summary>
    [Fact]
    public void TheDurationUnitsHaveOneRowAndTwoFamilies()
    {
        foreach (var key in new[] { "time.unit.minutes", "time.unit.hours", "time.unit.days" })
        {
            Assert.True(UiStrings.TryGet(key, out _), key + " 不在表里");
            Assert.Contains($"LocMessage.Of(\"{key}\"", CodeOf(PolicyCs), StringComparison.Ordinal);
            Assert.Contains($"LocMessage.Of(\"{key}\"", CodeOf(RssCacheCs), StringComparison.Ordinal);
        }
        // 倒计时那一族⛔ 再长出第二颗"…分钟/小时/天"形状的行（一颗槽、以单位字收尾＝同一个形状的第二主人）
        var twins = UiStrings.All
            .Where(e => e.Key.StartsWith("widget.cd.", StringComparison.Ordinal)
                        && Slots.Matches(e.Zh).Count == 1
                        && new[] { "分钟", "小时", "天" }.Any(unit => e.Zh.EndsWith(unit, StringComparison.Ordinal)))
            .Select(e => e.Key).ToArray();
        Assert.True(twins.Length == 0, "倒计时那一族又开了一颗自己的单位行（第二个主人）：\n  " + string.Join("\n  ", twins));
    }

    /// <summary>
    /// <b>一句念给人听的话⛔ 由片段拼</b>：旧写法是 <c>YearNumber(...) + " 周年"</c>，而那颗兜底给的是"今年"
    /// ⇒ 屏幕上出过「今年 周年」（一个空格挂在那儿）。三个分支各交一颗<b>整句</b>键。
    /// </summary>
    [Fact]
    public void TheAnniversaryLinesAreWholeSentencesNotGluedFragments()
    {
        var code = CodeOf(PolicyCs);
        Assert.DoesNotContain("+ \" 周年\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("YearNumber", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OccurrenceLine", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AnniversaryLine", code, StringComparison.Ordinal);

        var yearly = Item("生日", new DateTime(2000, 6, 6, 9, 0, 0), yearly: true);
        var thisYear = Item("本年那场", new DateTime(2026, 6, 6, 9, 0, 0), yearly: true);
        var beforeAnniv = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(8));

        var n = CountdownPolicy.Describe(yearly, beforeAnniv).Subline;
        Assert.Equal("widget.cd.annivAt", n.Key);
        Assert.Equal(2, n.Args!.Length);                                       // 钟点与年数各一颗，⛔ 先拼成一句
        Assert.DoesNotContain("今年", n.Args![1]?.ToString(), StringComparison.Ordinal);

        var first = CountdownPolicy.Describe(thisYear, beforeAnniv).Subline;
        Assert.Equal("widget.cd.thisYearAt", first.Key);
        Assert.Single(first.Args!);                                             // 「今年」那颗词住在表里：这里⛔ 有它
    }

    /// <summary>
    /// VM 那一侧：交键⛔ 交成品字，而"上限"与"空态"各只许<b>一个主人</b>
    /// （两处上限判断共用一颗 <c>widget.cd.limit</c>；空态那句里那颗按钮的名字<b>交实参</b>，⛔ 抄屏幕上的行名）。
    /// </summary>
    [Fact]
    public void TheVmHandsOutKeysAndTheOneOwnerSentencesStaySingle()
    {
        var vm = CodeOf(ViewModelCs);
        Assert.Equal(2, SourceGate.Count(vm, @"LocMessage.Of(""widget.cd.limit"", CountdownPolicy.MaxItems)"));
        Assert.Equal(1, SourceGate.Count(vm, @"LocMessage.Of(""widget.cd.errUnknown"")"));
        Assert.Equal(0, SourceGate.Count(vm, "Locale.Fmt(\"widget.cd.limit\""));   // ⛔ 第二份"当场取词"的写法
        Assert.Contains(@"LocMessage.Of(""widget.cd.empty"", Locale.T(""widget.cd.add""))", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("点下方「新建」", vm, StringComparison.Ordinal);
        // 存进档里的只许是用户自己起的那个名字（存成品默认名＝把念法当身份存，✅WI-46c 同族）
        Assert.Contains("CountdownPolicy.TitleStored(NewTitle)", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("CountdownPolicy.TitleOf(", vm, StringComparison.Ordinal);
    }

    /// <summary>
    /// 本批每一颗键都得<b>真的有人问</b>（正向尺读字面量，看不见 VM 里 <c>SetError</c> 那颗包装的实参 ⇒ 逐颗点名）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsActuallyAskedFor()
    {
        var asked = string.Join("\n",
            CodeOf(PolicyCs), CodeOf(ViewModelCs), CodeOf(WidgetCs), CodeOf(RssCacheCs),
            SourceGate.ReadRepoFile(WidgetXaml));
        var unasked = Shipped.Where(key => !asked.Contains("\"" + key + "\"", StringComparison.Ordinal)).ToList();
        Assert.True(unasked.Count == 0, "这些行进表却没人问（要么没接线，要么表里留了颗死行）：\n  " + string.Join("\n  ", unasked));
        Assert.Equal(20, Shipped.Length);
        Assert.Equal(Shipped.Length, Shipped.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <c>XAML</c> 那几格⛔ 留面字：那些值由绑定／code-behind 当场重写，挂着静态键＝换语言把带值那句盖掉
    /// （坑表 #276／#295，✅WI-53 同一颗判据）。
    /// </summary>
    [Fact]
    public void TheCountdownMarkupCarriesNoChineseAttributeValues()
    {
        var markup = SourceGate.ReadRepoFile(WidgetXaml).Replace("\r\n", "\n");
        var offenders = Regex.Matches(markup, @"(Text|Content|Header|ToolTipService\.ToolTip|PlaceholderText)=""([^""]*)""")
            .Cast<Match>().Where(m => Cjk.IsMatch(m.Groups[2].Value)).Select(m => m.Value).ToArray();
        Assert.True(offenders.Length == 0, "标记里又留下了面字（那一处的主人应当唯一）：\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>表里⛔ 有控制字符（换行是布局，不是文案；规则 #100）。</summary>
    [Fact]
    public void NoRowOfThisBatchCarriesAControlCharacter()
    {
        foreach (var key in Shipped)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), key + " 不在表里");
            foreach (var column in new[] { entry.Zh, entry.En, entry.Ja })
                Assert.False(column.Any(char.IsControl), $"{key} 那一列带了控制字符：{column}");
        }
    }

    /// <summary>
    /// <b>默认名是"念法"，⛔ 是存进档里的那串字</b>：新条目只存用户自己起的名字（空白＝按当前语言现算派生名），
    /// 而老档里那批当年生成的成品名要<b>认得出来</b>——认的判据⛔ 在本档里再写一份汉字面字（那会长出第二个主人），
    /// 而是拿表里那颗行的<b>中文列当模板</b>现拼一份比对。
    /// </summary>
    [Fact]
    public void TheGeneratedNameIsRecognisedFromTheTableAndNotFromALiteralInCode()
    {
        var code = CodeOf(PolicyCs);
        Assert.Contains("UiStrings.TryGet(message.Key, out var row)", code, StringComparison.Ordinal);
        Assert.Contains("string.Format(row.Zh, message.Args)", code, StringComparison.Ordinal);

        // 认得出只改念法，档里那串字保持原样（⛔ 在这里顺手回写用户的档）
        var legacy = Item("截止（2026-12-31 18:00）", new DateTime(2026, 12, 31, 18, 0, 0), yearly: false);
        Assert.Equal(CountdownPolicy.TitleMessage(legacy.At, yearly: false).Render(), CountdownPolicy.TitleText(legacy));
        Assert.Equal("截止（2026-12-31 18:00）", legacy.Title);

        // 日期对不上的"长得像"⛔ 被当成默认名换掉——那是用户自己打的字
        var lookalike = Item("截止（2030-1-1 08:00）", new DateTime(2026, 12, 31, 18, 0, 0), yearly: false);
        Assert.Equal("截止（2030-1-1 08:00）", CountdownPolicy.TitleText(lookalike));
    }
}
