#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Text;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// 中文全文检索回归测试。
/// </summary>
/// <remarks>
/// FTS5 用 <c>tokenize='porter unicode61'</c>，而 unicode61 把一段连续 CJK 视为
/// <b>一个</b> token。若不展开，索引里「搜索笔记工具」只有 1 个 token，
/// 搜「笔记」「工具」这类中间子串全部落空。以下用例确保该缺陷不回归。
/// </remarks>
public sealed class CjkSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public CjkSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_cjk_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    // ────────────────────────── 分词器纯函数 ──────────────────────────

    [Fact]
    public void IsCjk_CoversChineseJapaneseKorean()
    {
        Assert.True(CjkTokenizer.IsCjk('搜'));
        Assert.True(CjkTokenizer.IsCjk('あ'));   // 平假名
        Assert.True(CjkTokenizer.IsCjk('カ'));   // 片假名
        Assert.True(CjkTokenizer.IsCjk('한'));   // 韩文
        Assert.False(CjkTokenizer.IsCjk('A'));
        Assert.False(CjkTokenizer.IsCjk('1'));
        Assert.False(CjkTokenizer.IsCjk(' '));
    }

    [Fact]
    public void ExpandForIndex_EmitsSinglesAndBigrams()
    {
        string expanded = CjkTokenizer.ExpandForIndex("搜索");

        // 单字 + 相邻二元组；整串不发送（查询侧从不发整串，发了纯属浪费索引）
        Assert.Equal(new[] { "搜", "索", "搜索" },
                     expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ExpandForIndex_DoesNotEmitWholeRun()
    {
        string expanded = CjkTokenizer.ExpandForIndex("搜索笔记工具");

        Assert.DoesNotContain("搜索笔记工具", expanded);
        // 但二元组必须完整覆盖任意子串
        Assert.Contains("笔记", expanded);
        Assert.Contains("记工", expanded);
        Assert.Contains("工具", expanded);
    }

    [Fact]
    public void ExpandForIndex_PreservesNonCjkVerbatim()
    {
        string expanded = CjkTokenizer.ExpandForIndex("C# WinUI 桌面");

        // 非 CJK 部分原样保留（含 '#'
        Assert.StartsWith("C# WinUI ", expanded);
        // CJK 部分被展开
        Assert.Contains("桌面", expanded);
        Assert.Contains("桌", expanded);
        Assert.Contains("面", expanded);
    }

    [Fact]
    public void ExpandForIndex_HandlesLongRun()
    {
        var longRun = new string('搜', 200);
        string expanded = CjkTokenizer.ExpandForIndex(longRun);

        Assert.DoesNotContain(longRun, expanded);
        Assert.Contains("搜", expanded);      // 单字
        Assert.Contains("搜搜", expanded);    // 二元组
    }

    [Fact]
    public void SplitForQuery_NeverEmitsWholeRun()
    {
        // 铁律：整串绝不进查询侧，否则 4 字以上中文查询会自我淘汰
        var tokens = CjkTokenizer.SplitForQuery("笔记工具");

        Assert.Equal(new[] { "笔记", "记工", "工具" }, tokens);
        Assert.DoesNotContain("笔记工具", tokens);
    }

    [Fact]
    public void SplitForQuery_SingleCharEmitsItself()
    {
        Assert.Equal(new[] { "书" }, CjkTokenizer.SplitForQuery("书"));
    }

    [Fact]
    public void SplitForQuery_HandlesMixedAndMultipleParts()
    {
        var tokens = CjkTokenizer.SplitForQuery("winui 桌面组件");

        Assert.Equal(new[] { "winui", "桌面", "面组", "组件" }, tokens);
    }

    // ────────────────────────── 端到端 FTS 检索 ──────────────────────────

    private static readonly (string Title, string SourceId)[] Corpus =
    {
        ("搜索笔记工具", "c1"),
        ("StarMark 统一搜索", "c2"),
        ("如何整理收藏夹", "c3"),
        ("C# WinUI 桌面组件开发", "c4"),
    };

    private async Task SeedAsync()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(Corpus.Select(c => new Item
        {
            Type = ItemType.Bookmark,
            Source = "cjk-test",
            SourceId = c.SourceId,
            Title = c.Title,
        }).ToArray(), CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> SearchTitlesAsync(string keyword)
    {
        var repo = new ItemRepository(_factory);
        var result = await repo.SearchAsync(keyword, new SearchFilter { MaxResults = 20 }, CancellationToken.None);
        return result.Items.Select(i => i.Title).ToList();
    }

    /// <summary>这些查询在旧实现（未展开）下全部落空或大量漏召回。</summary>
    [Theory]
    [InlineData("笔记", "搜索笔记工具")]                       // 中间子串
    [InlineData("工具", "搜索笔记工具")]                       // 尾部子串
    [InlineData("搜索", "搜索笔记工具", "StarMark 统一搜索")]  // 跨两条
    [InlineData("收藏", "如何整理收藏夹")]
    [InlineData("整理收藏", "如何整理收藏夹")]                 // 跨词边界
    [InlineData("组件", "C# WinUI 桌面组件开发")]
    [InlineData("桌面组件", "C# WinUI 桌面组件开发")]          // 4 字查询
    [InlineData("统一", "StarMark 统一搜索")]                  // 2 字词
    public async Task Search_ChineseSubstring_IsRecallable(string keyword, params string[] expected)
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync(keyword);

        Assert.Equal(expected.OrderBy(x => x), titles.OrderBy(x => x));
    }

    [Fact]
    public async Task Search_ChineseNoMatch_ReturnsEmpty()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("量子物理");

        Assert.Empty(titles);
    }

    [Fact]
    public async Task Search_EnglishPrefix_StillWorks()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("WinUI");

        Assert.Contains("C# WinUI 桌面组件开发", titles);
    }

    [Fact]
    public async Task Search_SpecialCharKeyword_DoesNotThrow()
    {
        await SeedAsync();
        // 'C#' 含非字母数字字符，走引号转义分支
        var titles = await SearchTitlesAsync("C#");

        Assert.NotNull(titles);
    }

    // ────────────────────────── 迁移 v3 ──────────────────────────

    [Fact]
    public async Task MigrateV3_RebuildsLegacySearchText()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "legacy", SourceId = "L1", Title = "搜索笔记工具" },
        }, CancellationToken.None);

        // 人为把 search_text 还原成「未展开」的旧形态，并把版本号退回 2，
        // 模拟升级前的存量库
        using (var conn = _factory.Open())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE items SET search_text = '搜索笔记工具';";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sync_state(key, value) VALUES('schema_version', '2')
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                cmd.ExecuteNonQuery();
            }
            // 用旧文本重建索引，确保起点确实是「坏的」
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO items_fts(items_fts) VALUES('rebuild');";
                cmd.ExecuteNonQuery();
            }
        }

        Assert.Empty(await SearchTitlesAsync("笔记"));   // 迁移前：落空

        new MigrationRunner(_factory).EnsureSchema();     // 触发 v3

        Assert.Equal(new[] { "搜索笔记工具" }, await SearchTitlesAsync("笔记"));
    }
}
