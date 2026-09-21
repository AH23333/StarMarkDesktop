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
/// 纯空白关键词：SearchAsync 必须返回空结果，而非把 <c>MATCH ''</c> 交给 FTS5 触发语句级语法错误。
/// </summary>
/// <remarks>
/// 上游 <c>SearchService</c> 有 <c>IsNullOrWhiteSpace</c> 闸门，但 <c>ItemRepository.SearchAsync</c>
/// 是公共 Data 层契约（UI 回退分支 ItemGridWidgetViewModel 直接调用，不经 SafeAwait），
/// 不得对普通输入抛 <see cref="Microsoft.Data.Sqlite.SqliteException"/>。
/// </remarks>
public sealed class FtsBlankKeywordTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public FtsBlankKeywordTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_ftsblank_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task SeedAsync()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "ftsblank", SourceId = "b1", Title = "搜索笔记工具" },
            new Item { Type = ItemType.Bookmark, Source = "ftsblank", SourceId = "b2", Title = "bread and butter" },
        }, CancellationToken.None);
    }

    private async Task<SearchResult> SearchAsync(string keyword)
    {
        var repo = new ItemRepository(_factory);
        return await repo.SearchAsync(keyword, new SearchFilter { MaxResults = 20 }, CancellationToken.None);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(" \t \n ")]
    [InlineData("")]
    public async Task Search_BlankKeyword_ReturnsEmptyNoThrow(string keyword)
    {
        await SeedAsync();
        var result = await SearchAsync(keyword);   // 旧实现：MATCH '' → 语句级 fts5 语法错误抛出

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Search_NonBlank_StillRecalls()
    {
        await SeedAsync();
        var result = await SearchAsync("笔记");

        Assert.Contains("搜索笔记工具", result.Items.Select(i => i.Title));
    }
}
