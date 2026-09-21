#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using StarMark.Abstractions.Language;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="LanguageDetector"/> 分支契约护栏。它是每次 upsert 都会走到的纯函数（无硬件/网络），
/// 既有仅 ItemRepository 一处间接测（测的是 SQL type 闸门、非探测逻辑）。本文件钉死其文档化行为，
/// 当前实现正确、跑绿即防回归（补覆盖·非修缺陷）。
/// </summary>
public sealed class LanguageDetectorTests
{
    private static JsonObject Parse(string? json) => JsonNode.Parse(json!)!.AsObject();

    // ---- Detect：extra_json 标注优先，其次按源码扩展名兜底；不臆测、不误标网页 ----

    [Theory]
    [InlineData("https://ex.com/a/b/main.rs", "Rust")]
    [InlineData("https://ex.com/a/b/main.PY", "Python")]                 // 扩展名大小写不敏感
    [InlineData("https://ex.com/x/main.py?raw=1#frag", "Python")]        // query/fragment 走 AbsolutePath 天然排除
    [InlineData("https://ex.com/o/r/blob/HEAD/src/Foo.cs", "C#")]
    [InlineData("file:///C:/dev/tool.go", "Go")]
    [InlineData("https://ex.com/page.html", null)]                       // .html 故意不收录：网页书签不得误标语言
    [InlineData("https://ex.com/repo", null)]                            // 无扩展名
    [InlineData("not an absolute uri", null)]                            // 相对/非 URI 不臆测
    public void Detect_InfersFromSourceExtension_OnlyForKnownSourceFiles(string? uri, string? expected)
        => Assert.Equal(expected, LanguageDetector.Detect(null, uri, null, null));

    [Fact]
    public void Detect_PascalAnnotation_NormalizesAndBeatsExtension()
    {
        // 已有 Language 标注优先于扩展名；"typescript" 归一为规范名 "TypeScript"
        var got = LanguageDetector.Detect("{\"Language\":\"typescript\"}", "https://ex.com/a.py", null, null);
        Assert.Equal("TypeScript", got);
    }

    [Fact]
    public void Detect_LowercaseAnnotationKey_StillNormalized()
        => Assert.Equal("Ruby", LanguageDetector.Detect("{\"language\":\"ruby\"}", null, null, null));

    // ---- EnsureLanguage：不破坏既有 extra_json，最小写入 / 键归一 / 兜底不吞异常 ----

    [Fact]
    public void EnsureLanguage_ExistingPascalLanguage_PassesThroughByteForByte()
    {
        const string input = "{\"Language\":\"Python\",\"Repo\":\"x\"}";
        Assert.Equal(input, LanguageDetector.EnsureLanguage(input, "https://ex.com/a.go", null, null));
    }

    [Fact]
    public void EnsureLanguage_UnknownAnnotation_PreservedNotDropped()
    {
        // 目录未收录的语言必须原样保留（不得因 Normalize 未命中而丢弃用户数据）
        const string input = "{\"Language\":\"Brainfuck\"}";
        Assert.Equal(input, LanguageDetector.EnsureLanguage(input, null, null, null));
    }

    [Fact]
    public void EnsureLanguage_LowercaseKey_RenamedToPascalAndSiblingsPreserved()
    {
        var output = LanguageDetector.EnsureLanguage("{\"language\":\"python\",\"Stars\":5}", null, null, null);
        var o = Parse(output);
        Assert.Equal("Python", (string?)o["Language"]);
        Assert.False(o.ContainsKey("language"));   // 小写键须移除，避免大小写敏感过滤漏匹配
        Assert.Equal(5, (int?)o["Stars"]);         // 兄弟键不得丢失
    }

    [Fact]
    public void EnsureLanguage_InfersFromUriAndMergesIntoExistingObject()
    {
        var output = LanguageDetector.EnsureLanguage("{\"Foo\":\"bar\"}", "https://ex.com/main.go", null, null);
        var o = Parse(output);
        Assert.Equal("Go", (string?)o["Language"]);
        Assert.Equal("bar", (string?)o["Foo"]);    // 兜底写入须并入既有对象、不整体覆盖
    }

    [Fact]
    public void EnsureLanguage_CorruptJson_ReturnedUnchangedWithoutThrowing()
    {
        // 损坏 JSON：探测吞异常后无扩展名可判 → 原样返回、绝不抛、绝不用垃圾覆盖
        const string input = "{ not valid json";
        Assert.Equal(input, LanguageDetector.EnsureLanguage(input, null, null, null));
    }

    [Fact]
    public void EnsureLanguage_NothingDetectable_ReturnsOriginalNull()
        => Assert.Null(LanguageDetector.EnsureLanguage(null, "https://ex.com/page.html", null, null));
}
