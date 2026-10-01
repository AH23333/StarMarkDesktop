#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Data;
using StarMark.Integrations.GitHub;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「没配 Token 的人启动期不该多挂一对 HttpClient」（P-135 剩半边）＋「凭据要推到真正在用的那一颗」（P-140）。
/// <para>
/// 旧写法 <c>GitHubSource</c> 的 DI 构造链里直接 <c>new GitHubClient(options)</c>，而容器里另注册了一颗
/// （<c>App.xaml.cs</c> 的 <c>AddSingleton&lt;GitHubClient&gt;()</c>）——于是<b>同一个进程里有两颗客户端</b>：
/// 设置页保存 Token 后 <c>SyncCredentials()</c> 推的是容器那颗，跑 Star 同步用的却是源自己那颗，
/// 它的 Authorization 头停留在启动那一刻 ⇒ 界面按活的 options 说"已配置"，请求却带着空凭据去，
/// 用户看到的就是"填了 Token 点了同步还是不行"，只能重启（重启在本项目按缺陷算）。
/// </para>
/// <para>所以这里钉两层：行为层（不建 / 只建一次 / 借来的不释放 / 注入的照旧释放）＋接线层（源不许自己 new、注册必须取容器那颗）。</para>
/// </summary>
public sealed class GitHubSourceLazyClientTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_ghlazy_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { /* 临时库清理失败不影响断言 */ }
    }

    private static GitHubOptions Options(string? token = "ghp_test") => new() { Token = token, PageSize = 100 };

    private ItemRepository Repo()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        return new ItemRepository(factory);
    }

    /// <summary>只回一页空列表的假 API（不带 ETag ⇒ 不碰检查点，纯测客户端生命周期）。</summary>
    private sealed class EmptyPageHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    // ==================== 行为层 ====================

    /// <summary>
    /// 没配 Token：构造之后、问完 <c>IsAvailable</c>、甚至<b>跑完一轮同步</b>之后，
    /// 一颗客户端都不该建——"关着也在加载"在这条源上的形状就是"没配也在挂 HttpClient"。
    /// </summary>
    [Fact]
    public async Task NoToken_BuildsNoClient_EvenAfterAWholeSyncRound()
    {
        var options = Options(token: null);
        var builds = 0;
        await using var source = new GitHubSource(options, Repo(), () => { builds++; return new GitHubClient(options); });

        Assert.False(source.IsAvailable);
        Assert.Equal(0, builds);                                   // 光是"问可用性"不许建客户端

        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Empty(items);
        Assert.Equal(0, builds);
    }

    /// <summary>
    /// 可用性读的是<b>配置对象的当前值</b>，不是构造期快照：Token 事后填上，<c>IsAvailable</c> 当场翻真，
    /// 而且这一步仍然不该建客户端（建它的是第一次真发请求）。
    /// </summary>
    [Fact]
    public void IsAvailable_FollowsLiveOptions_WithoutBuildingClient()
    {
        var options = Options(token: null);
        var builds = 0;
        // 这条路上一颗客户端都不会被建出来，也就没有需要 Dispose 的东西；源本身不占资源，不必 using。
        var source = new GitHubSource(options, Repo(), () => { builds++; return new GitHubClient(options); });

        Assert.False(source.IsAvailable);
        options.Token = "ghp_filled_later";

        Assert.True(source.IsAvailable);
        Assert.Equal(0, builds);
    }

    /// <summary>配了 Token：两轮同步也只取一次客户端（工厂背后是容器单例，重复取回同一颗）。</summary>
    [Fact]
    public async Task TokenPresent_BuildsClientExactlyOnce_AcrossTwoRounds()
    {
        var options = Options();
        var repo = Repo();
        var builds = 0;
        await using var source = new GitHubSource(options, repo, () =>
        {
            builds++;
            return new GitHubClient(options, new HttpClient(new EmptyPageHandler()));
        });

        await source.FetchAsync(new SyncContext(), CancellationToken.None);
        await source.FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Equal(1, builds);
    }

    /// <summary>
    /// <b>借来的那颗不许由源释放</b>：DI 那条路上客户端是容器的单例（热榜 Star 写操作与设置页推凭据用的都是它），
    /// 源在关机前把它 Dispose 掉＝把共享实例打死，下一次点 Star 或同步就撞上 ObjectDisposedException。
    /// </summary>
    [Fact]
    public async Task BorrowedClient_SurvivesSourceDisposal()
    {
        var options = Options();
        var handler = new EmptyPageHandler();
        var client = new GitHubClient(options, new HttpClient(handler));
        var builds = 0;
        var source = new GitHubSource(options, Repo(), () => { builds++; return client; });

        await source.FetchAsync(new SyncContext(), CancellationToken.None);      // 这一轮把客户端取出来用掉
        await source.DisposeAsync();

        Assert.False(handler.Disposed);                                          // 源没把它连带 Dispose
        var again = await client.GetAllStarredAsync(CancellationToken.None);     // 共享实例仍可用
        Assert.Empty(again);
        Assert.True(handler.Requests >= 2);

        client.Dispose();
        Assert.True(handler.Disposed);                                           // 但"谁建谁收"仍成立：这颗的 Dispose 打得通
    }

    /// <summary>
    /// 反向对照：走测试缝（注入客户端）时源<b>拥有</b>它，<c>DisposeAsync</c> 照旧连带释放——
    /// 这条既有契约（P-19 那批钉下的）没被这次懒建放宽或删掉。
    /// </summary>
    [Fact]
    public async Task InjectedClient_IsStillDisposedBySource()
    {
        var options = Options();
        var handler = new EmptyPageHandler();
        var client = new GitHubClient(options, new HttpClient(handler));
        await using var source = new GitHubSource(options, Repo(), client);

        await source.FetchAsync(new SyncContext(), CancellationToken.None);
        await source.DisposeAsync();

        Assert.True(handler.Disposed);
    }

    // ==================== 接线层（形状门） ====================

    private static string Code(string file) => SourceGate.Code(SourceGate.ReadRepoFile(file));

    private const string Source = "src/StarMark.Integrations/GitHub/GitHubSource.cs";
    private const string Client = "src/StarMark.Integrations/GitHub/GitHubClient.cs";
    private const string OptionsFile = "src/StarMark.Integrations/GitHub/GitHubOptions.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";

    /// <summary>
    /// 源里<b>一次都不许出现</b> <c>new GitHubClient(</c>：那正是"自己第二颗"的写法，
    /// 放回去 P-140 就复活（而它不会有任何测试变红——只有真机点同步会 401）。
    /// </summary>
    [Fact]
    public void TheSourceNeverNewsItsOwnClient()
        => Assert.DoesNotContain("new GitHubClient(", Code(Source), StringComparison.Ordinal);

    /// <summary>注册必须把<b>容器那颗</b>交给源（工厂里取 <c>GetRequiredService&lt;GitHubClient&gt;</c>），而不是自建。</summary>
    [Fact]
    public void Registration_HandsOverTheSharedClient()
    {
        var segment = SourceGate.Between(Code(App),
            "services.AddSingleton<StarMark.Integrations.GitHub.GitHubClient>();",
            "services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.GitHub.GitHubSource>());");

        Assert.Contains("GetRequiredService<StarMark.Integrations.GitHub.GitHubClient>()", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("new StarMark.Integrations.GitHub.GitHubClient(", segment, StringComparison.Ordinal);
    }

    /// <summary>
    /// "配没配 Token"这条判据<b>只有一个出处</b>（<c>GitHubOptions.IsConfigured</c>）：
    /// 客户端与源的可用性都读它。两处各写一遍，将来一处算空白一处不算，界面与请求就会分岔。
    /// </summary>
    [Fact]
    public void TheConfiguredJudgement_HasExactlyOneBody()
    {
        Assert.Equal(1, SourceGate.Count(Code(OptionsFile), "public bool IsConfigured =>"));
        Assert.Contains("!string.IsNullOrEmpty(Token)", Code(OptionsFile), StringComparison.Ordinal);

        Assert.Equal(1, SourceGate.Count(Code(Client), "public bool IsConfigured =>"));
        Assert.Contains("=> _options.IsConfigured", Code(Client), StringComparison.Ordinal);   // 客户端只做转发
        Assert.DoesNotContain("!string.IsNullOrEmpty(_options.Token)", Code(Client), StringComparison.Ordinal);
    }
}
