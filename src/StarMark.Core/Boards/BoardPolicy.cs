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
/// ② 落点目录（片23 起那颗根归 <c>UserDataPaths</c>，UI 侧只问它要那颗子目录；那颗路径⛔ 是判据）；
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
