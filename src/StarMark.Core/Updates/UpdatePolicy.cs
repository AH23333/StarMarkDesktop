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

    private static bool IsPlainTag(string? tag) => IsPlainPath(tag, 100);

    /// <summary>
    /// 这个结局算不算"一次真的问过了"（决定要不要把 <c>LastProbeUtc</c> 写下去）。
    /// <para><b>网络类失败不算问过</b>：离线开机时那次失败要把下次机会留在"联网即补问"，
    /// 而不是记成"今天问过了"，从此要等满 24 小时——那一格的正确行为是自己重试，不是让人动手（P-54 口径）。
    /// 限流／服务端／未读回来这些算问过（它们确实得到了对方的答复，只是答复是"现在不行"）。</para>
    /// </summary>
    public static bool CountsAsProbed(UpdateVerdict verdict)
        => verdict is not (UpdateVerdict.NotReachable or UpdateVerdict.TimedOut);

    // ===== 取包（批次 UF）：地址仍然是我们拼，措辞仍然只在这一处 =====

    /// <summary>
    /// 某一顆发布资产的下载地址：<b>由本程序按"仓库 + 标签 + 资产名"拼出来</b>，
    /// 与 <see cref="ReleasePageUrl"/> 同一条口径，理由在这里更硬一档。
    /// <para>GitHub 的响应里本来就带着现成的下载地址（<c>assets[].browser_download_url</c>），
    /// 读它更省事——但那等于<b>把"从哪儿取代码"这个决定交给一个外部服务器</b>：一次被改写的响应
    /// 就能让"帮你更新"变成"帮你运行别人选的字节"。三段输入各有各的来源：仓库出自配置、标签出自
    /// 已经比过新旧的那一次探测、资产名一颗来自常量、一颗来自<b>签名之内</b>的清单字段。</para>
    /// <para>任一段认不出字符集就返回 null（不猜、也不"先把能改的改掉再试"）。</para>
    /// </summary>
    public static string? ReleaseAssetUrl(string repository, string tag, string assetName)
    {
        if (!IsPlainPath(tag, 100) || !IsPlainPath(assetName, 120)) return null;
        var path = RepositoryOf(repository);
        return IsPlainRepositoryPath(path)
            ? $"https://github.com/{path}/releases/download/{tag}/{assetName}"
            : null;
    }

    /// <summary>
    /// 一次取包要的<b>三个地址</b>：清单、签名、载荷。
    /// <para>三顆一次拼齐，是为了让<b>传输层拿到的是死地址</b>——它不需要、也不允许再去读清单里的任何字段
    /// 才能决定下一发请求打到哪儿（依赖方向 <c>Core → Integrations</c> 在这里正好帮了一把：
    /// Integrations 连清单的读法都看不见）。载荷那顆的名字出自 <see cref="UpdateAssets.PackageNameFor"/>，
    /// 输入只有标签，而标签在探测那一步已经过字符集与新旧两道判。</para>
    /// <para>任一顆拼不出来 ⇒ null（宁可不发请求，也不带着半套地址去试）。
    /// 清单里那一欄与这里的算出的名字是否同一顆，由 <c>UpdateIntegrity</c> 在<b>验签之后</b>核对。</para>
    /// </summary>
    public static PackageAssetAddresses? ReleaseAssetAddresses(string repository, string tag)
    {
        if (!AppVersion.TryParse(tag, out var parsed)) return null;
        var manifest = ReleaseAssetUrl(repository, tag, UpdateAssets.ManifestAssetName);
        var signature = ReleaseAssetUrl(repository, tag, UpdateAssets.SignatureAssetName);
        var package = ReleaseAssetUrl(repository, tag, UpdateAssets.PackageNameFor(AppVersion.Describe(parsed)));
        return manifest is null || signature is null || package is null
            ? null
            : new PackageAssetAddresses(manifest, signature, package);
    }

    /// <summary>"一段光秃秃的路径分量"：非空、限长、只含 GitHub 实际允许的字符（<b>不含斜杠</b>）。</summary>
    private static bool IsPlainPath(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value!.Length > maxLength) return false;
        foreach (var c in value)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) return false;
        return true;
    }

    /// <summary>仓库那一段：字符集同上，而斜杠<b>只许 owner 与 name 之间那一根</b>。</summary>
    private static bool IsPlainRepositoryPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.Length > 200) return false;
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '/') { slashes++; continue; }
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) return false;
        }
        return slashes == 1;
    }

    /// <summary>
    /// 取一次包的结局要说的那句话。<b>与 <see cref="Describe(UpdateVerdict)"/> 分开是两套枚举的代价</b>
    /// （见 <c>PackageFetchStatus</c> 的注释），重合的那几句刻意保持同一说法。
    /// </summary>
    public static string Describe(PackageFetchStatus status) => status switch
    {
        PackageFetchStatus.Fetched => "更新包已经取到，校验也过了（还没开始装）",
        PackageFetchStatus.AssetMissing => "这一版没带更新包（发布时漏传了清单或载荷），只能打开发布页看",
        PackageFetchStatus.AddressRefused => "更新地址拼不出来：仓库或标签的写法认不出来",
        PackageFetchStatus.OffHostRedirect => "下载中途被引到了 GitHub 之外的站点，已当场拒绝",
        PackageFetchStatus.TooLarge => "取回的字节超过这一档上限，没当它是更新包",
        PackageFetchStatus.ManifestUnreadable => "更新清单读不懂（多半是清单格式比这个程序新）",
        PackageFetchStatus.NotReachable => "这台机器现在连不上 GitHub（离线或代理挡着），下次联网会自己补问",
        PackageFetchStatus.TimedOut => "GitHub 没在限时内把包送完，过一会儿会自己再试",
        PackageFetchStatus.ServerError => "GitHub 那边报错了，过一会儿会自己再试",
        PackageFetchStatus.Unauthorized => "凭据被拒：GitHub Token 已失效或作用域不够，重新填一次即可",
        PackageFetchStatus.RateLimited => $"GitHub 暂时限流，{ProbeGapHours} 小时后会自己再问一次",
        PackageFetchStatus.DiskWriteFailed => "本机临时目录写不下去（磁盘满或没权限），更新没有开始",
        _ => "取更新包没有结果",
    };

    /// <summary>
    /// 一份取回来的包<b>审不过</b>时的那句话。
    /// <para>签名／哈希那两格与其余不同：它们不是"再试一次就好"，而是"这里有人在换东西"。
    /// 所以这几句必须照实说，不许折成一句通用的"更新失败"（P-54 同口径，也免得安全事件被降噪成噪声）。</para>
    /// <para>另一条口径上的洁癖：这一层的每一格都发生在<b>临时载荷已经落下</b>之后，所以哪一格都不许说
    /// "没动本机任何文件"——那句说的是"什么都没写"，而这里确实写过一颗临时文件。
    /// 能担保的是弱一点但真的那一件：<b>装目录里的东西一个字没换</b>，故统一落在"没装"／"已拒绝"上。</para>
    /// </summary>
    public static string Describe(UpdateIntegrity.Outcome outcome) => outcome switch
    {
        UpdateIntegrity.Outcome.Trusted => "这份包签得对、字节也对得上清单",
        UpdateIntegrity.Outcome.SignatureInvalid => "签名对不上：这份清单不是我们发出去的，已拒绝",
        UpdateIntegrity.Outcome.ManifestUnreadable => "更新清单读不懂（多半是清单格式比这个程序新），没装",
        UpdateIntegrity.Outcome.SchemaUnsupported => "清单格式比这个程序新，先升级程序再更新，没装",
        UpdateIntegrity.Outcome.FieldRejected => "清单里有认不出的值（路径越界、哈希形状不对、或没有主程序），已拒绝",
        UpdateIntegrity.Outcome.VersionMismatch => "清单写的版本与要取的那一版不是同一版，没装",
        UpdateIntegrity.Outcome.RollbackRefused => "这一版不比本机现在的更新，装了是往回退，所以没装",
        UpdateIntegrity.Outcome.LocalVersionUnknown => "本机版本号读不到，判断不了新旧，所以没装",
        UpdateIntegrity.Outcome.PackageHashMismatch => "包的内容与清单里的哈希对不上（可能被换过），已拒绝",
        UpdateIntegrity.Outcome.PackageSizeMismatch => "包的大小与清单里说的不一致，已拒绝",
        _ => "更新包没有通过校验，没装",
    };

    /// <summary>
    /// 摊包（把载荷变成一堆待换文件）那一步的结局措辞（批次 UG-1）。
    /// <para>与上面两套一样必须<b>逐格穷举</b>：这一层的失败都不是"再试一次就好"，
    /// 而是"这份包与它的清单不是一套"——说成"网络问题"会把一个发布事故藏成一次重试。</para>
    /// <para>口径照旧：没摊成的树已经被删掉了，所以每一句都在说"没往下走"，不许出现"部分替换成功"这种半句真话。</para>
    /// </summary>
    public static string Describe(StageStatus status) => status switch
    {
        StageStatus.Staged => "更新包已经摊开并逐颗对过账（还没开始换文件）",
        StageStatus.NotTrusted => "这份包没通过校验，压根不该来摊——一步都没往下走",
        StageStatus.PackageUnreadable => "更新包不是一颗读得开的压缩包（多半是发布机上打包那一步错了），没装",
        StageStatus.FileMissing => "清单列出的文件在包里找不到，没装",
        StageStatus.UnexpectedEntry => "包里有清单没记的文件（或重名条目），已拒绝",
        StageStatus.FileHashMismatch => "摊出来的文件与清单里的哈希对不上（可能被换过），已拒绝",
        StageStatus.DiskWriteFailed => "本机写不出暂存文件（磁盘满或没权限），更新没有开始",
        _ => "更新包没摊开，没装",
    };
}
