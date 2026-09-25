#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 护眼 / 休息提醒的<b>接线</b>闸门（扫源码，不跑界面）。
/// <para>
/// 节拍判据本身已经由 <see cref="EyeRestPolicyTests"/> 用注入时刻钉死了；这里守的是另一类错：
/// <b>只有真机才看得见、但一写错就变成"打扰别人"或"整个功能失效"的那几条链路约束</b>——
/// 幕布能不能被 Esc 提前揭开（规格点名"防形同虚设"）、幕布抢不抢焦点（抢了会把用户正在填的表单弄丢）、
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

    // ────────── 幕布：不给任何提前跳过的出口，但不抢焦点 ──────────

    [Fact]
    public void OverlayRegistersNoKeyOrMouseHandling_SoEscCannotSkipIt()
    {
        var xaml = SourceGate.ReadRepoFile(OverlayXaml);
        var code = SourceGate.ReadRepoFile(OverlayCode);

        // 规格 §2.7：倒数期间 Esc 不跳过。只要注册了任何输入处理，就存在"某条键能揭开"的可能，
        // 而这种口子一旦开，护眼就形同虚设——所以这里钉的是"一条都不注册"。
        foreach (var forbidden in new[] { "KeyDown", "PreviewKeyDown", "KeyUp", "AcceleratorKey",
                                          "PointerPressed", "PointerReleased", "Tapped", "AddHandler" })
        {
            Assert.DoesNotContain(forbidden, xaml);
            Assert.DoesNotContain(forbidden, code);
        }
        Assert.DoesNotContain("Escape", code);
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
    public void EveryCurtainFailurePathDowngradesToABubble()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static bool BeginRest(int intervalMinutes, bool enforced)");
        // 一条正常出口（非强制＝只发气泡）+ 三条失败退路（取显示器列表抛／系统没报告显示器／遮罩建到一半抛）
        // 都必须各有一条气泡出口，而不是静默不提醒——"到点了但什么都没发生"和"没到点"在用户侧长得一模一样。
        Assert.Equal(4, SourceGate.Count(body, "Notify(\"该休息一下了\""));
        Assert.Contains("CloseOverlays();", body);        // 建到一半失败：先收干净已建的，再降级
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

    [Fact]
    public void BubbleFallsBackToTheMainWindowNoticeAndThenToTheLog()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static bool Notify(string title, string body)");
        var tray = body.IndexOf("TryShowTrayNotification", StringComparison.Ordinal);
        var notice = body.IndexOf("ShowNotice", StringComparison.Ordinal);
        Assert.True(tray >= 0 && notice > tray, "主窗收进托盘时 InfoBar 是看不见的：气泡必须排在前面");
        Assert.Contains("StarLog.Info", body);           // 两条出口都没接住时留痕，不静默吞掉
        Assert.Contains("return channel != \"日志\";", body);   // 回报"有没有真的出现在屏幕上"，试一试据此说话
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
        var store = SourceGate.ReadRepoFile(Settings);
        // 开关与强制模式默认关（"== true"）；让路默认开（"!= false"）——不请自来的遮罩是最讨人嫌的一种帮忙，
        // 而"放 PPT 被砸"是要默认防的那一侧。
        Assert.Contains("public bool LoadEyeRestEnabled() => Load() is { } d && d.EyeRestEnabled == true;", store);
        Assert.Contains("public bool LoadEyeRestEnforced() => Load() is { } d && d.EyeRestEnforced == true;", store);
        Assert.Contains("public bool LoadEyeRestDeferOnFullscreen() => Load() is not { } d || d.EyeRestDeferOnFullscreen != false;", store);
        foreach (var field in new[] { "public bool? EyeRestEnabled", "public int? EyeRestIntervalMinutes",
                                      "public bool? EyeRestEnforced", "public bool? EyeRestDeferOnFullscreen" })
            Assert.Contains(field, store);               // 可空＝"没存过"与"存了 false"分得开，缺省才走上面的默认
    }

    [Fact]
    public void SavedIntervalIsClampedOnTheWayIn_NotOnlyOnTheWayOut()
    {
        var store = SourceGate.ReadRepoFile(Settings);
        var save = SourceGate.MethodBody(store, "public void SaveEyeRest(bool enabled, int intervalMinutes, bool enforced, bool deferOnFullscreen)");
        var load = SourceGate.MethodBody(store, "public int LoadEyeRestIntervalMinutes()");
        Assert.Contains("ClampInterval(intervalMinutes)", save);
        Assert.Contains("ClampInterval(", load);
        // 手改 settings.json 写进 0／99999 时，界面显示与真用到的必须是同一个数（两头都夹，不靠一头兜）
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
        var vm = SourceGate.ReadRepoFile(SettingsVm);
        var backfill = SourceGate.Between(vm, "_suppressEyeRestApply = true;", "_suppressEyeRestApply = false;");
        Assert.Equal(4, SourceGate.Count(backfill, "Safe("));     // 四项只回灌显示
        Assert.DoesNotContain("ApplyEyeRest", backfill);           // 不因"显示"而起停定时器
        Assert.DoesNotContain("SaveEyeRest", backfill);            // 更不因打开页面而把当前值写回存档
    }

    [Fact]
    public void StatusLineAdmitsWhenTheTimerIsNotActuallyRunning()
    {
        var vm = SourceGate.ReadRepoFile(SettingsVm);
        var build = SourceGate.MethodBody(vm, "private string BuildEyeRestStatus()");
        Assert.Contains("EyeRestService.IsRunning", build);
        Assert.Contains("节拍表没挂上", build);
        Assert.Contains("NextDueAt", build);                       // 下一次大约几点：用户唯一能核对的证据
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

        var begin = SourceGate.MethodBody(service, "private static bool BeginRest(int intervalMinutes, bool enforced)");
        Assert.Contains("if (_overlays.Count > 0)", begin);   // 连按两次不叠第二层幕布（只会更黑，且不解释任何事）
    }

    [Fact]
    public void ChangingTheSettingInvalidatesTheLastPreview()
    {
        var vm = SourceGate.ReadRepoFile(SettingsVm);
        var apply = SourceGate.MethodBody(vm, "private void ApplyEyeRestSwitch()");
        // 上一次演出来的已经不是新配置了：留着那行会读成"刚验证过现在的设置"
        Assert.Contains("EyeRestPreviewStatus = string.Empty;", apply);
        Assert.Contains("EyeRestStatus = BuildEyeRestStatus();", apply);
    }
}
