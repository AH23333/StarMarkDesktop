#nullable enable
using Xunit;
using StarMark.Abstractions;

namespace StarMark.Tests;

public sealed class UriNormalizerTests
{
    [Theory]
    [InlineData("https://github.com/a/b/", "https://github.com/a/b")]
    [InlineData("https://github.com/a/b?tab=readme", "https://github.com/a/b")]
    [InlineData("https://github.com/owner/repo/issues/123", "https://github.com/owner/repo")]
    [InlineData("http://example.com:80/x", "http://example.com/x")]
    [InlineData("https://example.com:443/x", "https://example.com/x")]
    [InlineData("https://Example.COM/x", "https://example.com/x")]
    [InlineData("https://example.com/p#frag", "https://example.com/p")]
    [InlineData("https://example.com/p?utm_source=x&id=1", "https://example.com/p?id=1")]
    [InlineData("https://example.com/p?fbclid=abc&id=1", "https://example.com/p?id=1")]
    public void Normalize_Standardizes(string input, string expected)
    {
        Assert.Equal(expected, UriNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_NonHttpPassthrough()
    {
        // file:// 等非 http(s) 源不应被改动，避免破坏文件路径键
        Assert.Equal("file:///C:/x.txt", UriNormalizer.Normalize("file:///C:/x.txt"));
    }

    /// <summary>
    /// 批次 Z 不变式：本地文件的 source_id 是 <see cref="LocalFileIdentity.SourceIdForPath"/> 产出的
    /// 裸 16 位十六进制串（不是 URI），而 <c>ItemRepository.UpsertOne</c> 对<b>每个</b> upsert 都会把
    /// source_id 喂给这个「面向 URL」的 Normalize。若 Normalize 哪天尝试「修正」这类短串（补协议、
    /// 改动大小写），文件去重键会被静默破坏、同一文件裂成多条。这里钉死恒等 + 幂等。
    /// 用真实哈希而非硬编码串，确保测的是实际落库形态。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\me\文档\报告 v2#1.txt")]
    [InlineData(@"D:\Program Files\app.exe")]
    [InlineData(@"E:\")]
    public void Normalize_LeavesLocalFileSourceIdUntouched(string path)
    {
        var sourceId = LocalFileIdentity.SourceIdForPath(path);
        Assert.Equal(sourceId, UriNormalizer.Normalize(sourceId));
        Assert.Equal(sourceId, UriNormalizer.Normalize(UriNormalizer.Normalize(sourceId)));
    }

    [Fact]
    public void Normalize_Idempotent()
    {
        var a = "https://github.com/a/b?tab=readme";
        Assert.Equal(UriNormalizer.Normalize(a), UriNormalizer.Normalize(UriNormalizer.Normalize(a)));
    }

    [Theory]
    [InlineData("http://example.com//", "http://example.com/")]
    [InlineData("http://example.com///", "http://example.com/")]
    [InlineData("https://github.com//", "https://github.com/")]
    public void Normalize_MultiSlashRoot_CollapsesToSingleSlashAndIsIdempotent(string input, string expected)
    {
        // 旧实现 TrimEnd('/') 把 "//" 塌成空串 → 重建得 "http://host"（无尾斜杠）；
        // 再次归一时 AbsolutePath 变 "/"（长度 1，不再触发去尾斜杠）→ "http://host/"，两次不一致、裂成两个 source_id。
        var once = UriNormalizer.Normalize(input);
        Assert.Equal(expected, once);
        Assert.Equal(once, UriNormalizer.Normalize(once)); // 幂等
    }

    [Fact]
    public void Normalize_EmptyReturnsEmpty()
    {
        Assert.Equal(string.Empty, UriNormalizer.Normalize(string.Empty));
        Assert.Equal(string.Empty, UriNormalizer.Normalize(null!));
    }

    [Fact]
    public void Normalize_KeepsNonDefaultPort()
    {
        Assert.Equal("https://example.com:8080/x", UriNormalizer.Normalize("https://example.com:8080/x"));
    }

    [Fact]
    public void Normalize_Ipv6Literal_PreservesBracketsAndIsIdempotent()
    {
        // .NET 的 Uri.Host 对 IPv6 字面量本就返回带方括号的 "[::1]"，归一后应保持括号且幂等。
        // 这条用例是"防回归"：任何试图再包一层方括号、或改用 Authority 拼端口导致错位的改动都会打破它。
        var once = UriNormalizer.Normalize("http://[::1]:8080/x");
        Assert.Equal("http://[::1]:8080/x", once);
        Assert.Equal(once, UriNormalizer.Normalize(once));

        // 默认端口 443 去掉，方括号保留
        Assert.Equal("https://[2001:db8::1]/p", UriNormalizer.Normalize("https://[2001:db8::1]:443/p"));
    }
}
