#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Feed;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 来源列表进出设置档时的规范化。<b>这一栏坏掉的后果是"用户加的源不见了"</b>，
/// 而它读的是磁盘上一份用户可以手改的 JSON，所以每一条丢弃/改写都要能被指名。
/// </summary>
public sealed class RssSourceListTests
{
    private static RssSourceConfig Src(int id, string url, string name = "", bool enabled = true)
        => new(id, name, url, enabled);

    [Fact]
    public void NullAndEmptyYieldEmpty()
    {
        Assert.Empty(RssSourceList.Normalize(null));
        Assert.Empty(RssSourceList.Normalize(Array.Empty<RssSourceConfig>()));
    }

    [Fact]
    public void GoodRowsSurviveUntouchedIncludingDisabledAndOrder()
    {
        var input = new[] { Src(3, "https://a/feed", "A"), Src(1, "https://b/feed", "B", enabled: false) };
        var result = RssSourceList.Normalize(input);

        Assert.Equal(2, result.Count);
        // 顺序原样：用户自己排的次序不该被清洗打乱；关掉的源也要留着（那是"暂时不想看"）
        Assert.Equal(3, result[0].Id);
        Assert.Equal("https://a/feed", result[0].Url);
        Assert.Equal(1, result[1].Id);
        Assert.False(result[1].Enabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://a/feed")]
    [InlineData("not a url")]
    public void UnusableAddressesAreDropped(string url)
        => Assert.Empty(RssSourceList.Normalize(new[] { Src(1, url, "X") }));

    [Fact]
    public void DuplicateUrlKeepsFirstOnly()
    {
        var result = RssSourceList.Normalize(new[]
        {
            Src(1, "https://a/feed", "第一个"),
            Src(2, " https://A/feed ", "主机名大小写与首尾空白不同也算同址"),
            Src(3, "https://b/feed", "第二个"),
        });

        // 两行同址会白跑一次网络，而且同一批条目在候选列表里出现两遍
        Assert.Equal(new[] { "第一个", "第二个" }, result.Select(r => r.Name));
    }

    [Fact]
    public void DifferentPathOrQueryIsNotTreatedAsDuplicate()
    {
        // 只按整串地址比对，不走 UriNormalizer：那边为条目去重会剥追踪参数与末尾斜杠，
        // 而订阅地址的 query 常常就是"要哪个 feed"的参数（?feed=rss2 与 ?feed=atom 是两个不同的源）。
        var result = RssSourceList.Normalize(new[]
        {
            Src(1, "https://a/feed?feed=rss2", "RSS"),
            Src(2, "https://a/feed?feed=atom", "Atom"),
            Src(3, "https://a/feed/", "带斜杠"),
        });

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void CollidingIdsGetRenumbered()
    {
        var result = RssSourceList.Normalize(new[]
        {
            Src(7, "https://a/feed", "A"),
            Src(7, "https://b/feed", "B"),
            Src(7, "https://c/feed", "C"),
        });

        // 抓取结果与校验符都按 id 对号；两行同 id 会把状态显示到错误的源那一行，界面上完全看不出来
        Assert.Equal(3, result.Select(r => r.Id).Distinct().Count());
        Assert.DoesNotContain(7, result.Skip(1).Select(r => r.Id));
        Assert.Equal("A", result[0].Name);
        Assert.Equal(7, result[0].Id);
    }

    [Fact]
    public void NonPositiveIdsGetReplacedWithoutClashing()
    {
        var result = RssSourceList.Normalize(new[]
        {
            Src(0, "https://a/feed", "A"),
            Src(-5, "https://b/feed", "B"),
            Src(1, "https://c/feed", "C"),
        });

        Assert.All(result, r => Assert.True(r.Id > 0));
        Assert.Equal(3, result.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void BlankNameFallsBackToHostWithoutWww()
    {
        var result = RssSourceList.Normalize(new[] { Src(1, "https://www.example.com/rss", "") });

        // 无名行在列表里既读不出也认不出是哪个源
        Assert.Equal("example.com", result[0].Name);
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmedAway()
    {
        var result = RssSourceList.Normalize(new[] { Src(1, "  https://a/feed  ", "  名字  ") });

        Assert.Equal("https://a/feed", result[0].Url);
        Assert.Equal("名字", result[0].Name);
    }

    [Fact]
    public void NullElementsAreSkippedNotFatal()
    {
        // 反序列化一份被截断的数组真会给进 null：这一栏坏掉不该把设置页整个弄崩
        var input = new List<RssSourceConfig> { Src(1, "https://a/feed", "A") };
        var withNull = new RssSourceConfig?[] { null, input[0], null }.ToList();

        var result = RssSourceList.Normalize(withNull!);
        Assert.Single(result);
    }
}

/// <summary>收藏进库时那条 source_id 的形状。<b>前缀一改，之前收藏进来的行就按 id 列不出也删不掉</b>，
/// 所以这里逐字钉住。</summary>
public sealed class RssEntryIdentityTests
{
    [Fact]
    public void PrefixIsPinnedVerbatim()
        => Assert.Equal("rss-bookmark:", RssEntryIdentity.BookmarkSourcePrefix);

    [Fact]
    public void IdIsPrefixPlusTrimmedLink()
    {
        Assert.Equal("rss-bookmark:https://a/x", RssEntryIdentity.BookmarkSourceId("  https://a/x  "));
        Assert.Equal("rss-bookmark:", RssEntryIdentity.BookmarkSourceId(null!));
    }

    [Fact]
    public void DoesNotCollideWithTrendingBookmarkPrefix()
        => Assert.NotEqual(TrendingItemDraft.BookmarkSourcePrefix, RssEntryIdentity.BookmarkSourcePrefix);
}
