#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Trending;
using StarMark.Integrations.GitHub;

namespace StarMark.Tests;

/// <summary>
/// 仓库标识解析 / 「已 Star」查询集 / 书签条目草案的契约护栏（全纯函数）。
/// </summary>
public sealed class GitHubRepoIdTests
{
    [Theory]
    [InlineData("octocat/hello-world", "octocat/hello-world")]
    [InlineData("  Octocat/Hello.World  ", "Octocat/Hello.World")]      // 只去空白与结尾斜杠，不改大小写
    [InlineData("octocat/hello/", "octocat/hello")]
    [InlineData("/octocat/hello", "octocat/hello")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("octocat", null)]                                        // 少一段
    [InlineData("octocat/hello/stargazers", null)]                       // 多一段：会打到别的端点
    [InlineData("/hello", null)]
    [InlineData("octocat/", null)]
    [InlineData("a b/c", null)]                                          // 空格
    [InlineData("../../etc/passwd", null)]                               // 路径穿越
    [InlineData("own%2Fer/repo", null)]                                  // 编码后的斜杠
    [InlineData("octocat/hello?x=1", null)]
    [InlineData("octocat/hello#frag", null)]
    public void Normalize_AcceptsOnlyTwoSafeSegments(string? input, string? expected)
        => Assert.Equal(expected, GitHubRepoId.Normalize(input));

    [Theory]
    [InlineData("https://github.com/octocat/hello", "octocat/hello")]
    [InlineData("http://github.com/octocat/hello", "octocat/hello")]
    [InlineData("https://www.github.com/octocat/hello/", "octocat/hello")]
    [InlineData("https://GitHub.com/Octocat/Hello.git", "Octocat/Hello")]   // 克隆地址后缀 .git 也指同一仓库
    [InlineData("https://github.com/octocat", null)]                        // 只有 owner 不是仓库
    [InlineData("https://github.com/octocat/hello/tree/main/src", null)]    // 子路径不是仓库地址
    [InlineData("https://api.github.com/repos/octocat/hello", null)]         // API 地址不当作网页地址解析
    [InlineData("https://notgithub.com/octocat/hello", null)]                // 别的 host 一律不认
    [InlineData("javascript:alert(1)", null)]                                // 外部来的字符串绝不产出一个可打开的目标
    [InlineData("file:///C:/x/y", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void TryFromUri_OnlyRecognisesGithubRepoPages(string? uri, string? expected)
        => Assert.Equal(expected, GitHubRepoId.TryFromUri(uri));

    [Fact]
    public void StarIndex_IsCaseInsensitiveAndSkipsUnusableRows()
    {
        var set = TrendingStarIndex.FromItems(new[]
        {
            new Item { Uri = "https://github.com/Octocat/Hello" },
            new Item { Uri = "https://gitlab.com/foo/bar" },     // 不是 GitHub 的行直接跳过
            new Item { Title = "a/b" },                          // 没有 URI 时退回标题里的 owner/repo
            new Item { Uri = "", Title = "" },
        });

        Assert.True(TrendingStarIndex.IsStarred(set, "octocat/hello"));
        Assert.True(TrendingStarIndex.IsStarred(set, "a/b"));
        Assert.False(TrendingStarIndex.IsStarred(set, "c/d"));
        Assert.False(TrendingStarIndex.IsStarred(set, "not-a-repo"));
        Assert.False(TrendingStarIndex.IsStarred(null, "a/b"));
        Assert.False(TrendingStarIndex.IsStarred(set, null));
    }

    [Fact]
    public void BookmarkDraft_IsLocalBookmarkKeyedByRepo()
    {
        var repo = new TrendingRepo("octocat/hello", "https://github.com/octocat/hello", "  描述  ", "Go", 1234, 5);

        var item = TrendingItemDraft.ForBookmark(repo, 1_800_000_000);

        Assert.Equal(ItemType.Bookmark, item.Type);
        Assert.Equal(ItemSources.Local, item.Source);                   // 本地来源：永不参与来源同步
        Assert.Equal("trending-bookmark:octocat/hello", item.SourceId); // 删除按 (source, source_id)，不按 URI
        Assert.Equal("octocat/hello", item.Title);
        Assert.Equal("https://github.com/octocat/hello", item.Uri);
        Assert.Equal("描述", item.Description);
        Assert.Equal(1234, item.StarsCount);
        Assert.Contains("Go · ★", item.Subtitle);
        Assert.Equal(1_800_000_000, item.CreatedAt);
        Assert.Equal(1_800_000_000, item.UpdatedAt);
    }

    [Fact]
    public void BookmarkDraft_WithoutLanguageOrDescription_StillLooksRight()
    {
        var item = TrendingItemDraft.ForBookmark(
            new TrendingRepo("o/r", "https://github.com/o/r", "", null, 0, null), 7);

        Assert.Equal("★ 0", item.Subtitle);
        Assert.Null(item.Description);
        Assert.Equal(7, item.UpdatedAt);
    }
}

/// <summary>Star / 取消星写操作（假 handler，不触网）：本应用第一个 GitHub 写接口，失败必须有声音。</summary>
public sealed class GitHubStarWriteTests
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<(string Method, string Url, string? Auth)> Calls { get; } = new();
        public Func<string, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content?.ReadAsByteArrayAsync(ct).GetAwaiter().GetResult();
            Assert.NotNull(body);                       // GitHub 的 star 接口要求空正文 + Content-Length: 0
            Assert.Empty(body!);
            Calls.Add((request.Method.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return Task.FromResult(Responder(request.Method.Method));
        }
    }

    private static (GitHubClient client, Recorder rec) Client(string? token = "tok")
    {
        var rec = new Recorder();
        return (new GitHubClient(new GitHubOptions { Token = token }, new HttpClient(rec)), rec);
    }

    private static HttpResponseMessage Code(HttpStatusCode c) => new(c);

    [Fact]
    public async Task Star_IssuesPutAgainstTheRepoPath_WithBearerToken()
    {
        var (client, rec) = Client();

        await client.SetStarredAsync("octocat/hello", starred: true, CancellationToken.None);

        var call = Assert.Single(rec.Calls);
        Assert.Equal("PUT", call.Method);
        Assert.Equal("https://api.github.com/user/starred/octocat/hello", call.Url);
        Assert.Equal("Bearer tok", call.Auth);
    }

    [Fact]
    public async Task Unstar_IssuesDelete()
    {
        var (client, rec) = Client();

        await client.SetStarredAsync("octocat/hello", starred: false, CancellationToken.None);

        Assert.Equal("DELETE", Assert.Single(rec.Calls).Method);
    }

    /// <summary>没配 Token 必须抛：读侧那套"静默返回空"用在按钮上就等于"点了没反应"。</summary>
    [Fact]
    public async Task NoToken_ThrowsWithActionableReason()
    {
        var (client, rec) = Client(token: null);

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.SetStarredAsync("octocat/hello", true, CancellationToken.None));

        Assert.Equal(GitHubErrorKind.Auth, ex.Kind);
        Assert.Contains("未配置 GitHub Token", ex.Message);
        Assert.Empty(rec.Calls);                        // 而且根本不该把请求发出去
    }

    [Theory]
    [InlineData("octocat/hello/stargazers")]
    [InlineData("../../user/admin")]
    [InlineData("just-a-name")]
    [InlineData("")]
    public async Task MalformedRepoName_IsRejectedBeforeAnyRequest(string fullName)
    {
        var (client, rec) = Client();

        await Assert.ThrowsAsync<GitHubApiException>(
            () => client.SetStarredAsync(fullName, true, CancellationToken.None));

        Assert.Empty(rec.Calls);
    }

    /// <summary>取消一个本来就没 star 的仓库：404 ＝ 目标状态已达成，不能报失败。</summary>
    [Fact]
    public async Task Unstar_404CountsAsSuccess()
    {
        var (client, rec) = Client();
        rec.Responder = _ => Code(HttpStatusCode.NotFound);

        await client.SetStarredAsync("octocat/hello", starred: false, CancellationToken.None);

        Assert.Single(rec.Calls);
    }

    [Fact]
    public async Task Star_404IsARealFailure()
    {
        var (client, rec) = Client();
        rec.Responder = _ => Code(HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.SetStarredAsync("ghost/repo", starred: true, CancellationToken.None));

        Assert.Contains("找不到该仓库", ex.Message);
    }

    [Fact]
    public async Task RateLimited_IsClassifiedNotFlattened()
    {
        var (client, rec) = Client();
        rec.Responder = _ => Code((HttpStatusCode)429);

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.SetStarredAsync("octocat/hello", true, CancellationToken.None));

        Assert.Equal(GitHubErrorKind.RateLimit, ex.Kind);
    }
}
