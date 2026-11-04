#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;
using StarMark.Integrations.Everything;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// P-145 (A) 的证人与闸门：Everything 三处下载点的「下载点验」。
/// <para>账本那颗缺陷的形状：SDK zip / 安装器 / 便携包都走 https 直连下载后<b>直接放行</b>——
/// 中间任何一环（投毒镜像、CDN 错件、非原子写的截断包）都能把一颗「看起来是 Everything」的
/// 可执行文件送进 <c>Process.Start</c>，而全库没有任何一路读数能分辨「下到的是不是 voidtools 发的那颗」。</para>
/// <para>修法走 (A)：URL 带版本号的两包<b>硬钉 SHA-256</b>（内容钉死）；滚动的 SDK zip 验解出 DLL 的
/// Authenticode 且签名者是 voidtools（「内容会滚、签名者不会滚」）。行为①～⑥＋形状①～④⑥ 钉的是这一层。</para>
/// <para>片203 修批（P-145 真机三缺陷，坑 #1141/#1142）：行为⑦＋形状⑤′⑦⑧⑨——「先松手再验」
/// （验签那一刻起 DLL 与 zip 都没人握着，⛔ 握着写句柄去 <c>WinVerifyTrust</c>＝0x80092003 假阴性）、
/// 「删除核销」（<c>TryDelete</c> 复核「此刻在不在」，三臂日志 ⛔ 无条件照写「已删」）、
/// EnableAsync 两臂结果都接回就绪（丢弃 SDK 臂＝「验过了」与「就绪了」断链）。</para>
/// </summary>
public sealed class EverythingDownloadTrustTests : IDisposable
{
    private const string QueuePath = "src/StarMark.Integrations/Everything/EverythingQueryQueue.cs";
    private const string TrustPath = "src/StarMark.Integrations/Everything/EverythingDownloadTrust.cs";

    private static readonly Regex Hex64 = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    private readonly string _tmp;

    public EverythingDownloadTrustTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), $"starmark_p145_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private string TempFile(byte[] bytes)
    {
        var p = Path.Combine(_tmp, $"f_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(p, bytes);
        return p;
    }

    /// <summary>正件证人：.NET 共享运行时目录里的 coreclr.dll——微软内嵌 Authenticode 签名
    /// （System32 的 notepad/explorer 是「目录签名」，WinVerifyTrust 文件模式与
    /// <c>CreateFromSignedFile</c> 都不认，不能当正件）；从 <c>typeof(object)</c> 所在目录定位，
    /// 测试跑在 <c>dotnet test</c> 下必然存在，不依赖网络、不依赖机器布局。</summary>
    private static string CoreClrPath
        => Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "coreclr.dll");

    /// <summary>全 <c>src\</c> 手写源码（抹注释）里某串的出现次数——「唯一出处」类闸门的尺。</summary>
    private static int CountInSrcCode(string needle)
        => ReadRepoUnder("src").Sum(f => Count(Code(f.Text), needle));

    /// <summary>行为①：SHA-256 比对跟内容走、与书写时的大小写无关——一位之差＝验不过。</summary>
    [Fact]
    public void VerifySha256FollowsTheContentWhateverHexCaseYouWriteItIn()
    {
        var path = TempFile("abc"u8.ToArray());
        const string hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        Assert.True(EverythingDownloadTrust.VerifySha256(path, hash));
        Assert.True(EverythingDownloadTrust.VerifySha256(path, hash.ToLowerInvariant()));   // hex 大小写不敏感
        Assert.False(EverythingDownloadTrust.VerifySha256(path, hash[..^1] + "0"));          // 末位之差＝验不过
    }

    /// <summary>行为②：内容被动过一位就换不来那句「过」——被投毒/截断的包全靠这一格拦。</summary>
    [Fact]
    public void VerifySha256RejectsTamperedContent()
    {
        var path = TempFile("abc"u8.ToArray());
        var wrong = Convert.ToHexString(SHA256.HashData("abd"u8.ToArray()));   // 别的内容的哈希
        Assert.False(EverythingDownloadTrust.VerifySha256(path, wrong));
    }

    /// <summary>行为③：微软内嵌签的 coreclr.dll 过 WinVerifyTrust——验签器对着真签名是通的（正件证人，不依赖网络）。</summary>
    [Fact]
    public void IsAuthenticodeTrustedAcceptsTheMicrosoftSignedCoreClr()
    {
        var coreclr = CoreClrPath;
        Assert.True(File.Exists(coreclr), $"测试环境缺 .NET 运行时的 coreclr.dll（正件证人失锚，先修环境）：{coreclr}");
        Assert.True(EverythingDownloadTrust.IsAuthenticodeTrusted(coreclr));
    }

    /// <summary>行为④：无签名文件验不过——随机字节写出来的临时文件没有 Authenticode。</summary>
    [Fact]
    public void IsAuthenticodeTrustedRejectsAnUnsignedFile()
    {
        var path = TempFile(Guid.NewGuid().ToByteArray());
        Assert.False(EverythingDownloadTrust.IsAuthenticodeTrusted(path));
    }

    /// <summary>行为⑤：签名者读得出来（coreclr 读到 Microsoft）——「是谁签的」这层判据有真数据喂着，
    /// ⛔ 只是字符串比较的空转。</summary>
    [Fact]
    public void TheSignerSubjectIsReadableWithoutDependingOnVoidtools()
    {
        var subject = EverythingDownloadTrust.TryGetSignerSubject(CoreClrPath);
        Assert.NotNull(subject);
        Assert.Contains("Microsoft", subject!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>行为⑥：签名有效但<b>签的不是 voidtools</b>照样拒——只验链不验人＝任何一张正经证书
    /// 都能冒充；这颗负证拿 coreclr（微软签、链有效）当「错的人」，不依赖网络。
    /// 无签名那路顺带验 null 兜底（<c>TryGetSignerSubject</c> 对无签名文件回 null，不抛）。</summary>
    [Fact]
    public void IsVoidtoolsSignedRejectsASignedButWrongSignerAndAnUnsignedFile()
    {
        var coreclr = CoreClrPath;
        Assert.True(File.Exists(coreclr), $"测试环境缺 .NET 运行时的 coreclr.dll（正件证人失锚，先修环境）：{coreclr}");
        Assert.False(EverythingDownloadTrust.IsVoidtoolsSigned(coreclr));
        Assert.Null(EverythingDownloadTrust.TryGetSignerSubject(TempFile(Guid.NewGuid().ToByteArray())));
    }

    /// <summary>行为⑦（坑 #1141）：删除核销的三态读数——被握着删不动＝如实回 false（⛔ 假报「已删」）、
    /// 松手后删净＝true、本来就不存在＝true（「此刻不在」就是目标态）。</summary>
    [Fact]
    public void TryDeleteReportsWhetherTheFileIsReallyGone()
    {
        var held = TempFile("held"u8.ToArray());
        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.None))   // 同锁现场：删不动的那一把
        {
            Assert.False(EverythingDownloadTrust.TryDelete(held));
            Assert.True(File.Exists(held));
        }
        Assert.True(EverythingDownloadTrust.TryDelete(held));                     // 松手后删净
        Assert.False(File.Exists(held));
        Assert.True(EverythingDownloadTrust.TryDelete(Path.Combine(_tmp, "never-existed.bin")));
    }

    /// <summary>形状①：两颗钉住的哈希都得是 64 位 hex——写漏/写歪一位（63 位、带空格）在这里现形。</summary>
    [Fact]
    public void TheTwoPinnedHashesAreSixtyFourHexDigits()
    {
        Assert.Matches(Hex64, EverythingDownloadTrust.InstallerSha256);
        Assert.Matches(Hex64, EverythingDownloadTrust.PortableZipSha256);
    }

    /// <summary>形状②：每颗钉住的哈希全 src 恰一处出处——第二处迟早改不同步，
    /// 两把尺各放各的行＝点验在两处各说各话。</summary>
    [Fact]
    public void EachPinnedHashHasExactlyOneHomeInSource()
    {
        Assert.Equal(1, CountInSrcCode(EverythingDownloadTrust.InstallerSha256));
        Assert.Equal(1, CountInSrcCode(EverythingDownloadTrust.PortableZipSha256));
    }

    /// <summary>形状③：三条下载 URL 各自全 src 恰一处出处——URL 与哈希/验法必须同刀同改，
    /// 拷去第二处＝升版本时漏改的那一处把钉好的哈希对着新包验（恒不过＝功能坏死）。</summary>
    [Fact]
    public void EachDownloadUrlHasExactlyOneOutletInSource()
    {
        Assert.Equal(1, CountInSrcCode("https://www.voidtools.com/Everything-1.4.1.1028.x64-Setup.exe"));
        Assert.Equal(1, CountInSrcCode("https://www.voidtools.com/Everything-1.4.1.1024.x64.zip"));
        Assert.Equal(1, CountInSrcCode("https://www.voidtools.com/Everything-SDK.zip"));
    }

    /// <summary>形状④：三处下载点全接上各自的验——安装器/便携包按钉死的哈希、SDK 按 voidtools 签名；
    /// 且安装器那臂的验在 <c>Process.Start</c> <b>之前</b>（验在后面＝先执行了再发现不对）。</summary>
    [Fact]
    public void AllThreeDownloadPointsVerifyBeforeUse()
    {
        var code = Code(ReadRepoFile(QueuePath));
        Assert.Equal(1, Count(code, "EverythingDownloadTrust.VerifySha256(installer,"));
        Assert.Equal(1, Count(code, "EverythingDownloadTrust.VerifySha256(zip,"));
        Assert.Equal(1, Count(code, "EverythingDownloadTrust.IsVoidtoolsSigned(sdkPath)"));

        var installerArm = MethodBody(ReadRepoFile(QueuePath), "public async Task<bool> EnsureEverythingInstalledAsync()");
        Assert.True(
            installerArm.IndexOf("VerifySha256", StringComparison.Ordinal)
                < installerArm.IndexOf("ProcessStartInfo", StringComparison.Ordinal),
            "安装器的哈希验必须排在 ProcessStartInfo 之前");
    }

    /// <summary>形状⑤（坑 #1141 改口径）：SDK 那臂删净才走——验不过时 DLL 与 zip 都得<b>删除核销</b>：
    /// <c>TryDelete</c> 回「此刻在不在」，⛔ 裸 <c>File.Delete</c> 被吞（可疑 DLL 留在缓存目录＝
    /// 下次 <c>File.Exists</c> 直接跳过下载与验签，点验被自己的缓存短路）。</summary>
    [Fact]
    public void TheSdkArmDeletesBothTheDllAndTheZipOnTrustFailure()
    {
        var arm = MethodBody(ReadRepoFile(QueuePath), "public async Task<bool> EnsureSdkReadyAsync()");
        Assert.Contains("EverythingDownloadTrust.TryDelete(sdkPath)", arm, StringComparison.Ordinal);
        Assert.Contains("EverythingDownloadTrust.TryDelete(zip)", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete(sdkPath)", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete(zip)", arm, StringComparison.Ordinal);
    }

    /// <summary>形状⑥：WinVerifyTrust 的 P/Invoke 声明恰一处——第二份声明＝第二颗 GUID/布局，
    /// 改布局时只改到一半的那颗就永远验不过。</summary>
    [Fact]
    public void WinVerifyTrustIsDeclaredExactlyOnce()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(TrustPath)), "private static extern int WinVerifyTrust("));
    }

    /// <summary>形状⑦（坑 #1141）：SDK 臂「先松手再验」——写 DLL 的 <c>File.Create</c> 句柄与
    /// ZipArchive 握 zip 的读句柄都必须收在 using <b>块</b>里、收在验签之前；
    /// ⛔ <c>using var</c> 跨过 <c>IsVoidtoolsSigned</c>：握着自己没关的句柄去验，
    /// 系统按「文件被占用」回 0x80092003＝假阴性。</summary>
    [Fact]
    public void TheSdkArmReleasesEveryHandleBeforeVerifying()
    {
        var arm = MethodBody(ReadRepoFile(QueuePath), "public async Task<bool> EnsureSdkReadyAsync()");
        Assert.Equal(1, Count(arm, "using (var archive = OpenCachedZip(zip))"));
        Assert.Equal(0, Count(arm, "using var archive = OpenCachedZip(zip)"));
        Assert.Equal(1, Count(arm, "using (var dst = File.Create(sdkPath))"));
        Assert.Equal(0, Count(arm, "using var dst = File.Create"));
    }

    /// <summary>形状⑧（坑 #1141）：删除核销铺满三臂——安装器／便携包臂的失败路径同样走
    /// <c>TryDelete</c>，⛔ 裸 <c>File.Delete</c> 被吞后又无条件写「已删包」。</summary>
    [Fact]
    public void TheInstallerAndPortableArmsAlsoDeleteWithVerification()
    {
        var code = ReadRepoFile(QueuePath);
        var installerArm = MethodBody(code, "public async Task<bool> EnsureEverythingInstalledAsync()");
        var portableArm = MethodBody(code, "private static async Task<string?> EnsureOwnedEverythingAsync(CancellationToken ct)");

        Assert.Contains("EverythingDownloadTrust.TryDelete(installer)", installerArm, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete(installer)", installerArm, StringComparison.Ordinal);
        Assert.Equal(2, Count(portableArm, "EverythingDownloadTrust.TryDelete(zip)"));   // 失败臂核销＋解压完清包
        Assert.DoesNotContain("File.Delete(zip)", portableArm, StringComparison.Ordinal);
    }

    /// <summary>形状⑨（坑 #1142）：EnableAsync 两臂的结果都接回就绪——SDK 臂验过了没接上＝
    /// 「验过了」与「就绪了」断链；丢弃那一臂的 bool 是静默半瘫，比抛异常更难查。</summary>
    [Fact]
    public void EnableAsyncWiresBothArmResultsIntoTheOutcome()
    {
        var body = MethodBody(ReadRepoFile(QueuePath),
            "public Task<LocalDiskSearchEnableOutcome> EnableAsync(CancellationToken ct)");
        Assert.Equal(1, Count(body, "EnsureSdkReadyAsync"));   // ⛔ 回到「await 完不接回」的旧病
        Assert.Contains("var sdkReady = await EnsureSdkReadyAsync().ConfigureAwait(false);", body, StringComparison.Ordinal);
        Assert.Contains("var ownedRunning = await EnsureOwnedRunningAsync(ct).ConfigureAwait(false);", body, StringComparison.Ordinal);
        Assert.Contains("sdkReady && ownedRunning", body, StringComparison.Ordinal);
    }
}
