#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-61b（P-188 第三刀·第一片）的闸门：<b>设置页「拓展功能」那一屏的七行状态只许住在表里</b>。
/// <para>
/// 射程＝同一栏里那七行互相解释的话：热榜开关那行、RSS 开关那行、屏幕画布那行、截屏那行、
/// 本地磁盘搜索那两行（状态＋失联扫描），外加那颗只在该重跑准备时才露面的「重试」按钮名。
/// 从前它们由 VM 在<b>算出来那一刻</b>拼成整句（<c>TrendingStatus = "已开启：导航栏…"</c>、
/// <c>LocalDiskSearchStatus = $"已开启，但准备 Everything 失败：{ex.Message}"</c>），
/// 换语言那一刻没人重念——屏幕上留的是"上一次算话那一刻的语言"（P-188 确证②）。
/// </para>
/// <para>
/// <b>这一族独有的一条发现</b>：同一颗开关的状态句有<b>两个作者</b>——「进页面回灌」那一条与「开关回调」那一条
/// 各写了一份措辞极近的句子（「右侧有」vs「右侧现在有」、「不需要 Token」vs「不需要 Token（读侧匿名）」）。
/// 翻位之后那一行会静默换一种说法，而这两条句子的差异<b>本身就是缺陷</b>（同一件事两个出处，改一漏一）。
/// 现在两处同指一颗键，颗数由 <see cref="TheTwoWritersOfOneSwitchSayOneSentence"/> 钉住。
/// </para>
/// <para>
/// 机制上这批⛔ 新东西：VM 交「键＋实参」＋宿主 <c>LocText.SetMessage</c> 是 ✅WI-37-B（护眼那两句）
/// 与 ✅WI-2k（状态读数那一族）已经走通的那条门；<b>设置页⛔ 因此成为第十位 <c>Locale.Changed</c> 主人</b>——
/// 重贴由 <c>LocText</c> 那唯一一处订阅负责，本页那份成对订阅仍然只念诊断那行（✅WI-57）。
/// 那颗按钮名是<b>固定的常驻词</b> ⇒ 交 XAML 那颗键（⛔ 绕回 VM 递一句成品字）。
/// </para>
/// <para>⚠ <c>BackupRow.RestoreLabel</c> 那几行同属这一族的状态字，但它们是 <c>DataTemplate</c> 里的行
/// ⇒ 现成的门走不到（模板只带一颗固定键），整族欠 <b>P-175 缺口②</b> 那一刀；这里⛔ 替它占名额。</para>
/// <para>颗数一律现读（<c>bin/WI61/dmap_61b.txt</c>／<c>census_61b_post.txt</c>，2026-10-10 现场），⛔ 凭记忆（#159/#160）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class SettingsExtendWordGateTests : IDisposable
{
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string EyeRestCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.EyeRest.cs";
    private const string CanvasCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Canvas.cs";
    private const string CaptureCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Capture.cs";
    private const string LocalDiskCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.LocalDisk.cs";
    private const string RssCs = "src/StarMark.UI/Views/SettingsPage.Rss.cs";
    private const string EngineCs = "src/StarMark.UI/Views/SettingsPage.Engine.cs";
    private const string HostCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>这七行里的六行状态属性：每一颗都⛔ 再收一句成品字。</summary>
    private static readonly string[] Rows =
    {
        "TrendingStatus", "RssStatus", "CanvasStatus", "CaptureStatus",
        "LocalDiskSearchStatus", "MissingFileRowsStatus",
    };

    /// <summary>本批交进表里的键，逐颗点名（计数型判据抓不到"换名"＝#164）。</summary>
    private static readonly string[] Keys =
    {
        "retryPrepare", "canvasOn", "canvasOff", "trendingOn", "trendingOff", "glanceOnStatus", "glanceOffStatus",
        "rssOnStatus", "rssOffStatus", "captureOn", "captureOff", "diskOffStopped", "diskOffHint", "diskPreparing",
        "diskNotReady", "diskReady", "diskWillSearch", "diskNotConnected", "diskPrepareFailed",
        "engineDeleted", "engineSizeApprox", "engineNotInstalled", "missingNoRows", "missingScannedWindow",
        "missingScanned", "missingAllThere", "missingGone", "missingScanFailed",
        "rssAddBad", "rssDuplicated", "rssAdded", "rssRemoved",
    };

    /// <summary>两个作者共用一颗键的那几颗（其余每颗只有一个作者）。</summary>
    private static readonly string[] Shared = { "trendingOn", "trendingOff", "rssOnStatus", "rssOffStatus", "diskReady" };

    /// <summary>带机器事实的那几颗（键位／条数／字节／异常原文）：槽数＝实参数，读数⛔ 抄进句子。</summary>
    private static readonly (string Key, int Slots)[] ArgKeys =
    {
        ("canvasOn", 1), ("canvasOff", 1), ("captureOn", 3), ("captureOff", 3),
        ("diskPrepareFailed", 1), ("engineSizeApprox", 1), ("missingScannedWindow", 1),
        ("missingScanned", 1), ("missingAllThere", 1), ("missingGone", 2), ("missingScanFailed", 1),
        ("rssAddBad", 1), ("rssAdded", 1), ("rssRemoved", 1),
    };

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    private static string AllSources() => string.Join("\n", new[]
        { CodeOf(Vm), CodeOf(EyeRestCs), CodeOf(CanvasCs), CodeOf(CaptureCs), CodeOf(LocalDiskCs), CodeOf(RssCs), CodeOf(EngineCs) });

    private static string Of(string key) => "LocMessage.Of(\"settings.extend." + key + "\"";

    /// <summary>出现次数口径：整颗 needle 一起数（⛔ 按行/按字符类数，那是另一族假读数＝#149）。</summary>
    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>①形状禁项：那六颗属性⛔ 再收成品字，声明那一行逐颗点名。</summary>
    [Fact]
    public void ThoseSixRowsHandIdentitiesAndNotSentences()
    {
        var all = AllSources();
        foreach (var row in Rows)
        {
            Assert.Equal(0, Times(all, row + " = \""));
            Assert.Equal(0, Times(all, row + " = $\""));
        }

        Assert.Contains("[ObservableProperty] private LocMessage? _rssStatus;", CodeOf(EyeRestCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _trendingStatus;", CodeOf(EyeRestCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _canvasStatus;", CodeOf(EyeRestCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _captureStatus;", CodeOf(CaptureCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _localDiskSearchStatus;", CodeOf(LocalDiskCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _missingFileRowsStatus;", CodeOf(LocalDiskCs));
        foreach (var back in new[] { "_rssStatus = string.Empty", "_trendingStatus = string.Empty", "_canvasStatus = string.Empty",
                                     "_captureStatus = string.Empty", "_localDiskSearchStatus = string.Empty",
                                     "_missingFileRowsStatus = string.Empty" })
            Assert.DoesNotContain("private string " + back, all);

        // 「没话要说」与"这一组到底还有没有可删的行"必须是两件事：可见性⛔ 拿一句给人看的话做字符串判空
        Assert.Contains("HasMissingFileRowsNotice => MissingFileRowsStatus is not null;", CodeOf(LocalDiskCs));
        Assert.DoesNotContain("string.IsNullOrEmpty(MissingFileRowsStatus)", CodeOf(LocalDiskCs));
    }

    /// <summary>②宿主：XAML ⛔ 再绑那六颗（绑上去就是一句停在旧语言的字），改由那颗门贴。</summary>
    [Fact]
    public void TheMarkupStopsBindingThoseRowsAndNamesThePasteTargets()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        var host = CodeOf(HostCs);
        var targets = new[] { "TrendingStatusText", "RssSwitchStatusText", "CanvasStatusText", "CaptureStatusText",
                              "LocalDiskSearchStatusText", "MissingFileRowsStatusText" };
        foreach (var row in Rows) Assert.DoesNotContain("{x:Bind ViewModel." + row, xaml);
        foreach (var name in targets)
        {
            Assert.Contains("x:Name=\"" + name + "\"", xaml);
            Assert.Contains("LocText.SetMessage(" + name + ", ViewModel.", host);
        }
        // 颗数从上面那一串名字现算，⛔ 再抄一份前缀名单进正则：本批把那颗元素改名成 `RssSwitchStatusText`，
        // 正则没跟上就报"少贴了一行"——那读起来像缺陷在宿主，其实缺陷在判据自己有两份定位（同一动作一份定位＝规则 #86）。
        Assert.Equal(targets.Length, targets.Count(name => Times(host, "LocText.SetMessage(" + name + ", ViewModel.") == 1));
    }

    /// <summary>③那颗按钮名只有一个出处：XAML 那颗键；VM 里那份属性连名字都不许回来。</summary>
    [Fact]
    public void TheRetryButtonNameHasExactlyOneOwner()
    {
        Assert.DoesNotContain("LocalDiskSearchRetryLabel", CodeOf(LocalDiskCs));
        Assert.DoesNotContain("LocalDiskSearchRetryLabel", SourceGate.ReadRepoFile(PageXaml));
        Assert.Contains("i18n:LocText.Key=\"settings.extend.retryPrepare\"", SourceGate.ReadRepoFile(PageXaml));
        Assert.Single(UiStrings.All, e => e.Zh == "重试准备 Everything");
    }

    /// <summary>
    /// ④两颗作者、一颗键：回灌与开关回调各递一次话的那几颗必须同指一颗键；其余每颗只有一个作者。
    /// <para>颗数⛔ 凭记忆：写高当场红、写低永远绿 ⇒ 顺序＝先拿数 → 再钉 <c>== N</c>（#160）。</para>
    /// </summary>
    [Fact]
    public void TheTwoWritersOfOneSwitchSayOneSentence()
    {
        var all = AllSources();
        foreach (var key in Shared) Assert.Equal(2, Times(all, Of(key)));
        foreach (var key in Keys.Except(Shared).Except(new[] { "retryPrepare" }))
            Assert.Equal(1, Times(all, Of(key)));
        Assert.Equal(0, Times(all, Of("retryPrepare")));   // 那颗走 XAML 的常驻键，⛔ 有人从 VM 递它
    }

    /// <summary>⑤键名单双向对账：每颗在表里恰好一行、在源码里至少被点名一次（⛔ 死键，⛔ 有名字没行）。</summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedSomewhere()
    {
        Assert.Equal(32, Keys.Length);
        var sources = AllSources() + SourceGate.ReadRepoFile(PageXaml);
        foreach (var key in Keys)
        {
            var full = "settings.extend." + key;
            Assert.Single(UiStrings.All, e => e.Key == full);
            Assert.True(sources.Contains(Of(key)) || sources.Contains("i18n:LocText.Key=\"" + full + "\""),
                        "键 " + full + " 在表里，却没人点名");
        }
    }

    /// <summary>⑥语言读数：每颗带机器事实的键三语都答话、槽数＝实参数，而实参⛔ 被吞也⛔ 留在屏幕上露 {0}。</summary>
    [Fact]
    public void TheSentencesAnswerInEveryLanguageAndCarryTheMachineFact()
    {
        foreach (var (key, slots) in ArgKeys)
        {
            Assert.True(UiStrings.TryGet("settings.extend." + key, out var entry), "表里没有 settings.extend." + key);
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                var text = UiStrings.TextOf(entry, language);
                for (var i = 0; i < slots; i++) Assert.Contains("{" + i + "}", text);
                Assert.DoesNotMatch(@"\{" + slots + @"\}", text);
            }
            var args = Enumerable.Range(0, slots).Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                // ⛔ 拿 TrySet 的返回值当"换没换到"的探针：它说的是"变没变"（相同就回 false，那是设计如此）。
                //    要判"换到了"就读当前状态那一格。
                Assert.Equal(language, Locale.Current);
                var text = Locale.Fmt("settings.extend." + key, args);
                Assert.DoesNotContain("{", text);     // 槽没填满＝屏幕上露 {0}
                Assert.DoesNotContain("}", text);
                if (slots > 0) Assert.Contains("M0", text);   // 实参进了槽，⛔ 被句子吞掉
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }
    }

    /// <summary>
    /// ⑦这一屏⛔ 新增<b>一位主人</b>（那是 <c>I18nWiringGateTests.ExactlyOneSubscriberRenamesTheStringsAndItRebuildsNothing</c>
    /// 按<b>档</b>点名的东西），而它<b>已有</b>的那一位每开一次订阅都必须成对摘掉。
    /// <para><b>口径改口（批次 WI-40-3b）</b>：这一格从前钉的是"这一档只许有一处 <code>+=</code>"，那句话把两件不同的事
    /// 混成了一件——本档从来不是"⛔ 再订阅"（它自己就是主人之一），要防的是<b>挂了没人摘</b>（坑表 #386：这颗 VM 是
    /// AddTransient，每进一次设置页多一份订阅，而那些页面早就不在了）。「快捷键」那一栏模板行的重贴（P-175 缺口②）
    /// 因此加进<b>同一对块</b>：多一个 handler、多一条配对的 <code>-=</code>，主人名单一个字没变。
    /// 现在按<b>每一位 handler</b> 点名"挂一次、摘两次（Loaded 先退再订＋Unloaded 收尾）"，并把 Unloaded 那一体
    /// 必须同时摘掉两位也钉住——这一族真正的失效是那条 <code>-=</code> 没写，⛔ 靠"总数不许变"去挡。</para>
    /// </summary>
    [Fact]
    public void TheHostPairsEveryLocaleSubscriptionItOpens()
    {
        var host = CodeOf(HostCs);
        Assert.Equal(1, Times(host, "Locale.Changed += ViewModel.PaintDiagnostics"));
        Assert.Equal(2, Times(host, "Locale.Changed -= ViewModel.PaintDiagnostics"));   // 先退再订＋收尾那一次
        Assert.Equal(1, Times(host, "Locale.Changed += RepasteHotkeyRows"));            // 「快捷键」那一栏的重贴
        Assert.Equal(2, Times(host, "Locale.Changed -= RepasteHotkeyRows"));
        // 收尾那一体必须两位都摘：只摘一位＝另一位永远挂着，而那些行早就不在了
        var unloaded = SourceGate.MethodBody(host, "private void Page_Unloaded(object sender, RoutedEventArgs e)");
        Assert.Contains("Locale.Changed -= ViewModel.PaintDiagnostics;", unloaded, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= RepasteHotkeyRows;", unloaded, StringComparison.Ordinal);
        Assert.Contains("private void ApplyExtensionStatuses()", host);
        // 两个推它的时机缺一不可：进页面那一次（回灌出来的值没人推就一直是空的）＋那六颗属性任一变化时那一次。
        Assert.Equal(1, Times(host, "private void ApplyExtensionStatuses()"));
        Assert.Equal(2, Times(host, "ApplyExtensionStatuses();"));
    }

    /// <summary>
    /// ⑧这一格是本批<b>自己踩出来的</b>：状态句一度直接取名 <c>glanceOn</c>／<c>rssOn</c>，
    /// 而那一栏里这四位名字<b>早就被开关那颗 On/Off 标签占了</b>（XAML 的 <c>LocText.OnContentKey</c> 正引着它们）。
    /// <para>后果不是"看着有点乱"：这张表在建表那一刻就按码点比键，重复即抛 <c>InvalidOperationException</c>，
    /// 于是 <c>UiStrings</c> 的类型初始化器整个失败——<b>屏幕上任何一句都取不到词</b>，
    /// 而不是只有那两行坏。判据口径＝两类各恰好一行、且各自的去向不交叉（标签归 XAML、状态句归 VM）。</para>
    /// </summary>
    [Fact]
    public void TheStatusSentencesDoNotShadowTheToggleLabels()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        var all = AllSources();
        foreach (var (label, status) in new[]
                 { ("glanceOn", "glanceOnStatus"), ("glanceOff", "glanceOffStatus"),
                   ("rssOn", "rssOnStatus"), ("rssOff", "rssOffStatus") })
        {
            Assert.Single(UiStrings.All, e => e.Key == "settings.extend." + label);
            Assert.Single(UiStrings.All, e => e.Key == "settings.extend." + status);
            Assert.Contains("Key=\"settings.extend." + label + "\"", xaml);
            Assert.Equal(0, Times(all, Of(label)));   // 标签那颗⛔ 有人从 VM 递成状态句：一递就同时坏两处
        }
    }
}
