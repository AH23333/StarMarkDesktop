#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Abstractions.Text;

/// <summary>一份"外部字节"最后按哪一种依据解开了（P-132）。诊断与日志要说的是这个，不是"反正都是文本"。</summary>
public enum ExternalTextBasis
{
    /// <summary>没有字节可解。</summary>
    Empty,

    /// <summary>载荷开头的 BOM。这是它自己写在最前几个字节上的声明，比任何外层说法都强。</summary>
    ByteOrderMark,

    /// <summary>外层给了编码名（XML 前奏的 <c>encoding</c> / HTTP 的 <c>charset</c>）且这个名字我们认得。</summary>
    DeclaredLabel,

    /// <summary>没有任何声明，整份字节流按 UTF-8 走得通。</summary>
    Utf8Probe,

    /// <summary>没有任何声明，而<b>第一段非 ASCII 就不是合法的 UTF-8</b> ⇒ 这是一份别的码页写出来的文档，按系统 ANSI 读。</summary>
    SystemAnsi,

    /// <summary>按 UTF-8 解出过真字符之后才坏 ⇒ 判"这份 UTF-8 有伤"，坏字节仍留成 <c>U+FFFD</c>，不把整篇换成另一码页。</summary>
    DamagedUtf8,
}

/// <param name="Text">解出来的文本。</param>
/// <param name="CodePage">实际用的码页（诊断要能指回"这次按哪个码页读的"）。</param>
/// <param name="Basis">见 <see cref="ExternalTextBasis"/>。</param>
public sealed record ExternalTextDecoded(string Text, uint CodePage, ExternalTextBasis Basis);

/// <summary>
/// <b>编码由载荷决定、不由我们约定</b>的那类字节 → 文本（订阅文档、磁盘上的文本文件）。
/// <para>
/// 与 <c>ClipboardPayload</c> 那两个入口的区别：剪贴板格式的编码是 Win32 定死的（<c>CF_TEXT</c>＝OEM 码页、
/// <c>CF_HDROP</c> 非宽＝ACP），照着格式名走就行；而 feed 与 <c>.txt</c> 的编码<b>是声明出来的</b>，
/// 声明有三种写法（BOM、XML 前奏、HTTP 头），也常常根本没有。
/// </para>
/// <para>
/// 判据只此一处，两层分工：<b>这一层只做"该按哪个码页"的决定</b>，不碰平台——码页本身由调用方交进来的
/// <paramref name="decodeByCodePage"/> 去翻（<c>StarMark.Abstractions</c> 写明零 <c>DllImport</c>）。
/// 参数<b>没有默认值</b>：留一个"默认按 UTF-8"的可选参数，就是给下一个调用方留一条静默乱码的路。
/// </para>
/// <para>
/// <b>"没有声明时先探 UTF-8、探不通才按系统 ANSI"这个次序是有意为之</b>：XML 与 RSS 的规范都把
/// "无声明"定义成 UTF-8，而今天能正常显示的绝大多数源正是 UTF-8——先按系统 ANSI 会把"修好乱码源"
/// 变成"把正常源改成乱码"。探测只对 UTF-8 自检不通过的字节流才让位，纯 ASCII 两边同解、一字不变。
/// </para>
/// </summary>
public static class ExternalText
{
    /// <summary>Win32 里 UTF-8 的码页号（<c>CP_UTF8</c>）。
    /// ⚠ 65000 是 <c>CP_UTF7</c>：抄错这一格时，表内所有断言会跟着一起错（它们钉的是同一个数），
    /// 只有把字节交给<b>真系统解码器</b>的那条用例才量得出来（批次 SR 就是这么撞上的）。</summary>
    public const uint Utf8CodePage = 65001;

    /// <summary><c>CP_UNICODE</c>：UTF-16 小端。</summary>
    public const uint Utf16LeCodePage = 1200;

    /// <summary><c>CP_UNICODE_BIGENDIAN</c>：UTF-16 大端。</summary>
    public const uint Utf16BeCodePage = 1201;

    /// <summary>
    /// 编码名 → Win32 码页。名字按"去掉 <c>-</c> <c>_</c> <c>.</c> 与空格并转小写"的写法登记（同一编码的别名很多）。
    /// <para>表外的名字一律 <see cref="CodePageForLabel"/> 交 null，由调用侧的探测继续判——
    /// <b>认不得就不读，不兜底成某个具体码页</b>："未知编码就当 GBK"这类兜底在别的机器上是错的，
    /// 而且会把"我们其实没读懂"这件事从日志里抹掉。</para>
    /// </summary>
    private static readonly Dictionary<string, uint> CodePagesByLabel = new(StringComparer.Ordinal)
    {
        ["utf8"] = Utf8CodePage,
        ["utf16"] = Utf16LeCodePage,          // 无 BOM 时按 Windows 的小端读；大端的现实形状都带 BOM，先被 BOM 那一步接住
        ["utf16le"] = Utf16LeCodePage,
        ["utf16be"] = Utf16BeCodePage,
        ["gbk"] = 936,
        ["gb2312"] = 936,
        ["gb231280"] = 936,
        ["csgb231280"] = 936,
        ["chinese"] = 936,
        ["xgbk"] = 936,
        ["gb18030"] = 54936,
        ["big5"] = 950,
        ["big5hkscs"] = 950,
        ["cnbig5"] = 950,
        ["csbig5"] = 950,
        ["xxbig5"] = 950,
        ["shiftjis"] = 932,
        ["sjis"] = 932,
        ["csshiftjis"] = 932,
        ["mskanji"] = 932,
        ["xsjis"] = 932,
        ["euckr"] = 949,
        ["korean"] = 949,
        ["cseuckr"] = 949,
        ["ksc5601"] = 949,
        ["ksc56011987"] = 949,
        ["iso88591"] = 28591,
        ["latin1"] = 28591,
        ["l1"] = 28591,
        ["88591"] = 28591,
        ["windows1250"] = 1250,
        ["windows1251"] = 1251,
        ["windows1252"] = 1252,
        ["cp1250"] = 1250,
        ["cp1251"] = 1251,
        ["cp1252"] = 1252,
        ["ansi1252"] = 1252,
        ["ascii"] = 2012,
        ["usascii"] = 2012,
    };

    /// <summary>把编码名归一到表键的写法：转小写并丢掉 <c>-</c> <c>_</c> <c>.</c> 与空白。
    /// 长过 64 个字符的不可能是编码名（这个字符串来自对方的头，先在边界上夹住）。</summary>
    private static string Canonicalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || label!.Length > 64) return string.Empty;
        var chars = new char[label.Length];
        var n = 0;
        foreach (var c in label)
        {
            if (c is '-' or '_' or '.' or ' ' or '\t' or '\r' or '\n' or '"' or '\'') continue;
            chars[n++] = char.ToLowerInvariant(c);
        }
        return new string(chars, 0, n);
    }

    /// <summary>这个编码名我们认不认得；认不得交 null（调用方继续按"没声明"处理，不是按猜测处理）。</summary>
    public static uint? CodePageForLabel(string? label)
        => CodePagesByLabel.TryGetValue(Canonicalize(label), out var codePage) ? codePage : null;

    /// <summary>
    /// 从字节流开头的 XML 前奏里取 <c>encoding="…"</c>；没有前奏、前奏里没有它、或它写在 <c>?&gt;</c> 之后都交 null。
    /// <para>前奏本身恒是 ASCII（GBK / Big5 / Shift_JIS 的 ASCII 段与 UTF-8 一致），所以把每个字节当字符拼出来扫
    /// 就够了——不需要先知道整份文档的编码才能读它的编码声明。</para>
    /// </summary>
    public static string? PrologLabel(byte[]? data, int length)
    {
        if (data is null || length < 14) return null;
        var limit = Math.Min(length, 200);
        var chars = new char[limit];
        for (var i = 0; i < limit; i++) chars[i] = (char)data[i];
        if (!new string(chars, 0, Math.Min(limit, 5)).StartsWith("<?xml", StringComparison.Ordinal)) return null;

        var head = new string(chars);
        var tagEnd = head.IndexOf("?>", StringComparison.Ordinal);
        var text = tagEnd < 0 ? head : head.Substring(0, tagEnd);   // 没有 "?&gt;" 就只看已到的这段，别跑到正文里抓

        var at = 0;
        while ((at = text.IndexOf("encoding", at, StringComparison.Ordinal)) >= 0)
        {
            var p = at + "encoding".Length;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || text[p] != '=') { at++; continue; }
            p++;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || (text[p] != '"' && text[p] != '\'')) { at++; continue; }
            var quote = text[p++];
            var close = text.IndexOf(quote, p);
            return close < 0 ? null : text.Substring(p, close - p);
        }
        return null;
    }

    /// <summary>
    /// 这份外部字节该按哪个码页读，读出来交回文本与实际用的码页。
    /// </summary>
    /// <param name="data">字节缓冲。<c>length</c> 可以小于数组长度（预览只读了文件前一段，缓冲区尾部不属于载荷）。</param>
    /// <param name="length">参与判定的字节数。</param>
    /// <param name="declaredLabel">载荷或外层给的编码名；没有就交 null。</param>
    /// <param name="decodeByCodePage">真正按码页翻字节的实现（<c>AnsiText.Decode</c> 就是这个形状）。<b>没有默认值</b>。</param>
    /// <param name="systemAnsiCodePage">这台机器的 ANSI 码页：<b>只在探测不通时才用它</b>，也就是他定的那句"没声明时跟随系统 ANSI"。</param>
    public static ExternalTextDecoded Decode(
        byte[]? data,
        int length,
        string? declaredLabel,
        Func<byte[], int, int, uint, string> decodeByCodePage,
        uint systemAnsiCodePage)
    {
        ArgumentNullException.ThrowIfNull(decodeByCodePage);
        if (data is null || length <= 0)
            return new ExternalTextDecoded(string.Empty, Utf8CodePage, ExternalTextBasis.Empty);

        var (bomCodePage, bomLength) = ByteOrderMark(data, length);
        if (bomCodePage != 0)
            return new ExternalTextDecoded(
                decodeByCodePage(data, bomLength, length - bomLength, bomCodePage), bomCodePage, ExternalTextBasis.ByteOrderMark);

        // 声明赢过探测：载荷自己说了 GBK 就按 GBK 读，哪怕这串字节恰好也是合法 UTF-8——
        // "我们比源更懂它的编码"这种事一旦开一次头，下一次就没人再去看声明了。
        if (CodePageForLabel(declaredLabel) is uint declaredCodePage)
            return new ExternalTextDecoded(
                decodeByCodePage(data, 0, length, declaredCodePage), declaredCodePage, ExternalTextBasis.DeclaredLabel);

        var (isValidUtf8, decodedRealCharacters) = ProbeUtf8(data, length);
        if (isValidUtf8)
            return new ExternalTextDecoded(
                decodeByCodePage(data, 0, length, Utf8CodePage), Utf8CodePage, ExternalTextBasis.Utf8Probe);

        // 已经解出过合法多字节字符，坏在后头 ⇒ 这是"一份有伤的 UTF-8"，不是"一份别的码页"。
        // 换成 ANSI 会把本来只错一两处的整篇换成另一种语言的字，比留一个 U+FFFD 坏得多。
        if (decodedRealCharacters)
            return new ExternalTextDecoded(
                decodeByCodePage(data, 0, length, Utf8CodePage), Utf8CodePage, ExternalTextBasis.DamagedUtf8);

        return new ExternalTextDecoded(
            decodeByCodePage(data, 0, length, systemAnsiCodePage), systemAnsiCodePage, ExternalTextBasis.SystemAnsi);
    }

    private static (uint CodePage, int Length) ByteOrderMark(byte[] data, int length)
    {
        if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return (Utf8CodePage, 3);
        if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return (Utf16LeCodePage, 2);
        if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return (Utf16BeCodePage, 2);
        return (0, 0);
    }

    /// <summary>
    /// 严格 UTF-8 自检：长度不对、后继字节不是 <c>10xxxxxx</c>、过长编码、代理区、超出 U+10FFFF 都算不通。
    /// 第二返回值说"失败之前有没有解出过真正的多字节字符"——它决定这次判"不是 UTF-8"还是判"这份 UTF-8 有伤"。
    /// </summary>
    private static (bool Valid, bool DecodedRealCharacters) ProbeUtf8(byte[] data, int length)
    {
        var sawRealCharacter = false;
        var i = 0;
        while (i < length)
        {
            var b = data[i];
            if (b < 0x80) { i++; continue; }

            int followings;
            uint minimum;
            switch (b & 0xF0)
            {
                case 0xC0: case 0xD0: followings = 1; minimum = 0x80; break;
                case 0xE0: followings = 2; minimum = 0x800; break;
                case 0xF0: followings = 3; minimum = 0x10000; break;
                default: return (false, sawRealCharacter);       // 落单的变化字节，或 F8..FF 这种不存在的编码
            }
            if (i + followings >= length) return (false, sawRealCharacter);   // 尾部断了：不算"是 UTF-8"

            uint point = (uint)(b & (0x3F >> followings));
            for (var k = 1; k <= followings; k++)
            {
                var c = data[i + k];
                if ((c & 0xC0) != 0x80) return (false, sawRealCharacter);
                point = (point << 6) | (uint)(c & 0x3F);
            }
            if (point < minimum || point > 0x10FFFF) return (false, sawRealCharacter);   // 过长 / 超范围
            if (point is >= 0xD800 and <= 0xDFFF) return (false, sawRealCharacter);      // UTF-8 不许编码代理区

            sawRealCharacter = true;
            i += followings + 1;
        }
        return (true, sawRealCharacter);
    }
}
