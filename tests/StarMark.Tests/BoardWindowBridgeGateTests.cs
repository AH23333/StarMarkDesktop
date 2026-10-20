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
    private const string RegistryCs = "src/StarMark.UI/Boards/BoardsWindows.cs";
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
    /// 在主窗档里只许出现一次（＝一颗调用，多扇那一档住在 <c>foreach</c> 里），定义与另两颗调用都住在托盘那一档。</para>
    /// <para><b>片32 加的那一档</b>＝逗号列表一次开多扇（<c>new,new</c>）：退出那一拍走<b>并发</b>等，
    /// 而两扇同时开着才有"总时长由最坏那一扇决定"这颗读数。取值仍是闭集——<c>last</c> 只许单独写
    /// （两扇续同一颗档＝两个主人抢一份盘），认不出的写法<b>一扇也⛔ 开</b>并留一行日志（⛔ 静默半开）。</para>
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
            "if (wants.Length > 0 && wants.All(w => w == \"new\" || (w == \"last\" && wants.Length == 1)))",
            main, StringComparison.Ordinal);
        Assert.Contains(
            "foreach (var want in wants) _ = OpenBoardsWindowAsync(resumeLast: want == \"last\");",
            main, StringComparison.Ordinal);
        Assert.Contains("StarLog.Warn(\"boards.dev.start.unknown value=\" + startBoard)", main, StringComparison.Ordinal);
        Assert.DoesNotContain("startBoard is \"new\" or \"last\"", main, StringComparison.Ordinal);   // 那颗单值旧写法已被取代
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
        var flush = SourceGate.MethodBody(raw, "Task<bool> FlushSnapshotAsync(string origin");
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
    /// P-220（片30）：<b>退出那一拍</b>也要问一次画板窗——收尾最后那句 <c>Environment.Exit(0)</c>
    /// ⛔ 经过任何一扇窗的 <c>AppWindow.Closing</c>，所以"用户关窗"那条路上的有界等待在这条路上一次也跑不到。
    /// <para>真机两臂<b>改前</b>（同一份码、同一起点、新板）：<c>bin/WI90/readings_30_board_pre.txt</c> 盘上多出
    /// <c>…-be4b.excalidraw</c>（241 字节），<c>bin/WI90/readings_30_main_pre.txt</c> 那颗板<b>压根没落盘</b>
    /// （只有 <c>bridge.ready</c>／<c>arm.post</c>，收尾之后进程 1 秒内退完）。<b>改后</b>同一腿重跑（<c>readings_30_main_post.txt</c>）读到 <c>boards.close.flush.exit … landed=True</c>、盘上多那颗新档。</para>
    /// <para>⚠ 改后那一发落到盘的是<b>空板</b>（<c>hasScene=False</c>、元素 0 颗）⇒它只证"这一拍跑了"，⛔ 证"最后几笔没丢"。
    /// 那一格由<b>片31</b> 补量（✅P-229）：接住那颗 2 元素种子档＋当场拉一笔＋抬笔 0.08 s 关主窗 ⇒
    /// <c>858/2 颗 → 2708/3 颗</c>，新进那颗是 <c>freedraw</c> 且包围盒对上这一笔的折算（<c>readings_31_main_115116.txt</c>）。
    /// 形状证人⛔ 能钉这一格（它要的是真鼠标＋真进程），所以这一格里最要紧的一句是：<b>那颗 <c>landed=True</c> 单独不算验过</b>。</para>
    /// <para>形状＝名册 <c>BoardsWindows</c> 由收尾链<b>第一步</b>请一次（排在关组件之前＝那颗 5 秒硬退出看门狗
    /// 只剩后面那一段要抢），两条来路<b>共用同一颗</b>有界等待（两份等待代码＝第二个主人），
    /// 名册<b>并发</b>等（⛔ 逐扇串：N 扇就把那颗预算乘 N）。</para>
    /// </summary>
    [Fact]
    public void TheExitPathFlushesEveryOpenBoardBeforeTheProcessEnds()
    {
        var win = ReadRepo(WindowCs);
        var code = SourceGate.Code(win);
        var exit = SourceGate.MethodBody(ReadRepo(MainCs), "private async void ExitApp()");
        Assert.Contains("await BoardsWindows.FlushAllForExitAsync();", exit, StringComparison.Ordinal);
        Assert.True(exit.IndexOf("FlushAllForExitAsync", StringComparison.Ordinal)
                    < exit.IndexOf("ShutdownAllAsync", StringComparison.Ordinal),
            "画板那一拍排到关组件之后＝它可能被那颗 5 秒看门狗砍掉");
        // 登记的手臂成对：名册上留一颗已关的窗＝退出时会去问一颗没桥的窗
        Assert.Equal(1, Count(code, "BoardsWindows.Add(this);"));
        Assert.Equal(1, Count(code, "BoardsWindows.Remove(this);"));
        Assert.Contains("BoardsWindows.Add(this);", SourceGate.MethodBody(win, "public BoardWindow()"), StringComparison.Ordinal);
        Assert.Contains("BoardsWindows.Remove(this);", SourceGate.MethodBody(win, "void OnClosed"), StringComparison.Ordinal);
        // 两条来路共用一颗：那份快照只许问一次、那颗预算只许等一次，差别只在 notice 那一面
        Assert.Equal(1, Count(code, "RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow);"));
        Assert.Equal(1, Count(code, "Task.Delay(BoardPolicy.CloseFlushBudgetMs)"));
        Assert.Contains("FlushSnapshotAsync(\"exit\", notice: false)", code, StringComparison.Ordinal);
        Assert.Contains("FlushSnapshotAsync(\"window\", notice: true)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NoticeCard.Show(", SourceGate.MethodBody(win, "Task<bool> FlushForExitAsync"), StringComparison.Ordinal);
        // 名册那一头：⛔ 逐扇串（一句 foreach 一次 await＝把那颗预算乘上扇数）
        var reg = SourceGate.Code(ReadRepo(RegistryCs));
        Assert.Equal(0, Count(reg, "foreach"));
        Assert.Contains("await Task.WhenAll(waits)", reg, StringComparison.Ordinal);
        Assert.Contains("lock (_open) _open.Clear();", reg, StringComparison.Ordinal);
    }

    /// <summary>
    /// 片32：<b>每一扇各自报一颗"这一拍我等了多久"</b>（<c>tookMs=</c>）。
    /// <para>为什么这一格值得单独钉：名册那一趟走的是<b>并发</b>等，"总时长＝最坏那一扇而⛔ 各扇之和"
    /// 是这句设计的全部理由。可只有那颗<b>总</b>时长在日志里时，这句话⛔ 有读数能撑住——两扇各 10 ms 与
    /// 两扇各 2 s 撞在同一个总数上（那是坑表 #696 那一族：由总量倒推）。所以耗时必须<b>按扇</b>报。</para>
    /// <para>那颗 <c>tookMs</c> 与超时那句里的 <c>ms=</c> 各说一件事（前者＝这一发<b>用掉的</b>，
    /// 后者＝那颗<b>预算</b>）：两个名字撞在一起＝"用完了预算"与"用了 2 秒"读起来一模一样。</para>
    /// </summary>
    [Fact]
    public void EachBoardReportsItsOwnWaitOnTheCloseBeat()
    {
        var raw = ReadRepo(WindowCs);
        var flush = SourceGate.MethodBody(raw, "Task<bool> FlushSnapshotAsync");
        Assert.Contains("var started = System.Diagnostics.Stopwatch.GetTimestamp();", flush, StringComparison.Ordinal);
        Assert.Contains("\" tookMs=\" + ElapsedMs(started)", flush, StringComparison.Ordinal);
        Assert.Equal(1, Count(flush, "tookMs"));                                   // 一句里报一次，两条来路共用那一颗
        Assert.Contains("boards.close.flush.timeout ms=", flush, StringComparison.Ordinal);   // 那颗预算仍只由超时那句报
        Assert.Contains("(System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000 / System.Diagnostics.Stopwatch.Frequency",
            SourceGate.Code(raw), StringComparison.Ordinal);                       // 刻度换算住在⛔ 日志拼接里
    }

    /// <summary>
    /// 片32 那一格<b>真正修掉</b>的东西：抬笔后马上关窗时，那一拍问早了——页面此刻交回的是<b>还没有那一笔</b>的场景。
    /// <para>读数（两扇同开、各一笔再各补一笔，正本 <c>bin/WI90/readings_32_main_*</c>）：
    /// 抬笔后 0～20 ms 关窗＝<b>最后画那一扇的那一笔进不了盘</b>，而两颗 <c>landed=True</c> 与"落地 2 扇"照样报好话；
    /// 40／80／150／250／300 ms 各一跑＝两颗档都对上自己那两笔。门槛落在 20～40 ms 之间，
    /// 所以那一拍<b>先让 150 ms 再开口</b>（约四倍余量，且⛔ 动那颗 2 s 预算的语义：预算量的还是"问出去之后等多久"）。</para>
    /// <para>这一格钉的是<b>顺序</b>而⛔ 只是"有没有这一段延时"：把 Delay 挪到 <c>RequestSceneSnapshot</c> 之后
    /// 等于什么都没改（问已经问出去了），而两颗行都照样在档里。所以用 index 比先后。</para>
    /// <para>那颗 150 是<b>量出来的</b>，所以也钉死：改这颗数必须带着新读数一起来（与 <c>CloseFlushBudgetMs</c> 同理，
    /// 并要维持"让的那段⛔ 超过等的那段"——否则那一拍的预算被自己前面那段吃掉）。</para>
    /// </summary>
    [Fact]
    public void TheCloseBeatLetsThePenReachThePageBeforeItAsks()
    {
        Assert.True(BoardPolicy.CloseFlushGraceMs < BoardPolicy.CloseFlushBudgetMs,
            "让的那段⛔ 能超过等的那段：预算就没意义了");
        Assert.Equal(150, BoardPolicy.CloseFlushGraceMs);
        var flush = SourceGate.MethodBody(ReadRepo(WindowCs), "Task<bool> FlushSnapshotAsync");
        var grace = flush.IndexOf("await System.Threading.Tasks.Task.Delay(BoardPolicy.CloseFlushGraceMs);",
            StringComparison.Ordinal);
        var ask = flush.IndexOf("RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow);", StringComparison.Ordinal);
        Assert.True(grace >= 0 && ask >= 0 && grace < ask,
            "那一段让必须发生在开口<b>之前</b>（挪到问之后＝白等，那一笔照样进不了这份快照）");
        Assert.Equal(1, Count(flush, "CloseFlushGraceMs"));                          // 一颗数只许有一个主人
    }

    /// <summary>
    /// 片32：那一行要<b>说得出它是不是只看到旧账</b>（<c>same=</c>）。
    /// <para>起因＝本批那颗缺陷正是被 <c>landed=True</c> 掩盖的：页面交回一份与盘上逐字相同的旧场景时，
    /// 宿主走 <c>DiskAlreadyHolds</c> 那一档，"落地"与"什么都没新落"读起来一模一样（片31 记过这一族，那时无读数）。
    /// 有了这一列，真机跑次里 <c>landed=True same=True</c> 与 <c>same=False</c> 分得开——前者才是"那一拍交回旧账"的脸。</para>
    /// <para>那颗赋值只许在<b>这一问还开着</b>时改（<c>_closingLanded is not null</c>）：停笔／失焦那一拍也常撞到同件，
    /// 让它们一起拨＝把一次无关的等价写说成那一拍读了旧账。开局复位一次，⛔ 让上一问的脸活到下一问。</para>
    /// </summary>
    [Fact]
    public void TheCloseBeatSaysWhetherThePageGaveBackSomethingNew()
    {
        var raw = ReadRepo(WindowCs);
        var flush = SourceGate.MethodBody(raw, "Task<bool> FlushSnapshotAsync");
        Assert.Contains("\" same=\" + _closingSameAsDisk", flush, StringComparison.Ordinal);
        Assert.Equal(1, Count(flush, " same="));
        Assert.Contains("_closingSameAsDisk = false;", flush, StringComparison.Ordinal);   // 一问一复位
        var got = SourceGate.MethodBody(raw, "void HandleExportBytes");
        Assert.Contains("_closingSameAsDisk = _closingLanded is not null;", got, StringComparison.Ordinal);
        Assert.Equal(1, Count(got, "_closingSameAsDisk"));                            // 只有那一问在等时才配改它
    }
    /// <summary>片33 坏臂量到的那一格钉成形状证人（P-227 的落地腿）：被写闸拒掉的那一份<b>只</b>喂那颗「另存一份到别处」。
    /// <para>四格一起钉：① 那一颗赋值只在拒的那一支里；② 开闸那一刻清掉它（手里那份已是最新，那颗可疑候选⛔ 再配在场）；
    /// ③ 选哪一份由 Core 那颗判据定，而屏幕上那句跟着分岔走（退回手里那份时必须说出「之后画的笔迹没包含」＝措辞与实现对齐）；
    /// ④ <c>_seenScene</c> 的主人仍只有两处（开场递档＋开了闸之后交回）——恢复那条路⛔ 认这一颗，那是 P-223 定死过的坏法。</para></summary>
    [Fact]
    public void TheRefusedSceneFeedsOnlyTheRescueCopyAndTheSentenceSaysWhichOneLanded()
    {
        var raw = ReadRepo(WindowCs);
        var refused = SourceGate.MethodBody(raw, "void HandleExportBytes");
        Assert.Contains("_refusedScene = bytes;", refused, StringComparison.Ordinal);
        Assert.Contains("private byte[]? _refusedScene;", SourceGate.Code(raw), StringComparison.Ordinal);
        Assert.Contains("_refusedScene = null;", SourceGate.MethodBody(raw, "void ArmIdleBeat"), StringComparison.Ordinal);
        var copy = SourceGate.MethodBody(raw, "Task CopySeenSceneAsideAsync");
        Assert.Contains("BoardPolicy.ScreenSceneIsWorthSaving(loaded, _refusedScene)", copy, StringComparison.Ordinal);
        Assert.Contains("? \"boards.copySavedStaleBody\" : \"boards.copySavedBody\"", copy, StringComparison.Ordinal);
        Assert.Contains("scene=\" + (ReferenceEquals(bytes, loaded) ? \"loaded\" : \"screen\")", copy, StringComparison.Ordinal);
        Assert.Equal(1, Count(copy, "File.WriteAllBytes("));                    // 只写用户挑的那颗新档，⛔ 动主档
        Assert.Equal(2, Count(SourceGate.Code(raw), "_seenScene = "));          // 开场＋开闸之后，⛔ 第三个写主
    }


    /// <summary>片35：用户关窗那一拍撞见<b>写闸拒</b>时，那颗窗必须亮回屏幕上（⛔ 带着那句没人看得见的话退场）。
    /// <para>坏的形状是"<c>landed=True</c> 有两种真法"里最坏那一种：拒掉也算答案（片27 为了⛔ 让用户白等两秒定的），
    /// 于是那一拍醒来就 <c>Close()</c>——而那两行字与那颗「另存一份到别处」贴在<b>一颗已经 Hide 的窗</b>上
    /// （P-224／P-227 那一族的第三种脸：话是真的，宿主⛔ 在场）。</para>
    /// <para>四格一起钉：① 那颗标志<b>只在拒的那一支</b>里、而且<b>只有那一问在等时</b>才配改（一问一复位，与 <c>same=</c> 同口径）；
    /// ② 它排在 <c>FinishClosingFlush()</c> <b>之前</b>——那一拍随时会醒，醒了就得读到这一拍的账；
    /// ③ 每一拍起手清一次（⛔ 让上一拍的"被拒"替这一拍说话）；④ 关窗那一手的分岔：被拒⇒<c>Show</c>＋<c>Activate</c>＋一句日志就 <c>return</c>，
    /// 其余照旧 <c>Close()</c> 且<b>只有一颗</b>（那颗超时那一支⛔ 改道，它已经走右下角那张卡）。</para></summary>
    [Fact]
    public void TheCloseBeatBringsTheWindowBackWhenWhatCameWasARefusal()
    {
        var raw = ReadRepo(WindowCs);
        // 那颗计数读的是<b>抹掉注释后的方法体</b>：我给那一支写的 doc-comment 里提了那颗字段的名，
        // 拿原文数就会把"给人看的理由"数成第二个出口（规则 #131 同族，与那颗面板格同一条口径）。
        var got = SourceGate.MethodBody(SourceGate.Code(raw), "void HandleExportBytes");
        Assert.Contains("_closingRefused = _closingLanded is not null;", got, StringComparison.Ordinal);
        Assert.Equal(1, Count(got, "_closingRefused"));                                  // 只有那一问在等时才配改它
        Assert.True(got.IndexOf("_closingRefused", StringComparison.Ordinal)
                    < got.IndexOf("FinishClosingFlush();", StringComparison.Ordinal),     // 拒的那一支排在最前＝第一颗结掉等待
                    "那颗标志排在结掉等待之后＝那一拍先醒了，读到的还是上一拍的旧账");
        Assert.Contains("_closingRefused = false;",
            SourceGate.MethodBody(SourceGate.Code(raw), "Task<bool> FlushSnapshotAsync"), StringComparison.Ordinal);   // 一问一复位
        var close = SourceGate.MethodBody(SourceGate.Code(raw), "Task FlushThenCloseAsync");
        Assert.Contains("if (_closingRefused)", close, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Show();", close, StringComparison.Ordinal);
        Assert.Contains("Activate();", close, StringComparison.Ordinal);                  // Show 只管回可见，那颗按钮要点得中
        Assert.Contains("boards.close.revive on=refused id=", close, StringComparison.Ordinal);
        Assert.True(close.IndexOf("if (_closingRefused", StringComparison.Ordinal) < close.IndexOf("Close();", StringComparison.Ordinal),
            "分岔⛔ 排在关之前＝那句坏话又挂在一颗藏着的窗上");
        Assert.Contains("return;", close, StringComparison.Ordinal);                     // 那颗分岔得<b>走掉</b>：只亮不回＝照样关
        Assert.True(close.IndexOf("return;", StringComparison.Ordinal) < close.IndexOf("Close();", StringComparison.Ordinal),
            "那颗 return 排在关之后＝亮回来又立刻关掉＝这一格⛔ 有牙");
        Assert.Equal(1, Count(close, "Close();"));                                        // ⛔ 多一颗出口，也⛔ 把超时那一支一起改了
        Assert.Equal(4, Count(SourceGate.Code(raw), "_closingRefused"));                  // 声明／那一支／复位／分岔，⛔ 第五颗主人
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
        Assert.Equal(5, Count(code, "PasteNotice();"));             // ShowNotice 那一拍＋换语言那一拍＋被拒亮回那一拍补两颗新按钮＋片49 那颗「再试一次」跑前跑后各一次（五处都只⛔ 调那颗主人，⛔ 各写一遍可见性）
        Assert.Equal(4, Count(code, "HideNotice();"));              // 收起面板只此一颗主人，四处正当调用：开闸成功（坏话不再是事实）＋片40「继续编辑」撤销这一拍＋片49 那两颗（重试终于读到了／他决定先开一块新板）
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
            "Task OpenCopyFolderAsync", "bool CanOpenCopyFolder",
            "void StayOnBoard", "void LeaveAnyway",
            // 片49 那三颗：坏话的上屏出口与它的两颗按钮都在这条线上（⛔ 在这里拼一句中文）
            "Task OpenSceneAsync", "Task RetryBlockedSceneAsync", "void StartFreshBoard",
        })
        {
            var leftovers = SourceGate.Cjk.Matches(SourceGate.MethodBody(code, sig))
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
            Assert.True(leftovers.Count == 0, "面板那一路出现了硬编码的字：" + sig + " → " + string.Join("／", leftovers));
        }
    }

    /// <summary>片39＝P-219 的上屏出口：念出路径之外再给一颗能点的「打开所在文件夹」，五格一起钉。
    /// <para>① 那颗按钮的<b>资格</b>＝"屏幕上那一句认定盘上放着一颗副本"，而这一问只许有一颗主人
    /// （一处声明＋一处守卫＋一处可见性＝三处引用，⛔ 第四处再 switch 一遍键名）；
    /// ② "打开失败"那一句<b>也算</b>：坏的是开目录那一手，副本确实写成了，那一刻用户要的正是再点一次
    /// （摘掉按钮＝把作业推回给他＝P-54 定过的坏法）；而"正在恢复"／"已停止保存"／"没能写到那个位置"三句
    /// ⛔ 认定副本存在，挂着那颗按钮＝让他点出一颗⛔ 存在的档；
    /// ③ 可见性与话都由 <c>PasteNotice</c> 算（换语言那一拍只走它、⛔ 重走 ShowNotice——片34 漏掉的就是那一拍）；
    /// ④ 这一屏上现在有两颗可点的，片28 量到的"白字画在灰底上"⛔ 许只修一颗；
    /// ⑤ 动作走本机路径那一族的唯一出口，⛔ 在画板这一档再拼一遍 explorer 命令行。</para></summary>
    [Fact]
    public void TheFolderExitAppearsOnlyWhileASentenceClaimsACopyLanded()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        // ① 那几种"按了那颗按钮之后的结果"只许有一份名单，两颗问法都从它派生
        var outcomes = SourceGate.Between(code, "string[] CopyOutcomeKeys", "];");
        Assert.Equal(4, Count(outcomes, "\"boards."));   // 那颗名单⛔ 只许装这四种下落（多一颗＝把旧话当成新事实）
        foreach (var landed in new[]
        {
            "boards.copySavedBody", "boards.copySavedStaleBody", "boards.openFolderFailedBody", "boards.copyFailedBody",
        })
            Assert.Contains(landed, outcomes, StringComparison.Ordinal);
        // ② 那颗按钮的资格＝"名单之内"且⛔ 是"没写成"那一句，而它⛔ 自己再列一遍键名（两处各列＝迟早只补一处）
        var can = SourceGate.MethodBody(code, "bool CanOpenCopyFolder");
        Assert.Equal(3, Count(code, "CanOpenCopyFolder"));
        Assert.Equal(3, Count(code, "NoticeSaysCopyOutcome"));
        Assert.Contains("_copyPath is not null", can, StringComparison.Ordinal);
        Assert.Contains("NoticeSaysCopyOutcome", can, StringComparison.Ordinal);
        Assert.Contains("\"boards.copyFailedBody\"", can, StringComparison.Ordinal);
        Assert.Contains("&& _noticeKey != \"boards.copyFailedBody\";", can, StringComparison.Ordinal);
        // ③ 同一件旧话⛔ 许把新事实顶掉（片39 真机量到的那一发：拒话重贴盖掉「已另存到 …」＋那颗新按钮跟着退场）
        var got = SourceGate.MethodBody(raw, "void HandleExportBytes");
        Assert.Contains("if (!NoticeSaysCopyOutcome) ShowArmStuckNotice();", got, StringComparison.Ordinal);
        // ④ 那颗按钮⛔ 在场＝用户可以再看一次；摘掉它＝把作业推回给他（P-54 定过的坏法）
        Assert.Equal(1, Count(outcomes, "boards.openFolderFailedBody"));

        Assert.Equal(1, Count(code, "folder.Visibility = CanOpenCopyFolder ?"));
        Assert.Contains("folder.Content = Word(\"boards.openFolderAction\");",
            SourceGate.MethodBody(code, "void PasteNotice"), StringComparison.Ordinal);
        Assert.Contains("Phrase(\"boards.openFolderFailedBody\", _copyPath ?? string.Empty);",
            SourceGate.MethodBody(code, "void PasteNotice"), StringComparison.Ordinal);

        var stage = SourceGate.MethodBody(code, "private Grid BuildStage");
        // （那颗按钮⛔ 跟着主题走这一格住在 BoardWordGateTests 那颗色尺里：一把尺只量一次，⛔ 两处各数一遍。）
        Assert.Contains("panel.Children.Add(_noticeFolderButton);", stage, StringComparison.Ordinal);

        var open = SourceGate.MethodBody(raw, "Task OpenCopyFolderAsync");
        // 动作走本机路径那一族的唯一出口，⛔ 在画板这一档再拼一遍 explorer 命令行；
        // ⚠ 而递进去的必须是<b>一颗 URI</b>：那颗还原判据只认 file:// 前缀，裸磁盘路径两格候选都是空串⇒
        // 退化成"图已不在本机"那句坏话＋桌面多开一颗 Explorer <b>主文件夹</b>窗（2026-10-19 真机读数逐字为证：
        // <c>boards.folder.opened … result=failed</c>・<c>title='主文件夹 - 文件资源管理器'</c>·正本 readings_39_183824）。
        Assert.Contains("Helpers.LauncherEx.TryOpenAsync(StarMark.Abstractions.LocalFileIdentity.UriForPath(folder))",
            open, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOpenAsync(folder)", open, StringComparison.Ordinal);
        Assert.Contains("ShowNotice(\"boards.openFolderFailedBody\", titled: true, offerCopy: true);",
            open, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "explorer.exe"));
        // 那一发要有记录，而<b>原因</b>也得进得去（P-56 口径：只报"坏了"下一跑说不出是谁坏的）
        Assert.Contains("boards.folder.opened", open, StringComparison.Ordinal);
        Assert.Contains("why=", open, StringComparison.Ordinal);
    }

    /// <summary>片40＝被关过一次又被亮回来的那一格，走／留都得就地有颗按钮（P-54：⛔ 把"再点一次×就退"这种隐藏出口推回给用户）。
    /// <para>① 两颗按钮（「继续编辑」「仍然离开」）各由 <c>BuildStage</c> add 一遍；
    /// ② 它们的可见性<b>只由一颗 <c>_closeRevived</c> 一处算</b>（两颗各一条 <c>? _closeRevived</c>，⛔ 别处再判一遍＝第二个主人）；
    /// ③ 全档只有一处把 <c>_closeRevived</c> 置真＝<c>FlushThenCloseAsync</c> 那颗被拒分支（停笔那一拍撞见的拒⛔ 算"用户按过×"，
    /// 那一格只配「另存」，挂"走／留"＝把一件没发生过的事当成就悬在那儿）；
    /// ④「继续编辑」撤销这一拍：面板收起＋<c>_closeFlushDone=false</c>（下次×重新问一遍，别把这次被拒当"已经拦过"）＋恢复 <c>_tearingDown</c>；
    /// ⑤「仍然离开」当场 <c>Close()</c>（第二拦由 <c>_closeFlushDone</c> 放行），并留一行读数；
    /// ⑥ 开闸成功那一拍把这一问作废（<c>_closeRevived=false</c>），⛔ 让下次换语言把两颗按钮贴回一块已经能存的板。</para></summary>
    [Fact]
    public void TheLeaveAndStayChoicesAppearOnlyWhereACloseWasRefused()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        // ① 两颗都在场（BuildStage 各 add 一遍）
        var stage = SourceGate.MethodBody(code, "private Grid BuildStage");
        Assert.Contains("panel.Children.Add(_noticeStayButton);", stage, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Add(_noticeLeaveButton);", stage, StringComparison.Ordinal);
        // ② 那颗主人＝PasteNotice：两颗各重贴一次字、各按 _closeRevived 算一次可见性
        var paste = SourceGate.MethodBody(code, "void PasteNotice");
        Assert.Contains("stay.Content = Word(\"boards.stayAction\");", paste, StringComparison.Ordinal);
        Assert.Contains("leave.Content = Word(\"boards.leaveAction\");", paste, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "stay.Visibility = _closeRevived ?"));
        Assert.Equal(1, Count(code, "leave.Visibility = _closeRevived ?"));
        // ③ 唯一置真的那一处＝被拒亮回分支；这一拍还须补一次 PasteNotice 才让两颗当场现身
        var revive = SourceGate.MethodBody(code, "Task FlushThenCloseAsync");
        Assert.Contains("_closeRevived = true;", revive, StringComparison.Ordinal);
        Assert.Contains("PasteNotice();", revive, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "_closeRevived = true"));
        // ④「继续编辑」＝撤销这一拍（三样复位）
        var stay = SourceGate.MethodBody(code, "void StayOnBoard");
        Assert.Contains("_closeFlushDone = false;", stay, StringComparison.Ordinal);
        Assert.Contains("_tearingDown = false;", stay, StringComparison.Ordinal);
        Assert.Contains("_closeRevived = false;", stay, StringComparison.Ordinal);
        Assert.Contains("HideNotice();", stay, StringComparison.Ordinal);
        // ⑤「仍然离开」＝当场 Close＋留一行读数（⛔ 自己再拼一份退出链）
        var leave = SourceGate.MethodBody(code, "void LeaveAnyway");
        Assert.Contains("Close();", leave, StringComparison.Ordinal);
        Assert.Contains("boards.close.leave", leave, StringComparison.Ordinal);
        // ⑥ 开闸成功＝这一问作废
        Assert.Contains("_closeRevived = false;",
            SourceGate.MethodBody(code, "void ArmIdleBeat"), StringComparison.Ordinal);
    }
}
