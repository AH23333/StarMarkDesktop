#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-56（P-186）的闸门：<b>节日的名字只许表里一份出处，而"哪天算节日"按语言各排一张清单，且那张清单⛔ 交出取好词的句子</b>。
/// <para>
/// 射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝速览那一格：Core 的两份日历计算
/// （<c>GlanceCalendar</c>／<c>MonthGrid</c>）、那一扇组件的 <c>.xaml</c> 与 code-behind。
/// 用户 2026-10-09 的裁决是取第二条（「只放公历，或者按照美国/日本的日历和传统节日编排」），
/// 于是这一族从此有两层事实：<b>概念名住表里</b>（一颗键，三列各给该语言里的正确写法），
/// <b>哪天算节日住清单里</b>（三张各排各的）。合不成一张"表配三种翻法"的原因是<b>日期本身不同</b>：
/// 儿童节在中国是 6/1、在日本是 5/5——那一对"哪天"是判据，⛔ 文案。
/// </para>
/// <para>⚠ 证人分两类：<b>源码形状</b>（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>，坑表 #370 那条边界）
/// 与<b>能真跑的 Core 读数</b>（<c>GlanceCalendar</c> 在测试工程里点得着 ⇒ 三档清单的可达性与实参形状都真算一遍）。
/// 屏幕上到底是哪一档念哪一句，仍欠真机读数（#575 那一跑）。</para>
/// <para>尺＝抹注释后逐行取连续汉字片段（坑表 #375／#378）；<b>禁项一律读 <see cref="SourceGate.Code"/></b>，
/// ⛔ 连注释读（规则 #131：否则下一手只想在注释里引用一句旧话的人得先删注释换绿灯）。</para>
/// </summary>
public sealed class GlanceWordGateTests : IDisposable
{
    private const string GlanceCalendarCs = "src/StarMark.Core/Widgets/GlanceCalendar.cs";
    private const string MonthGridCs = "src/StarMark.Core/Widgets/MonthGrid.cs";
    private const string GlanceWidgetCs = "src/StarMark.UI/Views/GlanceWidget.xaml.cs";
    private const string GlanceWidgetXaml = "src/StarMark.UI/Views/GlanceWidget.xaml";

    private static readonly Regex Cjk = new(@"[぀-ヿ㐀-䶿一-鿿]+", RegexOptions.Compiled);

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>本批落表的 6 颗速览句（49 颗节日名不重复列在这里：它们由下面那颗可达性判据逐颗问一遍）。</summary>
    private static readonly string[] ShippedSentences =
    [
        "widget.glance.lunarDay", "widget.glance.lunarDayWithFestival", "widget.glance.nextFestival",
        "widget.glance.trending.none", "widget.glance.trending.failed", "widget.glance.trending.rowTip",
    ];

    /// <summary>表里那 49 颗节日键（按表读，⛔ 在本档再抄一份名单——抄的那份会跟着表落后）。</summary>
    private static string[] FestivalRows()
        => Regex.Matches(SourceGate.ReadRepoFile("src/StarMark.Abstractions/I18n/UiStrings.cs"),
                "\"glance\\.festival\\.[A-Za-z0-9]+\"")
            .Cast<Match>().Select(m => m.Value.Trim('"')).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// <b>Core 交出的是那颗键，⛔ 在这里取词</b>（✅WI-37-C 闹钟、✅WI-53 天气码同一条口径）。
    /// <para><c>Locale.Current</c> 可以读——它选的是"用哪一张清单"，那是<b>判据</b>不是文案；
    /// 但它一旦用来取词，语言就在算出句子那一刻被冻住（坑表 #276 那一族）。</para>
    /// </summary>
    [Fact]
    public void TheCalendarAndTheGridNeverTakeWordsThemselves()
    {
        foreach (var rel in new[] { GlanceCalendarCs, MonthGridCs })
        {
            var code = CodeOf(rel);
            Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
            Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));
        }

        var calendar = CodeOf(GlanceCalendarCs);
        // 键→话那一步全类只有这一颗（清单里每颗规则交的是字符串键，不是成品词）
        Assert.Equal(1, SourceGate.Count(calendar, "LocMessage.Of(rule.Key)"));
        Assert.DoesNotContain("LocMessage.Of(\"glance.festival.", calendar, StringComparison.Ordinal);
        // 倒计时那颗存的是键，⛔ 念出来的名字（✅WI-46c 组名／WI-49 画笔名／WI-54 默认名／WI-55 单位名同一条界线）
        Assert.Contains("public sealed record FestivalCountdown(LocMessage Name, DateOnly Date, int Days)",
            calendar, StringComparison.Ordinal);
        Assert.DoesNotContain("public static string? Festival(", calendar, StringComparison.Ordinal);   // 交词的旧出口⛔ 回来
        // 读语言是为了挑清单，这一条要留着：⛔ 有人把它改成"三档共用一张表"（那是本批裁决的反面）
        Assert.Contains("RulesOf(Locale.Current)", calendar, StringComparison.Ordinal);
    }

    /// <summary>
    /// 农历日序那几张表是<b>这一族唯一还许留中文的地方</b>，因为它们是数据而⛔ 界面文案：
    /// 「正月初一」在英／日没有一一对上的口径，编一份"从不露面的翻译"进表＝登记假词（P-186 定案）。
    /// <para>判据是<b>位置</b>而⛔ 颗数：汉字只许住在那三颗派生日名的方法与那张月名表里。
    /// 有人往别的分支塞一句"今天逢…节"就当场红。</para>
    /// </summary>
    [Fact]
    public void ChineseSurvivesOnlyInTheLunarNameTables()
    {
        var calendar = CodeOf(GlanceCalendarCs);
        // 那张月名表的汉字在**声明的下一行**（数组初始值列表），只取声明那颗就把它漏在外面；
        // 切到收尾的 `};` 为止，⛔ 用 Between 截在 `};` 之前（那样比对不上行）。
        var namesDecl = calendar.IndexOf("LunarMonthNames =", StringComparison.Ordinal);
        Assert.True(namesDecl >= 0, "没找到月名表那颗声明（结构变了，守门要先跟上）");
        var namesEnd = calendar.IndexOf("};", namesDecl, StringComparison.Ordinal);
        Assert.True(namesEnd >= 0, "月名表没找到收尾的 };（结构变了，守门要先跟上）");
        var allowed = string.Join("\n",
            SourceGate.MethodBody(calendar, "public static string LunarText(DateOnly date)"),
            SourceGate.MethodBody(calendar, "private static string LunarDayName(int day)"),
            SourceGate.MethodBody(calendar, "private static string Num(int n)"),
            calendar[(calendar.LastIndexOf('\n', namesDecl) + 1)..(namesEnd + 2)]);

        var stray = CjkLines(calendar).Where(line => !allowed.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(stray.Count == 0,
            $"农历日序以外的地方又留下了中文（那＝在本族里私藏一份⛔ 换语言的成品词）：\n  " + string.Join("\n  ", stray));

        Assert.True(CjkLines(CodeOf(MonthGridCs)).Length == 0, "月历网格又自己写了一句汉字");
    }

    /// <summary>
    /// <b>双向对账＋逐颗可达</b>（台架规则 #111：键族必须穷举可达性，成串的键名暴露不了"这行永远走不到"）：
    /// 三档清单跑出来的键集合，必须与表里那 49 行<b>完全相等</b>。
    /// <para>两个方向各抓一种坏法：表里有、清单排不到＝那一行是死行（改措辞的人会以为它在屏幕上）；
    /// 清单排了、表里没登记＝屏幕上出现 <c>⟨glance.festival.xxx⟩</c>，而<b>通用表闸门看不见</b>——
    /// 它比对的是表里已有的行，不知道有人正在问一颗没登记的。</para>
    /// </summary>
    [Fact]
    public void EveryFestivalRowIsReachableAndNothingReachableIsUnlisted()
    {
        var rows = FestivalRows();
        Assert.Equal(49, rows.Length);   // 本批落表读数（2026-10-09）；⛔ 抄进别的档当判据

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2032, 12, 31);
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.True(GlanceCalendar.FestivalRuleCount(language) > 0, $"{language} 那一档没排清单");
            for (var day = from.DayNumber; day <= to.DayNumber; day++)
                if (GlanceCalendar.FestivalMessage(DateOnly.FromDayNumber(day)) is { } hit)
                    reached.Add(hit.Key);
        }

        var unreachable = rows.Except(reached, StringComparer.Ordinal).ToList();
        var unregistered = reached.Except(rows, StringComparer.Ordinal).ToList();
        Assert.True(unreachable.Count == 0,
            "这些行进了表却任何一档都排不到（要么清单漏了，要么它本来就是颗死行）：\n  " + string.Join("\n  ", unreachable));
        Assert.True(unregistered.Count == 0,
            "清单排出了没登记的键（屏幕上会是 ⟨…⟩，而表闸门只会说'表里没这颗'）：\n  " + string.Join("\n  ", unregistered));
    }

    /// <summary>
    /// <b>按代码实际给的实参渲染一次</b>（坑表 #393：那颗红只有这一格看得见）。
    /// <para>本批那一颗 <c>nameAnniv</c> 的同族形状是：一句里两个槽，实参却只递一颗拼好的字符串 ⇒
    /// 屏幕上那一格<b>整空白</b>，而通用表闸门全绿（它只数 <c>{n}</c> 集合，不知道调用点递了几颗）。
    /// 所以这里<b>从源码里数出实参数</b>，再拿那个数真渲染一遍，三档各跑一次：⛔ 把槽数抄成一个顺眼的常数。</para>
    /// </summary>
    [Fact]
    public void EachReadoutSentenceRendersWithTheArgsTheCodeActuallyHands()
    {
        var widget = CodeOf(GlanceWidgetCs);
        foreach (var key in new[] { "widget.glance.lunarDay", "widget.glance.lunarDayWithFestival", "widget.glance.nextFestival" })
        {
            var handed = ArgCount(widget, key);
            var slots = SlotCount(key);
            Assert.True(handed == slots, $"{key}：表里那句要 {slots} 个槽，代码只递 {handed} 颗实参（屏幕上会是空白或剩 {{0}}）");
        }

        // 真渲染：实参的<b>种类</b>也和代码一致（{0} 交的是还没念出来的那颗键，⛔ 交字符串）
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var next = GlanceCalendar.NextFestival(new DateOnly(2026, 10, 10));
            Assert.NotNull(next);
            var line = Locale.Fmt("widget.glance.nextFestival", next!.Name, next.Days + 1, next.Date.Month, next.Date.Day);
            Assert.False(string.IsNullOrWhiteSpace(line), $"{language} 档那句倒计时是空的");
            Assert.DoesNotContain("⟨", line, StringComparison.Ordinal);
            Assert.DoesNotContain("{", line, StringComparison.Ordinal);          // 槽没填上＝那一格原样带着花括号
            Assert.Contains(next.Name.Render(), line, StringComparison.Ordinal);  // 名字是<b>贴上去那一刻</b>才念的
            Assert.Contains((next.Days + 1).ToString(), line, StringComparison.Ordinal);
        }

        // 两颗槽的那一句：⛔ 在这里抄一颗"2026-10-01＝农历八月廿一"那种手算锚点（规则：锚点⛔ 凭记忆打）。
        // 判据是<b>两颗读数各进对的那一格</b>——日序由 LunarText 现算，再按它在句子里的位置与节日名比先后。
        Locale.TrySet(AppLanguage.Chinese);
        var day = new DateOnly(2026, 10, 1);
        var lunarToday = GlanceCalendar.LunarText(day);
        var festivalToday = GlanceCalendar.FestivalMessage(day);
        Assert.NotNull(festivalToday);
        var both = Locale.Fmt("widget.glance.lunarDayWithFestival", lunarToday, festivalToday!);
        Assert.Contains(lunarToday, both, StringComparison.Ordinal);
        Assert.Contains(festivalToday!.Render(), both, StringComparison.Ordinal);
        Assert.DoesNotContain("{", both, StringComparison.Ordinal);
        // ⛔ 在这里抄表里的措辞（"农历…·…"）当期望值：那会把"只改说法"判成缺陷（规则 #130）。
        // 判据只到"两颗读数各进对的那一格"为止。
        Assert.True(both.IndexOf(lunarToday, StringComparison.Ordinal) < both.IndexOf(festivalToday!.Render(), StringComparison.Ordinal),
            "两颗实参调了个（日序与节日名换了格）");
    }

    /// <summary>
    /// 这一族的宿主⛔ 留自己的汉字面字（日志除外），<c>XAML</c> 也⛔ 留"没人读的抄本"：
    /// 那四句示例字（"2026年9月／星期五／农历八月初八"）由 <c>RefreshDate()</c> 每次重写，
    /// 挂着＝同一句话两个出处，而它⛔ 换语言（✅WI-53 天气那 4 句、✅WI-2n 同一颗判据）。
    /// </summary>
    [Fact]
    public void TheHostAndItsMarkupCarryNoWordsOfTheirOwn()
    {
        var left = CjkLines(CodeOf(GlanceWidgetCs));
        var nonLog = left.Where(line => !line.Contains("StarLog", StringComparison.Ordinal)).ToList();
        Assert.True(nonLog.Count == 0,
            $"速览 code-behind 还剩 {nonLog.Count} 行自己的汉字面字（日志除外）：\n  " + string.Join("\n  ", nonLog));
        Assert.Equal(2, left.Length);   // 那两行日志的读数（2026-10-09 现场数过；日志⛔ 上屏，口径见 #368）

        var markup = SourceGate.ReadRepoFile(GlanceWidgetXaml).Replace("\r\n", "\n");
        markup = Regex.Replace(markup, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var offenders = Regex.Matches(markup, @"(Text|Content|Header|ToolTipService\.ToolTip|PlaceholderText)=""([^""]*)""")
            .Cast<Match>()
            .Where(m => Cjk.IsMatch(m.Groups[2].Value))
            .Select(m => m.Value)
            .ToArray();
        Assert.True(offenders.Length == 0,
            "标记里又留下了面字（那一处的主人应当唯一）：\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>热榜那三句住在"画"那一半，⛔ "读"那一半</b>。
    /// <para>换语言那一刻重贴的是<b>已经拿到手的那一份</b>：读那一趟要是在 <c>Locale.Changed</c> 回调里跑，
    /// 切一次语言就多一次网络往返、多一条失败日志，还可能把热榜变成"没拿到：超时"（坑表 #386 那一族的另一半）。
    /// 而"抓到了但今天没有"与"没抓到"是两颗不同的事实，⛔ 再合成一颗（P-54/P-56 口径）。</para>
    /// </summary>
    [Fact]
    public void TheTrendingCopyIsPaintedFromTheCachedResultAndTheFetchStaysWordless()
    {
        var widget = CodeOf(GlanceWidgetCs);
        var paint = SourceGate.MethodBody(widget, "private void PaintTrending()");
        var row = SourceGate.MethodBody(widget, "private Button BuildTrendingRow(");
        // 两句状态住在铺那一半，行提示在<b>建行</b>那一半（它由 PaintTrending 逐行调用）：
        // 判据是"整个画侧取词、读侧不取词"，⛔ 把三句都塞进同一颗方法体（那会把合法的搬家判成缺陷）。
        Assert.Contains("widget.glance.trending.none", paint, StringComparison.Ordinal);
        Assert.Contains("widget.glance.trending.failed", paint, StringComparison.Ordinal);
        Assert.Contains("widget.glance.trending.rowTip", row, StringComparison.Ordinal);
        Assert.Contains("BuildTrendingRow(", paint, StringComparison.Ordinal);

        foreach (var half in new[] { paint, row })
        {
            Assert.DoesNotContain("LoadTrendingAsync", half, StringComparison.Ordinal);
            Assert.DoesNotContain("GetRequiredService", half, StringComparison.Ordinal);   // ⛔ 顺手再读一次设置档
        }

        var fetch = SourceGate.MethodBody(widget, "private async System.Threading.Tasks.Task LoadTrendingAsync(bool force = false)");
        Assert.Equal(0, SourceGate.Count(fetch, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(fetch, "Locale.Fmt("));
        Assert.Equal(1, SourceGate.Count(fetch, "PaintTrending()"));
        Assert.Contains("_trendingShown = null;", fetch, StringComparison.Ordinal);      // 失败与"今天没有"分开写
        // 失败那句交的是<b>键</b>（批次 WI-58／P-190）：读侧⛔ 在这里取词，也⛔ 自拼一份"抓取失败：…"——
        // 那份句子主人在 `TrendingFailure.Of`（主窗热榜页与 Core 的 stale 那一路问的是同一颗）。
        Assert.Contains("_trendingFailure = StarMark.Abstractions.Trending.TrendingFailure.Of(ex);", fetch, StringComparison.Ordinal);
        Assert.DoesNotContain("_trendingFailure = ex.Message;", fetch, StringComparison.Ordinal);

        // ⚠ 那几颗事实<b>怎么连</b>也⛔ 由代码写一颗标点（坑表 #371 那颗分隔符的同族：本批普查当场量到）。
        // 骨架的主人＝`trending.statusNotice`（主窗热榜页念的是同一颗），它的英文列本来就是半角竖线；
        // 代码里再写一颗全角「｜」＝第二份出处，而且英文句子中间会冒出一颗 CJK 宽度的符号。
        Assert.DoesNotContain("string.Join(\"｜\"", widget, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"trending.statusNotice\"", paint, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>那颗连接符按语言各给一种排版</b>（与上面那条形状禁项<b>成对</b>，台架规则 #124：
    /// 只钉形状的话，有人把表里英文列也改成全角就没人说了；只钉读数的话，有人把 Join 写回代码也照样绿）。
    /// <para>判据是<b>字符宽度这一类</b>而⛔ 抄那一列的标点：拉丁那一档⛔ 出现全角标点，
    /// 日文那一档<b>应当</b>出现全角标点（那是那一档的排版事实）。中文档不参与这一格——它与日文同形，
    /// 而"同形"这件事已经在日文列闸门那里登记过。</para>
    /// </summary>
    [Fact]
    public void TheNoteSeparatorIsHalfWidthOnlyWhereTheLanguageIsLatin()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.English);
            var en = Locale.Fmt("trending.statusNotice", "A", "B");
            Assert.False(en.Any(c => c >= '　' && c <= '￯'),
                $"英文档那颗连接符带了 CJK 宽度的标点，屏幕上会是拉丁句子里夹一颗全角符号：{en}");

            Locale.TrySet(AppLanguage.Japanese);
            var ja = Locale.Fmt("trending.statusNotice", "A", "B");
            Assert.True(ja.Any(c => c >= '　' && c <= '￯'),
                $"日文档那颗连接符⛔ 是全角标点了（那一档的排版就是这么写）：{ja}");
        }
        finally { Locale.TrySet(before); }
    }

    /// <summary>
    /// 本批每一颗键都得<b>真的有人问</b>，且"哪天算节日"这一族<b>只有清单一个主人</b>：
    /// 月历圆点走的是同一颗 <c>FestivalMessage</c>，别处再排一份日期表＝屏幕上同一件事两套念法（✅P-179 同形口径）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsAskedForAndTheFestivalFamilyHasOneOwner()
    {
        var asked = string.Join("\n",
            CodeOf(GlanceWidgetCs), SourceGate.ReadRepoFile(GlanceWidgetXaml), CodeOf(GlanceCalendarCs));
        var unasked = ShippedSentences.Where(key => !asked.Contains("\"" + key + "\"", StringComparison.Ordinal)).ToList();
        Assert.True(unasked.Count == 0, "这些行进表却没人问（要么没接线，要么表里留了颗死行）：\n  " + string.Join("\n  ", unasked));
        Assert.Equal(6, ShippedSentences.Length);
        Assert.Equal(ShippedSentences.Length, ShippedSentences.Distinct(StringComparer.Ordinal).Count());

        var owners = SourceGate.ReadRepoUnder("src")
            .Where(x => x.Text.Contains("\"glance.festival.", StringComparison.Ordinal)
                        && !x.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(x => x.RelativePath.Replace('\\', '/'))
            .ToList();
        Assert.Equal(new[] { GlanceCalendarCs }, owners);   // 问节日键的档⛔ 多起来（月历与倒计时必须念同一张清单）
    }

    /// <summary>
    /// <b>⛔ 为农历成品词登记假翻译</b>：月名／日名是数据，只在 <c>ShowsLunarDayText</c> 为真的那一档露面
    /// （同 <c>LocOption.SelfName</c> 那一族"本来就不该翻的字"）。这一条钉的是<b>形状</b>：
    /// 表里不许出现 <c>glance.lunar*</c> 那一族键——将来有人想给「正月初一」配英文，得先回答"谁在屏幕上读它"。
    /// </summary>
    [Fact]
    public void TheLunarDayNamesStayDataAndNeverBecomeTableRows()
    {
        var table = SourceGate.ReadRepoFile("src/StarMark.Abstractions/I18n/UiStrings.cs");
        Assert.Empty(Regex.Matches(table, @"new Entry\(""glance\.lunar"));
        // 「农历」那三个字本身是界面文案，它住表里（这一颗⛔ 是数据）
        Assert.Contains("new Entry(\"widget.glance.lunarDay\"", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(", SourceGate.MethodBody(CodeOf(GlanceCalendarCs),
            "public static string LunarText(DateOnly date)"), StringComparison.Ordinal);
    }

    /// <summary>表里⛔ 有控制字符（换行是布局，不是文案；规则 #100）。49＋6 颗逐颗问。</summary>
    [Fact]
    public void NoRowOfThisBatchCarriesAControlCharacter()
    {
        foreach (var key in FestivalRows().Concat(ShippedSentences))
        {
            Assert.True(UiStrings.TryGet(key, out var entry), key + " 不在表里");
            foreach (var column in new[] { entry.Zh, entry.En, entry.Ja })
                Assert.False(column.Any(char.IsControl), $"{key} 那一列带了控制字符：{column}");
        }
    }

    /// <summary>抹掉注释后带着汉字的行（<see cref="CodeOf"/> 已抹过注释，这里只筛行）。</summary>
    private static string[] CjkLines(string code)
        => code.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && Cjk.IsMatch(line))
            .ToArray();

    /// <summary>表里那一行声明了几个槽（取 <c>{n}</c> 的去重数；三列必须同数，这里顺手反证一次）。</summary>
    private static int SlotCount(string key)
    {
        Assert.True(UiStrings.TryGet(key, out var entry), key + " 不在表里");
        var counts = new[] { entry.Zh, entry.En, entry.Ja }
            .Select(c => Regex.Matches(c, @"\{\d+\}").Cast<Match>().Select(m => m.Value).Distinct().Count())
            .ToArray();
        Assert.True(counts.Distinct().Count() == 1, $"{key} 三列的槽数不同形：{string.Join('/', counts)}");
        return counts[0];
    }

    /// <summary>
    /// 源码里那句 <c>Locale.Fmt("key", …)</c> 到底递了几颗实参——<b>按顶层逗号数，⛔ 抄常数</b>。
    /// <para>切到与之配对的右括号才停：嵌套的 <c>next.Date.Month</c> 里没有逗号，而 <c>foo(a, b)</c> 那种
    /// 实参自带括号的形状必须算进去，否则这一格会把 3 读成 2。</para>
    /// </summary>
    private static int ArgCount(string code, string key)
    {
        var anchor = $"Locale.Fmt(\"{key}\"";
        var at = code.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{anchor} 在代码里找不到（调用点搬家了？证人要先跟上）");
        var i = at + anchor.Length;
        var depth = 0;
        var commas = 0;
        for (; i < code.Length; i++)
        {
            var ch = code[i];
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                if (depth == 0) break;
                depth--;
            }
            else if (ch == ',' && depth == 0) commas++;
        }
        Assert.True(i < code.Length, $"{key} 那句没找到与之配对的右括号");
        return commas;   // 逗号数＝键之后的实参数
    }
}
