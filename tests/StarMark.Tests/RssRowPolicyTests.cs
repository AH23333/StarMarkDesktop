#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「RSS 候选这一行是什么、能做什么、收藏后落在哪」的判据（批次 RB）。
/// <para>为什么值得单独钉：这一行的身份决定了一整组动作的有无——判据写歪一处的后果不是"少个按钮"，
/// 而是<b>顺手一个置顶就把默认不入库的候选灌进了 items</b>，或者提示里指了一个根本不存在的文件夹。</para>
/// </summary>
public sealed class RssRowPolicyTests
{
    private static RssEntry Entry(DateTimeOffset? published = null, string sourceName = "开源中国") => new(
        "某篇文章", " https://a.example.com/post/1 ", published, "一段摘要", "作者", 7, sourceName);

    // ────────── 候选行的形状 ──────────

    [Fact]
    public void CandidateRowIsAnUnpersistedRowThePolicyRecognises()
    {
        var row = RssRowDraft.ForCandidate(Entry());

        Assert.Equal(0, row.Id);                                       // 不入库：Id=0 是所有"按需登记"动作的开关
        Assert.Equal(ItemSources.Rss, row.Source);
        Assert.True(ItemCardPolicy.IsRssCandidate(row.Source));
        Assert.False(ItemCardPolicy.IsRssCandidate(ItemSources.Trending));   // 两族各判各的，别互相认
        Assert.Equal(ItemType.Bookmark, row.Type);
        Assert.Equal("https://a.example.com/post/1", row.Uri);          // 地址去空白：带空格会多出一行一样的收藏
        // 与"收藏之后那一行"同一个身份函数，否则"已收藏"回显查不到
        Assert.Equal(RssEntryIdentity.BookmarkSourceId(Entry().Link), row.SourceId);
    }

    [Fact]
    public void MissingPublishTimeSaysSoInsteadOfLookingFresh()
    {
        Assert.Equal("源没给时间", RssRowDraft.PublishedText(Entry()));
        var text = RssRowDraft.PublishedText(Entry(new DateTimeOffset(2026, 9, 24, 3, 5, 0, TimeSpan.Zero)));
        // 只钉形状（不钉绝对值：它按本地时区显示）——"1970-01-01" 那种缺值不能冒充真实时间
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", text);
    }

    // ────────── 动作集：这一行只能是主窗右键的一个子集 ──────────

    [Fact]
    public void CandidateRowExposesNoLibraryWritingActionsAtAll()
    {
        Assert.False(ItemCardPolicy.ShowsLibraryActions(isLauncherMode: false, isTrendingRepo: false, isRssCandidate: true));
        Assert.False(ItemCardPolicy.CanSendToLauncher(false, false, hasUri: true, isRssCandidate: true));
        // 库动作全关之外，预览也关：源条目要先抓正文才能预览，用户明确"暂不提供预览"
        Assert.False(ItemCardPolicy.ShowsPreview(isRssCandidate: true));
        Assert.True(ItemCardPolicy.ShowsPreview(isRssCandidate: false));
        // 普通库行两者都开（关掉就是"主窗少了一半菜单"那种回归）
        Assert.True(ItemCardPolicy.ShowsLibraryActions(false, false, false));
        Assert.True(ItemCardPolicy.CanSendToLauncher(false, false, hasUri: true, isRssCandidate: false));
    }

    [Fact]
    public void CollectAffordanceNamesTheFolderItWillCreate()
    {
        var tip = ItemCardPolicy.RssCollectTip(false, RssFolders.DisplayPath("开源中国"));
        Assert.Contains("RSS订阅 / 开源中国", tip);
        Assert.Equal("收藏到文件夹", ItemCardPolicy.RssCollectLabel(false));
        // 已收藏后不再给"移除"（P-88）：文案要说清去哪儿移除，而不是留一颗含义不明的灰按钮
        var done = ItemCardPolicy.RssCollectTip(true, RssFolders.DisplayPath("开源中国"));
        Assert.Equal("已收藏", ItemCardPolicy.RssCollectLabel(true));
        Assert.Contains("已经在", done);
        Assert.Contains("资料库", done);
    }

    // ────────── 收藏落点：文件夹 ──────────

    /// <summary>候选行<b>自带</b>落点：tooltip 上那个文件夹与收藏时真正写进去的那一层，
    /// 必须是同一份数据算出来的两处读法。否则"提示说 A，实际存进 B"这种错没有任何东西能抓到。</summary>
    [Fact]
    public void CandidateRowCarriesItsOwnLandingFolder()
    {
        var row = RssRowDraft.ForCandidate(Entry(sourceName: "开源中国"));

        Assert.Equal(new[] { "RSS订阅", "开源中国" }, FolderPathUtil.BookmarkSegments(row));
        Assert.Contains("RSS订阅 / 开源中国", ItemCardPolicy.RssCollectTip(false, RssFolders.DisplayPath("开源中国")));
    }

    /// <summary>收藏写库的那一行：与候选行只差"进库"这件事（来源换成本地、补时间戳），
    /// 内容一个字段都不许多写或漏写——两份各写字段的形状，正是热榜那边出现过的"卡片一个标题、库里另一个"。</summary>
    [Fact]
    public void CollectedRowIsTheCandidateRowTurnedIntoALocalBookmark()
    {
        var candidate = RssRowDraft.ForCandidate(Entry(sourceName: "开源中国"));
        var stored = RssRowDraft.ForCollect(candidate, 1_700_000_000);

        Assert.Equal(ItemSources.Local, stored.Source);            // 进库后是普通书签，不该再标着 rss
        Assert.False(ItemCardPolicy.IsRssCandidate(stored.Source));
        Assert.Equal(0, stored.Id);                                // 由仓储按 (source, source_id) 判定新增还是合并
        Assert.Equal(candidate.SourceId, stored.SourceId);
        Assert.Equal(candidate.Uri, stored.Uri);
        Assert.Equal(candidate.Title, stored.Title);
        Assert.Equal(candidate.Description, stored.Description);
        Assert.Equal(candidate.ExtraJson, stored.ExtraJson);       // 落点随行带过来，不在收藏那一步另算
        Assert.Equal(1_700_000_000, stored.CreatedAt);
        Assert.Equal(1_700_000_000, stored.UpdatedAt);
        Assert.Equal(new[] { "RSS订阅", "开源中国" }, FolderPathUtil.BookmarkSegments(stored));
    }

    [Fact]
    public void BothCandidateFamiliesGetTheCollectSlotButWordedPerFamily()
    {
        Assert.True(ItemCardPolicy.ShowsCollect(isTrendingRepo: true, isRssCandidate: false));
        Assert.True(ItemCardPolicy.ShowsCollect(false, true));
        Assert.False(ItemCardPolicy.ShowsCollect(false, false));   // 库里的行没有"再收藏一次"

        // 同一个按钮位，两类行两套文案：混用就会出现"RSS 行上写着 GitHub 的落点"
        Assert.Equal("收藏到文件夹", ItemCardPolicy.CollectLabelFor(true, false));
        Assert.Equal("收进收藏", ItemCardPolicy.CollectLabelFor(false, false));
        Assert.Contains("RSS订阅", ItemCardPolicy.CollectTipFor(true, false, "RSS订阅 / 开源中国"));
        Assert.Contains("GitHub", ItemCardPolicy.CollectTipFor(false, false, "RSS订阅 / 开源中国"));
    }

    /// <summary>候选行没有"本机更新时间"：<c>UpdatedAt</c> 缺省 0，不关掉就会在卡片右上角印出 1970-01-01。</summary>
    [Fact]
    public void CandidateRowsShowNoLocalUpdateTimeLine()
    {
        Assert.False(ItemCardPolicy.ShowsTimeAndStarsLines(isTrendingRepo: false, isRssCandidate: true));
        Assert.True(ItemCardPolicy.ShowsTimeAndStarsLines(false, false));
    }

    [Fact]
    public void CollectingGoesIntoOneFolderPerSourceUnderTheRssRoot()
    {
        Assert.Equal(new[] { "RSS订阅", "开源中国" }, RssFolders.PathFor("  开源中国  "));
        Assert.Equal("RSS订阅 / 开源中国", RssFolders.DisplayPath("开源中国"));
    }

    /// <summary>源名里的 "/" 必须整段留着：<see cref="FolderPathUtil"/> 里数组元素＝一级，
    /// 拆成两级就等于把一个源劈成两个文件夹。</summary>
    [Fact]
    public void SlashInsideASourceNameStaysOneLevel()
        => Assert.Equal(new[] { "RSS订阅", "A/B 技术" }, RssFolders.PathFor("A/B 技术"));

    [Fact]
    public void BlankSourceNameDegradesToTheRootFolderInsteadOfInventingAName()
    {
        Assert.Equal(new[] { "RSS订阅" }, RssFolders.PathFor(null));
        Assert.Equal(new[] { "RSS订阅" }, RssFolders.PathFor("   "));
    }

    /// <summary>写侧与读侧必须说的是同一件事：按这条路径存进去的书签，文件夹树要真的分成两级。</summary>
    [Fact]
    public void TheFolderWrittenIsTheFolderTheTreeReadsBack()
    {
        var meta = new BookmarkMeta { FolderPaths = RssFolders.PathFor("开源中国").ToList() };
        var item = new Item { Type = ItemType.Bookmark, Source = ItemSources.Local, Title = "t" };
        item.ExtraJson = JsonSerializer.Serialize(meta);

        Assert.Equal(new[] { "RSS订阅", "开源中国" }, FolderPathUtil.BookmarkSegments(item));
        Assert.Equal(new[] { "RSS订阅", "开源中国" }, FolderPathUtil.GetSegments(item));
        // 没写 ExtraJson 的书签仍落兜底，不能因为 RSS 这一栏而变
        Assert.Equal(new[] { FolderPathUtil.OtherBookmarkGroup },
            FolderPathUtil.BookmarkSegments(new Item { Type = ItemType.Bookmark, Title = "t" }));
    }

    // ────────── 逐源状态：五种"没有内容"要分得开 ──────────

    private static RssSourceOutcome Outcome(bool enabled = true, string? error = null,
        bool notModified = false, bool stopped = false, int entries = 0)
        => new(new RssSourceConfig(1, "开源中国", "https://a.example.com/feed", enabled),
            Enumerable.Range(0, entries).Select(n => Entry()).ToList(), error, notModified, stopped);

    [Fact]
    public void EveryFlavourOfNothingToShowHasItsOwnSentence()
    {
        var texts = new[]
        {
            RssSourceStatus.Describe(null),
            RssSourceStatus.Describe(Outcome(stopped: true)),
            RssSourceStatus.Describe(Outcome(enabled: false)),
            RssSourceStatus.Describe(Outcome(notModified: true)),
            RssSourceStatus.Describe(Outcome(error: "对方返回 503")),
            RssSourceStatus.Describe(Outcome()),
            RssSourceStatus.Describe(Outcome(entries: 3)),
        };
        Assert.Equal(texts.Length, texts.Distinct().Count());        // 分不开＝用户不知道该改地址还是查网络
        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Contains("503", RssSourceStatus.Describe(Outcome(error: "对方返回 503")));
        Assert.Contains("3", RssSourceStatus.Describe(Outcome(entries: 3)));
    }

    [Fact]
    public void ASkippedSourceIsReportedAsSkippedNotAsBroken()
    {
        var text = RssSourceStatus.Describe(Outcome(stopped: true));
        Assert.DoesNotContain("失败", text);
        Assert.Contains("停止", text);
        // 停用的行不能显示成"通了但没有条目"：那会让人以为服务出问题了
        Assert.DoesNotContain("没有条目", RssSourceStatus.Describe(Outcome(enabled: false)));
    }
}
