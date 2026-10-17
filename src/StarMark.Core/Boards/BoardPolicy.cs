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
/// ② 落点目录（归 UI 侧的 <c>%APPDATA%\StarMark\boards</c>，那颗路径⛔ 是判据）；
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
