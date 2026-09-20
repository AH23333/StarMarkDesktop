#nullable enable
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 打开条目协议白名单闸门：只放行 http/https/file，拒绝可污染记录借 Shell 协议处理器执行的其它 scheme。
/// </summary>
public sealed class LaunchGuardTests
{
    [Theory]
    [InlineData("https://github.com/a/b")]
    [InlineData("http://example.com")]
    [InlineData("HTTPS://uppercase-scheme.test/x")]          // 大小写不敏感
    [InlineData("file:///C:/Data/demo.txt")]                 // 三斜杠本地
    [InlineData("file://C:/Users/me/Docs/report.pdf")]       // 两斜杠（Everything 侧形态）
    public void AllowedSchemes_ReturnTrue(string uri)
        => Assert.True(LaunchGuard.IsAllowedScheme(uri));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ms-msdt:/id/foo")]
    [InlineData("search-ms:displayname=x")]
    [InlineData("cmd.exe")]                                   // 无 scheme → 非绝对 URI
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    public void BlockedSchemesAndGarbage_ReturnFalse(string uri)
        => Assert.False(LaunchGuard.IsAllowedScheme(uri));

    [Theory]
    [InlineData("unknownproto://x")]
    [InlineData("myapp://deep/link")]
    [InlineData("mailto:someone@example.com")]               // 邮件非本应用承载，拒之
    [InlineData("javascript&colon;alert(1)")]
    public void UnknownCustomAndMailto_ReturnFalse(string uri)
        => Assert.False(LaunchGuard.IsAllowedScheme(uri));

    [Fact]
    public void Null_ReturnsFalse() => Assert.False(LaunchGuard.IsAllowedScheme(null));

    // 「打开所在位置」把路径拼进 explorer.exe /select,"<path>"，被子进程重切参数：
    // 含双引号即可越界注入额外 explorer 参数（被污染备份/快照 uri 可携），故必须拒绝。真实 NTFS 名恒不含 "。
    [Theory]
    [InlineData(@"D:\C#项目\报告.txt")]              // 合法路径
    [InlineData(@"C:\Users\me\Docs\a b.txt")]        // 含空格仍合法
    [InlineData(@"C:\a&b<c>d|e.txt")]                // 其它特殊字符不含引号 → 放行
    public void SafeShellSelectTargets_ReturnTrue(string path)
        => Assert.True(LaunchGuard.IsSafeShellSelectTarget(path));

    [Theory]
    [InlineData(@"c:\x"" ""C:\Windows\System32\calc.exe""")]  // 经典越界注入
    [InlineData("with\"quote")]
    [InlineData("\"")]
    public void ShellSelectTargetsContainingQuote_ReturnFalse(string path)
        => Assert.False(LaunchGuard.IsSafeShellSelectTarget(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShellSelectTargetEmptyOrWhitespace_ReturnFalse(string path)
        => Assert.False(LaunchGuard.IsSafeShellSelectTarget(path));

    [Fact]
    public void ShellSelectTargetNull_ReturnsFalse() => Assert.False(LaunchGuard.IsSafeShellSelectTarget(null));
}
