#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板字节负载解码（批次 IC）。这份实现原先是 Ditto 读取器里的私有方法，
/// 现在实时采集与 Ditto 库两条路径共用 ⇒ 它必须自己有一组直接测试，而不是只靠 Ditto 的端到端用例间接覆盖。
/// <para>钉住的都是真出过的错：UTF-16 尾零按<b>码元</b>剥（按字节剥会吃掉末字符）、
/// pFiles 越界必须夹住（uint 强转 int 会得到负偏移）。</para>
/// </summary>
public sealed class ClipboardPayloadTests
{
    private static byte[] U(string s) => Encoding.Unicode.GetBytes(s + "\0");

    [Fact]
    public void DecodeText_Unicode_StripsOnlyFullZeroCodeUnits_KeepsLastAsciiChar()
    {
        // 'A' = 0x41 0x00：那个 0x00 是高字节，不是终止符。按字节剥尾零会把它当终止符吃掉，
        // 再把配对的 0x41 一起丢掉 ⇒ "…A" 的末字符静默消失。
        Assert.Equal("abcA", ClipboardPayload.DecodeText(U("abcA"), ansi: false));
        Assert.Equal("中文", ClipboardPayload.DecodeText(U("中文"), ansi: false));
        Assert.Equal("", ClipboardPayload.DecodeText(new byte[] { 0, 0 }, ansi: false));   // 只有终止符
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x41 })]              // 奇数长度：对齐到偶数字节，不能抛
    public void DecodeText_OddOrEmptyInput_DoesNotThrow(byte[]? data)
        => Assert.DoesNotContain('\u0000', ClipboardPayload.DecodeText(data, ansi: false));

    [Fact]
    public void DecodeText_Ani_StripsSingleByteTerminator()
        => Assert.Equal("hi", ClipboardPayload.DecodeText(new byte[] { (byte)'h', (byte)'i', 0 }, ansi: true));

    [Fact]
    public void ParseDropFiles_Wide_ListEndsAtDoubleNull_AndIgnoresTrailingGarbage()
    {
        var data = BuildDropFiles(wide: true, header: 20, @"C:\a.txt", @"D:\目录\b.docx");
        var files = ClipboardPayload.ParseDropFiles(data);

        Assert.Equal(new[] { @"C:\a.txt", @"D:\目录\b.docx" }, files);
    }

    [Fact]
    public void ParseDropFiles_Ansi_ProducesSameList()
    {
        var data = BuildDropFiles(wide: false, header: 20, @"C:\a.txt", @"D:\b.docx");
        Assert.Equal(new[] { @"C:\a.txt", @"D:\b.docx" }, ClipboardPayload.ParseDropFiles(data));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { })]
    public void ParseDropFiles_MissingOrEmpty_ReturnsEmpty(byte[]? data)
        => Assert.Empty(ClipboardPayload.ParseDropFiles(data));

    [Fact]
    public void ParseDropFiles_HeaderShorterThan20Bytes_ReturnsEmpty()
        => Assert.Empty(ClipboardPayload.ParseDropFiles(new byte[19]));   // 连头部都不完整

    [Fact]
    public void ParseDropFiles_AbsurdPFilesOffset_IsClampedNotNegative()
    {
        // pFiles 是 uint：直接 (int) 强转会得到负偏移，随后从数组末尾之外开始读 ⇒ 越界或读出垃圾。
        var data = BuildDropFiles(wide: true, header: 20, @"C:\a.txt");
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), uint.MaxValue);

        Assert.Empty(ClipboardPayload.ParseDropFiles(data));
    }

    [Fact]
    public void ParseDropFiles_NonZeroHeaderOffset_IsHonored()
    {
        // 有些发送方给的 pFiles 大于 20（头部后面还有拖放效果等字段），路径列表起点必须按它来。
        var data = BuildDropFiles(wide: true, header: 32, @"C:\a.txt");
        Assert.Equal(new[] { @"C:\a.txt" }, ClipboardPayload.ParseDropFiles(data));
    }

    private static byte[] BuildDropFiles(bool wide, int header, params string[] paths)
    {
        using var ms = new System.IO.MemoryStream();
        var head = new byte[header];
        BitConverter.TryWriteBytes(head.AsSpan(0, 4), (uint)header);
        if (wide) BitConverter.TryWriteBytes(head.AsSpan(16, 4), 1u);
        ms.Write(head, 0, header);

        foreach (var p in paths)
        {
            if (wide)
            {
                var b = Encoding.Unicode.GetBytes(p);
                ms.Write(b, 0, b.Length);
                ms.WriteByte(0); ms.WriteByte(0);
            }
            else
            {
                foreach (var c in p) ms.WriteByte((byte)c);
                ms.WriteByte(0);
            }
        }
        ms.WriteByte(0);
        if (wide) ms.WriteByte(0);
        return ms.ToArray();
    }
}

/// <summary>
/// 采集去重闸门（批次 IC）：一次复制不能因为应用连发多次通知就被记成好几次，
/// 更不能把"用户在 StarMark 里点了复制"记成一次新的复制。
/// </summary>
public sealed class ClipboardDedupeTests
{
    private const long T0 = 1_000_000;

    [Fact]
    public void SameContentWithinWindow_CountsOnce()
    {
        var d = new ClipboardDedupe(windowMs: 700);

        Assert.False(d.ShouldSkip("一段文本", T0));          // 第一次：放行
        Assert.True(d.ShouldSkip("一段文本", T0 + 100));      // Office/浏览器连发通知：挡掉
        Assert.True(d.ShouldSkip("一段文本", T0 + 700));      // 窗口边界（<=）仍算同一次
    }

    [Fact]
    public void SameContentAfterWindow_IsANewCopy()
    {
        var d = new ClipboardDedupe(windowMs: 700);
        Assert.False(d.ShouldSkip("一段文本", T0));
        Assert.False(d.ShouldSkip("一段文本", T0 + 701));      // 出窗口 ⇒ 是真的"又复制了一次"
    }

    [Fact]
    public void DifferentContent_IsNeverBlockedByBurstWindow()
    {
        var d = new ClipboardDedupe(windowMs: 700);
        Assert.False(d.ShouldSkip("甲", T0));
        Assert.False(d.ShouldSkip("乙", T0 + 10));
    }

    [Fact]
    public void OwnWrite_IsSwallowedExactlyOnce_ThenSameTextFromElsewhereRecords()
    {
        var d = new ClipboardDedupe(windowMs: 700);
        d.NoteOwnWrite("来自历史页的文本", T0);

        Assert.True(d.ShouldSkip("来自历史页的文本", T0));            // 回声：挡
        Assert.False(d.ShouldSkip("来自历史页的文本", T0 + 5_000));   // 登记已消费；5 s 后用户真的又抄了一次 ⇒ 记
    }

    [Fact]
    public void OwnWriteRegistration_IsPerContent_NotGlobal()
    {
        var d = new ClipboardDedupe(windowMs: 700);
        d.NoteOwnWrite("甲", T0);

        Assert.False(d.ShouldSkip("乙", T0));    // 登记的是甲，不能把乙也吞掉
        Assert.True(d.ShouldSkip("甲", T0 + 10));
    }

    [Fact]
    public void OwnWriteRing_IsBounded_AndOldestDropped()
    {
        var d = new ClipboardDedupe(windowMs: 700);
        for (var i = 0; i < ClipboardDedupe.OwnWriteCapacity + 5; i++)
            d.NoteOwnWrite($"条目 {i}", T0);

        // 最早的 5 条已被挤出登记 ⇒ 不再当回声吞掉
        Assert.False(d.ShouldSkip("条目 0", T0));
        Assert.True(d.ShouldSkip($"条目 {ClipboardDedupe.OwnWriteCapacity + 4}", T0));
    }

    // ==================== 回声登记必须会过期 ====================

    [Fact]
    public void OwnWriteToken_OutlivesNoLongerThanTtl()
    {
        // 现场复现的失效：一次"登记了但没写成功"（应用被挂起 / 剪贴板被别家占住 / 抛异常）
        // 会留下无人消费的令牌。若令牌只按容量淘汰，用户几小时后<b>真的</b>复制同一段文字
        // 会被当成回声吞掉一次＝那条复制静默丢失。
        var d = new ClipboardDedupe(windowMs: 700, ownWriteTtlMs: 5_000);
        d.NoteOwnWrite("同一段文本", T0);

        Assert.True(d.ShouldSkip("同一段文本", T0 + 5_000));      // 边界（== TTL）仍算有效期内
        Assert.False(d.ShouldSkip("同一段文本", T0 + 5_001));     // 过期 ⇒ 放行，不再吞真复制
    }

    [Fact]
    public void OwnWriteToken_WithFutureTimestamp_StillExpires()
    {
        // 用户把时钟往回调（或唤醒后系统对时）⇒ 登记的 AtMs 落在"未来"。
        // 带符号差值恒为负 ⟹ 永不过期，这条令牌就变成长期屏蔽；取绝对值才不会。
        var d = new ClipboardDedupe(windowMs: 700, ownWriteTtlMs: 5_000);
        d.NoteOwnWrite("同一段文本", T0);

        Assert.False(d.ShouldSkip("同一段文本", T0 - 5_001));
    }

    [Fact]
    public void BurstWindow_IsSymmetric_AroundClockDrift()
    {
        var d = new ClipboardDedupe(windowMs: 700);
        Assert.False(d.ShouldSkip("同一段", T0));

        // 倒退但仍落在窗口内：与正向一样按"同一次复制的连发"处理。
        Assert.True(d.ShouldSkip("同一段", T0 - 100));

        // 时钟倒退一小时（用户调钟 / 唤醒后系统对时）：这不是"刚复制过"，是真的一次新复制，必须记。
        Assert.False(d.ShouldSkip("同一段", T0 - 3_600_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n")]
    public void EmptyAfterNormalization_IsAlwaysSkipped(string? raw)
        => Assert.True(new ClipboardDedupe().ShouldSkip(raw, T0));
}
