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
    private const string EnvelopeCs = "src/StarMark.UI/Boards/BoardEnvelope.cs";
    private const string RegistryCs = "src/StarMark.UI/Boards/BoardsWindows.cs";
    private const string MainCs = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string TrayCs = "src/StarMark.UI/MainWindow.Tray.cs";

    [Fact]
    public void BridgeJsSpeaksTheWiredSubsetOfTheProtocol()
    {
        var js = ReadRepo(BridgeJs);
        // 页面侧只会喊它真正实现了的那四颗（片68 补上 open.external 的发话人）；其余三颗 ⛔ 假喊（宿主回 not-wired）
        Assert.Equal(1, Count(js, "postIn(\"bridge.ready\""));
        Assert.Equal(1, Count(js, "postIn(\"export.bytes\""));
        Assert.Equal(1, Count(js, "postIn(\"open.external\""));
        Assert.Equal(0, Count(js, "postIn(\"paste.query\""));
        Assert.Equal(0, Count(js, "postIn(\"pin.request\""));
        Assert.Equal(0, Count(js, "postIn(\"background.request\""));
        // 片56：那句 nack 走的是 send（一颗信封形状⛔ 走 postIn 那颗 args 包装），所以它单独数一遍
        Assert.Equal(1, Count(js, "cmd: \"bridge.reply\""));
        // 出向五颗：页面侧逐颗处理（case 逐颗一处）
        foreach (var name in BridgeProtocol.OutboundEvents)
            Assert.Equal(1, Count(js, "case \"" + name + "\""));
    }

    /// <summary>
    /// 缩放那一档在真机上坏过一次：三档产物<b>逐颗同宽 120</b>（正本 <c>bin/WI98/cap64_console_r2.txt</c>），
    /// 而页面确实收到了档位（<c>bin/WI98/cap74_console_r2.txt</c> 里那颗档名念回 <c>rx3</c>）。
    /// 根因由随包 bundle 里那颗 <c>exportToCanvas</c> 的形参表说清：它<b>⛔ 收</b> <c>exportScale</c>，
    /// 缩放只从 <c>getDimensions()</c> 返回值里那颗 <c>scale</c> 来得（没给时恒 1×）。
    /// ⇒ 页面那颗 opts 必须走 <c>getDimensions</c> 这一道门；顶层再放一颗<b>没人读的</b> <c>exportScale</c>
    /// 就是给下一个同坑留位（修好的阶梯 120→240→360，正本 <c>bin/WI98/cap64_console_r3.txt</c>）。
    /// </summary>
    [Fact]
    public void ThePngExportCarriesTheScaleThroughTheOnlyDoorTheEngineReads()
    {
        var js = ReadRepo(BridgeJs);
        Assert.Equal(1, Count(js, "getDimensions: prefs.exportScale ? function (w, h) {"));
        Assert.Equal(1, Count(js, "return { width: w * prefs.exportScale, height: h * prefs.exportScale, scale: prefs.exportScale };"));
        // 那颗档位只有一个去处（appState＝SVG 那一路读的），且⛔ 在 opts 顶层再出现一颗同名键
        Assert.Equal(1, Count(js, "prefs.exportScale = scale;"));
        Assert.Equal(0, Count(js, "exportScale:"));
    }

    [Fact]
    public void BridgeJsNeverPostsAnUnknownCmdLiteral()
    {
        var js = ReadRepo(BridgeJs);
        var matches = System.Text.RegularExpressions.Regex.Matches(js, "postIn\\(\"([^\"]+)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
            Assert.True(BridgeProtocol.IsKnownName(m.Groups[1].Value, BridgeProtocol.Direction.Inbound),
                "页面侧出现了名单外的 cmd：" + m.Groups[1].Value);
        // ⚠ 那颗 send 绕开 postIn，从前这把尺就看不见它（名册 552 的另一半：尺的射程⛔ 等于要覆盖的面）
        var bare = System.Text.RegularExpressions.Regex.Matches(js, "cmd:\\s*\"([^\"]+)\"");
        Assert.True(bare.Count > 0, "这把尺要是⛔ 命中任何东西，它就是聋的（那颗 nack 的名字被改走也照样绿）");
        foreach (System.Text.RegularExpressions.Match m in bare)
            Assert.True(BridgeProtocol.IsKnownName(m.Groups[1].Value, BridgeProtocol.Direction.Inbound),
                "页面侧那颗裸 cmd ⛔ 在进向名单里：" + m.Groups[1].Value);
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
        // 已放行的行为分支各在（片56 起五颗）；未接线的三颗走 default 那一格——它<b>只留一行痕</b>（片57＝P-245 撤掉那句回话之后）
        Assert.Contains("case \"bridge.ready\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"open.external\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"export.bytes\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"bridge.reply\"", cs, StringComparison.Ordinal);   // 片56：那句 nack 从此有主人
        // ⚠ 这一格读的是<b>抹掉注释后</b>的代码：那颗标签在类文档里也出现一次（讲的就是这一族），
        // 按原文数会让"撤掉那一行痕"这一臂照旧绿＝名册 496 那一族（要改的是问法，⛔ 放宽判据）。
        Assert.Contains("not-wired", SourceGate.Code(cs), StringComparison.Ordinal);
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
    /// 四处 <c>FinishClosingFlush();</c> 分别对应"盘上本来就是它"、"这一问被写闸拒了"（片26），
    /// 与片58 落地那一格里<b>各走一支</b>的两颗——"那一发写手抛了"与"那一发真落进盘（或写不成）"。
    /// 少了前者那次关不掉，少了后两颗那颗写失败／那一发抛了要用户白等两秒。</para>
    /// </summary>
    [Fact]
    public void TheCloseFlushIsBoundedAndSaysWhenItGaveUp()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        Assert.Contains("BoardPolicy.CloseFlushBudgetMs", code, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "2000"));                       // 那颗数⛔ 在这档里再出现一次
        Assert.Equal(2000, BoardPolicy.CloseFlushBudgetMs);         // 正件：主人那里它还在
        Assert.Equal(5, Count(code, "FinishClosingFlush();"));      // 片58②：超限那一支也答了那一问（写闸拒／没变／超限／抛／落地＝五支，⛔ 第六支＝有人白等）
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
        Assert.Equal(5, Count(code, "FinishClosingFlush();"));      // 颗数与「各归哪一支」由上面那格钉住；这里问的是搬进线程池之后⛔ 多出一位主人
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
        // 片58② 起是四处：递档失败到底／写闸拒／<b>超限那一发</b>／重造失败——同一块面板⛔ 许再发明一句话，
        // 也⛔ 许少一处（少一处＝那一路又回到静默，P-53 那一族）。逐颗点名由各自那一格钉住。
        Assert.Equal(4, Count(code, "ShowArmStuckNotice();"));
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
        // 兜底那颗建目录：片54 起这一档里 <c>CreateDirectory</c> 只许有一位主人（<c>EnsureExports</c>），
        // 所以这一支只认它一次；"全档只许一处真调用"那一格住在片54 那颗新尺里（一把尺只量一次）。
        Assert.Equal(1, Count(pick, "EnsureExports();"));
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
        // 片61 起七颗：声明／写闸拒那一支／超限那一支／复位／分岔／<b>那一行痕里那列 refused=</b>／<b>退出那一拍的 return</b>
        Assert.Equal(7, Count(SourceGate.Code(raw), "_closingRefused"));                // ⛔ 第八颗主人（每加一处都要逐颗点名，⛔ 放宽谓词）
    }

    /// <summary>片61＝P-247 那一半：<b>那一问有回音</b>与<b>那一份落了地</b>是两颗问题，那行痕与主窗那句计数⛔ 许再混着报。
    /// <para>起因＝片59 真机量到的那张脸：痕 <c>landed=True</c>／<c>scene.saved 行数=0</c>／盘上停在<b>上一份</b>那颗档，
    /// 而主窗那句「退出：画板窗那一拍落地 N 扇」照旧把被拒的那一发算成好话（<c>MainWindow.xaml.cs</c> 那一行读的就是这颗 bool）。</para>
    /// <para>改法两粒，各钉一排：① 那一行痕补一列 <c>refused=</c>——那颗 <c>landed=</c> 的老语义<b>一个字节⛔ 动</b>
    /// （它问的是「页面回没回话」，片27 为了⛔ 让用户白等两秒定的），分家由新那一列说；② <b>退出那一拍交回的 bool</b>
    /// 改成「真落了地」＝<c>done &amp;&amp; !_closingRefused</c>，那颗 bool 只有一位消费者（那句计数），所以这一改⛔ 牵连别处。</para>
    /// <para>⚠ 这一格⛔ 钉「屏幕上那句话来不来得及被看见」——那一问归
    /// <see cref="TheCloseBeatBringsTheWindowBackWhenWhatCameWasARefusal"/>（用户按×那一拍亮回那颗窗）与片41 的真机读数；
    /// 退出那一拍本来就⛔ 贴卡（<c>notice:false</c>，见那颗禁项格），它的话只在日志里。</para></summary>
    [Fact]
    public void TheCloseFlushLineTellsEchoApartFromLanding()
    {
        var raw = ReadRepo(WindowCs);
        var flush = SourceGate.MethodBody(SourceGate.Code(raw), "Task<bool> FlushSnapshotAsync");
        Assert.Contains("\" refused=\" + _closingRefused", flush, StringComparison.Ordinal);
        Assert.Equal(1, Count(flush, " refused="));                     // 那颗字只有那一行痕念它＝⛔ 第二个措辞出处
        var atLanded = flush.IndexOf("landed=", StringComparison.Ordinal);
        var atRefused = flush.IndexOf(" refused=", StringComparison.Ordinal);
        var atSame = flush.IndexOf(" same=", StringComparison.Ordinal);
        Assert.True(atLanded >= 0 && atRefused > atLanded && atSame > atRefused,
            "那三列⛔ 按 landed→refused→same 排＝读痕的人按位置对不上（真机那份正本的口径）");
        Assert.Contains("landed=\" + done", flush, StringComparison.Ordinal);    // 那颗老字照旧只由「有没有回音」决定（⛔ 顺手改它的语义）

        var exit = SourceGate.MethodBody(SourceGate.Code(raw), "Task<bool> FlushForExitAsync");
        Assert.Contains("return done && !_closingRefused;", exit, StringComparison.Ordinal);
        Assert.Equal(1, Count(exit, "!_closingRefused"));               // 只有那颗计数由它说话
        Assert.DoesNotContain("NoticeCard.Show(", exit, StringComparison.Ordinal);   // 退出那一拍照旧⛔ 贴卡（P-220 定过的）
        Assert.DoesNotContain("Close();", exit, StringComparison.Ordinal);           // 也⛔ 拦关闭：进程马上就没了
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
        Assert.Contains("private void OnLocaleChanged()", code, StringComparison.Ordinal);
        Assert.Equal(5, Count(code, "PasteNotice();"));             // ShowNotice 那一拍＋换语言那一拍＋被拒亮回那一拍补两颗新按钮＋片49 那颗「再试一次」跑前跑后各一次（五处都只⛔ 调那颗主人，⛔ 各写一遍可见性）
        // 片54：那一行是第二块会说汉语的表面，它那颗主人同样只许被数得清的那几处调（多一处＝第二副措辞出处）。
        // 七颗＝开场那一拍（放开"还在导出"那一闩）＋交回字节那一拍＋按下去那一拍＋换语言那一拍＋开目录坏了那一拍
        //        ＋<b>片64 那两颗扳机各一颗</b>（挑完缩放／翻完开关当场改那行字，⛔ 等下一次导出才回音）。
        Assert.Equal(7, Count(code, "PasteBar();"));
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
            "Task OpenSavedFolderAsync", "bool CanOpenSavedFolder",
            "void StayOnBoard", "void LeaveAnyway",
            // 片49 那三颗：坏话的上屏出口与它的两颗按钮都在这条线上（⛔ 在这里拼一句中文）
            "Task OpenSceneAsync", "Task RetryBlockedSceneAsync", "void StartFreshBoard",
            // 片54 那一行：两颗入口与那句落点都只许念表里的键（硬编码的字在这里＝下一批换语言时它不动）
            "void PasteBar", "void ExportImageButton", "void ReportExport", "Task OpenExportedFolderAsync",
            "Task<bool> OpenFolderAroundAsync",
            // 片56 那两拍：nack 与那颗预算都只换那句话，字⛔ 许自己长在代码里
            "void HandleBridgeReply", "Task WatchExportAnswerAsync",
            // 片64 那两颗扳机：翻一档／换一态都只改那颗字段再重贴，字⛔ 许长在代码里（"×" 是记号而⛔ 是句子）
            "void CycleExportScale", "void ToggleExportBackground", "string ScaleToken",
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
    public void TheFolderExitAppearsOnlyWhileASentenceClaimsAFileLanded()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        // ① 那几种"屏幕上挂着的是某一手的下落"只许有一份名单，两颗问法都从它派生
        var outcomes = SourceGate.Between(code, "string[] FileOutcomeKeys", "];");
        // 片54 之后那一发导出的下落⛔ 进这一问（它由右上角那一行自己说）：这一族还是那颗「另存」的四种下落。
        // 多一颗＝把旧话当成新事实；少一颗＝下一批加了一句而这两问都听不见。
        Assert.Equal(4, Count(outcomes, "\"boards."));
        foreach (var landed in new[]
        {
            "boards.copySavedBody", "boards.copySavedStaleBody", "boards.openFolderFailedBody", "boards.copyFailedBody",
        })
            Assert.Contains(landed, outcomes, StringComparison.Ordinal);
        Assert.Equal(0, Count(outcomes, "boards.export"));      // 那两句住在另一块表面，混进来＝两颗按钮抢一句下落
        // ② 那颗按钮的资格＝"名单之内"且"那一句认定盘上真有一颗档"，而它⛔ 自己再列一遍键名（两处各列＝迟早只补一处）
        var can = SourceGate.MethodBody(code, "bool CanOpenSavedFolder");
        Assert.Equal(3, Count(code, "CanOpenSavedFolder"));
        // 片58② 起四颗：定义／写闸拒那一支的"⛔ 用旧话顶掉新事实"／<b>超限那一支同一颗问法</b>／那颗按钮的资格
        Assert.Equal(4, Count(code, "NoticeSaysFileOutcome"));
        Assert.Contains("_savedPath is not null", can, StringComparison.Ordinal);
        Assert.Contains("NoticeSaysFileOutcome", can, StringComparison.Ordinal);
        // 那一句"没写成"⛔ 认定档存在，而它⛔ 能从那份名单里被忘掉（忘掉＝那颗按钮指着⛔ 存在的档）⇒名单另有一对子守这一维
        var noLanded = SourceGate.Between(code, "string[] NoLandedFileKeys", "];");
        Assert.Equal(1, Count(noLanded, "\"boards."));
        Assert.Contains("boards.copyFailedBody", noLanded, StringComparison.Ordinal);
        Assert.Contains("Array.IndexOf(NoLandedFileKeys, _noticeKey!) < 0;", can, StringComparison.Ordinal);
        // ③ 同一件旧话⛔ 许把新事实顶掉（片39 真机量到的那一发：拒话重贴盖掉「已另存到 …」＋那颗新按钮跟着退场）
        var got = SourceGate.MethodBody(raw, "void HandleExportBytes");
        Assert.Contains("if (!NoticeSaysFileOutcome) ShowArmStuckNotice();", got, StringComparison.Ordinal);
        // ④ 那颗按钮⛔ 在场＝用户可以再看一次；摘掉它＝把作业推回给他（P-54 定过的坏法）
        Assert.Equal(1, Count(outcomes, "boards.openFolderFailedBody"));

        Assert.Equal(1, Count(code, "folder.Visibility = CanOpenSavedFolder ?"));
        Assert.Contains("folder.Content = Word(\"boards.openFolderAction\");",
            SourceGate.MethodBody(code, "void PasteNotice"), StringComparison.Ordinal);
        Assert.Contains("Phrase(\"boards.openFolderFailedBody\", _savedPath ?? string.Empty);",
            SourceGate.MethodBody(code, "void PasteNotice"), StringComparison.Ordinal);

        var stage = SourceGate.MethodBody(code, "private Grid BuildStage");
        // （那颗按钮⛔ 跟着主题走这一格住在 BoardWordGateTests 那颗色尺里：一把尺只量一次，⛔ 两处各数一遍。）
        Assert.Contains("panel.Children.Add(_noticeFolderButton);", stage, StringComparison.Ordinal);

        // 片54：那颗动作收进一块表面⛔ 许有第二份——两块表面（面板与右上角那一行）共用 <c>OpenFolderAroundAsync</c> 这一颗。
        var around = SourceGate.MethodBody(raw, "Task<bool> OpenFolderAroundAsync");
        // 动作走本机路径那一族的唯一出口，⛔ 在画板这一档再拼一遍 explorer 命令行；
        // ⚠ 而递进去的必须是<b>一颗 URI</b>：那颗还原判据只认 file:// 前缀，裸磁盘路径两格候选都是空串⇒
        // 退化成"图已不在本机"那句坏话＋桌面多开一颗 Explorer <b>主文件夹</b>窗（2026-10-19 真机读数逐字为证：
        // <c>boards.folder.opened … result=failed</c>・<c>title='主文件夹 - 文件资源管理器'</c>·正本 readings_39_183824）。
        Assert.Contains("Helpers.LauncherEx.TryOpenAsync(StarMark.Abstractions.LocalFileIdentity.UriForPath(folder))",
            around, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOpenAsync(folder)", around, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "Helpers.LauncherEx.TryOpenAsync(StarMark.Abstractions.LocalFileIdentity.UriForPath"));
        // 那一发要有记录，而<b>原因</b>也得进得去（P-56 口径：只报"坏了"下一跑说不出是谁坏的）
        Assert.Contains("boards.folder.opened", around, StringComparison.Ordinal);
        Assert.Contains("why=", around, StringComparison.Ordinal);
        // 坏话由各自那一块表面说（⛔ 在这颗共用动作里替两块 surface 选一句）
        var open = SourceGate.MethodBody(raw, "Task OpenSavedFolderAsync");
        Assert.Contains("if (!CanOpenSavedFolder) return;", open, StringComparison.Ordinal);
        Assert.Contains("ShowNotice(\"boards.openFolderFailedBody\", titled: true, offerCopy: true);",
            open, StringComparison.Ordinal);
        Assert.DoesNotContain("LauncherEx", open, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "explorer.exe"));
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

    /// <summary>片54＝那一发导出的落点归宿主（P-240 的②），六格一起钉。
    /// <para>① 那颗订阅<b>排在 Navigate 之前</b>，并与那三颗同一副先摘再挂（重建控件那一趟⛔ 两颗订阅，
    /// 而 Navigate 之后才起来的那一发下载如果没人接＝那颗框照旧起来，本批修的正是它）；
    /// ② <b>认不出档位＝一颗也不碰</b>：判据交 <c>null</c> 那一支必须当场收手，⛔ 因为我不认得就替它决定落点；
    /// ③ 落点与命名交宿主＝<c>ResultFilePath</c> 由 <c>ExportsDirectory</c> ＋ <c>SanitizeExportName</c> 拼，
    /// <c>Handled = true</c> 摘掉那颗系统框（这两颗一起才叫"改口"：只改落点⛔ 改命名＝那位主人还在浏览器手里）；
    /// ④ 那句"已存到"只跟着 <c>Completed</c>（⛔ 在字节还在路上那一刻说话），而"没写成"那一句也必须有（摘掉默认 UI 之后
    /// 屏幕上再没别人说这一手的下落＝把"点了没反应"留在屏幕上，§坑13 第一工程风险）；
    /// ⑤ 产物目录那颗 <c>CreateDirectory</c> 全档只许一位主人（P-241 的③件）；
    /// ⑥ 名字的主人只有一颗：<c>ExportStem()</c> 被两路各问一次，⛔ 在两支里各拼一遍 <c>id + 时刻</c>；
    /// ⑦（片55＝P-243）那一发落不下去时⛔ 许盖掉上一颗：档名唯一主人＝<c>BridgeProtocol.UniqueExportName</c>，
    /// 写盘那一手排在它之后，交回 <c>null</c> 那一支必须走"没能存成"那一句。</para></summary>
    [Fact]
    public void TheExportLandsWhereTheHostSaysWhileTheEngineDialogStaysShut()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        // ① 那颗接管下载的尝试⛔ 许留下："订了而永远不响"＝一颗没人读得到的分支（真机读数见类文档那段）。
        Assert.Equal(0, Count(code, "DownloadStarting"));
        Assert.Equal(0, Count(code, "CoreWebView2DownloadOperation"));

        // 两个开关不可合并：saveAsImage 关闭图片框，export 关闭 JSON 框。
        var js = ReadRepo(BridgeJs);
        Assert.Contains("export: false,", js, StringComparison.Ordinal);
        Assert.Contains("saveAsImage: false,", js, StringComparison.Ordinal);
        Assert.Equal(0, Count(js, "saveFileToDisk"));     // 那颗旋钮随整颗框一起退场（留着＝第二副口径）

        // ③ 那一发落点与命名归宿主：字节到手之后先拼名字、再建目录、再写，两拍下落各有读数
        var bytes = SourceGate.MethodBody(code, "void HandleExportBytes");
        Assert.Contains("BridgeProtocol.SanitizeExportName(envelope.Suggested, ExportStem(), envelope.Kind)",
            bytes, StringComparison.Ordinal);
        Assert.Contains("var target = Path.Combine(ExportsDirectory, name);", bytes, StringComparison.Ordinal);
        Assert.Contains("EnsureExports();", bytes, StringComparison.Ordinal);
        Assert.Contains("File.WriteAllBytes(target, bytes);", bytes, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)",
            bytes, StringComparison.Ordinal);                                    // ⛔ 静默：写不进要说得出
        Assert.Contains("StarLog.Warn(\"boards.export.failed kind=\"", bytes, StringComparison.Ordinal);
        foreach (var column in new[] { "kind=", "bytes=", "to=", "why=" })
            Assert.Contains(column, bytes, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.exportSavedBody\", target);", bytes, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.exportFailedBody\", null);", bytes, StringComparison.Ordinal);
        // 那句"已存到"排在真写成之后（⛔ 排在按下去那一拍）：两颗 ReportExport 都在写盘那句之后
        Assert.True(bytes.IndexOf("File.WriteAllBytes(target, bytes);", StringComparison.Ordinal)
            < bytes.IndexOf("ReportExport(\"boards.exportSavedBody\", target);", StringComparison.Ordinal),
            "那句成功话排在写盘之前＝还没落盘就告诉用户它落好了");

        // ④ 右上角那一行的三颗脸：两颗入口只问那两档，那颗出口只认"真落盘的那一颗"
        var bar = SourceGate.MethodBody(code, "void PasteBar");
        Assert.Contains("Word(\"boards.exportPngAction\")", bar, StringComparison.Ordinal);
        Assert.Contains("Word(\"boards.exportSvgAction\")", bar, StringComparison.Ordinal);
        Assert.Contains("Word(\"boards.openFolderAction\")", bar, StringComparison.Ordinal);
        Assert.Contains("_barFolderButton.IsEnabled = _exportPath is not null;", bar, StringComparison.Ordinal);
        Assert.Contains("_exportPath ?? string.Empty", bar, StringComparison.Ordinal);   // 那颗路径是实参，⛔ 拼成品句
        Assert.Equal(0, Count(bar, "Locale.T("));                                        // 只许念表里的键
        // 那一行⛔ 去动那块面板：面板可能正挂着"这一扇已停止保存"，一句好消息没资格把它顶掉（片39 那一族）
        Assert.DoesNotContain("ShowNotice", bar, StringComparison.Ordinal);
        // 那一行的在场资格：换语言那一拍两处都重贴（⛔ 一颗重贴一颗冻着＝P-175 那一族的第三种脸）
        var locale = SourceGate.MethodBody(code, "void OnLocaleChanged");
        Assert.Contains("PasteNotice();", locale, StringComparison.Ordinal);
        Assert.Contains("PasteBar();", locale, StringComparison.Ordinal);

        // ⑤ 那一问的唯一发送处：宿主侧只许一颗 export.pick 的形状（scene 与那两档图共用它，⛔ 两处各拼一遍）
        Assert.Equal(1, Count(code, "PostOutbound(\"export.pick\""));
        Assert.Contains("PostExportPick(\"scene\");", code, StringComparison.Ordinal);
        Assert.Contains("PostExportPick(kind);", code, StringComparison.Ordinal);
        Assert.Equal(3, Count(code, "PostExportPick("));      // 定义一颗＋两颗调用点，多一处＝第二个形状
        Assert.Contains("ExportImageButton(\"png\")", code, StringComparison.Ordinal);
        Assert.Contains("ExportImageButton(\"svg\")", code, StringComparison.Ordinal);
        var ask = SourceGate.MethodBody(code, "void ExportImageButton");
        Assert.Contains("if (!_bridgeReady || _exporting) return;", ask, StringComparison.Ordinal);
        Assert.Contains("if (!BridgeProtocol.IsKnownExportKind(kind)) return;", ask, StringComparison.Ordinal);

        // ⑥ 那颗目录与那颗名字各只有一位主人（②那一路删掉之后，调用点各少一颗）
        Assert.Equal(1, Count(code, "Directory.CreateDirectory(ExportsDirectory)"));
        Assert.Equal(2, Count(code, "EnsureExports();"));
        Assert.Equal(3, Count(code, "ExportStem()"));        // 声明一颗＋收名两路（原型与兜底根）＝片55 起的形状

        // ⑦ 同名⛔ 盖（片55＝P-243）：那一颗档名的唯一主人＝Core 那颗纯函数，写盘那一手排在它之后
        Assert.Equal(1, Count(code, "BridgeProtocol.UniqueExportName("));
        // "盘上有没有"那一问只许递给 File.Exists（NTFS 认⛔ 大小写＝同一颗名）；换成自己列目录再比串＝第二个主人
        Assert.Contains("candidate => File.Exists(Path.Combine(ExportsDirectory, candidate))", bytes, StringComparison.Ordinal);
        Assert.Contains("if (name is null)", bytes, StringComparison.Ordinal);
        Assert.Contains("why=NameTaken", bytes, StringComparison.Ordinal);
        // 名占满那一拍<b>⛔ 走到写盘那一句</b>：它念那句坏话之前就 return（从前这一格钉的是那句回话的故障名，
        // 而那句回话页面压根⛔ 听——改钉它真正守的那一维＝⛔ 拿别人的那颗档去盖）
        Assert.True(bytes.IndexOf("ReportExport(\"boards.exportFailedBody\", null);", StringComparison.Ordinal)
                    < bytes.IndexOf("var target = Path.Combine(ExportsDirectory, name);", StringComparison.Ordinal),
            "名占满那一拍的坏话排在拼落点之后＝那一拍已经走到写盘那一路了");
        Assert.Equal(1, Count(bytes, "File.WriteAllBytes(target, bytes);"));
        Assert.True(bytes.IndexOf("UniqueExportName", StringComparison.Ordinal)
                    < bytes.IndexOf("File.WriteAllBytes(target, bytes);", StringComparison.Ordinal),
                    "先定名再写：名⛔ 定就先写＝同名那一颗还是会被盖掉");
    }

    /// <summary>片64＝那两档交回用户（账本 P-242 的落地腿）。八格一起钉，缺一格都只是修了一半。
    /// <para>① <b>名单只有一位主人</b>：桥里那份 JS 字面必须与 <see cref="BoardPolicy.ExportScales"/> 现读相等——
    /// 页面读不到 C#，所以那里⛔ 得再抄一份数字，而"两份名单"迟早分岔（往名单加一颗 4 而只改 Core，
    /// 屏幕上那颗按钮会出现 4×、递进桥里却被当作⛔ 认的值丢掉＝一句话替用户决定了他挑的那一档）；
    /// ② 那颗 <c>appState</c> 从此是<b>这一发的副本</b>（<c>Object.assign({}, …, prefs)</c>），旧形状（直接交
    /// <c>API.getAppState()</c>）必须为 0 颗——那是 P-242 记下"尺寸由那台机器定"的那一手；
    /// ③ 禁项：<b>⛔ 写回引擎活着那份 appState</b>。片62 的处置原本点名 <c>setAppState</c>，片63 现读否掉：
    /// 那颗名字在随包档里 24 颗命中＝<b>18 颗属性形状／0 颗方法调用</b>，它⛔ 在那颗对外对象里（坑表 #851）。
    /// 今天这一格的绿＝"名字没出现"，而它守的是"别照着那颗名字再写一遍"；
    /// ④⑤ 两档都<b>不认就不并</b>（沿引擎默认），且 scene 那一发提前 return、永远⛔ 读它们；
    /// ⑥⑦ 宿主那一路只有一颗形状、两颗值都从手里那两颗字段来；
    /// ⑧ 两颗扳机在右上角那一行<b>永远在场</b>，⛔ 跟着"这一发正在跑"那闩关（挑档是宿主自己的事）。</para></summary>
    [Fact]
    public void TheTwoExportKnobsRideThatOneAskAndMergeOnlyIntoItsOwnOpts()
    {
        var js = ReadRepo(BridgeJs);                       // 那颗扫描器是照 C# 写的，页面那一档⛔ 交给它（本档同一口径）
        var code = SourceGate.Code(ReadRepo(WindowCs));

        // ① 名单：桥里那一串逐字由 Core 现读拼出（这里⛔ 写 1／2／3 三颗数字＝第二个主人）
        var guard = "if (scale === " + string.Join(" || scale === ", BoardPolicy.ExportScales)
                    + ") prefs.exportScale = scale;";
        Assert.Contains(guard, js, StringComparison.Ordinal);
        Assert.Contains("if (background === true || background === false) prefs.exportBackground = background;",
            js, StringComparison.Ordinal);

        // ② 那一发的 opts 带的是副本（旧形状一颗也不许留）
        Assert.Contains("appState: Object.assign({}, API.getAppState(), prefs),", js, StringComparison.Ordinal);
        Assert.Equal(0, Count(js, "appState: API.getAppState(),"));
        Assert.Equal(1, Count(js, "var prefs = {};"));

        // ③ 那颗引擎内部的 prop⛔ 许再被点名（片63 现读：它在对外对象里根本没有）
        Assert.Equal(0, Count(js, "setAppState"));

        // ④⑤ 值只从 args 来；scene 那一发⛔ 经过那颗 opts
        Assert.Contains("runExport(m.args && m.args.kind, m.args && m.args.skipInliningFonts, m.id,", js, StringComparison.Ordinal);
        Assert.Contains("m.args && m.args.exportScale, m.args && m.args.exportBackground);", js, StringComparison.Ordinal);
        Assert.Equal(1, Count(js, "m.args.exportScale"));
        Assert.Equal(1, Count(js, "m.args.exportBackground"));
        Assert.True(js.IndexOf("if (kind === \"scene\")", StringComparison.Ordinal)
                    < js.IndexOf("var opts = {", StringComparison.Ordinal),
                    "scene 那一支排在那颗 opts 之后＝板档那一路也会被这两档碰");

        // ⑥⑦ 宿主那一路：一颗形状、两颗值都取自手里那两颗字段
        Assert.Equal(1, Count(code, "Envelope(\"export.pick\", \"h4\""));
        Assert.Contains("JsonString(\"exportScale\") + \":\" + _exportScale + \",\"", code, StringComparison.Ordinal);
        Assert.Contains("JsonString(\"exportBackground\") + \":\" + (_exportBackground ? \"true\" : \"false\")",
            code, StringComparison.Ordinal);
        // 那两档从此<b>住在出向名册里</b>：⛔ 登记＝宿主自己拼的信封被那道静默的门挡下（两颗按钮点了没反应）
        Assert.Contains("exportScale", string.Join(",", BridgeProtocol.OutboundFields["export.pick"]),
            StringComparison.Ordinal);
        Assert.Contains("exportBackground", string.Join(",", BridgeProtocol.OutboundFields["export.pick"]),
            StringComparison.Ordinal);

        // ⑧ 两颗扳机各 add 一遍，且⛔ 跟着那一闩关
        var stage = SourceGate.MethodBody(code, "private Grid BuildStage");
        Assert.Contains("barRow.Children.Add(_barScaleButton);", stage, StringComparison.Ordinal);
        Assert.Contains("barRow.Children.Add(_barBackgroundButton);", stage, StringComparison.Ordinal);
        var bar = SourceGate.MethodBody(code, "void PasteBar");
        Assert.Equal(0, Count(bar, "_barScaleButton.IsEnabled"));
        Assert.Equal(0, Count(bar, "_barBackgroundButton.IsEnabled"));
        Assert.Contains("_barScaleButton.Click += (_, _) => CycleExportScale();", code, StringComparison.Ordinal);
        Assert.Contains("_barBackgroundButton.Click += (_, _) => ToggleExportBackground();", code, StringComparison.Ordinal);
    }

    // ────────── 片56：那一发导出问出去之后，两条下落都得有主人（P-244） ──────────

    /// <summary>页面那一半：<b>档位决定交回什么形状</b>，而链上每一环都得把那颗 promise 交回去。
    /// <para>四格分岔，缺任一格都只修了一半：① svg 那一支⛔ 再向一颗 DOM 节点要 <c>arrayBuffer</c>
    /// （现读类型＝<c>Promise&lt;SVGSVGElement&gt;</c>），改法是照引擎自己那一路取 <c>outerHTML</c> 再包一颗 Blob；
    /// ② 那颗 <c>return</c> 排在 <c>arrayBuffer</c> 那一环之前——从前它写在 then 里而没 return，
    /// 于是那一环一旦抛就永远走不到 <c>.catch</c>，而 <c>.catch</c> 那句 send 是宿主唯一的回音；
    /// ③ 整颗 <c>runExport</c> 另包一层 try，那是"API 还没起来"那一档<b>同步</b>抛的出口（它连 promise 都没生出来）；
    /// ④ 那颗 send 的两颗字段搬进 <c>args</c>＝它从此过同一道白名单门（⛔ 挂在顶层＝绕开门）。
    /// ⚠ 这里钉的是<b>写法</b>；"那一发真落盘了吗"⛔ 是形状量得到的，归真机腿那一跑。</para></summary>
    [Fact]
    public void ThePageBranchesByKindAndCarriesEveryFailureBack()
    {
        var js = ReadRepo(BridgeJs);
        // ① 现读的引擎口径逐字：outerHTML ＋ 那颗 MIME（⛔ 自己另发明一副序列化）
        Assert.Contains("L.exportToSvg(opts).then(function (node) { return new Blob([node.outerHTML], { type: \"image/svg+xml\" }); })",
            js, StringComparison.Ordinal);
        Assert.Contains(": L.exportToBlob(opts);", js, StringComparison.Ordinal);
        // ② 每一环把 promise 交回去：旧形状（arrayBuffer 包在 then 里而没 return）⛔ 许回来
        Assert.Contains("toBlob.then(function (blob) { return blob.arrayBuffer(); })", js, StringComparison.Ordinal);
        Assert.Equal(0, Count(js, "blob.arrayBuffer().then"));
        Assert.Equal(1, Count(js, "toBlob.then("));
        // ③ 同步抛的那一档也有出口：外层那颗 try 的 catch 里就是一句 send（两颗 send：一颗在 .catch、一颗在这里）
        Assert.Equal(2, Count(js, "send(replyId, false, \"export-failed\")"));
        Assert.Contains("} catch (e) {\n      send(replyId, false, \"export-failed\");", js, StringComparison.Ordinal);
        // ④ 那颗 nack 的两颗字段住在 args 里（名单门只看得进 args）
        Assert.Contains("args: { ok: ok, err: err || null }", js, StringComparison.Ordinal);
        Assert.Equal(0, Count(js, "id: id, ok: ok"));
    }

    /// <summary>宿主那一半：nack 有主人、那颗问有预算、而那一闩只由一颗收口处放开。
    /// <para>① 名字在册之后还⛔ 许"收到而不动"：switch 那一格得指到一颗真读 <c>ok</c> 的主人，
    /// 而<b>认不出的</b>与<b>迟到的</b>两档都收手（<c>Ok is not false || !_exporting</c>）；
    /// ② 那颗预算只住 Core：UI 档里再写一遍那颗数＝第二个主人（与 <c>CloseFlushBudgetMs</c> 同一条口径，
    /// 所以三颗数一起钉：档里读不到 5000、主人那里读得到、且它确实大于关窗那一拍的预算）；
    /// ③ 那颗闹钟比的是<b>两颗</b>：代次认得"问的是哪一发"，那颗闩认得"那一发还没结"——
    /// 只比闩就会让第一发的闹钟把第二发判死（<see cref="BoardWindow"/> 那颗 <c>_exportGen</c> 的注释说的正是这件事）；
    /// ④ 放开那一闩的地方全档只有两处（收口那颗＋ready 重闩），开口处一颗；
    /// ⑤ 那句 nack ⛔ 回 ack（页面那侧⛔ 读者），也⛔ 动盘上任何东西。</para></summary>
    [Fact]
    public void TheHostReadsTheNackAndBoundsTheAsk()
    {
        var code = SourceGate.Code(ReadRepo(WindowCs));
        var nack = SourceGate.MethodBody(code, "void HandleBridgeReply");
        Assert.Contains("HandleBridgeReply(envelope);", code, StringComparison.Ordinal);
        Assert.Contains("if (envelope.Ok is not false || !_exporting) return;", nack, StringComparison.Ordinal);
        Assert.Contains("boards.export.nack err=", nack, StringComparison.Ordinal);
        Assert.Contains("envelope.Err ?? \"unknown\"", nack, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.exportFailedBody\", null);", nack, StringComparison.Ordinal);
        Assert.Equal(0, Count(nack, "PostWebMessageAsJson"));             // ⛔ 回一句 ack：页面那侧⛔ 读者
        Assert.Equal(0, Count(nack, "File."));                            // 那一拍⛔ 动盘上任何东西

        var watch = SourceGate.MethodBody(code, "Task WatchExportAnswerAsync");
        Assert.Contains("await System.Threading.Tasks.Task.Delay(BoardPolicy.ExportAnswerBudgetMs);", watch, StringComparison.Ordinal);
        Assert.Contains("if (gen != _exportGen || !_exporting || _tearingDown) return;", watch, StringComparison.Ordinal);
        Assert.Contains("boards.export.stalled ms=", watch, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.exportFailedBody\", null);", watch, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "Task.Delay(BoardPolicy.ExportAnswerBudgetMs)"));
        Assert.Equal(0, Count(code, "5000"));                             // 那颗数⛔ 在这档里再出现一次
        Assert.Equal(5000, BoardPolicy.ExportAnswerBudgetMs);             // 正件：主人那里它还在
        Assert.True(BoardPolicy.ExportAnswerBudgetMs > BoardPolicy.CloseFlushBudgetMs,
            "图那一发要过渲染器，那颗预算⛔ 能短过关窗那一拍");

        var ask = SourceGate.MethodBody(code, "void ExportImageButton");
        Assert.Contains("_exportGen++;", ask, StringComparison.Ordinal);
        Assert.Contains("_ = WatchExportAnswerAsync(_exportGen);", ask, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "_exportGen++"));                     // 那位计数只许有一个写主
        Assert.Equal(1, Count(code, "private int _exportGen;"));
        Assert.Equal(2, Count(code, "_exporting = false;"));              // 收口那颗＋ready 重闩
        Assert.Equal(1, Count(code, "_exporting = true;"));               // 只有开口那一拍
    }

    /// <summary>那颗 <c>ok</c> 只认真布尔：认不出＝null，⛔ 替页面猜一句它没说的话。
    /// <para>为什么单独钉这颗读法：nack 的两种"没内容"（那颗键缺失、那颗键是颗字符串"false"）在屏幕上
    /// 都该长成"什么都没发生"（由那颗预算兜底），而被猜成 <c>false</c> 就直接念一句坏话、
    /// 被猜成 <c>true</c> 就把那一发悄悄放过（片33 那族"null 折成 0"的另一副脸）。</para></summary>
    [Fact]
    public void TheEnvelopeReadsTheNackFieldsWithoutGuessing()
    {
        var code = SourceGate.Code(ReadRepo(EnvelopeCs));
        Assert.Contains("Ok = ArgBool(\"ok\"),", code, StringComparison.Ordinal);
        Assert.Contains("Err = Arg(\"err\"),", code, StringComparison.Ordinal);
        Assert.Contains("v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null",
            code, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "bool? ArgBool"));
        Assert.Equal(0, Count(code, "bool.Parse"));
        Assert.Equal(0, Count(code, "Convert.ToBoolean"));
    }

    /// <summary>片57＝P-245：宿主→页面只剩<b>一颗过门的门</b>，那 16 句没人听的回话整族撤掉。
    /// <para>改前的形状＝那颗 <c>Reply</c> 工厂直接 post，把<b>进向那颗名</b>原样当出向名字用，
    /// 而 <c>OutboundEvents</c> 那六颗⛔ 包含它——出向那道门（<c>Check</c> 的 <c>Direction.Outbound</c>）
    /// 在这条路上压根⛔ 被问；页面那一头的 <c>onOut</c> 认得的也就是那六颗，多出来的一律落 <c>default: break</c>
    /// ⇒ 十六句全部静默丢掉（正本 <c>bin/WI97/probe57_readers_out.txt</c>：监听器一颗、switch 一颗、代码里裸 post 两颗）。</para>
    /// <para>⚠ 这一格为什么⛔ 只是"少一句解释"而值得动：<b>留着出口＝下一批把它读成"这条路已接通"</b>
    /// （RV-2 那颗先例，与名册 533「只改口＝死代码＋恒绿证人」同一问）。撤掉之后每一条被拒的路都还留一行痕，
    /// 而<b>被拒的那两颗名字（cmd 与 URL）⛔ 进日志</b>——日志伪造那一族（P-13）守的正是"把外人给的字符串念出去"。</para></summary>
    [Fact]
    public void TheHostHasExactlyOneDoorToThePage()
    {
        var code = SourceGate.Code(ReadRepo(WindowCs));
        // ① 那颗回话：工厂、调用点、命名实参那三种形状都该是零颗
        Assert.Equal(0, Count(code, "Reply(envelope.Cmd"));
        Assert.DoesNotContain("private void Reply(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("err: \"", code, StringComparison.Ordinal);
        // ② 裸 post 只许一颗，且它就住在那颗门里、排在出向门之后（存在与顺序两颗分开写＝名册 447）
        Assert.Equal(1, Count(code, "PostWebMessageAsJson"));
        var door = SourceGate.MethodBody(code, "public void PostOutbound");
        Assert.Contains("PostWebMessageAsJson", door, StringComparison.Ordinal);
        Assert.Contains("BridgeProtocol.Direction.Outbound", door, StringComparison.Ordinal);   // 问⛔ 是那一侧的名单＝门形在、牙没了
        Assert.True(door.IndexOf("BridgeProtocol.Check(", StringComparison.Ordinal)
                    < door.IndexOf("PostWebMessageAsJson", StringComparison.Ordinal),
            "先 post 再问门＝那颗门⛔ 守得住任何东西");
        // ③ 撤掉回话的那些拍子逐颗留痕（⛔ 静默），且痕里⛔ 念被拒的那颗名字
        foreach (var trace in new[] { "boards.bridge.rejected", "boards.open.rejected", "boards.open.accepted",
                    "boards.cmd.not-wired", "boards.export.kind.unknown", "boards.export.decode.failed",
                    "boards.scene.unchanged" })
            Assert.Contains(trace, code, StringComparison.Ordinal);
        var openBlock = SourceGate.Between(code, "case \"open.external\"", "case \"export.bytes\"");
        Assert.Equal(2, Count(openBlock, "StarLog"));
        foreach (var line in openBlock.Split('\n'))
            if (line.Contains("StarLog", StringComparison.Ordinal))
                Assert.DoesNotContain("envelope.Url", line, StringComparison.Ordinal);   // 那颗 URL 正是被拒的那一颗
    }

    /// <summary>片58＝P-241 的①件：那一发<b>整档写盘摸盘的活</b>离 UI 线程，<b>判据</b>留在 UI 线程。
    /// <para><b>为什么非搬不可（真机读数）</b>：尺＝<c>SendMessageTimeoutW(WM_NULL)</c> 的往返＝那一拍里 UI 线程
    /// 真正不肯收活的长度。改前（停笔／失焦那一拍，正本 <c>bin/WI98/readings_58_r4_210541.txt</c>）
    /// <b>67131 B → 15.6 ms｜2098752 B → 41.2｜8390208 B → 100.5</b>（同档三拍 115.7／100.5／99.8），
    /// 而工作集在一拍里跳 <b>550.3→622.4 MB（+72.1）</b>；</para>
    /// <para>改后（同一把尺挂在<b>关窗</b>那一拍，正本 <c>bin/WI98/readings_58z_ladder2_221131.txt</c>，四档 BAD=0）
    /// <b>67133 B → 15.1 ms｜2098752 B → 45.4｜8390209 B → 86.7｜26216001 B → 273.6</b>，
    /// 宿主自己报的 <c>tookMs</c> <b>182／305／631／1769</b>，那一行痕逐字 <c>tid=2 onUi=True closing=True</c>。
    /// ⚠ 三处口径⛔ 许混：<b>其一</b>，两跑⛔ 是同一拍（失焦↔关窗，后者多一段 grace），能并排的只有
    /// 「同一次整发写在 UI 线程不肯收活这一维上量到多少毫秒」那一列——而它只降了一截（8 MiB：100.5→86.7），
    /// 因为留在 UI 线程上的是<b>解码与那颗 base64 过桥</b>，那正是②件要拦的那一手；
    /// <b>其二</b>，这一族最早那一跑（<c>readings_58z_212332.txt</c>）痕里印的是 <c>offUi=True</c>——那颗常量
    /// <b>⛔ 是读数</b>（由我打死在码里、什么也没量），本批换成 <c>tid=</c>＋<c>onUi=</c> 两颗当场算出来的数，
    /// 下面(丙)那两行为此配了一颗禁项断言；
    /// <b>其三</b>，<c>tid=2 onUi=True</c> 证的是<b>续体回到 UI 线程</b>（判据那一半照旧），
    /// 而「摸盘那一发⛔ 在 UI 线程上」今天只有两样东西撑着：形状（(甲)(乙)：全档一处 <c>TrySave</c>
    /// ＋<c>TaskScheduler.Default</c>）与那颗一致性（25 MiB 那一拍整拍 1769 ms、UI 只拒收 273.6 ms）。
    /// <b>⛔ 写进发布说明</b>：改后<b>停笔</b>那一拍用同一把尺重测，还没有。</para>
    /// <para>四半各钉一件事，缺一半都算没修：<b>(甲)</b> 那一档里⛔ 再直接摸盘（否则那一刀压根⛔ 搬）；
    /// <b>(乙)</b> 两发⛔ 许同时踩同一颗 <c>.tmp</c>（串行闸门由我们钉住，改前由 UI 线程替你排的队），
    /// 且写手⛔ 许排回 UI 线程；<b>(丙)</b> 那一发抛了也⛔ 许静默（P-53 那一族）；
    /// <b>(丁)</b>「这一问有人在等」必须<b>交出去那一刻</b>就认——续体回来时那颗位已被关窗那一拍清掉了。</para></summary>
    [Fact]
    public void TheSceneWriteLeavesTheUiThreadButItsJudgmentsStayOnIt()
    {
        var code = SourceGate.Code(ReadRepo(WindowCs));
        var export = SourceGate.MethodBody(code, "void HandleExportBytes");
        var queue = SourceGate.MethodBody(code, "Task<SceneWrite> QueueSceneWrite");
        var settle = SourceGate.MethodBody(code, "Task SettleSceneWriteAsync");

        // (甲) 摸盘那一发搬出了那一档：全档只许在写手那一格里出现一次
        Assert.DoesNotContain("BoardSceneStore.TrySave", export, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "BoardSceneStore.TrySave("));
        Assert.Contains("BoardSceneStore.TrySave(_boardId, bytes, out var fault, out var rolledBackup)", queue, StringComparison.Ordinal);

        // (乙) 串行：这一发排在上一发后面，下一发再排在<b>这一发</b>后面（那颗临时件只有一颗，两发同时写＝互相踩）
        Assert.Contains("_sceneGate.ContinueWith", queue, StringComparison.Ordinal);
        Assert.Contains("_sceneGate = run.ContinueWith", queue, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.Default", queue, StringComparison.Ordinal);   // ⛔ 排回 UI 线程＝那样等于没搬
        Assert.Equal(0, Count(queue, "DispatcherQueue"));

        // (丙) 抛了也要留下一行痕（只念类型：那颗 message 里可能带路径与外人的字＝P-13 那条 CWE-117 的口径）
        Assert.Contains("boards.scene.write.threw type=", settle, StringComparison.Ordinal);
        Assert.Contains("ex.GetType().Name", settle, StringComparison.Ordinal);
        // ⚠ 那一发「离没离开 UI 线程」⛔ 许写成一颗由我打死的常量当证据：那一列必须由当场那颗线程号算出来。
        Assert.Contains("tid=\" + Environment.CurrentManagedThreadId", settle, StringComparison.Ordinal);
        Assert.Contains("onUi=\" + (Environment.CurrentManagedThreadId == _uiThreadId)", settle, StringComparison.Ordinal);
        Assert.DoesNotContain("offUi=True", code, StringComparison.Ordinal);   // 那颗写死的常量⛔ 许再回来冒充读数
        Assert.Contains("if (closingBeat) FinishClosingFlush();", settle, StringComparison.Ordinal);

        // (丁) 交出去那一刻抄下来的那颗位（⛔ 在续体里现问：那时它已经被清掉了）
        Assert.True(export.IndexOf("var closingBeat = _closingLanded is not null;", StringComparison.Ordinal)
                    < export.IndexOf("_ = SettleSceneWriteAsync(QueueSceneWrite(bytes), closingBeat);", StringComparison.Ordinal),
            "现问那颗位＝那一拍先醒了、这一发回来读到的是「没人等」＝那句 landed 与它后面的收尾一起歪");

        // 正件：判据照旧住在 UI 线程那一侧（⛔ 顺手把写闸搬走＝把 ✅P-223 换一副坏）
        Assert.Contains("if (!_autosaveArmed)", export, StringComparison.Ordinal);
        Assert.Contains("if (BoardPolicy.DiskAlreadyHolds(_lastWritten, bytes))", export, StringComparison.Ordinal);
        Assert.Contains("_seenScene = bytes;", export, StringComparison.Ordinal);
    }

    /// <summary>片58＝P-241 的②件：那一发进向字节<b>在解之前</b>按上限判，而那颗数字⛔ 住在宿主这一侧。
    /// <para><b>为什么"解之前"是这一格的全部意义</b>：<c>Convert.FromBase64String</c> 跑在 UI 线程上，
    /// 「解出来才知道多大」＝那一手的付担已经交了——真机上 25 MiB 那一发即便写盘已经离开 UI 线程，
    /// UI 线程照旧不肯收活 <b>273.6 ms</b>（正本 <c>bin/WI98/readings_58z_ladder2_221131.txt</c>；
    /// 那颗上限的三条出处在 <c>BoardPolicy.SceneBytesCeilingBytes</c> 的注释里）。</para>
    /// <para>四半：<b>(甲)</b> 问排在解之前，且全档只有一颗解码的门（多一颗＝多一处⛔ 带判据的入口）；
    /// <b>(乙)</b> 拒掉那一发⛔ 进盘、⛔ 进 <c>_seenScene</c>／<c>_refusedScene</c>（要它就得先解＝这一刀省的正是那一手），
    /// 也⛔ 静默；<b>(丙)</b> 两句分流——scene 说「这一扇已停止保存」那一族，png／svg 说「没能存成」那一族，
    /// 而说后者那一支必须<b>先返回</b>，否则同一发撞见两句话互相顶掉；
    /// <b>(丁)</b> 那一拍有人在等时，"拒"也要把它放行（P-54 那条口径：⛔ 把用户的退出键抵押给一发字节）。</para></summary>
    [Fact]
    public void TheInboundEnvelopeIsJudgedBeforeItIsDecoded()
    {
        var code = SourceGate.Code(ReadRepo(WindowCs));
        var export = SourceGate.MethodBody(code, "void HandleExportBytes");
        var refuse = SourceGate.MethodBody(code, "void RefuseOversizedEnvelope");

        // (甲) 那颗问排在解之前——两颗都要在，⛔ 只钉顺序（缺席的 IndexOf 返回 -1＝恒真，名册 447）
        Assert.Contains("BoardPolicy.InboundBytesOverCeiling(envelope.Bytes?.Length ?? 0)", export, StringComparison.Ordinal);
        Assert.Contains("Convert.FromBase64String", export, StringComparison.Ordinal);
        Assert.True(export.IndexOf("InboundBytesOverCeiling", StringComparison.Ordinal)
                    < export.IndexOf("Convert.FromBase64String", StringComparison.Ordinal),
            "解出来才问＝那一手的付担已经压在 UI 线程上了，这一刀压根⛔ 落地");
        Assert.Equal(1, Count(code, "Convert.FromBase64String"));   // 全档一颗解码的门

        // (乙) 拒掉那一发：只留一行痕，⛔ 摸盘、⛔ 记进手里那两份
        Assert.Contains("boards.scene.inbound.too-big", refuse, StringComparison.Ordinal);
        Assert.Equal(0, Count(refuse, "BoardSceneStore"));
        Assert.Equal(0, Count(refuse, "_seenScene"));
        Assert.Equal(0, Count(refuse, "_refusedScene"));
        Assert.Equal(0, Count(refuse, "QueueSceneWrite"));

        // (丙) 两句分流：png／svg 那一支说完"没能存成"就走，⛔ 再顶上一句"已停止保存"
        Assert.Contains("if (!BridgeProtocol.LandsInSceneArchive(envelope.Kind))", refuse, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.exportFailedBody\", null);", refuse, StringComparison.Ordinal);
        Assert.Equal(1, Count(refuse, "return;"));            // 那一支得<b>走掉</b>：说了两句＝用户以为这块板停了，而它其实⛔ 超限
        Assert.True(refuse.IndexOf("ReportExport(\"boards.exportFailedBody\", null);", StringComparison.Ordinal)
                    < refuse.IndexOf("ShowArmStuckNotice();", StringComparison.Ordinal),
            "两支说同一句话＝超限那张 png 被说成\"这一扇停止保存\"，用户会去等一颗根本不会再来的自动落盘");

        // (丁) 有人在等就得放行那一拍（与「写闸拒」那一格同一颗答案）
        Assert.Contains("_closingRefused = _closingLanded is not null;", refuse, StringComparison.Ordinal);
        Assert.Contains("FinishClosingFlush();", refuse, StringComparison.Ordinal);

        // 那颗数字⛔ 在这里第二份：宿主只问判据，上限住 Core
        Assert.Equal(0, Count(code, "8L * 1024 * 1024"));
        Assert.Equal(0, Count(code, "SceneBytesCeilingBytes ="));
        Assert.Equal(1, Count(code, "InboundBytesOverCeiling("));

        // 正件：没过上限那一发照旧走原路（⛔ 因为新加的门把合法那档一起挡在门外）
        Assert.Contains("if (BridgeProtocol.LandsInSceneArchive(envelope.Kind))", export, StringComparison.Ordinal);
        Assert.Contains("_ = SettleSceneWriteAsync(QueueSceneWrite(bytes), closingBeat);", export, StringComparison.Ordinal);
    }
}
