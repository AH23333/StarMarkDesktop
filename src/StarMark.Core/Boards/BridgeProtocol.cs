#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Core.Boards;

/// <summary>
/// 画板那扇窗（web 侧）与桌面壳之间那条桥的<b>唯一 schema 出处</b>（批次 Phase C 片12＝落地设计提案 v1 §21.1 的落码）。
/// <para>
/// <b>为什么住在 Core</b>：这里每颗判定都是纯字符串/集合形状，而它决定的是"<b>web 侧能让我做什么</b>"——
/// 越权面的判据必须能单测（同 <see cref="Backup.BackupPathPolicy"/> 那条理由）。UI 侧只许转发，⛔ 再写一份名单。
/// </para>
/// <para>
/// <b>名单两颗来源，各⛔ 同</b>：命令名与事件名是<b>我们自定</b>的（v1 §21.1 那张表），
/// 而"宿主能问画板要什么、能命令画板做什么"那 25 颗旋钮住在包自己那里（§20.2 逐字），
/// 名单只出现在本类＝防"桥的命令"与"包的方法"两处各写一份、对不上时静默失效。
/// </para>
/// <para>
/// <b>三条硬线在本类里的形状</b>：① 路径永远进不来（<see cref="SanitizeExportName"/> 之后不可能带目录，
/// <see cref="Check"/> 见到未列的键直接拒）；② 名单外一律拒并给出可上屏的<b>故障名</b>（交 UI 侧配 <c>LocMessage</c>，
/// 本类⛔ 成品字——那是 P-193/P-194 那条口径）；③ 外链只认 http(s) 且⛔ 带凭据。
/// </para>
/// </summary>
public static class BridgeProtocol
{
    /// <summary>信封里那颗 <c>v</c> 的本命值（§12.1 定形，v1 未改）。</summary>
    public const int Version = 1;

    /// <summary>JS→C# 的七颗命令名。颗数由证人钉住（<c>BoardBridgeProtocolTests</c> ①）。</summary>
    public static readonly string[] InboundCommands =
    [
        "bridge.ready", "board.loaded", "paste.query", "export.bytes", "pin.request", "background.request", "open.external",
    ];

    /// <summary>C#→JS 的六颗事件名。</summary>
    public static readonly string[] OutboundEvents =
    [
        "board.load", "theme.set", "lang.set", "feature.flags", "export.pick", "autosave.arm",
    ];

    /// <summary>
    /// 每颗命令<b>允许出现的 args 键</b>；名单外多一颗＝整条拒（硬线②）。
    /// <para>键名是我们自定的协议字段；值怎么解（base64、那颗 <c>data</c> 形状标签）住在 UI 侧，
    /// 但"有没有资格出现"这件事⛔ 由 UI 侧说了算。</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> InboundFields =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["bridge.ready"] = [],
            // 页面报"那份档落进画布了"＋<b>两颗数</b>：档里读到几颗（elements）、画布上现在几颗（canvas）。
            // 开闸由这两颗数与宿主递进去的那颗<b>全等</b>说了算（BoardPolicy.ShouldArmAfterLoad）——
            // ⛔ 拿一句无参的话当证据：片26 现场量到"promise 解掉了、页面回了话，而画布是空的"，
            // 那一跑的盘上读数＝那块 2 元素／2608 字节的板被盖成 0 元素／241 字节。
            ["board.loaded"] = ["elements", "canvas"],
            ["paste.query"] = ["shape", "bytes"],
            ["export.bytes"] = ["bytes", "kind", "suggested"],
            ["pin.request"] = ["token"],
            ["background.request"] = [],
            ["open.external"] = ["url"],
        };

    /// <summary>C#→JS 那六颗事件的允许字段（同一道门，方向反过来也要有主人）。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> OutboundFields =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["board.load"] = ["bytes", "id"],
            ["theme.set"] = ["theme"],
            ["lang.set"] = ["langCode"],
            ["feature.flags"] = ["uiOptions"],
            ["export.pick"] = ["kind", "skipInliningFonts"],
            // 宿主递给页面的"载档那一拍已过"：无参（它报的是时刻，不是东西）。
            ["autosave.arm"] = [],
        };

    /// <summary>拒的原因。UI 侧拿这颗枚举去配表里的键（⛔ 在这里念成品句，也⛔ 静默吞）。</summary>
    public enum Fault
    {
        /// <summary>放行。</summary>
        None,
        /// <summary>名字⛔ 在那一侧的名单里（含空串与大小写不同）。</summary>
        UnknownName,
        /// <summary>args 里出现未列的键——多一颗就拒，⛔ 忽略（忽略＝给越权留缝）。</summary>
        UnexpectedField,
        /// <summary>名字在册却⛔ 带那颗必需的键（例如 <c>open.external</c> 没 <c>url</c>）。</summary>
        MissingField,
        /// <summary>那颗 <c>version</c> 与本类对不上。</summary>
        WrongVersion,
    }

    /// <summary>方向：一条消息要么进（JS→C#），要么出（C#→JS）。</summary>
    public enum Direction
    {
        /// <summary>web 侧递给桌面壳。</summary>
        Inbound,
        /// <summary>桌面壳递给 web 侧。</summary>
        Outbound,
    }

    /// <summary>那颗 <c>export.bytes</c>／<c>export.pick</c> 里的档位枚举（逐字＝v1 §21.1 那一列）。</summary>
    public static readonly string[] ExportKinds = ["png", "svg", "scene"];

    /// <summary>
    /// 那颗档位对应的档后缀——<b>扩展名的唯一主人就是这一颗 switch</b>。
    /// <para><b>为什么⛔ 收一颗扩展名串</b>：判据先写成"点＋字母数字"的形状检查，于是 <c>.exe</c> 合规通过，
    /// 而这条桥的 web 侧本来就能随便递串上来（§18.4 现读：递上来的名字是 <c>blob:</c> 的 UUID）。
    /// 真正的问题不在"这颗串合不合法"，在"<b>谁有资格说这颗文件是什么</b>"——只有档位枚举有，
    /// 所以这里把参数从"扩展名"改收"档位"：不认识的名字给 <c>null</c>，⛔ 替它编一颗。</para>
    /// </summary>
    public static string? ExtensionForKind(string? kind) => kind switch
    {
        "png" => ".png",
        "svg" => ".svg",
        "scene" => BoardPolicy.SceneExtension,
        _ => null,
    };

    /// <summary>那颗 <c>paste.query</c> 递的 <c>data</c> 形状标签（§20.3 现读那四档）。</summary>
    public static readonly string[] PasteShapes = ["elements", "spreadsheet", "text", "errorMessage"];

    /// <summary>名字是否在册（按方向取那张名单；大小写敏感＝Ordinal，防 <c>EXPORT.BYTES</c> 混进来）。</summary>
    public static bool IsKnownName(string? name, Direction direction)
        => !string.IsNullOrEmpty(name)
           && (direction == Direction.Inbound ? InboundCommands : OutboundEvents)
                .Any(n => string.Equals(n, name, StringComparison.Ordinal));

    /// <summary>
    /// 一条消息的<b>总判定</b>：名字在册 ＋ 版本对得上 ＋ args 的<b>键</b>全在名单里。
    /// <para>⛔ 抛异常：抛出去到了 UI 侧就变成"点了没反应"（本仓定性过的坏）。所有坏形状都回一个可上屏的故障名。</para>
    /// </summary>
    public static (bool Allowed, Fault Reason) Check(
        int version, string? name, IEnumerable<string?>? argKeys, Direction direction = Direction.Inbound)
    {
        if (version != Version) return (false, Fault.WrongVersion);
        if (!IsKnownName(name, direction)) return (false, Fault.UnknownName);

        var fields = direction == Direction.Inbound ? InboundFields : OutboundFields;
        var allowed = fields[name!];
        var keys = (argKeys ?? Array.Empty<string?>()).ToList();

        foreach (var key in keys)
            if (string.IsNullOrEmpty(key) || Array.IndexOf(allowed, key!) < 0)
                return (false, Fault.UnexpectedField);

        // 名字在册却缺那颗必需的实参：缺了就明说（⛔ 让它走到"没得存"那条静默路上）
        var required = RequiredFields(name!);
        if (required is not null)
            foreach (var need in required)
                if (!keys.Any(k => string.Equals(k, need, StringComparison.Ordinal)))
                    return (false, Fault.MissingField);

        return (true, Fault.None);
    }

    /// <summary>
    /// 那颗命令至少得给一个实参（null＝⛔ 参的那颗，不设这道门）。
    /// <para><b>出向那两颗也在册</b>：那条门管的是"名字在册却递了个空壳"，与谁发起无关。
    /// 宿主自己拼 <c>board.load</c>／<c>export.pick</c>，少一颗键现场就是"点了没反应"——
    /// 而 v1 §21.1 那张 args 列早就写着这两颗必带（<c>bytes</c>＋<c>id</c>／<c>kind</c>），
    /// 所以这里⛔ 新加判据，只是把已经写在表上的事配上一道门。</para>
    /// </summary>
    private static string[]? RequiredFields(string name) => name switch
    {
        "export.bytes" => ["bytes", "kind"],
        "pin.request" => ["token"],
        "open.external" => ["url"],
        "paste.query" => ["shape"],
        "board.load" => ["bytes", "id"],
        // 开闸那句必须<b>带数</b>：一颗⛔ 给的 <c>board.loaded</c> 只能读成"它 promise 解掉了"，
        // 而闸门要的是"画布上真有宿主递进去那块板"（片26 量到的那颗坏就坏在这句话是空的）。
        "board.loaded" => ["elements", "canvas"],
        "export.pick" => ["kind"],
        _ => null,
    };

    /// <summary>
    /// 把 web 侧递来的档名收成<b>一颗裸文件名</b>：丢目录、去非法字符、空名与保留名换兜底、超长截主干。
    /// <para><b>为什么"丢名"而不是"拒"</b>：§18.4 现读那颗 <c>a.download</c> 被 WebView2 无视，
    /// 递上来的本来就是颗 <c>blob:</c> 的 UUID——拒它等于每次导出都弹错。而路径那一半⛔ 许进来（硬线①）。</para>
    /// </summary>
    /// <param name="suggested">web 侧给的名字（可为 null／空／带目录／带 ..）。</param>
    /// <param name="fallbackStem">兜底主干名（由 C# 侧的会话与时刻决定）。</param>
    /// <param name="kind">档位名（<see cref="ExportKinds"/> 那一列）；扩展名只从 <see cref="ExtensionForKind"/> 取，
    /// ⛔ 由 web 侧的串决定。档位不认识时落 <c>.png</c>——那是"每次导出都弹错"与"给一颗能开的图"之间唯一诚实的一档。</param>
    public static string SanitizeExportName(string? suggested, string fallbackStem, string? kind)
    {
        var name = (suggested ?? string.Empty).Trim();
        // 只取最后一段：`..\..\evil.png`、`D:/a/b/evil2.png` 到这儿都只剩 `evil.png`
        name = System.IO.Path.GetFileName(name.Replace('\\', '/'));

        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = ExtensionForKind(kind) ?? ".png";

        if (string.IsNullOrWhiteSpace(stem)
            || stem!.Length > MaxStemLength
            || Files.DeviceNamePolicy.IsReservedStem(stem)
            || stem.IndexOfAny(InvalidFileNameChars) >= 0)
            stem = fallbackStem;

        return stem + ext;
    }

    /// <summary>那颗设备名表住在 <see cref="Files.DeviceNamePolicy"/>（⛔ 在这里再抄一份＝第二个主人）。</summary>
    private static readonly char[] InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();

    /// <summary>主干名上限（超了就把整颗名换成兜底名，⛔ 截一半留个认不出的名）。</summary>
    public const int MaxStemLength = 96;

    /// <summary>那颗档位枚举是否在册。</summary>
    public static bool IsKnownExportKind(string? kind)
        => !string.IsNullOrEmpty(kind) && Array.IndexOf(ExportKinds, kind!) >= 0;

    /// <summary>同一颗主干名最多往尾巴上添几颗序号就去换兜底名（换完再数同样这么多颗）。</summary>
    public const int UniqueTrials = 64;

    /// <summary>
    /// 同名⛔ 盖：那一族已经被占了就往尾巴上添 <c>-2</c>／<c>-3</c>…，添满 <see cref="UniqueTrials"/> 颗换带时刻的兜底名再数一遍，
    /// 还满就交回 <c>null</c>＝这一发⛔ 落（调用方宁可说不出去，也⛔ 许悄悄把别人写好的东西抹掉）。
    /// <para><b>为什么判据住在 Core</b>：这一问决定"盘上会不会少一颗档"，而测试工程⛔ 引用 <c>StarMark.UI</c>
    /// ⇒只有搬进来才量得到（片43 那颗 <c>PickReadableScene</c> 走的是同一条路）。</para>
    /// <para><b>「有没有」由调用方递一把问句进来</b>：本类⛔ 碰文件系统——纯函数才摆得出"整目录都被占满"那一档。</para>
    /// </summary>
    /// <param name="name">已过 <see cref="SanitizeExportName"/> 的裸文件名（扩展名⛔ 在这里动）。</param>
    /// <param name="exists">问"这颗裸名在落点目录里已经有一颗了吗"。</param>
    /// <param name="fallbackName">带时刻的兜底名（撞满那一族时换它，它自己再撞就在它尾巴上继续添）。</param>
    public static string? UniqueExportName(string name, Func<string, bool> exists, string fallbackName)
    {
        foreach (var candidate in ExportNameCandidates(name, fallbackName))
            if (!exists(candidate))
                return candidate;
        return null;
    }

    /// <summary>
    /// 候选那一串＝<c>name</c>、<c>name-2</c>…<c>name-65</c>，再 <c>fallback</c>、<c>fallback-2</c>…。
    /// <para>两颗根都要数，⛔ 只数第一颗：盘上可能本来就有一颗人写的 <c>板子-2.png</c>——
    /// 从 <c>板子.png</c> 添出来的下一颗正好撞上它，那就又回到"悄悄盖掉别人的东西"。
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> 是因为 NTFS 认⛔ 大小写。</para>
    /// </summary>
    public static IEnumerable<string> ExportNameCandidates(string name, string fallbackName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { name, fallbackName })
        {
            if (string.IsNullOrEmpty(root)) continue;
            if (!seen.Add(root)) continue;          // 兜底恰好等于原名＝⛔ 报两遍同一颗
            yield return root;
            for (var n = 2; n <= UniqueTrials + 1; n++)
            {
                var candidate = WithSequenceSuffix(root, n);
                if (candidate.Length == 0 || !seen.Add(candidate)) continue;
                yield return candidate;
            }
        }
    }

    /// <summary>把序号添在最后一段扩展名之前，并让主干（含序号）⛔ 超 <see cref="MaxStemLength"/>；
    /// Windows 还不许一颗档名以 <c>.</c> 或空格收尾，所以截完之后要 <c>TrimEnd</c> 一次。</summary>
    private static string WithSequenceSuffix(string name, int n)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        var suffix = "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stem2 = stem.Length + suffix.Length > MaxStemLength
            ? stem[..(MaxStemLength - suffix.Length)]
            : stem;
        stem2 = stem2.TrimEnd('.', ' ');
        if (stem2.Length == 0) return string.Empty;
        return stem2 + suffix + ext;
    }

    /// <summary>
    /// 那颗档位递上来的字节<b>该落哪儿</b>——<c>scene</c> 落板档目录（一颗板一颗档，档名＝那颗 id，⛔ 时刻前缀），
    /// 其余（png／svg）落导出目录（一次一份，名字带时刻）。
    /// <para>这颗判定住在 Core 而不是 UI：它是"这份字节是不是那颗板的本体"那一问的答案，
    /// 而这一问的答案同时决定了<b>命名规则</b>与<b>要不要滚 .bak</b>——散到 UI 里就会长成两份。</para>
    /// </summary>
    public static bool LandsInSceneArchive(string? kind)
        => string.Equals(kind, "scene", StringComparison.Ordinal);

    /// <summary>那颗粘贴形状标签是否在册（⛔ 在册＝放行：在册只说明"我认得它"，要不要进画板另有判据）。</summary>
    public static bool IsKnownPasteShape(string? shape)
        => !string.IsNullOrEmpty(shape) && Array.IndexOf(PasteShapes, shape!) >= 0;

    /// <summary>
    /// 外链闸门（§21.1 那颗 <c>open.external</c>）：只认 http/https、要有机名、⛔ 带 userinfo。
    /// <para>系统浏览器怎么起是 UI 侧 <c>LauncherEx</c> 的事；这里只管"这条 URL 配不配递过去"。</para>
    /// <para><b>这颗拒⛔ 走 <see cref="Fault"/></b>：<see cref="Check"/> 只看键名、看不到键值，
    /// 所以那句拒的词由 UI 侧自己配（本类⛔ 成品字）。也正因为没有读者，<c>Fault</c> 里那颗
    /// 曾经叫 <c>ExternalUrlRejected</c> 的成员本批删了——批次 P-212 那一族的形状：注释承诺了一个不存在的消费者。</para>
    /// </summary>
    public static bool IsSafeExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(parsed.Host)) return false;
        // UserInfo 非空＝`https://user:pw@host`：那颗 Token 会跟着跳出去（P-30 那条口径）
        return string.IsNullOrEmpty(parsed.UserInfo);
    }
}
