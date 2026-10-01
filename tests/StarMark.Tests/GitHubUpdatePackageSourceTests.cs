#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using StarMark.Integrations.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 取包那一步的<b>传输纪律</b>（批次 UF）。它不判信任（那些在 <see cref="UpdateIntegrityTests"/>），
/// 它只管三件事，而每一件都有对应的坏形状：
/// ① <b>只发我们写出来的那几个地址</b>——多一发、跳去别宿主的那一发，都是把下载源交出去；
/// ② <b>一个字节的凭据都不带上这条会换宿主的路</b>；
/// ③ <b>落盘只落在指定的那一个临时目录</b>，半途而废的尾巴不能留到下一次。
/// </summary>
public sealed class GitHubUpdatePackageSourceTests : IDisposable
{
    private const string Repository = "AH23333/StarMarkDesktop";
    private const string Tag = "v1.0.1";

    private readonly string _staging = Path.Combine(Path.GetTempPath(), "uf_transport_" + Guid.NewGuid().ToString("N"));
    private readonly PackageAssetAddresses _addresses = UpdatePolicy.ReleaseAssetAddresses(Repository, Tag)!;

    private static readonly byte[] ManifestBytes = Encoding.UTF8.GetBytes("{\"schema\":1}");
    private static readonly byte[] SignatureBytes = Enumerable.Repeat((byte)0x30, 71).ToArray();
    private static readonly byte[] PayloadBytes = Encoding.UTF8.GetBytes("载荷的那堆字节");

    [Fact]
    public void TheAddressesComeFromCoreAndAreRequestedInThatOrder()
    {
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.Fetched, result.Status);
        Assert.Equal(
            new[] { _addresses.ManifestUrl, _addresses.SignatureUrl, _addresses.PackageUrl },
            transport.Calls.Select(c => c.Url).ToArray());
    }

    /// <summary>载荷那颗的名字出自标签派生的约定，而不是对方回的任何一个字段。</summary>
    [Fact]
    public void ThePackageIsFetchedByNameDerivedFromTheTagNotFromAnyRemoteString()
    {
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        Run(source);
        var packageUrl = transport.Calls[^1].Url;
        Assert.EndsWith($"/releases/download/{Tag}/{UpdateAssets.PackageNameFor("1.0.1")}", packageUrl, StringComparison.Ordinal);
        Assert.StartsWith("https://github.com/", packageUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// 这一条是凭据面：整条链上换了宿主、还会跳，所以一发都不许带 Authorization。
    /// 判据取"真发出去的那个请求"而不是"没配 Token"（#227 那一族：假传输要断言的是实际发出去的东西）。</summary>
    [Fact]
    public void NotOneRequestOnThisChainCarriesACredential()
    {
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        Run(source);
        Assert.All(transport.Calls, call => Assert.Null(call.Auth));
        Assert.DoesNotContain("Authorization", Code(ReadRepoFile("src/StarMark.Integrations/Updates/GitHubUpdatePackageSource.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheBytesAreHashedWhileTheyStreamInAndTheFileLandsWhereWeWereTold()
    {
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        var package = result.Package!;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(PayloadBytes)).ToLowerInvariant(), package.PackageSha256);
        Assert.Equal(PayloadBytes.LongLength, package.PackageBytes);
        Assert.Equal(PayloadBytes, File.ReadAllBytes(package.PackagePath));
        Assert.Equal(_staging, Path.GetDirectoryName(package.PackagePath));
        // 临时那颗的名字是我们自己生成的 GUID，不是标签/资产名派生的：换宿主、换标签都不能把它指到别处去
        Assert.Matches("^[0-9a-f]{32}\\.zip$", Path.GetFileName(package.PackagePath));
    }

    /// <summary>取成了就是一颗 <c>.zip</c>；<c>.part</c> 那半截不许活过这一次调用。</summary>
    [Fact]
    public void TheHalfWrittenTailNeverSurvivesASuccessfulFetch()
    {
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        Run(source);
        Assert.Empty(Directory.GetFiles(_staging, "*.part"));
    }

    [Fact]
    public void AStagingDirectoryThatCannotBeWrittenSaysSoRatherThanThrowing()
    {
        var unusable = Path.Combine(_staging, new string('\u0001', 4));       // 合法路径字符之外的东西
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => unusable);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.DiskWriteFailed, result.Status);
        Assert.False(File.Exists(result.Detail));                              // Detail 是异常类型名，不是路径
        Assert.EndsWith("Exception", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(PackageAssetAddresses.ManifestUrl), 1)]
    [InlineData(nameof(PackageAssetAddresses.SignatureUrl), 2)]
    [InlineData(nameof(PackageAssetAddresses.PackageUrl), 3)]
    public void AMissingAssetSaysSoAndStopsSpendingTheRestOfTheBandwidth(string which, int callsExpected)
    {
        var target = Address(which);
        using var transport = new FakeTransport(url => url == target
            ? Response(HttpStatusCode.NotFound, Array.Empty<byte>())
            : Small(url));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.AssetMissing, result.Status);
        Assert.Equal(callsExpected, transport.Calls.Count);
    }

    [Fact]
    public void ARedirectIntoTheGithubAssetCdnIsFollowedAndStillDeliversTheSameBytes()
    {
        var cdn = "https://objects.githubusercontent.com/whatever/" + UpdateAssets.PackageNameFor("1.0.1");
        using var transport = new FakeTransport(url => url switch
        {
            var u when u == _addresses.PackageUrl => Redirect(cdn),
            var u when u == cdn => Response(HttpStatusCode.OK, PayloadBytes),
            _ => Small(url),
        });
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.Fetched, result.Status);
        Assert.Equal(cdn, transport.Calls[^1].Url);
    }

    [Theory]
    [InlineData("https://evil.test/package.zip")]                                  // 换宿主
    [InlineData("https://objects.githubusercontent.com.evil.test/package.zip")]    // 拿域名当后缀顶过去
    [InlineData("http://objects.githubusercontent.com/package.zip")]               // 从 https 掉档
    [InlineData("https://api.github.com/anything")]                                // 跳回 API 域也不是资产域
    public void ARedirectOffTheAssetPathIsRefusedRatherThanFollowed(string target)
    {
        using var transport = new FakeTransport(url => url == _addresses.PackageUrl
            ? Redirect(target)
            : Small(url));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.OffHostRedirect, result.Status);
        Assert.DoesNotContain(target, transport.Calls.Select(c => c.Url), StringComparer.Ordinal);
    }

    /// <summary>
    /// 第一跳的两道判据<b>不许互相答题</b>：坏宿主那一种两道都会挡（资产域表也挡住 evil.test），
    /// 所以这里必须带一颗"合法宿主、错的位置"的地址——它只有"第一跳必须是 github.com"这一臂能挡。
    /// 少了这一行，把那一臂拆掉没人红（同一形状：#228 的"注释替标题答题"、坑表 #207 的"计数答了归属的问题"）。
    /// </summary>
    [Theory]
    [InlineData("https://evil.test/m")]
    [InlineData("https://objects.githubusercontent.com/releases/download/v1.0.1/update-manifest.json")]
    public async Task AFirstHopThatIsNotTheAssetHostIsRefusedBeforeItIsSent(string manifestUrl)
    {
        var addresses = new PackageAssetAddresses(manifestUrl, _addresses.SignatureUrl, _addresses.PackageUrl);
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = await source.FetchAsync(addresses);

        Assert.Equal(PackageFetchStatus.OffHostRedirect, result.Status);
        Assert.Empty(transport.Calls);                       // 一票都没投出去：判据在发请求之前
    }

    [Fact]
    public void ARelativeRedirectIsResolvedAgainstTheUrlWeAskedFor()
    {
        var relative = "cdn/same-host-package.zip";
        using var transport = new FakeTransport(url => url == _addresses.PackageUrl
            ? Redirect(relative)
            : Response(HttpStatusCode.OK, PayloadBytes));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.Fetched, result.Status);
        // 相对跳转按"我们刚问的那一颗"解析（基目录换掉最后一段），不是拼到根上去
        Assert.Equal($"https://github.com/{Repository}/releases/download/{Tag}/{relative}",
            transport.Calls[^1].Url);
    }

    /// <summary>签名那颗的上限来自 <c>UpdateAssets</c>：一整页 HTML 冒充签名要当场被按尺寸挡下。</summary>
    [Fact]
    public void ASignatureBiggerThanAnyPossibleSignatureIsRefused()
    {
        using var transport = new FakeTransport(url => url == _addresses.SignatureUrl
            ? Response(HttpStatusCode.OK, new byte[UpdateAssets.MaxSignatureBytes + 1])
            : Small(url));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.TooLarge, result.Status);
        Assert.Equal(2, transport.Calls.Count);                    // 载荷那一发根本没投出去
    }

    /// <summary>
    /// 断线发生在"已经被缓冲"的那一段（<c>HttpContent</c> 默认先把内容收进内存再交出来）：
    /// 这一格只管<b>分类</b>——它得说"这台机器现在到对方那段不通"，而不是把异常抛到界面上。
    /// <para><b>它证不了"半截文件不许留在盘上"</b>：那种形状下磁盘上从来没建过文件。
    /// UF 那批把这格登记成"这条不变量没有见证"（台架 UF18 当时报绿），并由此升坑表 #230；
    /// 下一格（走流的那道缝）才是它的证人——两格合起来才是完整的口径：<b>一种分类，两种失败位置</b>。</para>
    /// </summary>
    [Fact]
    public void ABrokenStreamBeforeItReachesTheDiskIsClassifiedAsUnreachable()
    {
        using var transport = new FakeTransport(url => url == _addresses.PackageUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new BufferedThenThrowsContent() }
            : Small(url));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);

        Assert.Equal(PackageFetchStatus.NotReachable, result.Status);
        Assert.Equal("IOException", result.Detail);          // 说得清是流断了，而不是泛泛一句"连不上"
    }

    /// <summary>
    /// 这一格与上面那格<b>只差在字节有没有到过磁盘</b>：载荷那条流先老老实实交出 256 KB，再当场断。
    /// 于是 <c>.part</c> 在盘上真存在过，"半途的尾巴一条都不许留下"这条不变量<b>第一次有了见证</b>——
    /// 台架 UF18（把 <c>finally</c> 里的清理拆成永假）在这一格上必须红。
    /// <para>为什么走 <c>packageStream</c> 那道缝而不是假传输：<see cref="HttpContent"/> 在
    /// <c>ReadAsStreamAsync</c> 里会先把整段收进内存，"到过磁盘再断"这个形状在假传输上<b>做不出来</b>——
    /// 那正是 UF 那批只能把这条不变量登记为"没有见证"的原因（坑表 #230）。缝只换"字节怎么来"，
    /// 写盘、改名、<c>finally</c> 清理走的还是生产那一段。</para>
    /// <para>顺带被这一格照出来的真缺陷：从网络读出来的 <c>IOException</c> 原先和写盘失败落在同一个 catch 上，
    /// 一次断线会被说成"这台机器的临时目录写不下去"。现在读与写分开接，所以这里断言的是 <c>NotReachable</c>。</para>
    /// </summary>
    [Fact]
    public async Task APayloadThatDiesAfterBytesReachedTheDiskLeavesNoHalfWrittenFileBehind()
    {
        using var transport = new FakeTransport(Route());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging,
            (_, _) => new ValueTask<Stream>(new DripThenThrowsStream()));
        var result = await source.FetchAsync(_addresses);

        Assert.Equal(PackageFetchStatus.NotReachable, result.Status);
        Assert.Equal("IOException", result.Detail);              // 说的是"那段路不通"，不是"你磁盘坏了"
        Assert.Empty(Directory.GetFiles(_staging));              // 写过、又被无条件删掉：这一条才是 UF18 的证人
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, PackageFetchStatus.ServerError)]    [InlineData(HttpStatusCode.Unauthorized, PackageFetchStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PackageFetchStatus.Unauthorized)]
    [InlineData(HttpStatusCode.RequestTimeout, PackageFetchStatus.ManifestUnreadable)]   // 认不出的答复不许冒充"服务端坏了"
    public void ServerSideAnswersKeepTheirOwnName(HttpStatusCode code, PackageFetchStatus expected)
    {
        using var transport = new FakeTransport(url => url == _addresses.ManifestUrl
            ? Response(code, Array.Empty<byte>())
            : Small(url));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        Assert.Equal(expected, Run(source).Status);
    }

    [Fact]
    public void RateLimitedIsNotFoldedIntoUnauthorized()
    {
        var response = Response(HttpStatusCode.Forbidden, Array.Empty<byte>());
        response.Headers.Add("X-RateLimit-Remaining", "0");
        using var transport = new FakeTransport(url => url == _addresses.ManifestUrl ? response : Small(url));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        Assert.Equal(PackageFetchStatus.RateLimited, Run(source).Status);
    }

    [Fact]
    public void ANetworkThatCannotConnectSaysReachableRatherThanThrowing()
    {
        using var transport = new FakeTransport(_ => throw new HttpRequestException("no route"));
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        var result = Run(source);
        Assert.Equal(PackageFetchStatus.NotReachable, result.Status);
        Assert.Equal("HttpRequestException", result.Detail);
    }

    /// <summary>用户／关机掐的那一下必须原样上抛，不许被折成"这次更新失败了"（同一形状在同步那条上判过）。</summary>
    [Fact]
    public async Task ACancelledCheckIsNotDowngradedIntoAFailure()
    {
        using var transport = new FakeTransport(_ => throw new TaskCanceledException());
        using var source = new GitHubUpdatePackageSource(transport, () => _staging);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.FetchAsync(_addresses, cts.Token));
    }

    /// <summary>
    /// 时限挂在<b>每一次请求</b>上而不是挂在客户端上：这条链两档时限差 40 倍（小资产 15 秒、载荷 10 分钟），
    /// 混在一处就成了"清单也等十分钟"或"载荷永远下不完"（<c>AiHttp</c> 里同一条口径）。
    /// </summary>
    [Fact]
    public void TheTwoTimeoutsArePerRequestAndDeliberatelyDifferent()
    {
        Assert.True(GitHubUpdatePackageSource.SmallAssetTimeout < GitHubUpdatePackageSource.PackageTimeout);
        Assert.Equal(15, GitHubUpdatePackageSource.SmallAssetTimeout.TotalSeconds);
        Assert.Equal(10, GitHubUpdatePackageSource.PackageTimeout.TotalMinutes);
        var code = Code(ReadRepoFile("src/StarMark.Integrations/Updates/GitHubUpdatePackageSource.cs"));
        Assert.Equal(1, Count(code, "Timeout.InfiniteTimeSpan"));
        Assert.Equal(1, Count(code, "CancelAfter(gap)"));
    }

    /// <summary>这一层的射程边界：它取字节，不解包、不起进程、不碰正在运行的那一份。</summary>
    [Theory]
    [InlineData("src/StarMark.Integrations/Updates/GitHubUpdatePackageSource.cs")]
    public void ThisLayerNeverInstallsAnything(string file)
    {
        var code = Code(ReadRepoFile(file));
        foreach (var forbidden in new[] { "ZipFile", "Process.Start", "Environment.Exit", "Assembly.Load",
                "File.Replace", "Microsoft.Win32" })
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
    }

    /// <summary>资产域那张表放宽一格就是"能被引到任何地方"，收紧一格就是真下载会红——所以两个方向都要钉。</summary>
    [Fact]
    public void TheAssetHostTableIsExactlyTheThreeWeChecked()
        => Assert.Equal(
            new[] { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" },
            GitHubUpdatePackageSource.AllowedDownloadHosts.OrderBy(h => h, StringComparer.Ordinal).ToArray());

    public void Dispose()
    {
        try { if (Directory.Exists(_staging)) Directory.Delete(_staging, recursive: true); }
        catch (IOException) { /* 测试尾巴，不因为它把结论改掉 */ }
    }

    // ===== 小工具 =====

    private PackageFetchResult Run(GitHubUpdatePackageSource source) => source.FetchAsync(_addresses).GetAwaiter().GetResult();

    private string Address(string which) => which switch
    {
        nameof(PackageAssetAddresses.ManifestUrl) => _addresses.ManifestUrl,
        nameof(PackageAssetAddresses.SignatureUrl) => _addresses.SignatureUrl,
        _ => _addresses.PackageUrl,
    };

    private HttpResponseMessage Small(string url)
        => Response(HttpStatusCode.OK, url == _addresses.ManifestUrl ? ManifestBytes
            : url == _addresses.SignatureUrl ? SignatureBytes : PayloadBytes);

    private Func<string, HttpResponseMessage> Route() => Small;

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Response(HttpStatusCode code, byte[] body)
        => new(code) { Content = new ByteArrayContent(body) };

    /// <summary>走默认缓冲路径的内容：读之前就抛 ⇒ 外面套的是 <c>HttpRequestException</c>。</summary>
    private sealed class BufferedThenThrowsContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new IOException("半路断了");

        protected override bool TryComputeLength(out long length)
        {
            length = PayloadBytes.LongLength;
            return true;
        }
    }

    /// <summary>
    /// 先把 256 KB 老老实实交出去，再当场断。<b>不这么写就证不到"到过磁盘"</b>：
    /// 一开头就抛的话读取方一个字节都没拿到，那条 <c>finally</c> 清理分支一次都不会走到（UF18 当初报绿的真正原因）。
    /// <para>只实现异步读：这条链上取字节的就是 <c>ReadAsync(Memory)</c>，别的面包在这格里没有意义。</para>
    /// </summary>
    private sealed class DripThenThrowsStream : Stream
    {
        private long _left = 4 * 64 * 1024;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Yield();
            if (_left <= 0) throw new IOException("送到一半断了");
            var take = Math.Min(buffer.Length, (int)_left);
            buffer.Span[..take].Fill(0);
            _left -= take;
            return take;
        }
    }

    private sealed class FakeTransport : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _route;
        public List<(string Url, string? Auth)> Calls { get; } = new();

        public FakeTransport(Func<string, HttpResponseMessage> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return Task.FromResult(_route(request.RequestUri!.ToString()));
        }
    }
}
