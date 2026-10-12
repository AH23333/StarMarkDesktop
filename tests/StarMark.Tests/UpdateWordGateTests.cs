#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-65 片1（✅P-194 第一颗）：<b>设置页「更新」那一行的两层同批交表</b>。
/// <para>
/// 这一行从前是「Core 当场念完整句 → <c>UpdateReport.Text</c>（string）→ 界面 <c>{x:Bind}</c> 一颗成品串」。
/// 换语言那一刻没人重贴、也没人重算 ⇒ 整行停在上一门语言（这一族最典型的一处：<c>UpdatePolicy.Describe*</c> 里
/// 那 62 个念键点全在屏幕上那一行后面）。现在两层一起改：Core 交<b>还没取词的</b>「键＋实参」，
/// 载体（<c>UpdateReport.Text</c>／<c>ApplyResult.Sentence</c>）与 VM 那颗状态属性一路持 <see cref="LocMessage"/>，
/// 贴上屏幕走宿主那一拍（<c>ApplyUpdateStatus</c>），取词因此发生在<b>贴上去这一刻</b>。
/// </para>
/// <para>
/// 只进日志的那两颗（<c>DescribeManifestAge</c>／<c>Describe(UpdaterOutcome)</c>）<b>刻意保持 string</b>：
/// 它们上的不是屏幕，把它们一起改回型只会让"哪些句子该跟着语言走"这件事失去一条可读的边界（✅WI-65a 归桶 C）。
/// </para>
/// <para>锚点一律写成整条字面（坑表 #171/#179：注释与代码同名字面会数进同一笔）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class UpdateWordGateTests : IDisposable
{
    private const string Policy = "src/StarMark.Core/Updates/UpdatePolicy.cs";
    private const string Service = "src/StarMark.Core/Updates/UpdateService.cs";
    private const string Applier = "src/StarMark.Core/Updates/UpdateApplier.cs";
    private const string Scheduler = "src/StarMark.UI/Helpers/UpdateScheduler.cs";
    private const string VmCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Updates.cs";
    private const string HostCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Dtt = "src/StarMark.Abstractions/DateTimeText.cs";

    /// <summary>屏幕上那一族那九颗工厂（<b>⛔ 把只进日志的那两颗算进来</b>）。</summary>
    private static readonly string[] ScreenFactories =
    {
        "public static LocMessage Describe(UpdateVerdict",
        "public static LocMessage Describe(PackageFetchStatus",
        "public static LocMessage Describe(UpdateIntegrity.Outcome",
        "public static LocMessage Describe(StageStatus",
        "public static LocMessage Describe(LaunchStatus",
        "public static LocMessage Describe(ApplyStatus",
        "public static LocMessage Describe(ApplyPhase",
        "public static LocMessage Describe(DownloadProgress",
    };

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static int Times(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>①九颗上屏的工厂交「键＋实参」，两颗只进日志的照旧交字符串——这条边界要看得见。</summary>
    [Fact]
    public void TheScreenFactoriesHandOutUnrenderedSentencesWhileTheLogOnesStayStrings()
    {
        var policy = Code(ReadRepoFile(Policy));
        Assert.Equal(8, ScreenFactories.Count(s => Times(policy, s) == 1));
        Assert.Equal(8, Times(policy, "public static LocMessage Describe("));   // ⛔ 第十颗：多一颗就是有人把日志那句也搬了
        Assert.Equal(1, Times(policy, "public static LocMessage UpdateCancelledSentence"));
        Assert.Equal(1, Times(policy, "public static string Describe(UpdaterOutcome"));
        Assert.Equal(1, Times(policy, "public static string DescribeManifestAge"));
    }

    /// <summary>②那一族 62 处念键点全部交键（＝✅WI-65a 那把尺称出的屏面数），且这九颗里⛔ 剩一处当场念完。</summary>
    [Fact]
    public void NotOneScreenSentenceIsReadOutBeforeItReachesTheDisplay()
    {
        var policy = Code(ReadRepoFile(Policy));
        var handed = 0;
        foreach (var sig in ScreenFactories)
        {
            var body = MethodBody(policy, sig);
            Assert.Equal(0, Times(body, "Locale.T(") + Times(body, "Locale.Fmt("));
            handed += Times(body, "LocMessage.Of(");
        }
        handed += Times(MethodBody(policy, "public static LocMessage UpdateCancelledSentence"), "LocMessage.Of(");
        Assert.Equal(62, handed);   // 正本＝bin/WI65/census_65.txt A.1（屏面 62／日志 14）
    }

    /// <summary>③两处跨层载体换型：话从 Core 到界面这一路⛔ 有任何一格先念成字符串。</summary>
    [Fact]
    public void TheTwoCarriersBetweenLayersHoldKeys()
    {
        Assert.Equal(1, Times(Code(ReadRepoFile(Service)), "DateTimeOffset CheckedUtc, bool Announced, LocMessage Text"));
        Assert.Equal(1, Times(Code(ReadRepoFile(Applier)), "record ApplyResult(ApplyStatus Status, LocMessage? Sentence = null"));
        Assert.Equal(1, Times(Code(ReadRepoFile(Applier)), "private static ApplyResult NotApplied(LocMessage sentence, string detail)"));
    }

    /// <summary>④界面那一半（从前是四句裸字面量＋两句崩溃话）现在只剩键名：那几个字⛔ 再住在这档里。</summary>
    [Fact]
    public void TheUiHalfOfThatRowIsGoneWhole()
    {
        var vm = Code(ReadRepoFile(VmCs));
        foreach (var was in new[]
                 {
                     "自动检查开着", "已关闭：程序不会自己上网问", "这台机器上还没检查过",
                     "上次检查", "这次检查没走完", "这次更新没走完",
                 })
            Assert.Equal(0, Times(vm, was));
        Assert.Equal(1, Times(vm, "private LocMessage BuildUpdateStatus()"));
        Assert.Equal(1, Times(vm, "[ObservableProperty] private LocMessage? _updateStatus"));
        // 合句的三样都是「键＋实参」：节奏、那段相对时间、上一次问出的结局（⛔ 任何一样先 Render）
        Assert.Contains("LocMessage.Of(\"upd.status.lastCheck\", cadence,", vm, StringComparison.Ordinal);
        Assert.Equal(1, Times(vm, "DateTimeText.RelativeMessage("));
        Assert.Equal(0, Times(vm, "DateTimeText.Relative("));   // 念完那一版⛔ 还留在这条链上
    }

    /// <summary>⑤那一行不再绑成品串：XAML 交一颗元素名，宿主那一拍贴，且在属性变更与进页两个时机都走。</summary>
    [Fact]
    public void TheRowIsPastedByTheHostRatherThanBound()
    {
        var xaml = ReadRepoFile(PageXaml);
        Assert.Equal(0, Times(xaml, "{x:Bind ViewModel.UpdateStatus"));
        Assert.Equal(1, Times(xaml, "x:Name=\"UpdateStatusText\""));
        var host = Code(ReadRepoFile(HostCs));
        Assert.Equal(1, Times(host, "private void ApplyUpdateStatus()"));
        Assert.Equal(2, Times(host, "ApplyUpdateStatus();"));   // 进页一次 ＋ 那颗属性变更一次
        Assert.Equal(1, Times(host, "LocText.SetMessage(UpdateStatusText, ViewModel.UpdateStatus)"));
        // ⛔ 为这一行再挂第四位 Locale.Changed 主人：重贴由 LocText 那份名单负责（✅WI-40-4 那颗成对闸门盯着）
        Assert.Equal(Times(host, "Locale.Changed +=") * 2, Times(host, "Locale.Changed -="));
    }

    /// <summary>⑥提示卡那一路不再走数据门：Core 交来的那颗本身就是「键＋实参」，套一层＝把身份弄没。</summary>
    [Fact]
    public void TheNoticeCardTakesTheSentenceWholeRatherThanThroughTheDataDoor()
    {
        var scheduler = Code(ReadRepoFile(Scheduler));
        Assert.Equal(0, Times(scheduler, "common.rawValue"));
        Assert.Equal(1, Times(scheduler, "NoticeCard.Show(title, report.Text, actionUrl"));
    }

    /// <summary>⑦这颗批次的核心承诺：同一颗 <see cref="LocMessage"/> 在三种语言下各念一次，实参照样带得过去。</summary>
    [Fact]
    public void TheSameMessageRendersInWhateverLanguageIsCurrentAtPasteTime()
    {
        var m = UpdatePolicy.Describe(UpdateVerdict.UpToDate, null, "1.2.3");
        Locale.TrySet(AppLanguage.Chinese);
        var zh = m.Render();
        Locale.TrySet(AppLanguage.English);
        var en = m.Render();
        Locale.TrySet(AppLanguage.Japanese);
        var ja = m.Render();
        Assert.NotEqual(zh, en);
        Assert.NotEqual(en, ja);
        Assert.Equal(3, new[] { zh, en, ja }.Count(s => s.Contains("1.2.3", StringComparison.Ordinal)));
    }

    /// <summary>⑧套娃那两格：外层重贴时<b>里面那半句</b>（进度头一句、相对时间）也跟着换语言——这一族最初的形状。</summary>
    [Fact]
    public void TheNestedHalfReRendersWithTheOuterOne()
    {
        var progress = UpdatePolicy.Describe(new DownloadProgress(1_048_576, null));
        Locale.TrySet(AppLanguage.Chinese);
        var zhHead = UpdatePolicy.Describe(ApplyPhase.Downloading).Render();
        Assert.StartsWith(zhHead, progress.Render(), StringComparison.Ordinal);
        Locale.TrySet(AppLanguage.English);
        var enHead = UpdatePolicy.Describe(ApplyPhase.Downloading).Render();
        Assert.StartsWith(enHead, progress.Render(), StringComparison.Ordinal);
        Assert.NotEqual(zhHead, enHead);

        // 相对时间那颗孪生：档位在此刻定，词留到贴上去那一步 ⇒ 念完那一版就是它 Render 过一次的结果（同一颗表，⛔ 两份字）
        var secs = DateTimeOffset.UtcNow.AddMinutes(-7).ToUnixTimeSeconds();
        Locale.TrySet(AppLanguage.Chinese);
        var ago = DateTimeText.RelativeMessage(secs, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        Assert.Equal(DateTimeText.Relative(secs, DateTimeOffset.UtcNow, TimeZoneInfo.Local), ago.Render());
        var zhAgo = ago.Render();
        Locale.TrySet(AppLanguage.English);
        Assert.NotEqual(zhAgo, ago.Render());   // 同一颗对象：词跟着此刻的语言走
    }

    /// <summary>⑨这六颗新键逐颗点名：表里各一行、代码里各一处，⛔ 一颗进了表而没人念、⛔ 一颗没人登记。</summary>
    [Fact]
    public void EveryNewKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        var table = ReadRepoFile(Table);
        var code = Code(ReadRepoFile(VmCs));
        foreach (var key in new[]
                 {
                     "upd.status.autoOn", "upd.status.off", "upd.status.neverChecked",
                     "upd.status.lastCheck", "upd.status.checkCrash", "upd.status.applyCrash",
                 })
        {
            Assert.Equal(1, Times(table, "new Entry(\"" + key + "\""));
            Assert.True(Times(code, "\"" + key + "\"") >= 1, code + "：这颗键没人念：" + key);
        }
    }
}
