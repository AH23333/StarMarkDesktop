#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>
/// "这份东西是不是我们发出去的"——更新包的<b>信任判据只在这一处</b>（批次 UF）。
/// <para>
/// 为什么必须有这一层，而不是"下载完直接换文件"：一条更新链路会把<b>任意字节的代码</b>送进这台机器，
/// 而这些字节在落到磁盘上之前，唯一属于我们自己的判断只有"https 连到了 api.github.com"。
/// TLS 挡住的是路线上的中间人，挡不住三件事：证书信任链本身被攻破、发布方存储被换包、
/// 以及最常被忽略的一件——<b>落盘之后、使用之前被人换掉同一台机器上的那份文件</b>（临时目录本用户可写）。
/// 所以判据是"字节必须对得上一个由签名背书的名字"，而不是"字节看起来像 zip"。
/// 这也是账本 P-29 那条欠账的形状（下载即执行／下载即加载，中间没有校验）。
/// </para>
/// <para>
/// <b>信任根是内嵌的公钥</b>：<see cref="UpdateTrustAnchor.EmbeddedPublicKeySpki"/>。
/// 为什么不拿 GitHub 回的 <c>assets[].digest</c> 当期望值：那个字段也是同一次响应里的字符串，
/// 能改响应的人同样能改它——拿它自证等于没有期望值。为什么不做 Authenticode：这份产物没有代码签名证书，
/// 自签证书链在这里提供不了任何对方无法控制的锚点。ECDSA P-256 分离签名用的私钥只在发布机上，
/// 仓库里只有公钥 ⇒ <b>能读到这个仓库的人（它是公开的）伪造不出一份我们认账的清单</b>。
/// </para>
/// </summary>
public static class UpdateIntegrity
{
    /// <summary>
    /// 一份取回来的包，逐项过完之后的<b>结局</b>。
    /// <para>每个成员都对应一句要给人看的话（<c>UpdatePolicy.Describe</c>），所以不许合并成"校验失败"：
    /// "签名不对"与"这一版不比你的新"是完全不同的两件事，前者是安全事件，后者是常态。</para>
    /// </summary>
    public enum Outcome
    {
        /// <summary>签得对、字段读得懂、版本确实更新、字节对得上哈希——可以交给下一步。</summary>
        Trusted,

        /// <summary>签名对不上清单原文（换过清单、换过公钥、签名不是这份清单的）。</summary>
        SignatureInvalid,

        /// <summary>清单不是能读的形状（不是 JSON、缺字段、字段形状不对）。</summary>
        ManifestUnreadable,

        /// <summary>清单的 <c>schema</c> 比这个程序认识的还新——先升级程序，再谈更新。</summary>
        SchemaUnsupported,

        /// <summary>清单里某个字段被拒（哈希不是 64 位小写十六进制、路径越界、载荷不叫 .zip、颗数不对…）。</summary>
        FieldRejected,

        /// <summary>清单里的版本与要去取的那个标签<b>不是同一版</b>（拿旧清单冒充新版、或发布时填错了版本）。</summary>
        VersionMismatch,

        /// <summary>清单这一版<b>不比本机新</b>（同版或更旧）——装它就是往回退。</summary>
        RollbackRefused,

        /// <summary>本机版本号读不到，于是"是不是更新"无从判断，拒。</summary>
        LocalVersionUnknown,

        /// <summary>载荷的实际哈希与清单里的对不上（<b>这是最该出声的一格</b>：包被换了）。</summary>
        PackageHashMismatch,

        /// <summary>载荷的实际字节数与清单里的对不上。</summary>
        PackageSizeMismatch,
    }

    /// <summary>判定结果。<paramref name="Manifest"/> 只在 <see cref="Outcome.Trusted"/> 时有意义。</summary>
    public sealed record Result(Outcome Outcome, ReleaseManifest? Manifest = null, string? Detail = null)
    {
        public bool IsTrusted => Outcome == Outcome.Trusted && Manifest is not null;
    }

    /// <summary>
    /// 生产入口：<b>用内嵌的那把公钥</b>审一份取回来的包。
    /// <para>参数里刻意没有"公钥"一格：调用方一旦能传公钥，就有把校验降级成"信任何一方"的一天。
    /// 测试要自造密钥时走 <see cref="Inspect(FetchedPackage,string,string,string?,UpdateTrustAnchor)"/>
    /// 那一个重载，而界面上唯一的接线点用这个四参版本（有闸门盯着）。</para>
    /// </summary>
    public static Result Inspect(FetchedPackage package, string repository, string tag, string? localVersion)
        => Inspect(package, repository, tag, localVersion, UpdateTrustAnchor.Embedded);

    /// <summary>
    /// 逐项审。<b>顺序是有讲究的</b>：先验签名（不看内容），再读字段，再对版本，最后才比哈希——
    /// 反过来做就等于"照一份没验过的清单去解释这台机器上的字节"。
    /// </summary>
    public static Result Inspect(FetchedPackage package, string repository, string tag, string? localVersion,
        UpdateTrustAnchor anchor)
    {
        if (package is null) return new Result(Outcome.ManifestUnreadable, Detail: "没有包可审");
        if (anchor is null) return new Result(Outcome.SignatureInvalid, Detail: "没有信任根");

        // ① 签名：盖的是清单原文那串字节，所以这里连 JSON 都不碰。
        var manifestBytes = package.ManifestBytes ?? Array.Empty<byte>();
        var signatureBytes = package.SignatureBytes ?? Array.Empty<byte>();
        if (manifestBytes.Length == 0 || manifestBytes.LongLength > UpdateAssets.MaxManifestBytes)
            return new Result(Outcome.ManifestUnreadable, Detail: $"清单字节数不在这档（{manifestBytes.Length}）");
        if (signatureBytes.Length is < 8 or > UpdateAssets.MaxSignatureBytes || !anchor.Verify(manifestBytes, signatureBytes))
            return new Result(Outcome.SignatureInvalid);

        // ② 字段：读不懂就到此为止（后面的比较用不上，也不能用上）。
        if (!UpdateManifestCodec.TryRead(manifestBytes, out var manifest, out var reason))
            return new Result(reason.ToOutcome(), Detail: reason.Detail);

        // ③ 版本：清单必须就是"我们正准备取的那一版"，而且要比本机新。
        if (!AppVersion.TryParse(manifest!.Version, out var declared))
            return new Result(Outcome.ManifestUnreadable, Detail: "清单里的版本读不懂");
        if (!AppVersion.TryParse(tag, out var wanted) || AppVersion.Compare(wanted, declared) != 0)
            return new Result(Outcome.VersionMismatch,
                Detail: $"清单写的是 {manifest.Version}，要取的那一版是 {tag}");
        // 载荷那颗是按标签算出来去取的（见 <c>UpdateAssets.PackageNameFor</c>），这里核对清单说的是不是同一颗：
        // 两边说的不是一样的东西，就是发布链的某一环错乱了，而"取回来的"与"要装的"必须是一件。
        if (!string.Equals(manifest.Package, UpdateAssets.PackageNameFor(AppVersion.Describe(declared)),
                StringComparison.Ordinal))
            return new Result(Outcome.FieldRejected, Detail: "清单里的载荷名与按标签算出来的那颗不是同一颗");
        if (!AppVersion.TryParse(localVersion, out var local))
            return new Result(Outcome.LocalVersionUnknown);
        if (AppVersion.Compare(local, declared) >= 0)
            return new Result(Outcome.RollbackRefused,
                Detail: $"本机 {AppVersion.Describe(local)}，清单这版 {manifest.Version} 不比它新");

        // ④ 字节：到这一步才允许拿磁盘上的东西跟清单对。
        if (package.PackageBytes != manifest.PackageBytes)
            return new Result(Outcome.PackageSizeMismatch,
                Detail: $"清单说 {manifest.PackageBytes} 字节，实际 {package.PackageBytes}");
        if (!string.Equals(package.PackageSha256, manifest.PackageSha256, StringComparison.Ordinal))
            return new Result(Outcome.PackageHashMismatch,
                Detail: $"清单 {Short(manifest.PackageSha256)}，实际 {Short(package.PackageSha256)}");

        return new Result(Outcome.Trusted, manifest);
    }

    /// <summary>日志里念哈希只念前 12 位：完整哈希进日志没有任何好处，而两串长十六进制并排着读不出差别。</summary>
    private static string Short(string? hex)
        => string.IsNullOrEmpty(hex) ? "?" : hex!.Length <= 12 ? hex : hex[..12];
}

/// <summary>
/// 内嵌的<b>信任根</b>：一把 P-256 公钥，SPKI 的 base64。
/// <para>公钥写在源码里是安全的（它能验，不能签）；<b>换这把钥匙等于换一次发布体系</b>：
/// 老程序只认老钥匙签的清单，所以私钥丢不得——丢了就是"这个已安装的程序再也收不到更新"。</para>
/// </summary>
public sealed class UpdateTrustAnchor
{
    /// <summary>
    /// 与 <c>%USERPROFILE%\.starmark\update-signing.pub.pem</c> 对应的那把公钥
    /// （私钥在发布机上、不入库；<c>tools/StarMark.UpdateSigner</c> 用它签清单）。
    /// <para>核对办法（不需要信任任何一方）：把这段 base64 解成 DER，其 SHA-256 应为
    /// <c>86622c74f380f4e3a2df86bd2c1d5de93b2c2061f4c41f91ba048dc76364b231</c>。</para>
    /// </summary>
    public const string EmbeddedPublicKeySpki =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEI86mvng0P2EKd7gIwuo/FQAGUq/a4uC2uSrKOb8yZ2zJK4qmG4llhQIIsSjr9wvZ+lJtU5iPbzQRq52Nvvykcg==";

    /// <summary>生产用的那一份。</summary>
    public static readonly UpdateTrustAnchor Embedded = FromSpki(EmbeddedPublicKeySpki)
        ?? throw new InvalidOperationException("内嵌的更新签名公钥读不懂（这份产物不该发出去）");

    private readonly ECDsa _key;

    private UpdateTrustAnchor(ECDsa key) => _key = key;

    /// <summary>
    /// 从 SPKI base64 造一个信任根。<b>只认 256 位那一档曲线</b>：钥匙曲线与签名格式必须钉死一种，
    /// 否则"能构造出验得过的密钥"的参数替换类问题就会开一条口子（同 #17 那族：常量要有一条真调用的测）。
    /// 读不懂／不是 EC／曲线不对 ⇒ null（不抛：这是启动期就会走到的一格）。
    /// </summary>
    public static UpdateTrustAnchor? FromSpki(string? base64Spki)
    {
        if (string.IsNullOrWhiteSpace(base64Spki)) return null;
        byte[] der;
        try
        {
            der = Convert.FromBase64String(base64Spki.Trim());
        }
        catch (FormatException) { return null; }

        try
        {
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(der, out _);
            if (key.KeySize != 256)
            {
                key.Dispose();
                return null;
            }
            return new UpdateTrustAnchor(key);
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// 验一份数据的 ECDSA-SHA256 签名。
    /// <para><b>格式写死成 DER（<see cref="DSASignatureFormat.Rfc3279DerSequence"/>）而不是用默认值</b>：
    /// 实测这颗运行时对默认格式给的是 64 字节的 P1363 拼接串，而 DER 是 70~72 字节——两边都用默认值时
    /// 自己当然对得上，可 openssl 签出来的东西就验不过，"换一把工具独立验一次"这条见证会当场断掉。
    /// 默认值也是值：它跟着版本变，签名格式一变就是全线更新不可用。</para>
    /// </summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            return _key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            // 畸形签名在原生层是会抛的：抛出来只是把一次更新变成一次崩溃
            return false;
        }
        catch (ArgumentException) { return false; }        // 格式串本身不合法（例如长度不对的 P1363 喂进来）
    }

    /// <summary>这把公钥自身的 SHA-256 指纹（<b>十六进制小写</b>）——用来当场证明"内嵌的那把就是发布用的那把"。</summary>
    public string Fingerprint()
    {
        var derived = _key.ExportSubjectPublicKeyInfo();
        return Convert.ToHexString(SHA256.HashData(derived)).ToLowerInvariant();
    }
}
