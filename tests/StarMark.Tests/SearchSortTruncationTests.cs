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
/// 关键词搜索的<b>截断落在哪一步</b>（P-33，批次 SO；他的裁决＝只改非默认排序那条腿）。
/// <para>
/// 旧写法只在 FTS 的 CTE 里按 bm25 取前 <c>MaxResults</c> 条，外层再按用户选的排序重排<b>幸存的那一小撮</b>。
/// 相关度腿这么做是诚实的（截断与最终排序同一个次序）；用户改按名字/时间/Star 数排就不是了——
/// 相关度前排的行占住名额，"名字最靠前的那几条"根本进不了结果集，
/// 而界面表现为"我选了按名字排，却没反应"，用户不会想到是少返回而不是没排序。
/// </para>
/// <para>
/// 所以这里钉两件事：① 非默认排序时，截断必须发生在<b>过滤与排序之后</b>（少返回与错序是同一个根因）；
/// ② 相关度腿的短路<b>不许顺手一起拆掉</b>（那是 P-49 换来的性能，正向对照专门守这一侧）。
/// </para>
/// </summary>
public sealed class SearchSortTruncationTests : IDisposable
{
    private const string Keyword = "rag";
    private const long BaseTime = 1_700_000_000L;

    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public SearchSortTruncationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_p33_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    /// <summary>
    /// 12 条都命中 "rag"，而<b>词频与字母序正好相反</b>：alpha01 出现 1 次 … alpha12 出现 12 次
    /// ⇒ 相关度前排是 alpha12…alpha08，字母序前排是 alpha01…alpha05，两批互不相交。
    /// 时间也刻意与字母序同向（alpha01 最新），所以"按最近更新"排同样是相关度那条腿截不出来的东西。
    /// <para>
    /// 这不是"随便造点数据"：夹具必须让两条腿<b>给出不同答案</b>，否则修前修后都绿＝用例空转（#194）。
    /// 这件事由 <see cref="RelevanceAndNameLegsDisagreeOnThisFixture"/> 当场核对，不靠注释自证。
    /// </para>
    /// </summary>
    private async Task SeedAsync()
    {
        var repo = new ItemRepository(_factory);
        var items = Enumerable.Range(1, 12).Select(i => new Item
        {
            Type = ItemType.Bookmark,
            Source = "p33",
            SourceId = $"i{i:D2}",
            Title = $"alpha{i:D2} " + string.Join(' ', Enumerable.Repeat(Keyword, i)),
            UpdatedAt = BaseTime - i,
            StarsCount = i,
        }).ToArray();
        await repo.UpsertAsync(items, CancellationToken.None);
    }

    private async Task<List<string>> TitlesAsync(string? sort, int maxResults)
    {
        var repo = new ItemRepository(_factory);
        var result = await repo.SearchAsync(Keyword,
            new SearchFilter { Sort = sort, MaxResults = maxResults }, CancellationToken.None);
        return result.Items.Select(i => i.Title.Split(' ')[0]).ToList();
    }

    [Fact]
    public async Task NameSortReturnsTheAlphabeticallyFirstRowsNotTheBestRanked()
    {
        await SeedAsync();

        var titles = await TitlesAsync("name", 5);

        // 修前：这五条是相关度前排的 alpha12/11/10/09/08（名字最靠前的那五条根本没进结果集）。
        Assert.Equal(new[] { "alpha01", "alpha02", "alpha03", "alpha04", "alpha05" }, titles);
    }

    [Fact]
    public async Task RecentSortReturnsNewestFirstEvenWhenRelevanceDisagrees()
    {
        await SeedAsync();

        var titles = await TitlesAsync("recent", 5);

        Assert.Equal(new[] { "alpha01", "alpha02", "alpha03", "alpha04", "alpha05" }, titles);
    }

    [Fact]
    public async Task StarsSortHonoursItsOwnOrderAfterFiltering()
    {
        await SeedAsync();

        // StarsCount 与词频同向（alpha12 星最多）⇒ 这条腿与相关度巧合同序；
        // 它守的是"排序本身跑通了、且没被里层截断切成残缺的 5 条"，区分力来自上一条与下一条。
        var titles = await TitlesAsync("stars", 12);

        Assert.Equal(12, titles.Count);                       // 修前这里最多只有 5 条（名额在里层就被相关度吃掉了）
        Assert.Equal("alpha12", titles[0]);
        Assert.Equal("alpha01", titles[^1]);
    }

    [Fact]
    public async Task NonDefaultSortPaginatesAfterOrderingWhenTheWindowWidens()
    {
        await SeedAsync();

        // SearchService 的分页是把窗口加宽到"本页末"（P-42 路线 D），所以第二页要靠更大的 MaxResults 取到；
        // 截断在排序之后 ⇒ 前 8 条就是字母序的前 8 条，翻到第 8 条也不会跳过中间那些。
        var titles = await TitlesAsync("name", 8);

        Assert.Equal(8, titles.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"alpha{i:D2}"), titles);
    }

    [Fact]
    public async Task RelevanceLegStillShortCircuitsToMaxResults()
    {
        await SeedAsync();

        var titles = await TitlesAsync("relevance", 5);

        // 正向对照：护栏不能反向把 P-49 换来的短路也关掉——相关度那条腿仍然只取前 5，
        // 而且取的是词频最高的那 5 条（与字母序前 5 条互不相交，见夹具说明）。
        Assert.Equal(5, titles.Count);
        Assert.DoesNotContain("alpha01", titles);
        Assert.Contains("alpha12", titles);
    }

    [Fact]
    public async Task NullAndEmptySortAreTreatedAsRelevanceNotAsAFifthLeg()
    {
        await SeedAsync();

        // 老调用方与测试桩不填 Sort（null），UI 也有传空串的路径：这两种必须走相关度那条腿，
        // 否则"没显式选排序"的调用会悄悄换成另一条 SQL（多花一整轮 join+分组，且顺序变味）。
        var byNull = await TitlesAsync(null, 5);
        var byEmpty = await TitlesAsync(string.Empty, 5);
        var byName = await TitlesAsync("relevance", 5);

        Assert.Equal(byName, byNull);
        Assert.Equal(byName, byEmpty);
        Assert.DoesNotContain("alpha01", byNull);
    }

    [Fact]
    public async Task RelevanceAndNameLegsDisagreeOnThisFixture()
    {
        await SeedAsync();

        // 这条不是被测的行为，是**夹具的牙齿**：两条腿拿到的必须是完全不同的一批条目。
        // 哪天夹具（或 bm25 的口径）变成两条腿给出同一批，上面几条就退化成空转——这里先红，提醒重造数据。
        var relevance = await TitlesAsync("relevance", 5);
        var name = await TitlesAsync("name", 5);

        Assert.Empty(relevance.Intersect(name));
        Assert.Equal(5, relevance.Count);
        Assert.Equal(5, name.Count);
    }
}
