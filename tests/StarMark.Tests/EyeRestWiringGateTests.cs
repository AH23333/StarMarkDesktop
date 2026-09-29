#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 护眼 / 休息提醒的<b>接线</b>闸门（扫源码，不跑界面）。
/// <para>
/// 节拍判据本身已经由 <see cref="EyeRestPolicyTests"/> 用注入时刻钉死了；这里守的是另一类错：
/// <b>只有真机才看得见、但一写错就变成"打扰别人"或"整个功能失效"的那几条链路约束</b>——
/// 幕布按档位决定接不接退出（可跳档必须点得开、强制档一条都不接，批次 RS 把这两件事解开了）、
/// 幕布抢不抢焦点（抢了会把用户正在填的表单弄丢）、
/// 没到点就去探前台窗口（每 15 秒一次 P/Invoke）、遮罩建不起来时有没有降级出口、
/// 以及"关掉护眼之后屏幕还黑着"这种最糟糕的收尾。
/// </para>
/// </summary>
public sealed class EyeRestWiringGateTests
{
    private const string OverlayXaml = "src/StarMark.UI/Views/EyeRestOverlayWindow.xaml";
    private const string OverlayCode = "src/StarMark.UI/Views/EyeRestOverlayWindow.xaml.cs";
    private const string Service = "src/StarMark.UI/Services/EyeRestService.cs";
    private const string Settings = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string SettingsVm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string WidgetWindow = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";
    private const string ClockVm = "src/StarMark.UI/ViewModels/ClockWidgetViewModel.cs";
    private const string ClockXaml = "src/StarMark.UI/Views/ClockWidget.xaml";
    private const string ClockXamlCs = "src/StarMark.UI/Views/ClockWidget.xaml.cs";
    private const string MainWindow = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string SettingsPageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>
    /// <c>BeginRest</c> 的整条签名（三处闸门都要切进它的方法体）。写成一份常量而不是各写一遍：
    /// 改签名时漏掉一处，那一处的判据就会静默永不生效（比红测危险）。
    /// </summary>
    private const string BeginRest = "private static bool BeginRest(int intervalMinutes, EyeRestNotice notice)";

    // ────────── 幕布：键盘一条都不接，鼠标按档位接 ──────────

    [Fact]
    public void TheCurtainNeverTakesKeys_ButASkippableNoticeTakesAMouseExit()
    {
        var xaml = SourceGate.ReadRepoFile(OverlayXaml);
        var code = SourceGate.ReadRepoFile(OverlayCode);

        // 幕布从来没有焦点，按键根本不会投递到它身上——"强制档不可跳过"是按构造成立的。
        // 但这条不能靠"忘了写"来兜：挂键盘的那几种拼法一条都不许出现（出现了就说明有人在往回退这次改判）。
        foreach (var forbidden in new[] { "KeyDown", "PreviewKeyDown", "KeyUp", "AcceleratorKey", "AddHandler", "Escape" })
        {
            Assert.DoesNotContain(forbidden, xaml);
            Assert.DoesNotContain(forbidden, code);
        }

        // 可跳档必须有鼠标出口（批次 RS 用户裁决：暗幕不该被"不能提前结束"绑住）。
        var enable = SourceGate.MethodBody(code, "private void EnableSkipByClicking()");
        Assert.Contains("Root.PointerPressed", enable);
        // 第一次点击常被系统当作"激活这扇窗"而吞掉输入，所以激活那条也要认——但只认 PointerActivated：
        // 点亮那一步（AppWindow.Show）自己带来的激活是 CodeActivated，认了它就会出现"幕布刚盖上就自己收了"。
        Assert.Contains("WindowActivationState.PointerActivated", enable);

        // 而挂不挂必须由档位决定：无条件挂＝强制档一点就开，那才是规格点名的"形同虚设"。
        var ctor = SourceGate.MethodBody(code, "public EyeRestOverlayWindow((string Device");
        Assert.Contains("if (EyeRestPolicy.IsSkippable(notice)) EnableSkipByClicking();", ctor);
        // 挂在还原前台之后（同上：先挂上就把自己收了）
        Assert.True(ctor.IndexOf("SetForegroundWindow(previous)", StringComparison.Ordinal)
            < ctor.IndexOf("EnableSkipByClicking();", StringComparison.Ordinal),
            "退出出口必须挂在点亮并还原前台之后");
    }

    [Fact]
    public void OverlayIsTopmostAndNeverActivates()
    {
        var code = SourceGate.ReadRepoFile(OverlayCode);
        Assert.Contains("HWND_TOPMOST", code);                    // 不压在最上层＝放映软件一盖就看不见提醒
        Assert.Contains("SWP_NOACTIVATE", code);
        Assert.Contains("SW_SHOWNOACTIVATE", code);
        // 抢焦点会把用户正在填的表单/编辑器焦点弄丢，倒数完还得重新点一遍
        Assert.DoesNotContain(".Activate()", code);
    }

    [Fact]
    public void OverlayGivesTheFocusBackToWhicheverWindowHadIt()
    {
        // 只看构造体：类注释里也写着 AppWindow.Show()，在整个文件里数顺序会数到注释上去。
        var ctor = SourceGate.MethodBody(SourceGate.ReadRepoFile(OverlayCode), "public EyeRestOverlayWindow((string Device");
        // 点亮那一步（AppWindow.Show）必然会把前台抢过来，所以"还回去"要排在 Show 之后：
        // 顺序错了就等于没还——幕布不吃键盘，那 20 秒用户打的字会落进一扇空窗里。
        var record = ctor.IndexOf("GetForegroundWindow()", StringComparison.Ordinal);
        var show = ctor.IndexOf("AppWindow.Show()", StringComparison.Ordinal);
        var restore = ctor.IndexOf("SetForegroundWindow(previous)", StringComparison.Ordinal);
        Assert.True(record >= 0 && show > record && restore > show,
            "先记前台窗 → 点亮 → 还原前台，三步缺一不可且顺序不能换");
        Assert.Contains("previous != hwnd", ctor);                // 别把焦点还给幕布自己
    }

    [Fact]
    public void CurtainIsADarkCurtainNotAThemedPanel()
    {
        var xaml = SourceGate.ReadRepoFile(OverlayXaml);
        // 浅色桌面上这块幕布也要暗得让人读不下去屏幕上的字：跟主题走会变成一块浅灰纱。
        Assert.Contains("Background=\"#C0000000\"", xaml);
        Assert.DoesNotContain("ThemeResource", xaml);
    }

    // ────────── 服务：性能闸门 + 失败出口 ──────────

    [Fact]
    public void WatchTick_MakesNoNativeCallBeforeTheDueTime()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void OnWatchTick");
        var guard = body.IndexOf("policy.DueAt(interval)", StringComparison.Ordinal);
        var probe = body.IndexOf("IsForegroundFullscreen()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && probe > guard,
            "没到点必须先返回：探前台窗口是\"到点了才问一句\"，不是每 15 秒一次的常驻开销");
    }

    [Fact]
    public void CycleResetsBeforeTheCurtainIsBuilt_SoAFailureCannotTurnIntoBombardment()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static bool BeginRest");
        var watch = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void OnWatchTick");
        Assert.True(watch.IndexOf("policy.Reset(now)", StringComparison.Ordinal)
            < watch.IndexOf("BeginRest(", StringComparison.Ordinal),
            "节拍必须在展示之前就重置：遮罩建不起来时若还没重置，每 15 秒就会再挨一次重试");
        Assert.DoesNotContain("policy.Reset", body);      // 重置只有一处，别在第二条路上再写一遍
    }

    [Fact]
    public void EveryCurtainFailurePathDowngradesToANoticeCard()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), BeginRest);
        // 一条正常出口（提示卡档＝只发一张卡）+ 三条失败退路（取显示器列表抛／系统没报告显示器／遮罩建到一半抛）
        // 都必须各有一条提示卡出口，而不是静默不提醒——"到点了但什么都没发生"和"没到点"在用户侧长得一模一样。
        Assert.Equal(4, SourceGate.Count(body, "Notify(\"该休息一下了\""));
        Assert.Contains("CloseOverlays();", body);        // 建到一半失败：先收干净已建的，再降级
        // 分流只看 Core 的两条判据，不看"这一档是不是强制"：合成就回到旧形状（想用暗幕必须先接受不能退出）。
        Assert.Contains("if (!EyeRestPolicy.UsesCurtain(notice))", body);
    }

    [Fact]
    public void OwnProcessNeverCountsAsAFullscreenApp()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static bool IsForegroundFullscreen");
        // 截图遮罩、贴图、暗幕本身都是铺满屏幕的矩形：把自己判成全屏＝护眼被自己延后掉。
        Assert.Contains("Environment.ProcessId", body);
        Assert.Contains("return false", body);            // 探不到就按"不是全屏"，不能每次都让路
    }

    [Fact]
    public void StoppingTheServiceTakesTheCurtainDownImmediately()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static void Stop()");
        Assert.Contains("CloseOverlays()", body);
        // "我已经把护眼关掉了，屏幕还黑着"是这条链上最糟糕的一种收尾
        Assert.True(body.IndexOf("_watch?.Stop()", StringComparison.Ordinal)
            < body.IndexOf("CloseOverlays()", StringComparison.Ordinal));
    }

    [Fact]
    public void StartingTwiceDoesNotRebuildTheTimer()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static void Start(DispatcherQueue queue, SettingsStore settings)");
        Assert.Contains("if (_watch is not null) return;", body);
        Assert.Contains("_policy ??=", body);            // 节拍起点跨开关保留：关掉再打开不该重新攒 45 分钟
    }

    /// <summary>
    /// 提醒的两级兜底：<b>先试右下角提示卡，贴不上屏幕才退到主窗提示条，两条都不成才只留日志</b>。
    /// <para>顺序不能反过来：主窗收进托盘时 InfoBar 根本看不见。批次 RV 之前这一格钉的是"托盘气泡"，
    /// 而那一发在 Windows 11 上返回成功却什么都不显示，于是第一级永远"成功"、后两级按构造走不到——
    /// 这条链坏着的时候日志是全绿的（记忆 ⑨"全绿而功能坏"那一族）。</para>
    /// </summary>
    [Fact]
    public void TheNoticeCardFallsBackToTheMainWindowNoticeAndThenToTheLog()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static bool Notify(string title, string body)");
        var card = body.IndexOf("NoticeCard.Show(title, body)", StringComparison.Ordinal);
        var notice = body.IndexOf("ShowNotice", StringComparison.Ordinal);
        Assert.True(card >= 0 && notice > card, "主窗收进托盘时 InfoBar 是看不见的：提示卡必须排在前面");
        Assert.Contains("StarLog.Info", body);           // 两条出口都没接住时留痕，不静默吞掉
        Assert.Contains("return channel != \"日志\";", body);   // 回报的是"有没有真的出现在屏幕上"，试一试据此说话
        Assert.DoesNotContain("App.MainWindow?.TryShow", body);  // 别再从主窗转一道手：那正是谎报的入口
    }

    [Fact]
    public void CurtainCanDieGracefully_InsteadOfCrashingTheTimer()
    {
        var code = SourceGate.ReadRepoFile(Service);
        var paint = SourceGate.MethodBody(code, "private static void ForEachOverlay");
        Assert.Contains("CloseOverlays()", paint);       // 幕布被外力收掉（拔显示器/注销）：断供就收幕
        Assert.Contains("_rest?.Stop()", paint);
    }

    // ────────── 默认关 + 起停只有一条路 ──────────

    [Fact]
    public void EverythingDefaultsToOff_ExceptTheFullscreenCourtesy()
    {
        var store = SourceGate.ReadRepoPartials(Settings);
        // 开关默认关（"== true"）；让路默认开（"!= false"）——不请自来的遮罩是最讨人嫌的一种帮忙，
        // 而"放 PPT 被砸"是要默认防的那一侧。
        Assert.Contains("public bool LoadEyeRestEnabled() => Load() is { } d && d.EyeRestEnabled == true;", store);
        Assert.Contains("public bool LoadEyeRestDeferOnFullscreen() => Load() is not { } d || d.EyeRestDeferOnFullscreen != false;", store);
        // 提醒形式走 Core 的那道夹：认不得的数值回默认档，而不是"夹到最近的一端"（默认是哪一档由 Core 说）
        Assert.Contains("public StarMark.Core.Health.EyeRestNotice LoadEyeRestNotice()", store);
        Assert.Contains("EyeRestPolicy.ClampNotice(Load()?.EyeRestNotice)", store);
        foreach (var field in new[] { "public bool? EyeRestEnabled", "public int? EyeRestIntervalMinutes",
                                      "public int? EyeRestNotice", "public bool? EyeRestDeferOnFullscreen" })
            Assert.Contains(field, store);               // 可空＝"没存过"与"存了 false"分得开，缺省才走上面的默认
    }

    /// <summary>
    /// 那颗"强制模式"布尔必须<b>整条消失</b>：留着一个没人读的字段，下一轮就有人把它接回去，
    /// 而接回去的症状正是这次改判治的那个（暗幕与能不能提前退出又被绑成一件事）。
    /// </summary>
    [Fact]
    public void TheOldForcedBooleanIsGoneFromEveryLayer()
    {
        var sources = new[]
        {
            SourceGate.ReadRepoFile(Service),
            SourceGate.ReadRepoFile(OverlayCode),
            SourceGate.ReadRepoFile(OverlayXaml),
            SourceGate.ReadRepoPartials(Settings),
            SourceGate.ReadRepoPartials(SettingsVm),
            SourceGate.ReadRepoFile(SettingsPageXaml),
            SourceGate.ReadRepoPartials(WidgetWindow),
        };
        foreach (var source in sources) Assert.DoesNotContain("EyeRestEnforced", source);
    }

    [Fact]
    public void SavedIntervalIsClampedOnTheWayIn_NotOnlyOnTheWayOut()
    {
        var store = SourceGate.ReadRepoPartials(Settings);
        var save = SourceGate.MethodBody(store, "public void SaveEyeRest(bool enabled, int intervalMinutes, StarMark.Core.Health.EyeRestNotice notice, bool deferOnFullscreen)");
        var load = SourceGate.MethodBody(store, "public int LoadEyeRestIntervalMinutes()");
        Assert.Contains("ClampInterval(intervalMinutes)", save);
        Assert.Contains("ClampInterval(", load);
        // 手改 settings.json 写进 0／99999 时，界面显示与真用到的必须是同一个数（两头都夹，不靠一头兜）
        Assert.Contains("ClampNotice(notice)", save);      // 提醒形式同理：不指望每个调用方先自己夹一遍
    }

    [Fact]
    public void TheOnlyWayToStartTheTimerIsThroughApplyEyeRest()
    {
        var app = SourceGate.ReadRepoFile(App);
        Assert.Equal(1, SourceGate.Count(app, "EyeRestService.Start("));
        Assert.Contains("if (fileSettings.LoadEyeRestEnabled())", app);
        var apply = SourceGate.MethodBody(app, "public static bool ApplyEyeRest(bool enabled)");
        Assert.Contains("return StarMark.UI.Services.EyeRestService.IsRunning;", apply);
        // 回报的是"实际在不在跑"，不是"用户点了开"——表挂不上时设置页要能自己承认
    }

    [Fact]
    public void OpeningTheSettingsPageDoesNotRestartTheClock()
    {
        var vm = SourceGate.ReadRepoPartials(SettingsVm);
        var backfill = SourceGate.Between(vm, "_suppressEyeRestApply = true;", "_suppressEyeRestApply = false;");
        Assert.Equal(4, SourceGate.Count(backfill, "Safe("));     // 四项只回灌显示
        Assert.DoesNotContain("ApplyEyeRest", backfill);           // 不因"显示"而起停定时器
        Assert.DoesNotContain("SaveEyeRest", backfill);            // 更不因打开页面而把当前值写回存档
    }

    [Fact]
    public void StatusLineAdmitsWhenTheTimerIsNotActuallyRunning()
    {
        var vm = SourceGate.ReadRepoPartials(SettingsVm);
        var build = SourceGate.MethodBody(vm, "private string BuildEyeRestStatus()");
        Assert.Contains("EyeRestService.IsRunning", build);
        Assert.Contains("节拍表没挂上", build);
        Assert.Contains("NextDueAt", build);                       // 下一次大约几点：用户唯一能核对的证据
    }

    /// <summary>
    /// 「怎么提醒」那一排必须<b>由 Core 那三档生成</b>：界面自己重写一遍选项，就会出现
    /// "下拉里有 ClampNotice 不认的一档"、"状态行说的与幕布上写的不一样"这类分岔（记忆 ⑧），
    /// 而分岔的这一次是用户看得见的。
    /// </summary>
    [Fact]
    public void TheNoticePickerIsGeneratedFromCore_NotRetypedInThePage()
    {
        var page = SourceGate.ReadRepoFile(SettingsPageXaml);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.EyeRestNoticeOptions}\"", page);
        Assert.Contains("SelectedIndex=\"{x:Bind ViewModel.EyeRestNoticeIndex, Mode=TwoWay}\"", page);
        Assert.DoesNotContain("只发一张右下角提示卡", page);                 // 三档的说法只在 Core 写一次

        var vm = SourceGate.ReadRepoPartials(SettingsVm);
        Assert.Contains("EyeRestPolicy.NoticeLabels", vm);                       // 下拉的项
        Assert.Contains("EyeRestPolicy.NoticeAt(EyeRestNoticeIndex)", vm);        // 下标 → 哪一档
        Assert.Contains("EyeRestPolicy.NoticeLabel(CurrentEyeRestNotice)", vm);   // 状态行取同一句
        Assert.Contains("EyeRestPolicy.NoticeHint(CurrentEyeRestNotice)", vm);    // 「试一试」的回执也说同一句
        Assert.DoesNotContain("只发一张右下角提示卡", vm);
    }

    // ────────── 「试一试」：给真机一条不用等满间隔的验证出口 ──────────

    [Fact]
    public void PreviewShowsTheCurrentSetting_WithoutTouchingTheCycle()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var preview = SourceGate.MethodBody(service, "public static bool Preview()");
        Assert.DoesNotContain("policy.Reset", preview);      // 演一次≠真休息过一轮：下一次该几点还是几点
        Assert.DoesNotContain(".Start(", preview);           // 也不替用户起表：没开启时答案是"没东西可演"
        Assert.Contains("BeginRest(", preview);
        // 试一试演的必须就是当前那一档：读的是提醒形式而不是旧的那颗强制布尔，否则"试一试"验证的
        // 是另一个东西，用户按下去看到的与下拉里选的对不上。
        Assert.Contains("settings.LoadEyeRestNotice()", preview);

        var begin = SourceGate.MethodBody(service, BeginRest);
        Assert.Contains("if (_overlays.Count > 0)", begin);   // 连按两次不叠第二层幕布（只会更黑，且不解释任何事）
    }

    /// <summary>
    /// 点一下屏幕这条出口要真的接到"收幕"那件事上：中间任何一环断掉，症状都是"暗幕点不开"——
    /// 而那正是这次改判要治的原始 complaint，全绿也照样是坏着的（记忆 ⑨ 那一族）。
    /// </summary>
    [Fact]
    public void TheClickOnACurtainActuallyTakesTheCurtainDown()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var begin = SourceGate.MethodBody(service, BeginRest);
        // 交出去的是"哪一档"而不是"可不可以在这里退出"那个布尔：布尔的方向只有被调方读得懂，
        // 接线处写反编译得过、闸门也看不出来，真机上却是"强制档一点就开／暗幕档点不开"（记忆 ⑥）。
        Assert.Contains("new EyeRestOverlayWindow(monitor, notice)", begin);
        Assert.Contains("overlay.SkipRequested += SkipRest;", begin);

        var skip = SourceGate.MethodBody(service, "public static void SkipRest()");
        Assert.Contains("if (_overlays.Count == 0) return;", skip);   // 幂等：倒数刚好走完时的第二次点击不该报错
        Assert.Contains("_rest?.Stop()", skip);                       // 不收那张表＝收完幕还在给已死的窗倒数
        Assert.Contains("CloseOverlays()", skip);
        Assert.DoesNotContain("policy.Reset", skip);                  // 提前结束不重新攒一轮（否则点一下反而更晚提醒）
    }

    [Fact]
    public void ChangingTheSettingInvalidatesTheLastPreview()
    {
        var vm = SourceGate.ReadRepoPartials(SettingsVm);
        var apply = SourceGate.MethodBody(vm, "private void ApplyEyeRestSwitch()");
        // 上一次演出来的已经不是新配置了：留着那行会读成"刚验证过现在的设置"
        Assert.Contains("EyeRestPreviewStatus = string.Empty;", apply);
        Assert.Contains("EyeRestStatus = BuildEyeRestStatus();", apply);
    }

    // ────────── 批次 WC-3：护眼与时钟组件整合 ──────────

    /// <summary>时钟组件上那一节是<b>第二个入口，不是第二个引擎</b>（批次 RT 补齐提醒形式与「试一试」）。</summary>
    [Fact]
    public void TheClockWidgetMenuIsASecondEntry_NotASecondEngine()
    {
        var widget = SourceGate.ReadRepoPartials(WidgetWindow);
        var call = SourceGate.MethodBody(widget, "private void PopulateMenu(MenuFlyout menu)");
        var section = SourceGate.MethodBody(widget, "private void BuildEyeRestSection(MenuFlyout menu)");

        // 只挂在时钟上（用户裁决是"护眼和时钟组件结合"，不是"每个组件都长一个开关"）
        Assert.Contains("if (_kind == WidgetKind.Clock) BuildEyeRestSection(menu);", call);
        // 写盘与起停走设置页同一对出口：两处各攒一份状态，迟早对不上，而对不上的那次是用户先看见。
        // 参数整条钉住（不是只钉"调用了它"）：这里把开关读反编译得过、也只少一行日志，
        // 真机上却是"按下去关掉护眼、屏幕照旧每 45 分钟黑一次"（记忆 ⑥ 那一族，坑表 #177）。
        Assert.Contains("settings.SaveEyeRest(toggle.IsChecked, settings.LoadEyeRestIntervalMinutes(),", section);
        Assert.Contains("App.ApplyEyeRest(toggle.IsChecked);", section);
        Assert.Contains("settings.SaveEyeRest(settings.LoadEyeRestEnabled(), minutes,", section);
        Assert.Contains("settings.SaveEyeRest(settings.LoadEyeRestEnabled(), settings.LoadEyeRestIntervalMinutes(),", section);
        Assert.Equal(2, SourceGate.Count(section, "App.ApplyEyeRest(settings.LoadEyeRestEnabled());"));

        // 每一项只管自己那一件，其余三项都<b>当场重新读</b>再带过去（拿打开菜单那一刻的快照写回去＝把别的项退回旧值）。
        // 钉的是<b>整段实参尾巴</b>而不是"这个名字出现几次"：方法开头那三行是显示用的读数，
        // 数裸方法名会把它们一起算进来，计数就失去了含义（#177 同族——锚点要钉在真正要守的那件事上）。
        Assert.Equal(3, SourceGate.Count(section, "settings.SaveEyeRest("));
        Assert.Equal(2, SourceGate.Count(section, "settings.LoadEyeRestNotice(), settings.LoadEyeRestDeferOnFullscreen());"));
        Assert.Equal(3, SourceGate.Count(section, "settings.LoadEyeRestDeferOnFullscreen());"));
        Assert.Equal(2, SourceGate.Count(section, "App.ApplyEyeRest(settings.LoadEyeRestEnabled());"));

        // 档位与文案都取 Core 那一份（自己拼"X 分钟"或把三档重打一遍就是第二份"有哪些档"）
        Assert.Contains("EyeRestPolicy.IntervalOptions", section);
        Assert.Contains("EyeRestPolicy.IntervalLabels", section);
        Assert.Contains("EyeRestPolicy.NoticeOptions", section);
        Assert.Contains("EyeRestPolicy.NoticeLabels", section);
        Assert.Contains("EyeRestPolicy.NoticeLabel(notice)", section);   // 菜单标题上的当前档
        Assert.DoesNotContain("15 分钟", section);
        Assert.DoesNotContain("只发一张右下角提示卡", section);
    }

    /// <summary>
    /// 组件上的「试一试」必须演<b>当前那一档</b>、失败要有可见的原因、而且不许自己起表。
    /// <para>"按了没反应"与"演过了但没看见"在用户侧长得一样；而开关没开时那颗灰项若不写清为什么灰，
    /// 用户只会认为这条功能坏了（P-54 那条"不许留需要他猜的中间态"）。</para>
    /// </summary>
    [Fact]
    public void TheClockWidgetsTryItEntryPreviewsTheCurrentNotice_AndExplainsWhyItCannot()
    {
        var section = SourceGate.MethodBody(SourceGate.ReadRepoPartials(WidgetWindow), "private void BuildEyeRestSection(MenuFlyout menu)");
        Assert.Contains("EyeRestService.Preview()", section);
        Assert.Contains("IsEnabled = running,", section);                                  // running＝节拍表真的挂着
        Assert.Contains("试一试（要先开启休息提醒，上面第一项）", section);                  // 灰着的那一句要自己说清原因
        Assert.Contains("App.MainWindow?.ShowNotice(\"没演成\"", section);                  // 按下去没演成要看得见
        Assert.Contains("EyeRestService.IsResting", section);                               // "幕布还盖着"与"表没挂上"要分开说
        Assert.DoesNotContain("EyeRestService.Start(", section);                            // 组件这一头不替用户起表
    }

    /// <summary>时钟上那一行读的是引擎已有的状态，组件自己不另起一张表。</summary>
    [Fact]
    public void TheClockLineReadsTheEngine_InsteadOfKeepingItsOwnBeat()
    {
        var vm = SourceGate.ReadRepoFile(ClockVm);
        Assert.Contains("EyeRestService.IsResting", vm);
        Assert.Contains("EyeRestService.NextDueAt", vm);
        // 禁的是方法体里的行为，不是注释里的字（类注释本来就要提"由组件的定时器每秒调用"）
        var update = SourceGate.MethodBody(vm, "public int Update()");     // 批次 RU：返回值＝这一拍弹掉几条
        var rest = SourceGate.MethodBody(vm, "private void RefreshRest(DateTime now)");
        foreach (var forbidden in new[] { "DispatcherQueueTimer", "EyeRestPolicy", "SaveEyeRest", "IntervalOptions" })
        {
            Assert.DoesNotContain(forbidden, update);
            Assert.DoesNotContain(forbidden, rest);
        }

        var xaml = SourceGate.ReadRepoFile(ClockXaml);
        // 关着的人不该在时钟上多看见一行空位
        Assert.Contains("ViewModel.HasRestLine, Mode=OneWay, Converter={StaticResource BoolToVis}", xaml);
        // 第三行也要吃字号：早先漏乘缩放系数就是"放大文字后日期不动"那一类
        var code = SourceGate.ReadRepoFile(ClockXamlCs);
        Assert.Contains("RestBlock.FontSize = DateBlock.FontSize;", code);

        // 闹钟那一行同一条口径（批次 RU）：不占位就不许露，露出来就得吃字号
        Assert.Contains("ViewModel.HasAlarmLine, Mode=OneWay, Converter={StaticResource BoolToVis}", xaml);
        Assert.Contains("AlarmBlock.FontSize = DateBlock.FontSize;", code);
        // 组件的 VM 不起第二张表、也不自己落盘：表在宿主时钟那一只手里，落盘在控件那一步
        Assert.DoesNotContain("CreateTimer", vm);
        Assert.DoesNotContain("SaveAlarmsAsync", vm);
    }

    /// <summary>从组件跳"完整设置"必须落在它点名的那一页——按标题选，所以标题要真的存在。</summary>
    [Fact]
    public void TheJumpFromTheWidgetLandsOnTheTabItNames()
    {
        var widget = SourceGate.ReadRepoPartials(WidgetWindow);
        var main = SourceGate.ReadRepoPartials(MainWindow);
        var page = SourceGate.ReadRepoFile(SettingsPageXaml);
        var section = SourceGate.MethodBody(widget, "private void BuildEyeRestSection(MenuFlyout menu)");

        Assert.Contains("Present(true, \"健康与诊断\")", section);
        Assert.Contains("page.SelectTab(tab)", main);
        Assert.Contains("<TabViewItem Header=\"健康与诊断\">", page);
    }
}
