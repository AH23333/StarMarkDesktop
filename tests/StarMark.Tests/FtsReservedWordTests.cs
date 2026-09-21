#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// 大写布尔保留字（AND/OR/NOT/NEAR）此前落入裸前缀分支 → 生成 <c>AND*</c> 之类的表达式，
/// FTS5 把 "AND" 当算符、对后面的 <c>*</c> 报 <c>fts5: syntax error</c>（语句级、整条查询崩）。
/// 修复后它们被降级为普通检索词（小写化去算符身份，仍走前缀），既不崩又能按字面召回。
/// </summary>
public sealed class FtsReservedWordTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public FtsReservedWordTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_ftsresv_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task SeedAsync()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "ftsresv", SourceId = "s1", Title = "搜索笔记工具" },
            new Item { Type = ItemType.Bookmark, Source = "ftsresv", SourceId = "s2", Title = "bread and butter" },
            new Item { Type = ItemType.Bookmark, Source = "ftsresv", SourceId = "s3", Title = "android runtime" },
        }, CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> SearchTitlesAsync(string keyword)
    {
        var repo = new ItemRepository(_factory);
        var result = await repo.SearchAsync(keyword, new SearchFilter { MaxResults = 20 }, CancellationToken.None);
        return result.Items.Select(i => i.Title).ToList();
    }

    [Theory]
    [InlineData("笔记 AND 工具")]
    [InlineData("AND")]
    [InlineData("OR")]
    [InlineData("NOT")]
    [InlineData("NEAR")]
    [InlineData("rust NOT async")]
    [InlineData("SQL OR NoSQL")]
    public async Task Search_UppercaseReservedWord_DoesNotThrow(string keyword)
    {
        await SeedAsync();
        var ex = await Record.ExceptionAsync(() => SearchTitlesAsync(keyword));
        Assert.Null(ex);   // 修复前：SqliteException "fts5: syntax error near \"*\""
    }

    // 保留字被当普通词，而非丢弃/整条空：独立 "AND" 仍应召回字面含 and 的条目。
    [Fact]
    public async Task Search_ReservedWord_AsLiteralTerm_Recalls()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("AND");

        Assert.Contains("bread and butter", titles);
    }

    // 大小写混合的普通前缀词不得被过度降级：And* 仍匹配 android / and。
    [Fact]
    public async Task Search_MixedCasePrefix_StillWorks()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("And");

        Assert.Contains("bread and butter", titles);
        Assert.Contains("android runtime", titles);
    }

    [Fact]
    public async Task Search_NormalQuery_Unaffected()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("笔记");

        Assert.Contains("搜索笔记工具", titles);
    }
}
