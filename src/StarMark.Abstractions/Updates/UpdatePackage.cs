#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions.Updates;

/// <summary>
/// 更新包里<b>归应用管</b>的那一颗文件（批次 UF）。
/// <para><paramref name="Path"/> 是相对安装根目录的正斜杠路径（判据在 Core 的清单读法里，
/// 那里挡住了 <c>..</c>、绝对路径与盘符——同一族形状在 Zip Slip 的普查里判过：
/// 写出目标必须由我们自己拼，不能由包里的名字派生）。</para>
/// </summary>
public sealed record ReleaseManifestFile(string Path, string Sha256, long Bytes);

/// <summary>
/// 一次发布的<b>清单</b>：哪一版、载荷叫什么、每一颗文件的哈希与大小。
/// <para>它是唯一被签名背书的东西，所以"哪些文件属于这次更新、各该有多长"这些答案都出自签名之内。
/// <b>唯一的例外是载荷那颗的名字</b>：它按 <see cref="UpdateAssets.PackageNameFor"/> 从标签算出来，
/// 清单里那一栏只用来交叉核对（在读清单之前就得先决定去哪儿读——见那里的注释）。</para>
/// </summary>
public sealed record ReleaseManifest(
    int Schema,
    string Version,
    string Package,
    string PackageSha256,
    long PackageBytes,
    IReadOnlyList<ReleaseManifestFile> Files);

/// <summary>
/// 一次"取包"取回来的三样东西的搬运袋：清单原文、签名原文、已经落到临时盘的载荷。
/// <para><b>这里不代表任何信任判定</b>——签名验没验过、哈希对不对、能不能装，全在 Core 那一步
/// （<c>UpdateIntegrity.Inspect</c>）。传输层只负责"把字节取回来并且知道它是哪些字节"，
/// 这样"取错了但看起来像对的"那几种形状才有地方被当场挡住。</para>
/// <para>两个字节数组不做相等比较：它们是外部输入的原样副本，比较哈希才是有意义的问题。</para>
/// </summary>
public sealed record FetchedPackage(
    byte[] ManifestBytes,
    byte[] SignatureBytes,
    string PackagePath,
    string PackageSha256,
    long PackageBytes);

/// <summary>
/// 取一次更新包的<b>结局分类</b>。
/// <para>为什么不复用 <see cref="ReleaseProbeStatus"/>：那一套说的是"问版本问得怎么样"，这一套说的是
/// "取东西取得怎么样"——两者只有四种因由重合（出不去／超时／服务端坏了／凭据被拒），
/// 合到一枚枚举上会让探测那侧的穷举分流被"清单没签对"这种无关成员污染，
/// 也会让"这一版根本没带安装包"这种只属于取包的答案没地方待。</para>
/// <para>代价是"离线"这类因由在两处各有一句话，因此 <c>UpdatePolicy</c> 里两个 <c>Describe</c>
/// 都得对各自枚举的<b>全部</b>成员穷举（有测试逐条点验），不许留到 default 臂去蒙。</para>
/// </summary>
public enum PackageFetchStatus
{
    /// <summary>清单、签名、载荷三样都取回来了（<see cref="PackageFetchResult.Package"/> 非空）。</summary>
    Fetched,

    /// <summary>这一版发布了，但<b>清单／签名／载荷缺一门</b>（远端 404）——发版漏传资产时的真实形状。</summary>
    AssetMissing,

    /// <summary>要写的路径／标签这一侧就认不出来，请求根本没发出去。</summary>
    AddressRefused,

    /// <summary>跳转被我们挡住：跳去了不是 GitHub 资产域的主机，或从 https 掉到 http。</summary>
    OffHostRedirect,

    /// <summary>回来的字节超过上限（清单是 JSON 的尺寸上限、载荷是清单里那个字节数再放宽的一档）。</summary>
    TooLarge,

    /// <summary>清单在，但读不懂（不是 JSON、缺字段、字段形状不对）。</summary>
    ManifestUnreadable,

    /// <summary>连不上对方：DNS、代理、离线、TLS。</summary>
    NotReachable,

    /// <summary>超时（有界等待，不许把"更新中"变成永远转圈）。</summary>
    TimedOut,

    /// <summary>对方内部错误（5xx）。</summary>
    ServerError,

    /// <summary>请求被拒（Token 失效、作用域不够）。</summary>
    Unauthorized,

    /// <summary>限流。</summary>
    RateLimited,

    /// <summary>字节能取回来但本机临时目录写不下去（磁盘满、ACL、被占用）。</summary>
    DiskWriteFailed,
}

/// <summary>取包结局。<b>失败也要带上"从哪儿看出来的"</b>，与 <see cref="ReleaseProbeResult"/> 同一口径。</summary>
public sealed record PackageFetchResult(
    PackageFetchStatus Status,
    FetchedPackage? Package = null,
    string? Detail = null)
{
    /// <summary>只有三样齐了才谈得上"验一验能不能装"。</summary>
    public bool HasPackage => Status == PackageFetchStatus.Fetched && Package is not null;
}

/// <summary>
/// 一次发布的<b>资产形状</b>：三颗资产叫什么、各自最大多少字节、载荷那颗按什么名字找。
/// <para>放在 Abstractions 是因为这件事有<b>两边</b>要用：拼地址与判清单在 Core，
/// 取字节在 Integrations，而依赖方向是 <c>Core → Integrations</c>——只有这里能同时被两边看见。
/// 更重要的是它让<b>传输层不必先读清单</b>：载荷那颗的名字是按标签算出来的，
/// 于是"从哪儿取字节"这个决定恰恰发生在签名之外的字段被读到之前。</para>
/// </summary>
public static class UpdateAssets
{
    /// <summary>
    /// 这个程序认账的清单格式那一档。<b>发布那边读它，不在脚本里另写一份 1</b>：
    /// 两边各写一份就会漂成"签了一份没人认得的东西"，那种表现是全线更新不可用，
    /// 而不是哪一格红（同一族的坑在 #189/#193 反复写过：两处实现必漂移）。
    /// </summary>
    public const int SupportedSchema = 1;

    /// <summary>清单那颗资产的文件名（签名盖的就是这一颗的字节）。</summary>
    public const string ManifestAssetName = "update-manifest.json";

    /// <summary>签名的资产名。<b>裸 DER</b>，不做 base64：少一道编码就少一处"两边编解码不一致"的可能。</summary>
    public const string SignatureAssetName = "update-manifest.sig";

    /// <summary>清单本身的字节上限：几千颗文件的哈希表也用不了这么多，超出即当作"这不是清单"。</summary>
    public const long MaxManifestBytes = 1_000_000;

    /// <summary>
    /// 签名那颗的字节上限。P-256 的 DER 签名最长 72 字节，留一档余量只为挡住"拿一整页 HTML 当签名"；
    /// <b>取包那侧用同一个数当下载上限</b>，否则会出现"下得回来但一定验不过"的那一格。
    /// </summary>
    public const int MaxSignatureBytes = 160;

    /// <summary>载荷字节上限。清单里写了更大的数就当它不是我们的包（这一档只防"荒谬"，不防蓄意，真正的界是签名）。</summary>
    public const long MaxPackageBytes = 512L * 1024 * 1024;

    /// <summary>清单里 <c>files</c> 的颗数上限。零颗是另一回事——那是"签了一份什么都不换的清单"，由 Core 拒。</summary>
    public const int MaxManifestFiles = 4096;

    /// <summary>
    /// 产物必须含的主程序。<b>清单里认不出这一颗就拒</b>：一份"签名合法但载荷里没有可执行入口"的清单
    /// 能把安装目录换成一堆没人能启动的文件——那是这条链最坏的一种成功。
    /// </summary>
    public const string EntryExeName = "StarMark.UI.exe";

    /// <summary>
    /// 载荷那颗的名字<b>由版本号算</b>，不由清单说。
    /// <para>为什么反过来定：清单要能读，得先把它的字节取回来；而"取哪颗"若由清单说，
    /// 就等于让一份<b>还没验过签名</b>的东西决定我们的下一个请求打到哪儿去。
    /// 现在这个名字只依赖标签（标签在探测那一步已经比过新旧、也过了字符集），
    /// 而清单里那一欄留着做<b>交叉核对</b>：两边说的不是同一个名字，就是有人在拼装的哪一步错乱了。</para>
    /// </summary>
    public static string PackageNameFor(string version) => $"StarMark-{version}-win-x64.zip";

    /// <summary>
    /// 把 zip 条目名归一成"清单里那种写法"（反斜杠改正斜杠、去掉开头的斜杠）。
    /// <para><b>发布机与摊包两侧必须共用这一句</b>：两侧各写一份的话，"签出来的名字"与
    /// "摊的时候查表用的名字"迟早漂开，而那种漂移的表现是每一颗都报"清单列了、包里没有"，
    /// 于是全线更新不可用（#189/#193 那一族）。这里只转换名字——
    /// 归一之后的名字仍然只用来<b>查表</b>，绝不拿它拼写写出去的路径（见 <c>UpdateStaging</c>）。</para>
    /// </summary>
    public static string NormalizeEntryName(string fullName) => fullName.Replace('\\', '/').TrimStart('/');

    /// <summary>归一后的这颗名字代不代表"一颗要落盘的文件"：目录项（以 <c>/</c> 结尾）与空名都不代表。</summary>
    public static bool IsFileEntry(string normalizedEntryName)
        => normalizedEntryName.Length > 0 && !normalizedEntryName.EndsWith("/", StringComparison.Ordinal);
}

/// <summary>
/// Core 拼好、交给传输层去取的三个地址。<b>三个串全部由本程序自己拼</b>，
/// 远端响应里的任何链接字段都不进这里（同一口径见 <c>RemoteRelease</c> 为什么没有 <c>Url</c>）。
/// </summary>
public sealed record PackageAssetAddresses(string ManifestUrl, string SignatureUrl, string PackageUrl);

/// <summary>
/// 取一次更新包（清单＋签名＋载荷）。<b>这一层不做信任判定，也不装任何东西</b>——
/// 验签与"这版能不能覆盖你现在这版"在 <c>StarMark.Core</c>，替换正在运行的程序是另一批（见账本 P-144）。
/// </summary>
public interface IUpdatePackageSource
{
    Task<PackageFetchResult> FetchAsync(PackageAssetAddresses addresses, CancellationToken ct = default);
}

/// <summary>
/// 把已验签的载荷<b>摊成一堆待换的文件</b>（逐颗对账）的结局分类（批次 UG-1）。
/// <para>与 <see cref="PackageFetchStatus"/> 一样另立一枚枚举：那一套说的是"取字节取得怎么样"，
/// 这一套说的是"摊开之后每颗文件与清单说没说得上话"。合在一起会让"连不上 GitHub"与
/// "清单列了包里没有"变成同一个答案，而前者下次联网会自己补问、后者是发布产物自己坏了。</para>
/// </summary>
public enum StageStatus
{
    /// <summary>清单列的每一颗都在、每一颗的哈希都对得上（<see cref="StageResult.StagedRoot"/> 可交给更新器）。</summary>
    Staged,

    /// <summary>没有一份"可信"判决就想开工——<b>这一档的存在就是它自己的理由</b>：
    /// 摊包的入口只认 <c>UpdateIntegrity</c> 的判决，绕开判决就没有暂存树。</summary>
    NotTrusted,

    /// <summary>载荷不是一颗读得开的 zip（多半是发布机上打包那一步错了，而不是网络上出了事）。</summary>
    PackageUnreadable,

    /// <summary>清单列了这颗，包里没有——签的内容与装的东西不是一套。</summary>
    FileMissing,

    /// <summary>包里有清单没列出的字节（或重名条目）。<b>没背书的字节不许进安装目录</b>。</summary>
    UnexpectedEntry,

    /// <summary>摊出来的这颗与清单上那颗哈希不是同一颗。</summary>
    FileHashMismatch,

    /// <summary>本机写不下去（暂存位置所在盘满、没权限，或给来的路径串本身不能用）。</summary>
    DiskWriteFailed,
}

/// <summary>
/// 摊包的结局。<see cref="StagedRoot"/> 只有 <see cref="StageStatus.Staged"/> 时才非空——
/// 半途失败的树在返回之前就已经被删掉了（"摊了一半"与"摊好了"在磁盘上必须看得出区别）。
/// </summary>
public sealed record StageResult(StageStatus Status, string? StagedRoot = null, string? Detail = null)
{
    /// <summary>能不能交给更新器去换文件。</summary>
    public bool IsStaged => Status == StageStatus.Staged && StagedRoot is not null;
}
