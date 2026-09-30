#nullable enable
using System;
using System.Runtime.InteropServices;
using StarMark.Abstractions;
using StarMark.Abstractions.Text;

namespace StarMark.Integrations.Clipboard;

/// <summary>
/// 把一段 <b>ANSI 字节</b>按码页翻成 UTF-16（P-35）。
/// <para>
/// 为什么用系统的 <c>MultiByteToWideChar</c> 而不是 <c>System.Text.Encoding.CodePages</c>：
/// 后者要新增一个包依赖，而"这台机器的 ANSI 码页是哪个"本来就是操作系统的事实——
/// 交给系统翻，既不加依赖，也不会出现"我们内置的码页表与系统实际配置不一致"这种第二种错。
/// </para>
/// <para>
/// 为什么不放在 <c>StarMark.Abstractions</c> 那份 <c>ClipboardPayload</c> 里：那一层写明
/// <b>只放字节→字符串的纯函数、不放任何 Win32 调用</b>（同一份解码既服务系统剪贴板也服务第三方库的 BLOB）。
/// 所以这里做一次注入：<c>ParseDropFiles(data, decodeAnsi)</c> 由调用方把解码器交进去，
/// 参数<b>没有默认值</b>——留一个"默认＝逐字节宽化"的可选参数，就是给未来的调用方留一条静默退回乱码的路。
/// </para>
/// </summary>
public static class AnsiText
{
    /// <summary>码页 0 ＝ 系统当前 ANSI 码页（ACP）。多字节转换函数按约定接受它。</summary>
    private const uint CpAcp = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetOEMCP();

    /// <summary>
    /// <see cref="CharSet"/> 必须显式写成 <see cref="CharSet.Unicode"/>：默认是 Ansi，
    /// 那时 <c>char[]</c> 按<b>一个字节一个字符</b>封送——函数写回来的 UTF-16 会被拆成一堆 <c>\0</c>，
    /// 症状是"路径变成了 8 个空字符"（本批第一次跑就是这样）。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(
        uint codePage, uint flags, byte[] multiByte, int countOfMultiByte, char[]? wideChar, int countOfWideChar);

    /// <summary>这台机器的 ANSI 码页（诊断与测试都用它，而不是把 936 之类的数字抄进代码）。</summary>
    public static uint SystemAnsiCodePage => GetACP();

    /// <summary>这台机器的 OEM 码页（<c>CF_TEXT</c> 按 Win32 约定用它，见 <see cref="DecodeSystemOemText"/>）。</summary>
    public static uint SystemOemCodePage => GetOEMCP();

    /// <summary>按<b>系统 ACP</b>翻一段字节（<c>CF_HDROP</c> 那两个调用方交的就是这个）。</summary>
    public static string DecodeSystemAnsi(byte[] data, int start, int length)
        => Decode(data, start, length, SystemAnsiCodePage);

    /// <summary>
    /// 按<b>系统 OEM 码页</b>翻整段字节，供 <c>CF_TEXT</c> 用（P-18）。
    /// <para>为什么不干脆复用 ACP：Win32 把 <c>CF_TEXT</c> 定义为"当前 OEM 码页的多字节文本"，
    /// 而 <c>CF_HDROP</c> 的非宽形式是 ANSI——两个格式各有各的约定，按约定走才不猜。</para>
    /// <para>简体中文 Windows 上两者都是 936 ⇒ 这个区分对用户今天看到的读数<b>没有差别</b>；
    /// 差别只出现在 OEM≠ACP 的机器上（如英文机 437 vs 1252），那里按约定走才有一致的期望。</para>
    /// </summary>
    public static string DecodeSystemOemText(byte[] data, int start, int length)
        => Decode(data, start, length, SystemOemCodePage);

    /// <summary>
    /// 显式给码页的那一档：单测靠它把"同一串字节在 936 下是中文、在 1252 下是另一回事"钉成确定断言，
    /// 而不是依赖跑测试那台机器的系统配置。
    /// </summary>
    public static string Decode(byte[] data, int start, int length, uint codePage)
    {
        if (data is null || length <= 0) return string.Empty;
        // 越界不猜：交空串，让调用方按"这一段没有"处理（这里出错没有"半对的中文名"可比）。
        if (start < 0 || length > data.Length - start) return string.Empty;

        // UTF-16 的两个码页必须先接住：<c>MultiByteToWideChar</c> 只服务"多字节→宽字符"，
        // 把 1200/1201 交给它，实测返回 0 ⇒ 走到下面的宽化兜底，症状是"每个字节各变成一个字符"
        // （PowerShell 存出来的 UTF-16 文本文件正好就是这副样子）。
        if (codePage == ExternalText.Utf16LeCodePage || codePage == ExternalText.Utf16BeCodePage)
            return FromUtf16(data, start, length, codePage == ExternalText.Utf16BeCodePage);

        // 段起点交给系统：`byte[]` 参数只能从头封送，所以按 (start,length) 切出这一条路径的字节。
        // 一次复制几十字节，换掉一整层 unsafe 指针——这里的量级不值得为它开 unsafe。
        var segment = data.AsSpan(start, length).ToArray();

        var need = MultiByteToWideChar(codePage, 0, segment, length, null, 0);
        if (need <= 0) return WidenedInstead(data, start, length, codePage, "测算长度");

        var wide = new char[need];
        var wrote = MultiByteToWideChar(codePage, 0, segment, length, wide, need);
        if (wrote <= 0) return WidenedInstead(data, start, length, codePage, "转换");

        return new string(wide, 0, wrote);
    }

    /// <summary>
    /// UTF-16 的两个字节序：这里不需要"猜编码"——字节序是 BOM 已经说了的，剩下的只是配对。
    /// 尾部凑不成一对的那一个字节交掉（它本来就是半截字符，猜出来的那个字符比不猜更坏）。
    /// </summary>
    private static string FromUtf16(byte[] data, int start, int length, bool bigEndian)
    {
        var count = length / 2;
        var chars = new char[count];
        for (var i = 0; i < count; i++)
        {
            var first = data[start + i * 2];
            var second = data[start + i * 2 + 1];
            chars[i] = bigEndian
                ? (char)((first << 8) | second)
                : (char)(first | (second << 8));
        }
        return new string(chars);
    }

    /// <summary>
    /// 翻不动时退回<b>旧的逐字节宽化</b>：至少那条路径还在列表里、还能被点开，
    /// 而不是整段丢成空串——但一定留一条 Warn，不让它悄悄变成"今天又是乱码"。
    /// 现实里这一步几乎不可达（ACP 由系统给出、字节来自系统剪贴板/第三方库），所以宁肯多写一句日志。
    /// </summary>
    private static string WidenedInstead(byte[] data, int start, int length, uint codePage, string stage)
    {
        StarLog.Warn($"ANSI 码页解码失败（{stage}，codePage={codePage}，{length} 字节），退回逐字节宽化：这条路径可能显示为乱码。");
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = (char)data[start + i];
        return new string(chars);
    }
}
