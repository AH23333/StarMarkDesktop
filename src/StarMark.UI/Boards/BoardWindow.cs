#nullable enable
using System;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using StarMark.Core.Boards;

namespace StarMark.UI.Boards;

/// <summary>
/// 画板那一扇窗（Phase C 片19＝§21 v1 的最小回路）：WebView2 虚拟宿主映随包资产、
/// board.html 挂引擎、消息进出全部过 Core 的 <see cref="BridgeProtocol"/>。
/// <para><b>生命周期档位＝§九 裁决 2「关窗即散」</b>：<see cref="Closed"/> 里 Close 掉
/// WebView2 控件（最后一扇走时浏览器进程树跟着散），userDataFolder 钉在
/// <c>%LOCALAPPDATA%\StarMark\wbview2</c>（v1 §坑8 那条改道）。</para>
/// <para><b>默认关</b>：本窗没有常驻入口，只有显式 <see cref="OpenNewAsync"/>／<see cref="OpenSceneAsync"/>；
/// 托盘那两颗（续上一块／新起一块）由接线批那颗总开关收放（本档⛔ 自己挂上去——"找不到人接又自己冒出来"是第二主人）。</para>
/// <para><b>回路现状</b>：bridge.ready／open.external／export.bytes 三颗全通；
/// paste.query／pin.request／background.request 回 <c>not-wired</c>——判定归各自批次（⛔ 假 allow）。</para>
/// <para><b>落盘与载入（片21＝§21.3）</b>：页面侧停笔 <see cref="BoardPolicy.AutosaveIdleMs"/> 毫秒自己交一份
/// 场景 JSON（<c>export.bytes</c>＋<c>kind:"scene"</c>，⛔ 新名字），宿主失焦那一拍再索取一次（<c>export.pick</c>）；
/// 字节经 <see cref="BridgeProtocol.LandsInSceneArchive"/> 分流进板档目录，png／svg 仍进导出目录。
/// <b>片22＝§21.4 那颗脏位</b>：交回来的字节与盘上那份<b>逐颗相等</b>时⛔ 写、⛔ 滚（<see cref="BoardPolicy.DiskAlreadyHolds"/>）；
/// 同批把 <c>export.bytes</c> 的 <c>suggested</c> 接上链——页面递 <c>appState.name</c>，只有导出那一路读它（板档档名＝那颗 id，⛔ 跟着显示名走）。</para>
/// <para><b>§21.3 那四拍都接上了线</b>。剩下那两拍各自能做的事⛔ 一样：
/// <b>关窗</b>那一拍走 <c>AppWindow.Closing</c>——拦一次、窗先 Hide（用户⛔ 等那两秒）、向页面索取一份快照并<b>有界</b>等它落地
/// （<see cref="BoardPolicy.CloseFlushBudgetMs"/>），到点照样关（⛔ 把用户的退出键抵押给一颗卡住的网页）；
/// <b>渲染进程挂了</b>那一拍<b>问不到</b>任何东西（页面已死，<see cref="BoardPolicy.BeatCanAskForSnapshot"/> 就为此而住 Core），
/// 它的活是按档恢复（<see cref="BoardPolicy.RecoveryForFailure"/>：重建控件／重载／只上报）＋ 桥第二次 ready 时把
/// <em>宿主见过的那一份</em>递回去——重载之后⛔ 许回到空板，也⛔ 许静默白屏（§21.5）。</para>
/// <para><b>片25 真机读数＋片26 落地</b>：那四拍里<b>失焦</b>与<b>关窗</b>两拍量到真落盘（片25：关窗后 0.55 s 盘上 3→4 元素），
/// <b>崩</b>那一拍两条恢复路都停在同一颗白帧＝<b>P-222 仍开放</b>（本档已给它配了 <c>boards.bridge.ready</c>／
/// <c>boards.nav.done</c>／<c>src=</c> 三处日志，先判因由再换手）；<b>停笔</b>那一拍原本⛔ 落过一根，
/// 因由＝页面侧把 <c>onChange</c> 的位置参数当"事件对象"读、判据恒假（<b>P-221</b>，片26 修完真机两跑各 1.50 s 落盘）。
/// 同批把"开闸"从一句<b>无参的话</b>换成<b>三颗数全等</b>：页面载完档回 <c>board.loaded{elements,canvas}</c>，
/// 宿主拿自己递进去的那颗数对完才递 <c>autosave.arm</c>，并打开<b>自己这一侧</b>那颗写闸
/// （<c>_autosaveArmed</c>，失焦／关窗那两拍由它挡）。改这一手的因由＝真机量到的<b>那颗数据丢失</b>（P-223）：
/// 重载之后页面那句"载完了"是真递出来的、画布却是空的，而盘上那块板随后被盖成 0 元素。</para>
/// <para>⚠ 已知绕过（归 P-220）：从<b>主窗</b>退出那一途读到的出口是 <c>Environment.Exit(0)</c>，
/// 它⛔ 经过各扇窗的 <c>Closing</c>，所以那一途上这拍一次也走不到。</para>
/// </summary>
public sealed class BoardWindow : Window
{
    /// <summary>虚拟宿主名（本类是它的唯一主人；闸门测试读这颗常量）。</summary>
    public const string VirtualHostName = "appassets.local";

    /// <summary>页面入口（相对随包根）。</summary>
    public const string BoardPageName = "board.html";

    /// <summary>userDataFolder 那条改道（v1 §坑8）：钉在 <c>%LOCALAPPDATA%</c> 之下。</summary>
    public const string UserDataFolderRelative = @"StarMark\wbview2";

    /// <summary>导出产物那颗落点（硬线①：落点由 C# 定，web 侧只交字节）。
    /// <para>它<b>住在板档目录之下</b>：那颗根只许有一位主人（<see cref="BoardSceneStore.Folder"/>），
    /// 这里只负责在它下面再切一颗 <c>exports\</c>——自己再拼一遍 <c>%APPDATA%\StarMark</c> 就是第二主人，
    /// 而那份抄本⛔ 认沙盒改道（片23 归一的就是这一族）。</para></summary>
    public static string ExportsDirectory => Path.Combine(BoardSceneStore.Folder, "exports");

    private Microsoft.UI.Xaml.Controls.WebView2 _web;
    private string _boardId;

    /// <summary>宿主<b>见过</b>的最近一份完整场景字节——桥每 ready 一次（首开、崩溃重载）就递它进画板。
    /// <para>它与 <see cref="_lastWritten"/> 问的⛔ 是同一件事：那一颗问"盘上主档此刻是哪份"（脏位归它），
    /// 这一颗问"重建起来该把什么放回去"。写失败的字节<b>也算见过</b>——用户那几笔不该因为盘上付不进就凭空退场。</para></summary>
    private byte[]? _seenScene;

    /// <summary>盘上主档那一刻是哪些字节（只有宿主自己写得动它，所以这一份状态有唯一主人）。
    /// null＝这扇窗还没写过、也没<b>从主档</b>载过（新板，或档是从 <c>.bak</c> 捡回来的）。</summary>
    private byte[]? _lastWritten;
    private bool _bridgeReady;

    /// <summary>关窗那一拍已经拦过一次（第二拍照常走下去——⛔ 让那扇窗永远关不掉）。</summary>
    private bool _closeFlushDone;

    /// <summary>已进入拆窗过程（含那次有界等待）：这一段时间里来的进程失效消息<b>只上报</b>，⛔ 再往一颗正在拆的窗上重建控件。</summary>
    private bool _tearingDown;

    /// <summary>关窗那一拍在等的这份快照：页面交回来（写成功、写失败、还是"一个字节都没变"）那一刻结掉。</summary>
    private System.Threading.Tasks.TaskCompletionSource<bool>? _closingLanded;

    /// <summary>恢复开的那一趟跑没跑完（两趟同时开着＝两颗控件抢同一扇窗）。</summary>
    private bool _recovering;

    /// <summary>这一扇窗已经试过几次"重建控件"（上限的来历见 <see cref="BoardPolicy.MaxRecreatePerWindow"/>）。</summary>
    private int _recreateTries;

    /// <summary>桥到第几次 ready 了（首开＝1，崩溃重载＝2、3…）。它＋nav.done 那两行是 P-222 的现场尺。</summary>
    private int _readySeq;

    /// <summary>
    /// 这一趟页面<b>有没有资格往板档上写</b>（片26 给 P-223 的那道门，宿主这一侧的副本）。
    /// <para><b>为什么页面那颗 <c>hostArmed</c> ⛔ 够</b>：它包在 <c>onChange</c> 里，只挡停笔那一路；
    /// 而<b>失焦</b>与<b>关窗</b>两拍是宿主问、页面答（<c>export.pick</c>→<c>runExport</c>→<c>deliver</c>），
    /// 现读那一路上⛔ 有这道闸——写盘的那支笔本来就握在宿主手里，闸门就⛔ 只装在对方身上。</para>
    /// <para>每一趟 <c>bridge.ready</c> 都从头算（那是"新页面诞生"的唯一时刻）：闸门的证据由那一趟的
    /// <c>board.loaded</c> 那两颗数给（<see cref="BoardPolicy.ShouldArmAfterLoad"/>）。</para>
    /// </summary>
    private bool _autosaveArmed;

    public BoardWindow()
    {
        _boardId = BoardPolicy.NewSceneId(DateTimeOffset.Now, Random.Shared.Next(0, 0x10000));
        Title = "StarMark Boards";
        _web = NewWeb();
        Content = _web;
        Closed += (_, _) => OnClosed();
        AppWindow.Closing += OnAppWindowClosing;
        // 失焦那一拍（§21.3 四拍其二）：向页面索取一份场景快照。⛔ 在这里判"要不要写"——
        // 写不写由 Core 的档位分流说了算，这一处只负责"该问一次了"。
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) RequestSceneSnapshot(BoardPolicy.SaveBeat.LosingFocus);
        };
    }

    /// <summary>那颗板 id（<see cref="BoardPolicy.NewSceneId"/> 的形状；宿主是它的主人）。</summary>
    public string BoardId => _boardId;

    /// <summary>起一扇新板（没带任何档）。</summary>
    public System.Threading.Tasks.Task OpenNewAsync()
    {
        _lastWritten = null;   // 盘上还没有这颗板的档：第一拍照常规写，别把"还没写过"读成"已经一样"
        return OpenAsync(null);
    }

    /// <summary>
    /// 接着开那颗已有的板：先按 id 把档读进内存，等 <c>bridge.ready</c> 那一刻再递给页面。
    /// <para>顺序是反不得的：桥没通就发 <c>board.load</c>，页面侧那位 <c>API</c> 还是 null，
    /// 场景就永远进不去（而宿主这边一句错都⛔ 报）。档读不到＝退回新板，⛔ 弹"档不见了"那种废话。</para>
    /// <para>从 <c>.bak</c> 捡回来的那一档⛔ 点亮脏位：主档位置上是空的，所以"盘上已经有了"那句⛔ 成立，
    /// 得让第一拍把主档补回去（否则那颗板在 <see cref="BoardSceneStore.LatestSceneId"/> 那一问里始终是缺的）。</para>
    /// </summary>
    public System.Threading.Tasks.Task OpenSceneAsync(string sceneId)
    {
        if (!BoardSceneStore.TryLoad(sceneId, out var bytes, out var fromBackup) || bytes.Length == 0) return OpenNewAsync();
        _boardId = sceneId;
        _lastWritten = fromBackup ? null : bytes;
        return OpenAsync(bytes);
    }

    /// <summary>那条唯一的"开一扇页"的路：首开与崩溃后的重建都走它（两份开场代码＝第二个主人）。</summary>
    private async System.Threading.Tasks.Task OpenAsync(byte[]? sceneBytes)
    {
        _seenScene = sceneBytes;
        _bridgeReady = false;   // 每一趟开场都从头算：上一趟的 ready⛔ 顶这一趟的
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UserDataFolderRelative);
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userData, new CoreWebView2EnvironmentOptions());
        await _web.EnsureCoreWebView2Async(env);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Boards");
        if (!Directory.Exists(assets))
            throw new InvalidOperationException("随包资产不在：" + assets);
        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHostName, assets, CoreWebView2HostResourceAccessKind.Allow);
        // 先摘再挂：重建那一趟用的是新控件，而同一颗控件被重开那一途（恢复）再进来时⛔ 要两颗订阅。
        _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
        _web.CoreWebView2.ProcessFailed += OnProcessFailed;
        _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        _web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _web.CoreWebView2.Navigate("https://" + VirtualHostName + "/" + BoardPageName);
    }

    /// <summary>
    /// 那一扇页到底落没落地（P-222 的现场尺其一）：崩之后无论走 Reload 还是重建控件，这一行都该再来一次，
    /// 而 <c>ok</c>／<c>err</c> 就是那三个候选因由的分岔——"重载回来了个错误文档"与"回来了却没载档"在这一行上不同形。
    /// </summary>
    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        => StarMark.Abstractions.StarLog.Info(
            "boards.nav.done seq=" + _readySeq + " ok=" + e.IsSuccess + " err=" + e.WebErrorStatus + " src=" + sender.Source);

    /// <summary>桥是否已就绪（页面递过 <c>bridge.ready</c>）。</summary>
    public bool BridgeReady => _bridgeReady;

    /// <summary>宿主主动发那颗 C#→JS 事件（键集先过 <see cref="BridgeProtocol.Check"/> 的出向门）。</summary>
    public void PostOutbound(string name, string json)
    {
        if (_web.CoreWebView2 is null) return;
        var envelope = BoardEnvelope.Parse(json);
        var (allowed, _) = BridgeProtocol.Check(
            envelope.Version, name, envelope.ArgKeys, BridgeProtocol.Direction.Outbound);
        if (!allowed) return;
        _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var envelope = BoardEnvelope.Parse(e.WebMessageAsJson ?? string.Empty);
        var (allowed, fault) = BridgeProtocol.Check(
            envelope.Version, envelope.Cmd, envelope.ArgKeys, BridgeProtocol.Direction.Inbound);
        if (!allowed)
        {
            Reply(envelope.Cmd, envelope.Id, ok: false, err: "fault:" + fault);
            return;
        }

        switch (envelope.Cmd)
        {
            case "bridge.ready":
                _bridgeReady = true;
                Reply(envelope.Cmd, envelope.Id, ok: true);
                OnBridgeReady();
                break;
            case "board.loaded":
                // 页面报"那份档落进画布了"＋两颗数。开闸只认<b>数</b>：宿主递进去几颗、档里读到几颗、画布上现在几颗，
                // 三颗全等才算"回到那块板"（片26 量到的是"promise 解掉了、页面回了话，而画布是空的"⇒ 那句话本身⛔ 是证据）。
                var want = BoardPolicy.CountWritableElements(_seenScene);
                if (BoardPolicy.ShouldArmAfterLoad(want, envelope.Elements, envelope.Canvas))
                {
                    Reply(envelope.Cmd, envelope.Id, ok: true);
                    ArmIdleBeat("loaded", " want=" + want + " loaded=" + envelope.Elements + " canvas=" + envelope.Canvas);
                }
                else
                {
                    StarMark.Abstractions.StarLog.Warn(
                        "boards.arm.refused want=" + want + " loaded=" + envelope.Elements + " canvas=" + envelope.Canvas
                        + " id=" + _boardId);
                    Reply(envelope.Cmd, envelope.Id, ok: false, err: "arm-refused");
                }
                break;
            case "open.external":
                if (!BridgeProtocol.IsSafeExternalUrl(envelope.Url))
                {
                    Reply(envelope.Cmd, envelope.Id, ok: false, err: "url-rejected");
                    break;
                }
                Reply(envelope.Cmd, envelope.Id, ok: true);
                _ = Helpers.LauncherEx.TryOpenAsync(envelope.Url);
                break;
            case "export.bytes":
                HandleExportBytes(envelope);
                break;
            default:
                // paste.query / pin.request / background.request：回路通了、判定归各自批次（⛔ 假 allow）
                Reply(envelope.Cmd, envelope.Id, ok: false, err: "not-wired");
                break;
        }
    }

    private void HandleExportBytes(BoardEnvelope envelope)
    {
        if (!BridgeProtocol.IsKnownExportKind(envelope.Kind))
        {
            Reply(envelope.Cmd, envelope.Id, ok: false, err: "fault:UnknownName");
            return;
        }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(envelope.Bytes ?? string.Empty); }
        catch (FormatException)
        {
            Reply(envelope.Cmd, envelope.Id, ok: false, err: "bridge-broken");
            return;
        }
        // 那颗档位决定落点（判据住 Core）：scene＝这颗板的本体→板档目录、档名＝那颗 id、滚 .bak；
        // png／svg＝一次性产物→导出目录、名字带时刻。两族⛔ 共用一份命名规则。
        if (BridgeProtocol.LandsInSceneArchive(envelope.Kind))
        {
            // 写闸（P-223）：这一趟页面还没证明自己"回到了宿主给它的那块板"，它交回的场景字节就⛔ 许落盘。
            // ⛔ 挡的只是这一族——png／svg 是一次性产物、另起一颗档名，盖不到任何板档（所以那颗门在档位分流之后）。
            // 拒掉的字节也⛔ 记进 _seenScene：记了＝下一趟恢复会把这块空场景当"见过的那一份"递回去，
            // 那才是把一次白屏变成永久丢板的一手。
            if (!_autosaveArmed)
            {
                StarMark.Abstractions.StarLog.Warn(
                    "boards.scene.write.refused notArmed bytes=" + bytes.Length + " id=" + _boardId);
                Reply(envelope.Cmd, envelope.Id, ok: false, err: "not-armed");
                FinishClosingFlush();   // 关窗那一拍等的是"这一问有答案"，拒掉也是答案——⛔ 让用户白等那两秒
                return;
            }
            // 崩那一拍要递回去的就是这一颗：交回来的那一刻起它就是"宿主见过的那一份"，⛔ 等盘上付进才认。
            _seenScene = bytes;
            // §21.4 那颗脏位＝宿主这一侧的字节对差：一个字节都没变（失焦那一拍最常撞这一档）就⛔ 动盘——
            // 动一次盘就多滚一次 .bak，而滚的那一下把上一份「真正不同」的退路顶成了同件。
            if (BoardPolicy.DiskAlreadyHolds(_lastWritten, bytes))
            {
                Reply(envelope.Cmd, envelope.Id, ok: true);
                FinishClosingFlush();
                return;
            }
            if (BoardSceneStore.TrySave(_boardId, bytes, out var fault))
            {
                _lastWritten = bytes;   // 这一刻起盘上主档就是这份字节
                Reply(envelope.Cmd, envelope.Id, ok: true);
            }
            else
            {
                StarMark.Abstractions.StarLog.Warn("boards.scene.save.failed: " + fault);
                Reply(envelope.Cmd, envelope.Id, ok: false, err: "save-failed");
            }
            // 关窗那一拍等的就是"这一问有答案了"，而⛔ 是"答案是好听的那颗"：写失败也当场结掉，
            // 让用户再等满那两秒⛔ 能多换回一个字节。
            FinishClosingFlush();
            return;
        }
        var stem = _boardId + "-" + BoardPolicy.FormatStamp(DateTime.Now);
        var name = BridgeProtocol.SanitizeExportName(envelope.Suggested, stem, envelope.Kind);
        Directory.CreateDirectory(ExportsDirectory);
        File.WriteAllBytes(Path.Combine(ExportsDirectory, name), bytes);
        Reply(envelope.Cmd, envelope.Id, ok: true);
    }

    private void OnBridgeReady()
    {
        _readySeq++;
        // 新页面诞生＝写闸重新闩上：这一趟的证据要由这一趟的 board.loaded 重新给（崩之前开过闸⛔ 顶这一趟）。
        _autosaveArmed = false;
        // 片26 诊断面（P-222）：崩之后那两条恢复路都停在同一颗白帧，而"后一手真发出去了"与"页面真回来了"
        // 是两件事。这一行＋下面那颗 nav.done 各给一次读数，才把"Reload 选了错的一手"与"重载回来了却没载档"
        // 判得开（⛔ 靠猜换手）。
        StarMark.Abstractions.StarLog.Info(
            "boards.bridge.ready seq=" + _readySeq + " id=" + _boardId + " hasScene=" + (_seenScene is { Length: > 0 }));
        // 三颗初始值（§21.5）：宿主是它们的主人；占位档先发中性值，接设置面归后续批次。
        PostOutbound("theme.set", Envelope("theme.set", "h1", "\"theme\":\"light\""));
        PostOutbound("lang.set", Envelope("lang.set", "h2", "\"langCode\":\"zh-CN\""));
        // 首开与崩溃重载共用这一条：这一刻才把档递进去（早一步页面那位 API 还没起来，晚一步没有读者）。
        // 见过一份就递一份——§21.5 那句「重载之后⛔ 许回到空板」要的正是这一颗。
        // 停笔那一拍的闸门由"档到底落没落进画布"那<b>三颗数</b>开（P-221→P-223）。有档可载＝等页面回 board.loaded
        // （它自己才知道落没落），新板＝当场开闸。⚠ 两条路都得发出去：漏发＝这一扇永远⛔ 写，
        // 那是把 P-221 换成一条更静的坏；而"没落地就开闸"＝两跑各自量到的那颗数据丢失
        // （片25 挂载那一拍：4 元素→241 字节／0 元素；片26 重载那一拍：2608 字节／2 元素→241 字节／0 元素）。
        if (_seenScene is { Length: > 0 } bytes)
        {
            PostOutbound("board.load", Envelope("board.load", "h3",
                JsonString("bytes") + ":" + JsonString(Convert.ToBase64String(bytes)) + ","
                + JsonString("id") + ":" + JsonString(_boardId)));
        }
        else
        {
            ArmIdleBeat("no-scene");
        }
    }

    /// <summary>
    /// 递那颗"此后画布上的变化都算用户改的"（<c>autosave.arm</c>，无参＝它报的是时刻），
    /// 并同时打开<b>宿主这一侧</b>那颗写闸——从这一刻起，这一趟页面交回的场景字节才许落盘。
    /// <para><paramref name="evidence"/> 只进日志：开闸这件事必须留得下"凭什么开的"那一颗读数。</para>
    /// </summary>
    private void ArmIdleBeat(string via, string evidence = "")
    {
        _autosaveArmed = true;
        StarMark.Abstractions.StarLog.Info("boards.arm.post via=" + via + evidence + " id=" + _boardId);
        PostOutbound("autosave.arm", Envelope("autosave.arm", "h5", string.Empty));
    }

    /// <summary>向页面索取一份场景快照（四拍里能问的那两拍走这里；页面侧回 <c>export.bytes</c>＋<c>kind:"scene"</c>）。</summary>
    private void RequestSceneSnapshot(BoardPolicy.SaveBeat beat)
    {
        if (!_bridgeReady) return;                                    // 桥没通就索取＝发进一颗没人听的口子（回音都⛔ 有）
        if (!BoardPolicy.BeatCanAskForSnapshot(beat)) return;         // 崩那一拍：页面已经死了，问也问不到东西
        PostOutbound("export.pick", Envelope("export.pick", "h4", JsonString("kind") + ":" + JsonString("scene")));
    }

    /// <summary>
    /// 关窗那一拍（§21.3 四拍其四）：拦一次→Hide→索取快照→<b>有界</b>等它落地→真关。
    /// <para>拦一次就够：第二次拍照常走下去，否则那颗等待本身变成"那扇窗关不掉"。</para>
    /// </summary>
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _tearingDown = true;                  // 从这里起的进程失效消息只上报，⛔ 再往拆着的窗上重建控件
        if (_closeFlushDone || !_bridgeReady) return;
        _closeFlushDone = true;
        args.Cancel = true;
        AppWindow.Hide();                     // 视觉上先退场：那两秒⛔ 挂在用户的手指上
        _ = FlushThenCloseAsync();
    }

    private async System.Threading.Tasks.Task FlushThenCloseAsync()
    {
        var landed = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        _closingLanded = landed;
        RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow);
        var budget = System.Threading.Tasks.Task.Delay(BoardPolicy.CloseFlushBudgetMs);
        if (await System.Threading.Tasks.Task.WhenAny(landed.Task, budget) != landed.Task)
            StarMark.Abstractions.StarLog.Warn("boards.close.flush.timeout ms=" + BoardPolicy.CloseFlushBudgetMs);
        _closingLanded = null;
        Close();
    }

    /// <summary>那份快照有答案了（写进盘、盘上本来就是它、还是写不进去）：结掉关窗那一拍的等待。</summary>
    private void FinishClosingFlush() => _closingLanded?.TrySetResult(true);

    /// <summary>
    /// 进程失效那一拍（§21.3 四拍其三／§21.5）：先复位那颗"桥通了"——⛔ 它现在还是 true，
    /// 下一拍失焦就会往一颗死控件 <c>PostWebMessageAsJson</c>（COM 异常当场抛在 UI 线程上）。
    /// 然后按 Core 分的档决定付哪一手；<b>没一颗档位是"把画板退成空白"</b>。
    /// </summary>
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var tier = BoardPolicy.RecoveryForFailure(e.ProcessFailedKind.ToString());
        StarMark.Abstractions.StarLog.Warn(
            "boards.process.failed kind=" + e.ProcessFailedKind + " tier=" + tier
            + " src=" + (_web.CoreWebView2?.Source ?? "<null>") + " readySeq=" + _readySeq);
        _bridgeReady = false;
        if (_tearingDown || tier == BoardPolicy.FailureRecovery.ReportOnly) return;
        if (tier == BoardPolicy.FailureRecovery.ReloadPage)
        {
            _web.CoreWebView2?.Reload();     // 控件还在：重载那一扇页，bridge.ready 会再来一次
            return;
        }
        if (_recovering) return;             // 两颗控件抢同一扇窗＝更坏的现场
        if (_recreateTries >= BoardPolicy.MaxRecreatePerWindow)
        {
            StarMark.Abstractions.StarLog.Warn("boards.recover.givenup tries=" + _recreateTries);
            return;
        }
        _recreateTries++;
        _recovering = true;
        RecoverByRecreatingWeb();
    }

    /// <summary>
    /// 官方表对那一档写的是"要重建一颗 WebView 才恢复得了"：换掉控件、走同一条 <see cref="OpenAsync"/>。
    /// <para>旧那颗自己已经进了 Closed 态（doc 逐字），这里⛔ 再 Close 它一次，也⛔ 对着它 Navigate；
    /// 恢复带着 <see cref="_seenScene"/>，所以用户看到的⛔ 是白屏，而是他刚才那块板。</para>
    /// </summary>
    private void RecoverByRecreatingWeb()
    {
        _web = NewWeb();
        Content = _web;
        _ = OpenRecoverAsync();
    }

    private async System.Threading.Tasks.Task OpenRecoverAsync()
    {
        try { await OpenAsync(_seenScene); }
        catch (Exception ex) { StarMark.Abstractions.StarLog.Error("boards.recover.failed", ex); }
        finally { _recovering = false; }
    }

    private static Microsoft.UI.Xaml.Controls.WebView2 NewWeb()
        => new() { DefaultBackgroundColor = Microsoft.UI.Colors.White };

    private static string Envelope(string cmd, string id, string argsJson)
        => "{" + JsonString("v") + ":1," + JsonString("cmd") + ":" + JsonString(cmd)
           + "," + JsonString("id") + ":" + JsonString(id)
           + "," + JsonString("args") + ":{" + argsJson + "}}";

    private void Reply(string? cmd, string? id, bool ok, string? err = null)
    {
        if (_web.CoreWebView2 is null) return;
        var errPart = err is null ? string.Empty : "," + JsonString("err") + ":" + JsonString(err);
        var json = """{"v":1,""" + JsonString("cmd") + ":" + JsonString(cmd)
                   + "," + JsonString("id") + ":" + JsonString(id)
                   + "," + JsonString("ok") + ":" + (ok ? "true" : "false") + errPart + "}";
        _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private static string JsonString(string? s) => System.Text.Json.JsonSerializer.Serialize(s);

    private void OnClosed()
    {
        // 决策 2「关窗即散」：页面侧收尾在 web 半边；宿主半边这里拆干净（最后一扇走＝浏览器进程树散）。
        _tearingDown = true;
        if (_web.CoreWebView2 is not null)
        {
            _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
            _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
        _web.Close();
    }
}
