#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 RV 的接线闸门：<b>提醒的出口换成自绘的右下角提示卡，而"已提醒"的凭据只能是屏幕上真的有这一块</b>。
/// <para>
/// 起因是发起人的一条真机反馈："所有声称气泡效果的，未曾见到气泡效果，均无提示效果"。
/// 那条链一路读的是系统那句通知请求的返回值，而它在 Windows 11 上<b>返回 TRUE 而什么都不显示</b>——
/// 于是一句谎报的成功把主窗提示条与"只留日志"两级兜底全堵死了，日志全绿而功能是坏的
/// （记忆 ⑨"全绿而功能坏"那一族的第四起：前三起分别是 ItemCard 的绑定极性、画布选工具的布尔方向、
/// 序号粗细没人读；这一起的特征是<b>谎报来自系统 API，代码本身一行都没写错</b>）。
/// </para>
/// <para>测试工程引用不到 <c>StarMark.UI</c>，所以这里全部是源码扫描；锚点一律写成<b>带分号的整条调用式</b>
/// （坑表 #171/#179：注释与代码同名字面会数进同一笔，判据会静默空转）。</para>
/// </summary>
public sealed class NoticeCardGateTests
{
    private const string Window = "src/StarMark.UI/Views/NoticeCardWindow.cs";
    private const string Service = "src/StarMark.UI/Services/NoticeCard.cs";
    private const string Interop = "src/StarMark.UI/Helpers/WindowInterop.cs";
    private const string Reporter = "src/StarMark.UI/Services/TrayReporter.cs";
    private const string EyeRest = "src/StarMark.UI/Services/EyeRestService.cs";
    private const string Capture = "src/StarMark.UI/Services/ScreenshotService.cs";
    private const string Apply = "public bool Apply(string title, string message)";

    // ────────── 那条谎报的出口必须整条消失，而不是"以后别再用" ──────────

    /// <summary>
    /// 全库普查：托盘代发的出口与它那三个旗标一个都不许留下。
    /// <para>留着的不是"没人用的代码"而是<b>一条会谎报成功的出口</b>——下一轮有人图省事把它接回去，
    /// 症状与这次完全一样，而且日志照旧全绿（记忆 ⑦：改判删掉的东西要整条消失）。</para>
    /// </summary>
    [Fact]
    public void TheTrayBalloonExitIsGoneFromEverySource()
    {
        foreach (var forbidden in new[] { "ShowNotification", "TryShowTrayNotification",
                                          "NIM_MODIFY", "NIF_INFO", "NIIF_INFO" })
        {
            var hits = 0;
            foreach (var (_, text) in SourceGate.ReadRepoUnder("src"))
                if (text.Contains(forbidden, StringComparison.Ordinal)) hits++;
            Assert.Equal(0, hits);      // 命中数说话：哪个文件还留着，红的时候能顺着字面找过去
        }
    }

    /// <summary>主窗不再是提醒的中转站（它收进托盘时看不见，而旧代码正是从那兒转手的）。</summary>
    [Fact]
    public void NoReminderGoesThroughTheMainWindowAnymore()
    {
        foreach (var (_, text) in SourceGate.ReadRepoUnder("src"))
            Assert.DoesNotContain("App.MainWindow?.TryShow", text, StringComparison.Ordinal);
    }

    // ────────── "已提醒"的凭据：窗口的实际矩形，不是任何一发的返回值 ──────────

    [Fact]
    public void TheCredentialIsTheWindowRect_NotSomeApiReturnValue()
    {
        var placed = SourceGate.MethodBody(SourceGate.ReadRepoFile(Window), "private static bool IsPlaced(IntPtr hwnd)");
        Assert.Contains("WindowInterop.IsWindowVisible(hwnd)", placed);
        Assert.Contains("GetWindowRect(hwnd, out var r)", placed);
        Assert.Contains("!WindowInterop.IsOffScreen(rect)", placed);   // 矩形还得真的落在某块屏的工作区内

        // 摆放之后必须"再验一次"才算数：算出坐标 ≠ 屏幕上真有这一块（旧的那版就是这么错的）。
        var apply = SourceGate.MethodBody(SourceGate.ReadRepoFile(Window), Apply);
        Assert.Contains("IsOnScreen = Place(hwnd, work, dpi) && IsPlaced(hwnd);", apply);
        // 每一次贴上屏幕都留下几何：这一批的缺陷正是"日志说发了、屏幕上没有"，事后唯一的分辨凭据就是这一句
        Assert.Contains("[提示卡] 已贴上屏幕：", apply);
    }

    [Fact]
    public void TheCardSitsOnTheMonitorUnderTheCursor()
    {
        var code = SourceGate.ReadRepoFile(Window);
        Assert.Contains("WindowInterop.MonitorWorkAreaAtCursor();", code);
        // 问不到光标在哪块屏时不许"随便摆一个位置"冒充提醒，要老实回 false
        var apply = SourceGate.MethodBody(code, Apply);
        Assert.Contains("if (monitor is null)", apply);
        Assert.Contains("HideCard();", apply);

        var helper = SourceGate.MethodBody(SourceGate.ReadRepoFile(Interop),
            "public static (RectInt32 Work, double Scale)? MonitorWorkAreaAtCursor()");
        Assert.Contains("GetCursorPos(out var pt)", helper);
        Assert.Contains("MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST)", helper);
        // 贴的是工作区（去掉任务栏），不是整屏：压在任务栏上会盖住开始菜单，也读不全
        Assert.Contains("mi.RcWork", helper);
        Assert.Contains("GetMonitorScale(hmonitor)", helper);
    }

    // ────────── 不抢焦点、但点得掉：与暗幕/贴图条同一套配方 ──────────

    [Fact]
    public void TheCardIsTopmostButNeverStealsTheForeground()
    {
        var apply = SourceGate.MethodBody(SourceGate.ReadRepoFile(Window), Apply);
        var record = apply.IndexOf("GetForegroundWindow()", StringComparison.Ordinal);
        var show = apply.IndexOf("AppWindow.Show();", StringComparison.Ordinal);
        var restore = apply.IndexOf("SetForegroundWindow(previous)", StringComparison.Ordinal);
        Assert.True(record >= 0 && show > record && restore > show,
            "先记前台窗 → 点亮 → 还原前台，三步缺一不可且顺序不能换（抢了焦点会吞掉用户正在填的表单）");
        Assert.Contains("previous != hwnd", apply);

        var place = SourceGate.MethodBody(SourceGate.ReadRepoFile(Window), "private bool Place(IntPtr hwnd, RectInt32 work, double dpi)");
        Assert.Contains("WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, x, y, width, height,", place);
        Assert.Contains("WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP, 0, 0, 0, 0,", place);   // 两步都要：只传 TOP 留在普通层
        Assert.Contains("WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);", place);
        Assert.Contains("WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE", place);
    }

    /// <summary>
    /// 不抢前台靠的是"亮完把前台还回去"，<b>不是</b>把窗设成不可激活：贴图条那次真机验证过，
    /// 挂上 <c>WS_EX_NOACTIVATE</c> 之后整扇窗收不到 XAML 的点击——卡看得见、点不动，
    /// 而这张卡唯一的鼠标出口就是"点一下关闭"。
    /// </summary>
    [Fact]
    public void TheCardTakesAClick_AndIsNotMadeUnactivatableToKeepTheFocus()
    {
        var code = SourceGate.ReadRepoFile(Window);
        Assert.DoesNotContain("WS_EX_NOACTIVATE", code);
        Assert.DoesNotContain(".Activate()", code);

        var mount = SourceGate.MethodBody(code, "private void MountClickToClose()");
        Assert.Contains("_card.PointerPressed += (_, _) => HideCard();", mount);
        Assert.Contains("WindowActivationState.PointerActivated", mount);   // 第一次点击常被当成"激活"而吞掉输入

        // 只挂一次：这扇窗是复用的，每次换内容都挂一遍就会攒出一串处理器
        Assert.Contains("if (_clickMounted) return;", mount);
        var apply = SourceGate.MethodBody(code, Apply);
        Assert.True(apply.IndexOf("IsOnScreen = Place(", StringComparison.Ordinal)
            < apply.IndexOf("MountClickToClose();", StringComparison.Ordinal),
            "退出出口必须挂在点亮并摆放之后（先挂上就等于卡片出现那一刻把自己收了）");
    }

    // ────────── 只有一张卡：新消息换内容，不叠窗 ──────────

    [Fact]
    public void ThereIsExactlyOneCard_AndANewMessageReplacesIt()
    {
        var service = SourceGate.ReadRepoFile(Service);
        Assert.Equal(1, SourceGate.Count(service, "new NoticeCardWindow("));
        Assert.Contains("_window = new NoticeCardWindow();", service);
        Assert.Contains("return _window.Apply(title, message);", service);
        // 窗被外力关掉后要能把引用丢掉，否则下一条永远发给一扇已死的窗，而"没贴上屏幕"会一直冒充兜底
        Assert.Contains("_window.Closed += (_, _) => _window = null;", service);
        // 收卡不是关窗：这条链随时会再来一条消息
        Assert.Contains("WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE);",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Window), "public void HideCard()"));
    }

    /// <summary>停留秒数只有一个出处：卡片上那句"N 秒后自动消失"与定时器读的是同一个常量。</summary>
    [Fact]
    public void TheHoldSecondsAreSaidOnce_NotRetypedInTheCardText()
    {
        var code = SourceGate.ReadRepoFile(Window);
        Assert.Contains("_hide.Interval = TimeSpan.FromSeconds(KeepSeconds);", code);
        Assert.Contains("点一下关闭 · {KeepSeconds} 秒后自动消失", code);
        Assert.DoesNotContain("10 秒后自动消失", code);
    }

    // ────────── 每个提醒出口都改问这张卡 ──────────

    [Fact]
    public void EveryReminderAsksTheCard()
    {
        foreach (var file in new[]
        {
            Reporter, EyeRest,
            "src/StarMark.UI/Services/AnnotationHub.cs",
            "src/StarMark.UI/Services/CanvasService.cs",
            "src/StarMark.UI/MainWindow.xaml.cs",
            "src/StarMark.UI/Views/CountdownWidget.xaml.cs",
            "src/StarMark.UI/Views/FocusTimerWidget.xaml.cs",
        })
            Assert.Contains("NoticeCard.Show(", SourceGate.ReadRepoFile(file));

        // 共用那份回报的措辞也要跟着换：日志里那句旧话会教用户去找一个不存在的面板
        var report = SourceGate.MethodBody(SourceGate.ReadRepoFile(Reporter), "public static void Report(string category, string title, string body)");
        Assert.Contains("shown = NoticeCard.Show($\"{category} · {title}\", body);", report);
        Assert.Contains("（提示卡没能贴上屏幕，结果只留在日志）", report);
        Assert.DoesNotContain("托盘未启用", report);   // 别再退回"怪托盘没启用"那套说法：出口已经不是它了

        // 护眼那一档的说法也只有一个出处（Core 的标签 + 服务的通道名），界面/日志不许各写一份
        var notify = SourceGate.MethodBody(SourceGate.ReadRepoFile(EyeRest), "private static bool Notify(string title, string body)");
        Assert.Contains("if (NoticeCard.Show(title, body)) channel = NoticeChannel;", notify);
        Assert.Contains("public const string NoticeChannel = \"右下角提示卡\";", SourceGate.ReadRepoFile(EyeRest));
    }

    // ────────── 截图那一帧不许把卡烤进图里 ──────────

    /// <summary>
    /// 抓屏抓的是<b>已经合成好的屏幕</b>，卡片事后减不回来（记忆 ⑩），所以含不含它只能在那一帧之前收起来决定；
    /// 而收/还必须在同一处的 <c>finally</c> 里，漏还的症状是"截一次图之后提示卡再也不出现"。
    /// </summary>
    [Fact]
    public void TheCaptureFrameHidesTheCardAndAlwaysGivesItBack()
    {
        var code = SourceGate.ReadRepoFile(Capture);
        foreach (var signature in new[] { "private static CaptureResult Grab()", "public static CaptureResult CaptureWithoutCanvas()" })
        {
            var body = SourceGate.MethodBody(code, signature);
            Assert.Contains("NoticeCard.SetHiddenForCapture(true);", body);
            Assert.Contains("finally", body);
            Assert.True(body.IndexOf("NoticeCard.SetHiddenForCapture(true);", StringComparison.Ordinal)
                < body.IndexOf("NoticeCard.SetHiddenForCapture(false);", StringComparison.Ordinal),
            $"{signature}：收起要排在还原之前");
        }

        // 两个入口会套起来（Grab 里再调 CaptureWithoutCanvas），所以窗那侧按层数记，不是按布尔
        var window = SourceGate.MethodBody(SourceGate.ReadRepoFile(Window), "public void SetHiddenForCapture(bool hidden)");
        Assert.Contains("if (_captureDepth == 0) _wasOnScreenBeforeCapture = IsOnScreen;", window);
        Assert.Contains("_captureDepth++;", window);
        Assert.Contains("if (--_captureDepth > 0) return;", window);
    }
}
