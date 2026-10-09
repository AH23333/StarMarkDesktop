#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-57（P-187）的闸门：<b>设置页「诊断」那一卡上的字全部只许住在表里，而 Core 交出的是还没取词的那句</b>。
/// <para>
/// 射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝那一颗 Border 里的每一行：Core 的
/// <c>DiagnosticsService</c>（左列标签＋右列值句）、Abstractions 的 <c>SourceAvailabilityText</c>／
/// <c>SourceNames</c>／<c>IItemSource</c>（"源：X"那一行念的两格与它的接口形状）、
/// 四个供话者递出的<b>成因句</b>，以及 VM 与那一行模板。
/// </para>
/// <para><b>为什么连 Integrations 那几句也一起交</b>：屏幕上那一行同时显示「源：Chrome 书签」与
/// 「没有这项数据：没在本机找到…」——只翻外层就是半中半英（✅WI-46a"未交完的族"那颗教训）。
/// 反过来 <c>StartupProfile</c> 的刻度行<b>本批判为日志摘录⛔ 交表</b>：它的用途是让人把面板上那一行
/// 与日志里那一行<b>逐字对上</b>，翻了就切断这条对账路（取舍与代价登记在账本 P-191）。</para>
/// <para><b>供话者那四档只圈住 <c>AvailabilityHint</c> 那颗方法体</b>（规则 #110：禁项按"会不会上这一屏"划射程）——
/// 同一档文件里还有卡片副标题、诊断日志那几族，它们各有主人，整档一刀切会咬到本批没交的人。</para>
/// <para>尺＝抹注释后逐行取连续汉字片段、并<b>排除日志头</b>那一行（与 <c>bin/WI48/census_core.py</c> 同一把；
/// 登记剩余量必须把尺子写进句子＝规则 #126）。数字来自 <c>bin/WI57/probe_57c.py</c> 这一跑，⛔ 凭记忆（#129）。</para>
/// </summary>
public sealed class DiagnosticsWordGateTests : IDisposable
{
    private const string DiagnosticsCs = "src/StarMark.Core/Diagnostics/DiagnosticsService.cs";
    private const string AvailabilityCs = "src/StarMark.Abstractions/SourceAvailabilityText.cs";
    private const string SourceNamesCs = "src/StarMark.Abstractions/SourceNames.cs";
    private const string IItemSourceCs = "src/StarMark.Abstractions/IItemSource.cs";
    private const string HealthCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Health.cs";
    private const string SettingsPageCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string SettingsPageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string TableCs = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    /// <summary>把成因句交进表的那四档供话者（现场 grep 出来的，⛔ 手抄名单——真漏过一颗的那次是 #367）。</summary>
    private static readonly string[] SupplierFiles =
    [
        "src/StarMark.Integrations/Bookmarks/BrowserBookmarksSource.cs",
        "src/StarMark.Integrations/Ditto/DittoSource.cs",
        "src/StarMark.Integrations/Everything/EverythingQueryQueue.cs",
        "src/StarMark.Integrations/GitHub/GitHubSource.cs",
    ];

    /// <summary>整档口径的那几档：这一族的<b>全部</b>屏幕上字都归本批交，所以整档=0 是真话。</summary>
    private static readonly string[] WholeFileZero = [DiagnosticsCs, AvailabilityCs, SourceNamesCs, IItemSourceCs, HealthCs];

    /// <summary>
    /// 尺的字符类＝<b>汉字＋假名＋全角那一族</b>（与普查尺 <c>bin/WI48/census_core.py</c> 同一份）。
    /// <para>⛔ 只写汉字与假名：那正是坑表 #403 那颗盲区的形状——<c>string.Join("、", …)</c> 与
    /// <c>$"{Title}（{Count}）"</c> 都能从"en 列⛔ CJK"那颗通用闸门底下走过去，而屏幕上是一句拉丁文里
    /// 夹着的中文标点。尺与管理口径不同类，量到却判不出来＝假绿（本批 Health.cs 那一行就是这么抓到的）。</para>
    /// </summary>
    private static readonly Regex Cjk = new(@"[぀-ヿ㐀-䶿一-鿿　-〿＀-￯]+", RegexOptions.Compiled);
    private static readonly string[] LogHeads = ["StarLog.", "Debug.", "Trace."];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    // 两颗尺升到共用件（台架规则 #86）：断言一个字没动，只是实现⛔ 两处各写一份。
    private static int SlotCount(string key) => SourceGate.RowSlots(key);

    private static List<(string Key, int Args, int Line)> CallSites(string code) => SourceGate.KeyedCallSites(code);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>
    /// 屏幕字计数口径：<b>抹注释后逐行</b>取汉字片段，<b>但排除日志头</b>那一行
    /// （<c>StarLog.Error($"诊断信息采集失败: …")</c> 上不了屏幕；日志按既定口径⛔ 交表）。
    /// </summary>
    private static List<string> ScreenWords(string text)
    {
        var hits = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var head = line.Split('"')[0];
            if (LogHeads.Any(h => head.Contains(h, StringComparison.Ordinal))) continue;
            hits.AddRange(Cjk.Matches(line).Cast<Match>().Select(m => m.Value));
        }
        return hits;
    }



    /// <summary>
    /// <b>Core 与 Abstractions 交出的是键，⛔ 在那里取词</b>（✅WI-53 天气码／✅WI-54 倒计时同一条口径）。
    /// <para>取词只有发生在"贴上去的那一刻"才谈得上换语言就换字；在 <c>CollectAsync</c> 里 <c>Render()</c> 一次，
    /// 语言就被冻在采集那一瞬——而那一趟里有 <c>COUNT(*)</c> 全表扫描，没人会为了换语言再跑它。</para>
    /// </summary>
    [Fact]
    public void TheCollectorsNeverTakeWordsThemselves()
    {
        foreach (var rel in new[] { DiagnosticsCs, AvailabilityCs, SourceNamesCs })
        {
            var code = CodeOf(rel);
            foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
                Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>量出来的剩余量＝0</b>：整档那五档、以及四档供话者各自的 <c>AvailabilityHint</c> 方法体里，
    /// 非日志的屏幕上汉字片段一颗都不剩（口径见 <see cref="ScreenWords"/>）。
    /// <para>⚠ 这条与下面那颗语言读数是<b>成对</b>的（规则 #124）：只有这一条的话，"把句子又拼回代码里"
    /// 会被计数抓住，但"键还在问、实参换了"那一族它看不见。</para>
    /// </summary>
    [Fact]
    public void ChineseSurvivesOnlyInTheLogLines()
    {
        foreach (var rel in WholeFileZero)
        {
            var words = ScreenWords(CodeOf(rel));
            Assert.True(words.Count == 0, $"{rel} 仍留着非日志的汉字片段 {words.Count} 处：{string.Join(" / ", words.Take(5))}");
        }

        foreach (var rel in SupplierFiles)
        {
            var body = SourceGate.MethodBody(CodeOf(rel), "public LocMessage? AvailabilityHint");
            var words = ScreenWords(body);
            Assert.True(words.Count == 0, $"{rel} 的成因句里还有汉字：{string.Join(" / ", words.Take(5))}");
        }
    }

    /// <summary>
    /// <b>本批落表的每一行，代码里真的在问它</b>（键名逐颗 grep 整棵 <c>src</c>，⛔ 只查本批那三档——
    /// 供话者的键住在 Integrations 里被问）。反向那一半由下面那颗可达性判据管。
    /// <para><b>语料必须把表自己排除掉</b>：第一跑没排除，于是"表里这行没人问"这句话被<b>表里那一行自己</b>
    /// 满足了——台架 A2 当场把它红成"没红"（通用那颗 <c>TheTableHasNoRowNothingAsksFor</c> 倒是咬住了，
    /// 这条家族的只是仪器自己骗人＝台架规则 #95/#102 的"每颗新证人要有一格只被它咬住"没做到）。</para>
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsAskedFor()
    {
        var rows = RowsOfThisFamily();
        Assert.True(rows.Length >= 30, $"本批落表的那一族只读到 {rows.Length} 行（名单形状一定漂了，⛔ 把\"没红\"当\"过了\"）");
        var corpus = SourceGate.ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .ToList();
        // 语料的形状要自己作证：收得到宿主、且真的没把表收进来（内容层⛔ 当判据——仓里另有两颗不相关的 `Entry`）。
        Assert.Contains(corpus, f => f.RelativePath.EndsWith("DiagnosticsService.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(corpus, f => f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal));
        var all = string.Join("\n", corpus.Select(f => f.Text));
        var unasked = rows.Where(key => !all.Contains("\"" + key + "\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Array.Empty<string>(), unasked);
    }

    /// <summary>
    /// <b>来源身份的键族必须双向对账</b>（规则 #111）：五颗注册身份各映射一颗自己的行，认不得的那条要
    /// 把身份本身念出来；表里这个键族的行**不多不少**就是这六颗——⛔ 拼 <c>"source.name." + id</c>，
    /// 因为成串的键名暴露不了"这一行永远走不到"。
    /// </summary>
    [Fact]
    public void EverySourceIdentityMapsToItsOwnRowAndUnknownOnesSaySo()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ItemSources.GitHub] = "source.name.github",
            [ItemSources.Chrome] = "source.name.chrome",
            [ItemSources.Edge] = "source.name.edge",
            [ItemSources.Ditto] = "source.name.ditto",
            [ItemSources.FileSystem] = "source.name.filesystem",
        };
        foreach (var (id, key) in expected)
        {
            Assert.Equal(key, SourceNames.NameKeyOf(id));
            var name = SourceNames.NameOf(id);
            Assert.Equal(key, name.Key);
            Assert.Null(name.Args);   // 认得的名字不需要实参：那一颗槽都不许多出来
        }

        // 认不得的身份⛔ 静默缩成"其它来源"四个字：那颗行必须把身份本身念出来，否则报障时最想知道的那个数没了。
        var unknown = SourceNames.NameOf("firefox");
        Assert.Equal(SourceNames.OtherKey, unknown.Key);
        Assert.Equal(new object?[] { "firefox" }, unknown.Args);
        Assert.Equal("其它来源（firefox）", unknown.Render());

        var rows = Regex.Matches(SourceGate.ReadRepoFile(TableCs), "\"source\\.name\\.[A-Za-z0-9]+\"")
            .Cast<Match>().Select(m => m.Value.Trim('"')).Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Values.Append(SourceNames.OtherKey).OrderBy(k => k, StringComparer.Ordinal).ToArray(), rows);
    }

    /// <summary>
    /// <b>每一句带槽的话，都要按代码实际递出的那颗实参渲染一次</b>（坑表 #393 那一族：槽数对得上、
    /// 递过去的却是一整句拼好的串 ⇒ 屏幕上那一格整空白，而通用表闸门全绿）。
    /// </summary>
    [Fact]
    public void EveryCallHandsExactlyTheSlotsItsRowDeclares()
    {
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in WholeFileZero.Concat(SupplierFiles))
        {
            foreach (var (key, args, line) in CallSites(CodeOf(rel)))
            {
                var slots = SlotCount(key);
                watched++;
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        }
        Assert.True(watched >= 20, $"这一族只读到 {watched} 处带字面键的 LocMessage.Of 调用（少于 20），判据自己失效了");
        Assert.Equal(Array.Empty<string>(), offenders);

        // 直通行只许一颗槽，且真的原样过：它一多槽就会把"事实"与"句子"混成同一个形状。
        Assert.Equal(1, SlotCount("diag.fact"));
        Assert.Equal(@"C:\x\starmark.db", Locale.Fmt("diag.fact", @"C:\x\starmark.db"));
    }

    /// <summary>
    /// <b>那一行的两格同时上屏 ⇒ 两格都必须交键</b>，而"源："前缀与来源名字是<b>嵌套</b>关系
    /// （<c>LocMessage</c> 的实参在自己的 <c>ToString</c> 里按当下语言取词），⛔ 在 Core 里先念好再拼。
    /// <para>三语各念各的这格只有真跑一遍才看得见——嵌套那颗一旦在 Core 里 <c>Render()</c> 过一次，
    /// 形状禁项（上面那颗）抓不到它，因为代码看起来完全正常。</para>
    /// </summary>
    [Fact]
    public void TheSourceRowNestsItsNameAndRepaintsWithTheLanguage()
    {
        var code = CodeOf(DiagnosticsCs);
        Assert.Contains("LocMessage.Of(\"diag.label.source\", SourceNames.NameOf(source.SourceId))", code, StringComparison.Ordinal);
        Assert.DoesNotContain("源：", code, StringComparison.Ordinal);   // 前缀抄回代码＝第二个主人（P-171 那一族）

        foreach (var (language, expect) in new (AppLanguage, string)[]
                 {
                     (AppLanguage.Chinese, "源：Chrome 书签"),
                     (AppLanguage.English, "Source: Chrome bookmarks"),
                     (AppLanguage.Japanese, "ソース：Chrome ブックマーク"),
                 })
        {
            Locale.TrySet(language);
            Assert.Equal(expect, Locale.Fmt("diag.label.source", SourceNames.NameOf(ItemSources.Chrome)));
        }
    }

    /// <summary>
    /// <b>宿主那一侧：事实存着、念法每次现算，而"贴句子"只许出现在 <c>PaintDiagnostics</c> 里</b>。
    /// <para>⚠ 这一条与上面那颗语言读数<b>成对</b>（规则 #124）：计数型判据抓不到"又改回存成品字"——
    /// 那颗键还在被问，变的是它收到的实参。</para>
    /// </summary>
    [Fact]
    public void TheHostStoresFactsAndRendersThemOnlyInsideThePaint()
    {
        var code = CodeOf(HealthCs);
        Assert.Contains("ObservableCollection<DiagnosticRow> DiagnosticEntries", code, StringComparison.Ordinal);
        Assert.Contains("new DiagnosticRow(e.Label.Render(), e.Value.Render())", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservableCollection<StarMark.Core.Diagnostics.DiagnosticEntry>", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticsError = Locale.Fmt", code, StringComparison.Ordinal);   // 失败那句也走同一颗重贴门

        // 采集⛔ 被当成"换语言"的重算时机：那一趟里有全表扫描，而订阅方只许重贴。
        var paint = SourceGate.MethodBody(code, "public void PaintDiagnostics()");
        Assert.DoesNotContain("CollectAsync", paint, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", paint, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>订阅与摘掉成对</b>（坑表 #386）：这颗 VM 是 <c>AddTransient</c>，挂着不摘＝每进一次设置页多一份订阅、
    /// 而那些页面早就不在了。两条都读 <c>Code()</c>：注释里写一句"记得摘"不算数（规则 #131）。
    /// </summary>
    [Fact]
    public void ThePageSubscribesAndUnsubscribesAsAPair()
    {
        var code = CodeOf(SettingsPageCs);
        Assert.Equal(1, SourceGate.Count(code, "Locale.Changed += ViewModel.PaintDiagnostics;"));
        // 摘掉那颗出现两次是刻意的：Loaded 里"先退再订"（本页可能被反复导航回来）＋ Unloaded 收尾那一次。
        Assert.Equal(2, SourceGate.Count(code, "Locale.Changed -= ViewModel.PaintDiagnostics;"));
    }

    /// <summary>
    /// <b>那一行的模板绑的是 UI 侧那一格，⛔ 直接绑 Core 那颗存键的记录</b>：
    /// <c>x:Bind</c> 把类型不符的落点交给 <c>ToString()</c>，那就等于"绑定求值那一刻的快照"，
    /// 换语言再没人回去重贴（坑表 #272 那族"闭合显示不跟随语言"的另一张脸）。
    /// </summary>
    [Fact]
    public void TheMarkupBindsThePaintedRowNotTheStoredFacts()
    {
        var xaml = SourceGate.ReadRepoFile(SettingsPageXaml);
        Assert.Contains("<DataTemplate x:DataType=\"viewModels:DiagnosticRow\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("diag:DiagnosticEntry", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns:diag=", xaml, StringComparison.Ordinal);   // 那扇命名空间随绑定的落点一起搬走
    }

    /// <summary>本批落表那一族的键名（从<b>表</b>里读，⛔ 在本档再抄一份名单——抄的那份会跟着表落后）。
    /// <para>⚠ 段数必须放开到<b>任意多</b>：第一跑这里写的是 <c>\.[A-Za-z0-9]+</c>（只认两段），
    /// 于是 <c>diag.label.build</c> 那一族 17 颗全数没进名单，而名单短到 17 颗被上面那颗"少于 30 就红"的
    /// 自证当场抓住（台架规则 #121 的口径：判据的名单由 grep 生成，且 grep 的形状要自己带一道闸）。</para></summary>
    private static string[] RowsOfThisFamily()
        => Regex.Matches(SourceGate.ReadRepoFile(TableCs),
                "\"(?:diag|source\\.name|source\\.avail|source\\.hint)\\.[A-Za-z0-9.]+\"")
            .Cast<Match>().Select(m => m.Value.Trim('"')).Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
}
