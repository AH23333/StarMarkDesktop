#nullable enable
using System;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
/// <para><b>片27＝那道闸门不再只说话给日志听</b>（P-224）：拒一次先<b>自己再递一次档</b>
/// （额度＝<see cref="BoardPolicy.ArmRetryBudget"/>，一次），仍对不上才开口，而开口那一句⛔ 只住在日志里——
/// 画布右下那块面板当场说"这块板没能回到画布上／这一扇已停止保存"，并给一颗<b>另存一份到别处</b>的按钮
/// （宿主自己写盘，⛔ 走 web 侧；系统"另存为"起不来就退到导出目录并把那条路径念给用户）。
/// 同批把§21.5 剩下那半也接上：决定动手恢复的那两档（重载／重建控件）当场贴<b>"这块板正在恢复"</b>，
/// 重建那一趟换的是 <see cref="_stage"/> 里的孩子而⛔ 重指 <c>Content</c>（否则面板连话一起被摘走），
/// 三圈用尽那档（<c>boards.recover.givenup</c>）当场关掉写闸并上屏那一句＋那颗按钮；
/// <b>关窗那一拍超时</b>走右下角那张卡（窗在这一拍开头就 Hide 了，面板⛔ 来得及看见）。
/// ⚠ 残留：那块面板的<b>脸面</b>（灰底黑字、它压住画布右下角那一带）⛔ 屏幕量过；
/// 那颗按钮真机也⛔ 按过——拒闸门在真机一次也没撞到（见片26 的如实登记），本批只补了形状证人与纯函数证人。</para>
/// <para>⚠ 已修（P-220＝片30）：从<b>主窗</b>退出那一途的出口确实是 <c>Environment.Exit(0)</c>，它⛔ 经过各扇窗的
/// <c>Closing</c>——真机两臂量到（同一份码、同一起点）：关画板窗那一臂盘上多出那颗档（<c>…-be4b</c>，241 字节），
/// 而关主窗那一臂（托盘两颗开关都关着）<b>那颗板压根没落盘</b>（<c>…-26bf</c> 只有 <c>bridge.ready</c>／
/// <c>arm.post</c> 两行，收尾之后进程 1 秒内退完）。修法＝<see cref="BoardsWindows"/> 那份名册由
/// 宿主那条收尾链（<c>ExitApp</c>）在第一步请一次，两条来路共用同一颗有界等待（<see cref="FlushSnapshotAsync"/>）。
/// 两臂<b>改前</b>的正本＝<c>bin/WI90/readings_30_board_pre.txt</c>／<c>readings_30_main_pre.txt</c>（旧码在成功那一发⛔ 说话，所以那两份只有档名册这一把尺）；<b>改后</b>同一腿重跑＝<c>readings_30_main_post.txt</c>：盘上多 <c>…-065c</c> 那份，而日志有了
/// <c>boards.close.flush.exit … landed=True</c>（本批起两条来路<b>各有一句</b>落地话，⛔ 再只在超时时才吭声）。
/// <b>片31 把那一格补量了（✅P-229）</b>：同一块板先接住那颗 2 元素种子档（<c>STARMARK_START_BOARD=last</c>，
/// ready 那行逐字 <c>hasScene=True</c>），在画布上拉一笔，抬笔 <b>0.08 s</b> 之后关<b>主窗</b>（那颗停笔去抖＝1.5 s，
/// 这一发⛔ 等满它）⇒盘上那颗档由 <c>858 字节／2 颗</c> 变成 <c>2708／3 颗</c>，新进那颗逐字 <c>freedraw</c>、
/// 包围盒 <c>316×105</c>＝我这一笔按那颗窗口（1440×879）折算的数——⛔ 只念那颗 <c>landed=True</c>：
/// 它为真也可能是"盘上本来就是它"那一档。正本＝<c>bin/WI90/readings_31_main_115116.txt</c>，
/// 对照臂（同一份脚本、走 <c>flush.window</c>）＝<c>readings_31_board_115054.txt</c>。</para>
/// <para><b>片32 补上那颗 ⚛ 的第一格，并顺手量到一颗真缺陷（✅P-230）</b>：钩子改收多颗写法
/// （<c>STARMARK_START_BOARD=new,new</c>）⇒两扇同开、各一笔再各补一笔，然后<b>立刻</b>关<b>主窗</b>。
/// 名册那一趟<b>两扇都落地</b>＝<c>落地 2 扇</c>＋逐扇各报一颗 <c>tookMs</c>；那句"并发等＝最坏那一扇决定总时长"
/// 第一次有读数（<c>readings_32_main_125501.txt</c>：总 <b>163 ms</b> ↔ 最坏 <b>153</b> ↔ 之和 <b>305</b>，复跑 125541 同形）。
/// 而<b>改前那两跑</b>（<c>readings_32_main_123806.txt</c>／<c>124513.txt</c>）里，最后画那一扇的<b>那一笔没进盘</b>
/// （那颗档停在 1444 字节／freedraw 1 颗），两句 <c>landed=True tookMs=[13, 13]／[15, 15]</c> 却一切正常＝
/// 抬笔后 <b>0～20 ms</b> 内问，那一下输入还排在"系统队列→宿主→浏览器进程→渲染进程"那条链上，页面交回的是<b>还没有那一笔</b>的旧场景，
/// 而宿主那一侧<b>听不出新旧</b>（源码对得上：<c>DiskAlreadyHolds</c> 那一支把"落地"与"什么都没新落"写成同一句话；
/// 改前那两跑读不到 <c>same=</c> 这一列＝它是本批补的，所以"那一发走的就是这一支"在改前读数里⛔ 单独点过名），
/// 于是那一行连"读到的是旧账"都说不出口。
/// 门槛量在 20～40 ms 之间（40／80／150／250／300 ms 各一跑全进），settle 臂（⛔ 关窗、只等那一拍自己落盘）
/// 先证明那一笔<b>画得上</b>（0.25 s／1.77 s 两颗都进）⇒修法＝开口前先让 <see cref="BoardPolicy.CloseFlushGraceMs"/>，
/// 两条来路共用同一颗 <see cref="FlushSnapshotAsync"/>；改后"抬笔即关"两跑各 2/2（最后那一扇逐字 <c>same=False tookMs=153</c>）。
/// ⚠ 这一手把丢笔压到量不到的档，⛔ 是"保证渲染进程收下了"——页面没有"我处理到第几笔"可问，宿主也⛔ 知道这块板上有几笔。</para>
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

    /// <summary>
    /// 那一拍等回来的那份字节<b>是不是盘上本来就有</b>（只进日志，判据住 <c>BoardPolicy.DiskAlreadyHolds</c>）。
    /// <para>为什么要单独报这一颗：<c>landed=True</c> 有三种真法（片31 记过一次），其中"盘上本来就是它"与"把同件又写一遍"
    /// 本来就长得一样，所以一份<b>还没有那一笔的旧场景</b>在<b>抬笔后立刻关窗</b>时也说成落地成功
    /// （片32 量到：隔 0～20 ms 关＝丢最后那一笔，而两颗 <c>landed=True</c> 都报好话——改前那两跑读不到 <c>same=</c> 这一列，
    /// "丢笔那一发走的到底是哪一档"在它们里⛔ 单独读数）。这一颗把那一下从"看不见的同件"变成日志里读得出的一列。</para>
    /// </summary>
    private bool _closingSameAsDisk;

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

    /// <summary>这一趟载档被拒过几次（额度＝<see cref="BoardPolicy.ArmRetryBudget"/>；开闸成功就归零）。</summary>
    private int _armTries;

    /// <summary>画布与那块面板同住的一层（<see cref="Content"/> 只许有一个主人：重建控件那一趟换的是这层里的孩子）。</summary>
    private readonly Grid _stage;
    private Border? _notice;
    private TextBlock? _noticeTitle;
    private TextBlock? _noticeBody;
    private Button? _noticeButton;

    /// <summary>那块面板此刻说的那一句住在表里的哪一颗键（null＝没在说话）。<b>换语言那一拍按它重贴</b>（机制 (C)）。</summary>
    private string? _noticeKey;

    /// <summary>那颗"另存一份到别处"的按钮这这一刻许不许按（另存正进行中＝⛔ 再按一次，那是第二份写入）。</summary>
    private bool _copying;

    /// <summary>另存到了哪儿（那句"已另存到 {0}"的槽；⛔ 进表，因为它是<b>数据</b>而⛔ 句子）。</summary>
    private string? _copyPath;

    public BoardWindow()
    {
        _boardId = BoardPolicy.NewSceneId(DateTimeOffset.Now, Random.Shared.Next(0, 0x10000));
        Title = "StarMark Boards";
        _web = NewWeb();
        _stage = BuildStage();
        Content = _stage;
        Closed += (_, _) => OnClosed();
        AppWindow.Closing += OnAppWindowClosing;
        // 名册那一头也在这里挂上：退出那一拍（`Environment.Exit` 那一步⛔ 经过任何一扇窗的 Closing）
        // 只有先知道有哪几扇开着，才问得到。摘在 <see cref="OnClosed"/>——与那颗静态事件同一副对子。
        BoardsWindows.Add(this);
        // 换语言那一刻，那块正开着的面板要跟着换话（⛔ 冻在第一趟的语言上——它说的可是"你的板没在保存"）。
        // 订阅挂在构造、摘在 <see cref="OnClosed"/>：静态事件上不摘＝这一窗的元素永远活着（P-198 那一族同一颗规矩）。
        StarMark.Abstractions.I18n.Locale.Changed += OnLocaleChanged;
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
                        + " id=" + _boardId + " tries=" + _armTries);
                    Reply(envelope.Cmd, envelope.Id, ok: false, err: "arm-refused");
                    // 先自己再递一次（那是一句自愈），额度用完了才开口：⛔ 一拒就喊，也把"其实再递一次就好"那种一次性时序
                    // 报成坏掉；而⛔ 一直递＝把同一块空画布反复刷给用户看。两条都⛔ 把决定推给用户（重启／看日志）。
                    if (BoardPolicy.ShouldRetryArm(_armTries) && _seenScene is { Length: > 0 })
                    {
                        _armTries++;
                        StarMark.Abstractions.StarLog.Warn("boards.arm.retry n=" + _armTries + " id=" + _boardId);
                        PostBoardLoad();
                    }
                    else
                    {
                        ShowArmStuckNotice();
                    }
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
                // 这一路与"停笔那一拍没开闸"是同一条静默面，而它更坏：用户已经画下的那几笔<b>一颗也没落盘</b>。
                ShowArmStuckNotice();
                FinishClosingFlush();   // 关窗那一拍等的是"这一问有答案"，拒掉也是答案——⛔ 让用户白等那两秒
                return;
            }
            // 崩那一拍要递回去的就是这一颗：交回来的那一刻起它就是"宿主见过的那一份"，⛔ 等盘上付进才认。
            _seenScene = bytes;
            // §21.4 那颗脏位＝宿主这一侧的字节对差：一个字节都没变（失焦那一拍最常撞这一档）就⛔ 动盘——
            // 动一次盘就多滚一次 .bak，而滚的那一下把上一份「真正不同」的退路顶成了同件。
            if (BoardPolicy.DiskAlreadyHolds(_lastWritten, bytes))
            {
                // 只有"这一份字节正是关窗那一拍在等的那一份"才配记进那一行：停笔／失焦那一拍也常撞到同件，
                // 让它们把这一列拨成 true＝把一次无关的等价写说成"那一拍读到旧账"。
                _closingSameAsDisk = _closingLanded is not null;
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
        if (_seenScene is { Length: > 0 }) PostBoardLoad();
        else ArmIdleBeat("no-scene");
    }

    /// <summary>
    /// 把"宿主见过的那一份"递进画布（首开、崩溃重建、开闸被拒后的那一次重试，<b>三条路共用这一颗形状</b>——
    /// 两份递档代码＝第二个主人，其中一份忘了递档是本仓定死的第一号坏法）。
    /// </summary>
    private void PostBoardLoad()
    {
        if (_seenScene is not { Length: > 0 } bytes) return;
        PostOutbound("board.load", Envelope("board.load", "h3",
            JsonString("bytes") + ":" + JsonString(Convert.ToBase64String(bytes)) + ","
            + JsonString("id") + ":" + JsonString(_boardId)));
    }

    /// <summary>
    /// 递那颗"此后画布上的变化都算用户改的"（<c>autosave.arm</c>，无参＝它报的是时刻），
    /// 并同时打开<b>宿主这一侧</b>那颗写闸——从这一刻起，这一趟页面交回的场景字节才许落盘。
    /// <para><paramref name="evidence"/> 只进日志：开闸这件事必须留得下"凭什么开的"那一颗读数。</para>
    /// </summary>
    private void ArmIdleBeat(string via, string evidence = "")
    {
        _autosaveArmed = true;
        _armTries = 0;                 // 开过闸，这一趟的额度就没意义了：下一次拒是新的一趟（⛔ 让旧额度累积）
        HideNotice();                  // "正在恢复"／"这一扇已停止保存"那两句都退场——它们说的已经不是事实
        StarMark.Abstractions.StarLog.Info("boards.arm.post via=" + via + evidence + " id=" + _boardId);
        PostOutbound("autosave.arm", Envelope("autosave.arm", "h5", string.Empty));
    }

    /// <summary>那一扇画板窗的右下那块面板：画布之上唯一的一颗主人（本方法把它排在 web 之后＝压在画布上）。</summary>
    private Grid BuildStage()
    {
        _noticeTitle = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Visibility = Visibility.Collapsed,
        };
        _noticeBody = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            MaxWidth = 420,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        _noticeButton = new Button
        {
            Content = Word("boards.saveCopyAction"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            // ⚠ 这一颗⛔ 只钉面板：2026-10-19 真机读数（§27.6 那条"⛔ 验到"当场兑现）——面板两行黑字量得到
            // （标题那一行 10.3% 深色像素），而那颗按钮整框最暗只到 #fafafa、字是 #ffffff ⇒ <b>白字画在灰底上＝看不见</b>。
            // 它是这一屏上唯一可点的那一颗，"看得见"是它存在的最低条件 ⇒ 底色／字色／描边三样一起钉死，
            // 与面板同一条口径：两边都⛔ 跟着主题走。
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _noticeButton.Click += async (_, _) => await CopySeenSceneAsideAsync();
        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
        panel.Children.Add(_noticeTitle);
        panel.Children.Add(_noticeBody);
        panel.Children.Add(_noticeButton);
        // 那块面板的底色与字色都<b>钉死</b>：画布那一侧永远是白板（theme.set 现在交的是 light），
        // 而一扇窗里两套深浅色同时说话就是 WI-81 那族"点了白色整扇看不见"的形状——两边都不跟着主题走，最坏也只是普通灰底。
        _notice = new Border
        {
            Child = panel,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 250, 250, 250)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 178, 178, 178)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 16),
        };
        var stage = new Grid();
        stage.Children.Add(_web);
        stage.Children.Add(_notice);
        return stage;
    }

    /// <summary>崩那一拍决定要动手（重载或重建）：那 2 s 的白帧从此有句话（§21.5"⛔ 许静默白屏"剩下的那一半）。</summary>
    private void ShowRecoveringNotice() => ShowNotice("boards.recoveringBody", titled: false, offerCopy: false);

    /// <summary>那颗写闸拒到底了：把"这一扇不再落盘"说出来，并把唯一还能救的那一份递给用户（P-224 的 (A)）。</summary>
    private void ShowArmStuckNotice()
        => ShowNotice("boards.stuckBody", titled: true, offerCopy: _seenScene is { Length: > 0 });

    private void ShowNotice(string bodyKey, bool titled, bool offerCopy)
    {
        _noticeKey = bodyKey;
        if (_notice is null) return;         // 编译期要的那颗判定：面板由构造里的 BuildStage 造好，实际⛔ 为空
        _noticeTitle!.Visibility = titled ? Visibility.Visible : Visibility.Collapsed;
        _noticeButton!.Visibility = offerCopy ? Visibility.Visible : Visibility.Collapsed;
        PasteNotice();
        _notice.Visibility = Visibility.Visible;
    }

    /// <summary>面板收起来（开闸成功＝那句坏话已经不是事实）。按钮上的"另存中"一并退掉。</summary>
    private void HideNotice()
    {
        _noticeKey = null;
        _copying = false;
        if (_notice is not null) _notice.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 按当前语言把面板上那两行重贴一次（<see cref="StarMark.Abstractions.I18n.Locale.Changed"/> 那一拍也走它）。
    /// <para>那颗"另存到 &lt;路径&gt;"的槽交的是<b>数据</b>，所以它进 <c>Fmt</c> 的实参；⛔ 在这里拼成品句（那是第二个措辞出处）。</para>
    /// </summary>
    private void PasteNotice()
    {
        if (_noticeBody is null || _noticeTitle is null) return;
        // 那颗按钮上的话也一起重贴：它是构造时冻进去的一串字，⛔ 比面板上那两行晚一步换语言（P-175 那一族的第三种脸）。
        if (_noticeButton is { } button) button.Content = Word("boards.saveCopyAction");
        switch (_noticeKey)
        {
            case "boards.recoveringBody":
                _noticeBody.Text = Word("boards.recoveringBody");
                break;
            case "boards.stuckBody":
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Word("boards.stuckBody");
                break;
            case "boards.copySavedBody":
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Phrase("boards.copySavedBody", _copyPath ?? string.Empty);
                break;
            case "boards.copyFailedBody":
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Word("boards.copyFailedBody");
                break;
            default:
                return;
        }
    }

    /// <summary>表里那一句按当前语言取词（一处限定名，⛔ 在每颗调用点重抄一遍全限定）。</summary>
    private static string Word(string key) => StarMark.Abstractions.I18n.Locale.T(key);

    /// <summary>带一颗实参的那一句（那颗路径是<b>数据</b>，进槽而⛔ 拼成品句）。</summary>
    private static string Phrase(string key, object? argument)
        => StarMark.Abstractions.I18n.Locale.Fmt(key, argument);

    private void OnLocaleChanged() => PasteNotice();

    /// <summary>
    /// 那颗按钮的活：把手上<b>见过的那一份</b>交出去——先问系统"另存为"；用户挑了照他挑的写，
    /// 他取消就<b>什么都不写</b>（面板留着、那颗按钮还能再按），只有宿主调不通那颗框才退到导出目录
    /// 并把那颗路径说给用户（⛔ 因为对话框起不来就把这事推回给用户，那是 P-54 定性过的坏）。
    /// </summary>
    private async System.Threading.Tasks.Task CopySeenSceneAsideAsync()
    {
        if (_copying || _seenScene is not { Length: > 0 } bytes) return;
        _copying = true;
        _noticeButton!.IsEnabled = false;
        try
        {
            var name = BoardPolicy.SceneFileName(_boardId);
            var target = await PickSavePathAsync(name);
            if (target is null) return;                     // 用户自己取消的：面板留着，他还能再按
            System.IO.File.WriteAllBytes(target, bytes);
            _copyPath = target;
            ShowNotice("boards.copySavedBody", titled: true, offerCopy: true);
            StarMark.Abstractions.StarLog.Info("boards.scene.copied to=" + target + " id=" + _boardId);
        }
        catch (Exception ex)
        {
            // 原因进日志，屏幕上只说"没写成、还能再试"：把 ex.Message 摊到面板上＝把一句没人能读的英文异常当用户出口。
            StarMark.Abstractions.StarLog.Warn("boards.scene.copy.failed: " + ex.GetType().Name + " " + ex.Message);
            ShowNotice("boards.copyFailedBody", titled: true, offerCopy: true);
        }
        finally
        {
            _copying = false;
            if (_noticeButton is not null) _noticeButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 问系统"另存为"要写到哪儿。分流只认 <c>BoardPolicy.ClassifyCopyPick</c> 那一颗判据（片28 的真机读数把它逼出来的）：
    /// <b>用户取消＝什么都不写</b>，只有"那颗框起不来"才退到导出目录（⛔ 因为对话框起不来就把这事推回给用户＝P-54）。
    /// </summary>
    private async System.Threading.Tasks.Task<string?> PickSavePathAsync(string suggestedName)
    {
        string? picked = null;
        var threw = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Helpers.WindowInterop.GetHwnd(this));
            picker.SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(suggestedName);
            picker.DefaultFileExtension = BoardPolicy.SceneExtension;
            picker.FileTypeChoices.Add(BoardPolicy.SceneExtension, new[] { BoardPolicy.SceneExtension });
            var file = await picker.PickSaveFileAsync();
            picked = file?.Path;
        }
        catch (Exception ex)
        {
            threw = true;
            StarMark.Abstractions.StarLog.Warn("boards.copy.pick.failed: " + ex.GetType().Name);
        }
        var pick = BoardPolicy.ClassifyCopyPick(threw, picked);
        if (!BoardPolicy.ShouldWriteCopy(pick))
        {
            // 那一问要有记录（⛔ 让"发生了什么"只剩一颗空盘），而屏上留着那句"可以先另存一份到别处"＋那颗还能再按的按钮。
            StarMark.Abstractions.StarLog.Info("boards.copy.declined id=" + _boardId);
            return null;
        }
        if (pick == BoardPolicy.CopyPick.Chosen) return picked;
        System.IO.Directory.CreateDirectory(ExportsDirectory);
        return System.IO.Path.Combine(ExportsDirectory,
            _boardId + "-copy-" + BoardPolicy.FormatStamp(DateTimeOffset.Now) + BoardPolicy.SceneExtension);
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

    /// <summary>
    /// 主人（<see cref="BoardsWindows"/>）在<b>退出那一拍</b>请这一次：与用户自己关窗走同一颗有界等待
    /// （<see cref="FlushSnapshotAsync"/>），差别只有两条——⛔ 拦下关闭（进程马上就没了，拦它没有意义），
    /// 也⛔ 往屏幕贴那张卡（整条链正在往下走，那张卡既⛔ 来得及被看见，还可能把退出拖住）。
    /// <para>返回＝那一份有没有落地。抛了也⛔ 带走退出链：这里就地兜住，只往日志说话。</para>
    /// </summary>
    public async System.Threading.Tasks.Task<bool> FlushForExitAsync()
    {
        if (_closeFlushDone || !_bridgeReady) return false;     // 用户那一拍已经跑过／桥没通＝问不到东西
        _closeFlushDone = true;
        _tearingDown = true;
        try
        {
            return await FlushSnapshotAsync("exit", notice: false);
        }
        catch (System.Exception ex)
        {
            StarMark.Abstractions.StarLog.Warn("boards.close.flush.failed: " + ex.GetType().Name);
            return false;
        }
    }

    private async System.Threading.Tasks.Task FlushThenCloseAsync()
    {
        await FlushSnapshotAsync("window", notice: true);
        Close();
    }

    /// <summary>
    /// 关窗那一拍真正做的事：<b>先让出 <see cref="BoardPolicy.CloseFlushGraceMs"/></b>，再向页面索取一份快照并<b>有界</b>等它落地，
    /// 返回落地与否（写成功、盘上本来就是它，都算落地）。用户那一拍与退出那一拍<b>共用这一颗</b>（两份等待代码＝第二个主人）。
    /// <para>那一小段"让"⛔ 是礼节，它顶着的是一件真事：抬笔后马上关窗时，那一下输入还在这条链上
    /// （系统队列→宿主→浏览器进程→渲染进程），页面此刻交回的是<b>没有那一笔的</b>场景。片32 量到的门槛在 20～40 ms 之间，
    /// 所以这里付 150 ms 换"最后一笔也在盘上"——窗已经 Hide，这段钟用户看不见，而丢的那一笔他看得见（在下一块板上）。</para>
    /// </summary>
    private async System.Threading.Tasks.Task<bool> FlushSnapshotAsync(string origin, bool notice)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var landed = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        _closingLanded = landed;
        _closingSameAsDisk = false;
        await System.Threading.Tasks.Task.Delay(BoardPolicy.CloseFlushGraceMs);
        RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow);
        var budget = System.Threading.Tasks.Task.Delay(BoardPolicy.CloseFlushBudgetMs);
        var done = await System.Threading.Tasks.Task.WhenAny(landed.Task, budget) == landed.Task;
        _closingLanded = null;
        StarMark.Abstractions.StarLog.Info(
            "boards.close.flush." + origin + " id=" + _boardId + " landed=" + done
            + " same=" + _closingSameAsDisk + " tookMs=" + ElapsedMs(started));
        if (done) return true;
        StarMark.Abstractions.StarLog.Warn(
            "boards.close.flush.timeout ms=" + BoardPolicy.CloseFlushBudgetMs + " origin=" + origin);
        if (!notice) return false;
        // 这一句只能走右下角那张卡：窗在这一拍开头就 Hide 了，画板里那块面板⛔ 来得及给用户看见。
        // 卡也没贴上屏幕时（非 UI 线程／建窗失败）日志已经在那儿了——兜底链⛔ 再往上叠一句假话。
        if (!Services.NoticeCard.Show(
                StarMark.Abstractions.I18n.LocMessage.Of("boards.closeFlushTitle"),
                StarMark.Abstractions.I18n.LocMessage.Of("boards.closeFlushBody")))
            StarMark.Abstractions.StarLog.Warn("boards.close.flush.notice.notShown id=" + _boardId);
        return false;
    }

    /// <summary>
    /// 这一扇在那一拍里<b>实际</b>等了多久（毫秒，向下取整）。名册那一趟是<b>并发</b>等的，
    /// 所以"总时长＝最坏那一扇"这句话只有每颗各报一个数时才量得出来——⛔ 由总量倒推（坑表 #696 同族）。
    /// 那颗 <c>tookMs</c> 与超时那句里的 <c>ms=</c>⛔ 是同一颗数：后者是<b>预算</b>，前者是<b>这一发用掉的</b>。
    /// </summary>
    private static long ElapsedMs(long started)
        => (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000 / System.Diagnostics.Stopwatch.Frequency;

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
            // 那句"正在恢复"只在这一下真的递出去了才贴：那颗 `?.` 短路＝什么都没问，
            // 而贴一句做不完的承诺比一句白话更坏（用户会等一个不会来的画板）。
            var view = _web.CoreWebView2;
            if (view is not null) { view.Reload(); ShowRecoveringNotice(); }
            return;
        }
        if (_recovering) return;             // 两颗控件抢同一扇窗＝更坏的现场
        if (_recreateTries >= BoardPolicy.MaxRecreatePerWindow)
        {
            StarMark.Abstractions.StarLog.Warn("boards.recover.givenup tries=" + _recreateTries);
            // 这一档就是§21.5 里那句"⛔ 许静默白屏"最直的一次：往后这块板确实再也好不了。
            // 于是把那颗写闸也当场关掉（这一趟交回来的字节再也没人证明过它回到过这块板）并把手上那份递给用户。
            _autosaveArmed = false;
            ShowArmStuckNotice();
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
    /// <para><b>换的是 <see cref="_stage"/> 里的孩子，⛔ 重指 <c>Content</c></b>：那块面板住在同一层里，
    /// 重指一次就把它连话一起摘了——而这一刻正是要它说话的时候（片27 给§21.5 的那半）。
    /// 插在 index 0 是让它继续当<b>画布那一位</b>：面板永远压在画布之上。</para>
    /// </summary>
    private void RecoverByRecreatingWeb()
    {
        var old = _web;
        _web = NewWeb();
        _stage.Children.Remove(old);
        _stage.Children.Insert(0, _web);
        ShowRecoveringNotice();
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
        BoardsWindows.Remove(this);                       // 与构造里那颗 Add 成对（名册上留一颗已关的窗＝退出时会去问一颗没桥的窗）
        // 那颗静态事件也一起摘：留着它，每开一扇已关的画板窗都会被"换语言"那一拍叫一次（订阅手臂成对，P-198 那把尺管的正是这一族）。
        StarMark.Abstractions.I18n.Locale.Changed -= OnLocaleChanged;
        if (_web.CoreWebView2 is not null)
        {
            _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
            _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
        _web.Close();
    }
}
