#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 内置剪贴板历史的<b>纯策略层</b>：归一、幂等键、标题、上限、以及"该不该记"的判定。
/// <para>
/// 刻意做成无 Win32、无 IO 的静态纯函数——采集窗口（<c>ClipboardWatcher</c>）与仓储层都只做
/// "取到内容 → 问这里 → 落库"，于是本文件里的每一条安全闸门都能被 xUnit 直接钉死。
/// 剪贴板是**全机器敏感度最高的一块数据**（密码管理器、银行卡号、私钥都会路过它），
/// 一旦默认开、又没有任何过滤，等于把用户的所有密码抄进一个明文 SQLite 文件；
/// 所以"记不记"的判定必须集中、可读、可测，而不是散在事件回调里。
/// </para>
/// </summary>
public static class ClipboardPolicy
{
    /// <summary>历史保留的最大条数。超出按"最近复制时间"从旧到新淘汰（置顶条目豁免）。</summary>
    public const int MaxEntries = 500;

    /// <summary>列表标题的最大字符数。首行再长也截到这里，避免一行日志撑满整张卡片。</summary>
    public const int MaxTitleChars = 160;

    /// <summary>
    /// 单条正文入库的最大字符数。整篇文档被复制是日常操作，不设上限会让库随使用线性膨胀；
    /// 超限即截断并置 <c>truncated</c> 标记（见 <see cref="Truncate"/>），在 UI 上明确告知。
    /// </summary>
    public const int MaxStoredChars = 32 * 1024;

    /// <summary>
    /// 短于此长度的内容不记录。复制一两个字符（误触、选中一个空格）绝大多数是噪声，
    /// 而且这类内容往往正是密码框里被顺手带出来的单个字符，记下来只有害处。
    /// </summary>
    public const int MinStoredChars = 2;

    /// <summary>source_id 前缀。与其它来源的键空间隔离，也让"清空历史"能按前缀一条 SQL 收口。</summary>
    public const string SourceIdPrefix = "c-";

    /// <summary>
    /// 图片条数上限的默认值（§4：200，用户可调范围 <see cref="ImageMaxEntriesCeil"/> 以内）。
    /// <b>刻意与文本的 <see cref="MaxEntries"/> 分开</b>：一张 4K 截图的 PNG 常有几百 KB 到几 MB，
    /// 500 张就是 GB 级——磁盘上界（Q1）要求图片自己一条线。
    /// </summary>
    public const int DefaultImageMaxEntries = 200;

    /// <summary>条数上限的下界：低于这个数这条历史就没有意义了（一次复制可能直接把它挤掉）。</summary>
    public const int MinEntries = 10;

    /// <summary>图片条数上限的上界（§4）。</summary>
    public const int ImageMaxEntriesCeil = 2000;

    /// <summary>文本条数上限的上界（§4）。默认值仍是 <see cref="MaxEntries"/>，只是从常量变成可见可配。</summary>
    public const int TextMaxEntriesCeil = 10_000;

    /// <summary>
    /// 图片条数上限的<b>唯一取整口径</b>：越界夹住，绝不出现 0 或负数（那等于"记一条删一条"）。
    /// <para>设置页与仓储都问它——两处各写一遍 <c>Math.Clamp</c> 的话，上限改了其中一处，
    /// 症状是"设置页显示 2000，实际只留 500"。</para>
    /// </summary>
    public static int ClampImageMaxEntries(int value)
        => Math.Clamp(value <= 0 ? DefaultImageMaxEntries : value, MinEntries, ImageMaxEntriesCeil);

    /// <summary>文本条数上限的唯一取整口径（默认 <see cref="MaxEntries"/>，现行为不变）。</summary>
    public static int ClampTextMaxEntries(int value)
        => Math.Clamp(value <= 0 ? MaxEntries : value, MinEntries, TextMaxEntriesCeil);

    /// <summary>
    /// 单张剪贴板图片的字节上限。<b>刻意是内部常数、不进设置页</b>（§4 洞1）：它挡的是"异常帧灌进来"
    /// 这类与用户偏好无关的事故，给设置页一个能把它调大的框，等于把护栏做成可调的装饰。
    /// <para>也是采集侧唯一允许从 <c>ReadGlobal</c> 的 64MB 拷贝防护里收窄下来的数——
    /// 超过它就当场拒收，不再解码、不再编码，白付一次 20MB 以上的拷贝。</para>
    /// </summary>
    public const int MaxImageBytes = 20 * 1024 * 1024;

    /// <summary>
    /// 图片条目的幂等键：与文本 <b>同一形状</b>（<see cref="SourceIdPrefix"/> + 摘要前 16 字节的
    /// 小写 hex），只是哈希对象从"归一文本"换成"归一后的 BGRA 像素"。
    /// <para>哈希像素而不是容器字节，理由与 <c>ClipboardDedupe</c> 的图片登记同一处：
    /// 同一张图经系统重排成 DIB/PNG 之后字节会变，像素不变。</para>
    /// </summary>
    public static string BuildImageSourceId(byte[] bgra)
    {
        var hash = SHA256.HashData(bgra);
        var sb = new StringBuilder(SourceIdPrefix.Length + 16 * 2);
        sb.Append(SourceIdPrefix);
        for (var i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// 短于此边长的图片不记录。图标、缩略图、拖拽选中残留都落在这里；与 20MB 那条同为**内部常数**：
    /// 它挡的不是偏好，是"历史被一屏小图标刷满"这种没有商量余地的坏观感。
    /// </summary>
    public const int MinImageEdge = 16;

    /// <summary>
    /// 这一帧图片该不该进历史。<b>三层门禁的裁决集中在这一个纯函数</b>（§3-Q1/Q3）：
    /// ① 单张字节上限（<see cref="MaxImageBytes"/>，采集侧在拷贝前就挡，这里再兜一次）；
    /// ② 尺寸下限；③ 来源排除（密码管理器在前台时同样不记，与文本共用 <see cref="IsExcludedApp"/>）。
    /// <para><b>图片内容一律不做敏感检查</b>——这不是遗漏而是 §3-Q3 的裁决：文本能扫卡号/私钥/JWT，
    /// 图片扫不了，所以采集默认关，并在设置页开关旁原话写明"开启即包含一切屏幕内容"。
    /// 拒绝时 <paramref name="reason"/> 是给状态行与日志看的原话（"哑丢弃是缺陷"，同 F10）。</para>
    /// </summary>
    public static bool ShouldRecordImage(int width, int height, long bytes, string? foregroundProcessName,
        out string? reason)
    {
        if (bytes <= 0) { reason = "图片负载为空"; return false; }
        if (bytes > MaxImageBytes)
        {
            reason = $"图片 {bytes / (1024 * 1024)} MB 超过 {MaxImageBytes / (1024 * 1024)} MB 上限，未记录";
            return false;
        }
        if (width < MinImageEdge || height < MinImageEdge)
        {
            reason = $"图片 {width}×{height} 小于 {MinImageEdge}px，未记录（多半是图标）";
            return false;
        }
        if (IsExcludedApp(foregroundProcessName))
        {
            reason = $"来源是密码管理器一类的应用（{foregroundProcessName}），图片未记录";
            return false;
        }
        reason = null;
        return true;
    }

    /// <summary>
    /// 行分隔符归一（CRLF/CR → LF）+ 去首尾空白。
    /// <para>这一步直接决定幂等键的稳定性：同一段文本从不同应用复制出来时换行风格不一致
    /// （记事本 CRLF / 浏览器 LF / 老工具 CR），不归一会存成三条"看起来一模一样"的历史，
    /// 用户表现为"复制过三次？我没复制过"。</para>
    /// </summary>
    public static string NormalizeText(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var s = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (s.IndexOf('\r') >= 0) s = s.Replace('\r', '\n');
        return s.Trim();
    }

    /// <summary>
    /// 这段剪贴板内容该不该进历史。返回 false 时 <paramref name="text"/> 为归一后的文本（可能为空）。
    /// 判定顺序即优先级：空/超短 → 敏感来源（密码管理器前台） → 敏感形状（私钥/JWT/银行卡）。
    /// </summary>
    public static bool ShouldRecord(string? raw, string? foregroundProcessName, out string text)
    {
        text = NormalizeText(raw);
        if (text.Length < MinStoredChars) return false;
        if (IsExcludedApp(foregroundProcessName)) return false;
        if (IsSensitive(text)) return false;
        return true;
    }

    /// <summary>
    /// 幂等键正文哈希：SHA-256(UTF-8(归一文本)) 前 16 位十六进制。
    /// <para>取全文而非截断后的内容——若用截断内容，"前 32 KB 相同的两份不同长文"会撞成同一条历史，
    /// 那种丢失是静默的。哈希全文的代价只是一次线性扫描。</para>
    /// <para>大小写、内部空白、换行都参与哈希：<b>不做</b>任何"语义归一"，否则两条内容不同但键相同的
    /// 条目互相覆盖 ⇒ 用户复制过的东西凭空消失。</para>
    /// </summary>
    public static string BuildSourceId(string normalizedText)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        // 注意是 **int 容量**构造器：写成 `SourceIdPrefix + 16 * 2` 会被解析成字符串 "c-32"，
        // 于是 StringBuilder 的初值变成 "c-32"，键长出一截没人认领的前缀（测试钉住了 2+32 的形状）。
        var sb = new StringBuilder(SourceIdPrefix.Length + 16 * 2);
        sb.Append(SourceIdPrefix);
        // 小写 hex：与备份/其它来源的键风格一致，且避免大小写折叠在 OrdinalIgnoreCase 唯一索引上出歧义。
        for (var i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// 列表标题 = 首行（控制字符折成空格）截到 <see cref="MaxTitleChars"/>，多行时末尾加一个省略号提示"下面还有"。
    /// 折行而非保留换行：卡片里一条多行 JSON 会把整列撑开。
    /// </summary>
    public static string BuildTitle(string normalizedText)
    {
        var nl = normalizedText.IndexOf('\n');
        var firstLine = nl < 0 ? normalizedText : normalizedText[..nl];
        var collapsed = CollapseControlChars(firstLine);

        var suffix = nl < 0 ? string.Empty : "…";              // 提示"下面还有内容"
        var budget = MaxTitleChars - suffix.Length - 1;        // 再给截断省略号留一位
        if (collapsed.Length > budget) collapsed = collapsed[..budget] + "…";
        return collapsed + suffix;
    }

    /// <summary>把换行/制表/其它控制符折成单空格（标题与摘要用，避免粘进 JSON 后出现裸控制字符）。</summary>
    public static string CollapseControlChars(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var c in s)
        {
            if (char.IsControl(c) || c == '\u00a0' || char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>超长正文截断；返回是否发生了截断（写进 extra_json 供 UI 明示"内容已截断"）。</summary>
    public static string Truncate(string normalizedText, out bool truncated)
    {
        if (normalizedText.Length <= MaxStoredChars)
        {
            truncated = false;
            return normalizedText;
        }
        truncated = true;
        return normalizedText[..MaxStoredChars];
    }

    // ==================== 条目动作口径 ====================

    /// <summary>
    /// 条目的"打开"是否应落为"把正文复制回剪贴板"。剪贴板历史条目<b>没有可启动的目标</b>（Uri 恒为空），
    /// 对它"打开"若照本宣科去启动 Uri，就是一个点下去毫无反应的死菜单项。
    /// <para>
    /// 判据要求 <see cref="ItemType.Clipboard"/> <b>且</b> Uri 为空：只按类型判会让用户手工改出 Uri 的条目
    /// 反而打不开；只按 Uri 空判会把待办/随记（同样可能没有 Uri）也拖进来，而它们的"打开"本就无意义，
    /// 不该被解释成"复制"。UI 的菜单文案与执行入口都走这一个判据，避免同一类型在两处各判一次而漂移。
    /// </para>
    /// </summary>
    public static bool OpensAsCopy(ItemType type, string? uri)
        => type == ItemType.Clipboard && string.IsNullOrWhiteSpace(uri);

    /// <summary>
    /// 这条是不是<b>内置</b>剪贴板历史（本机采集、StarMark 自己有权整条删掉）。
    /// <para>
    /// 必须按 <paramref name="source"/> 判而不是只按类型：<b>Ditto 派条目的 <see cref="ItemType"/> 同样是
    /// <c>Clipboard</c></b>（外部程序里它也叫"剪贴板历史"），但那些正文与幂等键属于 Ditto 自己的库——
    /// 从我们这里删既越界、也删不掉（仓储的 WHERE 带 <c>source='clipboard'</c>），
    /// 结果就是菜单上出现一个点了只会报"记录已经不在"的死项。
    /// </para>
    /// </summary>
    public static bool IsBuiltinEntry(string? source, ItemType type)
        => type == ItemType.Clipboard && source == ItemSources.Clipboard;

    // ==================== 不该出现在历史里的内容 ====================

    /// <summary>
    /// 这些进程在前台时复制的内容一律不记。密码管理器是"复制即密码"的唯一高频场景，
    /// 它们自带的"剪贴板 N 秒后清空"正是为此存在——我们把密码抄一份存到明天，
    /// 等于替用户把安全措施关掉。匹配用<b>前缀</b>（进程名不含扩展名），因为各家都有变体
    /// （KeePass / KeePassXC / KeePass2、1Password / 1Password-8）。
    /// 清单刻意保守且只按"是不是密码管理器"取；新增成员零迁移。
    /// </summary>
    public static readonly string[] ExcludedAppPrefixes =
    {
        "1password", "bitwarden", "keepass", "keepentry", "lastpass", "dashlane",
        "nordpass", "enpass", "proton pass", "proton-pass", "roboform", "passky",
        "passwordagent", "safeincloud", "keybase", "credentialui",   // 末项＝Windows 凭据对话框
    };

    /// <summary>是否属于排除清单里的来源应用（大小写不敏感前缀匹配，null/空＝未知来源，不排除）。</summary>
    public static bool IsExcludedApp(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var name = processName.Trim();
        // 传进来的可能是 "KeePass.exe" 这种带扩展名的形态：先剥掉扩展名再前缀匹配。
        var dot = name.IndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        foreach (var prefix in ExcludedAppPrefixes)
            if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 极高置信度的敏感内容形状：<b>私钥头 / JWT / 校验通过的银行卡号</b>。
    /// <para>刻意保守——每条都要"看起来确实是那个东西"才算，宁少不误挡大量普通复制。
    /// 已知的代价：13–19 位纯数字有约 1/10 概率通过 Luhn，所以一个长订单号有可能被拒记；
    /// 安全 &gt; 便利，且这里只是"不记历史"，不影响用户手上那份内容。</para>
    /// </summary>
    public static bool IsSensitive(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return ContainsPrivateKey(text) || ContainsJwt(text) || ContainsCardNumber(text);
    }

    /// <summary>PEM / OpenSSH / PGP 私钥头。私钥块永远不该进历史记录。</summary>
    public static bool ContainsPrivateKey(string text)
        => text.Contains("-----BEGIN ", StringComparison.Ordinal)
           || text.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.Ordinal)
           || text.Contains("BEGIN PGP PRIVATE KEY", StringComparison.Ordinal);

    /// <summary>
    /// JWT：<c>eyJ…​.eyJ…​.…</c> 三段 base64url。判定要求三段齐全且每段够长——
    /// 只查 <c>"eyJ"</c> 会把大量正常文本（英文单词、变量名）误伤。
    /// </summary>
    public static bool ContainsJwt(string text)
    {
        // 一个够长的 token 才可能是 JWT；短文本直接跳过，省掉逐字符扫描（复制的正文可能几十 KB）。
        if (text.Length < 40) return false;

        var i = 0;
        while ((i = text.IndexOf("eyJ", i, StringComparison.Ordinal)) >= 0)
        {
            if (IsJwtRun(text, i)) return true;
            i += 3;
        }
        return false;
    }

    /// <summary>
    /// 从 <paramref name="start"/>（必为 "eyJ" 处）起是否构成"三段、每段 ≥8 个 base64url 字符"的 JWT 形状。
    /// 首段以 eyJ 开头是因为 <c>{"</c> 的 base64 前缀恒为 eyJ —— 这是最省误判的识别点。
    /// </summary>
    private static bool IsJwtRun(string text, int start)
    {
        var segments = 0;
        var i = start;
        while (i < text.Length)
        {
            var from = i;
            while (i < text.Length && IsBase64Url(text[i])) i++;
            if (i - from >= 8) segments++;
            if (i < text.Length && text[i] == '.') { i++; continue; }
            break;
        }
        return segments >= 3;
    }

    private static bool IsBase64Url(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';

    /// <summary>
    /// 是否含银行卡号：<b>13–19 位数字</b>（允许组内单个空格/连字符分组）且过 Luhn。
    /// <para>两端都要求"不紧挨字母或数字"，否则会从更长的编号里截出一段刚好过 Luhn 的子串
    /// （20240501… 这类日期串、订单号、时间戳）——那种误判会让用户复制订单号时"莫名其妙没记"。</para>
    /// </summary>
    public static bool ContainsCardNumber(string text)
    {
        if (text.Length < 13) return false;

        var digits = new char[MaxCardDigits];
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsAsciiDigit(text[i])) { i++; continue; }
            // 起点前面不能紧跟数字或字母 ⇒ 那属于更长的串，从那里开始扫过即可。
            if (i > 0 && (char.IsAsciiDigit(text[i - 1]) || char.IsAsciiLetter(text[i - 1]))) { i++; continue; }

            var count = 0;
            var j = i;
            var lastWasDigit = false;
            while (j < text.Length)
            {
                var c = text[j];
                if (char.IsAsciiDigit(c))
                {
                    if (count == digits.Length) break;      // 超过 19 位 ⇒ 不是卡号，交给下面的收尾判定拒掉
                    digits[count++] = c;
                    lastWasDigit = true;
                    j++;
                    continue;
                }
                // 分组分隔符：只在"刚读完一位数字、且下一位还是数字"时吞掉（"4111 1111 1111 1111"）。
                if (lastWasDigit && (c == ' ' || c == '-')
                    && j + 1 < text.Length && char.IsAsciiDigit(text[j + 1]))
                {
                    lastWasDigit = false;
                    j++;
                    continue;
                }
                break;
            }

            var endsCleanly = j >= text.Length
                              || (!char.IsAsciiDigit(text[j]) && !char.IsAsciiLetter(text[j]));
            if (count is >= 13 and <= 19 && endsCleanly && PassesLuhn(digits, count)) return true;

            i = Math.Max(j + 1, i + 1);
        }
        return false;
    }

    private const int MaxCardDigits = 19;

    /// <summary>
    /// Luhn（mod 10）校验：从右往左，偶数位翻倍、&gt;9 则减 9，总和 %10 == 0。
    /// <paramref name="digits"/> 里只有数字（分隔符在扫描阶段就被跳过），故只取前 <paramref name="count"/> 个。
    /// </summary>
    public static bool PassesLuhn(char[] digits, int count)
    {
        var sum = 0;
        var doubled = false;
        for (var i = count - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (d < 0 || d > 9) return false;
            if (doubled)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubled = !doubled;
        }
        return sum % 10 == 0;
    }
}
