#nullable enable
using System;
using System.Globalization;

namespace StarMark.Core.Boards;

/// <summary>
/// 画板（excalidraw 那扇窗）在桌面这一侧的<b>身份、落盘节拍与撤销立场的唯一主人</b>（批次 Phase C 片12＝落地设计提案 v1 §21.3／§21.4 的落码）。
/// <para>
/// 这里只住<b>已经判过的事</b>：档名形状、一颗板一颗档的对应、四拍 autosave、
/// "载档与桥回写算⛔ 算一次用户动作"那颗裁决。三样东西刻意<b>⛔ 在这里出现</b>：
/// ① 我们自己给的图片字节上限（那条"内嵌 base64 之后档体积 ↔ 图张数"的曲线<b>还没量</b>，
/// 先写常数＝把猜测当判据；见 §21.7 与 <see cref="ImageCeilingsMeasured"/>）；
/// ② 那颗<b>成品路径</b>（片23 起根归 <c>UserDataPaths</c>，UI 侧只问它要那颗子目录）。<b>片39 给这一条改口</b>：
///    本档现在裁「<b>用户的产物进哪一族</b>」（<see cref="ExportsFolder"/>），但两棵根由宿主递进来——
///    本档⛔ 自己读环境变量、⛔ 认 <c>%APPDATA%</c>／<c>%USERPROFILE%</c>（层次倒挂，同它⛔ 认 WebView2 那颗 enum）；
/// ③ 上屏的句子（一律交 <c>LocMessage</c>，本档⛔ 成品字＝P-193/P-194 那条口径）。
/// </para>
/// </summary>
public static class BoardPolicy
{
    /// <summary>随包那颗引擎的减重长边（<b>现读</b>：<c>DEFAULT_MAX_IMAGE_WIDTH_OR_HEIGHT</c>，
    /// 定义在 <c>dist/dev/chunk-3KPV5WBD.js:322</c>＝1440）。<b>它是引擎那侧的读数，⛔ 我们给的上限</b>——
    /// 记在这里只为排障时对得上号（§19.3 那颗"too big 其实是解码失败"的形状就靠它对账）。</summary>
    public const int EngineReportedLongEdgePx = 1440;

    /// <summary>随包那颗引擎的字节闸门（<b>现读</b>：<c>MAX_ALLOWED_FILE_BYTES</c>＝<c>4*1024*1024</c>，
    /// 同档 <c>:323</c>；那颗 <c>dist/spike1.min.js</c> 里逐字 <c>Ine=4*1024*1024</c>，⛔ 任何 prop 出口）。
    /// 同样⛔ 是我们自己给的上限。</summary>
    public const long EngineReportedFileCeilingBytes = 4L * 1024 * 1024;

    /// <summary>
    /// 「我们自己给的那两颗图片上限（长边与字节）量过没有」。false 时闸门 ③ 钉住"本档一颗自定的数字都⛔ 许出现"。
    /// <para>翻成 true 的唯一入口＝把 §21.7 那条欠的曲线量出来（一张 4K 照片味 PNG 进画板之后，
    /// 那颗 <c>.excalidraw</c> 到底多大、几张能撑爆），然后写在这里并同步 <c>docs/excalidraw整合可行性深析.md</c>。</para>
    /// </summary>
    public const bool ImageCeilingsMeasured = false;

    /// <summary>一颗板的档后缀（一颗档一板；图以内嵌 base64 跟着走，§21.3 那颗裁决）。</summary>
    public const string SceneExtension = ".excalidraw";

    /// <summary>上一份的形状：只留最近一颗，⛔ 滚动多份（v1 §21.3 那句"另存一颗 <c>.bak</c>"）。</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>停笔多久算"这一笔收了"（autosave 那一拍，毫秒）。v1 §21.3 定 1.5 s。</summary>
    public const int AutosaveIdleMs = 1500;

    /// <summary>
    /// 关窗那一拍<b>最多</b>向页面等这份快照（毫秒，片24）。
    /// <para><b>为什么必须有上限</b>：那一拍拦下的是用户亲手点下的"关闭"。没有上限＝页面卡住时那扇窗<b>永远关不掉</b>，
    /// 那比丢最后几笔坏得多（P-54 那条口径的反面：⛔ 把用户的退出键抵押给一颗网页）。</para>
    /// <para><b>为什么是这一颗数</b>：§18.6 那把尺现读的桥往返＝控制消息 p50 <b>0.4</b> ms／p95 <b>0.7</b> ms，
    /// 1 MB 载荷单程 <b>31</b> ms、8 MB <b>177</b> ms。页面那颗 serialize 加这一趟往返，两秒是量到的最坏那一档的十倍以上；
    /// 而它同时是"关掉一扇画板"的<b>可感上限</b>——那一拍里窗已经 Hide，用户等的是进程收尾，⛔ 是一段看不见的钟。</para>
    /// </summary>
    public const int CloseFlushBudgetMs = 2000;

    /// <summary>
    /// 关窗／退出那一拍在<b>开口问之前</b>先让出这一段（毫秒，片32）。
    /// <para><b>为什么要有这一手</b>：用户松手后立刻关窗（点 X／按 Alt+F4／直接退程序）时，那一下抬笔的输入事件
    /// 还在 <c>系统队列→宿主 UI 线程→WebView2 浏览器进程→渲染进程</c> 这条链上走着，而页面交回的是<b>它此刻的</b>场景。
    /// 于是"问"本身会拿到一份<b>还没有那一笔</b>的旧场景，而宿主那一侧<b>听不出新旧</b>：盘上那颗档的字节没变，
    /// 而 <c>landed=True</c> 的几种真法里"盘上本来就是它"与"把同件又写一遍"本来就长得一样
    /// （改前那两跑⛔ 有 <c>same=</c> 这一列分它俩＝那一列是本批补的）。
    /// 片31 那句"landed=True 单独⛔ 算验过"在这里有了第四张脸：<b>它连"页面回的是旧账"都听不出来</b>。</para>
    /// <para><b>门槛是量出来的</b>（正本 <c>readings_32_main_*</c>，两扇同开、各画一笔再各补一笔）：
    /// 抬笔后 <b>0～20 ms</b> 就关窗那一跑<b>丢最后画那一扇的那一笔</b>（两跑同形，<c>landed=True tookMs=13/15</c> 两颗都报好话）；
    /// 隔 <b>40 ms</b> 关＝两颗档各对上自己那两笔；80／150／250／300 ms 各一跑同样 2/2；
    /// 对照臂（⛔ 关任何窗，只等那一拍自己落盘）给足时间两笔都进盘＝⛔ 是"那一笔压根没画上"。</para>
    /// <para><b>为什么是 150</b>：门槛落在 20～40 ms 之间，取观测量上界的约四倍留余量；它是 <see cref="CloseFlushBudgetMs"/>
    /// 的十三分之一，付在"窗已经 Hide 之后"的那段看不见的钟里，⛔ 换用户一次等待。这一手是<b>把丢笔的概率压到量不到的档</b>，
    /// ⛔ 是"保证渲染进程收下了"——页面没有"我处理到第几笔"可问（宿主也⛔ 知道有几笔），所以⛔ 往上叠一层假承诺。</para>
    /// </summary>
    public const int CloseFlushGraceMs = 150;

    /// <summary>
    /// 盘上那份是不是<b>已经</b>等于这颗字节（v1 §21.4 那颗脏位＝宿主那一侧的字节对差；⛔ 另造计数器、⛔ 让 JS 递 bool）。
    /// <para><b>这颗判定住在 Core</b>：它决定的不是"要不要调用一次写"，而是"<b>要不要滚 <see cref="BackupSuffix"/>"</b>——
    /// 每滚一次，上一份<b>真正不同</b>的 <c>.bak</c> 就被顶成这一份的同件，退路当场少一条。
    /// 失焦那一拍最常撞的正是"一个字节都没改"，所以这一判据的代价是直接落在用户数据上的。</para>
    /// <para><c>lastWritten</c> 为 null＝这扇窗里宿主还没写过、也没<b>从主档</b>载过（新板，或档是从 <c>.bak</c> 捡回来的）：
    /// 那时候盘上的主档要么不存在、要么不是页面此刻的样子，必写。</para>
    /// </summary>
    public static bool DiskAlreadyHolds(byte[]? lastWritten, byte[] next)
        => lastWritten is not null && lastWritten.AsSpan().SequenceEqual(next);

    /// <summary>
    /// 滚<b>这一次</b> <see cref="BackupSuffix"/> 会不会白丢一条退路（片37；§21.4 那颗脏位缺的另一半）。
    /// <para><see cref="DiskAlreadyHolds"/> 比的是<b>宿主内存里</b>那份「我上次写出去的字节」，而盘上那颗主档由不得它：
    /// 两扇同开同一颗板（片32 量过那张形状）、外部同步工具改档、上一发写失败之后重试——这三档里内存那份都落后于盘。
    /// 于是照样会滚一发「其实与盘上同件」的写：<c>.bak</c> 被顶成与新主档同一份，
    /// 上面那句<b>「退路当场少一条」</b>说的正是这件事——当时只堵了内存那一路，盘上那一路照旧漏。</para>
    /// <para><c>onDisk</c> 为 null＝主档不存在<b>或读不到</b>：读不到就判不了，而这一发的写多半也成不了
    /// （同一颗文件被人攥着），⛔ 拿一颗读不到的盘去顶掉唯一那条「真正不同」的退路。
    /// 与来件逐颗相等＝滚完 <c>.bak</c> 与新主档同件＝那条退路等于没有 ⇒ ⛔ 滚。其余照旧滚（这才是「上一份」）。</para>
    /// <para><b>片38 补上的另一半（P-234）</b>：光问「同不同」还不够，还得问<b>盘上那份还认不认得出是一颗板档</b>——
    /// 一颗 0 字节／半颗（截在 JSON 对象中间）的主档与来件<b>当然</b>不同，于是照滚，
    /// 而这一滚把唯一那条<b>还能打开</b>的 <c>.bak</c> 顶成了垃圾：<c>TryLoad</c> 的退路读的正是它。
    /// 「退路当场少一条」这句话在同一发上第二次成立，片37 只堵了第一次那一半。
    /// ⚠ 那颗形状判据＝<see cref="LooksLikeSceneDoc"/>，它⛔ 解 JSON——这颗只问「首尾那两个括弧还在不在」。</para>
    /// <para>判据住 Core＝「要不要动用户数据」这件事⛔ 由 UI 再算一遍；存储那一档只负责把盘上那份读给它看。</para>
    /// </summary>
    public static bool BackupRollNeeded(byte[]? onDisk, byte[] incoming)
        => onDisk is { Length: > 0 } && LooksLikeSceneDoc(onDisk) && !onDisk.AsSpan().SequenceEqual(incoming);

    /// <summary>
    /// 那串字节<b>还认得出是一颗板档</b>吗（片38；只问形状，⛔ 解析内容）。
    /// <para><b>为什么只问首尾</b>：这颗判据要答的不是「这份档能不能载入」（那是 <c>TryLoad</c> 之后引擎的事），
    /// 而是「拿它去顶掉唯一那条退路<b>划不划算</b>」。一颗连 JSON 对象那两个括弧都凑不齐的字节串——0 字节、
    /// 截在半路、被人塞了别的东西——当退路＝下次真出事时那一头也是死的；而凑齐了括弧的档哪怕内容坏，
    /// 也还是<b>引擎自己写出去的那一颗档的形状</b>，留着比留一颗空档强。⛔ 在这里跑解析器：那颗判据的代价是
    /// 每拍都要读一遍盘，而「能不能载入」这件事只有真出事那一拍才需要答案。</para>
    /// <para>空／null／只有一个括弧＝认不出。首尾之外的那点空白（<c>\n</c>／<c>\t</c>／<c>\r</c>／空格）跳过——
    /// 序列化器自己⛔ 在两头留白，留白⛔ 是坏形状的理由。</para>
    /// <para>⚠ <b>量不到的那一维</b>：两颗档拼成一串（首尾括弧都在）判得出「还是档」而判不出「只有一颗」——
    /// 要判那颗得真去配一遍括弧（＝每拍读盘时跑一次解析）。那颗门路只有外部拼接一种，而漏判的代价只是「那一发照旧滚」
    /// ＝<b>回到本批改前的行为</b>，⛔ 是比改前更坏的新行为。⇒ 这一维<b>明知而⛔ 堵</b>，记进账本那条边界。</para>
    /// </summary>
    public static bool LooksLikeSceneDoc(byte[]? bytes)
    {
        if (bytes is null or { Length: 0 }) return false;
        var first = 0;
        var last = bytes.Length - 1;
        while (first < last && IsPadding(bytes[first])) first++;
        while (last > first && IsPadding(bytes[last])) last--;
        return bytes[first] == StartOfDoc && bytes[last] == EndOfDoc;
    }

    private const byte StartOfDoc = (byte)'{';
    private const byte EndOfDoc = (byte)'}';

    /// <summary>那颗档头尾允许的那四颗空白（⛔ 认全 <c>char.IsWhiteSpace</c>：那颗认 Unicode 空白，而这里要的只是序列化器会写的那四颗）。</summary>
    private static bool IsPadding(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';

    /// <summary>
    /// 主干名里那颗时刻的格式（<c>yyMMdd-HHmmss</c> 那一颗形状，本地时刻）。
    /// <para><b>写成 <c>yyMMdd-HHmmss</c> 而不是直觉的 <c>yymmdd</c></b>：.NET 的自定义格式里
    /// <c>mm</c> 恒为<b>分钟</b>、月份只有 <c>MM</c>——那颗形状要的是"年月日-时分秒"，
    /// 用 <c>yymmdd</c> 会把分钟抄进月位，形状照样是 6 颗数字、<see cref="IsSceneId"/> 照样绿，
    /// 只有按颗拆开来对才能发现（本仓那条"⛔ 拿同一个构造函数当期望"的旧课，这一批撞在自家格式串上）。</para>
    /// </summary>
    private const string StampFormat = "yyMMdd-HHmmss";

    /// <summary>落盘的四拍（§21.3）。UI 侧每一拍都调同一颗保存出口，⛔ 各写一份时机判断。</summary>
    public enum SaveBeat
    {
        /// <summary>停笔 <see cref="AutosaveIdleMs"/> 毫秒。</summary>
        Idle,
        /// <summary>那扇窗失去焦点。</summary>
        LosingFocus,
        /// <summary>渲染进程刚挂了（重载之前那份必须留住，否则＝白屏之后还丢图）。</summary>
        RenderProcessFailed,
        /// <summary>窗 <c>Closed</c>（退到子进程之前）。</summary>
        ClosingWindow,
    }

    /// <summary>
    /// 那一拍<b>问得到</b>页面要一份快照（片24；四拍里只有两拍问得到）。
    /// <para><see cref="SaveBeat.Idle"/> 本来就是<b>页面自己</b>发起的那一拍——宿主"问"它等于把话回头递给自己；
    /// <see cref="SaveBeat.RenderProcessFailed"/> 那一刻页面已经死了，发出去的东西没人接（§21.5 给这一档的活是<b>重载＋把见过那份递回去</b>，⛔ 索取）。</para>
    /// <para><b>这颗判定为什么住 Core</b>：写调用点的人会本能地"每拍都问一次"，而这一条恰好是问不到的那两拍——
    /// 它⛔ 改变盘上的字节，只决定"要不要开一次口"，所以错了不报错，只是白等或白问。</para>
    /// </summary>
    public static bool BeatCanAskForSnapshot(SaveBeat beat)
        => beat is SaveBeat.LosingFocus or SaveBeat.ClosingWindow;

    /// <summary>渲染／浏览器那一族进程失效之后，宿主这一侧该付<b>哪一手</b>恢复动作（§21.5「重载之后⛔ 许回到空板，也⛔ 许静默白屏」）。</summary>
    public enum FailureRecovery
    {
        /// <summary>只上报：那一档要么自己会重起来，要么根本动不到画板；<b>重载反而会把用户画的东西退成错误页</b>。</summary>
        ReportOnly,
        /// <summary>页面没了而<b>那颗控件还在</b>：重载那一扇页，<c>bridge.ready</c> 再来时照旧把见过那份递回去。</summary>
        ReloadPage,
        /// <summary><b>控件本身没了</b>（官方表逐字：The app has to recreate a new WebView to recover）：只能拆掉重建一颗，⛔ 对着死的控件 Navigate。</summary>
        RecreateWeb,
    }

    /// <summary>
    /// 那颗进程失效的档位分到哪一手（片24）。名字交进来而⛔ 交 SDK 那颗 enum：Core⛔ 认得 WebView2，
    /// 分了这一档，UI 侧就只剩一颗 <c>switch</c> 的三条腿（判据与接线各归其主）。
    /// <para>出处＝<c>CoreWebView2ProcessFailedKind</c> 官方表的<b>逐字</b>恢复口径：
    /// <c>BrowserProcessExited</c>「The app has to recreate a new WebView to recover from this failure」／
    /// <c>RenderProcessExited</c>「You can use the <c>Reload</c> method to try to recover」／
    /// <c>UtilityProcessExited</c>·<c>GpuProcessExited</c>·<c>SandboxHelperProcessExited</c>「does <b>not</b> need to handle recovery」／
    /// <c>FrameRenderProcessExited</c>「does not affect the top-level document, only a subset of the subframes」⇒⛔ 重载（重载＝毁掉整块板）／
    /// <c>RenderProcessUnresponsive</c>「event will continue to be raised every few seconds」⇒只上报，⛔ 把一颗忙但活着的渲染器退了。</para>
    /// <para><b>认不出来的一律退到 <see cref="FailureRecovery.ReportOnly"/></b>：那颗表将来多出一档时，默认动作必须
    /// "什么也⛔ 毁"，而⛔ 是"先重载再说"。</para>
    /// <para><b>本仓钉住的那颗运行时现读</b>（WebView2 <c>1.0.3719.77</c> 的
    /// <c>Microsoft.Web.WebView2.Core.xml</c>，按那颗 enum 的 <c>F:</c> 成员数出来的十颗）＝
    /// <c>BrowserProcessExited</c>／<c>RenderProcessExited</c>／<c>RenderProcessUnresponsive</c>／<c>FrameRenderProcessExited</c>／
    /// <c>UtilityProcessExited</c>／<c>SandboxHelperProcessExited</c>／<c>GpuProcessExited</c>／<c>PpapiPluginProcessExited</c>／
    /// <c>PpapiBrokerProcessExited</c>／<c>UnknownProcessExited</c>——十颗全在证人里逐颗点了名（名单⛔ 靠记忆，见 <c>BoardPolicyTests</c> 那一格）。</para>
    /// </summary>
    public static FailureRecovery RecoveryForFailure(string processFailedKindName)
        => processFailedKindName switch
        {
            "BrowserProcessExited" => FailureRecovery.RecreateWeb,
            "RenderProcessExited" => FailureRecovery.ReloadPage,
            _ => FailureRecovery.ReportOnly,
        };

    /// <summary>
    /// 那份场景字节里有几颗<b>会被写回盘上的</b>元素（<b>认不出＝null，⛔ 替它猜一颗 0</b>）。
    /// <para><b>这颗数的口径就是本格的全部意义</b>：它⛔ 是"档里数组的长度"，而是"页面现在交得出几颗"——
    /// 页面那颗序列化器交的是 <c>API.getSceneElements()</c>，现读 bundle＝<c>scene.getNonDeletedElements()</c>
    /// （<c>isDeleted</c> 那颗被它滤掉），所以这里也跳掉 <c>isDeleted:true</c>。三颗比数的地方必须同吃一碗：
    /// 片26 第一版拿"数组整颗长度"当 <paramref name="canvas"/> 那一维的对照物（页面递的是 <c>getSceneElementsIncludingDeleted</c>），
    /// 崩后重载那一趟的读数三颗全等（want=4／loaded=4／canvas=4）而盘上仍被盖成 0 元素——那颗闸门比的是两颗
    /// ⛔ 同口径的数，挡了个⛔ 存在的形状（真读数与它的下一跑对照见 §二十一 片26 那一块）。</para>
    /// <para><b>为什么住 Core</b>：这颗数⛔ 只是诊断，它是开闸的<b>证据</b>（<see cref="ShouldArmAfterLoad"/>），
    /// 而它管着"要不要动那块板"。一颗当判据用的数⛔ 许住在解它的那一档里。</para>
    /// <para>认不出的形状一律 null：档不是 JSON、根不是对象、没有 <c>elements</c> 这一颗、那一颗不是数组。
    /// 把这四档都折成 0＝"这份档是空的"，而它真正的意思是"我读不懂"——按 <see cref="ShouldArmAfterLoad"/>
    /// 的口径，读不懂就⛔ 开闸。</para>
    /// </summary>
    public static int? CountWritableElements(byte[]? utf8Json)
    {
        if (utf8Json is not { Length: > 0 }) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(utf8Json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("elements", out var e)) return null;
            if (e.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            var n = 0;
            foreach (var el in e.EnumerateArray())
            {
                // 与那颗序列化器同口径：被标了删的进不了档，也就不该算进"画布上该有几颗"
                if (el.ValueKind == System.Text.Json.JsonValueKind.Object
                    && el.TryGetProperty("isDeleted", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True)
                    continue;
                n++;
            }
            return n;
        }
        catch (Exception) { return null; }   // 半截档／坏编码：这一颗读不懂，交 null（⛔ 当"空的"）
    }

    /// <summary>
    /// 那颗"另存一份到别处"该交哪一份：手里那份（<c>loaded</c>＝宿主见过、也是盘上主档那一份），
    /// 还是写闸拒掉的那一份（<c>refused</c>＝用户此刻屏幕上那块）。
    /// <para><b>为什么会有这一问</b>：片33 两臂成对量到的——同一双腿在画布上拖一道，闸开那一臂把主档推成
    /// 3 颗可写元素（2809 字节），闸关那一臂里那一笔只走到 <c>boards.scene.write.refused notArmed bytes=2810</c>
    /// 就被退回去，而那颗按钮当时交出去的是 <b>2 颗／858 字节</b>＝载入那一份，屏幕上却说"已另存到 &lt;路径&gt;"。
    /// 少的正是用户刚画上去的那一笔。</para>
    /// <para><b>为什么⛔ 无条件要那一颗</b>：它是闸门判定"这一趟页面⛔ 回到了宿主给它的那块板"之后才交回的字节，
    /// P-223 量到的那颗形状（2608 字节／2 元素→241 字节／0 元素）就是它。所以这一颗只有一种资格：
    /// <b>数得出颗数、那颗数⛔ 是零、而且⛔ 比手里那份少</b>——空画布与读不懂的形状永远输，
    /// "同数或更多"才赢（同数时也交屏幕上那一份：那是用户看得见的那一块）。</para>
    /// <para>交出去也⛔ 动主档：这一份只写用户挑的那颗新档（<see cref="SceneFileName"/> 那条路⛔ 走它）。</para>
    /// </summary>
    public static bool ScreenSceneIsWorthSaving(byte[]? loaded, byte[]? refused)
    {
        var screen = CountWritableElements(refused);
        if (screen is null or 0) return false;
        return screen.Value >= (CountWritableElements(loaded) ?? 0);
    }

    /// <summary>
    /// 载档那一拍页面报回的两颗数，够不够宿主开那道闸（片26 给 P-223 的修法）。
    /// <para><b>要挡的现场是真机量到的那一颗</b>：崩溃重载之后 <c>loadFromBlob</c> 的 promise 解掉了、页面照实回话，
    /// 而画布交不出可写的那几颗——盘上那块 4 元素／4936 字节的板，8 s 内被盖成 0 元素／241 字节（<c>.bak</c> 只剩一份）。</para>
    /// <para><b>为什么"promise 解掉了"⛔ 是证据</b>：那句话报的是"我这颗解码跑完了"，
    /// 而闸门要的是"画布上现在有宿主递给它的那块板"。所以这里问三颗数：宿主递进去的可写颗数 <paramref name="want"/>、
    /// 页面从档里数到的 <paramref name="loaded"/>、页面<b>当场从画布上数回来的</b> <paramref name="canvas"/>。
    /// <b>三颗必须同口径</b>（都＝那颗序列化器交回的那一堆），否则比的是两颗不同的事。
    /// 三颗全等才开闸（⛔ "对不上就先信页面"——猜错的代价是拿空板盖掉那块好档）。</para>
    /// <para><b>任一颗认不出（null）＝⛔ 开闸</b>：那是"读不懂"，而读不懂时唯一安全的动作是不写。</para>
    /// </summary>
    public static bool ShouldArmAfterLoad(int? want, int? loaded, int? canvas)
        => want is int w && loaded == w && canvas == w;

    /// <summary>
    /// 开闸被拒之后，还许<b>再递几次档</b>（片27 给 P-224 的那一次自愈）。
    /// <para>为什么只有 1 颗：拒的那一格通常是"载档那一拍没落地"这一类<b>一次性的时序</b>，再递一次就过得去；
    /// 而一颗真坏掉的运行时多递几次只是把同一块空画布多刷几遍，代价落在用户的眼上。额度用完还是对不上，
    /// 程序手上就⛔ 剩"再来一次"了——那时唯一诚实的动作是<b>把这一扇不再落盘说出来，并给一颗出口</b>（⛔ "重启一次看看"）。</para>
    /// </summary>
    public const int ArmRetryBudget = 1;

    /// <summary>这一趟已经拒过 <paramref name="tries"/> 次，还许不许再递一次档（认不出来的颗数＝⛔ 再递，同"读不懂就不写"那条口径）。</summary>
    public static bool ShouldRetryArm(int tries) => tries >= 0 && tries < ArmRetryBudget;

    /// <summary>那颗"另存一份到别处"问完系统之后，宿主手里剩下的三种结果（片29·P-226）。</summary>
    public enum CopyPick
    {
        /// <summary>用户挑了一颗路径：照他挑的那颗写。</summary>
        Chosen,
        /// <summary>用户在那颗框里选了取消／把那颗框关了：<b>什么都不写</b>——他没要一份副本，替他写一颗就是越权。</summary>
        Declined,
        /// <summary>那颗框起不来（提权跨完整性级别只是其中一种成因）：退到导出目录，⛔ 把这事推回给用户（P-54 口径）。</summary>
        Unavailable,
    }

    /// <summary>
    /// 把"那颗框的结果"分成三档。<b>交回 <c>null</c> 有两种成因而⛔ 一种脸</b>：用户取消／宿主起不了框都长同一颗样，
    /// 而这一侧唯一分得开的信号是<b>它有没有抛</b>：抛了＝<see cref="CopyPick.Unavailable"/>，⛔ 抛而没路径＝<see cref="CopyPick.Declined"/>。
    /// <para>为什么这一刀要落在纯函数里（片28 那腿的真机读数才把它逼出来）：取消那一发在盘上写出了 858 字节的一份副本，
    /// 屏幕上还念了一句"已另存到 …"——两种 <c>null</c> 走同一条兜底路＝用户点"取消"却得到一颗他没要的档＋一句与事实不符的话。</para>
    /// </summary>
    public static CopyPick ClassifyCopyPick(bool pickerThrew, string? pickedPath)
        => pickerThrew ? CopyPick.Unavailable : pickedPath is not null ? CopyPick.Chosen : CopyPick.Declined;

    /// <summary>这一档结果许不许往盘上写一份（⛔ 挑的＝⛔ 写；那句"已另存到 …"只许跟着真写了的那一次说）。</summary>
    public static bool ShouldWriteCopy(CopyPick pick) => pick != CopyPick.Declined;

    /// <summary>
    /// 一扇画板窗在它那一生里最多<b>重建</b>几次控件（片24）。
    /// <para>为什么要有这一颗数：那颗运行时真坏掉的机器上，"重建→立刻又死"是一条<b>不需要谁帮忙就会一直跑</b>的循环，
    /// 每一圈还往日志里写一行——用户看到的是那扇窗既⛔ 恢复也⛔ 关掉。三圈的口径＝"给他一次改错的机会，再加两次"，
    /// 超过就没了一颗自愈能修的东西，剩下的是日志里那句 <c>boards.recover.givenup</c>。</para>
    /// </summary>
    public const int MaxRecreatePerWindow = 3;

    /// <summary>一颗场景是怎么进到画板里的——它决定<b>撤销栈</b>那道门（§21.4）。</summary>
    public enum SceneOrigin
    {
        /// <summary>用户在画板上画／改。</summary>
        UserEdit,
        /// <summary>宿主从档里载进来（首次打开、重载）。</summary>
        SceneLoad,
        /// <summary>宿主把字节回写进画板（例如把那颗 <c>files</c> 补齐、把敏感项摘掉）。</summary>
        BridgeWriteBack,
    }

    /// <summary>那颗 <c>captureUpdate</c> 的三档逐字名（包里 <c>CaptureUpdateAction</c> 的键名）。</summary>
    public static class Capture
    {
        /// <summary>立刻进撤销栈（用户动作）。</summary>
        public const string Immediately = "IMMEDIATELY";

        /// <summary>永远⛔ 进撤销栈（载档与桥回写）。</summary>
        public const string Never = "NEVER";

        /// <summary>那颗<b>包自己的默认</b>（<c>@default CaptureUpdateAction.EVENTUALLY</c> 逐字）。
        /// 记在这里是为了让"⛔ 设就是 EVENTUALLY"这件事看得见：载档如果偷懒不传，图就被塞进撤销栈，
        /// 用户一次 Ctrl+Z 就把整块板退成空白。</summary>
        public const string Eventualy = "EVENTUALLY";
    }

    /// <summary>
    /// 那颗来源该用什么 <c>captureUpdate</c>。裁决（v1 §21.4）：<b>用户动作＝IMMEDIATELY，载档与桥回写＝NEVER</b>；
    /// <b>⛔ 许把后两档回成那颗包默认的 EVENTUALLY</b>（<c>BoardPolicyTests.LoadsAndWriteBacksNeverEnterTheUndoStack</c>
    /// 钉的就是这一条）。
    /// </summary>
    public static string CaptureFor(SceneOrigin origin) => origin switch
    {
        SceneOrigin.UserEdit => Capture.Immediately,
        SceneOrigin.SceneLoad => Capture.Never,
        SceneOrigin.BridgeWriteBack => Capture.Never,
        _ => Capture.Never,
    };

    /// <summary>
    /// 生成一颗稳定 id：<c>yyMMdd-HHmmss-4hex</c>（文件名即 id，§12.2 那条口径）。
    /// <para>随机段由调用方给（UI 侧用 <c>RandomNumberGenerator</c>），本函数⛔ 自己取熵——
    /// 它是纯函数才能被证人逐字钉住，也才敢在迁移／对账里重算。</para>
    /// </summary>
    /// <param name="at">建板那一刻（本地时刻）。</param>
    /// <param name="rand0toFFFF">0–65535；越界＝抛（这是调用方的编程错，⛔ 静默取模——取模会让那 4 位变 3 位分布）。</param>
    public static string NewSceneId(DateTimeOffset at, int rand0toFFFF)
    {
        if (rand0toFFFF < 0 || rand0toFFFF > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(rand0toFFFF), "那颗随机段要落在 0–65535");
        return FormatStamp(at)
               + "-" + rand0toFFFF.ToString("x4", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 那颗时刻的<b>主干形状</b>（<see cref="StampFormat"/>）——NewSceneId 与导出兜底名共用同一颗主人；
    /// UI 层⛔ 自己再写一份格式串（DisplayDateFormatGate 的射程就是「UI 里⛔ 有格式字面量」）。
    /// </summary>
    public static string FormatStamp(DateTimeOffset at)
        => at.ToLocalTime().ToString(StampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// 那颗 id 的形状（也是"档名是不是我给的"那道判据——⛔ 拿标题当身份）。
    /// <para>只判<b>形状</b>：月份 99 这种"看着不对"的串照样通过，因为它只能来自更早一颗真的 <c>DateTimeOffset</c>
    /// 或被损坏的档名，两种都由对账那一族处理，⛔ 在这里补历法判定（那是第二份判据）。</para>
    /// <para><b>为什么逐颗字符判而不是 <c>int.TryParse</c></b>：写法先是 <c>TryParse(… NumberStyles.HexNumber …)</c>，
    /// 而那颗样式名义上含 <c>AllowLeadingSign</c> 与前后空白——本批实测两格（<c>…--123</c> 与 <c>…- ab</c>）
    /// 在 .NET 9 上确实被拒，但那"<b>恰好</b>拒"是解析器的口径，⛔ 是我的判据。
    /// 身份这件事的形状由本类自己说完：六位数字、连字符、六位数字、连字符、四颗十六进制，一颗不多不少。</para>
    /// </summary>
    public static bool IsSceneId(string? id)
        => id is { Length: 18 }
           && AllDigits(id, 0, 6) && id[6] == '-'
           && AllDigits(id, 7, 6) && id[13] == '-'
           && AllHex(id, 14, 4);

    private static bool AllDigits(string s, int at, int count)
    {
        for (var i = at; i < at + count; i++)
            if (!char.IsAsciiDigit(s[i])) return false;
        return true;
    }

    private static bool AllHex(string s, int at, int count)
    {
        for (var i = at; i < at + count; i++)
            if (!Uri.IsHexDigit(s[i])) return false;
        return true;
    }

    /// <summary>导出产物与救回来的副本那颗目录的<b>名字</b>（一片档名，⛔ 一句上屏话）。</summary>
    public const string ExportsDirectoryName = "StarMark 画板";

    /// <summary>
    /// 用户<b>产物</b>（导出的字节、那颗"另存一份到别处"的兜底副本）落进哪一颗目录——P-219 那颗裁决的落地腿。
    /// <para>两条规则挤在一颗判据里：<b>①平时进「图片」那一族</b>，与同仓那颗截图目录（<c>图片\StarMark 截图</c>）
    /// 同一个主人。理由⛔ 是"看起来整齐"，而是本仓定性过多次的那句话：<b>用户的产物必须落在他找得到的地方，
    /// 取不到就是程序的事</b>（P-53／P-54 那一句的两半）。片23 那一版把它切成 <c>板档目录\exports</c>——
    /// 那颗"根只许一位主人"的正解顺手把<b>产物</b>与<b>程序内部档</b>排到了同一棵树下，而用户在图片目录里翻不到自己刚救回来的那一份。
    /// <b>②那颗根被环境变量改道时跟着改道</b>（沙盒取证、单测改道），⛔ 往用户真目录里写一颗——
    /// 批次 SS 撞过的正是"抄本⛔ 认改道"那一族。</para>
    /// <para><b>②排在①之前</b>：那棵根一旦改道，"用户找得到的地方"就是那一棵；这时候还去拼图片目录＝把取证写进真档。
    /// <paramref name="picturesRoot"/> 与 <paramref name="redirectedDataRoot"/> 都由宿主取：Core⛔ 认特殊目录，
    /// 正如它⛔ 认 <c>%APPDATA%</c>，这里只裁「拿哪一棵、怎么拼」。</para>
    /// <para>那颗目录名<b>⛔ 跟着界面语言变</b>（所以它进本档而⛔ 进表）：一句上屏话换三种语言＝三颗目录，
    /// 用户反而一颗都找不到；名字在这里是<b>身份</b>，⛔ 措辞。同仓那颗截图目录也是这一个口径。</para>
    /// </summary>
    public static string ExportsFolder(string picturesRoot, string? redirectedDataRoot)
        => System.IO.Path.Combine(redirectedDataRoot ?? picturesRoot, ExportsDirectoryName);

    /// <summary>一颗板的档名（<c>&lt;id&gt;.excalidraw</c>）。</summary>
    public static string SceneFileName(string id) => Require(id) + SceneExtension;

    /// <summary>上一份的档名（<c>&lt;id&gt;.excalidraw.bak</c>）。</summary>
    public static string BackupFileName(string id) => SceneFileName(id) + BackupSuffix;

    /// <summary>那颗档名是不是一颗板档（对账三分类的"板在／档在而板缺"靠它认；<c>.bak</c> ⛔ 算）。</summary>
    public static bool IsSceneFileName(string? fileName)
        => fileName is not null
           && fileName.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase)
           && !fileName.EndsWith(SceneExtension + BackupSuffix, StringComparison.OrdinalIgnoreCase)
           && IsSceneId(System.IO.Path.GetFileNameWithoutExtension(fileName));

    /// <summary>那颗上一份档名是不是一颗板档的备份。</summary>
    public static bool IsBackupFileName(string? fileName)
        => fileName is not null
           && fileName.EndsWith(SceneExtension + BackupSuffix, StringComparison.OrdinalIgnoreCase)
           && IsSceneId(fileName[..^(SceneExtension.Length + BackupSuffix.Length)]);

    private static string Require(string id)
    {
        if (!IsSceneId(id))
            throw new ArgumentException("那颗 id 形状⛔ 对：" + id, nameof(id));
        return id;
    }
}
