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

    [Fact]
    public void Normalize_Idempotent()
    {
        var a = "https://github.com/a/b?tab=readme";
        Assert.Equal(UriNormalizer.Normalize(a), UriNormalizer.Normalize(UriNormalizer.Normalize(a)));
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
}
