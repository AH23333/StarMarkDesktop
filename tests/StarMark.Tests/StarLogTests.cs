#nullable enable
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="StarLog"/> 日志伪造（CWE-117）净化契约护栏。StarLog 把内容拼进形如
/// <c>[时间戳] LEVEL 内容</c> 的记录；日志消息常含外部可控串（书签标题/拖入路径/URI/异常文本）。
/// 若其中的 CR/LF 原样落盘即可另起一行、伪造出以 <c>[时间戳]</c> 起始的条目、破坏"按 <c>^[</c> 锚定一行一事件"
/// 的解析不变式。本文件钉死两处纯函数（<c>SanitizeMessage</c> 与 <c>StackTail</c>）的安全契约，
/// 且验证"保留异常栈可读性"与"防伪"两者兼得（纯函数、无文件 I/O、不 flaky）。
/// </summary>
public sealed class StarLogTests
{
    [Theory]
    [InlineData("")]
    [InlineData("ordinary single line, no break")]
    [InlineData("path C:\\Users\\me\\file.txt")]
    [InlineData("tab\there kept verbatim")]
    public void Sanitize_WithoutNewline_ReturnsUnchanged(string message)
        // 无 CR/LF 的常态消息必须逐字节透传：修复不得改变正常日志内容。
        => Assert.Equal(message, StarLog.SanitizeMessage(message));

    [Theory]
    [InlineData("a\r\nb", "a\\nb")]   // CRLF 折叠成单个字面量，不得裂成两段
    [InlineData("a\rb", "a\\nb")]     // 孤立 CR
    [InlineData("a\nb", "a\\nb")]     // 孤立 LF
    public void Sanitize_CollapsesEachNewlineToOneLiteral(string input, string expected)
        => Assert.Equal(expected, StarLog.SanitizeMessage(input));

    [Fact]
    public void Sanitize_MixedBreaks_AllBecomeLiterals()
        => Assert.Equal("a\\nb\\nc\\nd", StarLog.SanitizeMessage("a\r\nb\rc\nd"));

    [Fact]
    public void Sanitize_EmitsNoRawCrLf_SoMessageStaysOneLine()
    {
        // 安全不变式：净化后的消息主体不得残留任何真实 CR/LF——否则伪造行成立。
        const string injected = "real\r\n[2020-01-01T00:00:00.0000000+08:00] INFO forged";
        var safe = StarLog.SanitizeMessage(injected);
        Assert.DoesNotContain('\r', safe);
        Assert.DoesNotContain('\n', safe);
    }

    [Fact]
    public void Sanitize_PreservesSurroundingText_NothingDropped()
    {
        var safe = StarLog.SanitizeMessage("keep-before\nkeep-after");
        Assert.Contains("keep-before\\nkeep-after", safe);
    }

    // ---- StackTail：既保留多行栈可读性，又保证续行不可被当作新条目 ----

    [Fact]
    public void StackTail_PreservesMultiLine_KeepsStackReadable()
    {
        var tail = StarLog.StackTail(new InvalidOperationException("boom"));
        // 栈仍跨多行（不被压成一行）——这是选择 A1 缩进方案而非全量转义的理由。
        Assert.Contains("\n", tail);
        Assert.Contains("boom", tail);
    }

    [Fact]
    public void StackTail_EveryPhysicalLineIsIndented_KeepsStackReadable()
    {
        // 构造 message 含伪造头 + 多行栈的异常；StackTail 后每一物理行都必须以四空格缩进开头。
        var ex = new Exception("prefix\n[2099-01-01T00:00:00.0000000+08:00] INFO forged\nsuffix");
        var tail = StarLog.StackTail(ex);
        Assert.All(tail.Split('\n'), line => Assert.True(
            line.Length == 0 || line.StartsWith("    "), "each continuation line must be indented"));
    }

    [Fact]
    public void StackTail_ForgedHeaderCarriedButIndented_EntryAnchorSafe()
    {
        // 缩进后攻击者伪造的 `[2099…] ERROR` 仍在文本里（不丢信息），但带前导空格、不以 '[' 起始，
        // 故按行锚定 `^[时间戳]` 的解析器不会把它误认成新条目——防伪成立。
        var tail = StarLog.StackTail(new Exception("msg\n[2099-01-01] ERROR evil"));
        Assert.Contains("[2099-01-01] ERROR evil", tail);              // 内容未丢
        Assert.DoesNotContain("\n[2099", tail);                        // 绝无"换行后紧接方括号"的行首形态
    }
}
