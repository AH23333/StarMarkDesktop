#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;

namespace StarMark.Tests;

/// <summary>
/// <c>CF_HDROP</c> 非宽（ANSI）分支的<b>码页解码</b>（P-35，批次 SP）。
/// <para>
/// 旧写法在这里 <c>(char)byte</c>，等于假定"ANSI 码页＝Latin-1"：中文路径读出来是
/// <c>Ã¤Ã¶</c> 一类的怪字（每个字节一个 U+00xx 码元），那条剪辑点「打开」的目标随之失效。
/// 现在把整段字节交给系统的 <c>MultiByteToWideChar</c>——不新增 <c>CodePages</c> 依赖，
/// 也不把"这台机器的 ANSI 码页是哪个"抄成常量。
/// </para>
/// <para>
/// 断言刻意<b>显式传码页</b>（936）而不是读跑测试那台机器的 ACP：
/// 后者会让这条用例在英文 Windows 上"合理地失败"，那就是没人会再信的 flaky 闸门。
/// </para>
/// </summary>
public sealed class DropFilesAnsiCodePageTests
{
    private const uint Cp936 = 936;      // GBK：简体中文 Windows 的 ANSI 码页（只在断言里当参数用）

    private static readonly byte[] GbkDownloadFolder =
    {
        // "D:\下载\新建文件夹.txt" 的 GBK 字节（22 字节，用 GBK 编码器算定后逐字节抄下来，
        // 不让测试再去问系统"这台机器的 GBK 表长什么样"——那正是本批要避免的隐依赖）。
        0x44, 0x3A, 0x5C,
        0xCF, 0xC2, 0xD4, 0xD8, 0x5C,
        0xD0, 0xC2, 0xBD, 0xA8, 0xCE, 0xC4, 0xBC, 0xFE, 0xBC, 0xD0,
        0x2E, 0x74, 0x78, 0x74,
    };

    private const string GbkDownloadFolderText = @"D:\下载\新建文件夹.txt";

    // ==================== 解码器本身 ====================

    [Fact]
    public void Decode_GbkBytesWithExplicitCodePage_RecoversTheChinesePath()
        => Assert.Equal(GbkDownloadFolderText, AnsiText.Decode(GbkDownloadFolder, 0, GbkDownloadFolder.Length, Cp936));

    [Fact]
    public void Decode_IsNotTheOldPerByteWidening()
    {
        // 这条钉的是"缺陷确实被换掉了"：同一串字节，逐字节宽化会得到另一串东西（每个字节一个 U+00xx）。
        // 只断言"不等于"，不断言那串垃圾长什么样——它不是我们要维护的口径。
        var widened = new string(Array.ConvertAll(GbkDownloadFolder, b => (char)b));

        Assert.NotEqual(widened, AnsiText.Decode(GbkDownloadFolder, 0, GbkDownloadFolder.Length, Cp936));
        Assert.DoesNotContain('Ï', AnsiText.Decode(GbkDownloadFolder, 0, GbkDownloadFolder.Length, Cp936));
    }

    [Fact]
    public void Decode_AsciiIsIdenticalOnEveryCodePage()
    {
        var ascii = Encoding.ASCII.GetBytes(@"C:\a.txt");
        Assert.Equal(@"C:\a.txt", AnsiText.Decode(ascii, 0, ascii.Length, Cp936));
        Assert.Equal(@"C:\a.txt", AnsiText.Decode(ascii, 0, ascii.Length, 1252));
    }

    [Theory]
    [InlineData(0)]                     // 空段：调用方按"这一段没有"处理，不该造出一个字符
    [InlineData(-1)]                    // 负长度（上游算错）同样不猜
    public void Decode_NonPositiveLength_ReturnsEmpty(int length)
        => Assert.Equal(string.Empty, AnsiText.Decode(GbkDownloadFolder, 0, length, Cp936));

    [Theory]
    [InlineData(20, 8)]                 // 起点 + 长度超出数组末尾
    [InlineData(-1, 4)]                 // 负起点
    public void Decode_OutOfRange_ReturnsEmptyInsteadOfGuessing(int start, int length)
        => Assert.Equal(string.Empty, AnsiText.Decode(GbkDownloadFolder, start, length, Cp936));

    [Fact]
    public void Decode_TruncatedMultiByteTail_DoesNotThrow_AndKeepsTheRest()
    {
        // 截到"D:\下载\" + 一枚没有配对的 GBK 前字节（0xD0 是"新"的前半）——最常见的畸形尾巴。
        // 旧写法在这里塞进一个 U+00D0 怪字；新写法不许抛、不许把整段丢光，也不许再吐那个怪字。
        var truncated = GbkDownloadFolder.AsSpan(0, 9).ToArray();

        var decoded = AnsiText.Decode(truncated, 0, truncated.Length, Cp936);

        Assert.StartsWith(@"D:\下载", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', decoded);
        Assert.DoesNotContain('Ð', decoded);
    }

    [Fact]
    public void SystemAnsiCodePage_IsReported_NotHardCoded()
    {
        // 生产路径读的是系统 ACP（这里只要求它给出一个"像样的"码页号）：
        // 把 936 抄进 src 才是本批要避免的那种错——换台机器就悄悄错。
        Assert.True(AnsiText.SystemAnsiCodePage > 0);
    }

    // ==================== ParseDropFiles 的接线与边界 ====================

    [Fact]
    public void ParseDropFiles_NonWide_RoutesEverySegmentThroughTheGivenDecoder()
    {
        var calls = new List<string>();
        var data = BuildAnsiDropFiles(@"C:\a.txt", @"D:\b.docx");

        var files = ClipboardPayload.ParseDropFiles(data, (bytes, start, len) =>
        {
            var s = "»" + Encoding.ASCII.GetString(bytes, start, len);
            calls.Add(s);
            return s;
        });

        Assert.Equal(new[] { @"»C:\a.txt", @"»D:\b.docx" }, files);
        Assert.Equal(2, calls.Count);       // 每段恰好一次，不多切一遍也不少切一遍
    }

    [Fact]
    public void ParseDropFiles_Wide_NeverAsksForAnAnsiDecoder()
    {
        var calls = 0;
        var data = BuildWideDropFiles(@"D:\目录\b.docx");

        var files = ClipboardPayload.ParseDropFiles(data, (_, _, _) => { calls++; return string.Empty; });

        Assert.Equal(new[] { @"D:\目录\b.docx" }, files);
        Assert.Equal(0, calls);             // 宽形自己就是 UTF-16：谁去"翻码页"谁就会把路径翻坏
    }

    [Fact]
    public void ParseDropFiles_NonWide_HandsTheSegmentToTheSystemDecoder()
    {
        // 端到端只钉"接线"（这一段字节确实交给了系统解码器、没有半路自己宽化），
        // 不断言"解出来必是那个中文路径"——那是系统码页的事实，在英文 Windows 上会合理地不同（＝flaky）。
        // 内容层面的确定断言在上面那两条（显式传 936）。
        var data = BuildAnsiDropFilesRaw(GbkDownloadFolder);

        var files = ClipboardPayload.ParseDropFiles(data, AnsiText.DecodeSystemAnsi);

        var only = Assert.Single(files);
        Assert.Equal(AnsiText.Decode(GbkDownloadFolder, 0, GbkDownloadFolder.Length, AnsiText.SystemAnsiCodePage), only);
        Assert.StartsWith("D:", only, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseDropFiles_NonWide_ListStartingWithNull_IsEmpty()
    {
        // 旧写法：首字节就是 NUL ⇒ sb 为空 ⇒ 撞上"连续 NUL"直接结束 ⇒ 空列表。改写后必须一模一样。
        var data = BuildAnsiDropFilesRaw(Array.Empty<byte>(), Array.Empty<byte>());

        Assert.Empty(ClipboardPayload.ParseDropFiles(data, AnsiText.DecodeSystemAnsi));
    }

    [Fact]
    public void ParseDropFiles_NonWide_LastSegmentWithoutDoubleNull_StillLands()
    {
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes(@"C:\a.txt"));
        body.Add(0);            // 单 NUL 收尾，没有列表级的双 NUL（有的发送方就这么写）
        Assert.Equal(new[] { @"C:\a.txt" },
            ClipboardPayload.ParseDropFiles(BuildAnsiDropFilesRaw(body.ToArray()), AnsiText.DecodeSystemAnsi));

        body.Add(0);            // 补上双 NUL ⇒ 结果不许变
        Assert.Equal(new[] { @"C:\a.txt" },
            ClipboardPayload.ParseDropFiles(BuildAnsiDropFilesRaw(body.ToArray()), AnsiText.DecodeSystemAnsi));
    }

    [Fact]
    public void ParseDropFiles_NonWide_StopsAtDoubleNull_IgnoringTrailingGarbage()
    {
        // 列表级终止是"连续两个 NUL"；那之后的字节（这里是 'XXXX'）不许被当成第二条路径。
        var ms = new System.IO.MemoryStream();
        var head = new byte[20];
        BitConverter.TryWriteBytes(head.AsSpan(0, 4), 20u);
        ms.Write(head, 0, head.Length);
        ms.Write(Encoding.ASCII.GetBytes(@"C:\a.txt"), 0, 8);
        ms.WriteByte(0); ms.WriteByte(0);
        ms.Write(Encoding.ASCII.GetBytes("XXXX"), 0, 4);

        Assert.Equal(new[] { @"C:\a.txt" },
            ClipboardPayload.ParseDropFiles(ms.ToArray(), AnsiText.DecodeSystemAnsi));
    }

    // ==================== 造 blob ====================

    private static byte[] BuildAnsiDropFiles(params string[] asciiPaths)
    {
        var parts = new byte[asciiPaths.Length][];
        for (var i = 0; i < asciiPaths.Length; i++) parts[i] = Encoding.ASCII.GetBytes(asciiPaths[i]);
        return BuildAnsiDropFilesRaw(parts);
    }

    private static byte[] BuildAnsiDropFilesRaw(params byte[][] segments)
    {
        var ms = new System.IO.MemoryStream();
        var head = new byte[20];
        BitConverter.TryWriteBytes(head.AsSpan(0, 4), 20u);         // pFiles＝20（fWide 保持 0＝ANSI）
        ms.Write(head, 0, head.Length);
        foreach (var seg in segments)
        {
            ms.Write(seg, 0, seg.Length);
            ms.WriteByte(0);
        }
        ms.WriteByte(0);                                            // 列表级终止 NUL
        return ms.ToArray();
    }

    private static byte[] BuildWideDropFiles(params string[] paths)
    {
        var ms = new System.IO.MemoryStream();
        var head = new byte[20];
        BitConverter.TryWriteBytes(head.AsSpan(0, 4), 20u);
        BitConverter.TryWriteBytes(head.AsSpan(16, 4), 1u);         // fWide＝1
        ms.Write(head, 0, head.Length);
        foreach (var p in paths)
        {
            var b = Encoding.Unicode.GetBytes(p);
            ms.Write(b, 0, b.Length);
            ms.WriteByte(0); ms.WriteByte(0);
        }
        ms.WriteByte(0); ms.WriteByte(0);
        return ms.ToArray();
    }
}
