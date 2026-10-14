#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-61c（P-188 第三刀·第二片）的闸门：<b>设置页「数据」那一屏的状态读数只许住在表里</b>。
/// <para>
/// 射程＝这一屏那<b>六行</b>（WI-61c 交五行，<b>批次 WI-62 加上 <c>BackupStatus</c> 那一行</b>）里
/// <b>UI 自己算得出</b>的读数：写盘失败那句、自动备份那句（含"下一次约…"的续句）、
/// 「本机已有备份」那颗标题、剪贴板历史开关的状态句、清理孤儿的结果句、备份列表/导出的回执句。
/// 从前 VM 在<b>算出来那一刻</b>把整句拼好（<c>AutoBackupStatus = $"已开启：距上一份满 {hours} 小时…"</c>），
/// 换语言那一刻没人重念 ⇒ 屏幕上留的是"上一次算话那一刻的语言"（P-188 确证②）。
/// </para>
/// <para>
/// <b>本批刻意没动的四行</b>（<c>BackupStatus</c>／<c>UpdateStatus</c>／<c>ClipboardUsage</c>／<c>ClipboardImageStatus</c>）
/// 由 <see cref="TheFourCrossLayerRowsAreNeitherHalfTranslatedNorPasted"/> 钉住"没被半搬"：
/// 它们的话有一半由 Core／Abstractions 供（<c>BackupService.DeleteBackup</c>／<c>UpdatePolicy.Describe*</c>／
/// <c>ClipAssets.DescribeUsage</c>／<c>ClipboardPolicy.DescribeImageIntake</c>），只搬 UI 那一半就会出现
/// <b>同一行半中半英</b>——那种"看着像做完了"的状态比原缺陷更难发现（各自归 ✅P-193／P-194／P-188 剩族）。
/// </para>
/// <para>
/// <b>批次 WI-62 把这一族里的 <c>BackupStatus</c> 那一行整条翻完了</b>（Core 那半句与 UI 这半句同批交表，
/// 见 <c>BackupWordGateTests</c>）⇒ 上面那句"四行"从今天起是<b>三行</b>：名单里 <see cref="Deferred"/> 只剩
/// <c>UpdateStatus</c>／<c>ClipboardUsage</c>／<c>ClipboardImageStatus</c>，<c>BackupStatus</c> 升进
/// <see cref="Rows"/> 与 <see cref="Names"/>。那颗证人的<b>名字⛔ 跟着改</b>：✅WI-61c／✅WI-41 的文档与
/// <c>AiWordGateTests</c> 的注释都按旧名指着它，改名就等于留下一处读不到的指针（坑表 #422 同一族）——
/// 名字说的是"那四行当年一起被钉住"这件事，行数由 <see cref="Deferred"/> 自己报。
/// </para>
/// <para>
/// 机制上这批⛔ 新东西：VM 交「键＋实参」＋宿主 <c>LocText.SetMessage</c> 是 ✅WI-61b 第一片刚走通的那条门；
/// <b>设置页⛔ 因此成为第十位 <c>Locale.Changed</c> 主人</b>——本页那份成对订阅仍只念诊断那行（✅WI-57），
/// 重贴由 <c>LocText</c> 那唯一一处全局订阅负责。两句话的拼接也交表（<c>settings.data.autoBackupSentence</c>＝<c>{0}{1}</c>），
/// 因为代码里拼 <c>+</c> 就是把语序钉死在中文那一侧（✅WI-60b 那颗 <c>widget.mon.note.join</c> 同一口径）。
/// </para>
/// <para>颗数一律现读（<c>bin/WI61c/slots_61c.txt</c>／<c>bin/WI61c/pins_61c.txt</c>，2026-10-10 现场），⛔ 凭记忆（#159/#160）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class SettingsDataWordGateTests : IDisposable
{
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string HealthCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Health.cs";
    private const string ClipboardCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Clipboard.cs";
    private const string HostCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>这九行里的状态属性：每一颗都⛔ 再收一句成品字（第七、八行＝✅WI-64「图片占用」与「图片采集状态」；
    /// 第九行＝✅WI-65 片1「更新」那一行，它的话的一半住在 <c>UpdatePolicy.Describe*</c>）。</summary>
    private static readonly string[] Rows =
    {
        "SaveErrorMessage", "AutoBackupStatus", "BackupsHeader", "BackupStatus",
        "ClipboardHistoryStatus", "ClipboardCleanupStatus", "ClipboardImageStatus", "ClipboardUsage",
        "UpdateStatus",
    };

    /// <summary>宿主贴上去的那九颗元素名（XAML 里 <c>x:Name</c> 与 <c>SetMessage</c> 的第一实参逐颗同名）。</summary>
    private static readonly string[] Names =
    {
        "SaveErrorMessageText", "AutoBackupStatusText", "BackupsHeaderText", "BackupStatusText",
        "ClipboardHistoryStatusText", "ClipboardCleanupStatusText", "ClipboardImageStatusText", "ClipboardUsageText",
        "UpdateStatusText",
    };

    /// <summary>本批交进表里的键，逐颗点名（计数型判据抓不到"换名"＝#164）。</summary>
    private static readonly string[] Keys =
    {
        "autoBackupOff", "autoBackupHow", "autoBackupNoAuto", "autoBackupNext", "autoBackupDue", "autoBackupSentence",
        "backupsHeaderNone", "backupsHeaderCount",
        "clipHistoryOff", "clipHistoryRecording", "clipHistoryWatchMissing",
        "clipHistoryStarted", "clipHistoryListenerGone", "clipHistoryStopped",
        "cleanNoScan", "cleanNothing", "cleanCancelled", "cleanRunFailed",
        "cleanAllBlocked", "cleanDone", "cleanDoneWithTemp", "cleanPartlyBlocked", "cleanFailed",
        "saveNotWritten", "saveFailed",
    };

    /// <summary>带机器事实的那几颗（件数／小时数／回溯天数／分钟数／字节／异常原文）：槽数＝实参数，读数⛔ 抄进句子。
    /// <para><c>autoBackupSentence</c> 不在这一列：那颗 join 键三语打的<b>本来就是同一串</b> <c>{0}{1}</c>，
    /// 把它放进"三语读数必须互异"那一族会得到一条假缺陷——那颗键的判据另有 <see cref="TheJoinKeyAddsNothingButNestingRenders"/>。</para></summary>
    private static readonly (string Key, int Slots)[] ArgKeys =
    {
        ("autoBackupHow", 3), ("autoBackupNoAuto", 1), ("autoBackupNext", 1), ("autoBackupDue", 1),
        ("backupsHeaderCount", 1),
        ("cleanAllBlocked", 1), ("cleanDone", 2), ("cleanDoneWithTemp", 3), ("cleanPartlyBlocked", 2),
        ("cleanFailed", 1), ("saveNotWritten", 1), ("saveFailed", 1),
    };

    /// <summary>话的另一半在 Core／Abstractions、<b>当时⛔ 动</b>的那几行：钉住"没被半搬"。
    /// <para>WI-61c 立这颗闸门时这里坐着四行，第四行 <c>BackupStatus</c> 已在批次 WI-62 里<b>整条</b>翻完
    /// （Core 那半句与 UI 这半句同批），于是它升进 <see cref="Rows"/>／<see cref="Names"/>。
    /// 方法名里的"Four"⛔ 改：✅WI-61c／✅WI-41 的文档、<c>AiWordGateTests</c> 的注释与 ✅WI-59b 起的
    /// 各份收口读数都按那个名字指着它，改名等于留下一处读不到的指针（坑表 #422 同一族）——
    /// 名字说的是"那四行当年一起被钉住"这件事，<b>行数由这一颗名单自己报</b>。</para>
    /// <para>✅WI-64：<c>ClipboardUsage</c>／<c>ClipboardImageStatus</c> 两行整条走完；✅WI-65 片1 把最后一行
    /// <c>UpdateStatus</c>（＝P-194 那一族的第一颗）也两层同批走完 ⇒ <b>这一族在这里归零</b>。
    /// 名单空着⛔ 把闸门删掉：它仍是"下一行同形状的字不许半搬"的那道钉（空名单＝零处半搬，判据自己会报）。</para></summary>
    private static readonly string[] Deferred = { };

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    private static string AllSources() =>
        string.Join("\n", new[] { CodeOf(Vm), CodeOf(HealthCs), CodeOf(ClipboardCs) });

    private static string Of(string key) => "LocMessage.Of(\"settings.data." + key + "\"";

    /// <summary>出现次数口径：整颗 needle 一起数（⛔ 按行或按字符类数，那是另一族假读数＝#149）。</summary>
    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>①形状禁项：那五颗属性⛔ 再收成品字，声明那一行逐颗点名；回型⛔ 悄悄退回 <c>string</c>。</summary>
    [Fact]
    public void ThoseFiveRowsHandIdentitiesAndNotSentences()
    {
        var all = AllSources();
        foreach (var row in Rows)
        {
            Assert.Equal(0, Times(all, row + " = \""));
            Assert.Equal(0, Times(all, row + " = $\""));
        }

        Assert.Contains("[ObservableProperty] private LocMessage? _saveErrorMessage;", CodeOf(Vm));
        Assert.Contains("[ObservableProperty] private LocMessage? _autoBackupStatus;", CodeOf(Vm));
        Assert.Contains("public LocMessage? BackupsHeader => Backups.Count == 0", CodeOf(Vm));
        Assert.Contains("[ObservableProperty] private LocMessage? _backupStatus;", CodeOf(Vm));
        Assert.Contains("[ObservableProperty] private LocMessage? _clipboardHistoryStatus;", CodeOf(HealthCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _clipboardCleanupStatus;", CodeOf(ClipboardCs));
        // ✅WI-64 那两行：声明本身就是判据——回型一旦退成 <c>string</c>，屏幕上那两行就又冻在取词那一刻了。
        Assert.Contains("[ObservableProperty] private LocMessage? _clipboardImageStatus;", CodeOf(ClipboardCs));
        Assert.Contains("[ObservableProperty] private LocMessage? _clipboardUsage;", CodeOf(ClipboardCs));
        foreach (var field in new[]
                 { "_saveErrorMessage", "_autoBackupStatus", "_backupStatus", "_clipboardHistoryStatus", "_clipboardCleanupStatus",
                   "_clipboardImageStatus", "_clipboardUsage" })
            Assert.Equal(0, Times(all, "private string " + field));

        // "这一屏到底有没有一句要给人看"是另一件事：可见性⛔ 拿一句给人看的话做字符串判空（✅VQ 那条"两件事两颗 bool"）。
        Assert.Equal(0, Times(all, "IsNullOrEmpty(SaveErrorMessage)"));
        Assert.Equal(4, Times(all, "HasSaveError = "));               // 现场数：写成功复位、回灌复位、两处置真
    }

    /// <summary>②宿主：XAML ⛔ 再绑那六行（绑上去就是一句停在旧语言的字），改由那颗门贴。</summary>
    [Fact]
    public void TheMarkupStopsBindingThoseRowsAndNamesThePasteTargets()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        var host = CodeOf(HostCs);
        foreach (var row in Rows) Assert.DoesNotContain("{x:Bind ViewModel." + row, xaml);
        foreach (var name in Names)
        {
            Assert.Contains("x:Name=\"" + name + "\"", xaml);
            Assert.Contains("LocText.SetMessage(" + name + ", ViewModel.", host);
        }
        // 颗数从上面那一串名字现算，⛔ 再抄一份前缀名单进正则（同一动作只许一份定位＝规则 #86）。
        Assert.Equal(Names.Length, Names.Count(name => Times(host, "LocText.SetMessage(" + name + ", ViewModel.") == 1));
    }

    /// <summary>
    /// ③「已开启：多久一份」＋「下一次约什么时候」这两半<b>各是一句话</b>，拼接这件事也交表。
    /// <para>代码里 <c>how + $"下一次约…"</c> 那种写法把语序钉死在中文那一侧：英文／日文里"下一次"那句该在前还是该在后，
    /// 由那一门的句子自己决定，⛔ 由 C# 的 <c>+</c> 决定（✅WI-60b 那颗 <c>widget.mon.note.join</c> 同一口径）。</para>
    /// </summary>
    [Fact]
    public void TheTwoHalvesOfTheAutoBackupSentenceJoinThroughOneKey()
    {
        var vm = CodeOf(Vm);
        Assert.Contains("private static LocMessage Two(LocMessage head, LocMessage tail)", vm);
        Assert.Equal(1, Times(vm, Of("autoBackupSentence")));
        Assert.Equal(3, Times(vm, "Two(how, "));                       // 无自动件／未到点／已到点 三臂各接一次
        Assert.Equal(0, Times(vm, "how + "));                          // 从前那一半是代码里拼的
        Assert.Equal(1, Times(vm, Of("autoBackupHow")));
    }

    /// <summary>
    /// 那颗 join 键三语<b>本来就该是同一串</b> <c>{0}{1}</c>，而套娃必须真的取到词。
    /// <para>交给槽的是 <see cref="LocMessage"/> 本身（身份），格式化时靠它那句 <c>ToString() =&gt; Render()</c>
    /// 才按<b>当前</b>语言念出来。如果谁把 <c>Two(how, …)</c> 改成先 <c>.Render()</c> 再递字符串，
    /// 这句话就又在拼接那一刻定稿了——屏幕上那行换语言不动，而本文件其余判据全绿（＝P-188 那个原始缺陷的形状）。</para>
    /// </summary>
    [Fact]
    public void TheJoinKeyAddsNothingButNestingRenders()
    {
        Assert.True(UiStrings.TryGet("settings.data.autoBackupSentence", out var join), "表里没有这颗 join 键");
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Assert.Equal("{0}{1}", UiStrings.TextOf(join, language));
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);
            var head = LocMessage.Of("settings.data.backupsHeaderCount", 3);
            var tail = LocMessage.Of("settings.data.clipHistoryOff");
            var said = LocMessage.Of("settings.data.autoBackupSentence", head, tail).Render();
            Assert.Equal(Locale.Fmt("settings.data.backupsHeaderCount", 3) + Locale.Fmt("settings.data.clipHistoryOff"), said);
            Assert.DoesNotContain("{", said);
            Assert.DoesNotContain("settings.data.", said);          // ⛔ 把身份原样念给用户听
        }
        Assert.Equal(0, Times(CodeOf(Vm), "Two(how.Render()"));
    }

    /// <summary>④键名单双向对账：每颗在表里恰好一行、在源码里恰好被点名一次（⛔ 死键，⛔ 有名字没行，⛔ 两个作者）。</summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        Assert.Equal(25, Keys.Length);
        Assert.Equal(12, ArgKeys.Length);
        var all = AllSources();
        var host = CodeOf(HostCs);
        foreach (var key in Keys)
        {
            var full = "settings.data." + key;
            Assert.Single(UiStrings.All, e => e.Key == full);
            Assert.Equal(1, Times(all, Of(key)));
            Assert.Equal(0, Times(host, Of(key)));                    // 句子⛔ 由宿主递：那一侧只该拿身份去取词
        }
        // 带槽的那 12 颗与 ArgKeys 逐颗对平：颗数对不上＝有人加了一颗带读数的句子却没登记槽数
        Assert.Equal(ArgKeys.Length, Keys.Count(k => ArgKeys.Any(a => a.Key == k)));
    }

    /// <summary>⑤语言读数：每颗带机器事实的键三语都答话、槽数＝实参数，而实参⛔ 被吞也⛔ 留在屏幕上露 {0}。</summary>
    [Fact]
    public void TheSentencesAnswerInEveryLanguageAndCarryTheMachineFact()
    {
        foreach (var (key, slots) in ArgKeys)
        {
            Assert.True(UiStrings.TryGet("settings.data." + key, out var entry), "表里没有 settings.data." + key);
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
                Assert.Equal(language, Locale.Current);
                var text = Locale.Fmt("settings.data." + key, args);
                Assert.DoesNotContain("{", text);     // 槽没填满＝屏幕上露 {0}
                Assert.DoesNotContain("}", text);
                Assert.Contains("M0", text);          // 实参进了槽，⛔ 被句子吞掉
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }
    }

    /// <summary>
    /// ⑥巡查那颗分钟数走 <see cref="NumberText"/>：句子的槽带不了 <c>:0</c> 这种格式串（✅P-122 那条"显示侧数字格式收一处"）。
    /// <para>从前是 <c>$"…（{AutoBackupScheduler.ProbePeriod.TotalMinutes:0} 分钟内）…"</c>。交表之后槽只能收<b>已经成形的读数</b>，
    /// 如果调用点图省事直接把 <see cref="TimeSpan"/> 递进去，屏幕上就出现 <c>00:10:00</c> 或带小数点的原值——那是一句换了写法的读数。</para>
    /// </summary>
    [Fact]
    public void TheProbeMinutesReadingGoesThroughNumberText()
    {
        var status = SourceGate.MethodBody(AllSources(), "private LocMessage? BuildAutoBackupStatus()");
        Assert.Contains("NumberText.Grouped(AutoBackupScheduler.ProbePeriod.TotalMinutes)", status);
        Assert.Equal(0, Times(status, "TotalMinutes:0"));
        Assert.DoesNotContain("TotalMinutes, ", status);               // ⛔ 把 TimeSpan 本身递进槽
    }

    /// <summary>
    /// ⑦这一屏⛔ 新订阅者：重贴由 <c>LocText</c> 那唯一一处全局订阅负责；本页那份成对订阅仍只念诊断那行。
    /// <para>两颗推 <c>ApplyDataStatuses</c> 的时机缺一不可：进页面那一次（回灌出来的值没人推就一直是空的）＋
    /// 这六颗属性任一变化时那一次（<c>nameof</c> 逐颗点名＝计数型判据抓不到"换名"，#164）。</para>
    /// <para>方法名里那颗 <b>Five</b> 是 WI-61c 当时的行数，⛔ 跟着改：坑表与各批收口读数都按这个名字指着它
    /// （改完就是一处读不到的假指针＝坑表 #422 同一族）。行数由 <see cref="Rows"/> 自己报。</para>
    /// </summary>
    [Fact]
    public void TheHostPastesThoseFiveRowsFromTwoTimings()
    {
        var host = CodeOf(HostCs);
        Assert.Equal(1, Times(host, "private void ApplyDataStatuses()"));
        Assert.Equal(2, Times(host, "ApplyDataStatuses();"));
        foreach (var row in Rows) Assert.Equal(1, Times(host, "nameof(SettingsPageViewModel." + row + ")"));
        // 全仓计数型钉：这颗档里挂着的宿主贴字段数（✅WI-64 加那两行＝16；✅WI-65 片1 加「更新」那一行＝17；
        // ✅WI-67 片8 加「开机自动加载组件」那行取舍说明＝18）。
        // ⚠ 改这颗数⛔ 只改数：分配要一起写下来，下一批只看得到"18 ＝ 拓展功能 8 ＋数据屏 8 ＋更新那一行 1 ＋组件开机那行 1"这一条口径。
        Assert.Equal(18, Times(host, "LocText.SetMessage("));         // 拓展功能那 8 ＋这一屏 8 ＋「更新」那一行 1 ＋「开机自动加载」那一行 1
        // 这一档的 <c>Locale.Changed +=</c> 颗数⛔ 写死成"一颗"：设置页早就是这条事件的一位主人，
        // 而这一族真正的失效是<b>挂了没人摘</b>（坑表 #386），不是"订阅多了"——批次 WI-40-3b 给「快捷键」那一栏
        // 的重贴加进同一个成对块时，这里就红过一次（定向筛选器⛔ 覆盖邻族，只有全量揪得出来）。
        // "有哪几位 handler"只在那一颗闸门逐颗点名（<c>SettingsExtendWordGateTests.TheHostPairsEveryLocaleSubscriptionItOpens</c>），
        // ⛔ 在两处各抄一份名单：下次加一位就会只改到一半。这里只钉不变式——每位都是「挂一次、摘两次」。
        Assert.Equal(Times(host, "Locale.Changed +=") * 2, Times(host, "Locale.Changed -="));
    }

    /// <summary>
    /// ⑧<span><see cref="Deferred"/> 里那些行</span>要么整句走、要么整句不动：它们<b>仍由绑定贴</b>，且⛔ 有人替它们半贴。
    /// <para>形状＝同一行里 UI 那半换了语言、Core 那半停在上一门 —— 那种"半个交表"会让 P-193／P-194 的下一批
    /// 以为这里已经做过，也会让用户在同一行里读到两种语言（✅WI-40-0 定口径时把这一族划出射程的原始理由）。</para>
    /// </summary>
    [Fact]
    public void TheFourCrossLayerRowsAreNeitherHalfTranslatedNorPasted()
    {
        Assert.Empty(Deferred);   // ✅WI-65 片1 之后归零：BackupStatus 由 WI-62 走完、那两行由 WI-64 走完、更新那一行由本批走完（名与颗数一起钉＝#164）
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        var host = CodeOf(HostCs);
        var all = AllSources();
        foreach (var row in Deferred)
        {
            Assert.Contains("{x:Bind ViewModel." + row + ", Mode=OneWay}", xaml);
            Assert.Equal(0, Times(host, "LocText.SetMessage(" + row + "Text"));
            Assert.Equal(0, Times(host, "nameof(SettingsPageViewModel." + row + ")"));
            Assert.Equal(0, Times(all, Of(row)));
        }
    }
}
