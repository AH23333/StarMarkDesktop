#nullable enable
using StarMark.Abstractions.Language;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="LanguageCatalog"/> 归一契约护栏。Normalize 是每次 upsert 语言标注与
/// <c>ItemRepository</c> 语言分桶（:428）都会走到的纯函数（无硬件/网络），此前**无任何直接测试**：
/// <see cref="LanguageDetector"/> 的既有测（LanguageDetectorTests）只覆盖探测分支路由、并未钉死目录归一的边界语义。
/// 本文件锁定其文档化行为——当前实现正确、跑绿即防回归（补覆盖·非修缺陷）。
/// 关键非显然契约：空白串原样返回（<b>不</b> trim）、未知语言保留但<b>已</b> trim、
/// 以及 <see cref="LanguageCatalog.NameOfCode"/> 与 Normalize 在大小写/trim 上的刻意不对称。
/// </summary>
public sealed class LanguageCatalogTests
{
    // ---- Normalize：空白兜底 / trim / 大小写不敏感归一 / 未知保留 ----

    [Theory]
    // 空白串走 IsNullOrWhiteSpace 早退，返回**原始未 trim 入参**（不是空串）：这是易被"顺手清理"改坏的边界。
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("\t\n ", "\t\n ")]
    // 已收录语言：大小写不一 → 归一为规范展示名（Code 与 Name 同名，经 byName 不区分大小写命中）。
    [InlineData("Python", "Python")]
    [InlineData("python", "Python")]
    [InlineData("c#", "C#")]
    [InlineData("objective-c", "Objective-C")]
    [InlineData("c", "C")]
    // 先 trim 再命中：两侧空白不得阻碍归一。
    [InlineData("  rust  ", "Rust")]
    [InlineData(" C++ ", "C++")]
    // 目录未收录：原样保留用户数据，但**已 trim**（与上方空白早退"不 trim"形成对照）。
    [InlineData("  Brainfuck  ", "Brainfuck")]
    [InlineData("FooBar", "FooBar")]
    public void Normalize_CanonicalizesKnownAndPreservesUnknown(string raw, string expected)
        => Assert.Equal(expected, LanguageCatalog.Normalize(raw));

    // ---- NameOfCode：按 Code 精确取展示名，刻意大小写敏感且**不 trim**（与 Normalize 不对称）----

    [Fact]
    public void NameOfCode_ExactCaseHit_ReturnsName()
        => Assert.Equal("Python", LanguageCatalog.NameOfCode("Python"));

    [Fact]
    public void NameOfCode_IsCaseSensitive_UnlikeNormalize()
    {
        // Normalize 对大小写不敏感……
        Assert.Equal("Python", LanguageCatalog.Normalize("python"));
        // ……但 NameOfCode 走 _byCode(Ordinal 区分大小写)，小写码必须返回 null。
        Assert.Null(LanguageCatalog.NameOfCode("python"));
    }

    [Fact]
    public void NameOfCode_DoesNotTrim_UnlikeNormalize()
    {
        Assert.Equal("Rust", LanguageCatalog.Normalize("  Rust  "));  // Normalize 会 trim
        Assert.Null(LanguageCatalog.NameOfCode("Rust "));             // NameOfCode 不 trim → 未命中
    }

    [Fact]
    public void NameOfCode_UnknownCode_ReturnsNull()
        => Assert.Null(LanguageCatalog.NameOfCode("Brainfuck"));
}
