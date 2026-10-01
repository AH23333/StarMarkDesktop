#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Integrations.Updates;

/// <summary>
/// 取一次更新包：清单 + 签名 + 载荷（批次 UF）。<b>只搬到临时目录，一个字节都不往安装目录放</b>。
/// <para>
/// 这个类<b>不做任何信任判定</b>，也<b>读不懂清单</b>：三个地址是 Core 拼好递下来的
/// （<see cref="PackageAssetAddresses"/>），字节取回来算个哈希交上去，签不签得对、能不能装由
/// <c>UpdateIntegrity</c> 说。这么切不是因为懒，而是因为<b>取字节的这一步必须处在"还没信任何东西"的位置</b>：
/// 一旦允许它先读清单再决定去哪儿取，"一份没验过签名的输入"就成了下一个请求的作者。
/// </para>
/// <para>三条与探测那侧不同的处置，各有理由：
/// ① <b>这一条链上根本不带凭据</b>。下载地址是 <c>github.com/&lt;仓库&gt;/releases/download/&lt;标签&gt;/&lt;资产&gt;</c>，
///    它会跳到 CDN 域；把 <c>Authorization</c> 挂在第一段上，等于给一条会换宿主的路由送长效凭据
///    （BJ 那批查凭据外泄时判的就是这一族）。公开仓库的资产匿名可下，所以不送凭据不损失任何功能；
///    私有仓库那一份下载会在 404／403 上拿到一句实话，而不是"悄悄降级成不安全的那条路"。
/// ② <b>跳转要手工跟</b>。这条链非跟不可（不跟就没有包），所以判据换成"每一跳都重新验"：
///    仍是 https、宿主仍在 GitHub 资产域内、跳数有上限。这比"完全禁止跳转"松，比
///    <c>AllowAutoRedirect = true</c> 严——后者会把凭据与信任一起带到一个我们没读过的地址上。
/// ③ <b>边下边算哈希</b>。载荷是几十到几百兆，先落盘再回读只是把同一堆字节读两遍；
///    而"实际字节数不超过上限"这条要在下载<em>过程中</em>就成立，否则对方回一个 5 GB 的洞就把磁盘写满了。
/// </para>
/// </summary>
public sealed class GitHubUpdatePackageSource : IUpdatePackageSource, IDisposable
{
    /// <summary>资产下载允许落在哪些宿主上（<b>第一跳必是 github.com，跳完也只许去这两个 CDN 域</b>）。</summary>
    internal static readonly string[] AllowedDownloadHosts =
    {
        "github.com",
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com",
    };

    /// <summary>清单／签名那两颗小东西的时限。</summary>
    internal static readonly TimeSpan SmallAssetTimeout = TimeSpan.FromSeconds(15);

    /// <summary>载荷的时限。比小资产长得多，但<b>必须有界</b>：无界的"正在更新"就是永远不动。</summary>
    internal static readonly TimeSpan PackageTimeout = TimeSpan.FromMinutes(10);

    private const string AssetHost = "github.com";
    private const int MaxHops = 4;

    private readonly HttpClient _http;
    private readonly Func<string> _stagingDir;
    private readonly Func<HttpResponseMessage, CancellationToken, ValueTask<Stream>> _openPackageStream;

    /// <param name="handler">测试缝：假传输。不注入时用"关自动跳转"的默认处理器（跳转由本类自己判）。</param>
    /// <param name="stagingDirProvider">临时载荷放哪儿。默认 <c>%TEMP%\StarMarkUpdate</c>——
    /// <b>刻意不是安装目录</b>：往正在运行的那堆文件旁边写东西，才是这条链最不该有的半径。</param>
    /// <param name="packageStreamProvider">载荷那条流<b>怎么来</b>的另一道缝，理由不是解耦好看而是取证：
    /// 测试用的 <c>HttpContent</c> 会在 <c>ReadAsStreamAsync</c> 里被整个先收进内存，
    /// 于是"字节已经到过磁盘、然后断线"这一族形状在假传输上<b>做不出来</b>——
    /// 少了这道缝，"半截文件不许冒充完整文件"那条不变量就只能永远没有见证（坑表 #230，台架 UF18）。
    /// 生产调用不传它，走的还是 <c>response.Content</c>。</param>
    public GitHubUpdatePackageSource(HttpMessageHandler? handler = null, Func<string>? stagingDirProvider = null,
        Func<HttpResponseMessage, CancellationToken, ValueTask<Stream>>? packageStreamProvider = null)
    {
        _http = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        // 时限分两档挂在每次请求上（见 <c>AiHttp</c> 里同一条口径：两种时限混在客户端上就分不清了）
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _stagingDir = stagingDirProvider ?? DefaultStagingDir;
        _openPackageStream = packageStreamProvider ?? DefaultOpenPackageStream;
    }

    public async Task<PackageFetchResult> FetchAsync(PackageAssetAddresses addresses, CancellationToken ct = default)
    {
        if (addresses is null)
            return new PackageFetchResult(PackageFetchStatus.AddressRefused, Detail: "Core 没给出地址");

        var manifest = await GetAsync(addresses.ManifestUrl, UpdateAssets.MaxManifestBytes,
            SmallAssetTimeout, staging: null, ct).ConfigureAwait(false);
        if (manifest.Failure is not null)
            return new PackageFetchResult(manifest.Failure.Value, Detail: manifest.Detail);

        var signature = await GetAsync(addresses.SignatureUrl, UpdateAssets.MaxSignatureBytes,
            SmallAssetTimeout, staging: null, ct).ConfigureAwait(false);
        if (signature.Failure is not null)
            return new PackageFetchResult(signature.Failure.Value, Detail: signature.Detail);

        var package = await GetAsync(addresses.PackageUrl, UpdateAssets.MaxPackageBytes,
            PackageTimeout, staging: _stagingDir(), ct).ConfigureAwait(false);
        if (package.Failure is not null)
            return new PackageFetchResult(package.Failure.Value, Detail: package.Detail);

        return new PackageFetchResult(PackageFetchStatus.Fetched, new FetchedPackage(
            manifest.Data!, signature.Data!, package.Path!, package.Sha256!, package.Bytes!.Value));
    }

    private sealed record Fetched(string? Path, string? Sha256, long? Bytes, byte[]? Data,
        PackageFetchStatus? Failure, string? Detail);

    /// <summary>
    /// 一次带"逐跳重验"的取字节。<paramref name="staging"/> 为 null 时读进内存，否则流式写进那里并算哈希。
    /// </summary>
    private async Task<Fetched> GetAsync(string url, long cap, TimeSpan gap, string? staging, CancellationToken ct)
    {
        var current = url;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(gap);

        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (!TryValidate(current, firstHop: hop == 0, out var hostError))
                return Failed(PackageFetchStatus.OffHostRedirect, hostError);

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                // 刻意不加 Authorization：见类注释 ①
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    bounded.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Failed(PackageFetchStatus.TimedOut, $"{gap.TotalSeconds:0} 秒内没送完");
            }
            catch (OperationCanceledException) { throw; }        // 用户／关机掐的：原样交回，不许降级成"失败"
            catch (HttpRequestException ex)
            {
                return Failed(PackageFetchStatus.NotReachable, ex.GetBaseException().GetType().Name);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is >= 300 and <= 399)
                {
                    var location = response.Headers.Location;
                    if (location is null) return Failed(PackageFetchStatus.OffHostRedirect, "跳转没给去处");
                    current = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(response.RequestMessage?.RequestUri ?? new Uri(current), location).ToString();
                    continue;                                       // 下一跳开头会重验宿主
                }

                var failure = ClassifyFailure(response, status);
                if (failure is not null) return failure;

                return staging is null
                    ? await ReadIntoMemoryAsync(response, cap, bounded.Token).ConfigureAwait(false)
                    : await WriteToDiskAsync(response, cap, staging, bounded.Token).ConfigureAwait(false);
            }
        }
        return Failed(PackageFetchStatus.OffHostRedirect, $"跳了 {MaxHops} 跳还没拿到东西");
    }

    /// <summary>
    /// 每一跳开头问一句：还是 https 吗？还在 GitHub 的资产域里吗？
    /// <para>判据用 <see cref="Uri.Host"/> 而不是"串里有没有那个域名"——后者会被
    /// <c>github.com.evil.test</c> 顶过去（同一形状在 <c>GitHubReleaseSource.BuildUrl</c> 的注释里写过）。</para>
    /// </summary>
    private static bool TryValidate(string url, bool firstHop, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { error = "地址不是绝对 URI"; return false; }
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = $"这一跳掉出了 https（{uri.Scheme}）";
            return false;
        }
        if (firstHop && !string.Equals(uri.Host, AssetHost, StringComparison.OrdinalIgnoreCase))
        {
            error = $"第一跳不在 {AssetHost}（{uri.Host}）";
            return false;
        }
        if (Array.IndexOf(AllowedDownloadHosts, uri.Host) < 0)
        {
            error = $"宿主不在资产域内（{uri.Host}）";
            return false;
        }
        return true;
    }

    private static Fetched? ClassifyFailure(HttpResponseMessage response, int status)
    {
        if (status < 400) return null;
        var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
            ? System.Linq.Enumerable.FirstOrDefault(values) : null;
        if (status is 403 or 429 && string.Equals(remaining?.Trim(), "0", StringComparison.Ordinal))
            return Failed(PackageFetchStatus.RateLimited, "X-RateLimit-Remaining: 0");
        if (status == 404) return Failed(PackageFetchStatus.AssetMissing, "HTTP 404");
        if (status is 401 or 403) return Failed(PackageFetchStatus.Unauthorized, $"HTTP {status}");
        if (status >= 500) return Failed(PackageFetchStatus.ServerError, $"HTTP {status}");
        return Failed(PackageFetchStatus.ManifestUnreadable, $"HTTP {status}");
    }

    private static Fetched Failed(PackageFetchStatus status, string? detail = null)
        => new(null, null, null, null, status, detail);

    private static async Task<Fetched> ReadIntoMemoryAsync(HttpResponseMessage response, long cap, CancellationToken ct)
    {
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        long total = 0;
        while (await source.ReadAsync(chunk, ct).ConfigureAwait(false) is var read and > 0)
        {
            total += read;
            if (total > cap) return Failed(PackageFetchStatus.TooLarge, $"超过 {cap} 字节");
            buffer.Write(chunk, 0, read);
        }
        return new Fetched(null, null, total, buffer.ToArray(), null, null);
    }

    /// <summary>
    /// 流式写进临时目录：先写 <c>.part</c>，成了才改名。
    /// <para>改名之前它就不是"那份包"，而半途留下的那条尾巴必须<b>无条件</b>被删掉——
    /// 所以清理写在 <c>finally</c> 里，而不是摊在每一个 catch 上：分类会写漏一种异常，
    /// 不变量不能跟着一起漏（<b>半截文件冒充完整文件</b>是这条链上最贵的一类脏，#224 那一族）。</para>
    /// <para>复制那一段收在独立方法里也是有原因的：句柄没关掉就 <c>File.Delete</c>／<c>File.Move</c>
    /// 在 Windows 上必定失败，清理写在 <c>using</c> 块里面就等于每次都清不掉。</para>
    /// </summary>
    private async Task<Fetched> WriteToDiskAsync(HttpResponseMessage response, long cap, string staging,
        CancellationToken ct)
    {
        var part = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".part");
        var final = Path.ChangeExtension(part, ".zip");
        var moved = false;
        try
        {
            Directory.CreateDirectory(staging);
            var (hashText, written) = await CopyAsync(response, part, cap, ct).ConfigureAwait(false);
            File.Move(part, final, overwrite: true);
            moved = true;
            return new Fetched(final, hashText, written, null, null, null);
        }
        catch (PackageTooLargeException ex)
        {
            return Failed(PackageFetchStatus.TooLarge, ex.Message);
        }
        catch (PackageStreamBrokenException ex)
        {
            // 从对方那条流里读出来的失败：这台机器到对方那段不通，不是"本机磁盘写不下去"，更不是"重装程序"
            return Failed(PackageFetchStatus.NotReachable, ex.Inner.GetType().Name);
        }
        catch (OperationCanceledException)
        {
            throw;                                          // 用户／关机掐的：原样交回，但尾巴照样清
        }
        catch (HttpRequestException ex)
        {
            // 连接在半路被掐断（真代理与对方挂掉的常见形状）：取字节这一侧它就和连不上是同一句话
            return Failed(PackageFetchStatus.NotReachable, ex.GetBaseException().GetType().Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                or ArgumentException)
        {
            // ArgumentException 也在这一档：临时目录那串是外面给的（%TEMP% 被人改过就什么都能出现），
            // 让它冒到界面上只是把"这台机器写不下去"变成一次崩溃。
            return Failed(PackageFetchStatus.DiskWriteFailed, ex.GetType().Name);
        }
        finally
        {
            if (!moved) DeleteQuietly(part);
        }
    }

    /// <summary>
    /// 边读边写边算哈希。<b>"从网络读"与"往磁盘写"的异常必须分开接</b>：
    /// 两条路都可能抛 <c>IOException</c>，混在一个 catch 里就会把一次断线说成"这台机器的临时目录写不下去"
    /// （那道缝刚照出来的错，见 <see cref="PackageStreamBrokenException"/>）。
    /// </summary>
    private async Task<(string Hash, long Bytes)> CopyAsync(HttpResponseMessage response, string part,
        long cap, CancellationToken ct)
    {
        await using var source = await _openPackageStream(response, ct).ConfigureAwait(false);
        await using var target = new FileStream(part, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 64 * 1024,
        });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read;
            try
            {
                read = await source.ReadAsync(chunk, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new PackageStreamBrokenException(ex);
            }
            if (read <= 0) break;

            total += read;
            if (total > cap) throw new PackageTooLargeException($"超过 {cap} 字节");
            hash.AppendData(chunk, 0, read);
            await target.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        await target.FlushAsync(ct).ConfigureAwait(false);
        return (UpdateHashHex.ToHex(hash.GetHashAndReset()), total);
    }

    /// <summary>载荷字节比上限还长：这不是"磁盘坏了"，而是"对方在骗我们一个尺寸"。</summary>
    private sealed class PackageTooLargeException : IOException
    {
        public PackageTooLargeException(string message) : base(message) { }
    }

    /// <summary>
    /// 载荷那条流在半路断了。<b>刻意不继承 IOException</b>：继承了就会掉回"磁盘写不下去"那一档，
    /// 而这一族的正确处置是"下次联网自己补问"，不是让用户去查临时目录。
    /// </summary>
    private sealed class PackageStreamBrokenException : Exception
    {
        public PackageStreamBrokenException(Exception inner) : base(inner.Message, inner) => Inner = inner;
        public Exception Inner { get; }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清不掉只是留了个临时尾巴：它没通过校验，也没有任何调用方会拿它去装东西。
            // 这里抛出去只会把"更新失败"变成"更新失败并且崩在收尾"（尺子不许弄崩被量的东西）。
            StarLog.Warn($"[更新] 临时载荷没删掉：{ex.GetType().Name}");
        }
    }

    private static async ValueTask<Stream> DefaultOpenPackageStream(HttpResponseMessage response, CancellationToken ct)
        => await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

    private static string DefaultStagingDir()
        => Path.Combine(Path.GetTempPath(), "StarMarkUpdate");

    public void Dispose() => _http.Dispose();
}
