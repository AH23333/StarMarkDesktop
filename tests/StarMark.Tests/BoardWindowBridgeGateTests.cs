using System;
using System.IO;
using System.Linq;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板桥回路那面闸门（Phase C 片19）：页面侧名单与 Core <see cref="BridgeProtocol"/> 逐颗对账、
/// BoardWindow 的宿主形状（虚拟宿主名／userDataFolder 改道／关窗即散／默认关）。
/// </summary>
public sealed class BoardWindowBridgeGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string ReadRepo(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative));

    private const string BridgeJs = "tools/boards/board-bridge.js";
    private const string BoardHtml = "tools/boards/board.html";
    private const string WindowCs = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string MainCs = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string TrayCs = "src/StarMark.UI/MainWindow.Tray.cs";

    [Fact]
    public void BridgeJsSpeaksTheWiredSubsetOfTheProtocol()
    {
        var js = ReadRepo(BridgeJs);
        // 片19 最小回路：页面侧只会喊它真正实现了的那两颗；其余三颗 ⛔ 假喊（宿主回 not-wired）
        Assert.Equal(1, Count(js, "postIn(\"bridge.ready\""));
        Assert.Equal(1, Count(js, "postIn(\"export.bytes\""));
        Assert.Equal(0, Count(js, "postIn(\"paste.query\""));
        Assert.Equal(0, Count(js, "postIn(\"pin.request\""));
        Assert.Equal(0, Count(js, "postIn(\"background.request\""));
        // 出向五颗：页面侧逐颗处理（case 逐颗一处）
        foreach (var name in BridgeProtocol.OutboundEvents)
            Assert.Equal(1, Count(js, "case \"" + name + "\""));
    }

    [Fact]
    public void BridgeJsNeverPostsAnUnknownCmdLiteral()
    {
        var js = ReadRepo(BridgeJs);
        var matches = System.Text.RegularExpressions.Regex.Matches(js, "postIn\\(\"([^\"]+)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
            Assert.True(BridgeProtocol.IsKnownName(m.Groups[1].Value, BridgeProtocol.Direction.Inbound),
                "页面侧出现了名单外的 cmd：" + m.Groups[1].Value);
    }

    [Fact]
    public void BoardPageWiresTheAssetGlobalAndTheBundle()
    {
        var html = ReadRepo(BoardHtml);
        Assert.Contains("window.EXCALIDRAW_ASSET_PATH = \"/\";", html, StringComparison.Ordinal);
        Assert.Contains("excalidraw.min.js", html, StringComparison.Ordinal);
        Assert.Contains("board-bridge.js", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardWindowUsesTheVirtualHostAndThePinnedUserDataFolder()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("SetVirtualHostNameToFolderMapping", cs, StringComparison.Ordinal);
        Assert.Contains("https://\" + VirtualHostName + \"/\" + BoardPageName", cs, StringComparison.Ordinal);
        // v1 §坑8 那条改道：userDataFolder 钉在 %LOCALAPPDATA%\StarMark\wbview2，⛔ 跟 exe 目录
        Assert.Contains("SpecialFolder.LocalApplicationData", cs, StringComparison.Ordinal);
        Assert.Contains(@"StarMark\wbview2", cs, StringComparison.Ordinal);
        Assert.Equal(1, Count(cs, "SetVirtualHostNameToFolderMapping"));
    }

    [Fact]
    public void BoardWindowDisposesOnClosedAndStaysDefaultOff()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("Closed += (_, _) => OnClosed()", cs, StringComparison.Ordinal);
        Assert.Contains("_web.Close()", cs, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived -= OnWebMessageReceived", cs, StringComparison.Ordinal);
        // 默认关：本档⛔ 自动挂常驻入口（主窗接线归接线批）
        Assert.DoesNotContain("MainWindow", cs, StringComparison.Ordinal);
        Assert.Contains("OpenNewAsync", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardWindowNeverInventsItsOwnWhitelist()
    {
        var cs = ReadRepo(WindowCs);
        // 名单主人＝BridgeProtocol：窗口⛔ 复写一份名单数组（第二主人＝对不上时静默失效那颗坑）
        Assert.DoesNotContain("string[] InboundCommands", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("string[] OutboundEvents", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InboundFields =", cs, StringComparison.Ordinal);
        // 三颗已放行的行为分支在；未接线的三颗走 default 的 not-wired
        Assert.Contains("case \"bridge.ready\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"open.external\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"export.bytes\"", cs, StringComparison.Ordinal);
        Assert.Contains("not-wired", cs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 片25 取证用的那颗"启动即起板"钩子：取值是一颗闭集、起窗只走托盘那两颗入口共用的同一条主人。
    /// <para>为什么要有这一颗钩子：§21.3 那四拍要的真机入口是"一块板上正在画"，而托盘那颗图标在 UIA 里
    /// 探不到（现读：Shell_TrayWnd 子树 3 颗节点、0 颗有名按钮；那颗溢出岛 0 颗节点）——把"人手点一下"
    /// 当验收条件＝本仓定性过的坏。⛔ 因此另起一份开场代码：这一格里那颗 <c>OpenBoardsWindowAsync(</c>
    /// 在主窗档里只许出现一次（＝一颗调用），定义与另两颗调用都住在托盘那一档。</para>
    /// </summary>
    [Fact]
    public void TheDevHookStartsABoardThroughTheTraysOpener()
    {
        var main = ReadRepo(MainCs);
        var tray = ReadRepo(TrayCs);
        Assert.Equal(1, Count(main, "GetEnvironmentVariable(\"STARMARK_START_BOARD\")"));
        // ⚠ 钉<b>整行</b>字面量：台架的 A2 臂（把取值集合放宽成 "new"｜"last"｜"resume"）证明
        //   两颗 Assert.Contains 对"多一颗档位"是瞎的——那句前缀照样命中＝一颗恒绿断言（坑表 #695 同族）。
        Assert.Contains(
            "if (startBoard is \"new\" or \"last\") _ = OpenBoardsWindowAsync(resumeLast: startBoard == \"last\");",
            main, StringComparison.Ordinal);
        Assert.Equal(1, Count(main, "OpenBoardsWindowAsync("));                    // 主窗档里只有那一颗调用
        Assert.DoesNotContain("new StarMark.UI.Boards.BoardWindow()", main, StringComparison.Ordinal);
        Assert.Equal(1, Count(tray, "private async System.Threading.Tasks.Task OpenBoardsWindowAsync(bool resumeLast)"));
        Assert.Equal(3, Count(tray, "OpenBoardsWindowAsync("));                    // 定义一颗＋托盘两颗
    }

    private static int Count(string haystack, string needle)
        => haystack.Split(needle).Length - 1;

    // ────────── 片24：四拍剩下那两拍 ──────────

    /// <summary>
    /// <b>崩</b>那一拍与<b>关窗</b>那一拍都真挂上了，而且开口之前先问 Core「这一拍问得到吗」。
    /// <para>这一格里最要紧的是 <c>_bridgeReady = false;</c> 出现在那颗 handler 里：那一颗旗要是还亮着，
    /// 下一次失焦就往一颗<b>死控件</b> <c>PostWebMessageAsJson</c>——COM 异常当场抛在 UI 线程上，
    /// 用户看到的正是"画板自己崩给我看"。</para>
    /// </summary>
    [Fact]
    public void TheLastTwoSaveBeatsAreWiredAndBothAskThroughCore()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("ProcessFailed += OnProcessFailed", cs, StringComparison.Ordinal);
        Assert.Contains("ProcessFailed -= OnProcessFailed", cs, StringComparison.Ordinal);   // 拆窗那一拍摘干净
        Assert.Contains("AppWindow.Closing += OnAppWindowClosing", cs, StringComparison.Ordinal);
        Assert.Contains("BoardPolicy.BeatCanAskForSnapshot(beat)", cs, StringComparison.Ordinal);
        Assert.Contains("RequestSceneSnapshot(BoardPolicy.SaveBeat.LosingFocus)", cs, StringComparison.Ordinal);
        Assert.Contains("RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow)", cs, StringComparison.Ordinal);
        Assert.Equal(3, Count(cs, "RequestSceneSnapshot("));        // 定义一颗＋两拍各一颗
        Assert.Contains("_bridgeReady = false;",
            SourceGate.MethodBody(cs, "void OnProcessFailed"), StringComparison.Ordinal);
    }

    /// <summary>
    /// 恢复起来那一页拿到的＝<b>宿主见过的那一份</b>，⛔ 一颗空板（§21.5 那句硬约束落在这颗字段上）。
    /// <para>首开与重载共用同一条 <c>OpenAsync</c>：两份开场代码就是第二个主人，而第二主人的第一号坏法
    /// 是"其中一份忘了递档"。那颗被摘掉的 <c>_pendingScene</c> 名字两头都⛔ 留（正件＝新名字在这一档里读得到）。</para>
    /// </summary>
    [Fact]
    public void TheRecoveredPageGetsTheSceneTheHostHasSeenNotAnEmptyBoard()
    {
        var cs = ReadRepo(WindowCs);
        // 片27 把递档抽成一颗共用的 <c>PostBoardLoad</c>：这一格问的两件事因此分开钉——
        // ready 那一拍"见过一份就递一份"，而那颗方法自己拒收空的那一份（⛔ 把空字节当一块板递进画布）。
        Assert.Contains("if (_seenScene is { Length: > 0 }) PostBoardLoad();", cs, StringComparison.Ordinal);
        Assert.Contains("if (_seenScene is not { Length: > 0 } bytes) return;", cs, StringComparison.Ordinal);
        Assert.Contains("RecoverByRecreatingWeb", cs, StringComparison.Ordinal);
        // 片27 起，重载那一手是"先取核、核在才 Reload"，而那句"正在恢复"只跟着真递出去的那一下（?. 短路＝贴一句做不完的假话）
        Assert.Contains("if (view is not null) { view.Reload(); ShowRecoveringNotice(); }", cs, StringComparison.Ordinal);
        Assert.Equal(1, Count(cs, "private async System.Threading.Tasks.Task OpenAsync("));
        Assert.Contains("await OpenAsync(_seenScene)",
            SourceGate.MethodBody(cs, "Task OpenRecoverAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("_pendingScene", cs, StringComparison.Ordinal);
        Assert.True(Count(cs, "_seenScene") >= 5, "这把尺要是瞎的，新名字在这一档里也读不到");
    }

    /// <summary>
    /// 关窗那一拍的等待<b>有界</b>、等的是"这一问有答案"而⛔"答案好听"，而且到点会说话。
    /// <para>那颗毫秒数只许住在主人那里（UI 档里再写一遍 2000＝第二主人，改的人只改得动一处）；
    /// 三处 <c>FinishClosingFlush();</c> 分别对应"盘上本来就是它"、"这一问交回答案了（写成功还是写失败都算）"
    /// 与片26 新加的那一颗"这一问被写闸拒了"——
    /// 少了前者那次关不掉，少了后者那颗写失败要用户白等两秒。</para>
    /// </summary>
    [Fact]
    public void TheCloseFlushIsBoundedAndSaysWhenItGaveUp()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        Assert.Contains("BoardPolicy.CloseFlushBudgetMs", code, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "2000"));                       // 那颗数⛔ 在这档里再出现一次
        Assert.Equal(2000, BoardPolicy.CloseFlushBudgetMs);         // 正件：主人那里它还在
        Assert.Equal(3, Count(code, "FinishClosingFlush();"));      // 片26 多出的那一处＝"这一问被写闸拒了"也是答案
        Assert.Contains("AppWindow.Hide()", code, StringComparison.Ordinal);
        Assert.Contains("boards.close.flush.timeout", raw, StringComparison.Ordinal);
        Assert.Contains("_recovering", code, StringComparison.Ordinal);   // 两颗控件抢同一扇窗＝更坏的现场
    }

    /// <summary>
    /// 停笔那一拍等的是"数对上了"那句话（P-221 落地，片26 把它从一句<b>无参的话</b>换成<b>三颗数全等</b>）。
    /// <para>这颗格有三半，缺任一半都⛔ 算修好：<b>(甲)</b> 页面侧那颗"读事件对象三颗键"的恒假判据⛔ 许抄回来
    /// （引擎交的是位置参数——现读 bundle <c>this.props.onChange?.(C,this.state,this.files)</c>）；
    /// <b>(乙)</b> 开闸的话两条路都要说得出（有档可载＝等页面回 <c>board.loaded</c>，新板＝当场说），
    /// 漏发＝这一扇永远⛔ 写＝把 P-221 换成一条更静的坏；<b>(丙)</b> 而"没落地就开闸"是两跑现场量到的
    /// 数据丢失（片25 挂载那一拍 4 元素→241 字节／0 元素；片26 重载那一拍 2608 字节／2 元素→241 字节／0 元素）
    /// ⇒ 开闸只许由页面<b>报出数来</b>驱动，宿主拿自己递进去的那颗数对完才算数。</para>
    /// <para>⚠ 那颗"对完"⛔ 能写在注释里：本批先按"页面回一句 board.loaded 就开闸"落地，跑真机才发现
    /// 那句话照样是空的（promise 解掉了而画布是白的）＝"消息到达 ⛔ 等于状态达成"（坑表那一族）。</para>
    /// </summary>
    [Fact]
    public void TheIdleBeatWaitsForTheHostsArmWord()
    {
        var js = ReadRepo(BridgeJs);
        Assert.Equal(0, Count(js, "event.elements"));                              // 那颗恒假判据
        Assert.Equal(1, Count(js, "if (!hostArmed) return;"));
        Assert.Equal(1, Count(js, "hostArmed = true;"));
        Assert.Equal(1, Count(js, "case \"autosave.arm\":"));
        // 载完了那句必须<b>带两颗数</b>，而那颗画布数取的是<b>序列化器自己问的那一颗出口</b>（getSceneElements＝滤掉删的）
        Assert.Equal(1, Count(js, "postIn(\"board.loaded\", { elements: writable, canvas: onCanvas });"));
        Assert.Equal(1, Count(js, "onCanvas = API.getSceneElements().length;"));
        Assert.Equal(0, Count(js, "getSceneElementsIncludingDeleted"));   // 那颗⛔ 同口径的出口⛔ 许被拿回来当证据（片26 第一跑栽在这上面）

        var cs = ReadRepo(WindowCs);
        var body = SourceGate.MethodBody(cs, "void OnBridgeReady");
        // 片27 把"递档"抽成一颗共用的 <c>PostBoardLoad</c>（首开／重建／重试三趟共用一份形状）：
        // 这一句因此改问两件事——ready 那一拍确实叫了它，而那颗出向话住在它那里（⛔ 删断言，改锚）。
        Assert.Contains("PostBoardLoad();", body, StringComparison.Ordinal);
        Assert.Contains("PostOutbound(\"board.load\"",
            SourceGate.MethodBody(cs, "void PostBoardLoad"), StringComparison.Ordinal);
        Assert.DoesNotContain("autosave.arm", body, StringComparison.Ordinal);     // (丙)⛔ 同拍开闸
        Assert.Contains("ArmIdleBeat(\"no-scene\")", body, StringComparison.Ordinal);  // (乙)新板那一途
        var inbound = SourceGate.MethodBody(cs, "void OnWebMessageReceived");
        Assert.Contains("BoardPolicy.ShouldArmAfterLoad(want, envelope.Elements, envelope.Canvas)",
            inbound, StringComparison.Ordinal);                                     // (丙)开闸前先把数对完
        Assert.Contains("ArmIdleBeat(\"loaded\",", inbound, StringComparison.Ordinal);   // (乙)载完那一途
        Assert.Contains("boards.arm.refused", cs, StringComparison.Ordinal);       // 对不上要有那句话可查，⛔ 静默
        Assert.Equal(3, Count(cs, "ArmIdleBeat("));                                 // 一颗定义＋两颗调用
        Assert.Contains("PostOutbound(\"autosave.arm\", Envelope(\"autosave.arm\", \"h5\", string.Empty));",
            SourceGate.MethodBody(cs, "void ArmIdleBeat"), StringComparison.Ordinal);   // 唯一出口（⛔ 第二主人）
    }

    /// <summary>
    /// 写盘那颗闸门握在<b>宿主</b>手里（片26＝P-223 的修法）。
    /// <para>页面那颗 <c>hostArmed</c> 只包得住停笔一路：失焦与关窗那两拍是宿主问、页面答
    /// （<c>export.pick</c>→<c>runExport</c>→<c>deliver</c>），现读那条路上⛔ 有对方那道 if。
    /// 写盘的笔本来就握在宿主手里，主闸就⛔ 装在对方身上。</para>
    /// <para>四颗形状各钉一处：每一趟 ready 重新闩上（新页面要自己重新举证）、开闸只许有一个出口、
    /// 门排在档位分流<b>之后</b>（png／svg 是一次性产物，⛔ 被它挡）、被拒的字节⛔ 进 <c>_seenScene</c>
    /// ——进了＝下一趟恢复会把空场景当"见过的那一份"递回画布，那才是把一次白屏变成永久丢板的一手。</para>
    /// </summary>
    [Fact]
    public void TheHostHoldsTheWriteGate()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        // 片27 多出第五处＝三圈用尽那一档当场把闸也关掉（那一趟再没人证明过它回到过这块板）
        Assert.Equal(5, Count(code, "_autosaveArmed"));             // 一颗声明＋闩上＋开闸＋一读＋givenup 关闸
        Assert.Contains("_autosaveArmed = false;",
            SourceGate.MethodBody(raw, "void OnBridgeReady"), StringComparison.Ordinal);
        Assert.Contains("_autosaveArmed = true;",
            SourceGate.MethodBody(raw, "void ArmIdleBeat"), StringComparison.Ordinal);

        var export = SourceGate.MethodBody(raw, "void HandleExportBytes");
        Assert.Equal(1, Count(export, "if (!_autosaveArmed)"));
        Assert.True(export.IndexOf("if (!_autosaveArmed)", StringComparison.Ordinal)
                    < export.IndexOf("_seenScene = bytes;", StringComparison.Ordinal),
                    "被拒的那一份⛔ 许当成\"宿主见过的那一块板\"");
        Assert.Contains("LandsInSceneArchive", export, StringComparison.Ordinal);  // 正件：门在档位分流之后
        Assert.True(export.IndexOf("LandsInSceneArchive", StringComparison.Ordinal)
                    < export.IndexOf("if (!_autosaveArmed)", StringComparison.Ordinal));
        Assert.Contains("boards.scene.write.refused", raw, StringComparison.Ordinal);
        Assert.Equal(3, Count(code, "FinishClosingFlush();"));      // 拒掉也是答案：⛔ 让关窗那一拍白等满预算
    }

    /// <summary>
    /// 崩那一拍留下一串跑得过的痕迹（P-222 的现场尺）。
    /// <para>片25 只证到"后一手真发出去了＋新进程真在＋屏幕真白"，而<b>为什么</b>没量到——三个候选因由
    /// 在那些读数上同形。这一格钉的是三处日志都在、且导航那一拍的订阅走"先摘再挂"（重建那一趟用的是新控件，
    /// 同一颗控件被重开再进来时⛔ 要两颗订阅＝一份日志打两遍，那条读数就不能当数了）。</para>
    /// </summary>
    [Fact]
    public void TheCrashRecoveryLeavesARunningTrail()
    {
        var cs = SourceGate.Code(ReadRepo(WindowCs));
        Assert.Contains("NavigationCompleted -= OnNavigationCompleted", cs, StringComparison.Ordinal);
        Assert.Contains("NavigationCompleted += OnNavigationCompleted", cs, StringComparison.Ordinal);
        Assert.Contains("boards.nav.done", cs, StringComparison.Ordinal);
        Assert.Contains("boards.bridge.ready", cs, StringComparison.Ordinal);
        Assert.True(Count(cs, "src=") >= 2, "崩那一拍与导航那一拍都得交出那颗 Source");
        Assert.Equal(1, Count(cs, "private void OnNavigationCompleted("));
    }

    /// <summary>
    /// 那一页只许有<b>一棵</b> React 根（片26＝P-222 的真因）。
    /// <para>theme.set／lang.set／feature.flags 原先各走一遍 <c>L.createRoot(box).render(…)</c>——同一颗 DOM 容器上
    /// 再挂一棵根＝React 把前一棵树整棵拆掉，而拆掉的正是宿主刚递进画布的那块板。现场的脸（06:22 那一跑）＝
    /// <c>board.loaded</c> 那一刻画布上确实有那颗可写元素（canvas=1 是真数），1.5 s 后交回盘上的却是 0 元素／241 字节。</para>
    /// <para>这颗格钉的是形状：造根那一句全档只许一颗、必须挂在"没有才造"那条件后面、且⛔ 许再出现
    /// "造完当场 render"那颗旧写法（那颗就是拆树的那一手）。render 那颗出口也只许一处＝第二次改要挂哪棵根，
    /// 只能问同一个主人。</para>
    /// </summary>
    [Fact]
    public void ThePageMountsExactlyOneReactRoot()
    {
        var js = ReadRepo(BridgeJs);
        Assert.Equal(1, Count(js, "L.createRoot("));                                  // 唯一造根点
        Assert.Contains("if (!root) root = L.createRoot(box);", js, StringComparison.Ordinal);
        Assert.Equal(0, Count(js, "L.createRoot(box).render"));
        Assert.Equal(1, Count(js, ".render(L.React.createElement(L.Excalidraw"));
        Assert.Contains("rerenderWith({});", js, StringComparison.Ordinal);             // 出生那一挂走同一个主人
    }

    // ────────── 片27：那道闸门不再只说话给日志听（P-224）＋恢复期⛔ 再静默 ──────────

    /// <summary>
    /// 载档被拒那一拍：先<b>自己再递一次</b>（一句自愈），额度用尽了才开口，而那句话⛔ 只住在日志里。
    /// <para>为什么先重试再开口：片26 现场那三跑里"再递一次就好"是一次性时序故障的形状，一拒就喊会把没坏的板报成坏了；
    /// 而⛔ 一直递下去是另一头的坏——同一块空画布反复刷给用户看。额度＝1 由 Core 的
    /// <see cref="BoardPolicy.ShouldRetryArm"/> 说了算，UI 档里连那颗常数都⛔ 许再问一次（第二主人）。</para>
    /// <para>开口那一支的三颗形状各钉一处：停笔那一路拒到底／宿主自己挡下的那次场景写／三圈用尽那档。
    /// 面板上那颗"另存一份到别处"只在<b>手上有字节</b>时才亮——给用户一颗按了没用的按钮，是本仓定性过的坏。</para>
    /// </summary>
    [Fact]
    public void ARefusedArmIsRetriedOnceAndThenSpeaksOnTheBoard()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        var inbound = SourceGate.MethodBody(raw, "void OnWebMessageReceived");
        // 那颗额度⛔ 搬进这一档：判据的主人住 Core，UI 只问"还要不要再递"
        Assert.Equal(0, Count(code, "ArmRetryBudget"));
        Assert.Equal(1, BoardPolicy.ArmRetryBudget);                                   // 正件：主人那里它还在
        Assert.Contains("BoardPolicy.ShouldRetryArm(_armTries) && _seenScene is { Length: > 0 }",
            inbound, StringComparison.Ordinal);
        // 再递一次走的是与首开<b>同一颗</b>形状（两份开场代码＝第二个主人），而这一趟的计数当场自增、开过闸就归零
        Assert.Contains("PostBoardLoad();", inbound, StringComparison.Ordinal);
        Assert.Equal(3, Count(code, "PostBoardLoad("));                                // 一颗定义＋首开与重试两颗调用
        Assert.Equal(1, Count(code, "_armTries++"));
        Assert.Contains("_armTries = 0;", SourceGate.MethodBody(raw, "void ArmIdleBeat"), StringComparison.Ordinal);
        // 拒到底才上屏，且三处出口共用同一颗面板
        Assert.Contains("ShowArmStuckNotice();", inbound, StringComparison.Ordinal);
        Assert.Equal(3, Count(code, "ShowArmStuckNotice();"));
        Assert.Contains("ShowArmStuckNotice();", SourceGate.MethodBody(raw, "void HandleExportBytes"), StringComparison.Ordinal);
        var stuck = SourceGate.MethodBody(raw, "void ShowArmStuckNotice");
        Assert.Contains("offerCopy: _seenScene is { Length: > 0 }", stuck, StringComparison.Ordinal);
        Assert.Contains("titled: true", stuck, StringComparison.Ordinal);
    }

    /// <summary>
    /// 那颗"另存一份到别处"按下去之后的三档<b>各走各的路</b>（片28 那腿的读数把这一刀逼出来的：
    /// 用 <c>WM_CLOSE</c> 真把那颗框关了之后，盘上多出一份 858 字节的副本、屏幕上念出"已另存到 …"，而用户点的是【取消】）。
    /// <para>写不写这一笔交 Core 那颗判据（<c>BoardPolicy.ClassifyCopyPick</c>／<c>ShouldWriteCopy</c>），UI 只认它的三档；
    /// 那句"已另存到 …"从此只跟着<b>真写了</b>的那一次说。</para>
    /// </summary>
    [Fact]
    public void ADeclinedCopyWritesNothingWhileAnUnavailablePickerStillFallsBack()
    {
        var raw = ReadRepo(WindowCs);
        var pick = SourceGate.MethodBody(raw, "Task<string?> PickSavePathAsync");
        var copy = SourceGate.MethodBody(raw, "Task CopySeenSceneAsideAsync");
        Assert.Contains("BoardPolicy.ClassifyCopyPick(threw, picked)", pick, StringComparison.Ordinal);
        Assert.Contains("BoardPolicy.ShouldWriteCopy(pick)", pick, StringComparison.Ordinal);
        // 旧形状⛔ 许回来：null 一路落到兜底那两行＝取消也写一份（那颗 if 正是本批量到的那颗坏）
        Assert.DoesNotContain("if (file is not null) return file.Path;", pick, StringComparison.Ordinal);
        Assert.Contains("boards.copy.declined", pick, StringComparison.Ordinal);
        // 兜底那颗建目录只许活在一支里，且全档一颗（两处主人＝迟早只补一颗）
        Assert.Equal(1, Count(pick, "CreateDirectory(ExportsDirectory)"));
        Assert.Contains("if (target is null) return;", copy, StringComparison.Ordinal);
        Assert.Contains("boards.scene.copied", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// §21.5 剩下那半：崩那一拍与关窗超时那一条⛔ 再静默。
    /// <para>决定动手的两档（重载／重建控件）当场贴"这块板正在恢复"，而那一句只跟着<b>真递出去</b>的那一下；
    /// 三圈用尽那一档反过来——它把写闸当场关掉并说"这块板没能回到画布上"＋给那颗按钮（往后确实再也好不了）。</para>
    /// <para><c>_stage.Children</c> 那一对钉的是<b>窗的内容只许有一个主人</b>：从前重建那一趟写 <c>Content = _web</c>，
    /// 重指一次就把那块面板连话一起摘走——而那一刻正是要它说话的时候。
    /// 插回 <c>index 0</c> 是让它继续当画布那一位（面板永远压在画布之上）。</para>
    /// <para>关窗超时那一句走右下角那张卡：窗在那一拍开头就 <c>Hide()</c> 了，画板里的面板⛔ 来得及给用户看见。
    /// 卡自己也没贴上屏幕时（非 UI 线程／建窗失败）日志留一行可查的 <c>notice.notShown</c>——⛔ 让"提醒已发出"替一条没人看见的坏话打掩护。</para>
    /// </summary>
    [Fact]
    public void TheRecoveryAndCloseBeatsNeverStaySilent()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        var fail = SourceGate.MethodBody(raw, "void OnProcessFailed");
        Assert.Equal(2, Count(code, "ShowRecoveringNotice();"));                        // 重载＋重建两档
        Assert.Contains("if (view is not null) { view.Reload(); ShowRecoveringNotice(); }", fail, StringComparison.Ordinal);
        Assert.Contains("ShowArmStuckNotice();", fail, StringComparison.Ordinal);        // 三圈用尽＝说得出"往后也不再落盘"
        Assert.Contains("_autosaveArmed = false;", fail, StringComparison.Ordinal);
        var rec = SourceGate.MethodBody(raw, "void RecoverByRecreatingWeb");
        Assert.Contains("_stage.Children.Remove(old);", rec, StringComparison.Ordinal);
        Assert.Contains("_stage.Children.Insert(0, _web);", rec, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "Content = _web"));                                 // ⛔ 第二颗主人：重指 Content＝把面板连话一起摘走
        Assert.Equal(1, Count(code, "Content = _stage"));                               // 正件：指一次，就在构造里
        var flush = SourceGate.MethodBody(raw, "Task FlushThenCloseAsync");
        Assert.Contains("NoticeCard.Show(", flush, StringComparison.Ordinal);
        Assert.Contains("boards.closeFlushTitle", flush, StringComparison.Ordinal);
        Assert.Contains("boards.closeFlushBody", flush, StringComparison.Ordinal);
        Assert.Contains("boards.close.flush.notice.notShown", flush, StringComparison.Ordinal);
        // 那颗静态事件成对（窗关了不摘＝这一窗的元素永远活着）
        Assert.Equal(1, Count(code, "Locale.Changed += OnLocaleChanged"));
        Assert.Contains("Locale.Changed -= OnLocaleChanged",
            SourceGate.MethodBody(raw, "void OnClosed"), StringComparison.Ordinal);
    }

    /// <summary>
    /// 那块面板说的话全部住在表里，并且<b>换语言那一刻跟着换</b>。
    /// <para>这一格里最要紧的是那颗按钮：它的字是<b>构造时</b>一次性冻进去的 <c>Content</c>，
    /// 重贴只贴两行正文就会留下"句子换了语言、按钮还留着旧语言"那种半中半英的脸（P-175 那一族的第三种形状）。</para>
    /// <para>禁项读的是<b>抹掉注释后的方法体</b>：doc-comment 里那句"从前这里写的是…"是给人的理由，⛔ 当第二个出口数
    /// （规则 #131 同族）。开闸那一刻 <c>HideNotice();</c> 也钉一次——那句坏话还留着，就是一句当场可见的假话。</para>
    /// </summary>
    [Fact]
    public void ThePanelSpeaksOnlyWordsFromTheTableAndRedrawsThemOnLocaleChange()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        Assert.Contains("private void OnLocaleChanged() => PasteNotice();", code, StringComparison.Ordinal);
        Assert.Equal(2, Count(code, "PasteNotice();"));             // ShowNotice 那一拍＋换语言那一拍（两颗都归同一位主人）
        Assert.Equal(1, Count(code, "HideNotice();"));                                  // 开闸＝那句坏话已经不是事实
        // 唯一的取词出口：两颗工厂各一处，调用点⛔ 重抄全限定名
        Assert.Equal(1, Count(code, "private static string Word(string key) => StarMark.Abstractions.I18n.Locale.T(key);"));
        Assert.Equal(1, Count(code, "Locale.T("));
        Assert.Equal(1, Count(code, "Locale.Fmt("));
        // 那颗按钮上的话也在重贴之列
        Assert.Contains("button.Content = Word(\"boards.saveCopyAction\");",
            SourceGate.MethodBody(code, "void PasteNotice"), StringComparison.Ordinal);
        // 面板那一路⛔ 一句硬编码的字（字只许住在表里，屏幕那一路⛔ 拼成品句）
        foreach (var sig in new[]
        {
            "Grid BuildStage", "void ShowNotice", "void HideNotice", "void PasteNotice",
            "void ShowRecoveringNotice", "void ShowArmStuckNotice", "void OnLocaleChanged",
        })
        {
            var leftovers = SourceGate.Cjk.Matches(SourceGate.MethodBody(code, sig))
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
            Assert.True(leftovers.Count == 0, "面板那一路出现了硬编码的字：" + sig + " → " + string.Join("／", leftovers));
        }
    }
}
