#nullable enable
using System;
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
/// 摊包那一层的闸门（批次 UG-1）：<b>全部用真临时目录与真 zip 跑</b>。
/// <para>UF 那批欠过一格见证（坑表 #230/#232）：假装置递不到"字节真的到过磁盘"那条分支，于是
/// 一条安全不变量只能报绿。这一层从一开始就没这个选择——它写的就是盘，
/// 所以"失败之后盘上不许留东西"在这里是当场能看的，不是约定。</para>
/// </summary>
public sealed class UpdateStagingTests : IDisposable
{
    private const string Repository = "AH23333/StarMarkDesktop";
    private const string Version = "1.0.1";
    private const string Tag = "v1.0.1";
    private const string Local = "1.0.0";

    private readonly string _root;
    private readonly string _stagingRoot;
    private readonly string _zip;

    public UpdateStagingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "StarMarkStagingTests", Guid.NewGuid().ToString("N"));
        _stagingRoot = Path.Combine(_root, "stage");
        _zip = Path.Combine(_root, UpdateAssets.PackageNameFor(Version));
        Directory.CreateDirectory(_stagingRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* 测试尾巴，不因为它把结论改掉 */ }
    }

    // ===== 成功那一格：摊出来的必须恰好是签名说过的那一堆 =====

    [Fact]
    public async Task TheStagedTreeIsExactlyWhatTheManifestSigned()
    {
        var entries = new[] { File15, Entry("a/lib.dll", 3), Entry("assets/icon.png", 7) };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);
        var package = Bundle(key, manifest, _zip);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), package, _stagingRoot);

        Assert.True(staged.IsStaged);
        Assert.Equal(Path.Combine(_stagingRoot, Version + ".new"), staged.StagedRoot);
        foreach (var (path, bytes) in entries)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(staged.StagedRoot!,
                path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(entries.Length, CountFiles(staged.StagedRoot!));                           // 一颗不多
        Assert.Empty(Directory.GetFiles(staged.StagedRoot!, "*.part", SearchOption.AllDirectories));   // 尾巴不留
    }

    /// <summary>
    /// 清单允许零字节的小文件（真产物里 <c>contentTypes</c>、空 <c>.dat</c> 这一族）——摊出来还是那一颗空文件。
    /// <para>名字不能是 <c>.keep</c>：清单的读法（<c>UpdateManifestCodec</c>）本来就拒"以点开头或以点结尾的末段"，
    /// 这一跑先是把这条判据撞了一遍——判决回的是 <c>FieldRejected</c>，压根到不了摊包。</para>
    /// </summary>
    [Fact]
    public async Task AZeroByteFileInTheManifestStillLandsAsAZeroByteFile()
    {
        var entries = new[] { File15, ("logs/empty.dat", Array.Empty<byte>()) };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot);

        Assert.True(staged.IsStaged, staged.Status + "｜" + staged.Detail);
        Assert.Equal(0, new FileInfo(Path.Combine(staged.StagedRoot!, "logs", "empty.dat")).Length);
    }

    // ===== 入口不收裸清单：没有"可信"判决就没有盘 =====

    /// <summary>
    /// 判决写着 <c>Trusted</c> 却没有清单（<c>Result</c> 的默认值允许这么拼出来）也不算可信——
    /// <see cref="UpdateIntegrity.Result.IsTrusted"/> 把两件事合在一起判，这一格钉的就是那个合取
    /// （#226"析取臂里每一条都要有人答"的同族）。
    /// </summary>
    [Fact]
    public async Task ATrustedVerdictWithoutItsManifestIsNotEnoughToWriteAnything()
    {
        var staged = await UpdateStaging.PrepareAsync(
            new UpdateIntegrity.Result(UpdateIntegrity.Outcome.Trusted), null, _stagingRoot);

        Assert.Equal(StageStatus.NotTrusted, staged.Status);
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>判决说"不可信"的每一种都是同一句话：一个字节都不许写。</summary>
    [Theory]
    [InlineData(UpdateIntegrity.Outcome.SignatureInvalid)]
    [InlineData(UpdateIntegrity.Outcome.ManifestUnreadable)]
    [InlineData(UpdateIntegrity.Outcome.SchemaUnsupported)]
    [InlineData(UpdateIntegrity.Outcome.FieldRejected)]
    [InlineData(UpdateIntegrity.Outcome.VersionMismatch)]
    [InlineData(UpdateIntegrity.Outcome.RollbackRefused)]
    [InlineData(UpdateIntegrity.Outcome.LocalVersionUnknown)]
    [InlineData(UpdateIntegrity.Outcome.PackageHashMismatch)]
    [InlineData(UpdateIntegrity.Outcome.PackageSizeMismatch)]
    public async Task AnyVerdictShortOfTrustedNeverReachesTheDisk(UpdateIntegrity.Outcome outcome)
    {
        var entries = new[] { Entry("StarMark.UI.exe", 4) };
        WriteZip(_zip, entries);
        var manifest = Signed(_zip, entries);
        using var key = new TestKey();

        var staged = await UpdateStaging.PrepareAsync(new UpdateIntegrity.Result(outcome, manifest),
            Bundle(key, manifest, _zip), _stagingRoot);

        Assert.Equal(StageStatus.NotTrusted, staged.Status);
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>判决在手、包却是空的：也不能凭空摊出一棵树来。</summary>
    [Fact]
    public async Task AMissingPayloadIsRefusedEvenWithAGoodVerdict()
    {
        var entries = new[] { Entry("StarMark.UI.exe", 4) };
        WriteZip(_zip, entries);
        using var key = new TestKey();

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, Signed(_zip, entries), _zip), null, _stagingRoot);

        Assert.Equal(StageStatus.NotTrusted, staged.Status);
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    // ===== 逐颗对账：这一层的存在理由 =====

    /// <summary>
    /// <b>UF 那格证人立的同一族规矩，在这里是核心</b>：判决与包都在手上了，但载荷在判决<em>之后</em>被换过
    /// （临时目录本机可写，"先验后摊"之间完全可能被换）。整包哈希在那一刻已经是过去时，
    /// 而每一颗文件对回清单是现在时——所以这里必须是 <c>FileHashMismatch</c>，且半棵树不许留下。
    /// </summary>
    [Fact]
    public async Task APayloadSwappedAfterTheVerdictIsCaughtByThePerFileHash()
    {
        var entries = new[] { File15, Entry("a/lib.dll", 3) };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);
        var package = Bundle(key, manifest, _zip);
        var verdict = Verdict(key, manifest, _zip);
        Assert.True(verdict.IsTrusted);

        WriteZip(_zip, new[] { Entry("StarMark.UI.exe", 80), Entry("a/lib.dll", 3) });      // 判决之后换掉其中一颗

        var staged = await UpdateStaging.PrepareAsync(verdict, package, _stagingRoot);

        Assert.Equal(StageStatus.FileHashMismatch, staged.Status);
        Assert.Contains("StarMark.UI.exe", staged.Detail, StringComparison.Ordinal);          // 说得出是哪一颗
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));                 // 半棵树不留
    }

    /// <summary>清单多签了一颗、包里没有：这是"签的内容与装的东西不是一套"，整棵树都不许留。</summary>
    [Fact]
    public async Task AFileTheManifestListedButThePackageOmitsRefusesTheWholeTree()
    {
        var entries = new[] { File15, Entry("a/lib.dll", 3) };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var signed = Signed(_zip, entries);
        var lied = signed with
        {
            Files = signed.Files.Append(new ReleaseManifestFile("b/extra.dll", HexOf(new byte[] { 1, 2 }), 2)).ToList(),
        };

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, lied, _zip), Bundle(key, lied, _zip), _stagingRoot);

        Assert.Equal(StageStatus.FileMissing, staged.Status);
        Assert.Contains("b/extra.dll", staged.Detail, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>包里有清单没记的字节：不知道是谁放的，也就不许跟着进安装目录。</summary>
    [Fact]
    public async Task BytesTheManifestNeverSignedAreNotAllowedIntoTheTree()
    {
        var entries = new[] { File15 };
        WriteZip(_zip, entries);
        AddEntry(_zip, "surprise.dll", new byte[] { 9, 9, 9 });              // 只在包里，不在清单里
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot);

        Assert.Equal(StageStatus.UnexpectedEntry, staged.Status);
        Assert.Contains("1 颗", staged.Detail, StringComparison.Ordinal);      // 说得清多出来几颗
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>重名条目不当成"挑一颗算哈希"：清单对一颗路径只有一个答案，包里有两份就是这颗包自己不一致。</summary>
    [Fact]
    public async Task ADuplicateEntryNameIsRefusedRatherThanPickedFrom()
    {
        var entries = new[] { File15 };
        WriteZip(_zip, entries);
        AddEntry(_zip, "StarMark.UI.exe", new byte[] { 8, 8 });
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot);

        Assert.Equal(StageStatus.UnexpectedEntry, staged.Status);
        Assert.Contains("重名", staged.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// 条目名里那些 <c>../</c> 与盘符<b>进不了路径拼接</b>：写出目标只出自清单（签名背书的内容），
    /// 包里的名字只用来查表。这一格要求"树外面一颗都没多出来"——Zip Slip 那一族在这里的正面处置。
    /// </summary>
    [Fact]
    public async Task AnEntryNamedLikeAnEscapeNeverBecomesTheWriteTarget()
    {
        var entries = new[] { File15 };
        WriteZip(_zip, entries);
        AddEntry(_zip, "../../evil.dll", new byte[] { 4 });
        AddEntry(_zip, "C:/abs.dll", new byte[] { 5 });
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot);

        Assert.Equal(StageStatus.UnexpectedEntry, staged.Status);
        Assert.False(File.Exists(Path.Combine(_root, "evil.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "abs.dll")));
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>目录项（以 <c>/</c> 结尾、不带字节）不算"清单没列的文件"，否则用资源管理器压的包都会被白拒一次。</summary>
    [Fact]
    public async Task DirectoryEntriesAreNotCountedAsUnlistedBytes()
    {
        var entries = new[] { File15 };
        WriteZip(_zip, entries);
        AddEntry(_zip, "assets/", Array.Empty<byte>());
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot);

        Assert.True(staged.IsStaged);
        Assert.Equal(1, CountFiles(staged.StagedRoot!));
    }

    // ===== 包本身坏了与本机写不下去：两句话不许互串 =====

    [Fact]
    public async Task APayloadThatIsNotAZipSaysThePackageIsBrokenRatherThanTheDisk()
    {
        var entries = new[] { File15 };
        File.WriteAllBytes(_zip, new byte[64]);                               // 尺寸像样，内容不是压缩包
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);                                // 清单照此刻的字节签：整包哈希是过的

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot);

        Assert.Equal(StageStatus.PackageUnreadable, staged.Status);
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>
    /// 暂存位置那里已经站着一颗同名文件（或它的父级是文件）：说"本机写不下去"并带上因由，
    /// 而不是把异常抛到界面上——<c>stagingRoot</c> 是外面给的，%TEMP% 被人改过就什么都能出现（#230 同口径）。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnusableStagingRootSaysDiskRatherThanCrashing(bool rootIsInsideAFile)
    {
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "我是一颗文件");
        var entries = new[] { File15 };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);

        var staged = await UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip),
            rootIsInsideAFile ? Path.Combine(blocked, "sub") : blocked);

        Assert.Equal(StageStatus.DiskWriteFailed, staged.Status);
        Assert.False(string.IsNullOrEmpty(staged.Detail));                     // 要带"从哪儿看出来的"
    }

    /// <summary>取消不是错误：原样交回，但半棵树照样删（不变量不许跟着措辞一起漏，#230）。</summary>
    [Fact]
    public async Task ACancelledStageHandsTheCancellationBackAndLeavesNoTree()
    {
        var entries = new[] { File15 };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateStaging.PrepareAsync(Verdict(key, manifest, _zip), Bundle(key, manifest, _zip), _stagingRoot, cts.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_stagingRoot));
    }

    /// <summary>上一次失败留下的半截树不算"已经摊好"：下一次成功之前先清干净（自愈，不叫用户删目录，P-54 同口径）。</summary>
    [Fact]
    public async Task TheTreeLeftByAFailedAttemptIsCleanedBeforeTheNextOneStages()
    {
        var entries = new[] { File15, Entry("a/lib.dll", 3) };
        WriteZip(_zip, entries);
        using var key = new TestKey();
        var manifest = Signed(_zip, entries);
        var package = Bundle(key, manifest, _zip);
        var verdict = Verdict(key, manifest, _zip);

        WriteZip(_zip, new[] { Entry("StarMark.UI.exe", 77), Entry("a/lib.dll", 3) });        // 先制造一次失败
        Assert.Equal(StageStatus.FileHashMismatch, (await UpdateStaging.PrepareAsync(verdict, package, _stagingRoot)).Status);

        WriteZip(_zip, entries);                                                              // 再把包修回真值
        var fresh = Signed(_zip, entries);
        var staged = await UpdateStaging.PrepareAsync(Verdict(key, fresh, _zip), Bundle(key, fresh, _zip), _stagingRoot);

        Assert.True(staged.IsStaged);
        Assert.Equal(entries.Length, CountFiles(staged.StagedRoot!));
        Assert.Empty(Directory.GetFiles(staged.StagedRoot!, "*.part", SearchOption.AllDirectories));
    }

    // ===== 措辞与射程 =====

    public static TheoryData<StageStatus> EveryStageStatus()
    {
        var data = new TheoryData<StageStatus>();
        foreach (StageStatus value in Enum.GetValues<StageStatus>()) data.Add(value);
        return data;
    }

    /// <summary>
    /// 结局有几格，句子就有几句、各不相同（重合就说明有两格被并成了一句，而它们的处置并不相同）。
    /// 光有这一条是不够的，见 <see cref="EveryStageStatusHasItsOwnArmInUpdatePolicy"/>。
    /// </summary>
    [Fact]
    public void TheStageSentencesAreAsManyDistinctOnesAsThereAreCauses()
        => Assert.Equal(Enum.GetValues<StageStatus>().Length,
            Enum.GetValues<StageStatus>().Select(UpdatePolicy.Describe).Distinct(StringComparer.Ordinal).Count());

    /// <summary>
    /// 每一格在 <c>UpdatePolicy.Describe</c> 里必须有<b>自己的那条臂</b>。
    /// 上一格测不到的是真正的事故形状：新增一格而忘了配句子，它从兜底臂拿到的那句"更新包没摊开，没装"
    /// 与其余各句依然各不相同 ⇒ 只比句数的那格会绿着放过去（绿得没有因果关系，#211/#226 同一族）。
    /// 兜底臂本身是要留的（<c>(StageStatus)99</c> 这种不在枚举里的值总得有一句话），
    /// 但它不许接走任何一格真的因由。
    /// </summary>
    [Fact]
    public void EveryStageStatusHasItsOwnArmInUpdatePolicy()
    {
        var policy = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs"));
        foreach (StageStatus value in Enum.GetValues<StageStatus>())
            Assert.Contains($"StageStatus.{value} =>", policy, StringComparison.Ordinal);
    }

    /// <summary>每一格说自己那一句；而"没摊成"的每一句都不许暗示任何东西被装上了。</summary>
    [Theory]
    [MemberData(nameof(EveryStageStatus))]
    public void NoRefusalSentenceClaimsAnythingWasInstalled(StageStatus status)
    {
        var text = UpdatePolicy.Describe(status);
        Assert.False(string.IsNullOrWhiteSpace(text));
        if (status == StageStatus.Staged) return;

        // 这一层只摊文件：它既没起进程，也没碰安装目录，所以这几句今天说出来就是谎
        Assert.DoesNotContain("已安装", text, StringComparison.Ordinal);
        Assert.DoesNotContain("替换完成", text, StringComparison.Ordinal);
        Assert.DoesNotContain("重启后生效", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 摊包这一层的射程：只写暂存树。<b>起进程、挪安装目录、就地替换都不在这一层</b>（那是 UG-2 的更新器）；
    /// 而 <c>ExtractToDirectory</c> 在这一族里是禁项——它会把没对过账的字节先落盘，
    /// 于是"逐颗哈希"这一整个关卡被一次调用绕过（同一口径见 UF 的 <c>ThisLayerNeverInstallsAnything</c>）。
    /// </summary>
    [Fact]
    public void ThisLayerOnlyWritesTheStagingTree()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateStaging.cs"));
        foreach (var banned in new[]
                 {
                     "ExtractToDirectory", "Process.Start", "Directory.Move", "File.Replace",
                     "AppContext.BaseDirectory",
                 })
            Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
    }

    /// <summary>
    /// "入口的第一个参数必须是判决"——这条不是风格：能绕过判决就没有判决。
    /// 用读源码的方式钉住，是因为它坏了以后每格测都可能照样绿（签名对得上、哈希也对得上，只是没人验过）。
    /// </summary>
    [Fact]
    public void StagingCannotBeFedABareManifest()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateStaging.cs"));
        Assert.Contains("Task<StageResult> PrepareAsync(", code, StringComparison.Ordinal);
        Assert.Contains("UpdateIntegrity.Result verdict", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareAsync(ReleaseManifest", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// "包里那颗叫什么、算不算一颗文件"这颗判据<b>全仓只许有一个定义，两侧都得引用它</b>：
    /// 发布机与摊包各写一份，就会漂成"签出来的名字"与"查表用的名字"不是一把，
    /// 表现是每一颗都报"清单列了、包里没有"，于是全线更新不可用（#189/#193）。
    /// <para>针法刻意钉在<b>这个名字的定义＋那两处的引用</b>上，而不是钉"谁写了 <c>Replace('\\', '/')</c>"——
    /// 后者在 <c>LocalFileIdentity</c> 与 <c>DittoSource</c> 里是为别的事写的，拿它当全仓普查会把无关代码误判成违规
    /// （假失败最坏：它教人把闸门改松而不是改对，#123）。</para>
    /// </summary>
    [Fact]
    public void TheEntryNameRuleIsDefinedOnceAndUsedByBothSides()
    {
        var files = ReadRepoUnder("src").Concat(ReadRepoUnder("tools"))
            .Select(f => (f.RelativePath, Text: Code(f.Text))).ToList();

        var definitions = files
            .Where(f => f.Text.Contains("static string NormalizeEntryName(", StringComparison.Ordinal)).ToList();
        Assert.Single(definitions);
        Assert.Equal("src/StarMark.Abstractions/Updates/UpdatePackage.cs", definitions[0].RelativePath);

        foreach (var consumer in new[] { "src/StarMark.Core/Updates/UpdateStaging.cs", "tools/StarMark.UpdateSigner/Program.cs" })
        {
            var text = files.Single(f => f.RelativePath == consumer).Text;
            Assert.Contains("UpdateAssets.NormalizeEntryName(", text, StringComparison.Ordinal);
            Assert.DoesNotContain(@"Replace('\\', '/')", text, StringComparison.Ordinal);   // 不许再手写一遍
        }
    }

    // ===== 小工具 =====

    private static readonly (string Path, byte[] Bytes) File15 = Entry("StarMark.UI.exe", 15);

    private static (string Path, byte[] Bytes) Entry(string path, int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)(i * 31 + 7);
        return (path, bytes);
    }

    private static string HexOf(byte[] bytes) => UpdateHashHex.ToHex(SHA256.HashData(bytes));

    private static string ReplaceNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void WriteZip(string path, (string Path, byte[] Bytes)[] entries)
    {
        if (File.Exists(path)) File.Delete(path);       // Create 模式不许目标已存在（"判决之后把载荷换掉"那几格要重压一次）
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    private static void AddEntry(string zipPath, string name, byte[] bytes)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(bytes);
    }

    /// <summary>清单<b>照此刻包里的东西</b>签：颗数、名字、哈希、字节数全对得上。</summary>
    private static ReleaseManifest Signed(string zipPath, (string Path, byte[] Bytes)[] entries)
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

    private static UpdateIntegrity.Result Verdict(TestKey key, ReleaseManifest manifest, string zipPath)
        => UpdateIntegrity.Inspect(Bundle(key, manifest, zipPath), Repository, Tag, Local, key.Anchor);

    private static int CountFiles(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;

    /// <summary>测试自造的 P-256 钥匙对（发布那把私钥不入库，也不进任何测试）。</summary>
    private sealed class TestKey : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public TestKey()
            => Anchor = UpdateTrustAnchor.FromSpki(Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()))
                ?? throw new InvalidOperationException("自造的测试钥匙都不认，那 FromSpki 的判据就是空的");

        public UpdateTrustAnchor Anchor { get; }

        public byte[] Sign(byte[] bytes) => _key.SignData(bytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        public void Dispose() => _key.Dispose();
    }
}
