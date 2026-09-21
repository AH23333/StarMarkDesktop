#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Integrations.GitHub;

namespace StarMark.Tests;

/// <summary>
/// GetAllStarredAsync 分页安全上限应按"条目数"封顶，而非"固定 50 页"。
/// 旧实现 maxPages=50 使真实上限 = 50 × PageSize：PageSize 调小（如 10）会把可拉取量
/// 从宣称的 5000 静默砍到 500（超出部分永不获取、也不报错）。
/// </summary>
public sealed class GitHubClientPagingTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<int, int> _countForPage; // 页码 → 该页返回条目数
        public Handler(Func<int, int> countForPage) => _countForPage = countForPage;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var page = ParsePage(request.RequestUri!.Query);
            var count = _countForPage(page);
            var json = "[" + string.Join(",", Enumerable.Range(0, count)
                .Select(i => $$"""{"id":{{page * 100000 + i}},"full_name":"o/r{{page}}_{{i}}"}""")) + "]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
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

    private static GitHubOptions Options(int pageSize)
        => new() { Token = "ghp_test", PageSize = pageSize };

    [Fact]
    public async Task SmallPageSize_StillFetchesBeyondOld50PageWall()
    {
        // perPage=10：page 1..59 满页，page 60 返回 2（末页）。旧码在 page 50 停 → 500。
        var handler = new Handler(page => page < 60 ? 10 : 2);
        using var client = new GitHubClient(Options(10), new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(59 * 10 + 2, all.Count);   // 592：证明翻页不再被固定 50 页卡死
    }

    [Fact]
    public async Task FetchHaltsAtItemBudget5000()
    {
        // perPage=10、每页都满 → 新实现应停在 5000 条（条目预算），旧实现停在 500（50 页）。
        var handler = new Handler(_ => 10);
        using var client = new GitHubClient(Options(10), new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(5000, all.Count);
    }

    [Fact]
    public async Task ItemBudgetTrimAppliesWhenPageSizeNotDivide5000()
    {
        // 钉死 GetAllStarredAsync 末尾 `if (all.Count > maxItems) all.RemoveRange(...)` 的截断臂。
        // 上方 FetchHaltsAtItemBudget5000 用 perPage=10（整除 5000）→ 循环 `break` 时 all.Count 恰为
        // 5000，`>` 不成立 → 截断臂从未进入。用 perPage=99（不整除 5000）：page 51 满页使 all=5049
        // 触发 `>=` break，随后必须裁回恰好 5000；删掉 RemoveRange 会返回 5049。
        var handler = new Handler(_ => 99);
        using var client = new GitHubClient(Options(99), new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(5000, all.Count);   // 5049 → 裁到预算上限
    }
}
