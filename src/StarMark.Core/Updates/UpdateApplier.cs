#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>
/// 「立即更新」那一颗按钮背后的整条链：<b>取包 → 审包 → 摊包 → 交棒</b>（批次 UG-3）。
/// <para>
/// 这一层刻意<b>不自己换文件、也不自己退出程序</b>：换文件的是更新器那个进程（<see cref="UpdaterEngine"/>），
/// 而"关掉现在这一个"是界面那侧的动作（它才知道单实例互斥、才知道怎么把窗口收干净）。
/// 这一层的最后一句真话是<b>"活已经交出去了"</b>（<see cref="LaunchStatus.Started"/>），
/// 不是"装好了"——把这两种话混在一层，界面上就有一句谎（#234）。
/// </para>
/// <para>四步各自已有的判据一处都不重写：审包走 <see cref="UpdateIntegrity"/>（信任根内嵌，不接受调用方传），
/// 摊包走 <see cref="UpdateStaging"/>（只收判决），交棒走 <see cref="UpdaterLauncher"/>（整目录副本＋安装目录之外）。
/// 这一层只负责<b>顺序</b>与<b>每一步摔了之后说什么</b>。</para>
/// <para>盘上留下的东西只有两样，而且都不在安装目录里：暂存根旁边那棵 <c>.new</c>（下一次摊之前自己删，
/// 是自愈而不是让人去清目录，P-54）与那颗下载下来的 zip——<b>后者在本次走完之后无条件删掉</b>，
/// 因为"帮你更新一次"不该在 %TEMP% 里留一百多兆没人认领的字节。</para>
/// </summary>
public sealed class UpdateApplier
{
    private readonly IUpdatePackageSource _packages;
    private readonly Func<string?> _environment;
    private readonly Func<string?> _localVersion;
    private readonly Func<string> _installDir;

    /// <summary>结局。<b>只有两格</b>：交出去了／这次什么都没换（哪一步没过去写在 <c>Detail</c>，
    /// 那句话出自各步已有的 <c>Describe</c>，这里不另写一套措辞）。</summary>
    public sealed record ApplyResult(ApplyStatus Status, string? Sentence = null, string? Detail = null)
    {
        public bool IsHandedOff => Status == ApplyStatus.HandedOff;
    }

    public UpdateApplier(
        IUpdatePackageSource packages,
        Func<string>? installDir = null,
        Func<string?>? localVersion = null,
        Func<string?>? environment = null)
    {
        _packages = packages;
        _installDir = installDir ?? ProcessDirectory;
        _localVersion = localVersion ?? (() =>
            AppVersion.TryReadLocal(out var v) ? AppVersion.Describe(v) : null);
        _environment = environment ?? (() => Environment.GetEnvironmentVariable(UpdatePolicy.RepositoryEnvironmentVariable));
    }

    /// <summary>正在跑的那颗 exe 所在的目录＝安装目录。<b>不接受调用方指定别的</b>：
    /// 换错一棵树比不换更糟，而"换哪儿"这件事只有进程自己知道是可靠的。</summary>
    private static string ProcessDirectory()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException("认不出正在运行的这颗 exe 在哪儿");
        return dir;
    }

    /// <summary>
    /// 两处注入边。<b>界面上那条唯一的接线点不传这一格</b>（有闸门钉着）：
    /// <see cref="InspectPackage"/> 一旦能被外面换掉，"信谁"就变成了调用方的参数，而那正是
    /// <see cref="UpdateIntegrity"/> 生产入口刻意不接受公钥参数的同一理由（#229）；
    /// <see cref="Launcher"/> 只是为了不让测去起真进程与动 <c>%LOCALAPPDATA%</c>。
    /// </summary>
    public sealed class Options
    {
        public Func<FetchedPackage, string, string, string?, UpdateIntegrity.Result>? InspectPackage { get; init; }
        public UpdaterLauncher.Options? Launcher { get; init; }
    }

    /// <summary>
    /// 走一遍。<paramref name="remoteTag"/> 是"上一次问出来的那一版"——<b>界面把结论递进来，
    /// 但不递任何路径或地址</b>：地址由 Core 按配置里的仓库自己拼（远端回的连接串一个字节都不参与，UE 那条），
    /// 落点由 <see cref="ProcessDirectory"/> 与 <see cref="UpdaterPaths"/> 定。
    /// </summary>
    /// <param name="onPhase">每进下一步叫一次（界面据此改那一行字）。可以为 null。</param>
    /// <param name="onBytes">下载载荷期间的字节读数（批次 VW）。<b>它在哪个线程上叫不作保证</b>——
    /// 传输层那边是 <c>ConfigureAwait(false)</c> 之后的续接，所以接过去的那一侧（界面）必须自己回 UI 线程。
    /// 刻意与 <paramref name="onPhase"/> 分成两条：那一条说"走到哪一步"，这一条说"这一步走了多少"，
    /// 混成一条回调就会有人把"下了 40 MB"演成"装好了"（#234）。</param>
    public async Task<ApplyResult> ApplyAsync(
        string? remoteTag, Action<ApplyPhase>? onPhase = null, CancellationToken ct = default,
        Options? options = null, Action<DownloadProgress>? onBytes = null)
    {
        var o = options ?? new Options();
        var inspect = o.InspectPackage ?? UpdateIntegrity.Inspect;
        var repository = UpdatePolicy.RepositoryOf(_environment());
        var tag = remoteTag;
        if (string.IsNullOrWhiteSpace(tag))
            return new ApplyResult(ApplyStatus.NothingApplied, UpdatePolicy.Describe(ApplyStatus.NothingApplied),
                "没有要装的那一版（还没查过，或上一次没问到标签）");

        var addresses = UpdatePolicy.ReleaseAssetAddresses(repository, tag);
        if (addresses is null)
            return new ApplyResult(ApplyStatus.NothingApplied, UpdatePolicy.Describe(ApplyStatus.NothingApplied),
                $"那个标签拼不出资产地址：{tag}");

        onPhase?.Invoke(ApplyPhase.Downloading);
        PackageFetchResult fetch;
        try
        {
            fetch = await _packages.FetchAsync(addresses, ct, onBytes).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }          // 掐掉就是掐掉：不写结局、不兜底（P-55）
        catch (Exception ex)
        {
            // 取包层本该自己分类；真漏出来的一种按"连不上"说，与检查那层的同一口径（#230）。
            StarLog.Warn($"[更新] 取包异常（按连不上处理）：{ex.GetType().Name}");
            fetch = new PackageFetchResult(PackageFetchStatus.NotReachable, null, ex.GetType().Name);
        }
        if (!fetch.HasPackage)
            return NotApplied(UpdatePolicy.Describe(fetch.Status), $"取包：{fetch.Status} {fetch.Detail}");

        var package = fetch.Package!;
        try
        {
            onPhase?.Invoke(ApplyPhase.Inspecting);
            var verdict = inspect(package, repository, tag, _localVersion());
            if (!verdict.IsTrusted)
                return NotApplied(UpdatePolicy.Describe(verdict.Outcome), $"审包：{verdict.Outcome} {verdict.Detail}");

            var install = _installDir();
            var stagingRoot = UpdaterPaths.StagingRootFor(install);
            onPhase?.Invoke(ApplyPhase.Staging);
            var staged = await UpdateStaging
                .PrepareAsync(verdict, package, stagingRoot, ct).ConfigureAwait(false);
            if (!staged.IsStaged)
                return NotApplied(UpdatePolicy.Describe(staged.Status), $"摊包：{staged.Status} {staged.Detail}");

            onPhase?.Invoke(ApplyPhase.HandingOff);
            var launch = UpdaterLauncher.PrepareAndStart(
                new UpdaterRequest(Environment.ProcessId, staged.StagedRoot!, install), o.Launcher);
            if (!launch.IsStarted)
                return NotApplied(UpdatePolicy.Describe(launch.Status), $"交棒：{launch.Status} {launch.Detail}");

            StarLog.Info($"[更新] 已把 {tag} 的替换交给更新器（摊在 {staged.StagedRoot}，装到 {install}）");
            return new ApplyResult(ApplyStatus.HandedOff, UpdatePolicy.Describe(ApplyStatus.HandedOff));
        }
        finally
        {
            // 载荷用完就走：它已经在摊包那一步逐颗对过账了，留着只会占地方并被当成"可复用的缓存"。
            TryDeletePayload(package.PackagePath);
        }
    }

    private static ApplyResult NotApplied(string sentence, string detail)
    {
        StarLog.Warn($"[更新] 没有装：{detail}");
        return new ApplyResult(ApplyStatus.NothingApplied, sentence, detail);
    }

    private static void TryDeletePayload(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉只是那颗 zip 还在 %TEMP% 里：本次的结局已经定了，不该被收尾改写（尺子不许弄崩被量的东西）。
            StarLog.Warn($"[更新] 下载下来的包没删掉：{ex.GetType().Name}");
        }
    }
}

/// <summary>
/// 走到哪一步了（<b>只给界面写那一行字用，不是结局</b>）。
/// <para>它和 <see cref="ApplyStatus"/> 分成两族是因为量纲不同：这一族每走一步都会变，
/// 而那一族只在最后说一次"交出去了"或"什么都没换"。混成一族就会有一格既是进度又是结局，
/// 界面于是可以把"正在下载"演成"已经装好"（#234 那一族的另一面）。</para>
/// </summary>
public enum ApplyPhase { Downloading, Inspecting, Staging, HandingOff }

/// <summary>整条链的结局。<b>只有两格</b>，理由见 <see cref="UpdateApplier"/>。</summary>
public enum ApplyStatus
{
    /// <summary>更新器已经带着这条指令跑起来了（换没换成接下来由它说，界面上这句不许写成"已装好"）。</summary>
    HandedOff,

    /// <summary>一个字节都没换，程序还开着。哪一步没过去在 <c>Detail</c> 里，那句话由各步自己的 <c>Describe</c> 说。</summary>
    NothingApplied,
}
