#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 剪贴板原始负载（<c>CF_UNICODETEXT</c> / <c>CF_TEXT</c> / <c>CF_HDROP</c>）的字节解码。
/// <para>
/// 这里只放"字节 → 字符串"的纯函数，不放任何 Win32 调用。同一份解码既要服务
/// 系统剪贴板的实时采集（<c>GetClipboardData</c> 拷出来的字节），也要服务 Ditto 库里
/// <c>Data.ooData</c> 的 BLOB —— 之前只有 Ditto 侧一份私有实现，抽出来共用，
/// 免得两条路径对"UTF-16 尾零怎么剥"各写一遍、各错一遍。
/// </para>
/// </summary>
public static class ClipboardPayload
{
    /// <summary>文本格式：宽字符（Windows 上最常见的 CF_UNICODETEXT）。</summary>
    public const string FormatUnicodeText = "CF_UNICODETEXT";

    /// <summary>文本格式：单字节（旧程序写入的 CF_TEXT）。</summary>
    public const string FormatAnsiText = "CF_TEXT";

    /// <summary>文件列表格式。</summary>
    public const string FormatHDrop = "CF_HDROP";

    /// <summary>
    /// 把文本类剪贴板负载解成字符串。
    /// <paramref name="ansi"/> 为真按单字节（UTF-8 容错读法）处理，否则按 UTF-16LE。
    /// </summary>
    public static string DecodeText(byte[]? data, bool ansi)
    {
        if (data is null || data.Length == 0) return string.Empty;
        try
        {
            if (ansi)
            {
                // ANSI/UTF-8：NUL 终止符是单字节，按字节剥离安全。
                var end = data.Length;
                while (end > 0 && data[end - 1] == 0) end--;
                return Encoding.UTF8.GetString(data, 0, end);
            }

            // UTF-16LE：NUL 终止符是「码元」= 2 字节 0x0000，必须按整个码元剥离。
            // 逐「字节」剥尾零再 `end & ~1` 会把以 ASCII 结尾的文本（'A' = 0x41,0x00）的合法高位
            // 字节 0x00 误当终止符吃掉，再 &~1 丢弃配对的 0x41 → 末字符被静默截断。
            // 先对齐到偶数字节，再仅剥离完整的 0x0000 对，杜绝奇数剥离与错切。
            var len = data.Length & ~1;
            while (len >= 2 && data[len - 2] == 0 && data[len - 1] == 0) len -= 2;
            return Encoding.Unicode.GetString(data, 0, len);
        }
        catch
        {
            return string.Empty;   // 调用方（后台采集 / 只读第三方库）都按"拿不到就当没有"处理
        }
    }

    /// <summary>
    /// 解析 <c>CF_HDROP</c> 的 DROPFILES 结构：头部（pFiles 指向列表起点）+ 双 NUL 结束的路径列表。
    /// <para>刻意不假设头部固定 20 字节：不同发送方给的 <c>pFiles</c> 不一样，越界一律按数据末尾夹住
    /// （<c>(int)pFiles</c> 直接强转一个 uint 会得到负数偏移，故必须先判上界）。</para>
    /// </summary>
    public static List<string> ParseDropFiles(byte[]? data)
    {
        var files = new List<string>();
        if (data is null || data.Length < 20) return files;

        var pFiles = BitConverter.ToUInt32(data, 0);
        var wide = BitConverter.ToUInt32(data, 16) != 0;
        var offset = (int)Math.Min(pFiles, (uint)data.Length);

        if (wide)
        {
            var sb = new StringBuilder();
            var i = offset;
            while (i + 1 < data.Length)
            {
                var ch = (char)BitConverter.ToUInt16(data, i);
                i += 2;
                if (ch == 0)
                {
                    if (sb.Length > 0)
                    {
                        files.Add(sb.ToString());
                        sb.Clear();
                    }
                    // 连续两个 NUL 表示列表结束
                    if (i + 1 < data.Length && BitConverter.ToUInt16(data, i) == 0) break;
                    continue;
                }
                sb.Append(ch);
            }
        }
        else
        {
            var sb = new StringBuilder();
            var i = offset;
            while (i < data.Length)
            {
                var ch = (char)data[i++];
                if (ch == 0)
                {
                    if (sb.Length > 0)
                    {
                        files.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (i < data.Length && data[i] == 0) break;
                    continue;
                }
                sb.Append(ch);
            }
        }
        return files;
    }
}
