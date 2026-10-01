#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions.Updates;

/// <summary>
/// "远端最新那版"长什么样，以及<b>为什么没问到</b>（批次 UE）。
/// <para>
/// 契约类型放在 Abstractions 是这仓既定的分层口径（<c>Abstractions.Trending</c> 同一件事）：
/// 编排服务在 Core、抓取在 Integrations，而依赖方向是 <c>Core → Integrations</c>，
/// 只有 Abstractions 能同时被两边看见。
/// </para>
/// </summary>
/// <param name="Tag">发布标签（GitHub 惯例形如 <c>v1.1.0</c>）——版本比较与那句话都只读这一格。</param>
/// <param name="Name">发布标题，可空。<b>只用于展示，绝不拿来当版本号</b>（缺 <c>tag_name</c> 时按"读不懂"处理）。</param>
/// <param name="Prerelease">是不是预发布。</param>
/// <param name="PublishedUtc">发布时间，取不到为 null。
/// <para><b>这里刻意没有 <c>Url</c> 一格</b>：对方还会回一个 <c>html_url</c>，而那个串是远端说了算的字符串。
/// 检查更新唯一往屏幕外走的一步是"用浏览器打开发布页"，把一个外部服务器给的地址直接交给系统去开，
/// 等于让它在"你的应用有新版本"这句话的授权下换掉落地站点。发布页地址由本程序按<b>配置里的仓库</b>自己拼
/// （<c>UpdatePolicy.ReleasePageUrl</c>），远端那个字段从解析那一步起就不进任何数据结构。</para>
/// </param>
public sealed record RemoteRelease(string Tag, string? Name, bool Prerelease, DateTimeOffset? PublishedUtc);

/// <summary>
/// 一次探测的<b>结局分类</b>。<b>为什么要有这个枚举而不是一个异常</b>：
/// "查不到"在这条线上不是一种失败，而是好几种得说不一样的事——
/// 仓库还没发布过（第一次上线时就是这个答案，不是错误）、这台机器现在出不去（代理/离线）、
/// 限流（匿名 60 次/小时，重启一次查一次就撞）、Token 失效（他有 Token 却过期了）、
/// 对方坏了。把它们并成一个"失败"，界面就只能说"检查失败"，
/// 而那正是本仓判过最多次的缺陷形状：<b>把要人做的事推回给用户</b>（P-54）。
/// </summary>
public enum ReleaseProbeStatus
{
    /// <summary>问到了最新版（<see cref="ReleaseProbeResult.Release"/> 非空）。</summary>
    Found,

    /// <summary>仓库存在，但<b>一个 Release 都没发过</b>（远端 404 ⇒ GitHub 就是这么回应的）。</summary>
    NothingPublished,

    /// <summary>仓库不存在或不可见（改过 owner/repo、私有仓库又没带凭据）。</summary>
    RepositoryNotVisible,

    /// <summary>请求被拒（Token 失效、作用域不够）。</summary>
    Unauthorized,

    /// <summary>限流（匿名配额 60 次/小时；带 Token 时更高）。</summary>
    RateLimited,

    /// <summary>连不上对方：DNS、代理、离线、TLS 都算这一格（细分要额外信息，且对用户是同一句话）。</summary>
    NotReachable,

    /// <summary>超时（有界等待；对方挂住时不许把检查变成"永远转圈"）。</summary>
    TimedOut,

    /// <summary>对方内部错误（5xx）。</summary>
    ServerError,

    /// <summary>回来了，但不是能读的形状（不是 JSON、缺 tag_name、JSON 结构变了）。</summary>
    Unreadable,
}

/// <summary>一次探测的结果。<b>失败也要带上"从哪儿看出来的"</b>（异常类型或 HTTP 状态），否则无从查起。</summary>
public sealed record ReleaseProbeResult(ReleaseProbeStatus Status, RemoteRelease? Release = null, string? Detail = null)
{
    /// <summary>只有 <see cref="ReleaseProbeStatus.Found"/> 且带得上版本标签时才是"问到东西"。</summary>
    public bool HasRelease => Status == ReleaseProbeStatus.Found && Release is not null;

    /// <summary>
    /// 这一格要不要<b>打扰</b>用户。<b>只有"确实有新版本"才是 yes</b>——
    /// 限流／离线／超时都属"再问一次就知道"，为它们弹卡就是把检查工具的噪声变成用户的负担；
    /// 而"仓库还没发布"在第一次上线时是常态，每天弹一次尤其荒唐（判据只在这一处，见 <c>UpdatePolicy</c>）。
    /// </summary>
    public bool WorthAnnouncing => Status == ReleaseProbeStatus.Found;
}

/// <summary>
/// 问一次"远端最新是什么版本"。<b>不许往机器上下任何东西</b>——这条接口只读，
/// 下载与应用是另一件事（登记在账本，前置条件见 <c>docs/待决策事项.md</c>）。
/// </summary>
public interface IReleaseSource
{
    Task<ReleaseProbeResult> ProbeAsync(string repository, CancellationToken ct = default);
}
