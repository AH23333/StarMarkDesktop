#nullable enable
using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
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
/// <para><b>回路现状（片57 现读・片68 补发话人）</b>：进向八颗里 <c>bridge.ready</c>／<c>board.loaded</c>／<c>open.external</c>／
/// <c>export.bytes</c>／<c>bridge.reply</c> 五颗有主人；<c>paste.query</c>／<c>pin.request</c>／<c>background.request</c>
/// 三颗还没接线——宿主只往日志里落一行痕（<c>boards.cmd.not-wired</c>），判定归各自批次（⛔ 假 allow）。
/// ⚠「有主人」这句话说的是<b>宿主那一侧有 case</b>，⛔ 等于那颗名字两头都通：<c>open.external</c> 从片57 起就
/// 只有这一半，页面一颗也不发（发话人由片68 补上＝桥里那颗 <c>onLinkOpen</c>，见
/// <c>BoardLinkGateTests.PageHandsTheLinkClickToTheHost</c>）。「名字在册⛔ 等于这条路通」＝§28.2 那句口径的正件。
/// 那一族从前还各回一句"收到／没收"，而页面那颗听众压根⛔ 存在（P-245 撤掉的就是它：宿主→页面只剩那颗过门的门）。</para>
/// <para><b>落盘与载入（片21＝§21.3）</b>：页面侧停笔 <see cref="BoardPolicy.AutosaveIdleMs"/> 毫秒自己交一份
/// 场景 JSON（<c>export.bytes</c>＋<c>kind:"scene"</c>，⛔ 新名字），宿主失焦那一拍再索取一次（<c>export.pick</c>）；
/// 字节经 <see cref="BridgeProtocol.LandsInSceneArchive"/> 分流进板档目录，png／svg 仍进导出目录。
/// <b>片22＝§21.4 那颗脏位</b>：交回来的字节与盘上那份<b>逐颗相等</b>时⛔ 写、⛔ 滚（<see cref="BoardPolicy.DiskAlreadyHolds"/>）；
/// 同批把 <c>export.bytes</c> 的 <c>suggested</c> 接上链——页面递 <c>appState.name</c>，只有导出那一路读它（板档档名＝那颗 id，⛔ 跟着显示名走）。</para>
/// <para><b>§21.3 那四拍都接上了线</b>。剩下那两拍各自能做的事⛔ 一样：
/// <b>关窗</b>那一拍走 <c>AppWindow.Closing</c>——拦一次、窗先 Hide（用户⛔ 等那两秒）、向页面索取一份快照并<b>有界</b>等它落地
/// （<see cref="BoardPolicy.CloseFlushBudgetMs"/>），到点照样关（⛔ 把用户的退出键抵押给一颗卡住的网页）；
/// <b>可那一拍撞见的是"页面交回了、而被写闸拒掉"，⛔ 关</b>——那颗窗先 Hide 了，把那两行字留在藏着的窗上＝一句没人看得见的坏话，
/// 所以把它亮回屏幕、让用户当场看得见那句、按得动那颗「另存一份到别处」（片35；他再点一次×就是他的决定）；
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
/// <c>boards.close.flush.exit … landed=True</c>（本批起两条来路<b>各有一句</b>落地话，⛔ 再只在超时时才吭声；
/// 片61 起那一行还带一列 <c>refused=</c>——那颗 <c>landed=</c> 问的从来只是「页面回没回话」，「那一份进没进盘」由新列与
/// <see cref="FlushForExitAsync"/> 交回的那颗 bool 说，见 P-247）。
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
/// <para><b>片54＝那一发导出的出口收进宿主（P-240 的②）</b>：片53 量到那颗「导出图片」⛔ 是死的、按下去真落盘
/// （正本 <c>readings_51e_123104</c>：系统「另存为」里按 PNG⇒t=0.5 s 落档 2203 颗字节），而<b>落点与命名归那颗框</b>。
/// 本批第一版照 §21.4 的思路去<b>接管那颗下载</b>（订 <c>DownloadStarting</c>、改 <c>ResultFilePath</c>、<c>Handled＝true</c>），
/// 真机一跑当场把它否掉（正本 <c>bin/WI93/readings_54_134804.txt</c>＋<c>bin/WI93/log54_forensics.txt</c>）：
/// 那颗 <c>#32770「另存为」960x600</c> 照样起来，而<b>整份日志里 <c>boards.download</c> 零行</b>
/// ——同一把尺同一跑读得到 <c>boards.nav.done</c>／<c>boards.bridge.ready</c>／<c>boards.arm.post</c> 各一行（尺⛔ 聋），
/// 被测那颗 dll 与装配那颗逐字相等（沙盒⛔ 是旧二进制）。
/// 现读那颗引擎的 <c>saveAs</c> 说的是另一句话：那颗模块选择器在<b>顶层文档</b>里有 <c>showOpenFilePicker</c> 时
/// 走的是 <c>window.showSaveFilePicker</c> 那一支（<c>excalidraw.min.js</c> 现读 4878772 颗字节里那颗调用逐字只 1 处＝偏移 245263），
/// <b>压根⛔ 是下载</b>⇒宿主订不到，也改不动它的落点。
/// ⇒按账本 P-240 ②那句<b>原话</b>动手（我中途改的那一次口已撤回）：引擎那颗框连同开它的那两颗名字一起关到⛔ 通
/// （<c>board-bridge.js</c> 现读 <c>export:false</c>＋<c>saveAsImage:false</c>，随包资产走片18 那颗闸门重冻），
/// 出口换成宿主这一扇右上角那一行：<c>export.pick</c> 带 <c>kind＝png／svg</c> → 页面交字节 →
/// <see cref="HandleExportBytes"/> 落产物目录（档名＝<see cref="ExportStem"/> 兜底，盘上已有同一颗就添 <c>-2</c>／<c>-3</c>＝
/// <see cref="StarMark.Core.Boards.BridgeProtocol.UniqueExportName"/>，⛔ 盖掉上一颗＝账本 P-243），而那句"已存到 &lt;路径&gt;"与那句"没能存成"
/// 由<b>那一行自己</b>说（<see cref="PasteBar"/>）：它⛔ 走那块面板，因为面板此刻可能正挂着"这一扇已停止保存"，
/// 一句好消息⛔ 有权把那句坏话顶掉（片39 真机量到的那一发就是这一族）。</para>
/// <para>⚠ 那颗框里的四颗旋钮（背景／深色模式／包含画布数据／缩放 1×2×3×）跟着关掉了＝导出按 appState 默认走（1×、带背景）；
/// 要不要在宿主侧重造，归账本新立的那一颗（⛔ 在这一批顺手补，也⛔ 静默当它不存在）。</para>
/// </summary>
public sealed class BoardWindow : Window
{
    /// <summary>虚拟宿主名（本类是它的唯一主人；闸门测试读这颗常量）。</summary>
    public const string VirtualHostName = "appassets.local";

    /// <summary>页面入口（相对随包根）。</summary>
    public const string BoardPageName = "board.html";

    /// <summary>userDataFolder 那条改道（v1 §坑8）：钉在 <c>%LOCALAPPDATA%</c> 之下。</summary>
    public const string UserDataFolderRelative = @"StarMark\wbview2";

    /// <summary>导出产物与那颗救回来的副本落进哪一颗目录（硬线①：落点由 C# 定，web 侧只交字节；P-219 的裁决＝进「图片」那一族）。
    /// <para><b>规则住 <see cref="BoardPolicy.ExportsFolder"/>，这里只把两棵现成的根递进去</b>：特殊目录与那颗改道信号
    /// 都是宿主这一侧的事实（Core⛔ 认 <c>%APPDATA%</c>，同它⛔ 认 WebView2 那颗 enum 一个口径）。
    /// 片23 那一版切成 <c>BoardSceneStore.Folder\exports</c> 修的是"根只许一位主人"，但它把<b>产物</b>与
    /// <b>程序内部档</b>排在同一棵树下⇒用户在图片目录里翻不到自己刚救回来的那一份（P-219 登记的就是这一格）。</para></summary>
    public static string ExportsDirectory => BoardPolicy.ExportsFolder(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        StarMark.Abstractions.UserDataPaths.RedirectedDataRoot());

    private Microsoft.UI.Xaml.Controls.WebView2 _web;
    private string _boardId;

    /// <summary>
    /// 这一扇撞过、而<b>此刻还拿不出来</b>的那颗档（片49＝P-239）。
    /// <para>它是面板上那两颗按钮的唯一在场资格（<see cref="PasteNotice"/> 只读这一颗，⛔ 在 <c>ShowNotice</c>
    /// 与换语言那一拍各写一遍可见性＝第二个主人）。载入成功、改开新板、换了一颗档都当场清掉：
    /// 挂着不清＝下一句别的坏话（"这一扇已停止保存"）会连带把「再试一次」贴回屏幕，而那一刻⛔ 没有东西可试。</para>
    /// </summary>
    private string? _blockedSceneId;

    /// <summary>那颗「再试一次」正在跑（与 <see cref="_copying"/> 同一副规矩：跑的时候按钮点得着＝他会连按，两发重试各自开场）。</summary>
    private bool _retrying;

    /// <summary>宿主<b>见过</b>的最近一份完整场景字节——桥每 ready 一次（首开、崩溃重载）就递它进画板。
    /// <para>它与 <see cref="_lastWritten"/> 问的⛔ 是同一件事：那一颗问"盘上主档此刻是哪份"（脏位归它），
    /// 这一颗问"重建起来该把什么放回去"。写失败的字节<b>也算见过</b>——用户那几笔不该因为盘上付不进就凭空退场。</para></summary>
    private byte[]? _seenScene;

    /// <summary>写闸<b>拒掉</b>的最近一份场景（片33 量到的那一格：面板起来之后用户画的那一笔，只有这一颗里有）。
    /// <para>它⛔ 进 <see cref="_seenScene"/> 的那条恢复路——那是 P-223 定死过的坏法（把白屏当"见过的那一份"递回画布）。
    /// 它只有一个消费者：那颗"另存一份到别处"，而且要先过 <see cref="BoardPolicy.ScreenSceneIsWorthSaving"/>；
    /// 开了闸就当场清掉（那一刻起手里那份就是最新的，这一颗可疑的旧账⛔ 再配当候选）。</para></summary>
    private byte[]? _refusedScene;

    /// <summary>此刻那块画布上有几颗<b>永远不会出现正文</b>的外部内容（<c>embeddable</c> 那一族・判据住 Core）。
    /// <para>它只有一个消费者：那一行「未显示的外部内容：N」。三档下落要分得清——<c>null</c>＝那份字节读不懂
    /// （⛔ 说"一颗也没藏"）、<c>0</c>＝这块板上确实没有、<c>&gt;0</c>＝真有 N 颗空框在屏幕上没话可说。
    /// 账本 P-255 量的就是最后那一档：挂载 0 颗而 <c>canvas=3</c>，程序一个字也⛔ 说。</para>
    /// <para>它由<b>宿主见过的最新那一份字节</b>定（<see cref="OpenAsync"/> 与页面交回那两拍），
    /// 所以用户删掉那颗外链框之后这一行会自己退场——⛔ 另配一颗"清除"的动作＝第二个主人。</para></summary>
    private int? _hiddenEmbeddables;

    /// <summary>盘上主档那一刻是哪些字节（只有宿主自己写得动它，所以这一份状态有唯一主人）。
    /// null＝这扇窗还没写过、也没<b>从主档</b>载过（新板，或档是从 <c>.bak</c> 捡回来的）。</summary>
    private byte[]? _lastWritten;
    private bool _bridgeReady;

    /// <summary>关窗那一拍已经拦过一次（第二拍照常走下去——⛔ 让那扇窗永远关不掉）。</summary>
    private bool _closeFlushDone;

    /// <summary>
    /// 关窗那一拍等回来的那份字节是<b>被写闸拒掉的</b>（而⛔ "页面压根没回答"）。
    /// <para>这两件事从前长得一样：拒的那一支也算"这一问有答案"（<see cref="FinishClosingFlush"/> 当场结掉等待，
    /// ⛔ 让用户白等那两秒），于是 <c>landed=True</c> 与"那颗窗已经 Hide 了"拼在一起＝
    /// 用户画的那几笔一颗也没落盘，而屏幕上<b>一句也⛔</b>——那句坏话（<c>boards.stuckBody</c> 那两行＋那颗「另存一份到别处」）
    /// 贴在一颗藏着的面板上（P-224／P-227 那一族的第三种脸：话是真的，宿主⛔ 在场）。</para>
    /// <para>这颗数只有一位消费者：<see cref="FlushThenCloseAsync"/> 拿它决定"关掉"还是"把窗亮回来"。
    /// 一问一复位（与 <see cref="_closingSameAsDisk"/> 同一颗口径），⛔ 让上一拍的旧账替这一拍说话。</para>
    /// </summary>
    private bool _closingRefused;

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

    /// <summary>那块面板的<b>第一行</b>（那句话＋它那六颗出口同住一层）：片108＝P-257 (C) 起那一格有两行，
    /// 收起第一行＝连同它的出口一起收起，⛔ 把第二行的字与第一行的按钮留在同一块边框里。</summary>
    private StackPanel? _noticeMain;
    private TextBlock? _noticeTitle;
    private TextBlock? _noticeBody;
    private Button? _noticeButton;

    /// <summary>那块面板的<b>第二行</b>（片108＝P-257 的落地腿）：外链那两句坏话只住这里，
    /// 所以第一行挂着话时它照样上得了屏——归属规则从"面板空着才进"换成"两行各有主人"。</summary>
    private TextBlock? _noticeLinkBody;

    /// <summary>那颗「打开所在文件夹」（片39＝P-219 的上屏出口）：它只跟着"盘上真有一颗副本"那几句话出现。</summary>
    private Button? _noticeFolderButton;

    /// <summary>片40＝关窗那一拍被拒之后，屏幕上除了「另存」还须给「要留」与「要走」两颗（<see cref="_closeRevived"/> 为真才在场）。</summary>
    private Button? _noticeStayButton;

    /// <summary>那颗「仍然离开」：用户当场决定不救、直接关这扇（⛔ 让他去猜"再点一次×就退"那颗隐藏出口）。</summary>
    private Button? _noticeLeaveButton;

    /// <summary>片49＝那颗档现在拿不出来那一句的两颗按钮（<see cref="_blockedSceneId"/> 不为空才在场）：
    /// 「再试一次」与「先开一块新板」。</summary>
    private Button? _noticeRetryButton;
    private Button? _noticeFreshButton;

    /// <summary>这一拍用户按过×、而那次关窗被写闸拒⇒窗被亮回来。两颗「继续编辑／仍然离开」只在这格在场：
    /// 没被关过＝⛔ 有话要说"走还是留"（停笔那一拍的拒只配「另存」）。</summary>
    private bool _closeRevived;

    /// <summary>那块面板<b>第一行</b>此刻说的那一句住在表里的哪一颗键（null＝那一行空着）。<b>换语言那一拍按它重贴</b>（机制 (C)）。</summary>
    private string? _noticeKey;

    /// <summary>那块面板<b>第二行</b>此刻说的那一颗键（null＝那一行空着）：只由 <see cref="ShowLinkNotice"/> 写、
    /// 只由 <see cref="HideLinkNotice"/> 收——那一行归外链那两句，⛔ 顶掉第一行也⛔ 被第一行的收口顺带撤掉（片108＝P-257）。</summary>
    private string? _linkNoticeKey;

    /// <summary>那颗"另存一份到别处"的按钮这这一刻许不许按（另存正进行中＝⛔ 再按一次，那是第二份写入）。</summary>
    private bool _copying;

    /// <summary>那两句"这一发外链没开成"——那块面板<b>第二行</b>上由时长收的话（第一行那些都由状态收：
    /// "已停止保存"在写闸重新开起来之前句句是真，而"这条链没打开"没有"什么时候不再是事实"那一刻）。
    /// <para>这一串是<b>那颗定时器唯一的名单</b>：收口那一拍问的是它，⛔ 在 Tick 里再 switch 一遍键名（两处各列＝迟早只补一处）。</para></summary>
    private static readonly string[] LinkNoticeKeys = ["boards.linkBlockedBody", "boards.linkFailedBody"];

    /// <summary>那两句的计时器（一颗、懒建、只由 <see cref="BoardPolicy.LinkNoticeMs"/> 定时）。
    /// <para>⛔ 每次开口新建一颗：Tick 挂着这一窗，新建而不摘＝每发失败的点击多留一位订阅者（P-198 那一族）。</para></summary>
    private DispatcherTimer? _linkNoticeTimer;

    /// <summary>面板上那句"已另存到 &lt;路径&gt;"指的<b>那颗档</b>（生产者⛔ 一处：那颗「另存一份到别处」）。
    /// <para>⛔ 进表，因为它是<b>数据</b>而⛔ 句子（P-193 那条口径）。那一发导出的产物⛔ 写这一颗——它住在右上角
    /// 那一行里（<see cref="_exportPath"/>）：两块表面各说各的档，⛔ 让一句好消息把面板上那句"这一扇已停止保存"顶掉
    /// （片39 真机量到的就是这一族：旧话把新事实盖掉、那颗出口跟着退场）。</para></summary>
    private string? _savedPath;

    /// <summary>右上角那一行<b>最近真落盘</b>的那颗产物（那句"已存到"与那颗「打开所在文件夹」共用的唯一答案）。
    /// <para>它⛔ 是"最近一次尝试"：失败⛔ 覆盖它（盘上那一颗还在，用户照样点得开），⛔ 跟着换板清掉——
    /// 那颗档名里带着它所属那块板的 id（<see cref="ExportStem"/>），说的就是"哪一块板的哪一刻"。</para></summary>
    private string? _exportPath;

    /// <summary>右上角那一行此刻挂着的<b>那一句住在表里的哪颗键</b>（null＝还没导出过）。
    /// <para>记<b>键</b>而⛔ 记成品串：换语言那一拍要按它重贴（机制 (C)），而那颗路径是实参、⛔ 是句子。</para></summary>
    private string? _exportOutcomeKey;

    /// <summary>那一发导出<b>用户挑的</b>缩放档（名单唯一的主人＝<see cref="BoardPolicy.ExportScales"/>）。
    /// <para>为什么这里要有一颗自己的值：引擎那颗默认⛔ 是常数，它按
    /// <c>cd.includes(devicePixelRatio)?devicePixelRatio:1</c> 算（随包档 offset 2166977／2168217 现读）
    /// ＝同一块板在 100% 与 200% 两块屏上导两张不一样大的图，而屏幕上那句"已存到"从未说过它＝账本 P-242。
    /// 今天这一档<b>由用户挑、每一发都递进去</b>，所以那台显示器的缩放⛔ 再替他的图定尺寸。</para></summary>
    private int _exportScale = BoardPolicy.ExportScaleDefault;

    /// <summary>那一发导出<b>用户挑的</b>那颗背景开关（默认与引擎自家 <c>exportBackground:!0</c> 对齐＝开着）。</summary>
    private bool _exportBackground = BoardPolicy.ExportBackgroundDefault;

    /// <summary><b>那一发问出去的</b>两档（开口那一刻记下，供屏幕上那句"已存到"念）。
    /// <para>⛔ 直接念 <see cref="_exportScale"/>／<see cref="_exportBackground"/>：那句话讲的是<b>已经落在盘上的那张图</b>，
    /// 而他会在那一发还跑着的时候改挑档——念手里那颗＝指着一张 1× 的图说它是 2×（片32 那族"上一问的脸活到下一问"的另一副脸）。
    /// 写它只有一处＝<see cref="ExportImageButton"/> 开口之前，与那颗 <see cref="_exportGen"/> 同一拍。</para></summary>
    private int _sentScale = BoardPolicy.ExportScaleDefault;
    private bool _sentBackground = BoardPolicy.ExportBackgroundDefault;

    /// <summary>那一行正有一发导出在跑（与 <see cref="_copying"/>／<see cref="_retrying"/> 同一副规矩：
    /// 跑的那一发点得亮＝他会连按出两发，屏幕上两句话抢同一颗落点）。</summary>
    private bool _exporting;

    /// <summary>那一发导出问出去的第几<b>代</b>（片56＝P-244 的第三处：那颗预算的闹钟只认自己那一代）。
    /// <para>为什么⛔ 只看那颗 <see cref="_exporting"/>：它是一颗<b>共用的闩</b>。第一发早就结掉了、第二发正跑着时，
    /// 第一发那颗还在睡的闹钟醒来会读到"还在跑"＝把<b>别人那一发</b>判成没回音，当场放开那一闩、
    /// 还把第二发的回音变成一句没人听的坏话（片32 那族"上一问的脸活到下一问"的另一副脸）。
    /// 写它只有一处＝<see cref="ExportImageButton"/> 每次开口自增；闹钟那一拍比对它，⛔ 动盘上任何东西。</para></summary>
    private int _exportGen;

    /// <summary>那一发 PNG 的<b>落点是桌面</b>而⛔ 导出目录（片148＝深析 §四 第2行：画板导出 → 贴桌面）。
    /// <para>为什么是一颗位而⛔ 另起一条"直接贴"的路：那一发要的字节由页面现算（缩放与那颗背景开关都归它），
    /// 宿主这侧⛔ 那份画面的第二份实现——所以问同一发，只在字节回来那一刻分岔。</para>
    /// <para>⚠ 它由 <see cref="ReportExport"/> 清掉，而⛔ 由"字节回来"那一支清：三条下落（真回来／页面说办不成／
    /// 预算到点）都要过那一处收口。留在原地＝下一发按「导出 PNG」时悄悄贴到桌面上，
    /// 而那颗按钮上的字说的是"存成一张档"（S1 那条口径：会改状态的收尾，最后一步要重读）。</para></summary>
    private bool _exportPins;

    /// <summary>右上角那一行（导出那两颗入口＋那颗落点的话）：它⛔ 是那块面板的零件——面板只在有事要说时才在场，
    /// 而"我刚导出那张图，它落在哪儿"每一次都要有答案。</summary>
    private StackPanel? _bar;

    private Button? _barPngButton;
    private Button? _barSvgButton;
    private Button? _barPinButton;
    private Button? _barScaleButton;
    private Button? _barBackgroundButton;
    private Button? _barFolderButton;
    private TextBlock? _barStatus;

    /// <summary>那一行底下第二颗读数：那块板上有几颗外部内容不会显示（P-255・见 <see cref="_hiddenEmbeddables"/>）。
    /// <para>它⛔ 住在 <see cref="_notice"/> 那块面板里：那一块只有一位（<c>_noticeKey</c>），
    /// 而它此刻可能正挂着「这一扇已停止保存」——一句中性的说明⛔ 有权把那句坏话顶掉（片39 真机量到的正是这一族）。</para></summary>
    private TextBlock? _barHiddenEmbeds;

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
        _blockedSceneId = null;   // 这一扇现在说的是"一块新板"：那颗拿不出来的档⛔ 再挂在面板上（两颗按钮的资格只由它算）
        _lastWritten = null;   // 盘上还没有这颗板的档：第一拍照常规写，别把"还没写过"读成"已经一样"
        return OpenAsync(null);
    }

    /// <summary>
    /// 接着开那颗已有的板：先按 id 把档读进内存，等 <c>bridge.ready</c> 那一刻再递给页面。
    /// <para>顺序是反不得的：桥没通就发 <c>board.load</c>，页面侧那位 <c>API</c> 还是 null，
    /// 场景就永远进不去（而宿主这边一句错都⛔ 报）。</para>
    /// <para>读不到分<b>两族</b>（片49＝P-239，从前折成同一颗 <c>false</c>）：
    /// <b>「⛔ 有这颗档」</b>＝照旧退回新板（托盘那句"续上最近一块"本来就该容得下"一块也没有"）；
    /// <b>「这颗档现在拿不出来」</b>＝先<b>宿主自己</b>重试几回（额度＝<see cref="BoardPolicy.LoadRetryTries"/>），
    /// 撞见的那两档门路（别人正攥着、自家那次改名正在半路）按秒就退，一发重试就救得回来——
    /// 把"请稍后重试"写在屏幕上让他按，是程序自己做得到的一件事转成他的操作步数（P-54 定过的坏法）。
    /// 重试到点还是拿不出来，才<b>把这一扇停在面板上</b>：说一句他读得懂的话，给两颗他按得动的按钮，
    /// 而⛔ 另起一块空画布当作那份档没了（那是 P-239 那句坏话的本体：屏幕上"我的板空了"，盘上一颗字节也⛔ 动）。</para>
    /// <para>停在面板上＝这一趟<b>压根没开页面</b>，所以那颗「再试一次」接回来的画布一定是空的：
    /// 接档⛔ 顶掉任何用户笔迹——这一点由"没开页面"保证，而⛔ 由一句"看着像是空的"猜。</para>
    /// <para>从 <c>.bak</c> 捡回来的那一档⛔ 点亮脏位：盘上的<b>主档位置当⛔ 一份档</b>（没了，或还在却是 0 字节／半颗——
    /// 后者由片43 把这一头接通），所以"盘上已经有了"那句⛔ 成立，得让第一拍把主档补回去
    /// （否则那颗板在 <see cref="BoardSceneStore.LatestSceneId"/> 那一问里始终是缺的）。
    /// ⚠ 「读出来的是⛔ 是能打开的一份」这一问归存储那颗形状判据，这里⛔ 再判一遍字节数
    /// （原来这里挂着半句 <c>bytes.Length == 0</c>＝把"读出一颗垃圾"当场折成"这颗板不存在"，正是另起新板那一步）。</para>
    /// </summary>
    public async System.Threading.Tasks.Task OpenSceneAsync(string sceneId)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (BoardSceneStore.TryLoad(sceneId, out var bytes, out var fromBackup, out var blocked))
            {
                _blockedSceneId = null;
                _boardId = sceneId;
                _lastWritten = fromBackup ? null : bytes;
                HideNotice();      // 那句"现在打不开"已经不是事实；递档那一拍要出问题会说它自己的那句话
                await OpenAsync(bytes);
                return;
            }
            if (!blocked) { await OpenNewAsync(); return; }      // 真没有这颗板：照旧静默新起一块
            if (attempt >= BoardPolicy.LoadRetryTries)
            {
                _blockedSceneId = sceneId;
                StarMark.Abstractions.StarLog.Warn(
                    "boards.scene.blocked id=" + sceneId + " tries=" + attempt
                    + " gapMs=" + BoardPolicy.LoadRetryDelayMs);
                ShowNotice("boards.blockedBody", titled: true, offerCopy: false);
                return;
            }
            StarMark.Abstractions.StarLog.Info(
                "boards.scene.blocked.retry n=" + attempt + " id=" + sceneId);
            await System.Threading.Tasks.Task.Delay(BoardPolicy.LoadRetryDelayMs);
        }
    }

    /// <summary>
    /// 那颗「再试一次」：走的就是开场那一条（两份重试代码＝第二个主人，其中一份忘了收面板是本仓定死的那类坏）。
    /// <para>按下去必须有回音（P-226 同一条口径）：那颗闸 <see cref="_retrying"/> 让按钮在跑的这一发里点不亮，
    /// 跑完无论成没成都把它放开——⛔ 让一句"还在试"永远挂着。</para>
    /// </summary>
    private async System.Threading.Tasks.Task RetryBlockedSceneAsync()
    {
        if (_retrying || _blockedSceneId is not { } sceneId) return;
        _retrying = true;
        PasteNotice();
        try { await OpenSceneAsync(sceneId); }
        finally { _retrying = false; PasteNotice(); }
    }

    /// <summary>
    /// 那颗「先开一块新板」：他不等了，就给他一块能使的板——新板用的是这一扇自己那颗新 id，
    /// 盘上那颗拿不出来的档<b>一个字节也⛔ 动</b>（它连被打开都没被打开），下一趟照样接得回去。
    /// </summary>
    private void StartFreshBoard()
    {
        if (_blockedSceneId is not { } sceneId) return;
        StarMark.Abstractions.StarLog.Info("boards.scene.fresh insteadOf=" + sceneId + " id=" + _boardId);
        _ = OpenNewAsync();
        HideNotice();
    }

    /// <summary>那条唯一的"开一扇页"的路：首开与崩溃后的重建都走它（两份开场代码＝第二个主人）。</summary>
    private async System.Threading.Tasks.Task OpenAsync(byte[]? sceneBytes)
    {
        _seenScene = sceneBytes;
        RememberHiddenEmbeddables(sceneBytes);   // 这块板上有几颗外部内容不会显示（P-255・新板＝⛔ 一颗）
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
        // 账本 P-248 那道「第二道」的<b>后半</b>（片69 翻成 deny；前半＝片67 那颗只记⛔ 挡的观察者）。
        // 为什么这一翻现在<b>敢</b>做，而片67 不敢：那颗名册由一颗<b>不占屏幕</b>的隐藏宿主量到了
        // （正本 bin/WI98/roster69_r4／roster69_r3 两跑＋那颗正件 roster69_ctl3），三颗数一起说：
        // ① 随包那一族的 8 颗全住 <c>https://appassets.local</c>；② 树自己发的外呼 <b>0</b> 颗；
        // ③ 而"挡"这一手<b>确实落得到页面</b>——同一台本机服务器两种写法，被放行的那颗⛔ 经观察者（那颗虚站映射
        //   按<b>站名</b>劫持，连 <c>http://127.0.0.1:8099</c> 都由目录供），被拦的那颗读回 <c>451</c>。
        // ⚠ ③ 的另一半就是<b>⛔ 白屏</b>那半句的根据：随包那八颗压根进不了这把尺，所以这颗桩<b>动不到画布本体</b>。
        _web.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        _web.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
        _web.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
        // 同一批（P-248 后半）另两颗：页要<b>开新窗</b>（<c>window.open</c>／<c>target=_blank</c>）与顶帧要<b>导航去别处</b>。
        // ⛔ 只订那颗出站事件就以为边界齐了——那两条路各自有自己的事件，一颗也不经 <c>WebResourceRequested</c>。
        _web.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        _web.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _web.CoreWebView2.NavigationStarting -= OnNavigationStarting;
        _web.CoreWebView2.NavigationStarting += OnNavigationStarting;
        // 同一族还剩<b>子帧</b>那一头（账本 P-252）：顶帧那颗闸门压根管不到 <c>iframe</c>——片72 臂 D 现读
        // 「摘掉 CSP 往画布递一颗远程 iframe，宿主观察者一颗也没听到」＝那一路连<b>听到</b>都⛔ 听到。
        _web.CoreWebView2.FrameNavigationStarting -= OnFrameNavigationStarting;
        _web.CoreWebView2.FrameNavigationStarting += OnFrameNavigationStarting;
        // ⛔ 在这里订 <c>DownloadStarting</c> 去接管那一发导出（本批第一版试过，被真机当场否掉）：
        // 那颗引擎的 saveAs 走的是 <c>window.showSaveFilePicker</c>，压根⛔ 是下载——那颗事件一颗也不响，
        // 逐字读数见类文档那段（正本 readings_54_134804＋log54_forensics）。
        _web.CoreWebView2.Navigate("https://" + VirtualHostName + "/" + BoardPageName);
    }

    /// <summary>
    /// 那一扇页到底落没落地（P-222 的现场尺其一）：崩之后无论走 Reload 还是重建控件，这一行都该再来一次，
    /// 而 <c>ok</c>／<c>err</c> 就是那三个候选因由的分岔——"重载回来了个错误文档"与"回来了却没载档"在这一行上不同形。
    /// </summary>
    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        => StarMark.Abstractions.StarLog.Info(
            "boards.nav.done seq=" + _readySeq + " ok=" + e.IsSuccess + " err=" + e.WebErrorStatus + " src=" + sender.Source);

    /// <summary>这一扇页外呼过的<b>不同目标</b>（方案＋主机名，⛔ 路径）——只用来节流那颗痕：一颗目标只念一行。</summary>
    private readonly HashSet<string> _netTargets = new(StringComparer.OrdinalIgnoreCase);
    private int _netLocal, _netInline, _netForeign, _netDenied;
    private int _newWindowBlocked, _navBlocked, _frameNavBlocked;

    /// <summary>那颗桩顶出去的状态码（账本 P-248 后半）。<b>451 而⛔ 403</b> 的理由不是语义，是<b>可分辨</b>：
    /// 真机那一跑要能分清「这颗宿主挡的」与「那颗站自己拒的」，两颗同码就只能猜（片69 正件那条臂读回的正是这颗 451）。</summary>
    private const int BlockedResponseStatus = 451;

    /// <summary>
    /// 那颗出站观察者（账本 P-248）：数「读了随包那一份」「压根⛔ 出机」「读了别处」「被这颗挡下」四颗数，
    /// 每颗<b>新</b>外呼目标落一行 <c>boards.net.foreign</c> 并把四颗数一起带上（⛔ 只念外呼那一颗：
    /// 一颗 <c>local=0</c> 的名册说明这把尺压根没开火，而那和「这页只出机」在单看外呼数时同一个脸）。
    /// 该不该挡<b>⛔ 在此处判</b>——判据的主人住在
    /// <see cref="BoardsAssetPolicy.IsOutboundDenyUri"/>，本方法只照它分类并落桩（⛔ 在 UI 里再写一份方案／主机比较＝第二个主人）。
    /// </summary>
    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var raw = e.Request?.Uri;
        Uri? uri = raw is not null && Uri.TryCreate(raw, UriKind.Absolute, out var parsed) ? parsed : null;
        if (BoardsAssetPolicy.IsLocalAssetUri(uri, VirtualHostName))
        {
            _netLocal++;
            return;
        }
        if (BoardsAssetPolicy.IsInlineUri(uri))
        {
            _netInline++;
            return;
        }
        _netForeign++;
        var target = BoardsAssetPolicy.DescribeRequestTarget(uri);
        if (BoardsAssetPolicy.IsOutboundDenyUri(uri, VirtualHostName))
        {
            // 空体那一颗桩替掉真答案（片69 正件量到它落得到页面：那颗读回 451、正文 0 颗字）。
            // ⛔ 在这里兜一颗 try：挡失败了也得上屏——静默兜住＝让"挡"这句话替一件没发生的事作证（P-13 那一族）。
            _netDenied++;
            e.Response = sender.Environment.CreateWebResourceResponse(
                new Windows.Storage.Streams.InMemoryRandomAccessStream(),
                BlockedResponseStatus, "Blocked By StarMark", "Content-Length: 0\r\n");
        }
        if (_netTargets.Add(target))
            StarMark.Abstractions.StarLog.Warn(
                "boards.net.foreign n=" + _netForeign + " target=" + target
                + " local=" + _netLocal + " inline=" + _netInline + " denied=" + _netDenied + " id=" + _boardId);
    }

    /// <summary>
    /// 页要开一颗<b>新窗</b>（<c>window.open</c>／<c>target=_blank</c>）：一律 <c>Handled</c>，一颗也不给它开
    /// （账本 P-248 后半第二颗；那颗画布上带链接的档由 <c>open.external</c> 交宿主开，见片68）。
    /// ⛔ 把 <c>e.Uri</c> 递进日志——那颗串由页面说了算，且带着他点过哪条链接的隐私（P-13/P-193 同一条口径）。
    /// </summary>
    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _newWindowBlocked++;
        StarMark.Abstractions.StarLog.Warn(
            "boards.newwindow.blocked n=" + _newWindowBlocked + " initiated=" + e.IsUserInitiated + " id=" + _boardId);
    }

    /// <summary>
    /// <b>顶帧</b>要导航去<b>别处</b>（<c>location = …</c>／表单提交）：照那颗出站判据取消——画布被换成一颗外站
    /// 之后桥就死了，而那颗桥是这扇窗唯一的用处（账本 P-248 后半第三颗）。
    /// <para>放行那一支<b>⛔</b> 留痕：每一趟开场都走它（<c>Navigate</c> 那一发），落行＝把噪声写成读数。
    /// 口径与观察者<b>同一颗判据</b>（<see cref="BoardsAssetPolicy.IsOutboundDenyUri"/>）：
    /// 随包那一份、压根⛔ 出机的那一颗（<c>about:blank</c> 就在这一档，控件起步要用它）、以及读不懂的那颗串都放行——
    /// ⛔ 拿一颗读不懂的串去挡一条也许本机的导航，代价是画布白屏。</para>
    /// </summary>
    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var raw = e.Uri;
        Uri? uri = raw is not null && Uri.TryCreate(raw, UriKind.Absolute, out var parsed) ? parsed : null;
        if (!BoardsAssetPolicy.IsOutboundDenyUri(uri, VirtualHostName)) return;
        e.Cancel = true;
        _navBlocked++;
        StarMark.Abstractions.StarLog.Warn(
            "boards.nav.blocked n=" + _navBlocked
            + " target=" + BoardsAssetPolicy.DescribeRequestTarget(uri) + " id=" + _boardId);
    }

    /// <summary>桥是否已就绪（页面递过 <c>bridge.ready</c>）。</summary>
    public bool BridgeReady => _bridgeReady;

    /// <summary>
    /// <b>子帧</b>要导航去<b>别处</b>（画布上那颗 <c>embeddable</c> 元素／档里带的 <c>iframe</c>）——
    /// 上一那颗顶帧闸门<b>压根管不到它</b>：账本 P-252 的读数就是「摘掉 CSP 递一颗远程 iframe，宿主观察者一颗也没听到」。
    /// <para>口径照 <see cref="OnNavigationStarting"/> 逐条搬：同一颗出站判据（<see cref="BoardsAssetPolicy.IsOutboundDenyUri"/>）、
    /// 那颗串只<b>读</b>一次且⛔ 进日志（它由页面说了算；<c>e.Name</c> 那一项更是页面起的，⛔ 落痕）、
    /// 放行那一支⛔ 留痕（<c>about:blank</c> 与本机 iframe 每一趟开场都要走它，控件起步与画布自己的子帧都在这一档）。
    /// ⚠ 这一手<b>翻成 deny</b>（⛔ 只记）：远程 iframe 带进来的不是画布的承重件（随包那八颗全住虚站），
    /// 而第一道 CSP 早已把 <c>frame-src</c> 关到⛔ 通——这一颗是<b>页⛔ 再带 CSP 时</b>的那道退路。</para>
    /// </summary>
    private void OnFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var raw = e.Uri;
        Uri? uri = raw is not null && Uri.TryCreate(raw, UriKind.Absolute, out var parsed) ? parsed : null;
        if (!BoardsAssetPolicy.IsOutboundDenyUri(uri, VirtualHostName)) return;
        e.Cancel = true;
        _frameNavBlocked++;
        StarMark.Abstractions.StarLog.Warn(
            "boards.frame.nav.blocked n=" + _frameNavBlocked
            + " target=" + BoardsAssetPolicy.DescribeRequestTarget(uri) + " id=" + _boardId);
    }

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
            // 名单外的名字／坏形状只留一行痕：宿主→页面那一路⛔ 有一句"回话"的立锥之地（P-245 撤掉的就是那一族）。
            // ⛔ 把 <c>envelope.Cmd</c> 递进日志——这颗名字正是被拒的那一颗，由页面说了算（日志伪造那一族，P-13）。
            StarMark.Abstractions.StarLog.Warn("boards.bridge.rejected fault=" + fault + " id=" + _boardId);
            return;
        }

        switch (envelope.Cmd)
        {
            case "bridge.ready":
                _bridgeReady = true;
                OnBridgeReady();          // 那一行痕在它里面（seq／id／hasScene）
                break;
            case "board.loaded":
                // 页面报"那份档落进画布了"＋两颗数。开闸只认<b>数</b>：宿主递进去几颗、档里读到几颗、画布上现在几颗，
                // 三颗全等才算"回到那块板"（片26 量到的是"promise 解掉了、页面回了话，而画布是空的"⇒ 那句话本身⛔ 是证据）。
                var want = BoardPolicy.CountWritableElements(_seenScene);
                if (BoardPolicy.ShouldArmAfterLoad(want, envelope.Elements, envelope.Canvas))
                {
                    ArmIdleBeat("loaded", " want=" + want + " loaded=" + envelope.Elements + " canvas=" + envelope.Canvas);
                }
                else
                {
                    StarMark.Abstractions.StarLog.Warn(
                        "boards.arm.refused want=" + want + " loaded=" + envelope.Elements + " canvas=" + envelope.Canvas
                        + " id=" + _boardId + " tries=" + _armTries);
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
                    // ⛔ 把那颗 URL 递进日志：它正是被拒的那一颗，可能带查询串里的凭据（BJ 那一族）。
                    StarMark.Abstractions.StarLog.Warn("boards.open.rejected id=" + _boardId);
                    // 这一支从前只留一行痕：他点了、画板没动、屏幕上⛔ 一个字——那是 P-249 那句
                    // "点了什么也不发生"的第二副脸（引擎默认那一支由片68 订了听众，这一支是<b>宿主自己</b>拒的，
                    // 更⛔ 静默：判据在我们的名单里，用户却无从知道那条链为什么不算）。
                    ShowLinkNotice("boards.linkBlockedBody");
                    break;
                }
                StarMark.Abstractions.StarLog.Info("boards.open.accepted id=" + _boardId);
                // 那一手到底开没开成要留下话（名册 422 那一族：⛔ 把动作尺的正件丢了）——从前这里 _ = 一发就完事，
                // 「系统里没有能处理 http 的默认程序」与「开成了」在痕上长同一张脸。上屏那一句＝片87 那一刀（账本 P-249 后半）。
                _ = ReportExternalOpenAsync(envelope.Url);
                break;
            case "export.bytes":
                HandleExportBytes(envelope);
                break;
            case "bridge.reply":
                HandleBridgeReply(envelope);
                break;
            default:
                // paste.query / pin.request / background.request：名字过了门、回路却还没接（⛔ 假 allow）。
                // 这颗名进日志是安全的（它已过名字闸门＝集合由我们定死）；等各自批次落地时要给的是一颗
                // <b>登记进出向名单的回话</b>，⛔ 再拿一句没人听的出去（P-245 定案那一笔）。
                StarMark.Abstractions.StarLog.Warn("boards.cmd.not-wired cmd=" + envelope.Cmd + " id=" + _boardId);
                break;
        }
    }

    private void HandleExportBytes(BoardEnvelope envelope)
    {
        if (!BridgeProtocol.IsKnownExportKind(envelope.Kind))
        {
            // ⛔ 念出那颗档位：它正是名单外的哪一颗，由页面说了算。
            StarMark.Abstractions.StarLog.Warn("boards.export.kind.unknown id=" + _boardId);
            return;
        }
        byte[] bytes;
        // 那颗上限先问，且<b>排在解它之前</b>（片58＝P-241 的②件）：解 base64 那一手本身就跑在 UI 线程上，
        // 「解出来才知道多大」＝那一手的付担已经交了——25 MiB 那一发实测按住界面 273.6 ms（正本见
        // <see cref="StarMark.Core.Boards.BoardPolicy.SceneBytesCeilingBytes"/> 那一段）。
        if (BoardPolicy.InboundBytesOverCeiling(envelope.Bytes?.Length ?? 0))
        {
            RefuseOversizedEnvelope(envelope);
            return;
        }
        try { bytes = Convert.FromBase64String(envelope.Bytes ?? string.Empty); }
        catch (FormatException)
        {
            StarMark.Abstractions.StarLog.Warn(
                "boards.export.decode.failed b64Len=" + (envelope.Bytes?.Length ?? 0) + " id=" + _boardId);
            return;
        }
        // 那颗档位决定落点（判据住 Core）：scene＝这颗板的本体→板档目录、档名＝那颗 id、滚 .bak；
        // png／svg＝一次性产物→导出目录、名字带时刻。两族⛔ 共用一份命名规则。
        if (BridgeProtocol.LandsInSceneArchive(envelope.Kind))
        {
            // 那一行第二颗读数在这里跟上：这一份字节＝用户<b>此刻屏幕上那块</b>，无论下面写闸开不开（P-255）。
            // 放在写闸判定<b>之前</b>是有意的——拒掉那一臂里用户刚贴的外链照样在屏幕上空着，只说"没存上"⛔ 说"那一块为什么是空的"。
            RememberHiddenEmbeddables(bytes);
            // 写闸（P-223）：这一趟页面还没证明自己"回到了宿主给它的那块板"，它交回的场景字节就⛔ 许落盘。
            // ⛔ 挡的只是这一族——png／svg 是一次性产物、另起一颗档名，盖不到任何板档（所以那颗门在档位分流之后）。
            // 拒掉的字节也⛔ 记进 _seenScene：记了＝下一趟恢复会把这块空场景当"见过的那一份"递回去，
            // 那才是把一次白屏变成永久丢板的一手。
            if (!_autosaveArmed)
            {
                StarMark.Abstractions.StarLog.Warn(
                    "boards.scene.write.refused notArmed bytes=" + bytes.Length + " id=" + _boardId);
                // 拒掉⛔ 等于"那几笔没发生"：片33 坏臂逐字读到用户那一笔只走到这一行之前
                // （<c>boards.scene.write.refused notArmed bytes=2810</c>），而那颗按钮当时交出去的是载入那一份
                // （858 字节／2 颗可写元素）。这里单独留一份给那颗按钮，⛔ 动 _seenScene／⛔ 动盘上主档：
                // P-223 那条"把空场景当见过的那一份递回画布"的路照旧关门——上面那半段注释说的就是它，⛔ 因为这一颗而改口。
                _refusedScene = bytes;
                // 这一拍如果正被关窗那一拍等着，"拒"与"没回答"对那颗等待是同一颗答案，对用户却是两回事：
                // 前者那几笔<b>确实一颗也没进盘</b>，而那句话必须让他看见（见 <see cref="_closingRefused"/>）。
                _closingRefused = _closingLanded is not null;      // 只有那一问在等时才配改它（与那颗 same= 一问一复位同口径）
                // 这一路与"停笔那一拍没开闸"是同一条静默面，而它更坏：用户已经画下的那几笔<b>一颗也没落盘</b>。
                // ⚠ 而<b>同一件旧话不许把新事实顶掉</b>（片39 真机量到的那一发）：那颗「打开所在文件夹」按成功之后
                // 同一拍里这次失焦又撞开一次写闸拒⇒「这一扇已停止保存」整句盖掉「已另存到 &lt;路径&gt;」、
                // 那颗新按钮跟着退场＝用户刚拿到的那条出口从屏幕上消失（正本 readings_39_184213）。
                if (!NoticeSaysFileOutcome) ShowArmStuckNotice();
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
                // 这一拍从前只由那句回话留下痕迹（而它⛔ 有读者）：不动盘也是宿主做过的一颗决定，得有痕。
                StarMark.Abstractions.StarLog.Info("boards.scene.unchanged bytes=" + bytes.Length
                    + " id=" + _boardId + " closing=" + (_closingLanded is not null));
                FinishClosingFlush();
                return;
            }
            // 那一发摸盘的活（读盘上那份＋滚 <c>.bak</c>＋写 <c>.tmp</c>＋改名）交进线程池（片58＝P-241 的①件）。
            // 改前的形状是<b>同步 I/O 跑在 UI 线程上</b>，而停笔那一拍每 1.5 s 就来一回。<b>真机量到的代价</b>（正本
            // <c>bin/WI98/readings_58_r4_*.txt</c>；尺＝<c>SendMessageTimeoutW(WM_NULL)</c> 的往返＝那一拍里 UI 线程
            // 真正不肯收活的长度）：<b>67 KB → 15.6 ms｜2 MB → 41 ms｜8 MB → 100～116 ms</b>，而工作集在一拍里跳 <b>+72 MB</b>。
            // ⚠ 那些颗数里<b>还⛔ 算上引擎那一侧的 serialize</b>——真带图的板按片13 那条曲线（照片 1440 每张 +0.32 MiB／
            // 噪声 1440 每张 +9.08 MiB）轻易几十 MB，那一档宿主同样得整发同步付。
            // ⚠ 这一刀搬的<b>⛔ 是判据</b>：写闸（<see cref="_autosaveArmed"/>）、那颗脏位
            // （<see cref="BoardPolicy.DiskAlreadyHolds"/>）与三行痕的分支选择照旧留在 UI 线程
            // （账本 P-241① 那句「⛔ 顺手把写闸搬走＝把 ✅P-223 换一副坏」）。
            var closingBeat = _closingLanded is not null;   // 当场抄下来：续体回来时那一颗位已被关窗那一拍清掉
            _ = SettleSceneWriteAsync(QueueSceneWrite(bytes), closingBeat);
            return;
        }
        // 落点是桌面那一支（片148＝深析 §四 第2行）：问的是同一发字节，只是它⛔ 进导出目录、也⛔ 参与
        // 「打开所在文件夹」那颗资格（下面那一路的 landed 由真写成文件的那一支给）。
        if (_exportPins && BridgeProtocol.ExtensionForKind(envelope.Kind) == ".png")
        {
            PinExportedPng(bytes);
            return;
        }
        var name = BridgeProtocol.UniqueExportName(
            BridgeProtocol.SanitizeExportName(envelope.Suggested, ExportStem(), envelope.Kind),
            candidate => File.Exists(Path.Combine(ExportsDirectory, candidate)),
            BridgeProtocol.SanitizeExportName(null, ExportStem(), envelope.Kind));
        if (name is null)
        {
            // 带序号的那一族连兜底名一起被占满（同板同秒连按到底才摆得出这一档）。
            // ⛔ 硬写＝把别人写好的东西抹掉，也⛔ 许让那句"已存到"指着别人的那颗档（P-243）。
            StarMark.Abstractions.StarLog.Warn("boards.export.failed kind=" + envelope.Kind
                + " bytes=" + bytes.Length + " why=NameTaken id=" + _boardId);
            ReportExport("boards.exportFailedBody", null);
            return;
        }
        var target = Path.Combine(ExportsDirectory, name);
        try
        {
            EnsureExports();
            File.WriteAllBytes(target, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 坏的那一路⛔ 静默（右上角那一行此刻就是这一手唯一的脸），原因进日志（P-56 口径）。
            // ⛔ 在这里 ShowNotice：那块面板此刻可能正挂着"这一扇已停止保存"，一句坏话⛔ 有权把另一句更坏的顶掉。
            StarMark.Abstractions.StarLog.Warn("boards.export.failed kind=" + envelope.Kind
                + " to=" + target + " bytes=" + bytes.Length + " why=" + ex.GetType().Name + " id=" + _boardId);
            ReportExport("boards.exportFailedBody", null);
            return;
        }
        StarMark.Abstractions.StarLog.Info("boards.export.saved kind=" + envelope.Kind
            + " bytes=" + bytes.Length + " scale=" + _sentScale + " background=" + _sentBackground
            + " to=" + target + " id=" + _boardId);
        ReportExport("boards.exportSavedBody", target);
    }

    /// <summary>
    /// 把那一发 PNG 贴上桌面（片148＝深析 §四 第2行：画板导出 → 贴桌面）。
    /// <para>解码吃仓里那一颗<b>纯托管</b> PNG 解码器（<c>ClipboardPayload.TryDecodePng</c>），⛔ 为这一格
    /// 另写一份"从 PNG 到 BGRA"；摆位与剪贴板那张图贴桌面共用同一条判据（<c>ItemCardPolicy.CenteredPlacement</c>：
    /// 光标所在屏工作区居中，探不到就老实退回 (0,0) 并留一行痕——那张窗仍可拖走，而拒绝执行没有第二次机会）。</para>
    /// <para>解不开＝右上角那一行走"没能弄成"那一句，⛔ 静默：那串字节是页面刚交回来的，用户有权知道它没落地。
    /// "共几张、上限几张"那句由 <c>PinManager</c> 的通知卡统一说，这里再报一遍＝同一件事两种措辞。</para>
    /// </summary>
    private void PinExportedPng(byte[] png)
    {
        if (!StarMark.Abstractions.Clipboard.ClipboardPayload.TryDecodePng(png, out var frame, out var whyNot))
        {
            StarMark.Abstractions.StarLog.Warn("boards.pin.decode.failed bytes=" + png.Length
                + " why=" + whyNot + " id=" + _boardId);
            ReportExport("boards.exportFailedBody", null);
            return;
        }
        var work = StarMark.UI.Helpers.WindowInterop.MonitorWorkAreaAtCursor()?.Work;
        if (work is null) StarMark.Abstractions.StarLog.Info("boards.pin.no-cursor-monitor id=" + _boardId);
        var placement = StarMark.Abstractions.ItemCardPolicy.CenteredPlacement(
            work?.X ?? 0, work?.Y ?? 0, work?.Width ?? frame.Width, work?.Height ?? frame.Height,
            frame.Width, frame.Height);
        StarMark.UI.Services.ScreenshotService.PinPixels(frame.Bgra, frame.Width, frame.Height, placement);
        StarMark.Abstractions.StarLog.Info("boards.pin.sent bytes=" + png.Length
            + " w=" + frame.Width + " h=" + frame.Height + " id=" + _boardId);
        ReportExport("boards.pinnedBody", null);
    }

    /// <summary>
    /// 造这扇窗那一手所在的<b>托管线程号</b>（⛔ 是一颗由 UI 那边递进来的数，当场读一次）。
    /// <para>它只有一位消费者：落地那一格那行痕里的 <c>onUi=</c> 那一列。<b>为什么非要量这一颗</b>：
    /// 把写盘交进线程池之后，那句「续体会回到 UI 线程的消息泵」本来只是我按框架习惯做的<b>推断</b>，
    /// 而推断⛔ 能写进日志当读数（坑表那条「注释里的『实测』也要先有读数」）——
    /// 那一列读的是<b>真发生的那一下</b>：续体真回到建控件树那一颗线程上吗。</para></summary>
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;

    /// <summary>
    /// 那一发整档写的<b>串行闸门</b>（片58＝P-241 的①件）。
    /// <para><b>为什么必须有这一颗</b>：<c>BoardSceneStore</c> 那颗临时件的档名是 <c>&lt;主档&gt;.tmp</c>——
    /// <b>同一颗板上两发同时在写</b>就是互相踩同一颗临时件（那一次改名会盖掉对方还没写完的一份）。
    /// 改前那一形状由 UI 线程<b>替你</b>排了队（同步写＝天然串行），搬进线程池之后这颗顺序必须由我们自己钉住。</para>
    /// <para><b>丢掉中间那一发为什⛔ 丢东西</b>：每一发交回的都是<b>整块板的快照</b>而⛔ 增量，后一发覆盖前一发的内容
    /// ⛔ 丢那一笔，丢掉的只有「同一块板再写一遍」那次 I/O。<b>而最后一拍⛔ 会被丢</b>——它的等待方
    /// （<see cref="FlushSnapshotAsync"/>）就在下面那一格里等它<b>真的</b>落地，所以 <c>landed=True</c> 的老意思一个字节也⛔ 改。</para>
    /// </summary>
    private System.Threading.Tasks.Task _sceneGate = System.Threading.Tasks.Task.CompletedTask;

    /// <summary>那一发写盘的三种下落（只有「真写成」那一发才配把脏位往前推）。</summary>
    private readonly record struct SceneWrite(bool Ok, string? Fault, bool RolledBackup, byte[] Bytes);

    /// <summary>
    /// 那一发交回的字节<b>过上限</b>：⛔ 解它，也⛔ 让它静默（片58＝P-241 的②件）。
    /// <para><b>两种档位各说各的<b>已有</b>那句话</b>（本批⛔ 添一颗键：那两句都住在画板那一族的名册里，
    /// 由 <c>BoardWordGateTests.TheBoardFamilyHasExactlyThirtyThreeRowsAndTheyAreNamed</c> 逐颗点名钉住）：
    /// scene＝这一扇的自动保存对这一发起停了＝片27 那句「这一扇已停止保存」＋那颗「另存一份到别处」；
    /// png／svg＝那一发导出没成＝片54 那句「没能存成，原因在日志里」。⚠ 两句都是<b>真话</b>：
    /// 前者确实停了（这块板超限期间不再自动落盘），后者确实没落盘。</para>
    /// <para><b>那颗按钮交出去的是手里那份而⛔ 这一发</b>：过限的字节⛔ 进 <c>_refusedScene</c>（要它就得先解＝
    /// 这一刀省的正是那一手），于是 <see cref="CopySeenSceneAsideAsync"/> 那格会挑载入那一份，
    /// 而那句成功话用的是 <c>boards.copySavedStaleBody</c>——<b>它本来就明说交的是旧的那一份</b>，⛔ 把这事说成"你的笔迹保住了"。</para>
    /// </summary>
    private void RefuseOversizedEnvelope(BoardEnvelope envelope)
    {
        var b64 = envelope.Bytes?.Length ?? 0;
        // ⚠ 那颗 kind 已过枚举闸门（<see cref="BridgeProtocol.IsKnownExportKind"/>），所以念得出而⛔ 是外人给的串。
        StarMark.Abstractions.StarLog.Warn("boards.scene.inbound.too-big b64=" + b64
            + " decoded~=" + BoardPolicy.InboundDecodedBytesUpperBound(b64)
            + " ceiling=" + BoardPolicy.SceneBytesCeilingBytes
            + " kind=" + envelope.Kind + " id=" + _boardId);
        if (!BridgeProtocol.LandsInSceneArchive(envelope.Kind))
        {
            ReportExport("boards.exportFailedBody", null);   // 那一发导出的三件收口（那句／那颗闩／那颗闹钟）同一位主人
            return;
        }
        // 过限与「写闸没开」对那一拍是同一颗答案：拒。而⛔ 把这一发记进 _seenScene（P-223 定死过的坏法）——
        // 那份字节压根没被解出来，它连"宿主见过的那一块板"都⛔ 是。
        _closingRefused = _closingLanded is not null;
        if (!NoticeSaysFileOutcome) ShowArmStuckNotice();
        FinishClosingFlush();
    }

    /// <summary>把那一发摸盘的活交进线程池，排在<b>上一发后面</b>（串行，理由见 <see cref="_sceneGate"/>）。
    /// <para>判据要的「盘上那一份」由 <c>TrySave</c> 自己在<b>写的那一刻</b>读——改前也是在那一刻读，所以
    /// 「该不该滚 <c>.bak</c>」这一问的口径⛔ 变（✅P-234 那两半原样生效，包括「盘上那份读不出是一颗档⛔ 滚」）。</para>
    /// <para><b>片146 在这一发后面又接了一手</b>：写成之后把那块板的正文补进收藏系统那一行
    /// （<see cref="BoardSceneIndex.RegisterAfterSave"/>）。它排在<b>同一颗串行闸里</b>而⛔ 另起一圈：
    /// 两发写盘互踩已经是要命的事，两发「写库」互踩是同一颗坏换了个地方。⚠ 它⛔ 在 <c>run</c> 的返回值里——
    /// 那颗 <c>Ok</c> 说的是「用户的笔迹在不在盘上」，索引件没跟上⛔ 配把它拧成 false（详见那一档）。</para></summary>
    private System.Threading.Tasks.Task<SceneWrite> QueueSceneWrite(byte[] bytes)
    {
        var run = _sceneGate.ContinueWith(_ =>
        {
            var ok = BoardSceneStore.TrySave(_boardId, bytes, out var fault, out var rolledBackup);
            if (ok) BoardSceneIndex.RegisterAfterSave(_boardId!, bytes);
            return new SceneWrite(ok, fault, rolledBackup, bytes);
        }, System.Threading.CancellationToken.None,
           System.Threading.Tasks.TaskContinuationOptions.None,
           System.Threading.Tasks.TaskScheduler.Default);
        _sceneGate = run.ContinueWith(_ => { });    // 下一发排在它后面；这一句⛔ 把结果再排回 UI 线程＝那样等于没搬
        return run;
    }

    /// <summary>
    /// 那一发落地之后回 UI 线程收尾：脏位只由<b>真写成</b>的那一发推，痕照旧逐颗留，
    /// 而关窗那一拍的等待<b>到这一格才结</b>——「字节真的在盘上了」才是那句 <c>landed</c>。
    /// <para><b>为什⛔ 怕「两发的收尾互踩」</b>：写由 <see cref="_sceneGate"/> 串行⇒完成顺序＝交出去的顺序；
    /// <b>再加上</b>每一发的续体真排回同一颗 UI 线程的消息泵（FIFO），才有「<b>最后落笔的那一发最后写脏位</b>」这一句。
    /// ⚠ 后面那一半<b>今天还没读数</b>：它由那一行痕里的 <c>onUi=</c> 那一列报（那颗数＝<see cref="_uiThreadId"/> 对当场那颗线程号），
    /// 下一跑真机读数才能把它从推断换成读数。要是那一列读回 <c>False</c>，这一格就得重做——
    /// 脏位与那颗等待由各线程池的续体各写各的，「最后一发最后落」这句⛔ 成立。</para>
    /// <para>这条推理⛔ 靠「我觉得 await 会回来」：把脏位交回<b>发出去</b>那一拍、或把 <c>FinishClosingFlush()</c>
    /// 提到写<b>之前</b>，两样都必须有证人当场响（<c>BoardWindowBridgeGateTests</c> 那一格＋<c>bench_58.py</c> 九臂，
    /// 正本 <c>bin/WI98/bench58_console_r1.txt</c>＝臂 9／BAD 0）。</para>
    /// <para>⚠ <b>进程退出时还在排队的那一发</b>：写本身仍是「先 <c>.tmp</c> 后改名」，死在半路留下的是一颗废弃临时件
    /// ＋一份完整主档——✅P-223 那套原子性⛔ 因为换了线程就消失（<c>BoardSceneStore</c> 档头那段「写的顺序是有原因的」）。</para>
    /// </summary>
    private async System.Threading.Tasks.Task SettleSceneWriteAsync(System.Threading.Tasks.Task<SceneWrite> write, bool closingBeat)
    {
        SceneWrite done;
        try
        {
            done = await write;
        }
        catch (System.Exception ex)
        {
            // 写手抛了＝这一发既没进盘也没留下一句话——正是本仓定性过的那类坏（P-53：写盘失败⛔ 静默）。
            // 只念故障类型：那颗 message 里可能带路径与外人的字（P-13 那条 CWE-117 的口径）。
            StarMark.Abstractions.StarLog.Warn("boards.scene.write.threw type=" + ex.GetType().Name + " id=" + _boardId);
            if (closingBeat) FinishClosingFlush();
            return;
        }

        if (done.Ok)
        {
            _lastWritten = done.Bytes;   // 这一刻起盘上主档就是这份字节（⛔ 在「发出去」时就当已经写了）
            // <c>onUi=</c> 那一列才是「那一发真离过 UI 线程、续体又真回来了」的读数（⛔ 由我写死一颗 <c>True</c> 冒充证据）。
            StarMark.Abstractions.StarLog.Info("boards.scene.saved id=" + _boardId
                + " bytes=" + done.Bytes.Length + " rolled=" + done.RolledBackup
                + " tid=" + Environment.CurrentManagedThreadId
                + " onUi=" + (Environment.CurrentManagedThreadId == _uiThreadId)
                + " closing=" + closingBeat);
        }
        else
        {
            StarMark.Abstractions.StarLog.Warn("boards.scene.save.failed: " + done.Fault
                + " tid=" + Environment.CurrentManagedThreadId
                + " onUi=" + (Environment.CurrentManagedThreadId == _uiThreadId));
        }
        if (closingBeat) FinishClosingFlush();       // 那一拍的等待由「交出去」挪到「真落地」
    }

    /// <summary>
    /// 页面那句"那一办发不出去"的<b>唯一读者</b>（片56＝P-244 的第二处）。
    /// <para>改前的形状＝这颗名字压根⛔ 在进向名单里，于是每一句 nack 都死在 <see cref="BridgeProtocol.Check"/>
    /// 的名字闸门（<c>fault:UnknownName</c>），连这一格都走不到。后果⛔ 只是"少一句解释"，而是<b>那一闩永远不放开</b>：
    /// 真机逐字（正本 <c>bin/WI95/readings_55_171814.txt</c>）＝按下去之后第 26 行那颗「导出 SVG」<c>可点=False</c>、
    /// 第 29 行 <c>最后一按 8.00 秒落=空</c>、第 32 行连那颗「导出 PNG」也 <c>可点=False</c>——一发坏掉把同一行两颗入口一起关死。</para>
    /// <para><b>只在"还等着"时开口</b>：那颗 <c>_exporting</c> 已经放开＝这一问早就由那两份字节（或那颗预算）结掉了，
    /// 迟到的一句 nack⛔ 许再替它说一遍（否则就是把同一件坏话贴到用户下一次尝试的落点上，片32 那族"上一问的脸活到下一问"）。</para>
    /// <para>⚠ 认不出的那颗 <c>ok</c>（null）与 <c>ok=True</c> 一律不动：<b>现读页面侧只在办不成时出声</b>
    /// （成功那一路交的是 <c>export.bytes</c>）。这里⛔ 为那颗还不存在的"办成了"写一臂——恒不执行的一格会把
    /// 坏掉的那一支衬得像已经有人管（名册 466 那一问）。同样⛔ 回一句 ack：那句话在页面侧⛔ 有读者
    /// （<c>onOut</c> 的 default 只 break），多一颗没人读的键正是硬线②挡的那类东西。</para>
    /// </summary>
    private void HandleBridgeReply(BoardEnvelope envelope)
    {
        if (envelope.Ok is not false || !_exporting) return;
        StarMark.Abstractions.StarLog.Warn(
            "boards.export.nack err=" + (envelope.Err ?? "unknown") + " id=" + _boardId);
        ReportExport("boards.exportFailedBody", null);   // 那句话＋那颗闩＋那颗闹钟，都由同一颗主人结掉
    }

    /// <summary>
    /// 那一发导出问出去之后<b>最多等这么久</b>（片56＝P-244 的第三处：一句⛔ 回音的话⛔ 许把两颗入口长关）。
    /// <para>为什么⛔ 只靠那颗 nack 就够：nack 只覆盖"页面知道自己抛了"那两档。还有两条路一句也发不出来——
    /// 页面那颗 React 树整个没了（渲染进程挂了那一拍之后）、以及消息在半路丢。到那时屏幕上留的仍然是
    /// "点了没反应"加两颗永远点不亮的入口，而用户手上没有一颗自愈的按钮。</para>
    /// <para>醒来先比两颗：<see cref="_exportGen"/> 认得"问的是哪一发"，<c>_exporting</c> 认得"那一发还没结"。
    /// 窗已经拆了（<c>_tearingDown</c>）就一句话也⛔ 说——那一拍连表面都没了。</para>
    /// <para>⚠ 这一手⛔ 改盘上任何东西：它只放开那一闩并换一句话。所以<b>迟到的成功照样算数</b>——
    /// 那份字节若真回来了，<see cref="HandleExportBytes"/> 会把那句改回"已存到"并重新给那颗出口按钮，
    /// 日志里 <c>boards.export.stalled</c> 与 <c>boards.export.saved</c> 两行前后排着，就是这一格的真面目。</para>
    /// </summary>
    private async System.Threading.Tasks.Task WatchExportAnswerAsync(int gen)
    {
        await System.Threading.Tasks.Task.Delay(BoardPolicy.ExportAnswerBudgetMs);
        if (gen != _exportGen || !_exporting || _tearingDown) return;
        StarMark.Abstractions.StarLog.Warn(
            "boards.export.stalled ms=" + BoardPolicy.ExportAnswerBudgetMs + " gen=" + gen + " id=" + _boardId);
        ReportExport("boards.exportFailedBody", null);
    }

    /// <summary>右上角那一行报一句落点（成功＝连那颗档一起记，失败＝只换话、那颗上一次真落盘的档⛔ 被顶掉）。
    /// <para>那颗 <c>landed</c> 只许由<b>真写成</b>的那一路给：<see cref="_exportPath"/> 是那颗
    /// 「打开所在文件夹」的唯一资格，写了这颗就得保证盘上真有它（P-219 那句"界面上要给出口"的另一半）。</para>
    /// <para>片56 起它同时是那一闩的<b>唯一收口处</b>：三条下落（字节真回来／页面说一句办不成／那颗预算到点）
    /// 都走这一颗，所以"放开那两颗入口"这件事⛔ 长在第二个地方——改前的坏正是它只长在"字节真回来"那一支。</para></summary>
    private void ReportExport(string outcomeKey, string? landed)
    {
        if (landed is not null) _exportPath = landed;
        _exportOutcomeKey = outcomeKey;
        _exporting = false;
        // 那颗落点位跟这一闩一起结：三条下落都走这一处，所以"这一发是贴到桌面"⛔ 活到下一发（见那颗位的注释）。
        _exportPins = false;
        PasteBar();
    }

    /// <summary>
    /// 右上角那一行按当前语言重贴（那颗按钮上的字与那句落点都归这一拍；换语言⛔ 走 <see cref="PasteNotice"/> 以外的那条路＝
    /// P-175 那一族"一颗重贴一颗冻着"的第三种脸）。
    /// <para>两颗入口的可点状态由<b>两颗</b>事实决定：桥通不通（⛔ 通就发问＝发进一颗没人听的口子）、
    /// 这一发是不是正在跑（跑的时候点得亮＝两发抢同一刻的落点）。这里⛔ 判"该不该看得见"：那一行永远在场，
    /// 因为它说的是"我刚点的那一下怎么样了"。</para>
    /// </summary>
    private void PasteBar()
    {
        if (_barStatus is null || _barPngButton is null || _barSvgButton is null
            || _barScaleButton is null || _barBackgroundButton is null || _barFolderButton is null
            || _barPinButton is null) return;
        var live = _bridgeReady && !_exporting;
        _barPngButton.Content = Word("boards.exportPngAction");
        _barSvgButton.Content = Word("boards.exportSvgAction");
        _barPngButton.IsEnabled = live;
        _barSvgButton.IsEnabled = live;
        // 「贴到桌面」与「导出 PNG」吃同一发问、同一副闸：它是那颗 PNG 的<b>另一种下落</b>，⛔ 是第三条档位
        // （档位名单归 BridgeProtocol.ExportKinds，那里多一颗才是真多一种产物）。
        _barPinButton.Content = Word("boards.pinAction");
        _barPinButton.IsEnabled = live;
        // 那两颗扳机⛔ 跟着 live 那一闩一起关：挑档是宿主自己的事，桥还没通也照样能先挑好；
        // 把它们点不亮＝把"这一刻还没通"变成"这台程序没有这两颗旋钮"（片33 那颗可点状态的同一条口径）。
        _barScaleButton.Content = Phrase("boards.exportScaleAction", ScaleToken(_exportScale));
        _barBackgroundButton.Content = Phrase("boards.exportBackgroundAction", Word(BackgroundKey(_exportBackground)));
        _barFolderButton.Content = Word("boards.openFolderAction");
        _barFolderButton.IsEnabled = _exportPath is not null;
        _barStatus.Text = _exportOutcomeKey switch
        {
            // 那句"已存到"从此<b>带上那一发真用的两档</b>（P-242：从前它只说路径，而那张图的尺寸由那台机器的缩放定）。
            // 念的是 <see cref="_sentScale"/>／<see cref="_sentBackground"/>＝问出去那一刻记下的那两颗，⛔ 手里此刻那颗。
            "boards.exportSavedBody" => Phrase("boards.exportSavedBody", _exportPath ?? string.Empty,
                Phrase("boards.exportScaleAction", ScaleToken(_sentScale)),
                Phrase("boards.exportBackgroundAction", Word(BackgroundKey(_sentBackground)))),
            "boards.exportFailedBody" => Word("boards.exportFailedBody"),
            // 那一发没落文件，所以它⛔ 带路径：「打开所在文件夹」那颗照旧指着上一次真写成档的那一份
            //（那颗资格的唯一写处在上面，⛔ 在这里给）。
            "boards.pinnedBody" => Word("boards.pinnedBody"),
            "boards.openFolderFailedBody" => Phrase("boards.openFolderFailedBody", _exportPath ?? string.Empty),
            _ => string.Empty,
        };
    }

    /// <summary>那一行第二颗读数按当前语言重贴一次（<see cref="StarMark.Abstractions.I18n.Locale.Changed"/> 那一拍与
    /// "手里那份字节变了"那一拍都走它——片34 那颗按钮上的话漏掉的就是同一拍）。
    /// <para>那颗数是<b>数据</b>，进 <c>Fmt</c> 的实参，⛔ 在这里拼成品句（与那句"已存到 &lt;路径&gt;"同一条口径）。
    /// <c>null</c>（那份字节读不懂）与 <c>0</c>（确实没藏）在这一行上说同一种话＝都⛔ 开口：
    /// "我读不懂这一发交回来的档"是宿主自己的事，进日志（P-193 那条"只进日志⛔ 上屏"的口径），⛔ 拿它吓用户。</para></summary>
    private void PasteHiddenEmbedNotice()
    {
        if (_barHiddenEmbeds is null) return;
        var count = _hiddenEmbeddables ?? 0;
        _barHiddenEmbeds.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count > 0) _barHiddenEmbeds.Text = Phrase("boards.embedHiddenBody", count);
    }

    /// <summary>那一列数的<b>唯一写处</b>：宿主每见过一份完整场景字节就问一次 Core（开场与页面交回那两拍都走这一颗，
    /// ⛔ 两处各解一遍档）。读到 0 也照样重贴一次——用户删掉那颗外链框，那一行得当场退场（⛔ 另配一颗"清除"＝第二个主人）。
    /// <para>痕只在"从读得懂翻成读不懂"那一拍落一次：这一颗住在停笔／失焦那一拍上（每 1.5 秒一回），
    /// 每次都说＝把同一件坏话刷满日志（P-45 那条节流口径的同一条）。</para></summary>
    private void RememberHiddenEmbeddables(byte[]? sceneBytes)
    {
        var counted = BoardPolicy.CountHiddenEmbeddables(sceneBytes);
        if (counted is null && _hiddenEmbeddables is not null && sceneBytes is { Length: > 0 })
            StarMark.Abstractions.StarLog.Warn(
                "boards.embed.hidden unreadable bytes=" + sceneBytes.Length + " id=" + _boardId);
        _hiddenEmbeddables = counted;
        PasteHiddenEmbedNotice();
    }

    /// <summary>那颗缩放念出来的样子（<c>2×</c>＝数字＋乘号，⛔ 跟着语言走，所以它是<b>数据</b>而⛔ 是一句表里的话）。
    /// <para>与 <see cref="BoardPolicy.ExportScales"/> 同一颗主人：数字来自名单，这里只加那颗记号。</para></summary>
    private static string ScaleToken(int scale) => scale + "×";

    /// <summary>那颗开关此刻念哪颗键（<c>开</c>／<c>关</c> 是措辞⇒进表，⛔ 在这里写死汉字）。</summary>
    private static string BackgroundKey(bool on) => on ? "boards.exportOn" : "boards.exportOff";

    /// <summary>那一发导出<b>问出去之前</b>把两档记下：屏幕上那句话、那行日志、那颗回音都念这一份（见 <see cref="_sentScale"/>）。</summary>
    private void RememberSentExportKnobs()
    {
        _sentScale = _exportScale;
        _sentBackground = _exportBackground;
    }

    /// <summary>那颗缩放按钮的活：按名单<b>循环</b>一档（1×→2×→3×→1×）。循环那一步的判据住 Core（<see cref="BoardPolicy.NextExportScale"/>）。</summary>
    private void CycleExportScale()
    {
        _exportScale = BoardPolicy.NextExportScale(_exportScale);
        PasteBar();
    }

    /// <summary>那颗背景按钮的活：翻那颗开关（默认与引擎自家 <c>exportBackground:!0</c> 对齐＝开着）。</summary>
    private void ToggleExportBackground()
    {
        _exportBackground = !_exportBackground;
        PasteBar();
    }

    /// <summary>
    /// 那一发导出：<b>宿主问、页面答、宿主写</b>（账本 P-240 的②）。档位只有在册那两档，
    /// 而这一处⛔ 判"该不该让他点"——那颗闸由 <see cref="PasteBar"/> 按两列事实算，这里只挡已经排上的第二发。
    /// <para>字节不在这里拿：页面把 blob 编成 base64 交回 <c>export.bytes</c>，落盘与那句话都在
    /// <see cref="HandleExportBytes"/> 那一头＝一次一问一答，⛔ 在这里等一颗不知道会不会来的回音。</para>
    /// <para>片56 起这一处还管<b>那一问的两条下落</b>：办不成了由页面说一句 nack（<see cref="HandleBridgeReply"/>），
    /// 一句也⛔ 说的则由那颗预算到点结掉（<see cref="WatchExportAnswerAsync"/>）——<b>开口之前</b>把代次记下，
    /// 这一处是那颗计数唯一的主人。</para>
    /// </summary>
    private void ExportImageButton(string kind, bool pinsInsteadOfSave = false)
    {
        if (!_bridgeReady || _exporting) return;
        if (!BridgeProtocol.IsKnownExportKind(kind)) return;      // ⛔ 在册的档位⛔ 发（⛔ 由这里替页面猜一档）
        _exporting = true;
        // 那颗落点位只在"这一发真按的是那颗贴到桌面"时立起来；档位⛔ 是 png 就⛔ 立（SVG 那条路没有像素）。
        _exportPins = pinsInsteadOfSave && kind == "png";
        _exportGen++;
        RememberSentExportKnobs();                                // 开口之前记：那一发用的两档从此⛔ 跟着他后来的挑法改口
        PasteBar();
        PostExportPick(kind);
        _ = WatchExportAnswerAsync(_exportGen);
    }

    /// <summary>那颗 <c>export.pick</c> 信封的唯一发送处（四拍里那两拍索取板档、右上角那一行索取图＝同一颗形状，⛔ 两处各拼一遍）。
    /// <para>片64 起这里同时递<b>那一发挑的两档</b>：缩放（在册名单 <see cref="BoardPolicy.ExportScales"/>）与那颗背景开关。
    /// ⚠ 这两颗字段住在 <c>args</c> 而⛔ 需进向名册——那颗名字闸门只查 <c>cmd</c>，字段归<b>对面</b>那一路自己认
    /// （片56 那颗 <c>bridge.reply</c> 同一条口径），而页面侧认不出的值一律回退到引擎自家那颗默认（见桥里 <c>runExport</c>）。</para></summary>
    private void PostExportPick(string kind)
        => PostOutbound("export.pick", Envelope("export.pick", "h4",
            JsonString("kind") + ":" + JsonString(kind) + ","
            + JsonString("exportScale") + ":" + _exportScale + ","
            + JsonString("exportBackground") + ":" + (_exportBackground ? "true" : "false")));

    /// <summary>
    /// 把一张图放上<b>这一块板</b>——<c>image.insert</c> 的唯一发送处（深析 §四 那三格共用的那一个出口）。
    /// <para>四问在这一侧问完，一问不过就交回<b>那一问的名字</b>并留一行痕，屏幕上那句由调用方说
    /// （三条入口各有各的出口，见 <see cref="BridgeProtocol.Insert"/>）：
    /// ① 桥通没通（没通就发＝发进一颗没人听的口子，连回音都⛔ 有——片56 那条口径）；
    /// ② 那颗 MIME 够不够格（名单住 Core，⛔ 由调用方自报）；
    /// ③ 那颗 dataURL 装不装得下（与写闸同一颗上限，见 <see cref="BoardPolicy.InjectedDataUrlOverCeiling"/>）；
    /// ④ 字节与那两颗数是不是正数。</para>
    /// <para>交回 <see cref="BridgeProtocol.Insert.Posted"/> 只说"那一发递出去了"，⛔ 说"盘上有了"：落盘归页面那一趟 autosave 与宿主那道写闸，
    /// 写闸拒了自有一套话（P-223 那一族）。⚠ 日志里⛔ 出现那颗 MIME——它来自调用方，
    /// 而日志伪造那一族（P-13）守的正是"把没判定过的串念成正文"这一手。</para>
    /// </summary>
    public BridgeProtocol.Insert TryInsertImage(byte[] imageBytes, string mimeType, int width, int height)
    {
        if (_tearingDown || !_bridgeReady)
        {
            StarMark.Abstractions.StarLog.Warn("boards.insert.no-bridge id=" + _boardId);
            return BridgeProtocol.Insert.NoBridge;
        }
        if (!BridgeProtocol.IsInjectableImageMime(mimeType))
        {
            StarMark.Abstractions.StarLog.Warn("boards.insert.mime.rejected id=" + _boardId);
            return BridgeProtocol.Insert.BadMime;
        }
        if (imageBytes is null || imageBytes.Length == 0 || width <= 0 || height <= 0)
        {
            StarMark.Abstractions.StarLog.Warn(
                "boards.insert.bad-shape w=" + width + " h=" + height + " id=" + _boardId);
            return BridgeProtocol.Insert.BadShape;
        }
        var dataUrl = BridgeProtocol.DataUrlOf(mimeType, imageBytes);
        if (BoardPolicy.InjectedDataUrlOverCeiling(dataUrl.Length))
        {
            StarMark.Abstractions.StarLog.Warn(
                "boards.insert.over-ceiling bytes=" + imageBytes.Length + " id=" + _boardId);
            return BridgeProtocol.Insert.OverCeiling;
        }
        PostOutbound("image.insert", Envelope("image.insert", "h6",
            JsonString("dataUrl") + ":" + JsonString(dataUrl) + ","
            + JsonString("mimeType") + ":" + JsonString(mimeType) + ","
            + JsonString("width") + ":" + width + ","
            + JsonString("height") + ":" + height));
        StarMark.Abstractions.StarLog.Info(
            "boards.insert.posted bytes=" + imageBytes.Length + " w=" + width + " h=" + height + " id=" + _boardId);
        return BridgeProtocol.Insert.Posted;
    }

    /// <summary>导出产物那颗档的主干名（唯一主人：桥那一路与那一发下载共用）。
    /// <para>⛔ 跟着那块板的<b>显示名</b>走：名字在这里是身份（哪一块、什么时候），⛔ 措辞——
    /// 显示名住在场景档里（✅P-218 那条定案），宿主⛔ 替它另存一份，也⛔ 拿它当产物档名（§18.4 现读：
    /// 那颗 <c>a.download</c> 交上来的本来就是颗 <c>blob:</c> 的 UUID）。</para></summary>
    private string ExportStem() => _boardId + "-" + BoardPolicy.FormatStamp(DateTimeOffset.Now);

    /// <summary>产物目录先存在再写（P-241 的③件）：这一档里那颗 <c>CreateDirectory</c> 只许出现一次＝一位主人。
    /// <para>⚠ 这一手收的是"谁说了算"，⛔ 省掉那颗系统调用——每一次写还是各要一次（要省它得改成 latch，
    /// 而 latch 会把"目录被用户删掉之后再也写不进"变成新坏，P-45 那一族的口径今天没到那一档）。</para></summary>
    private static void EnsureExports() => Directory.CreateDirectory(ExportsDirectory);

    /// <summary>
    /// 那一发导出的<b>落点与命名归宿主</b>（片54＝P-240 的②）：字节由页面交回（<c>export.pick</c>→<c>export.bytes</c>），
    /// 写盘与那句话都在 <see cref="HandleExportBytes"/> 与 <see cref="PasteBar"/> 这两处，引擎那颗框连同开它的那两颗名字一起关到⛔ 通。
    /// <para>本批第一版在这里订过 <c>DownloadStarting</c>（改 <c>ResultFilePath</c>＋<c>Handled＝true</c>），被真机当场否掉：
    /// 那颗 <c>#32770「另存为」</c>照样起来、而那颗事件一颗也没响（逐字与那份"尺不聋"的正见类文档那一段）。
    /// ⛔ 留那段代码"以后也许用得上"——它今天没有任何一条路走得到（随包那一路走的是 <c>showSaveFilePicker</c>），
    /// 而订了接管一颗事件＝把用户挑的落点改成宿主挑的，那是<b>拿到了却没问过</b>的一手。</para>
    /// </summary>
    private void OnBridgeReady()
    {
        _readySeq++;
        // 新页面诞生＝写闸重新闩上：这一趟的证据要由这一趟的 board.loaded 重新给（崩之前开过闸⛔ 顶这一趟）。
        _autosaveArmed = false;
        // 那一行"还在导出"那一闩也在这里放开：页面没了＝那一问永远不会有回音，而⛔ 让两颗入口永远点不亮＝
        // 把一次崩溃变成"那扇窗再也导不出图"（P-226 那条"按下去要有回音"的下游，与 <see cref="_retrying"/> 同一副规矩）。
        _exporting = false;
        PasteBar();
        // 片26 诊断面（P-222）：崩之后那两条恢复路都停在同一颗白帧，而"后一手真发出去了"与"页面真回来了"
        // 是两件事。这一行＋下面那颗 nav.done 各给一次读数，才把"Reload 选了错的一手"与"重载回来了却没载档"
        // 判得开（⛔ 靠猜换手）。
        StarMark.Abstractions.StarLog.Info(
            "boards.bridge.ready seq=" + _readySeq + " id=" + _boardId + " hasScene=" + (_seenScene is { Length: > 0 }));
        // 三颗初始值（§21.5）：宿主是它们的主人。主题与语言这一拍<b>递的是此刻的真值</b>（片147）——
        // 改前这两颗发的是硬编码占位（浅色＋中文），深色主题／英文／日文用户打开画板看到的仍是浅色中文。
        ReapplyAppearance();
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
        _refusedScene = null;          // 开过闸＝从这一刻起交回来的每一份都进 _seenScene，那颗可疑候选⛔ 再配在场
        _closeRevived = false;         // 这一扇能存了＝"走还是留"那一问当场作废（⛔ 让下一次换语言把两颗按钮又贴回来）
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
        // 片39＝P-219 那句「界面上⛔ 一颗出口告诉用户文件在哪」的落地腿：路径念出来之外，再给一颗能点的。
        // ⚠ 它⛔ 是"顺手加的一颗按钮"：片28 量到过这一屏上那颗按钮"白字画在灰底上＝看不见"，
        // 而这一扇窗上现在<b>有两颗</b>可点的，所以三样（字色／底色／描边）照样各钉一遍，⛔ 跟着主题走。
        _noticeFolderButton = new Button
        {
            Content = Word("boards.openFolderAction"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _noticeFolderButton.Click += async (_, _) => await OpenSavedFolderAsync();
        // 片40＝被关过又被亮回来的那一格：走／留都得就地给一颗。三样颜色照旧各钉一遍（这一屏上现在有四颗可点的，
        // 那颗"白字画在灰底上＝看不见"的尺从片28 起每加一颗都要重新量，⛔ 让新按钮吃主题刷）。
        _noticeStayButton = new Button
        {
            Content = Word("boards.stayAction"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _noticeStayButton.Click += (_, _) => StayOnBoard();
        _noticeLeaveButton = new Button
        {
            Content = Word("boards.leaveAction"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _noticeLeaveButton.Click += (_, _) => LeaveAnyway();
        // 片49＝那颗档现在拿不出来那一句的两颗。三样颜色照旧各钉一遍（⛔ 跟着主题走）：
        // 这一族每加一颗可点的控件，"白字画在灰底上＝看不见"那把尺就得重新量一次（片28 量到的正是这一格）。
        _noticeRetryButton = new Button
        {
            Content = Word("boards.retryAction"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _noticeRetryButton.Click += (_, _) => _ = RetryBlockedSceneAsync();
        _noticeFreshButton = new Button
        {
            Content = Word("boards.freshBoardAction"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _noticeFreshButton.Click += (_, _) => StartFreshBoard();
        // 片108＝P-257 (C) 的落地腿：那块面板从此<b>排两行</b>。第二行只归外链那两句坏话住——
        // 第一行挂着那句不带计时器的旧话时，「这条链接没递出去」照样上得了屏。
        // ⛔ 换掉旧话（那是片39 真机量到的"旧话顶掉新事实"那一族）、也⛔ 给它配一颗关闭：
        // 那句"没能打开"由时长自己退场（名单＝<see cref="LinkNoticeKeys"/>），而第一行由状态收。
        // 字色与那颗宽度上限照旧各钉一遍：这一屏上多了一行字，片28 那把"白字画在灰底上＝看不见"的尺
        // 与片12 那颗「浮出长句给宽度上限」的判据都要连着新那一行重新量（⛔ 让第二样撑宽那块面板）。
        _noticeLinkBody = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            MaxWidth = 420,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Visibility = Visibility.Collapsed,
        };
        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
        panel.Children.Add(_noticeTitle);
        panel.Children.Add(_noticeBody);
        panel.Children.Add(_noticeButton);
        panel.Children.Add(_noticeFolderButton);
        panel.Children.Add(_noticeStayButton);
        panel.Children.Add(_noticeLeaveButton);
        panel.Children.Add(_noticeRetryButton);
        panel.Children.Add(_noticeFreshButton);
        // 那块面板的底色与字色都<b>钉死</b>：画布那一侧永远是白板（theme.set 现在交的是 light），
        // 而一扇窗里两套深浅色同时说话就是 WI-81 那族"点了白色整扇看不见"的形状——两边都不跟着主题走，最坏也只是普通灰底。
        _noticeMain = panel;                   // 第一行连同它那六颗出口（收起第一行＝一起收起，⛔ 把按钮留在第二行旁边）
        var rows = new StackPanel();
        rows.Children.Add(_noticeMain);
        rows.Children.Add(_noticeLinkBody);
        _notice = new Border
        {
            Child = rows,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 250, 250, 250)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 178, 178, 178)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 16),
        };
        // 片54＝那一发导出的两颗入口＋那句落点（账本 P-240 的②：引擎那颗框关到⛔ 通之后，这一行就是唯一的一条路）。
        // ⚠ 它⛔ 住在那块面板里：面板只在有事要说时才在场，而"我刚点的那一下怎么样了"每一发都要有答案——
        // 而那块面板此刻可能正挂着"这一扇已停止保存"，一句好消息⛔ 有权把那句坏话顶掉（片39 真机量到的正是这一族）。
        // 三样颜色照旧各钉一遍（片28 那把"白字画在灰底上＝看不见"的尺：这一屏上现在多了三颗可点的控件）。
        // 片64＝那两档交回用户（P-242 的落地腿）。⚠ 它们走的是那颗<b>已经量过的 Button</b>，而⛔ 下拉、⛔ 复选框：
        // 这一行上每一颗可点的控件都由同一副"钉死三样颜色"的配方撑住（那颗尺同时钉 ThemeResource 计数为 0），
        // 而下拉与复选框的字色由那颗主题模板给＝深色主题下把白字贴到这块白底上＝片27／28 那一族"看不见"的下游。
        _barScaleButton = new Button
        {
            Content = Phrase("boards.exportScaleAction", ScaleToken(_exportScale)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _barScaleButton.Click += (_, _) => CycleExportScale();
        _barBackgroundButton = new Button
        {
            Content = Phrase("boards.exportBackgroundAction", Word(BackgroundKey(_exportBackground))),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _barBackgroundButton.Click += (_, _) => ToggleExportBackground();
        _barPngButton = new Button
        {
            Content = Word("boards.exportPngAction"),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _barPngButton.Click += (_, _) => ExportImageButton("png");
        // 「贴到桌面」（片148＝深析 §四 第2行）紧跟在「导出 PNG」后面：它俩是同一发的两种下落，
        // 中间插进那颗 SVG 会让人以为这一颗也产一张档。三样颜色照旧各钉一遍（同上面那几颗的理由）。
        _barPinButton = new Button
        {
            Content = Word("boards.pinAction"),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _barPinButton.Click += (_, _) => ExportImageButton("png", pinsInsteadOfSave: true);
        _barSvgButton = new Button
        {
            Content = Word("boards.exportSvgAction"),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _barSvgButton.Click += (_, _) => ExportImageButton("svg");
        // 那颗「打开所在文件夹」在这一行里<b>永远在场</b>，只是盘上还没落东西时点不亮：
        // 藏起来＝"这台程序没有这功能"，而它其实只是"这一刻还没有东西可开"（片33 那颗可点状态那一格的同一条口径）。
        _barFolderButton = new Button
        {
            Content = Word("boards.openFolderAction"),
            Margin = new Thickness(6, 0, 0, 0),
            IsEnabled = false,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        _barFolderButton.Click += async (_, _) => await OpenExportedFolderAsync();
        _barStatus = new TextBlock
        {
            Text = string.Empty,
            FontSize = 12,
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        // 第二颗读数归第二行（片82 真机量到的那一格＝账本 P-255）：那块板上有几颗外部内容永远不会出正文时，
        // 屏幕上必须有一句说得清"这一块空的不是坏了，是这一族我们故意不放行"。
        // ⚠ 它在"没有东西可说"时收起来，而⛔ 像上面那句落点那样留一行空字：那一行说的是"我刚点的那一下怎么样了"，
        // 每一发都要有答案；这一行说的是"这块板上有没有藏着的空框"，没有的时候念一颗 0 等于替用户造一颗他没问过的坏消息。
        // 黑字仍按片28 那把尺钉死（⛔ 跟主题走＝深色主题下白字画在白底上＝又隐形一次）。
        _barHiddenEmbeds = new TextBlock
        {
            Text = string.Empty,
            FontSize = 12,
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        var barRow = new StackPanel { Orientation = Orientation.Horizontal };
        barRow.Children.Add(_barScaleButton);
        barRow.Children.Add(_barBackgroundButton);
        barRow.Children.Add(_barPngButton);
        barRow.Children.Add(_barPinButton);
        barRow.Children.Add(_barSvgButton);
        barRow.Children.Add(_barFolderButton);
        // 片204＝P-285：那一排永远摊开的按钮整条压在引擎自己的工具条上（用户真机量到：遮住、
        // 关不掉、挪不开）。收法＝一颗「⋯」收合钮：那一排默认收起、点了才摊开；两行读数
        // （那句落点与那句「未显示的外部内容」）照旧摊在外面——它们是"刚才那一发怎么样了"与
        // "这块板有没有藏着空框"的答案，⛔ 跟着动作按钮一起藏。⚠ 那颗钮念的是一颗语言中立的
        // 字符而⛔ 表里那颗键：三语用户念它同一副脸，也没有要翻译的词（那一排里每颗按钮
        // 自己的字照旧走 Word/Phrase）。三样颜色照旧各钉一遍（同排那六颗的同一副配方）。
        var barMenuButton = new Button
        {
            Content = "⋯",
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130)),
        };
        barMenuButton.Click += (_, _) =>
            barRow.Visibility = barRow.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        barRow.Visibility = Visibility.Collapsed;
        _bar = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 12, 0),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(232, 250, 250, 250)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 178, 178, 178)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        _bar.Children.Add(barMenuButton);
        _bar.Children.Add(barRow);
        _bar.Children.Add(_barStatus);
        _bar.Children.Add(_barHiddenEmbeds);
        var stage = new Grid();
        stage.Children.Add(_web);
        stage.Children.Add(_notice);
        stage.Children.Add(_bar);
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
        _noticeMain!.Visibility = Visibility.Visible;   // 第一行自己回场（上一拍可能只因为第二行还挂着字而没整块收过）
        _noticeTitle!.Visibility = titled ? Visibility.Visible : Visibility.Collapsed;
        _noticeButton!.Visibility = offerCopy ? Visibility.Visible : Visibility.Collapsed;
        PasteNotice();
        _notice.Visibility = Visibility.Visible;
    }

    /// <summary>收起<b>第一行</b>（开闸成功＝那句坏话已经不是事实）：按钮上的"另存中"一并退掉。
    /// <para>片108＝P-257：那块边框只在第二行也空着时才跟着收——外链那句"这条链没打开"由它自己那颗时长收，
    /// ⛔ 由第一行的收口顺带撤掉（那是第二个主人）。</para></summary>
    private void HideNotice()
    {
        _noticeKey = null;
        _copying = false;
        if (_notice is null) return;
        if (_noticeMain is { } main) main.Visibility = Visibility.Collapsed;
        _notice.Visibility = _linkNoticeKey is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 按当前语言把面板上那两行重贴一次（<see cref="StarMark.Abstractions.I18n.Locale.Changed"/> 那一拍也走它）。
    /// <para>那颗"另存到 &lt;路径&gt;"的槽交的是<b>数据</b>，所以它进 <c>Fmt</c> 的实参；⛔ 在这里拼成品句（那是第二个措辞出处）。</para>
    /// </summary>
    private void PasteNotice()
    {
        if (_noticeBody is null || _noticeTitle is null || _noticeLinkBody is null) return;
        // 那颗按钮上的话也一起重贴：它是构造时冻进去的一串字，⛔ 比面板上那两行晚一步换语言（P-175 那一族的第三种脸）。
        if (_noticeButton is { } button) button.Content = Word("boards.saveCopyAction");
        // 那颗「打开所在文件夹」与它的可见性也归这一拍算：换语言只走 PasteNotice、⛔ 重走 ShowNotice
        //（片34 那颗按钮上的话漏掉的就是同一拍——两颗按钮同一条口径，⛔ 一颗重贴一颗冻着）。
        if (_noticeFolderButton is { } folder)
        {
            folder.Content = Word("boards.openFolderAction");
            folder.Visibility = CanOpenSavedFolder ? Visibility.Visible : Visibility.Collapsed;
        }
        // 那颗「继续编辑／仍然离开」也归这一拍算：可见性只由 _closeRevived 一处定（⛔ 在 ShowNotice 里各写一遍＝第二个主人），
        // 换语言那一拍只走 PasteNotice、⛔ 重走 ShowNotice（与那颗「打开所在文件夹」同一条口径，片34 漏的就是这一拍）。
        if (_noticeStayButton is { } stay)
        {
            stay.Content = Word("boards.stayAction");
            stay.Visibility = _closeRevived ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_noticeLeaveButton is { } leave)
        {
            leave.Content = Word("boards.leaveAction");
            leave.Visibility = _closeRevived ? Visibility.Visible : Visibility.Collapsed;
        }
        // 片49 那两颗也归这一拍算：在场资格只有 <see cref="_blockedSceneId"/> 一颗（⛔ 在 ShowNotice 里再写一遍＝第二个主人）。
        // 那颗「再试一次」把"正在跑"也念进这里——跑的那一发它点不亮，用户才⛔ 连按出两发开场（P-226 那条"按下去要有回音"）。
        if (_noticeRetryButton is { } retry)
        {
            retry.Content = Word("boards.retryAction");
            retry.Visibility = _blockedSceneId is null ? Visibility.Collapsed : Visibility.Visible;
            retry.IsEnabled = !_retrying;
        }
        if (_noticeFreshButton is { } fresh)
        {
            fresh.Content = Word("boards.freshBoardAction");
            fresh.Visibility = _blockedSceneId is null ? Visibility.Collapsed : Visibility.Visible;
        }
        // 第二行（片108＝P-257 (C) 的落地腿）：那两句归 <see cref="_linkNoticeKey"/>，⛔ 再由"面板空不空"决定上不上屏，
        // 所以换语言那一拍也必须重贴它（与第一行同一条口径）；那颗键名同时是那句正文的键，⛔ 在这里再挑一遍措辞（第二个出处）。
        switch (_linkNoticeKey)
        {
            case "boards.linkBlockedBody":
                _noticeLinkBody.Text = Word("boards.linkBlockedBody");
                break;
            case "boards.linkFailedBody":
                _noticeLinkBody.Text = Word("boards.linkFailedBody");
                break;
        }
        switch (_noticeKey)
        {
            case "boards.recoveringBody":
                _noticeBody.Text = Word("boards.recoveringBody");
                break;
            case "boards.blockedBody":
                // 那句"拿不出来"必须说清两件事：盘上那颗档⛔ 没了、这一发⛔ 动过它——
                // 用户听到"打不开"时默认想的是"我的板丢了"（P-239 登记的就是这一格误会）。
                _noticeTitle.Text = Word("boards.blockedTitle");
                _noticeBody.Text = Word("boards.blockedBody");
                break;
            case "boards.stuckBody":
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Word("boards.stuckBody");
                break;
            case "boards.copySavedBody":
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Phrase("boards.copySavedBody", _savedPath ?? string.Empty);
                break;
            case "boards.copySavedStaleBody":
                // 屏幕上那块⛔ 是交出去的那一块（那一颗可疑字节没过判据）⇒ 这句话必须自己说清楚，
                // ⛔ 让用户以为刚画的笔迹已经保住了（片33 坏臂量到的就是这一格）。
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Phrase("boards.copySavedStaleBody", _savedPath ?? string.Empty);
                break;
            case "boards.copyFailedBody":
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Word("boards.copyFailedBody");
                break;
            case "boards.openFolderFailedBody":
                // 坏的是"开那颗目录"这一手，⛔ 是那块板存不下来——标题留着说的是同一件事实（写闸还拒着），
                // 而那颗「打开所在文件夹」⛔ 跟着摘掉：摘了就等于让用户⛔ 能再点一次（P-54 定过的坏法）。
                _noticeTitle.Text = Word("boards.stuckTitle");
                _noticeBody.Text = Phrase("boards.openFolderFailedBody", _savedPath ?? string.Empty);
                break;
            default:
                return;
        }
    }

    /// <summary>表里那一句按当前语言取词（一处限定名，⛔ 在每颗调用点重抄一遍全限定）。</summary>
    private static string Word(string key) => StarMark.Abstractions.I18n.Locale.T(key);

    /// <summary>带读数的句子（那颗路径与那两档念法是<b>数据</b>，进槽而⛔ 拼成品句）。
    /// <para>形参收成 <c>params</c>：片64 那句"已存到"从此有三颗实参（路径＋缩放＋背景），
    /// 而各语言那一行必须给同一组编号——那条口径由 <c>I18nContractTests</c> 逐行比对钉住。</para></summary>
    private static string Phrase(string key, params object?[] arguments)
        => StarMark.Abstractions.I18n.Locale.Fmt(key, arguments);

    /// <summary>换语言那一拍：那块面板、那一行落点与那一行第二颗读数<b>三处都要重贴</b>（片54 起这一扇上有两块会说汉语的表面，
    /// 片83 起那一块上从此有两行；只搬一处＝P-175 那一族"一颗重贴一颗冻着"的第三种脸，而那一行⛔ 在面板的名单里）。</summary>
    /// <summary>
    /// 把<b>此刻</b>的主题与界面语言递给引擎——那两颗 outbound 的<b>唯一发送处</b>
    /// （Phase C 片147＝集成矩阵「设置同步（主题／语言）」那一格的后半）。
    /// <para>改前的形状是两颗硬编码占位值（浅色＋<c>zh-CN</c>，注释自己写"接设置面归后续批次"）：
    /// 深色主题／英文／日文用户打开画板，引擎仍按浅色中文渲染，而屏幕上那扇窗看起来"一切正常"——
    /// 与本仓定性过的"全绿而功能坏"同族，差别只在那一族有人配证人、这一格没人配。</para>
    /// <para>三个调用点：桥起来那一拍（<see cref="OnBridgeReady"/>——页面那位 prop 主人这一刻才存在）、
    /// 换语言那一刻（<see cref="OnLocaleChanged"/>）、换主题那一刻（由主窗 <c>RefreshAppearance</c>
    /// 经 <see cref="BoardsWindows.ReapplyAppearance"/> 名册叫到，与 <c>CenteredDialog.ApplyThemeToOpenDialogs</c>
    /// 同一副规矩：独立窗不继承主窗主题，得有人显式推一次）。</para>
    /// <para>两颗值的<b>主人都不在这一格</b>："偏好落成浅还是深"归 <c>ThemeManager.ResolveEffectiveTheme</c>
    /// （含 Default 跟随系统那一档），"引擎认哪串字面"归 <c>BoardPolicy.EngineThemeName</c>／
    /// <c>EngineLangCode</c>。这里只把两边接起来，⛔ 自己判、⛔ 自己拼字面。</para>
    /// </summary>
    public void ReapplyAppearance()
    {
        // 桥没通就发＝发进一颗没人听的口子（连"发没发出去"都⛔ 有回音，与那颗 RequestSceneSnapshot 同一道闸门）。
        if (!_bridgeReady || _tearingDown) return;
        var pref = App.Services.GetRequiredService<StarMark.UI.Helpers.SettingsStore>().LoadTheme();
        var dark = StarMark.UI.Helpers.ThemeManager.ResolveEffectiveTheme(pref) == Microsoft.UI.Xaml.ElementTheme.Dark;
        // 片204＝P-287 的发侧读数位：宿主订阅→lang.set→桥侧同一棵根重渲染→引擎按 langCode prop
        // 变更惰量加载内嵌 locale，六环静态全通（片204 侧查到底）；用户量到的「不跟」断点只能真机定界。
        // 这一行把实际递给 WebView2 的那串语言码留在日志里——复现那趟对着它读（日志对着而画布没换＝
        // 断点在桥/引擎侧；日志里就没有那一行＝断点在宿主侧），⛔ 在复现之前猜着改。
        StarMark.Abstractions.StarLog.Info("boards.lang.push code="
            + BoardPolicy.EngineLangCode(StarMark.Abstractions.I18n.Locale.Current));
        PostOutbound("theme.set", Envelope("theme.set", "h1",
            "\"theme\":" + JsonString(BoardPolicy.EngineThemeName(dark))));
        PostOutbound("lang.set", Envelope("lang.set", "h2",
            "\"langCode\":" + JsonString(BoardPolicy.EngineLangCode(StarMark.Abstractions.I18n.Locale.Current))));
    }

    private void OnLocaleChanged()
    {
        // 片205＝P-287 的入口读数：这一行在日志里＝这一扇的订阅活着、换语言那一拍真的进来了
        //（日志里没有它＝断点在这一扇上游——多播链那一头由 Locale 的逐户隔离留痕接手定界）。
        StarMark.Abstractions.StarLog.Info("boards.locale.tap id=" + _boardId);
        // 三颗重贴逐颗兜（片205）：一颗抛了⛔ 配把 lang.set 那一发也带走——真机量到的正是这副脸
        //（主窗换了、这扇一动不动，日志里连 boards.lang.push 那一行都没有）。兜住的那颗只留一行痕，
        // 根因另立一案修，⛔ 在换语言这一拍里滚雪球。
        try { PasteNotice(); }
        catch (System.Exception ex)
        {
            StarMark.Abstractions.StarLog.Warn("boards.locale.paste.threw part=notice type=" + ex.GetType().Name + " id=" + _boardId);
        }
        try { PasteBar(); }
        catch (System.Exception ex)
        {
            StarMark.Abstractions.StarLog.Warn("boards.locale.paste.threw part=bar type=" + ex.GetType().Name + " id=" + _boardId);
        }
        try { PasteHiddenEmbedNotice(); }
        catch (System.Exception ex)
        {
            StarMark.Abstractions.StarLog.Warn("boards.locale.paste.threw part=hidden type=" + ex.GetType().Name + " id=" + _boardId);
        }
        // 换语言那一刻，画布上那套引擎的标签也要跟着重贴（§23.1 真机量过：那颗 prop 换的那一刻
        // 在同一颗实例上真实重贴，⛔ 需要重挂）——面板那三句是我们自己的话，上面三行管它们。
        ReapplyAppearance();
    }

    /// <summary>
    /// 那颗按钮的活：把<b>此刻救得回来的那一份</b>交出去——屏幕上那块过了
    /// <see cref="BoardPolicy.ScreenSceneIsWorthSaving"/> 就交那块，否则退回手里那份，而退回那一句
    /// 必须自己说清楚（⛔ 让用户以为刚画的笔迹已经保住了）；先问系统"另存为"；用户挑了照他挑的写，
    /// 他取消就<b>什么都不写</b>（面板留着、那颗按钮还能再按），只有宿主调不通那颗框才退到导出目录
    /// 并把那颗路径说给用户（⛔ 因为对话框起不来就把这事推回给用户，那是 P-54 定性过的坏）。
    /// </summary>
    private async System.Threading.Tasks.Task CopySeenSceneAsideAsync()
    {
        if (_copying || _seenScene is not { Length: > 0 } loaded) return;
        // 交哪一份由那颗判据定（片33 两臂成对量到的：面板起来之后画的那一笔只在被拒的那一份里）。
        // ⛔ 动盘上主档：选出来这一份只写用户挑的那颗新档。
        var bytes = BoardPolicy.ScreenSceneIsWorthSaving(loaded, _refusedScene) ? _refusedScene! : loaded;
        _copying = true;
        _noticeButton!.IsEnabled = false;
        try
        {
            var name = BoardPolicy.SceneFileName(_boardId);
            var target = await PickSavePathAsync(name);
            if (target is null) return;                     // 用户自己取消的：面板留着，他还能再按
            System.IO.File.WriteAllBytes(target, bytes);
            _savedPath = target;
            ShowNotice(ReferenceEquals(bytes, loaded) ? "boards.copySavedStaleBody" : "boards.copySavedBody",
                titled: true, offerCopy: true);
            // 那颗 scene 列说的是"交出去的是哪一份"：screen＝屏幕上那块（含面板起来之后画的笔迹），
            // loaded＝那颗可疑字节没过判据（多半是空画布）而退回手里那份——读数的分岔就靠这一列对回来。
            StarMark.Abstractions.StarLog.Info("boards.scene.copied to=" + target + " id=" + _boardId
                + " scene=" + (ReferenceEquals(bytes, loaded) ? "loaded" : "screen") + " bytes=" + bytes.Length);
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
    /// 面板上那一句是不是<b>认定盘上已经放着一颗档</b>——那颗「打开所在文件夹」的唯一资格（一句话一个出处，
    /// ShowNotice 与换语言那一拍都问它，⛔ 两处各 switch 一遍键名）。
    /// <para>"打开失败"那一句也算：它说的是<b>开目录</b>那一手坏了，档本身确实写成了，而那一刻用户要的正是<b>再点一次</b>；
    /// 那一句"没写成"（<see cref="NoLandedFileKeys"/>）⛔ 算（那一刻没东西可点开）。反过来，"正在恢复"／"这一扇已停止保存"
    /// 那两句⛔ 认定任何档存在（那时一颗都还没写），挂着那颗按钮＝让他点出一颗⛔ 存在的档。</para>
    /// <para>片54 之后那一发导出的产物⛔ 进这一问：它由右上角那一行自己说（<see cref="PasteBar"/>），
    /// 资格只看 <see cref="_exportPath"/>——两块表面各数各的档，⛔ 混成一颗。</para>
    /// </summary>
    private bool CanOpenSavedFolder => _savedPath is not null && NoticeSaysFileOutcome
                                      && Array.IndexOf(NoLandedFileKeys, _noticeKey!) < 0;

    /// <summary>面板上挂着的那一句<b>可能是哪几种结果</b>（一句一个下落）。
    /// 这一串是<b>唯一那份名单</b>：两颗问法（"是不是那一手的结果"与"盘上有没有档"）都从它派生——
    /// 同一串键名两处各列一遍＝迟早只补一处。</summary>
    private static readonly string[] FileOutcomeKeys =
    [
        "boards.copySavedBody", "boards.copySavedStaleBody", "boards.openFolderFailedBody", "boards.copyFailedBody",
    ];

    /// <summary>上面那串名单里<b>盘上压根没落档</b>的那一句（"没能写到那个位置"）。
    /// <para>它属于"按了之后有回音"那一族，⛔"那儿有一颗档"那一族——两颗问法⛔ 能共用一份名单，
    /// 否则那颗「打开所在文件夹」会指着一颗⛔ 存在的档（P-219 那句"界面上要给出口"的反面）。</para></summary>
    private static readonly string[] NoLandedFileKeys = ["boards.copyFailedBody"];

    /// <summary>屏幕上那一句是不是<b>那几种结果之一</b>。它比"这一扇已停止保存"那句<b>新</b>：
    /// 说的是刚刚发生的一件事，而那句旧话每一拍重贴说的还是同一件早就说过的事（片39 真机量到的那一发：
    /// 拒话重贴把「已另存到 &lt;路径&gt;」整句顶掉、那颗「打开所在文件夹」跟着退场）。</summary>
    private bool NoticeSaysFileOutcome => Array.IndexOf(FileOutcomeKeys, _noticeKey!) >= 0;

    /// <summary>
    /// 那一发外链交回宿主之后的下落：<b>开成了还是没开成，各留一行痕</b>（兄弟出口 <see cref="OpenFolderAroundAsync"/> 同形状）。
    /// <para>为什么要有这一颗：<see cref="Helpers.LauncherEx.TryOpenAsync"/> 交回的是"到底开没开成"那句话，
    /// 而它从前在这里被 <c>_ =</c> 直接扔掉＝"浏览器没注册 http 处理程序"与"开成了"在日志里长同一张脸，
    /// 下一批只能靠猜（P-56 那条口径：只报"坏了"说不出是谁坏的，反过来"什么都不报"也说不出是没坏）。</para>
    /// <para>那颗 URL 本身⛔ 递进日志（P-30：查询串里可能带凭据），递的是 LauncherEx 那句<b>与它无关</b>的原因。
    /// 片87 起那一句也<b>上了屏</b>（账本 P-249 后半 (A)）；片108＝P-257 起它住在面板的<b>第二行</b>，
    /// 所以第一行挂着话时也照样上得了屏，归属规则见 <see cref="ShowLinkNotice"/>。</para>
    /// </summary>
    private async System.Threading.Tasks.Task ReportExternalOpenAsync(string? url)
    {
        var why = await Helpers.LauncherEx.TryOpenAsync(url);
        // 两句各写<b>整颗</b>前缀（⛔ 拼成 "boards.open." + 那颗脸）：在日志与代码里都能一次 grep 中同一句话
        if (why is null)
        {
            StarMark.Abstractions.StarLog.Info("boards.open.opened id=" + _boardId);
            // 开成了＝上一发那句"没能打开"当场⛔ 是最新的事实：它由这一拍收，⛔ 等那颗定时器（八秒里他可能已经
            // 连点了两次，第二次成了而屏幕上还挂着"没能打开"＝那句话变成了假话）。
            HideLinkNotice();
            return;
        }
        StarMark.Abstractions.StarLog.Info("boards.open.failed id=" + _boardId + " why=" + why);
        ShowLinkNotice("boards.linkFailedBody");
    }

    /// <summary>
    /// 那一发外链没开成时把那一句放上那块面板的<b>第二行</b>（账本 P-249 后半 (A) 的上屏腿＋P-257 (C) 的归属腿）。
    /// <para><b>归属规则＝一句一行、各有主人</b>（片108 改的正是这一格）：这一句⛔ 再挤第一行的位子，
    /// 所以第一行挂着"这一扇已停止保存"那类<b>不带计时器</b>的旧话时也照样上得了屏。
    /// ⛔ 回退到"面板空着才进"：片107 真机量到那一格的反面＝占住第一行的那句⛔ 超时、⛔ 一颗撤销，
    /// 于是那两句坏话<b>永久静音</b>（用户点了没反应，屏幕上说的是上一件早就说过的事・正本
    /// <c>bin/WI99/p107_r2/board107_r5.txt</c>）。⛔ 让"这条链没打开"顶掉"这一扇已停止保存"——
    /// 后者说的是<b>笔迹有丢的风险</b>，被一句无关的坏话盖掉就等于让他以为那块板存好了（片39／✅P-235 那一族），
    /// 而那颗「打开所在文件夹」也跟着第一行退场＝把出口删掉。</para>
    /// <para>那一发落痕说的是<b>两句同屏</b>这件事：<c>over=</c> 记的就是第一行当时挂着谁，
    /// ⛔ 让下一批把"屏幕上没话"或"只有一句"读成"没坏"（P-56 那条口径）。</para>
    /// <para>⛔ 进 <see cref="FileOutcomeKeys"/>：这一句⛔ 认定盘上落了一颗档，那颗「打开所在文件夹」跟着它出场
    /// 就是指着一颗⛔ 存在的档（片39 定过的那一半）。</para>
    /// </summary>
    private void ShowLinkNotice(string bodyKey)
    {
        _linkNoticeKey = bodyKey;
        if (_notice is null) return;         // 编译期要的那颗判定：面板由构造里的 BuildStage 造好，实际⛔ 为空
        _noticeLinkBody!.Visibility = Visibility.Visible;
        PasteNotice();
        _notice.Visibility = Visibility.Visible;
        // 这一句说的是"刚刚发生的一件事"，没有"什么时候不再是事实"那一刻，所以由时长收（那颗数住在 Core）。
        _linkNoticeTimer ??= NewLinkNoticeTimer();
        _linkNoticeTimer.Stop();
        _linkNoticeTimer.Start();
        StarMark.Abstractions.StarLog.Info("boards.notice.link key=" + bodyKey
            + " over=" + (_noticeKey ?? "none") + " id=" + _boardId);
    }

    /// <summary>那一颗懒建的计时器：间隔只有 <see cref="BoardPolicy.LinkNoticeMs"/> 一颗出处，响了就去问 <see cref="HideLinkNotice"/>。
    /// <para>类型名⛔ 带旧 SDK 那颗 Dispatching 命名空间前缀：Windows App SDK 2.3 里 <c>DispatcherTimer</c> 住在
    /// <c>Microsoft.UI.Xaml</c>（与本档其余那颗定时器同一写法），带旧前缀＝CS0234 编译不过。</para></summary>
    private DispatcherTimer NewLinkNoticeTimer()
    {
        var timer = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(BoardPolicy.LinkNoticeMs) };
        timer.Tick += (_, _) => HideLinkNotice();
        return timer;
    }

    /// <summary>收掉<b>第二行那两句</b>：那一行空着就一颗字节也不动，而第一行挂着谁与它无关——
    /// ⛔ 拿一句过期的定时去收别人的坏话（那一族最坏的形状＝八秒之后「这一扇已停止保存」自己消失了，而写闸还拒着）。
    /// <para>两路走它：那颗定时器响了，与下一发<b>真开成了</b>（<see cref="ReportExternalOpenAsync"/> 那一支）。
    /// 那块边框只在两行都空着时才跟着收（片108＝P-257：两行各有主人）。</para></summary>
    private void HideLinkNotice()
    {
        if (_linkNoticeKey is null || Array.IndexOf(LinkNoticeKeys, _linkNoticeKey) < 0) return;
        _linkNoticeKey = null;
        _linkNoticeTimer?.Stop();
        if (_noticeLinkBody is { } link) link.Visibility = Visibility.Collapsed;
        if (_noticeKey is null && _notice is not null) _notice.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 「打开所在文件夹」那<b>一颗</b>动作（两块表面共用：面板那颗按钮、右上角那一行那颗）。
    /// <para>动作走 <see cref="Helpers.LauncherEx"/>（本机路径那一族的唯一出口，⛔ 在这里再拼一遍 explorer 命令行）。
    /// ⚠ 那颗 URI 是<b>递进去的形态</b>，⛔ 是宿主自己拼着玩的：LauncherEx 底下那颗还原判据只认 <c>file://</c> 前缀，
    /// 裸磁盘路径递进去两格候选都是空串⇒走"URI 激活＋图已不在本机"那一支，屏幕上得到一句与事实无关的坏话，
    /// 而桌面上还多开一颗 Explorer <b>主文件夹</b>窗（2026-10-19 真机读数逐字为证：result=failed・title='主文件夹 - 文件资源管理器'）。
    /// 拼那颗 URI 的主人＝<c>LocalFileIdentity.UriForPath</c>，这里⛔ 再写一遍前缀（两类生产者的口径归它）。</para>
    /// <para>交回一颗"开成了没"：坏话由<b>哪一块表面</b>说、怎么说，归各自那一拍（⛔ 在这里替两块 surface 选一句），
    /// 原因进日志（P-56 口径：只报"坏了"下一批说不出是谁坏的）。</para>
    /// </summary>
    private async System.Threading.Tasks.Task<bool> OpenFolderAroundAsync(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? path;   // 取不出上一格就拿这一颗本身（一颗档的路径也开得出它自己）
        var message = await Helpers.LauncherEx.TryOpenAsync(StarMark.Abstractions.LocalFileIdentity.UriForPath(folder));
        StarMark.Abstractions.StarLog.Info("boards.folder.opened " + (message is null ? "ok" : "failed")
            + " path=" + folder + (message is null ? "" : " why=" + message));
        return message is null;
    }

    /// <summary>面板那颗「打开所在文件夹」（P-219 那三行里那颗 (B) 的出口：念出路径之外，再给一颗能点的）。
    /// <para>⛔ 静默：开不成就把面板那句话换成"没能打开、副本还在 &lt;路径&gt;"，那颗按钮留着可再点（P-54：⛔ 把作业推回给用户）。</para></summary>
    private async System.Threading.Tasks.Task OpenSavedFolderAsync()
    {
        if (!CanOpenSavedFolder) return;                       // 那颗按钮本⛔ 在场：这一问就是它不在场时的形状
        if (await OpenFolderAroundAsync(_savedPath!)) return;
        ShowNotice("boards.openFolderFailedBody", titled: true, offerCopy: true);
    }

    /// <summary>右上角那一行那颗「打开所在文件夹」（那一发导出的产物）。
    /// <para>坏话写给<b>那一行自己</b>：面板此刻可能正挂着另一件事，而那一行说的就是"刚那一下"（片39 那一族的口径）。
    /// ⛔ 因为开目录坏了就把 <see cref="_exportPath"/> 清掉——那颗档还在盘上，他还能再点一次。</para></summary>
    private async System.Threading.Tasks.Task OpenExportedFolderAsync()
    {
        if (_exportPath is not { } path) return;               // 盘上还没落过一颗＝那颗按钮本⛔ 亮
        if (await OpenFolderAroundAsync(path)) return;
        _exportOutcomeKey = "boards.openFolderFailedBody";
        PasteBar();
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
        EnsureExports();
        return System.IO.Path.Combine(ExportsDirectory,
            _boardId + "-copy-" + BoardPolicy.FormatStamp(DateTimeOffset.Now) + BoardPolicy.SceneExtension);
    }

    /// <summary>向页面索取一份场景快照（四拍里能问的那两拍走这里；页面侧回 <c>export.bytes</c>＋<c>kind:"scene"</c>）。</summary>
    private void RequestSceneSnapshot(BoardPolicy.SaveBeat beat)
    {
        if (!_bridgeReady) return;                                    // 桥没通就索取＝发进一颗没人听的口子（回音都⛔ 有）
        if (!BoardPolicy.BeatCanAskForSnapshot(beat)) return;         // 崩那一拍：页面已经死了，问也问不到东西
        PostExportPick("scene");
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
    /// 也⛔ 在<b>那一问超时</b>那一支往屏幕贴右下角那张卡（整条链正在往下走，那张卡既⛔ 来得及被看见，还可能把退出拖住）。
    /// <para>⚠ <c>notice</c> 那颗参数的射程只到<b>超时那一支</b>：被写闸拒掉那一发走的是
    /// <see cref="RefuseOversizedEnvelope"/>，它⛔ 认这一拍由谁请的，所以画板里那块面板（<see cref="ShowArmStuckNotice"/>）
    /// <b>两拍都贴</b>。用户那一拍贴得有意义（片35／40 那条：那一支⛔ 关窗，把窗亮回来让他当场看见）；
    /// 退出那一拍贴在一颗马上随进程没掉的窗上＝那一格的在场只许由那一行痕的 <c>refused=True</c> 说，⛔ 说成"他看见了"。</para>
    /// <para><b>返回＝那一份有没有落地</b>——片61 把这一句从「那一问有回音」收紧成它本来想说的那件事：
    /// 被写闸拒掉的那一发（<c>refused=True</c>）一颗字节也没进盘，交回 <c>false</c>。
    /// 起因＝片59 量到的那张脸：痕 <c>landed=True</c> 而盘上停在上一份那颗档，主窗那句「落地 N 扇」照样报好话（P-247）。
    /// 那颗 <c>landed=</c> 的老语义⛔ 动（它问的是「页面回没回话」），分家由新加那一列 <c>refused=</c> 说。
    /// 抛了也⛔ 带走退出链：这里就地兜住，只往日志说话。</para>
    /// </summary>
    public async System.Threading.Tasks.Task<bool> FlushForExitAsync()
    {
        if (_closeFlushDone || !_bridgeReady) return false;     // 用户那一拍已经跑过／桥没通＝问不到东西
        _closeFlushDone = true;
        _tearingDown = true;
        try
        {
            var done = await FlushSnapshotAsync("exit", notice: false);
            return done && !_closingRefused;        // 有回音⛔ 等于落了地：拒掉的那一发算没落地（主窗那句计数由这一颗说话）
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
        // 那一拍撞见的若是"页面交回了、而被写闸拒掉"（⛔ "两秒内压根没回答"——后者已经走右下角那颗卡），
        // 那几笔一颗也没进盘，而此刻那颗窗是<b>藏着的</b>：面板上那两行字与那颗「另存一份到别处」贴在一颗⛔ 看得见的窗上＝
        // 一句没人能看见的坏话。⇒ 这一支⛔ 关：把窗亮回来，让用户当场看见那句话、当场按那颗按钮（片34 那颗候选字节正是它能救的东西）。
        // 他要是还想关，再点一次×就是他的决定——_closeFlushDone 已经挡掉第二拦，那颗窗这一回直接退。
        // ⚠ _tearingDown 照旧留着：这一趟之后那颗页已经证明"交回的字节⛔ 可信"，为它重建一扇⛔ 听过这句话的新窗反而误导。
        if (_closingRefused)
        {
            AppWindow.Show();
            Activate();                     // Show 只管回可见；那颗按钮要点得中，得让这扇窗回到他眼前
            // 片40：这一格是唯一"用户已经按过×、事情悬在这里"的时刻——走／留都得当场给一颗，⛔ 让他去猜"再点一次×就退"那颗隐藏出口。
            // 那颗面板此刻挂的是 stuckBody（关窗那一拍里贴上的），这里只补两颗按钮的在场资格（可见性只由 _closeRevived 一处算）。
            _closeRevived = true;
            PasteNotice();
            StarMark.Abstractions.StarLog.Info("boards.close.revive on=refused id=" + _boardId);
            return;
        }
        Close();
    }

    /// <summary>
    /// 「继续编辑」＝撤销这一拍关窗：面板收起、那颗窗回到正常待命（下一次按×重新走一遍有界等待，
    /// 而不是被这一拍的 <c>_closeFlushDone</c> 直接放行）。这一扇的写闸照旧没开（那句"已停止保存"说的还是事实），
    /// 但⛔ 让面板一直盖着画布——他要的若是"我先不关"，程序就给他"不关"，⛔ 逼他先救一份才能脱身（P-54）。
    /// </summary>
    private void StayOnBoard()
    {
        _closeRevived = false;
        _closeFlushDone = false;            // 撤销这一拍：下次按×重新问一次，别把这次被拒当成"已经拦过了"
        _tearingDown = false;               // 恢复正常的进程失效重建（这一趟页还活着，⛔ 再拿"正在拆"挡着它）
        HideNotice();
        StarMark.Abstractions.StarLog.Info("boards.close.stay id=" + _boardId);
    }

    /// <summary>「仍然离开」＝用户当场决定不救、直接关这扇。<c>_closeFlushDone</c> 已置真，这一次 <c>Close()</c>
    /// 走到 <see cref="OnAppWindowClosing"/> 会直接放行（⛔ 第二拦），那几笔按他说的丢下——救的出口（另存）他刚看得见、刚被自己否掉。</summary>
    private void LeaveAnyway()
    {
        StarMark.Abstractions.StarLog.Info("boards.close.leave id=" + _boardId);
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
        _closingRefused = false;          // 一问一复位：⛔ 让上一拍的"被拒"替这一拍说话
        await System.Threading.Tasks.Task.Delay(BoardPolicy.CloseFlushGraceMs);
        RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow);
        var budget = System.Threading.Tasks.Task.Delay(BoardPolicy.CloseFlushBudgetMs);
        var done = await System.Threading.Tasks.Task.WhenAny(landed.Task, budget) == landed.Task;
        _closingLanded = null;
        StarMark.Abstractions.StarLog.Info(
            "boards.close.flush." + origin + " id=" + _boardId + " landed=" + done
            + " refused=" + _closingRefused + " same=" + _closingSameAsDisk
            + " tookMs=" + ElapsedMs(started));
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

    private static string JsonString(string? s) => System.Text.Json.JsonSerializer.Serialize(s);

    private void OnClosed()
    {
        // 决策 2「关窗即散」：页面侧收尾在 web 半边；宿主半边这里拆干净（最后一扇走＝浏览器进程树散）。
        _tearingDown = true;
        BoardsWindows.Remove(this);                       // 与构造里那颗 Add 成对（名册上留一颗已关的窗＝退出时会去问一颗没桥的窗）
        // 那颗静态事件也一起摘：留着它，每开一扇已关的画板窗都会被"换语言"那一拍叫一次（订阅手臂成对，P-198 那把尺管的正是这一族）。
        StarMark.Abstractions.I18n.Locale.Changed -= OnLocaleChanged;
        // 那颗按时长收的话也一起摘：Tick 挂着这一窗，不摘＝每关一扇画板都留下一位会在八秒后再响一次的订阅者
        // （与上面那颗静态事件同一副对子，P-198 那把尺管的正是这一族）。
        _linkNoticeTimer?.Stop();
        if (_web.CoreWebView2 is not null)
        {
            _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
            _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
        _web.Close();
    }
}
