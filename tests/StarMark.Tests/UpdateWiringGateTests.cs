#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 UE 的接线闸门：<b>"检测版本更新"这条链真接通了没有</b>。
/// <para>
/// 测试工程引用不到 <c>StarMark.UI</c>（#184），所以界面这一侧只能扫源码；而判据本身
/// （版本怎么比、结局怎么说、什么时候该问、值不值得出声）已经在 <see cref="UpdateCheckTests"/> 与
/// <see cref="GitHubReleaseSourceTests"/> 里逐字钉过。这里钉的是<b>那条链有没有接到屏幕上</b>——
/// 本仓反复出现的一类事故是"逻辑全绿而功能坏"：判据写对了，可没人把它接到一颗按钮、
/// 一次启动排程、一张卡上（ItemCard 绑定极性、画布选工具、序号粗细那三起同族）。
/// </para>
/// <para>
/// 锚点一律写成<b>带引号或带分号的整条字面</b>（坑表 #171/#179：注释与代码同名字面会数进同一笔），
/// XAML 先抹掉注释再数（与 <c>SettingsTaxonomyGateTests</c> 同一条口径）。
/// </para>
/// </summary>
public sealed class UpdateWiringGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string Scheduler = "src/StarMark.UI/Helpers/UpdateScheduler.cs";
    private const string Card = "src/StarMark.UI/Views/NoticeCardWindow.cs";
    private const string CardService = "src/StarMark.UI/Services/NoticeCard.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.Updates.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Updates.cs";
    private const string MainWindow = "src/StarMark.UI/MainWindow.xaml.cs";

    private static string Markup(string text)
        => Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string GeneralTab(string xaml)
        => Between(xaml, "<TabViewItem Header=\"常规\">", "</TabViewItem>");

    // ────────── 屏幕上那一颗开关：一个编辑入口、落在这张页、按下去当场生效 ──────────

    [Fact]
    public void TheSwitchHasExactlyOneEditorAndSitsOnTheGeneralTab()
    {
        var tab = GeneralTab(Markup(ReadRepoFile(Xaml)));
        Assert.Equal(1, Count(tab, "<TextBlock Text=\"关于与更新\" Style=\"{StaticResource SettingTitle}\""));
        Assert.Equal(1, Count(tab, "ViewModel.UpdateAutoCheckEnabled, Mode=TwoWay"));
        // 版本号与上一次的结果都要看得见（只给一颗开关而说不出"上次查到了什么"＝不能自证）
        Assert.Equal(1, Count(tab, "ViewModel.LocalVersionText, Mode=OneWay"));
        Assert.Equal(1, Count(tab, "ViewModel.UpdateStatus, Mode=OneWay"));
        Assert.Equal(1, Count(tab, "Click=\"UpdateCheck_Click\""));
        Assert.Equal(1, Count(tab, "Click=\"UpdateOpenPage_Click\""));
    }

    /// <summary>
    /// 那颗按钮默认收着：<b>"打开下载页"只由 Core 那句判据放行</b>，界面不许自己决定什么时候亮。
    /// 在"这个仓库还没发布过版本"那一格放一颗下载按钮，等于承诺一个不存在的产物。
    /// </summary>
    [Fact]
    public void TheDownloadButtonStartsHidden_AndItsGateLivesInCore()
    {
        var tab = GeneralTab(Markup(ReadRepoFile(Xaml)));
        Assert.Contains("x:Name=\"UpdateOpenPageButton\"", tab, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", tab, StringComparison.Ordinal);

        var page = Code(ReadRepoFile(Page));
        Assert.Contains("if (ViewModel.HasDownloadableRelease)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Verdict ==", page, StringComparison.Ordinal);   // 判据不许在界面重写一遍

        var vm = Code(ReadRepoPartials("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs"));
        Assert.Equal(1, Count(vm, "UpdatePolicy.HasDownloadableRelease"));

        var policy = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs"));
        Assert.Contains("public static bool HasDownloadableRelease(UpdateVerdict verdict)", policy, StringComparison.Ordinal);
    }

    /// <summary>
    /// 按一下就落盘并把巡查表重排：<b>"改了要等下次启动才认"在本仓等于一句隐藏的重启指令</b>
    /// （与自动备份、护眼、剪贴板那几条同一口径，而 P-54 把它定性为缺陷）。
    /// </summary>
    [Fact]
    public void FlippingTheSwitchPersistsAndReschedulesRightThere()
    {
        var vm = Code(ReadRepoPartials("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs"));
        var apply = MethodBody(vm, "private void ApplyUpdateAutoCheck(bool enabled)");
        Assert.Contains("Updates().AutoCheckEnabled = enabled;", apply);
        Assert.Contains("UpdateScheduler.Start(_settings, Updates());", apply);
        Assert.Contains("RefreshUpdateStatus();", apply);
        Assert.Equal(1, Count(vm, "UpdateScheduler.Start("));     // 重排只有这一处，别处再点一次就是两次起停
    }

    /// <summary>回灌那一位时把副作用压住：否则每次打开设置页都把巡查表收掉再起一遍。</summary>
    [Fact]
    public void BackfillReadsTheSwitch_WithoutTouchingTheTimerOrTheDisk()
    {
        var vm = Code(ReadRepoPartials("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs"));
        var backfill = MethodBody(vm, "public void BackfillUpdateState()");
        Assert.Contains("_suppressUpdateApply = true;", backfill);
        Assert.Contains("_suppressUpdateApply = false;", backfill);
        Assert.Equal(0, Count(backfill, "UpdateScheduler."));
        Assert.Equal(0, Count(backfill, "ApplyUpdateAutoCheck"));
        Assert.Equal(0, Count(backfill, "CheckAsync"));
        // 处理器第一件事就是看这把闩——顺序反了就变成"进一次页面重排一次定时器"
        var changed = MethodBody(vm, "partial void OnUpdateAutoCheckEnabledChanged(bool value)");
        Assert.Contains("if (_suppressUpdateApply) return;", changed);
        // 而回灌确实被那次进页面调到了（一次，不多次）
        Assert.Equal(1, Count(vm, "BackfillUpdateState();"));
    }

    // ────────── 什么时候真的上网：判据在 Core，排程在 UI，两处都不能缺 ──────────

    /// <summary>关掉之后<b>连定时器都不建</b>："界面写着已关闭、其实还在每小时上网问"是最难发现的不诚实。</summary>
    [Fact]
    public void OffMeansTheTimerIsNeverBuilt()
    {
        var code = Code(ReadRepoFile(Scheduler));
        var start = MethodBody(code, "public static void Start(SettingsStore settings, UpdateService service)");
        Between(start, "Stop();", "if (!settings.LoadUpdateState().AutoCheckEnabled) return;");
        Between(start, "if (!settings.LoadUpdateState().AutoCheckEnabled) return;", "_timer = new Timer(");
        Assert.Equal(1, Count(start, "_timer = new Timer("));
        Assert.Contains("public static void Stop()", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 开机那一发要<b>让出首屏</b>并且<b>排在窗口点亮之后</b>：这条链要走网络，
    /// 而首屏那 1~3 秒正被"启动后 UI 冻结"那条链盯着量（批次 WE/TA/TB）。
    /// </summary>
    [Fact]
    public void StartupSchedulesOneDelayedProbeAndOnePatrolAfterTheWindowIsUp()
    {
        var app = Code(ReadRepoFile(App));
        Assert.Equal(1, Count(app, "UpdateScheduler.ScheduleFirstProbe("));
        Assert.Equal(1, Count(app, "UpdateScheduler.Start("));
        Between(app, "UIStallWatchdog.Start(_window.DispatcherQueue);", "UpdateScheduler.ScheduleFirstProbe(");
        var scheduler = Code(ReadRepoFile(Scheduler));
        Assert.Contains("await Task.Delay(FirstProbeDelay)", scheduler, StringComparison.Ordinal);
    }

    /// <summary>巡查只是"多问几遍判据"，节奏本身只有 Core 那一处（24 小时那条不许在 UI 重写一份）。</summary>
    [Fact]
    public void TheCadenceIsSaidOnce_AndThePatrolOnlyAsksWhetherItIsDue()
    {
        var scheduler = Code(ReadRepoFile(Scheduler));
        Assert.Contains("await service.TryAutoProbeAsync()", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("FromHours(24)", scheduler, StringComparison.Ordinal);
        Assert.Contains("UpdatePolicy.ProbeGapHours", scheduler, StringComparison.Ordinal);
        Assert.Equal(1, Count(Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs")),
            "ProbeGapHours = 24"));
    }

    // ────────── 接线：容器里那三颗必须接成一条，而不是各接各的 ──────────

    /// <summary>
    /// 抓取器接的是<b>容器里那一颗 SettingsStore</b>。此前 WidgetWindow.Reveal 那起事故（批次 H1）
    /// 就是"本地又 new 了一份"——两处各写各的档，症状是设置怎么看都不对。
    /// </summary>
    [Fact]
    public void TheRegistrationWiresProbeStoreAndServiceOutOfTheContainer()
    {
        var app = Code(ReadRepoFile(App));
        Assert.Equal(1, Count(app, "new StarMark.Integrations.Updates.GitHubReleaseSource("));
        Assert.Equal(1, Count(app, "new StarMark.Core.Updates.UpdateService("));
        // 状态宿主必须是容器里那一颗 SettingsStore：本地再 new 一份就是两份档互相盖写（批次 H1 那起事故）
        var store = Between(app, "services.AddSingleton<StarMark.Core.Updates.IUpdateStateStore>", "services.AddSingleton(sp => new StarMark.Core.Updates.UpdateService");
        Assert.Contains("GetRequiredService<StarMark.UI.Helpers.SettingsStore>()", store, StringComparison.Ordinal);
        Assert.DoesNotContain("new StarMark.UI.Helpers.SettingsStore", store, StringComparison.Ordinal);
        // Token 现取而不是构造期快照：改完 Token 不必重启就能按新配额问（热榜同一条）
        Assert.Contains("tokenProvider: () => sp.GetRequiredService<StarMark.Integrations.GitHub.GitHubOptions>().Token",
            app, StringComparison.Ordinal);
    }

    /// <summary>宿主是设置档那一份：<b>整仓只有接口那一个文件与这一个宿主提到它</b>（多一处＝第二个 store 在盖写）。</summary>
    [Fact]
    public void ThereIsExactlyOneStateStoreImplementation()
    {
        var hits = 0;
        foreach (var (_, text) in ReadRepoUnder("src"))
            if (Code(text).Contains("IUpdateStateStore", StringComparison.Ordinal)) hits++;

        Assert.Equal(3, hits);      // 接口的家（Core）+ 唯一的宿主（UI 设置档）+ DI 那一句接线
        Assert.Equal(1, Count(Code(ReadRepoFile("src/StarMark.UI/Helpers/SettingsStore.Updates.cs")), ": IUpdateStateStore"));
    }

    // ────────── 托盘：那一项永远点得动，结果当场看得见 ──────────

    /// <summary>（常量，菜单行，处理器）三处齐全——少一处就是"菜单里有这项而点了没反应"。</summary>
    [Fact]
    public void TheTrayItemIsDeclaredListedAndHandled()
    {
        var tray = Code(ReadRepoPartials(MainWindow));
        Assert.Equal(3, Count(tray, "TrayCheckUpdate"));
        Assert.Contains("new(\"检查更新\", TrayCheckUpdate, SeparatorBefore: true),", tray, StringComparison.Ordinal);
        Assert.Contains("case TrayCheckUpdate:", tray, StringComparison.Ordinal);
        // 手动那一发不看自动开关（关掉自动＝"别自己问"，不是"不许我问"），也不看任何功能总开关
        var handler = MethodBody(tray, "private async Task CheckUpdateFromTrayAsync()");
        Assert.Contains("await service.CheckAsync(manual: true)", handler);
        Assert.DoesNotContain("LoadUpdateState", handler, StringComparison.Ordinal);
        Assert.Contains("UpdateScheduler.AnnounceOnDemand", handler, StringComparison.Ordinal);
    }

    // ────────── 提示卡的动作：整张卡是那一下，地址由本程序拼 ──────────

    /// <summary>
    /// 卡片带动作时<b>两路都要挂</b>（PointerPressed 与 PointerActivated）：第一次点击常被系统当作
    /// "激活这扇窗"而吞掉输入，只挂一路的症状就是"按了下载没反应、卡还收掉了"。
    /// 而这条链唯一的动作出口不能是"去设置页找一颗按钮"——那是把要做的事推回给人（P-54）。
    /// </summary>
    [Fact]
    public void TheCardActionIsMountedOnBothClickPaths()
    {
        var code = Code(ReadRepoFile(Card));
        var mount = MethodBody(code, "private void MountClickToClose()");
        Assert.Contains("_card.PointerPressed += (_, _) => HideCard();", mount);
        Assert.Contains("_card.PointerPressed += (_, _) => OpenAction();", mount);
        Assert.Equal(2, Count(mount, "WindowActivationState.PointerActivated"));
        Assert.Contains("if (_clickMounted) return;", mount);
        // 动作是"当时那条消息的"，所以复用这扇窗时上一条不会留给下一条
        Assert.Contains("_actionUrl = string.IsNullOrWhiteSpace(url) ? null : url;",
            MethodBody(code, "public void SetAction(string? url, string? label)"));
        // 交出去之前先清空：同一条消息被再点一次不该再开一扇浏览器
        var open = MethodBody(code, "private void OpenAction()");
        Assert.Contains("SetAction(null, null);", open);
        Assert.Contains("ReportIfRefusedAsync(url)", open);
        // 真正交给系统那一步走 LauncherEx（协议白名单闸门只有那一个出处），不许自己调 Launcher
        var relay = MethodBody(code, "private static async Task ReportIfRefusedAsync(string url)");
        Assert.Contains("var reason = await LauncherEx.TryOpenAsync(url);", relay);
        Assert.Contains("if (reason is not null) StarLog.Warn(", relay);   // "系统里没有能打开它的程序"不许静默
        Assert.DoesNotContain("Launcher.LaunchUriAsync", code, StringComparison.Ordinal);
    }

    /// <summary>停留秒数仍然只有一个出处；两种说法都由那一颗常量生成（不许有人写死"10 秒"）。</summary>
    [Fact]
    public void TheCardStillSaysTheHoldSecondsOnce()
    {
        var code = Code(ReadRepoFile(Card));
        Assert.Contains("点一下关闭 · {KeepSeconds} 秒后自动消失", code, StringComparison.Ordinal);
        Assert.Contains("点一下打开{label ?? \"链接\"} · {KeepSeconds} 秒后自动消失", code, StringComparison.Ordinal);
        Assert.DoesNotContain("10 秒后自动消失", code, StringComparison.Ordinal);
        Assert.Equal(3, Count(code, "HintFor("));   // 定义一处 + 造出来时一次 + 换动作时一次
    }

    /// <summary>设动作必须排在换内容之前；<see cref="NoticeCard.Show"/> 那一句转发不许被人改成"只换内容"。</summary>
    [Fact]
    public void TheActionIsSetBeforeTheMessageReplacesTheCard()
    {
        var service = Code(ReadRepoFile(CardService));
        var show = MethodBody(service, "public static bool Show(string title, string message, string? actionUrl = null, string? actionLabel = null)");
        Between(show, "_window.Closed += (_, _) => _window = null;", "_window.SetAction(actionUrl, actionLabel);");
        Between(show, "_window.SetAction(actionUrl, actionLabel);", "return _window.Apply(title, message);");
    }

    /// <summary>
    /// <b>只有"确实有新版"那两种出声才带地址</b>：手动那一发无论结果都说得出，但不该在
    /// "还没发布过／已经最新"那些格给一个下载出口。判据取自 Core，UI 一行都不重写。
    /// </summary>
    [Fact]
    public void OnlyTheAnswerThatPromisesANewVersionCarriesALink()
    {
        var scheduler = Code(ReadRepoFile(Scheduler));
        Assert.Contains("UpdatePolicy.HasDownloadableRelease(report.Verdict) ? report.PageUrl : null",
            MethodBody(scheduler, "public static void AnnounceOnDemand(UpdateReport report)"));
        Assert.Contains("ShowCard(\"发现新版本\", report, report.PageUrl);", scheduler, StringComparison.Ordinal);
        // 出声必须回 UI 线程：这扇窗要在 UI 线程上建，从池线程直接发的症状是"永远贴不上屏幕"
        var showCard = MethodBody(scheduler, "private static void ShowCard(string title, UpdateReport report, string? actionUrl)");
        Assert.Contains("App.MainWindow?.DispatcherQueue", showCard);
        Assert.Contains("pump.TryEnqueue(() =>", showCard);
        Assert.Contains("NoticeCard.Show(title, report.Text, actionUrl, \"发布页\");", showCard);
        // 而"贴上了没有"问的是屏幕，不是任何一发的返回值（批次 RV 那条谎报的教训）
        Assert.Contains("if (!shown) StarLog.Info(", showCard);
    }

    /// <summary>界面不许另写一份结局的措辞：那一句话的唯一出处是 <c>UpdatePolicy.Describe</c>。</summary>
    [Fact]
    public void TheUiDoesNotRetellAnyOfTheVerdictSentences()
    {
        foreach (var file in new[] { Page, Scheduler, ViewModel })
        {
            var code = Code(ReadRepoFile(file));
            foreach (var forbidden in new[] { "已经是最新", "还没有发布过版本", "连不上 GitHub", "限流", "凭据被拒" })
                Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 这条链在界面上<b>只读</b>：不许出现写盘、起进程、替换文件这些形状
    /// （"就地装新版"是登记在账本的另一件事，前置条件还没落地）。
    /// </summary>
    [Theory]
    [InlineData("src/StarMark.UI/Helpers/UpdateScheduler.cs")]
    [InlineData(Page)]
    public void TheUpdateSideInTheUiStaysReadOnly(string file)
    {
        var code = Code(ReadRepoFile(file));
        foreach (var forbidden in new[] { "File.Write", "File.Move", "File.Delete", "Process.Start",
                "Environment.Exit", "DownloadFile", "Assembly.Load" })
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
    }
}
