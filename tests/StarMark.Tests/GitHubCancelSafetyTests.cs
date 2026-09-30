#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Data;
using StarMark.Integrations.GitHub;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「同步可中途取消」的安全边界（P-55 第二半）。
/// <para>
/// 加取消入口本身不难，难的是<b>取消不能顺手关掉 304 短路</b>。GitHub 分页里页 1 带回的 ETag
/// 描述的是<b>全量列表</b>：旧实现中途取消时 <c>break</c> 返回已拉到的那几页，调用方
/// <see cref="GitHubSource.FetchAsync"/> 看到的与完整一轮无差别 ⇒ 照样把新 ETag 与 last_synced_at
/// 落进 sync_state。下一次同步首页带 If-None-Match 直接命中 304 ⇒ 被掐掉的那几页<b>永远补不回来</b>。
/// 也就是说：不做这层护栏，"能取消"这个新功能本身就是数据丢失开关。
/// </para>
/// <para>因此钉两件事：① 取消的一律抛，不返回半截；② 未跑完的一轮绝不前移 ETag 检查点（内存与落库都不）。</para>
/// </summary>
public sealed class GitHubCancelSafetyTests : IDisposable
{
    private const string PriorEtag = "\"etag-before\"";
    private const string FreshEtag = "\"etag-full\"";

    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_ghcancel_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { /* 临时库清理失败不影响断言 */ }
    }

    // 假页大小必须与 GitHubClient 的 _perPage（由 PageSize 钳到 [1,100]）同源，
    // 否则"返回数 < perPage"会被立刻判成末页，翻页臂根本没跑到。
    private static GitHubOptions Options(int pageSize = 100) => new() { Token = "ghp_test", PageSize = pageSize };

    /// <summary>
    /// 假 starred API：每页 <paramref name="perPage"/> 条；<paramref name="lastPage"/> 那页返回不满一页
    /// （GitHub 的末页语义）；<paramref name="cancelFromPage"/> 那页模拟"请求发出后连接被取消"；
    /// <paramref name="failFromPage"/> 那页返回 401。
    /// </summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly int _perPage;
        private readonly int? _lastPage;
        private readonly int? _cancelFromPage;
        private readonly int? _failFromPage;
        private readonly CancellationTokenSource? _cancelWith;

        public int Requests { get; private set; }

        public FakeGitHub(int perPage, int? lastPage = null, int? cancelFromPage = null,
            int? failFromPage = null, CancellationTokenSource? cancelWith = null)
        {
            _perPage = perPage;
            _lastPage = lastPage;
            _cancelFromPage = cancelFromPage;
            _failFromPage = failFromPage;
            _cancelWith = cancelWith;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var page = ParsePage(request.RequestUri!.Query);
            Requests++;

            if (_cancelFromPage == page)
            {
                _cancelWith?.Cancel();   // 真实取消就是这形状：token 翻了，在途请求以 TaskCanceledException 收场
                return Task.FromException<HttpResponseMessage>(
                    new TaskCanceledException($"模拟：第 {page} 页请求被取消"));
            }

            if (_failFromPage is int from && page >= from)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var count = _lastPage == page ? _perPage - 1 : _perPage;
            var json = "[" + string.Join(",", Enumerable.Range(0, count)
                .Select(i => $$"""{"id":{{page * 100000 + i}},"full_name":"o/r{{page}}_{{i}}"}""")) + "]";
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            resp.Headers.ETag = new EntityTagHeaderValue(FreshEtag);
            return Task.FromResult(resp);
        }

        private static int ParsePage(string query)
        {
            foreach (var kv in query.TrimStart('?').Split('&'))
            {
                var p = kv.Split('=', 2);
                if (p.Length == 2 && p[0] == "page" && int.TryParse(p[1], out var n)) return n;
            }
            return 1;
        }
    }

    // ==================== 客户端层：取消/失败都不算"跑完" ====================

    [Fact]
    public async Task AlreadyCancelledToken_MakesNoRequestAtAll()
    {
        // 循环头的 ThrowIfCancellationRequested：取消已发生就不再发任何一页请求。
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new FakeGitHub(perPage: 100);
        using var client = new GitHubClient(Options(), new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAllStarredAsync(cts.Token));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task CancelledMidPagination_Throws_InsteadOfReturningPartialPage()
    {
        // 旧写法 `if (ct.IsCancellationRequested) break;` ⇒ 正常返回第 1 页 100 条，
        // 调用方无从区分"用户只 star 了 100 个仓库"与"翻到第 2 页被掐断"。
        using var cts = new CancellationTokenSource();
        var handler = new FakeGitHub(perPage: 100, cancelFromPage: 2, cancelWith: cts);
        using var client = new GitHubClient(Options(), new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAllStarredAsync(cts.Token));
        Assert.Equal(2, handler.Requests);   // 确实只跑到第 2 页（第 1 页那 100 条已到手，却没被当成结果返回）
    }

    [Fact]
    public async Task CancelledMidPagination_DoesNotAdvanceCachedETag()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeGitHub(perPage: 100, cancelFromPage: 2, cancelWith: cts);
        using var client = new GitHubClient(Options(), new HttpClient(handler)) { CachedETag = PriorEtag };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAllStarredAsync(cts.Token));

        // 页 1 已把 FreshEtag 带回（handler 正常返回过），未跑完 ⇒ 必须退回本轮开始前的值
        Assert.Equal(PriorEtag, client.CachedETag);
        Assert.NotEqual(FreshEtag, client.CachedETag);
    }

    [Fact]
    public async Task FailedMidPagination_RollsBackCachedETagToo()
    {
        // 另一条臂：不是取消，而是页 2 直接 401。旧实现页 1 就写 CachedETag，之后抛出，
        // 内存里的检查点已属"从未跑完的那一轮"——单例客户端带着它，下次同步凭空 304。
        using var cts = new CancellationTokenSource();
        using var client = new GitHubClient(Options(),
            new HttpClient(new FakeGitHub(perPage: 100, failFromPage: 2)))
        {
            CachedETag = PriorEtag,
        };

        await Assert.ThrowsAsync<GitHubApiException>(() => client.GetAllStarredAsync(cts.Token));
        Assert.Equal(PriorEtag, client.CachedETag);
    }

    [Fact]
    public async Task CompletedPagination_AdvancesCachedETag()
    {
        // 正向对照：整轮跑完（末页返回不满一页）时才允许前移，否则 P1-4 的条件请求省流就白做了。
        using var cts = new CancellationTokenSource();
        using var client = new GitHubClient(Options(3),
            new HttpClient(new FakeGitHub(perPage: 3, lastPage: 2)))
        {
            CachedETag = PriorEtag,
        };

        var all = await client.GetAllStarredAsync(cts.Token);

        Assert.Equal(5, all.Count);              // 页 1 满 3 条 + 页 2（末页）2 条
        Assert.Equal(FreshEtag, client.CachedETag);
    }

    // ==================== 源层：检查点落库面 ====================

    private ItemRepository Repo()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        return new ItemRepository(factory);
    }

    [Fact]
    public async Task Fetch_CancelledMidPull_Throws_AndLeavesSyncStateCheckpointsUntouched()
    {
        var repo = Repo();
        using var cts = new CancellationTokenSource();
        var client = new GitHubClient(Options(),
            new HttpClient(new FakeGitHub(perPage: 100, cancelFromPage: 2, cancelWith: cts)));
        await using var source = new GitHubSource(Options(), repo, client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.FetchAsync(new SyncContext(), cts.Token));

        // 抛出之后就算"补一次提交"也不许写出任何东西：协调器正是在载荷落库之后才提交检查点的。
        await source.CommitCheckpointAsync(CancellationToken.None);

        // 两个检查点都必须仍是空：写下任何一个，下一次同步就可能被 304 短路而永不补齐。
        Assert.Null(await repo.GetSyncStateAsync("github:etag", CancellationToken.None));
        Assert.Null(await repo.GetSyncStateAsync("github:last_synced_at", CancellationToken.None));
    }

    [Fact]
    public async Task Fetch_CompletedPull_LeavesCheckpointsUnwrittenUntilCommitted()
    {
        // P-19 的正身：载荷还没落库之前，检查点不许已经进库。旧写法在 FetchAsync 内部就写库，
        // 而 upsert 是协调器拿到返回之后才做的 ⇒ 崩在这两步之间＝新 ETag 已提交、条目没提交，
        // 下一轮首页带 If-None-Match 命中 304 返回零条，那批新 Star 再也不会被拉回来。
        var repo = Repo();
        var client = new GitHubClient(Options(3), new HttpClient(new FakeGitHub(perPage: 3, lastPage: 2)));
        await using var source = new GitHubSource(Options(3), repo, client);

        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Equal(5, items.Count);                                   // 载荷拉到了，但这一轮还没资格留下检查点
        Assert.Null(await repo.GetSyncStateAsync("github:etag", CancellationToken.None));
        Assert.Null(await repo.GetSyncStateAsync("github:last_synced_at", CancellationToken.None));
    }

    [Fact]
    public async Task CommitCheckpointWithoutAFetch_WritesNothing()
    {
        // 暂存的必须是"这一轮的"：一次都没拉过的源没资格写时间戳（否则诊断页会显示一次从未发生过的同步），
        // 上一轮没提交成的也不许被下一次提交顺手带进库。
        var repo = Repo();
        await using var source = new GitHubSource(Options(), repo,
            new GitHubClient(Options(), new HttpClient(new FakeGitHub(perPage: 3, lastPage: 1))));

        await source.CommitCheckpointAsync(CancellationToken.None);

        Assert.Null(await repo.GetSyncStateAsync("github:etag", CancellationToken.None));
        Assert.Null(await repo.GetSyncStateAsync("github:last_synced_at", CancellationToken.None));
    }

    [Fact]
    public async Task Fetch_CompletedPull_WritesBothCheckpoints()
    {
        // 正向对照：取消护栏不能顺手把 P1-4 的检查点写回也关掉。
        // 提交这一步现在归协调器在 UpsertAsync 之后调用（断言一条未减，只是把它接到"提交之后"）。
        var repo = Repo();
        using var cts = new CancellationTokenSource();
        var client = new GitHubClient(Options(3), new HttpClient(new FakeGitHub(perPage: 3, lastPage: 2)));
        await using var source = new GitHubSource(Options(3), repo, client);

        var items = await source.FetchAsync(new SyncContext(), cts.Token);
        await source.CommitCheckpointAsync(cts.Token);

        Assert.Equal(5, items.Count);
        Assert.Equal(FreshEtag, await repo.GetSyncStateAsync("github:etag", CancellationToken.None));
        var stamped = await repo.GetSyncStateAsync("github:last_synced_at", CancellationToken.None);
        Assert.NotNull(stamped);
        Assert.True(long.Parse(stamped!) > 0);
        // client 由 source.DisposeAsync 一并释放（它持有并 Dispose 注入进来的客户端）。
    }
}
