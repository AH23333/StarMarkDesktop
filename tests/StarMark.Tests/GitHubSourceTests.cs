#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.GitHub;

namespace StarMark.Tests;

/// <summary>
/// GitHubSource.MapToItem 映射层契约钉测（补齐批次 AI 对书签映射层的同类覆盖，此前 GitHub 映射零单测）。
/// MapToItem 为 internal（StarMark.Tests 已 InternalsVisibleTo），纯逻辑不触网。
/// </summary>
public sealed class GitHubSourceTests
{
    private static GitHubStarApiModel Repo(
        long id = 4242,
        string fullName = "octocat/Hello-World",
        string? language = "C#",
        long stars = 1234,
        string pushedAt = "2023-05-17T09:00:00Z",
        string createdAt = "2011-01-26T19:01:12Z",
        string? updatedAt = "2024-02-01T10:00:00Z",
        string? starredAtRaw = null,
        string? description = "A description",
        List<string>? topics = null,
        string? ownerLogin = "octocat") => new()
    {
        Id = id,
        FullName = fullName,
        Language = language,
        StargazersCount = stars,
        PushedAt = pushedAt,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
        StarredAtRaw = starredAtRaw,
        Description = description,
        Topics = topics ?? new(),
        HtmlUrl = "https://github.com/" + fullName,
        Owner = ownerLogin is null ? null : new GitHubOwnerApiModel { Login = ownerLogin },
    };

    private const long Now = 1_700_000_000;

    [Fact]
    public void MapToItem_SourceIdIsRepoPrefixedNumericId()
    {
        // 去重键 = "repo-" + repo.Id（GitHub 全局唯一且稳定的仓库 id），与 URL 无关。
        var item = GitHubSource.MapToItem(Repo(id: 999), Now);
        Assert.Equal("repo-999", item.SourceId);
        Assert.Equal(ItemSources.GitHub, item.Source);
        Assert.Equal(ItemType.GitHubStar, item.Type);
    }

    [Theory]
    [InlineData("C#", "Hello-World · C#")]
    [InlineData(null, "Hello-World")]
    [InlineData("", "Hello-World")]
    public void MapToItem_TitleAppendsLanguageOnlyWhenPresent(string? language, string expectedTitle)
    {
        var item = GitHubSource.MapToItem(Repo(language: language), Now);
        Assert.Equal(expectedTitle, item.Title);
    }

    [Theory]
    [InlineData("octocat/Hello-World", "Hello-World")]
    [InlineData("org/a/b", "a/b")]        // 取第一个 '/' 之后的全部（含嵌套）
    [InlineData("noSlash", "noSlash")]    // 无 '/' 回落整串
    public void MapToItem_RepoNameIsSegmentAfterFirstSlash(string fullName, string expectedRepo)
    {
        var item = GitHubSource.MapToItem(Repo(fullName: fullName, language: null), Now);
        // ExtraJson 用默认序列化器（PascalCase 属性名，区别于客户端读 API 的 snake_case）。
        Assert.Contains($"\"Repo\":\"{expectedRepo}\"", item.ExtraJson);
    }

    [Fact]
    public void MapToItem_SubtitleIsFullName_AndUriIsHtmlUrl()
    {
        var item = GitHubSource.MapToItem(Repo(fullName: "octocat/Hello-World"), Now);
        Assert.Equal("octocat/Hello-World", item.Subtitle);
        Assert.Equal("https://github.com/octocat/Hello-World", item.Uri);
    }

    [Fact]
    public void MapToItem_TagsCappedAtFirstTenTopics()
    {
        var topics = Enumerable.Range(0, 15).Select(i => "t" + i).ToList();
        var item = GitHubSource.MapToItem(Repo(topics: topics), Now);
        Assert.Equal(10, item.Tags.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => "t" + i), item.Tags);
    }

    [Fact]
    public void MapToItem_TimestampsParsedFromIso()
    {
        var item = GitHubSource.MapToItem(Repo(), Now);
        Assert.Equal(1296068472, item.CreatedAt); // 2011-01-26T19:01:12Z
        Assert.Equal(1706781600, item.UpdatedAt); // 2024-02-01T10:00:00Z
        Assert.Equal(Now, item.SyncedAt);
    }

    [Theory]
    [InlineData("th-TH")]   // 佛历：默认历法年份 +543
    [InlineData("fa-IR")]   // 波斯历
    [InlineData("ar-SA")]   // 希吉来历
    public void MapToItem_IsoTimestamps_AreCultureInvariant(string culture)
    {
        // GitHub 恒返回 ISO 8601 基本格式带 Z 的 UTC 串；映射层绝不能按 CurrentCulture 历法解析，
        // 否则非公历区域下每条 star 的 CreatedAt/UpdatedAt 会被算成错误 epoch，击穿 recent/starred 排序与健康度分桶。
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var item = GitHubSource.MapToItem(Repo(), Now);
            Assert.Equal(1296068472, item.CreatedAt); // 2011-01-26T19:01:12Z——非公历历法下也必须原样
            Assert.Equal(1706781600, item.UpdatedAt); // 2024-02-01T10:00:00Z
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void MapToItem_MissingTimestampsFallBackToNow()
    {
        // createdAt 缺失/非法 → now；updatedAt 缺失 → 退回 pushedAt → 再缺失 → now。
        var item = GitHubSource.MapToItem(Repo(createdAt: "not-a-date", updatedAt: null, pushedAt: ""), Now);
        Assert.Equal(Now, item.CreatedAt);
        Assert.Equal(Now, item.UpdatedAt); // updatedAt null → pushedAt 空(null) → now
    }

    [Fact]
    public void MapToItem_UpdatedAtFallsBackToPushedAtWhenUpdatedAtMissing()
    {
        var item = GitHubSource.MapToItem(Repo(updatedAt: null, pushedAt: "2023-05-17T09:00:00Z"), Now);
        Assert.Equal(1684314000, item.UpdatedAt); // = pushedAt
    }

    [Fact]
    public void MapToItem_StarredAtUsesEnvelopeTruth_WhenPresent()
    {
        // 【P-6A 已落地】star+json 信封的 starred_at＝用户点 Star 的时刻（真值），
        // pushed_at 降为 updated 族的兜底，不再顶替排序键。
        var item = GitHubSource.MapToItem(Repo(starredAtRaw: "2026-02-03T04:05:06Z",
            pushedAt: "2023-05-17T09:00:00Z"), Now);
        Assert.Contains("\"StarredAt\":1770091506", item.ExtraJson); // 2026-02-03T04:05:06Z
    }

    [Fact]
    public void MapToItem_StarredAtFallsBackToNow_WhenEnvelopeMissing()
    {
        // 信封缺/坏 → now（不再退 pushed_at）：兜底层不许把排序键悄悄换回仓库 push 时间。
        var item = GitHubSource.MapToItem(Repo(starredAtRaw: null, pushedAt: "2023-05-17T09:00:00Z"), Now);
        Assert.Contains($"\"StarredAt\":{Now}", item.ExtraJson);
    }

    [Fact]
    public void MapToItem_NullDescriptionBecomesEmpty()
    {
        Assert.Equal(string.Empty, GitHubSource.MapToItem(Repo(description: null), Now).Description);
    }

    [Fact]
    public void MapToItem_NullOwnerFallsBackToEmpty()
    {
        var meta = GitHubSource.MapToItem(Repo(ownerLogin: null), Now);
        // Owner 为空不抛；Subtitle 仍是 fullName，ExtraJson 内 Owner 为 ""
        Assert.Equal("octocat/Hello-World", meta.Subtitle);
        Assert.Contains("\"Owner\":\"\"", meta.ExtraJson);
    }
}
