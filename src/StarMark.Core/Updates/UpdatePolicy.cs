#nullable enable
using System;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>检查的结局。<b>每一格都要能被界面原样说一句话</b>，所以不给一个笼统的 Failed。</summary>
public enum UpdateVerdict
{
    /// <summary>远端有更新的一版。</summary>
    NewerAvailable,
    /// <summary>已经最新。</summary>
    UpToDate,
    /// <summary>本机比远端还新（装了预发布、或自己编的版）。</summary>
    LocalAhead,
    /// <summary>这个仓库还没发布过版本（第一次上线时就是这个答案，不是故障）。</summary>
    NothingPublished,
    /// <summary>仓库看不见（改名／私有而没带凭据）。</summary>
    RepositoryNotVisible,
    /// <summary>凭据被拒（Token 失效或作用域不够）。</summary>
    Unauthorized,
    /// <summary>被限流（问得太勤）。</summary>
    RateLimited,
    /// <summary>这台机器现在出不去（离线／代理／DNS）。</summary>
    NotReachable,
    /// <summary>对方没在限时内回答。</summary>
    TimedOut,
    /// <summary>对方内部错误。</summary>
    ServerError,
    /// <summary>问到了，但那串版本号看不懂。</summary>
    UnreadableRemoteTag,
    /// <summary>本机版本号读不到 ⇒ 无从比较（<b>不许猜成"更旧"从而报"有新版"</b>）。</summary>
    LocalVersionUnknown,
}

/// <summary>
/// 检查更新的全部判据：<b>分类、措辞、节奏、不重复打扰、去问谁</b>（批次 UE）。
/// <para>
/// 存在理由：这条线上"怎么说"和"要不要说"比"能不能连上 GitHub"更容易做错。三种形状各踩过一次：
/// ① 把"查不到"并成一格 ⇒ 界面只能说"失败"，用户无从动作（P-54 判过的"把要做的事推回给人"）；
/// ② 把读不到的本机版本当 0.0.0 ⇒ 每次检查都报"有新版"，而那条提示永远点不通；
/// ③ 每天提醒同一个新版 ⇒ 第三次起用户只会把整条功能关掉。
/// </para>
/// <para>措辞只有这一处（<see cref="Describe"/>）：日志、托盘那一项、设置页那张卡、右下角的卡
/// 都读它——同一件事在四个地方四种说法，是本仓 #122/#130 那一族的老病。</para>
/// </summary>
public static class UpdatePolicy
{
    /// <summary>
    /// 去哪个仓库问。<b>真源只有这一处</b>（发布方＝这个应用自己的仓库）。
    /// <para><see cref="RepositoryOverride"/> 存在的唯一理由是让"发现新版本"那一格在真机上可证：
    /// 这个仓库今天还没有任何 Release（批次 UE 查过：releases 与 tags 都是空），
    /// 所以产品第一次上线的正确答案是 <see cref="UpdateVerdict.NothingPublished"/>——
    /// 那是终点态，不是可以拿来交差的中间态。要证明"真的找到新版时界面会说什么"，
    /// 只能指着一个已经有发布历史的仓库问一次。</para>
    /// </summary>
    public const string DefaultRepository = "AH23333/StarMarkDesktop";

    /// <summary>冒烟／取证用的临时改道（只认 <c>owner/name</c> 形状，别的值一律忽略）。</summary>
    public const string RepositoryEnvironmentVariable = "STARMARK_UPDATE_REPO";

    /// <summary>两次自动检查之间的最短间隔。手动"现在检查"不受这条管。</summary>
    public const int ProbeGapHours = 24;

    /// <summary>今天该问谁：默认那一个，或改道值（形状不对就当没改）。</summary>
    public static string RepositoryOf(string? environmentValue)
    {
        if (string.IsNullOrWhiteSpace(environmentValue)) return DefaultRepository;
        var trimmed = environmentValue.Trim();
        var slash = trimmed.IndexOf('/');
        // 只认 "owner/name"：多一段、空段、带协议头的都不接受——这条字符串会被拼进请求路径，
        // 放宽一次形状就变成"能让程序去问任意主机的一条注入面"。
        if (slash <= 0 || slash != trimmed.LastIndexOf('/') || slash == trimmed.Length - 1) return DefaultRepository;
        if (trimmed.IndexOf("://", StringComparison.Ordinal) >= 0) return DefaultRepository;
        return trimmed;
    }

    /// <summary>
    /// 把一次探测的结果<b>折成一个结局</b>。<b>顺序是有意的</b>：
    /// 本机版本读不到 ⇒ 就算远端有更新也不能报"有新版"（无从判断新旧，报了就是猜）。
    /// </summary>
    public static UpdateVerdict Classify(ReleaseProbeResult probe, string? localVersionText)
    {
        if (!AppVersion.TryParse(localVersionText, out var local)) return UpdateVerdict.LocalVersionUnknown;

        switch (probe.Status)
        {
            case ReleaseProbeStatus.NothingPublished: return UpdateVerdict.NothingPublished;
            case ReleaseProbeStatus.RepositoryNotVisible: return UpdateVerdict.RepositoryNotVisible;
            case ReleaseProbeStatus.Unauthorized: return UpdateVerdict.Unauthorized;
            case ReleaseProbeStatus.RateLimited: return UpdateVerdict.RateLimited;
            case ReleaseProbeStatus.NotReachable: return UpdateVerdict.NotReachable;
            case ReleaseProbeStatus.TimedOut: return UpdateVerdict.TimedOut;
            case ReleaseProbeStatus.ServerError: return UpdateVerdict.ServerError;
            case ReleaseProbeStatus.Unreadable: return UpdateVerdict.UnreadableRemoteTag;
        }

        if (probe.Release is null) return UpdateVerdict.UnreadableRemoteTag;
        if (!AppVersion.TryParse(probe.Release.Tag, out var remote)) return UpdateVerdict.UnreadableRemoteTag;

        return AppVersion.Compare(local, remote) switch
        {
            < 0 => UpdateVerdict.NewerAvailable,
            0 => UpdateVerdict.UpToDate,
            _ => UpdateVerdict.LocalAhead,
        };
    }

    /// <summary>
    /// 那句话的唯一出处。<b>每格都带下一步能做的动作</b>：有新版就给版本号、被限流就说什么时候再问、
    /// 出不去就说这是网络而不是程序坏了。只有"仓库还没发布"这一格什么都不承诺——
    /// 那本来就是发布侧的事，写"请去发布"就是把活推回给人。
    /// </summary>
    public static string Describe(UpdateVerdict verdict, RemoteRelease? release = null, string? localVersionText = null)
        => verdict switch
        {
            UpdateVerdict.NewerAvailable =>
                $"有新版本 {release?.Tag ?? "?"}（本机 {localVersionText ?? AppVersion.Unknown}）",
            UpdateVerdict.UpToDate => $"已经是最新（{localVersionText ?? AppVersion.Unknown}）",
            UpdateVerdict.LocalAhead =>
                $"本机版本 {localVersionText ?? AppVersion.Unknown} 比远端 {release?.Tag ?? "?"} 更新（这通常是预发布或自己编的版）",
            UpdateVerdict.NothingPublished => "这个仓库还没有发布过版本",
            UpdateVerdict.RepositoryNotVisible => $"查不到仓库 {DefaultRepository}（改名或私有而没配凭据）",
            UpdateVerdict.Unauthorized => "凭据被拒：GitHub Token 已失效或作用域不够，重新填一次即可",
            UpdateVerdict.RateLimited => $"GitHub 暂时限流（匿名每小时 {60} 次），{ProbeGapHours} 小时后会自己再问一次",
            UpdateVerdict.NotReachable => "这台机器现在连不上 GitHub（离线或代理挡着），下次联网会自己补问",
            UpdateVerdict.TimedOut => "GitHub 没在限时内回答，过一会儿会自己再问一次",
            UpdateVerdict.ServerError => "GitHub 那边报错了，过一会儿会自己再问一次",
            UpdateVerdict.UnreadableRemoteTag =>
                $"远端那个版本标签读不懂（{release?.Tag ?? "空"}），所以不猜新旧",
            UpdateVerdict.LocalVersionUnknown => "本机版本号读不到（这份产物没写版本号），无法比较",
            _ => "检查更新没有结果",
        };

    /// <summary>
    /// 现在该不该<b>自动</b>去问一次：总开关关着就永远不问；从没问过就问（第一次开机之后）；
    /// 问过了要隔满 <see cref="ProbeGapHours"/>。
    /// <para>用绝对差值而非带符号比较：机器时钟被调过（快／慢）时，带符号差值会一侧永久成立或永不成立
    /// ——同一族的坑在自动备份那条上写得更细（<c>AutoBackupPolicy.ShouldRun</c>）。</para>
    /// </summary>
    public static bool ShouldAutoProbe(bool enabled, DateTimeOffset? lastProbeUtc, DateTimeOffset nowUtc)
    {
        if (!enabled) return false;
        if (lastProbeUtc is null) return true;
        return (nowUtc - lastProbeUtc.Value).Duration() >= TimeSpan.FromHours(ProbeGapHours);
    }

    /// <summary>
    /// 这一次结果要不要<b>出声</b>（右下角那张卡）。两条都要满足：结局是"确实有新版"，
    /// 而且<b>这个标签还没提醒过</b>。
    /// <para>第二臂是这条功能唯一"不打扰人"的保证：没有它，每天开机都会被同一版弹一次卡，
    /// 第三天他就会把开关关掉——一个聒噪的检查器比没有检查器更糟。</para>
    /// </summary>
    public static bool ShouldAnnounce(UpdateVerdict verdict, string? releaseTag, string? alreadyAnnouncedTag)
        => verdict == UpdateVerdict.NewerAvailable
            && !string.IsNullOrWhiteSpace(releaseTag)
            && !string.Equals(releaseTag, alreadyAnnouncedTag, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 界面上那颗「打开下载页」该不该出现。判据只有这一处：<b>只有"确实有一版比你现在的更新"才有东西可下</b>。
    /// <para>留着按钮而点下去是 GitHub 的 404 页，与"点了没反应"是同一类缺陷（P-54）；
    /// 而在"已经最新""还没发布过"那些格放一个下载按钮，等于用界面暗示有一个并不存在的产物。</para>
    /// </summary>
    public static bool HasDownloadableRelease(UpdateVerdict verdict) => verdict == UpdateVerdict.NewerAvailable;

    /// <summary>
    /// 发布页地址：<b>本程序自己拼，绝不用远端回的那条 <c>html_url</c></b>。
    /// <para>为什么专门立这一条：检查更新唯一一步"往屏幕外走"的动作就是拿浏览器打开一个地址，
    /// 而 <c>html_url</c> 是外部服务器想写什么就写什么的字符串。把它原样交给系统去开，
    /// 等于让一句"有新版本"的话顺手换掉落地站点——BJ 那批查凭据外泄时同一条口径
    /// （<c>GitHubReleaseSource</c> 因此连这个字段都不读）。
    /// 站点、路径、仓库三段全来自本程序自己的常量与配置。</para>
    /// <para>标签那一段只认 GitHub 标签实际允许的字符集，且限长：它是远端给的字符串，
    /// 而这里要把它放进路径。认不回来就退回那一列的总页（<c>/releases</c>）——
    /// "打不开具体的那一版"和"把路径写到别处去"相比，前者只是少半屏信息。</para>
    /// </summary>
    public static string ReleasePageUrl(string repository, string? tag)
        => IsPlainTag(tag)
            ? $"https://github.com/{RepositoryOf(repository)}/releases/tag/{tag}"
            : $"https://github.com/{RepositoryOf(repository)}/releases";

    private static bool IsPlainTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 100) return false;
        foreach (var c in tag)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) return false;
        return true;
    }

    /// <summary>
    /// 这个结局算不算"一次真的问过了"（决定要不要把 <c>LastProbeUtc</c> 写下去）。
    /// <para><b>网络类失败不算问过</b>：离线开机时那次失败要把下次机会留在"联网即补问"，
    /// 而不是记成"今天问过了"，从此要等满 24 小时——那一格的正确行为是自己重试，不是让人动手（P-54 口径）。
    /// 限流／服务端／未读回来这些算问过（它们确实得到了对方的答复，只是答复是"现在不行"）。</para>
    /// </summary>
    public static bool CountsAsProbed(UpdateVerdict verdict)
        => verdict is not (UpdateVerdict.NotReachable or UpdateVerdict.TimedOut);
}
