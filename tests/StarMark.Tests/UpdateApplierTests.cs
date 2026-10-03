#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「立即更新」那一颗按钮背后的整条链（批次 UG-3）：<b>取包 → 审包 → 摊包 → 交棒</b>。
/// <para>与 UG-1／UG-2 同一口径：<b>全部用真临时目录跑</b>，只注入两条与外界打交道的边
/// （<see cref="UpdateApplier.Options.InspectPackage"/> 换信任根、
/// <see cref="UpdaterLauncher.Options.StartRunner"/> 不起真进程）。这一层的存在理由就是"顺序"与
/// "每一步摔了说什么"，而顺序只有在真盘上才量得出来——假装置递不到"那颗 zip 真的被删了"与
/// "摊好的树真的在那儿"这两条（#230/#232 那一族的欠见证形状）。</para>
/// </summary>
public sealed class UpdateApplierTests : IDisposable
{
    private const string Repository = "AH23333/StarMarkDesktop";
    private const string Version = "1.0.1";
    private const string Tag = "v1.0.1";
    private const string Local = "1.0.0";

    private readonly string _root;
    private readonly string _install;         // 假安装目录（里面真的站着 exe 与 Resources\）
    private readonly string _payload;         // 下载落点（真 zip）
    private readonly string _updaterSource;   // 更新器那一带的家（RunnerSourceOverride）
    private readonly string _runnerHome;      // 副本落点（RunnerHomeOverride：不许碰 %LOCALAPPDATA%）
    private readonly string _stagingRoot;     // 摊包的家（安装目录旁边，不是 %TEMP%）

    public UpdateApplierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "StarMarkApplierTests", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "app", "StarMark");
        _payload = Path.Combine(_root, "dl", UpdateAssets.PackageNameFor(Version));
        _updaterSource = Path.Combine(_root, "updater");
        _runnerHome = Path.Combine(_root, "runner");
        _stagingRoot = UpdaterPaths.StagingRootFor(_install);
        Directory.CreateDirectory(Path.Combine(_install, "Resources"));
        File.WriteAllText(Path.Combine(_install, "StarMark.UI.exe"), "old-exe-bytes");
        File.WriteAllText(Path.Combine(_install, "Resources", "app.ico"), "ico-bytes");
        Directory.CreateDirectory(_updaterSource);
        File.WriteAllText(Path.Combine(_updaterSource, UpdaterPaths.UpdaterExeName), "runner-bytes");
        Directory.CreateDirectory(Path.GetDirectoryName(_payload)!);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* 测试尾巴，不因为它把结论改掉 */ }
    }

    // ===== 成功那一格：交出去、不留下载残渣、安装目录一个字节没动 =====

    [Fact]
    public async Task TheHappyPathHandsOffAndLeavesNoDownloadedZip()
    {
        var started = new List<(string Exe, IReadOnlyList<string> Args)>();
        var (applier, options) = Fixture(Files(), startRunner: started);

        var result = await applier.ApplyAsync(Tag, options: options);

        Assert.Equal(ApplyStatus.HandedOff, result.Status);
        Assert.Equal(UpdatePolicy.Describe(LaunchStatus.Started), result.Sentence);   // 那句不许在界面侧另写一份
        Assert.False(File.Exists(_payload));                                          // 那颗 zip 用完就走
        Assert.Single(started);
    }

    /// <summary>
    /// 交出去的那条指令<b>说的是真的两棵树</b>：新树＝这一层刚摊出来的那一棵，
    /// 目标＝正在跑的这一颗 exe 所在的目录（不是界面递进来的、也不是包里那套名字）。
    /// </summary>
    [Fact]
    public async Task TheHandedOffRequestNamesTheStagedTreeAndTheRealInstallDir()
    {
        var started = new List<(string Exe, IReadOnlyList<string> Args)>();
        var (applier, options) = Fixture(Files(), startRunner: started);

        await applier.ApplyAsync(Tag, options: options);

        var (_, args) = Assert.Single(started);
        Assert.True(UpdaterContract.TryDecode(args, out var request, out var decodeError));
        Assert.Null(decodeError);
        Assert.Equal(Environment.ProcessId, request!.ParentPid);
        Assert.Equal(_install, request.InstallDir);
        Assert.Equal(Path.Combine(_stagingRoot, Version + ".new"), request.StagedTree);
    }

    /// <summary>摊出来的那棵树真的在盘上、且逐颗就是清单那几颗（这条不证就没法说"已经准备好可换了"）。</summary>
    [Fact]
    public async Task TheStagedTreeIsOnDiskWhenTheHandoffHappens()
    {
        var (applier, options) = Fixture(Files(), startRunner: new());

        await applier.ApplyAsync(Tag, options: options);

        Assert.Equal(new[] { "StarMark.UI.exe", "a/lib.dll" },
            RelativeNames(Path.Combine(_stagingRoot, Version + ".new")));
    }

    /// <summary>四步各报一次、且按真实次序——界面上那一行字不许跳步，也不许把两步并成一步。</summary>
    [Fact]
    public async Task TheFourPhasesAreReportedOnceEachInOrder()
    {
        var seen = new List<ApplyPhase>();
        var (applier, options) = Fixture(Files(), startRunner: new());

        await applier.ApplyAsync(Tag, seen.Add, CancellationToken.None, options);

        Assert.Equal(new[]
        {
            ApplyPhase.Downloading, ApplyPhase.Inspecting, ApplyPhase.Staging, ApplyPhase.HandingOff,
        }, seen);
    }

    /// <summary>
    /// 字节读数必须<b>一路走到开口要它的那个人</b>（批次 VW）。这一格的存在理由：
    /// 回调在 Core 这一层是可以被"忘了传"的——忘了传之后界面还是照常转圈、照常报四步，
    /// 只是那一行字永远停在"正在下载…"，而这种坏没有任何别的格子会红。
    /// </summary>
    [Fact]
    public async Task TheByteReadoutsTravelToTheCallerWhoAskedForThem()
    {
        var seen = new List<DownloadProgress>();
        var (applier, options) = Fixture(Files(), startRunner: new());

        await applier.ApplyAsync(Tag, _ => { }, CancellationToken.None, options, seen.Add);

        Assert.Equal(new[] { new DownloadProgress(1024, 2048), new DownloadProgress(2048, 2048) }, seen);
    }

    /// <summary>
    /// 接了读数<b>不许改变这条链的任何一步</b>：四步仍然各报一次、顺序不变，
    /// 而假源报几格就只有几格到达（不重复、不放大）。少了这一格，"把回调塞进编排"这件事只有一半证据。
    /// </summary>
    [Fact]
    public async Task ReadoutsDoNotChangeAnyStepOfTheChain()
    {
        var seen = new List<ApplyPhase>();
        var marks = 0;
        var (applier, options) = Fixture(Files(), startRunner: new());

        await applier.ApplyAsync(Tag, seen.Add, CancellationToken.None, options, _ => marks++);

        Assert.Equal(new[]
        {
            ApplyPhase.Downloading, ApplyPhase.Inspecting, ApplyPhase.Staging, ApplyPhase.HandingOff,
        }, seen);
        Assert.Equal(2, marks);
    }

    /// <summary>安装目录在这一层<b>一个字节都不许变</b>：它只负责把活交出去（连成功那一格也是）。</summary>
    [Fact]
    public async Task TheInstallDirectoryIsUntouchedWhenTheHandoffSucceeds()
    {
        var before = ContentOf(_install);
        var (applier, options) = Fixture(Files(), startRunner: new());

        await applier.ApplyAsync(Tag, options: options);

        Assert.Equal(before, ContentOf(_install));
    }

    // ===== 还没出门就拒：一次网络都不许碰 =====

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NothingToApplyNeverAsksForAPackage(string? tag)
    {
        var source = new CountingSource(new PackageFetchResult(PackageFetchStatus.Fetched, null, "不该被叫到"));
        var applier = new UpdateApplier(source, installDir: () => _install, localVersion: () => Local,
            environment: () => Repository);

        var result = await applier.ApplyAsync(tag);

        Assert.Equal(ApplyStatus.NothingApplied, result.Status);
        Assert.Equal(UpdatePolicy.Describe(ApplyStatus.NothingApplied), result.Sentence);
        Assert.False(string.IsNullOrEmpty(result.Detail));
        Assert.Equal(0, source.Calls);                                                // 一次都没问
        Assert.False(Directory.Exists(_stagingRoot));                                  // 连暂存根都没建
    }

    /// <summary>标签读不出版本号 ⇒ 拼不出资产地址，也就没得取（与"没查过"同一格：什么都没换）。</summary>
    [Fact]
    public async Task ATagThatBuildsNoAssetAddressIsRefusedWithoutAsking()
    {
        var source = new CountingSource(new PackageFetchResult(PackageFetchStatus.Fetched, null, "不该被叫到"));
        var applier = new UpdateApplier(source, installDir: () => _install, localVersion: () => Local,
            environment: () => Repository);

        var result = await applier.ApplyAsync("这不是一个版本号");

        Assert.Equal(ApplyStatus.NothingApplied, result.Status);
        Assert.Equal(0, source.Calls);
    }

    // ===== 每一步摔了说自己那一句（措辞不许被并成一句"更新失败"） =====

    public static TheoryData<PackageFetchStatus> FetchFailures() => new()
    {
        PackageFetchStatus.AssetMissing,
        PackageFetchStatus.NotReachable,
        PackageFetchStatus.TimedOut,
        PackageFetchStatus.RateLimited,
        PackageFetchStatus.DiskWriteFailed,
        PackageFetchStatus.Unauthorized,
    };

    /// <summary>
    /// 取包摔了：界面念的是<b>取包那一族自己的那一句</b>（UF 那批已钉它们各不相同），
    /// 而不是这一层新造的一句"更新失败"。顺带钉住：这一刻盘上不该有暂存根。
    /// </summary>
    [Theory]
    [MemberData(nameof(FetchFailures))]
    public async Task EveryFetchFailureIsPassedThroughWithItsOwnSentence(PackageFetchStatus status)
    {
        var applier = new UpdateApplier(new FixedSource(new PackageFetchResult(status, null, "test detail")),
            installDir: () => _install, localVersion: () => Local, environment: () => Repository);

        var result = await applier.ApplyAsync(Tag);

        Assert.Equal(ApplyStatus.NothingApplied, result.Status);
        Assert.Equal(UpdatePolicy.Describe(status), result.Sentence);
        Assert.NotEqual(UpdatePolicy.Describe(ApplyStatus.NothingApplied), result.Sentence);  // 不许退回那句通用话
        Assert.False(Directory.Exists(_stagingRoot));
    }

    /// <summary>
    /// <b>默认那条路用内嵌的公钥</b>：自造密钥签出来的包在真链路上必拒。
    /// 这一格是 <see cref="UpdateApplier.Options.InspectPackage"/> 那道注入边的对价——
    /// 边可以换信任根，先要证明"不换就是内嵌那把"，否则缝一旦被人接上，全线校验当天归零而没人红。
    /// </summary>
    [Fact]
    public async Task ATestSignedPackageIsRejectedByTheEmbeddedTrustRoot()
    {
        var applier = new UpdateApplier(SignedSource(), installDir: () => _install,
            localVersion: () => Local, environment: () => Repository);

        var result = await applier.ApplyAsync(Tag);                      // 不传 Options ⇒ 走生产入口

        Assert.Equal(ApplyStatus.NothingApplied, result.Status);
        Assert.Equal(UpdatePolicy.Describe(UpdateIntegrity.Outcome.SignatureInvalid), result.Sentence);
        Assert.False(Directory.Exists(_stagingRoot));
        Assert.False(File.Exists(_payload));                             // 拒绝之后那颗 zip 也不许留着
    }

    /// <summary>签得对但不比本机新 ⇒ 审包那一层就挡住，不去摊一棵装上要往回退的树。</summary>
    [Fact]
    public async Task APackageThatIsNotNewerIsRefusedBeforeAnythingIsStaged()
    {
        var (applier, options) = Fixture(Files(), localVersion: Version, startRunner: new());

        var result = await applier.ApplyAsync(Tag, options: options);

        Assert.Equal(UpdatePolicy.Describe(UpdateIntegrity.Outcome.RollbackRefused), result.Sentence);
        Assert.False(Directory.Exists(_stagingRoot));
        Assert.False(File.Exists(_payload));
    }

    /// <summary>清单列了包里没有的一颗：摊包那一层的句子原样递出去，半棵不许留，zip 也删。</summary>
    [Fact]
    public async Task AManifestThatListsWhatTheZipLacksStopsAtStaging()
    {
        var (applier, options) = Fixture(Files(), manifestEntries: Files().Concat(new[] { Entry("ghost.dll", 5) }).ToArray());

        var result = await applier.ApplyAsync(Tag, options: options);

        Assert.Equal(UpdatePolicy.Describe(StageStatus.FileMissing), result.Sentence);
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
        Assert.False(File.Exists(_payload));
    }

    /// <summary>
    /// 摊好了但这台机器上没带更新器：<b>那句是真话</b>，而摊好的那一棵<b>留在原地</b>——
    /// 安装目录没动，下一次摊之前会自己清掉（自愈，不叫人来删目录，P-54 同口径）。
    /// </summary>
    [Fact]
    public async Task AnInstallWithoutAnUpdaterSaysSoAndSwapsNothing()
    {
        File.Delete(Path.Combine(_updaterSource, UpdaterPaths.UpdaterExeName));
        var (applier, options) = Fixture(Files(), startRunner: new());
        var installBefore = ContentOf(_install);

        var result = await applier.ApplyAsync(Tag, options: options);

        Assert.Equal(UpdatePolicy.Describe(LaunchStatus.NotStarted), result.Sentence);
        Assert.True(Directory.Exists(Path.Combine(_stagingRoot, Version + ".new")));
        Assert.Equal(installBefore, ContentOf(_install));
        Assert.False(Directory.Exists(Path.Combine(_install, UpdaterPaths.UpdaterFolderName)));   // 连副本都没往里带
    }

    /// <summary>取消不是失败：原样交回，但摊了一半的树与那颗 zip 都不许留在盘上（#230：不变量不跟措辞走）。</summary>
    [Fact]
    public async Task ACancelledRunPropagatesAndLeavesNoHalfTreeOrZip()
    {
        var (applier, options) = Fixture(Files(), startRunner: new());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applier.ApplyAsync(Tag, null, cts.Token, options));

        Assert.False(File.Exists(_payload));
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    // ===== 形状：这一层的射程与措辞的出处 =====

    /// <summary>
    /// 这一层<b>不换文件、不起进程、不退出程序</b>：那些分别是 <see cref="UpdaterEngine"/> 与界面那侧的事。
    /// "自我替换"最难的一种坏就是两层都以为对方会做（或两层都去做）。
    /// </summary>
    [Fact]
    public void ThisLayerNeverSwapsOrStartsOrExits()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateApplier.cs"));
        foreach (var banned in new[]
                 {
                     "Directory.Move", "File.Replace", "File.Move", "Process.Start", "Process.",
                     "Environment.Exit", "UpdaterEngine.Run", "ReleaseSingleInstance",
                 })
            Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
        Assert.Contains("UpdaterLauncher.PrepareAndStart(", code, StringComparison.Ordinal);  // 唯一的出门动作
    }

    /// <summary>审包那一步<b>默认走不接受公钥的生产入口</b>；注入边只是测试的缝，不许把这颗类型名带进这一层。</summary>
    [Fact]
    public void TheTrustRootIsNotAParameterOfThisLayer()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateApplier.cs"));
        Assert.Contains("?? UpdateIntegrity.Inspect", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateTrustAnchor", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryApplyStatusAndPhaseHasItsOwnArmInUpdatePolicy()
    {
        var policy = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs"));
        foreach (ApplyStatus value in Enum.GetValues<ApplyStatus>())
            Assert.Contains($"ApplyStatus.{value} =>", policy, StringComparison.Ordinal);
        foreach (ApplyPhase value in Enum.GetValues<ApplyPhase>())
            Assert.Contains($"ApplyPhase.{value} =>", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApplySentencesAreAsManyDistinctOnesAsThereAre()
    {
        Assert.Equal(Enum.GetValues<ApplyStatus>().Length,
            Enum.GetValues<ApplyStatus>().Select(UpdatePolicy.Describe).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enum.GetValues<ApplyPhase>().Length,
            Enum.GetValues<ApplyPhase>().Select(UpdatePolicy.Describe).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>进度那几句说出来必须"还在走"：不许出现"完成/成功/已装"，也不许出现"请…／稍后"（P-54）。</summary>
    [Theory]
    [MemberData(nameof(EveryApplyPhase))]
    public void ProgressSentencesNeverReadLikeAResult(ApplyPhase phase)
    {
        var text = UpdatePolicy.Describe(phase);
        Assert.StartsWith("正在", text, StringComparison.Ordinal);
        foreach (var banned in new[] { "完成", "成功", "已装", "请", "稍后" })
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 读数那句的<b>头一句必须是从四步那一句取来的</b>，不是抄一份一模一样的字。
    /// <para>为什么要按源码形状判：两份完全相同的串在行为测上永远一致（<c>StartsWith</c> 照样过），
    /// 而"抄一份"之后改一处另一处不会跟着改——那是 #189/#193 那一族最典型的静默漂移。
    /// 判据形状＝方法体里要有 <c>Describe(ApplyPhase.Downloading)</c> 这一手，且不出现那句字面。</para>
    /// </summary>
    [Fact]
    public void TheReadoutHeadIsTakenFromThePhaseSentenceRatherThanCopied()
    {
        var body = MethodBody(Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs")),
            "public static string Describe(DownloadProgress progress)");
        Assert.Contains("Describe(ApplyPhase.Downloading)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("正在下载", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 带读数的下载那一句（批次 VW）：<b>头一句必须就是四步那一句</b>（措辞只有一个出处），
    /// 而接上去的读数不许把这句话变成结局。两种分母各判一次：知道总长才给百分比。
    /// </summary>
    [Fact]
    public void TheReadoutSentenceIsThePhaseSentencePlusNumbers()
    {
        var head = UpdatePolicy.Describe(ApplyPhase.Downloading);
        var known = UpdatePolicy.Describe(new DownloadProgress(1_048_576, 2_097_152));
        var unknown = UpdatePolicy.Describe(new DownloadProgress(1_048_576, null));

        Assert.StartsWith(head, known, StringComparison.Ordinal);
        Assert.StartsWith(head, unknown, StringComparison.Ordinal);
        Assert.Contains("50%", known, StringComparison.Ordinal);         // 有分母才有百分比
        Assert.DoesNotContain("%", unknown, StringComparison.Ordinal);   // 没分母就不许编一个出来
        foreach (var banned in new[] { "完成", "成功", "已装", "请", "稍后" })
        {
            Assert.DoesNotContain(banned, known, StringComparison.Ordinal);
            Assert.DoesNotContain(banned, unknown, StringComparison.Ordinal);
        }
    }

    /// <summary>结局那两格不许把"交出去了"演成"装好了"（#234：两种量纲并成一族就是一句谎）。</summary>
    [Theory]
    [MemberData(nameof(EveryApplyStatus))]
    public void ResultSentencesNeverClaimTheSwapIsDone(ApplyStatus status)
    {
        var text = UpdatePolicy.Describe(status);
        Assert.False(string.IsNullOrWhiteSpace(text));
        foreach (var banned in new[] { "已安装", "替换完成", "更新成功", "请重新安装" })
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
    }

    public static TheoryData<ApplyPhase> EveryApplyPhase()
    {
        var data = new TheoryData<ApplyPhase>();
        foreach (ApplyPhase value in Enum.GetValues<ApplyPhase>()) data.Add(value);
        return data;
    }

    public static TheoryData<ApplyStatus> EveryApplyStatus()
    {
        var data = new TheoryData<ApplyStatus>();
        foreach (ApplyStatus value in Enum.GetValues<ApplyStatus>()) data.Add(value);
        return data;
    }

    // ===== 装置 =====

    private static (string Path, byte[] Bytes)[] Files()
        => new[] { Entry("StarMark.UI.exe", 15), Entry("a/lib.dll", 3) };

    private static (string Path, byte[] Bytes) Entry(string path, int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)(i * 31 + 7);
        return (path, bytes);
    }

    /// <summary>
    /// 真 zip 落到 <see cref="_payload"/>、清单<b>照此刻的字节</b>签、按测试钥匙审，
    /// 交棒那一步只记参数不起进程。<paramref name="manifestEntries"/> 与 <paramref name="entries"/>
    /// 不一致就是"清单与包不是一套"那种事故形状。
    /// </summary>
    private (UpdateApplier Applier, UpdateApplier.Options Options) Fixture(
        (string Path, byte[] Bytes)[] entries,
        string localVersion = Local,
        List<(string Exe, IReadOnlyList<string> Args)>? startRunner = null,
        (string Path, byte[] Bytes)[]? manifestEntries = null)
    {
        WriteZip(_payload, entries);
        using var key = new TestKey();
        var manifest = Signed(manifestEntries ?? entries, _payload);
        var package = Bundle(key, manifest, _payload);
        var anchor = key.Anchor;
        var sink = startRunner ?? new List<(string Exe, IReadOnlyList<string> Args)>();

        var applier = new UpdateApplier(new FixedSource(
                new PackageFetchResult(PackageFetchStatus.Fetched, package, null)),
            installDir: () => _install, localVersion: () => localVersion, environment: () => Repository);
        var options = new UpdateApplier.Options
        {
            InspectPackage = (p, repo, tag, local) => UpdateIntegrity.Inspect(p, repo, tag, local, anchor),
            Launcher = new UpdaterLauncher.Options
            {
                StartRunner = (exe, args) => sink.Add((exe, args)),
                RunnerSourceOverride = _updaterSource,
                RunnerHomeOverride = _runnerHome,
            },
        };
        return (applier, options);
    }

    private IUpdatePackageSource SignedSource()
    {
        WriteZip(_payload, Files());
        using var key = new TestKey();
        return new FixedSource(new PackageFetchResult(PackageFetchStatus.Fetched,
            Bundle(key, Signed(Files(), _payload), _payload), null));
    }

    /// <summary>
    /// 定值取包装置。<b>它自己"下"两格</b>（<paramref name="progress"/> 每格叫一次）：
    /// 这一格证的不是传输层的节奏（那一半在 <c>GitHubUpdatePackageSourceTests</c> 用真流照），
    /// 而是<b>界面那条回调有没有一路走到取包这一层</b>——Core 要是把 <c>onBytes</c> 掉了，
    /// 这里数出来就是空表，而"进度不显示"那种坏在别处全都看不出来。
    /// </summary>
    private sealed class FixedSource(PackageFetchResult result) : IUpdatePackageSource
    {
        public Task<PackageFetchResult> FetchAsync(PackageAssetAddresses addresses, CancellationToken ct = default,
            Action<DownloadProgress>? progress = null)
        {
            progress?.Invoke(new DownloadProgress(1024, 2048));
            progress?.Invoke(new DownloadProgress(2048, 2048));
            return Task.FromResult(result);
        }
    }

    /// <summary>带计数的取包装置：<b>"没出门"这件事只能由它自己数出来</b>（递一个失败的结局不算证明没被叫）。</summary>
    private sealed class CountingSource(PackageFetchResult result) : IUpdatePackageSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<PackageFetchResult> FetchAsync(PackageAssetAddresses addresses, CancellationToken ct = default,
            Action<DownloadProgress>? progress = null)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(result);
        }
    }

    private static ReleaseManifest Signed((string Path, byte[] Bytes)[] entries, string zipPath)
    {
        var body = File.ReadAllBytes(zipPath);
        return new ReleaseManifest(UpdateAssets.SupportedSchema, Version, UpdateAssets.PackageNameFor(Version),
            HexOf(body), body.LongLength,
            entries.Select(e => new ReleaseManifestFile(e.Path, HexOf(e.Bytes), e.Bytes.LongLength)).ToList());
    }

    private static FetchedPackage Bundle(TestKey key, ReleaseManifest manifest, string zipPath)
    {
        var bytes = UpdateManifestCodec.Write(manifest);
        var body = File.ReadAllBytes(zipPath);
        return new FetchedPackage(bytes, key.Sign(bytes), zipPath, HexOf(body), body.LongLength);
    }

    private static string HexOf(byte[] bytes) => UpdateHashHex.ToHex(SHA256.HashData(bytes));

    /// <summary>只用于"这条链默认信谁"那一格：自造的钥匙在生产入口下必拒。</summary>
    private sealed class TestKey : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public TestKey() => Anchor = UpdateTrustAnchor.FromSpki(
                Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()))
            ?? throw new InvalidOperationException("自造的测试钥匙都不认，那 FromSpki 的判据就是空的");

        public UpdateTrustAnchor Anchor { get; }

        public byte[] Sign(byte[] bytes) =>
            _key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        public void Dispose() => _key.Dispose();
    }

    private static void WriteZip(string path, (string Path, byte[] Bytes)[] entries)
    {
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    private static string[] RelativeNames(string dir) => Directory
        .GetFiles(dir, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
        .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>一颗目录的"内容指纹"：路径与字节各占一半——只比名字看不出内容被动过，只比字节看不出多出什么。</summary>
    private static string ContentOf(string dir) => string.Join("|", Directory
        .GetFiles(dir, "*", SearchOption.AllDirectories)
        .Select(f => $"{Path.GetRelativePath(dir, f).Replace('\\', '/')}={Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(f)))}")
        .OrderBy(x => x, StringComparer.Ordinal));
}
