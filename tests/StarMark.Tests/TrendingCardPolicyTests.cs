#nullable enable
using System;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Trending;

namespace StarMark.Tests;

/// <summary>
/// 批次 KE：热榜候选行「复用了卡片，但动作口径必须换掉」这一层的契约护栏。
/// <para>
/// 这批判据全部留在 Abstractions 而不是写在 XAML / code-behind 里，原因是 <c>StarMark.Tests</c> 引不到
/// StarMark.UI：判据一旦只存在于 XAML 的 <c>ConverterParameter=Invert</c> 上，"漏关一个按钮"就没有任何东西会红。
/// 而漏关的代价不对称——对一条 Id=0 的候选点「置顶」会经"按需登记"把它写进主库，直接违背用户裁决的
/// "热榜默认不入库"；反过来 Star / 书签 不出现则是"点了没反应"（P-54）。
/// </para>
/// </summary>
public sealed class TrendingCardPolicyTests
{
    private const string RepoFull = "octocat/Hello-World";

    private static TrendingRepo Repo() => new(
        RepoFull, "https://github.com/" + RepoFull, "GitHub repository", "C#", 12345, 230);

    // ===== 这一行是什么 =====

    [Theory]
    [InlineData(ItemSources.Trending, true)]
    [InlineData(ItemSources.Local, false)]
    [InlineData(ItemSources.GitHub, false)]
    [InlineData(ItemSources.Chrome, false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrendingRepo_RecognisesOnlyTheTrendingSource(string? source, bool expected)
        => Assert.Equal(expected, ItemCardPolicy.IsTrendingRepo(source));

    /// <summary>大小写不放过：source 是写进库的字符串，判定按 Ordinal，改一个字面量就得同步改判定。</summary>
    [Fact]
    public void IsTrendingRepo_IsCaseSensitive_BecauseSourcesArePersistedLiterals()
        => Assert.False(ItemCardPolicy.IsTrendingRepo("Trending"));

    // ===== 哪个动作该出现（真值表）=====

    [Theory]
    [InlineData(false, false, true)]    // 普通库条目：库管理动作照常
    [InlineData(true, false, false)]    // 快捷启动合成入口：没有对应库行
    [InlineData(false, true, false)]    // 热榜候选：有库行会被"按需登记"造出来 ⇒ 必须关
    [InlineData(true, true, false)]
    public void LibraryActions_ShowOnlyForRealLibraryRows(bool launcher, bool trending, bool expected)
        => Assert.Equal(expected, ItemCardPolicy.ShowsLibraryActions(launcher, trending));

    [Fact]
    public void TrendingActions_AndLibraryActions_NeverAppearOnTheSameRow()
    {
        Assert.True(ItemCardPolicy.ShowsTrendingActions(true));
        Assert.False(ItemCardPolicy.ShowsTrendingActions(false));
        foreach (var launcher in new[] { false, true })
            foreach (var trending in new[] { false, true })
                Assert.False(ItemCardPolicy.ShowsTrendingActions(trending)
                            && ItemCardPolicy.ShowsLibraryActions(launcher, trending));
    }

    // ===== Star / 书签 的显示值 =====

    [Fact]
    public void StarDisplay_FlipsSoAlreadyStarredReadsAsClickAgainUnstars()
    {
        Assert.Equal("☆", ItemCardPolicy.StarGlyph(false));
        Assert.Equal("★", ItemCardPolicy.StarGlyph(true));
        Assert.Equal("Star", ItemCardPolicy.StarLabel(false));
        Assert.Equal("已 Star", ItemCardPolicy.StarLabel(true));
    }

    [Fact]
    public void StarTip_NeverClaimsToBeRealtime_AndSaysWhenTokenIsMissing()
    {
        // 判定来自本机已同步列表，不是 HEAD 探测：这句不写，用户会以为界面是实时的、刚在网页 star 的也该显示
        Assert.Contains("本机已同步的 Star 列表", ItemCardPolicy.StarTip(true, true));
        Assert.Contains("本机已同步的 Star 列表", ItemCardPolicy.StarTip(false, false));
        // 没配 Token 时必须当场说清"点了会失败"，而不是让人按下去才知道
        Assert.Contains("未配置 Token", ItemCardPolicy.StarTip(false, false));
        Assert.DoesNotContain("未配置 Token", ItemCardPolicy.StarTip(false, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CollectLabels_AlwaysSayTheLocalBookmarkDoesNotTouchGitHub(bool collected)
    {
        Assert.Equal(collected ? "移出收藏" : "收进收藏", ItemCardPolicy.CollectLabel(collected));
        Assert.Contains("不会动 GitHub 的 Star 状态", ItemCardPolicy.CollectTip(collected));
    }

    // ===== 星数文案：不知道 ≠ 没涨 =====

    [Fact]
    public void PeriodGain_IsOmittedWhenUnknown_NeverRenderedAsZero()
    {
        Assert.Null(TrendingStarsText.PeriodGain(null, "本周"));
        Assert.Equal("＋230 本周", TrendingStarsText.PeriodGain(230, "本周"));
        // 0 是"知道、确实没涨"，与 null（兜底来源给不出这个字段）是两件事
        Assert.Equal("＋0 本周", TrendingStarsText.PeriodGain(0, "本周"));
    }

    [Fact]
    public void PeriodGain_UsesInvariantThousandsSeparator()
        => Assert.Equal("＋1,234 今日", TrendingStarsText.PeriodGain(1234, "今日"));

    [Theory]
    [InlineData(null, "★ 0")]
    [InlineData(0L, "★ 0")]
    [InlineData(-5L, "★ 0")]          // 异常输入显示 0，不显示空白（空白看着像坏了）
    [InlineData(12345L, "★ 12,345")]
    public void Total_IsNeverBlank(long? stars, string expected)
        => Assert.Equal(expected, TrendingStarsText.Total(stars));

    // ===== 候选行形状 =====

    [Fact]
    public void RowDraft_IsAnUnpersistedRow_ThePolicyRecognises()
    {
        var item = TrendingRowDraft.ForRow(Repo(), TrendingPeriod.Weekly, TrendingSource.TrendingHtml);

        Assert.Equal(0L, item.Id);                                  // Id=0 ⇒ 卡片上"按 Id 动作"全部不适用
        Assert.Equal(ItemSources.Trending, item.Source);
        Assert.Equal("trending:" + RepoFull, item.SourceId);
        Assert.Equal(RepoFull, item.Title);
        Assert.Equal("https://github.com/" + RepoFull, item.Uri);
        Assert.Equal(12345L, item.StarsCount);
        // 这条断言把"形状"和"判据"钉在一起：改了 Source 常量而忘了改判定 ⇒ 这里红
        Assert.True(ItemCardPolicy.IsTrendingRepo(item.Source));
        Assert.False(ItemCardPolicy.ShowsLibraryActions(false, ItemCardPolicy.IsTrendingRepo(item.Source)));
    }

    [Fact]
    public void RowDraft_SubtitleCarriesLanguageStarsAndGain()
        => Assert.Equal("C# · ★ 12,345 · ＋230 本周",
            TrendingRowDraft.ForRow(Repo(), TrendingPeriod.Weekly, TrendingSource.TrendingHtml).Subtitle);

    /// <summary>Search API 给不出"本期新增"，所以那一腿的结果不许出现 ＋N（编数据比缺数据糟）。</summary>
    [Fact]
    public void RowDraft_DropsTheGainSegment_OnTheSearchApiLeg_EvenWhenTheRepoCarriesOne()
    {
        var item = TrendingRowDraft.ForRow(Repo(), TrendingPeriod.Weekly, TrendingSource.SearchApi);

        Assert.Equal("C# · ★ 12,345", item.Subtitle);
        Assert.DoesNotContain("＋", item.Subtitle);
    }

    [Fact]
    public void RowDraft_SkipsMissingLanguageAndBlankDescription()
    {
        var item = TrendingRowDraft.ForRow(
            new TrendingRepo("a/b", "https://github.com/a/b", "   ", null, 0, null),
            TrendingPeriod.Daily, TrendingSource.TrendingHtml);

        Assert.Equal("★ 0", item.Subtitle);
        Assert.Null(item.Description);          // 空描述若给 "" 会让卡片多出一行空白
        Assert.Equal(0L, item.StarsCount);
    }

    // ===== 已 Star / 已收藏 的判定 =====

    private static Item StarItem(string full) => new()
    {
        Type = ItemType.GitHubStar,
        Source = ItemSources.GitHub,
        SourceId = full,
        Title = full,
        Uri = "https://github.com/" + full,
    };

    private static Item BookmarkItem(string sourceId, string source = ItemSources.Local) => new()
    {
        Type = ItemType.Bookmark,
        Source = source,
        SourceId = sourceId,
        Title = sourceId,
        Uri = "https://github.com/x/y",
    };

    [Fact]
    public void StarState_UsesSyncedListThenSessionOverride_CaseInsensitively()
    {
        var s = new TrendingStarState();
        s.ReloadFrom(new[] { StarItem("Owner/Repo") });

        Assert.True(s.IsStarred("owner/repo"));                 // GitHub 的 owner/repo 大小写不敏感
        Assert.True(s.IsStarred("  owner/repo/ "));             // 顺手清空白与尾斜杠
        Assert.False(s.IsStarred("other/repo"));
        Assert.False(s.IsStarred(null));

        s.Record("other/repo", true);
        Assert.True(s.IsStarred("Other/Repo"));

        // 会话内的取消 Star 也必须立刻可见：只看同步列表的话，刷新一下又显示"已 Star"＝点了没生效
        s.Record("Owner/Repo", starred: false);
        Assert.False(s.IsStarred("owner/repo"));
    }

    [Fact]
    public void StarState_ReloadDropsSessionOverrides_BecauseTheNewListIsAuthoritative()
    {
        var s = new TrendingStarState();
        Assert.False(s.HasPendingChanges);
        s.Record("a/b", true);
        Assert.True(s.HasPendingChanges);

        // 留着改动＝永久掩盖远端：下次同步若用户已在网页上取消 star，界面还得说"已 Star"
        s.ReloadFrom(Array.Empty<Item>());
        Assert.False(s.HasPendingChanges);
        Assert.False(s.IsStarred("a/b"));
    }

    [Fact]
    public void StarState_IgnoresUnusableIds_InsteadOfRememberingGarbage()
    {
        var s = new TrendingStarState();
        s.Record("../../etc/passwd", true);
        s.Record(null, true);
        s.Record("only-one-segment", true);

        Assert.False(s.HasPendingChanges);
        Assert.False(s.IsStarred("../../etc/passwd"));
    }

    [Fact]
    public void CollectIndex_RecognisesOnlyTrendingBookmarks()
    {
        var set = TrendingCollectIndex.FromItems(new[]
        {
            BookmarkItem(TrendingItemDraft.BookmarkSourceId("a/b")),
            BookmarkItem("C:/Windows/notepad.exe"),                       // 同一 source 的其它书签
            BookmarkItem(TrendingItemDraft.BookmarkSourceId("c/d"), ItemSources.Chrome),  // 前缀对但来源不是本机
        });

        Assert.True(TrendingCollectIndex.IsCollected(set, "A/B"));
        Assert.False(TrendingCollectIndex.IsCollected(set, "c/d"));
        Assert.False(TrendingCollectIndex.IsCollected(set, "C:/Windows/notepad.exe"));
        Assert.False(TrendingCollectIndex.IsCollected(set, null));
        Assert.False(TrendingCollectIndex.IsCollected(null, "a/b"));
        Assert.Empty(TrendingCollectIndex.FromItems(null));
    }

    /// <summary>
    /// 「收藏」与「判定已收藏」必须用同一个键：两处各拼一份前缀的话，就会出现
    /// 点过一次书签、再点又插一条（P-65 那一类"按错键分派"的形状）。
    /// </summary>
    [Fact]
    public void BookmarkDraft_AndCollectIndex_RoundTripOnTheSameKey()
    {
        var bookmark = TrendingItemDraft.ForBookmark(Repo(), 1_700_000_000);
        var set = TrendingCollectIndex.FromItems(new[] { bookmark });

        Assert.StartsWith(TrendingItemDraft.BookmarkSourcePrefix, bookmark.SourceId, StringComparison.Ordinal);
        Assert.Equal(ItemSources.Local, bookmark.Source);
        Assert.True(TrendingCollectIndex.IsCollected(set, RepoFull));
        // 候选行本身（未入库）不算已收藏——否则按钮一上来就显示「移出收藏」
        Assert.False(TrendingCollectIndex.IsCollected(set,
            TrendingRowDraft.ForRow(Repo(), TrendingPeriod.Weekly, TrendingSource.TrendingHtml).SourceId));
    }

    /// <summary>
    /// 两处集合的<b>不对称</b>是有意为之，本条把它钉住：收藏判定按 <c>source=local</c> 自己收窄，
    /// star 判定则把作用域交给调用方的查询（<c>GetBySourceAsync(github, GitHubStar)</c>）——
    /// 所以候选行绝不能整批丢给 <see cref="TrendingStarIndex"/>，它带着合法的 GitHub URL，会被认成"已 Star"。
    /// </summary>
    [Fact]
    public void StarIndex_ScopeIsTheCallers_Query_NotTheProjections()
    {
        var row = TrendingRowDraft.ForRow(Repo(), TrendingPeriod.Weekly, TrendingSource.TrendingHtml);

        Assert.Empty(TrendingCollectIndex.FromItems(new[] { row }));
        Assert.True(TrendingStarIndex.IsStarred(TrendingStarIndex.FromItems(new[] { row }), RepoFull));
        // 真正喂给它的是已同步的 star 条目；这一批里没有候选行
        Assert.True(TrendingStarIndex.IsStarred(TrendingStarIndex.FromItems(new[] { StarItem(RepoFull) }), RepoFull));
        Assert.False(TrendingStarIndex.IsStarred(
            TrendingStarIndex.FromItems(new[] { BookmarkItem(TrendingItemDraft.BookmarkSourceId(RepoFull)) }), RepoFull),
            "本机书签条目不是 star：混进 star 集合会把「收进收藏」显示成「已 Star」");
    }
}
