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
    /// 全库普查：托盘代发的出口与它的谎报旗标一个都不许留下。
    /// <para>留着的不是"没人用的代码"而是<b>一条会谎报成功的出口</b>——下一轮有人图省事把它接回去，
    /// 症状与这次完全一样，而且日志照旧全绿（记忆 ⑦：改判删掉的东西要整条消失）。</para>
    /// <para>WI-2o 起换口径：托盘 tooltip 的换语言重发（<c>NIM_MODIFY</c>＋<c>NIF_TIP</c>）是合法出口，
    /// 它不附通知——气泡要弹出来非 <c>NIF_INFO</c> 不可。所以 <c>NIM_MODIFY</c> 从"全库禁字"
    /// 降为"只许住在 TrayHost"（SetTip 一颗），气泡那三个字面（NIF_INFO／NIIF_INFO／两个方法名）仍是全库禁项。</para>
    /// </summary>
    [Fact]
    public void TheTrayBalloonExitIsGoneFromEverySource()
    {
        foreach (var forbidden in new[] { "ShowNotification", "TryShowTrayNotification",
                                          "NIF_INFO", "NIIF_INFO" })
        {
            var hits = 0;
            foreach (var (_, text) in SourceGate.ReadRepoUnder("src"))
                if (text.Contains(forbidden, StringComparison.Ordinal)) hits++;
            Assert.Equal(0, hits);      // 命中数说话：哪个文件还留着，红的时候能顺着字面找过去
        }
        // NIM_MODIFY 只许两处：TrayHost 的 tooltip 重发（合法主人）与 NativeMethods 的那一行声明
        // （常量的家，只许声明不许用）。在别处出现＝气泡那条路被接回去了。
        foreach (var (path, text) in SourceGate.ReadRepoUnder("src"))
        {
            if (!text.Contains("NIM_MODIFY", StringComparison.Ordinal)) continue;
            if (path.EndsWith("TrayHost.cs", StringComparison.Ordinal)) continue;
            if (path.EndsWith("NativeMethods.cs", StringComparison.Ordinal)
                && text.Split("NIM_MODIFY").Length - 1 == 1
                && text.Contains("const uint NIM_MODIFY", StringComparison.Ordinal)) continue;
            Assert.Fail($"NIM_MODIFY 溜出了 TrayHost（气泡出口的形状又回来了）：{path}");
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
        // 批次 WI-47：那两行改由表里说 ⇒ 锚点跟着改锚，钉的仍是"停留秒数当实参交出去、代码里⛔ 写死数字"
        Assert.Contains("Locale.Fmt(\"card.hint.plain\", KeepSeconds)", code);
        Assert.DoesNotContain("10 秒后自动消失", code);
    }

    /// <summary>
    /// 卡片自己那一行 footer 与那两颗动作标签都交表（批次 WI-47，P-177 收尾）。
    /// <para>footer 是<b>每一张卡</b>共用的一行 ⇒ 它归这一族的最后一刀（WI-46a 判定 3）：提前交出去＝还没交完的
    /// 那几族每张卡从此半英半中。停留秒数仍只有 <c>KeepSeconds</c> 一个主人——两行都收实参，表里⛔ 写死"10 秒"。</para>
    /// <para>⚠ 禁项都<b>带引号点名</b>（坑表 #368）：那颗窗的日志里有一句「…{KeepSeconds} 秒后自动消失）」，
    /// 裸词会把日志一起判死，于是后人只能删日志换绿灯。</para>
    /// <para>两颗更新卡抬头（<c>upd.card.*</c>）也归这里：它们从前是 <c>ShowCard("检查更新", …)</c> 的<b>私有包装实参</b>——
    /// "按调用形状数屏幕字"那把尺只看见直接调 <c>NoticeCard.Show</c> 的点，于是整族看不见（新尺见坑表 #375）。
    /// ⚠ 那颗禁项读 <c>Code()</c>：同档注释里就有一句「托盘『检查更新』」，连注释读会把解释判死。</para>
    /// </summary>
    [Fact]
    public void TheCardFooterAndItsActionLabelsAreTakenFromTheTable()
    {
        var win = SourceGate.Code(SourceGate.ReadRepoFile(Window));
        var upd = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/UpdateScheduler.cs"));
        Assert.Equal(1, SourceGate.Count(win, "Locale.Fmt(\"card.hint.plain\", KeepSeconds)"));
        Assert.Equal(1, SourceGate.Count(win, "Locale.Fmt(\"card.hint.withAction\", label ?? Locale.T(\"card.action.link\"), KeepSeconds)"));
        Assert.Equal(1, SourceGate.Count(upd, "Locale.T(\"card.action.releasePage\")"));
        foreach (var dead in new[] { "\"点一下关闭", "\"点一下打开", "\"链接\"", "\"发布页\"" })
        {
            Assert.DoesNotContain(dead, win);
            Assert.DoesNotContain(dead, upd);
        }
        // 抬头两颗：一张是"他刚按了检查更新"的回话，一张是"真的有新版"——两件事，各一颗键，⛔ 复用托盘那一项
        Assert.Equal(1, SourceGate.Count(upd, "ShowCard(Locale.T(\"upd.card.checkTitle\"), report,"));
        Assert.Equal(1, SourceGate.Count(upd, "ShowCard(Locale.T(\"upd.card.newVersionTitle\"), report, report.PageUrl)"));
        foreach (var dead in new[] { "\"检查更新\"", "\"发现新版本\"" })
            Assert.DoesNotContain(dead, upd);
    }

    /// <summary>
    /// 「谁在说话 · 说了什么」这一句的拼装只许一处（批次 WI-47）。
    /// <para>从前番茄钟自己写着 <c>"番茄钟 · " + title</c>：同一动作两份实现，分隔符漂移时只有那一族会跟着漂；
    /// 而且那颗"说话者"的名字与组件名是两个出处 ⇒ 现在都从 <c>widget.kind.focus</c> 取（组件名只有一个主人）。</para>
    /// </summary>
    [Fact]
    public void TheSpeakerDotTitleJoinHasOneOwner()
    {
        var reporter = SourceGate.Code(SourceGate.ReadRepoFile(Reporter));
        var focus = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/FocusTimerWidget.xaml.cs"));
        var clock = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/CountdownWidget.xaml.cs"));
        var hub = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Services/AnnotationHub.cs"));
        Assert.Equal(1, SourceGate.Count(reporter, "public static string CardTitle(string category, string title)"));
        Assert.Equal(1, SourceGate.Count(reporter, "NoticeCard.Show(CardTitle(category, title), body);"));
        Assert.Equal(1, SourceGate.Count(focus, "TrayReporter.CardTitle(Locale.T(\"widget.kind.focus\"), title)"));
        foreach (var file in new[] { reporter, focus, clock, hub })
            Assert.DoesNotContain(" · \" +", file);
        Assert.DoesNotContain("\"番茄钟 · \"", focus);
        // 到点那两族自己的句子也交表了（倒计时那颗正文是条目名＝数据，⛔ 进表）
        Assert.Equal(1, SourceGate.Count(clock, "Locale.T(\"widget.countdown.dueTitle\")"));
        Assert.Equal(5, SourceGate.Count(focus, "Locale.T(\"widget.focus."));
        Assert.DoesNotContain("\"倒计时到点\"", clock);
        foreach (var dead in new[] { "\"专注结束\"", "\"休息结束\"", "\"该休息一下了\"", "不自动进休息", "可以开始下一轮" })
            Assert.DoesNotContain(dead, focus);
    }

    /// <summary>
    /// 主窗那张"正在后台运行"的卡说的是<b>他实际绑着的那颗键</b>（批次 WI-47，结案 P-178）。
    /// <para>从前正文写死 <c>Ctrl+Alt+Space</c>：那颗是<b>用户可改</b>的全局热键，改了键之后这句在教人去按一颗哑键
    /// ＝一条说明配了一颗哑键（同一禁项在批次 VC 已为说明文字定死）。现在正文收 <c>CanvasService.BindingText</c> 那颗
    /// <b>唯一实现</b>的键位文本（没绑定时回表里那颗 <c>hotkey.unbound</c>，⛔ 回一个假键位）。</para>
    /// </summary>
    [Fact]
    public void TheBackgroundCardSaysTheRealBinding_NotACopiedKey()
    {
        const string Main = "src/StarMark.UI/MainWindow.xaml.cs";
        var main = SourceGate.Code(SourceGate.ReadRepoFile(Main));
        Assert.Equal(1, SourceGate.Count(main, "Locale.T(\"main.bgRunningTitle\")"));
        Assert.Equal(1, SourceGate.Count(main, "Locale.Fmt(\"main.bgRunningBody\", CanvasService.BindingText(HotkeyActions.MainToggle))"));
        Assert.DoesNotContain("Ctrl+Alt", main);                  // 与画布那颗面板同一禁项：键名⛔ 抄进句子
        Assert.DoesNotContain("\"StarMark 正在后台运行\"", main);
        Assert.DoesNotContain("随时呼出窗口\"", main);             // 那句话现在在表里，代码里只许留下取词
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
            // 批次 UE：更新检查的出声也走这一张卡（它不是第二条提醒出口，只是多一个调用方）。
            "src/StarMark.UI/Helpers/UpdateScheduler.cs",
        })
            Assert.Contains("NoticeCard.Show(", SourceGate.ReadRepoFile(file));

        // 共用那份回报的措辞也要跟着换：日志里那句旧话会教用户去找一个不存在的面板
        var report = SourceGate.MethodBody(SourceGate.ReadRepoFile(Reporter), "public static void Report(string category, string title, string body)");
        // 批次 WI-47：抬头那句的拼装搬进 `CardTitle`（一个主人），这里的锚点跟着改锚
        Assert.Contains("shown = NoticeCard.Show(CardTitle(category, title), body);", report);
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
