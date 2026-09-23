#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Trending;

namespace StarMark.Core.Trending;

/// <param name="Repos">要显示的候选（缓存命中时就是缓存内容）。</param>
/// <param name="Via">来源；null＝旧缓存里没记来源（老数据），界面标注"来源未知"。</param>
/// <param name="FromCache">这批数据来自缓存（没发请求）。</param>
/// <param name="Stale">抓取失败后回读的旧缓存——<b>必须与 FromCache 分开</b>：
/// "今天没抓到，给你看上次那份"与"今天已经抓过了"是两件事，混成一个用户就不知道该不该重试。</param>
/// <param name="FetchedAt">这批数据的抓取时刻（Unix 秒；0＝未知）。</param>
/// <param name="Notice">要贴给用户的一句话（兜底原因 / 失败原因 / 缓存没写进去）。可为 null。</param>
public sealed record TrendingResult(
    IReadOnlyList<TrendingRepo> Repos, TrendingSource? Via,
    bool FromCache, bool Stale, long FetchedAt, string? Notice = null);

/// <summary>
/// 热榜编排：读缓存 →（同本地日历日且非强制刷新则直接返回）→ 抓取 → 写缓存；抓取失败回读旧缓存并标 stale。
/// <para>
/// 刻意<b>不收</b> <c>IItemRepository</c> 而是收两个窄委托：一是让"缓存读写失败"这条分支可以单测
/// （真仓储要建库），二是本服务只用得到 sync_state 这一个能力，注入整个仓储接口会把它的作用面放大。
/// </para>
/// <para>不进 <c>SyncCoordinator</c> 的串行循环：那条队列服务的是"同步收藏"，热榜既不写 items 也不带凭据，
/// 混进去只会让同步状态多一个假归属。</para>
/// </summary>
public sealed class TrendingService
{
    private readonly ITrendingSource _source;
    private readonly Func<string, CancellationToken, Task<string?>> _readCache;
    private readonly Func<string, string, CancellationToken, Task> _writeCache;
    private readonly Func<DateTimeOffset> _now;

    public TrendingService(
        ITrendingSource source,
        Func<string, CancellationToken, Task<string?>> readCache,
        Func<string, string, CancellationToken, Task> writeCache,
        Func<DateTimeOffset>? now = null)
    {
        _source = source;
        _readCache = readCache;
        _writeCache = writeCache;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <param name="force">页内「刷新」按钮：跳过"同一本地日历日"的短路，但仍然"抓到才覆盖缓存"。</param>
    public async Task<TrendingResult> GetAsync(
        TrendingPeriod period, string? language, bool force, CancellationToken ct)
    {
        var key = TrendingCacheCodec.CacheStateKey(period, language);
        var cached = TrendingCacheCodec.Decode(await _readCache(key, ct));
        var now = _now();
        var nowUnix = now.ToUnixTimeSeconds();

        if (!force && cached is { HasContent: true })
        {
            if (TrendingCacheCodec.IsSameLocalDay(cached.FetchedAt, nowUnix, now.Offset))
                return new TrendingResult(cached.Repos, TrendingSources.ParseCode(cached.Via),
                    FromCache: true, Stale: false, cached.FetchedAt);
        }

        TrendingFetchResult fetched;
        try
        {
            fetched = await _source.FetchAsync(period, language, ct);
        }
        catch (OperationCanceledException) { throw; }        // 用户掐的，不是"抓取失败"：不许回读旧缓存装成一次成功
        catch (Exception ex)
        {
            // 抓取失败但有旧缓存 ⇒ 给它，并说明是上次的（空列表比报错更难查，见 P-54/P-56 口径）
            if (cached is { HasContent: true })
                return new TrendingResult(cached.Repos, TrendingSources.ParseCode(cached.Via),
                    FromCache: true, Stale: true, cached.FetchedAt, ex.Message);
            throw;                                            // 什么都没有：抛出去让界面给原因 + 「重试」
        }

        var wrote = await TryWriteCacheAsync(key, fetched, nowUnix, ct);
        return new TrendingResult(fetched.Repos, fetched.Via, FromCache: false, Stale: false, nowUnix,
            wrote is null ? fetched.Notice : Combine(fetched.Notice, wrote));
    }

    /// <summary>
    /// 缓存写失败不该把已经抓到的数据丢掉（那等于"网络好了但界面还是看不到"），
    /// 也不能静默：否则用户以为明天还会自动更新，实际每天都在重抓。⇒ 数据照出，附一句原因。
    /// </summary>
    private async Task<string?> TryWriteCacheAsync(string key, TrendingFetchResult fetched, long nowUnix, CancellationToken ct)
    {
        var payload = TrendingCacheCodec.Encode(new TrendingCachedEntry(
            fetched.Repos, nowUnix, fetched.Via.Code()));
        try
        {
            await _writeCache(key, payload, ct);
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StarLog.Warn($"热榜缓存写入失败（{key}）：{ex.Message}");
            return "缓存未能写入本机，下次打开仍会重新抓取";
        }
    }

    private static string? Combine(string? a, string b) => string.IsNullOrEmpty(a) ? b : a + "；" + b;
}
