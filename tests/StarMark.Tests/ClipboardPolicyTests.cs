#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板历史<b>策略层</b>契约（批次 IA）。
/// <para>
/// 这一层决定"什么会被抄进一个明文 SQLite 文件"，所以断言的方向是双向的：
/// 既钉"正常内容一定记"（少记＝功能失效），也钉"密码形态一定不记"（多记＝泄露）。
/// 全部为纯函数判定，不涉及 Win32 ⇒ 采集窗口无法在这里伪造通过。
/// </para>
/// </summary>
public sealed class ClipboardPolicyTests
{
    // ==================== 归一：幂等键的地基 ====================

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  abc  ", "abc")]
    [InlineData("a\r\nb", "a\nb")]      // 记事本/CRLF
    [InlineData("a\rb", "a\nb")]        // 老工具/裸 CR
    [InlineData("a\n\n b ", "a\n\n b")] // 只去首尾，内部换行与空白逐字保留（否则键会把不同内容折叠成同一条）
    public void NormalizeText_CrlfAndCrBecomeLf_AndOnlyOuterWhitespaceGoes(string? raw, string expected)
        => Assert.Equal(expected, ClipboardPolicy.NormalizeText(raw));

    [Fact]
    public void BuildSourceId_SameTextDifferentLineEndings_SameKey()
    {
        // 同一段话从 Word（CRLF）和从浏览器（LF）复制 ⇒ 必须是同一条历史。
        var fromWord = ClipboardPolicy.BuildSourceId(ClipboardPolicy.NormalizeText("第一行\r\n第二行"));
        var fromWeb = ClipboardPolicy.BuildSourceId(ClipboardPolicy.NormalizeText("第一行\n第二行"));
        Assert.Equal(fromWord, fromWeb);
    }

    [Fact]
    public void BuildSourceId_PrefixedFixedLengthHex_AndDistinguishesTrailingNewlineInside()
    {
        var id = ClipboardPolicy.BuildSourceId("hello");
        Assert.StartsWith(ClipboardPolicy.SourceIdPrefix, id);
        var hex = id.Substring(ClipboardPolicy.SourceIdPrefix.Length);
        Assert.Equal(32, hex.Length);                       // 16 字节 → 32 位十六进制
        Assert.True(hex.All(Uri.IsHexDigit));
        // 内部差异必须落到不同键上："a b" 与 "a  b"（两个空格）是两条内容
        Assert.NotEqual(ClipboardPolicy.BuildSourceId("a b"), ClipboardPolicy.BuildSourceId("a  b"));
        // 大小写敏感：Copy 与 copy 不能互相覆盖
        Assert.NotEqual(ClipboardPolicy.BuildSourceId("Copy"), ClipboardPolicy.BuildSourceId("copy"));
    }

    [Fact]
    public void BuildSourceId_UsesFullText_NotTruncatedBody()
    {
        // 两份"前 32 KB 完全相同、尾部不同"的长文若撞键，第二条会把第一条静默覆盖。
        var head = new string('x', ClipboardPolicy.MaxStoredChars);
        var a = ClipboardPolicy.BuildSourceId(head + "_A");
        var b = ClipboardPolicy.BuildSourceId(head + "_B");
        Assert.NotEqual(a, b);
    }

    // ==================== 该不该记 ====================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("a")]          // 单字符：误触或密码框里顺手带出来的一个字符 ⇒ 一律不记
    public void ShouldRecord_TooShortOrBlank_NotRecorded(string? raw)
        => Assert.False(ClipboardPolicy.ShouldRecord(raw, "chrome", out _));

    [Fact]
    public void ShouldRecord_NormalText_RecordedWithNormalizedBody()
    {
        Assert.True(ClipboardPolicy.ShouldRecord("  会议改到 3 点\r\n地点：B2 ", "chrome", out var text));
        Assert.Equal("会议改到 3 点\n地点：B2", text);
    }

    [Theory]
    [InlineData("KeePass")]
    [InlineData("KeePass.exe")]
    [InlineData("keepass2")]
    [InlineData("1Password-8")]
    [InlineData("Bitwarden")]
    [InlineData("LastPass")]
    [InlineData("Proton Pass")]
    [InlineData("CredentialUI")]
    public void ShouldRecord_FromPasswordManager_NotRecorded(string app)
    {
        // 内容本身完全无害（"hello world 123"），被拒只能是因为来源 ⇒ 这条断言才有鉴别力。
        Assert.False(ClipboardPolicy.ShouldRecord("hello world 123", app, out _));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Code")]
    [InlineData("Taskmgr")]
    [InlineData("")]
    [InlineData(null)]
    public void ShouldRecord_AllowedApps_StillRecorded(string? app)
        => Assert.True(ClipboardPolicy.ShouldRecord("hello world 123", app, out _));

    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXk")]
    [InlineData("before\n-----BEGIN PGP PRIVATE KEY BLOCK-----\n")]
    public void IsSensitive_PrivateKeyBlocked(string text)
        => Assert.True(ClipboardPolicy.IsSensitive(text));

    [Fact]
    public void IsSensitive_JwtBlocked_ButEyhAloneIsNot()
    {
        Assert.True(ClipboardPolicy.IsSensitive(
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U"));
        Assert.True(ClipboardPolicy.IsSensitive("Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpX.eyJzdWIiOiJhZG1pbiJ9.c2lnLXBhZC1ub3QtdGhhdC1sb25nLWJ1dC1lbi9kZGY="));
        // 只含 "eyJ" 的正常英文不误伤；不足三段（只有 header）也不算 JWT
        Assert.False(ClipboardPolicy.IsSensitive("the eyJ token key here is not a json web token at all"));
        Assert.False(ClipboardPolicy.IsSensitive("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"));
    }

    [Theory]
    [InlineData("4111 1111 1111 1111")]        // Visa 测试号，分组空格
    [InlineData("4111111111111111")]           // 连续 16 位
    [InlineData("5500-0000-0000-0004")]        // Mastercard 测试号，连字符
    [InlineData("卡号 4111 1111 1111 1111 有效期 12/30")]  // 混在正文里也要抓到
    public void IsSensitive_CardNumberBlocked(string text)
        => Assert.True(ClipboardPolicy.IsSensitive(text));

    [Theory]
    [InlineData("4111111111111112")]              // Luhn 不过
    [InlineData("订单号 123456789012")]           // 12 位：短于卡号区间
    [InlineData("2024050112345678901234")]        // 22 位纯数字：超出卡号区间
    [InlineData("x4111111111111111")]             // 前面紧挨字母 ⇒ 是更长标识符的一部分
    [InlineData("4111111111111111y")]             // 后面紧挨字母
    [InlineData("12:30")]
    public void IsSensitive_LooksLikeDigitsButIsNot_CardNotFlagged(string text)
        => Assert.False(ClipboardPolicy.ContainsCardNumber(text));

    [Fact]
    public void ContainsCardNumber_SeparatorsOnlyGroupAdjacentDigits()
    {
        // 20 位纯数字里没有任何合法的 13–19 位"干净窗口"（两端不能紧挨数字），整串都不该判成卡号。
        Assert.False(ClipboardPolicy.ContainsCardNumber("1111 2222 3333 4444 5555"));
        // 已知合法测试卡：分组与连续两种形态都要抓到
        Assert.True(ClipboardPolicy.ContainsCardNumber("4111 1111 1111 1111"));
        Assert.True(ClipboardPolicy.ContainsCardNumber("4111111111111111"));
        // 分隔符后不接数字 ⇒ 不许吞（"4111 1111 1111 1111 x" 仍是卡号；但 "4111  x…" 会断开成 4 位）
        Assert.False(ClipboardPolicy.ContainsCardNumber("4111 x 1111 1111 1111"));
    }

    [Fact]
    public void PassesLuhn_KnownVectors()
    {
        Assert.True(ClipboardPolicy.PassesLuhn("4111111111111111".ToCharArray(), 16));
        Assert.False(ClipboardPolicy.PassesLuhn("4111111111111112".ToCharArray(), 16));
        Assert.True(ClipboardPolicy.PassesLuhn("4242424242424242".ToCharArray(), 16));   // Stripe 测试卡
    }

    // ==================== 标题 / 截断 ====================

    [Fact]
    public void BuildTitle_FirstLineOnly_WithEllipsisWhenMultiline()
    {
        Assert.Equal("第一行", ClipboardPolicy.BuildTitle("第一行"));
        Assert.Equal("第一行…", ClipboardPolicy.BuildTitle("第一行\n第二行\n第三行"));
        // 制表与换行等控制符折成空格：粘进卡片不能出现裸控制字符
        Assert.Equal("a b c", ClipboardPolicy.BuildTitle("a\tb\u00a0c"));
    }

    [Fact]
    public void BuildTitle_TruncatesWithinBudget()
    {
        var title = ClipboardPolicy.BuildTitle(new string('z', ClipboardPolicy.MaxTitleChars + 500));
        Assert.True(title.Length <= ClipboardPolicy.MaxTitleChars, $"标题超出预算：{title.Length}");
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Truncate_BoundaryIsInclusive()
    {
        var at = ClipboardPolicy.Truncate(new string('q', ClipboardPolicy.MaxStoredChars), out var t1);
        Assert.False(t1);
        Assert.Equal(ClipboardPolicy.MaxStoredChars, at.Length);

        var over = ClipboardPolicy.Truncate(new string('q', ClipboardPolicy.MaxStoredChars + 7), out var t2);
        Assert.True(t2);
        Assert.Equal(ClipboardPolicy.MaxStoredChars, over.Length);
    }
}
