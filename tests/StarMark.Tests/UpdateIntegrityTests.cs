#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 更新包的<b>信任判据</b>：清单读法、签名、版本对齐、字节对账（批次 UF）。
/// <para>
/// 这一批要钉的东西只有一句话：<b>什么样的字节才允许被当成"我们发出去的那一版"</b>。
/// 它做错的表现不是崩溃，而是两种更贵的东西——把别人签的东西当自己的装上（远程代码执行），
/// 或把自己发的东西一律拒收（更新通道永远不通，而日志上看着像"有人在换包"）。
/// 所以每一格都要点出<b>是哪一栏把这份包挡下来的</b>。
/// </para>
/// <para>
/// 内嵌公钥那一格特别说明：测试手上没有发布私钥（它在发布机上、不入库），所以自造钥匙那批用例
/// 证的只是"换一把钥匙签的东西不认"＋"验签真的在算"，而<b>"内嵌这把与发布那把是同一把"</b>由
/// <see cref="TheEmbeddedKeyStillMatchesTheGoldenOpenSslSignature"/> 那条黄金用例来证——
/// 那份签名是 openssl 产的，不是这颗运行时自己签的（同一把钥匙自己签自己验，是 #17 那一族
/// 最会冒充"测过了"的形状）。
/// </para>
/// </summary>
public sealed class UpdateIntegrityTests
{
    private const string Repository = "AH23333/StarMarkDesktop";
    private const string Tag = "v1.0.1";
    private const string PackageName = "StarMark-1.0.1-win-x64.zip";
    private const string PayloadHash = "f9faf910da80977a9f2c7be894c0e4f859bea8e124af54f660d4d8846a448fee";
    private const string EntryHash = "07079e3aa86949582a60adbde8e05e9fb7f1fd6c8707d7a3eb15516f81f152b4";
    private const string LibraryHash = "0343b13708cd52d33ecbb1043b5ae145e73a532bdccf7de6925db5cce6054fd3";

    // 黄金数据：清单那 516 个字节与那份 71 字节的签名都出自 openssl（见 UpdateManifestCodec.Write 的字节稳定用例）
    private const string GoldenManifestBase64 =
        "ew0KICAic2NoZW1hIjogMSwNCiAgInZlcnNpb24iOiAiMS4wLjEiLA0KICAicGFja2FnZSI6ICJTdGFyTWFyay0xLjAuMS13aW4teDY0LnppcCIsDQogICJwYWNrYWdlU2hhMjU2IjogImY5ZmFmOTEwZGE4MDk3N2E5ZjJjN2JlODk0YzBlNGY4NTliZWE4ZTEyNGFmNTRmNjYwZDRkODg0NmE0NDhmZWUiLA0KICAicGFja2FnZUJ5dGVzIjogMjQ0LA0KICAiZmlsZXMiOiBbDQogICAgew0KICAgICAgInBhdGgiOiAiU3Rhck1hcmsuVUkuZXhlIiwNCiAgICAgICJzaGEyNTYiOiAiMDcwNzllM2FhODY5NDk1ODJhNjBhZGJkZThlMDVlOWZiN2YxZmQ2Yzg3MDdkN2EzZWIxNTUxNmY4MWYxNTJiNCIsDQogICAgICAiYnl0ZXMiOiAxNQ0KICAgIH0sDQogICAgew0KICAgICAgInBhdGgiOiAiYS9saWIuZGxsIiwNCiAgICAgICJzaGEyNTYiOiAiMDM0M2IxMzcwOGNkNTJkMzNlY2JiMTA0M2I1YWUxNDVlNzNhNTMyYmRjY2Y3ZGU2OTI1ZGI1Y2NlNjA1NGZkMyIsDQogICAgICAiYnl0ZXMiOiAzDQogICAgfQ0KICBdDQp9";
    private const string GoldenOpenSslSignatureHex =
        "30450220293760de32cc6944000f1ae32d18d7cbc3ce34028321e456000b33b952b1cebb022100d1b32b979a864d541df2a19799cba0ea6a2e14983ccd9742341920b6cfd591f2";

    // ===== 清单的读法与写法 =====

    [Fact]
    public void WriteThenReadGivesTheSameManifestBack()
    {
        var written = Manifest();
        Assert.True(UpdateManifestCodec.TryRead(UpdateManifestCodec.Write(written), out var read, out var reject),
            reject.Detail);
        Assert.Equal(written.Schema, read!.Schema);
        Assert.Equal(written.Version, read.Version);
        Assert.Equal(written.Package, read.Package);
        Assert.Equal(written.PackageSha256, read.PackageSha256);
        Assert.Equal(written.PackageBytes, read.PackageBytes);
        Assert.Equal(written.Files.Select(f => f.Path + f.Sha256 + f.Bytes),
            read!.Files.Select(f => f.Path + f.Sha256 + f.Bytes));
    }

    /// <summary>签名盖的就是"写出来的那串字节"，所以写必须确定，而且要与 openssl 签过的那一串一字不差。</summary>
    [Fact]
    public void TheBytesWeSignAreExactlyTheOnesTheGoldenSignatureCovers()
        => Assert.Equal(GoldenManifestBase64, Convert.ToBase64String(UpdateManifestCodec.Write(Manifest())));

    [Fact]
    public void AnEmptyStreamIsNotAManifest()
    {
        Assert.False(UpdateManifestCodec.TryRead(Array.Empty<byte>(), out var read, out var reject));
        Assert.Null(read);
        Assert.Equal(ManifestRejectKind.Unreadable, reject.Kind);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("{\"schema\":1}")]
    [InlineData("{\"schema\":\"1\",\"version\":\"1.0.1\"}")]
    public void AnythingThatIsNotAFullManifestIsRefused(string json)
    {
        Assert.False(UpdateManifestCodec.TryRead(Encoding.UTF8.GetBytes(json), out var read, out var reject));
        Assert.Null(read);
        Assert.Equal(ManifestRejectKind.Unreadable, reject.Kind);
    }

    [Fact]
    public void AManifestNewerThanThisProgramIsRefusedRatherThanHalfRead()
    {
        Assert.False(UpdateManifestCodec.TryRead(
            Encoding.UTF8.GetBytes(ManifestJson(new Dictionary<string, string> { ["schema"] = "2" })), out _, out var reject));
        Assert.Equal(ManifestRejectKind.SchemaTooNew, reject.Kind);
        Assert.Equal(UpdateIntegrity.Outcome.SchemaUnsupported, reject.ToOutcome());
    }

    [Theory]
    [InlineData("version", "\"不是数字\"", ManifestRejectKind.Unreadable)]
    [InlineData("package", "\"sub/StarMark-1.0.1-win-x64.zip\"", ManifestRejectKind.FieldRejected)]        // 载荷名不许带路径
    [InlineData("package", "\"StarMark-1.0.1-win-x64.tar\"", ManifestRejectKind.FieldRejected)]            // 不是 .zip
    [InlineData("package", "\"StarMark-1.0.1-win-x64.zip?x=1\"", ManifestRejectKind.FieldRejected)]
    [InlineData("packageSha256", "\"ABCDEF\"", ManifestRejectKind.FieldRejected)]                          // 短了，也不是小写
    [InlineData("packageSha256", "\"F9FAF910DA80977A9F2C7BE894C0E4F859BEA8E124AF54F660D4D8846A448FEE\"", ManifestRejectKind.FieldRejected)]
    [InlineData("packageBytes", "0", ManifestRejectKind.FieldRejected)]
    [InlineData("packageBytes", "-5", ManifestRejectKind.FieldRejected)]
    [InlineData("packageBytes", "999999999999", ManifestRejectKind.FieldRejected)]
    public void EachFieldHasABoundsAndItBites(string field, string value, ManifestRejectKind expected)
    {
        var json = ManifestJson(new Dictionary<string, string> { [field] = value });
        Assert.False(UpdateManifestCodec.TryRead(Encoding.UTF8.GetBytes(json), out var read, out var reject),
            $"这一栏放宽了也没人红：{field} = {value}");
        Assert.Null(read);
        Assert.Equal(expected, reject.Kind);
    }

    /// <summary>省着写的版本号照样是同一版：<c>1.0</c> 读成 <c>1.0.0</c>（写标签的人省一截是常态）。</summary>
    [Fact]
    public void AVersionWrittenShortIsStillReadAsAVersion()
    {
        var json = ManifestJson(new Dictionary<string, string> { ["version"] = "\"1.0\"" });
        Assert.True(UpdateManifestCodec.TryRead(Encoding.UTF8.GetBytes(json), out var read, out var reject), reject.Detail);
        Assert.Equal("1.0", read!.Version);
    }

    /// <summary>
    /// 零颗文件单独一格，而且<b>必须说出是被哪一臂拒的</b>：只断言"FieldRejected"的话，
    /// "files 里没有主程序"那一臂会替"颗数下限"答题（两臂对空表给同一个分类），
    /// 台架把 `is 0` 拆掉就没人红——同一形状在 #228/#229 里各以另一种面貌出现过。
    /// </summary>
    [Fact]
    public void AnEmptyFileListIsRefusedByTheCountBoundNotJustByAnyOfThem()
    {
        var json = Encoding.UTF8.GetBytes(ManifestJson(new Dictionary<string, string> { ["files"] = "[]" }));
        Assert.False(UpdateManifestCodec.TryRead(json, out var read, out var reject));
        Assert.Null(read);
        Assert.Equal(ManifestRejectKind.FieldRejected, reject.Kind);
        Assert.Contains("颗数", reject.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("StarMark.UI.exe", true)]
    [InlineData("a/b/lib.dll", true)]
    [InlineData("assets/fonts/x.ttf", true)]
    [InlineData("../Escape.dll", false)]                     // 写到安装目录之外（Zip Slip 的经典形状）
    [InlineData("/abs/StarMark.UI.exe", false)]
    [InlineData("C:/Windows/evil.dll", false)]
    [InlineData("a\\\\b\\\\lib.dll", false)]                 // 反斜杠：Windows 上就是另一条路径写法
    [InlineData("a//b.dll", false)]
    [InlineData("a/./b.dll", false)]
    [InlineData("a/../b.dll", false)]
    [InlineData("NUL.txt", false)]                           // Windows 设备名：解出来就是打不开的东西
    [InlineData("a/NUL.txt", false)]                         // 藏在子目录里也一样打不开
    [InlineData("PRN.exe", false)]
    [InlineData("COM1.dll", false)]
    [InlineData("LPT9.dll", false)]
    [InlineData("trailing.", false)]
    [InlineData(".hidden", false)]
    [InlineData("", false)]
    public void OnlyRelativePlainPathsAreAccepted(string path, bool allowed)
        => Assert.Equal(allowed, UpdateManifestCodec.IsSafeRelativePath(path.Replace("\\\\", "\\")));

    [Theory]
    [InlineData("StarMark-1.0.1-win-x64.zip", true)]
    [InlineData("StarMark-1.0.1-win-x64.ZIP", true)]
    [InlineData("sub/StarMark.zip", false)]
    [InlineData("StarMark.zip ", false)]
    [InlineData("StarMark.zip?token=1", false)]
    [InlineData("StarMark%2ezip", false)]
    [InlineData("StarMark.tar.gz", false)]
    [InlineData("", false)]
    public void ThePayloadNameMustBeABareZipFileName(string name, bool allowed)
        => Assert.Equal(allowed, UpdateManifestCodec.IsSafePackageName(name));

    [Fact]
    public void AManifestWithoutAnEntryExeIsRefused()
    {
        var json = ManifestJson(new Dictionary<string, string>
        {
            ["files"] = $$"""[ { "path": "README.md", "sha256": "{{EntryHash}}", "bytes": 3 } ]""",
        });
        Assert.False(UpdateManifestCodec.TryRead(Encoding.UTF8.GetBytes(json), out _, out var reject));
        Assert.Equal(ManifestRejectKind.FieldRejected, reject.Kind);
        Assert.Contains(UpdateAssets.EntryExeName, reject.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPathsDifferingOnlyByCaseAreTheSameFileOnWindows()
    {
        var json = ManifestJson(new Dictionary<string, string>
        {
            ["files"] = $$"""
                [ { "path": "StarMark.UI.exe", "sha256": "{{EntryHash}}", "bytes": 3 },
                  { "path": "starmark.ui.exe", "sha256": "{{LibraryHash}}", "bytes": 3 } ]
                """,
        });
        Assert.False(UpdateManifestCodec.TryRead(Encoding.UTF8.GetBytes(json), out _, out var reject));
        Assert.Equal(ManifestRejectKind.FieldRejected, reject.Kind);
    }

    [Fact]
    public void AFileEntryWithABadHashIsRefused()
    {
        var json = ManifestJson(new Dictionary<string, string>
        {
            ["files"] = """[ { "path": "StarMark.UI.exe", "sha256": "nope", "bytes": 3 } ]""",
        });
        Assert.False(UpdateManifestCodec.TryRead(Encoding.UTF8.GetBytes(json), out _, out var reject));
        Assert.Equal(ManifestRejectKind.FieldRejected, reject.Kind);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]     // 大写不算
    [InlineData("000000000000000000000000000000000000000000000000000000000000000", false)]      // 少一位
    [InlineData("000000000000000000000000000000000000000000000000000000000000000g", false)]
    [InlineData(null, false)]
    public void HashesAreLowercaseHexAndNothingElse(string? hex, bool ok)
        => Assert.Equal(ok, UpdateManifestCodec.IsLowercaseHex64(hex));

    /// <summary>
    /// 期望值与算出来的值要<b>逐字符相等</b>才算同一堆字节，所以字面写法只许有一处实现。
    /// 两各格式化（一处 <c>x2</c> 一处 <c>ToHexString</c>）漂开的表现是"包完全正确却永远验不过"，
    /// 而那条日志长得跟"有人在换包"一模一样。
    /// </summary>
    [Fact]
    public void TheHexSpellingComesFromExactlyOnePlace()
    {
        Assert.Equal("0aff00", UpdateHashHex.ToHex(new byte[] { 0x0A, 0xFF, 0x00 }));
        Assert.Equal(1, ReadRepoUnder("src").Sum(f => Count(Code(f.Text), "static string ToHex(ReadOnlySpan<byte>")));
    }

    // ===== 信任根 =====

    [Fact]
    public void TheEmbeddedKeyIsTheOnePublishingSignedWith()
    {
        var fingerprint = UpdateTrustAnchor.Embedded.Fingerprint();
        // 与 %USERPROFILE%\.starmark\update-signing.pub.pem 的 DER 摘要同一口径（openssl dgst -sha256 核过）
        Assert.Equal("86622c74f380f4e3a2df86bd2c1d5de93b2c2061f4c41f91ba048dc76364b231", fingerprint);
        Assert.Equal(64, fingerprint.Length);
    }

    [Fact]
    public void TheEmbeddedKeyStillMatchesTheGoldenOpenSslSignature()
    {
        var manifest = Convert.FromBase64String(GoldenManifestBase64);
        var signature = Convert.FromHexString(GoldenOpenSslSignatureHex);
        Assert.True(UpdateTrustAnchor.Embedded.Verify(manifest, signature));

        var touchedDocument = (byte[])manifest.Clone();
        touchedDocument[touchedDocument.Length - 2] ^= 0x01;              // 动原文一个字节
        Assert.False(UpdateTrustAnchor.Embedded.Verify(touchedDocument, signature));

        var touchedSignature = (byte[])signature.Clone();
        touchedSignature[touchedSignature.Length - 1] ^= 0x01;            // 动签名一个字节
        Assert.False(UpdateTrustAnchor.Embedded.Verify(manifest, touchedSignature));
    }

    [Fact]
    public void AManifestSignedByAnotherKeyIsNotTrusted()
    {
        using var other = new TestKey();
        var bytes = UpdateManifestCodec.Write(Manifest());
        Assert.True(other.Anchor.Verify(bytes, other.Sign(bytes)));                    // 它自己那把是好的（否则下一条红是假红）
        Assert.False(UpdateTrustAnchor.Embedded.Verify(bytes, other.Sign(bytes)));
    }

    /// <summary>格式钉成 DER，不吃默认值：P1363 那 64 字节拼接串长得像、验得过自家默认值，但不是标准格式。</summary>
    [Fact]
    public void OnlyDerFormattedSignaturesAreAccepted()
    {
        var bytes = UpdateManifestCodec.Write(Manifest());
        using var p1363Signed = new TestKey(DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var loose = p1363Signed.Sign(bytes);
        Assert.Equal(64, loose.Length);
        Assert.False(p1363Signed.Anchor.Verify(bytes, loose));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("不是 base64!!!")]
    [InlineData("AAAAAAAA")]
    public void AKeyThatCannotBeReadIsNotATrustAnchor(string? spki)
        => Assert.Null(UpdateTrustAnchor.FromSpki(spki));

    /// <summary>只认 256 位那一档：允许别的曲线就等于允许"换参数换出一把能验过的钥匙"。</summary>
    [Fact]
    public void AKeyOnAnotherCurveIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(UpdateTrustAnchor.FromSpki(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void SomethingThatIsNotAnSpkiAtAllIsRefused()
        => Assert.Null(UpdateTrustAnchor.FromSpki(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })));

    // ===== 逐项审：每一格都要点得出是谁挡的 =====

    [Fact]
    public void AWholeSignedMatchingPackageIsTrusted()
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        var result = UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, Tag, "1.0.0", key.Anchor);
        Assert.True(result.IsTrusted, result.Detail);
        Assert.Equal(2, result.Manifest!.Files.Count);
    }

    /// <summary>
    /// 生产那一个入口用的必须是<b>内嵌这把</b>钥匙：测试没有发布私钥，所以真清单从那个入口进去
    /// 必然落在"签名对不上"——这一条红过就说明有人给调用方开了一个"自己传公钥"的口子。
    /// </summary>
    [Fact]
    public void TheProductionEntryHasNoWayToSubstituteAnotherKey()
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        var result = UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, Tag, "1.0.0");
        Assert.Equal(UpdateIntegrity.Outcome.SignatureInvalid, result.Outcome);
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateIntegrity.cs"));
        Assert.Equal(1, Count(code, "UpdateTrustAnchor.Embedded"));
        Assert.Contains("Inspect(FetchedPackage package, string repository, string tag, string? localVersion)", code);
    }

    [Fact]
    public void AByteChangedManifestStopsAtTheSignature()
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        var bytes = UpdateManifestCodec.Write(manifest);
        var signature = key.Sign(bytes);                 // 先按<b>原文</b>签
        bytes[bytes.Length - 5] ^= 0x20;                 // 再动原文：改过的东西拿原签名对不上
        var bundle = new FetchedPackage(bytes, signature, "/tmp/x.zip", HexOf(Payload), Payload.LongLength);
        Assert.Equal(UpdateIntegrity.Outcome.SignatureInvalid,
            UpdateIntegrity.Inspect(bundle, Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ANonSignatureBlobIsRejectedWithoutCallingTheCryptoLayer(int length)
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        var bundle = new FetchedPackage(UpdateManifestCodec.Write(manifest), new byte[length],
            "/tmp/x.zip", HexOf(Payload), Payload.LongLength);
        Assert.Equal(UpdateIntegrity.Outcome.SignatureInvalid,
            UpdateIntegrity.Inspect(bundle, Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    [Fact]
    public void AManifestForAnotherVersionDoesNotGetToDescribeThisOne()
    {
        using var key = new TestKey();
        var manifest = Manifest(version: "9.9.9", packageHash: HexOf(Payload));
        Assert.Equal(UpdateIntegrity.Outcome.VersionMismatch,
            UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    /// <summary>标签写 <c>v1.0</c> 而清单写 <c>1.0.0</c>：数值同一版，认。</summary>
    [Fact]
    public void TheSameVersionWrittenTwoWaysIsStillTheSameVersion()
    {
        using var key = new TestKey();
        var manifest = Manifest(version: "1.0.0", package: "StarMark-1.0.0-win-x64.zip", packageHash: HexOf(Payload));
        Assert.Equal(UpdateIntegrity.Outcome.Trusted,
            UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, "v1.0", "0.9.0", key.Anchor).Outcome);
    }

    [Theory]
    [InlineData("1.0.1")]        // 同一版：装它是重装
    [InlineData("1.0.2")]        // 本机更新：装它是往回退
    [InlineData("2.0.0")]
    public void AnUpdateThatIsNotNewerThanLocalIsRefused(string local)
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        Assert.Equal(UpdateIntegrity.Outcome.RollbackRefused,
            UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, Tag, local, key.Anchor).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("读不懂")]
    public void WhenLocalVersionCannotBeReadNothingIsInstalled(string? local)
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        Assert.Equal(UpdateIntegrity.Outcome.LocalVersionUnknown,
            UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, Tag, local, key.Anchor).Outcome);
    }

    [Fact]
    public void APackageWhoseBytesDoNotMatchTheManifestIsTheOneToShoutAbout()
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        var bundle = Bundle(key, manifest, payload: SameLengthButDifferentBytes);
        Assert.Equal(UpdateIntegrity.Outcome.PackageHashMismatch,
            UpdateIntegrity.Inspect(bundle, Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    [Fact]
    public void APackageOfAnotherLengthIsRefusedBeforeItIsEvenHashed()
    {
        using var key = new TestKey();
        var manifest = Manifest(packageHash: HexOf(Payload));
        var lying = Bundle(key, manifest) with { PackageBytes = Payload.LongLength + 1 };
        Assert.Equal(UpdateIntegrity.Outcome.PackageSizeMismatch,
            UpdateIntegrity.Inspect(lying, Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    /// <summary>
    /// 载荷那颗的名字按标签算，清单那一栏只交叉核对。两边不是一把＝发布链某处错乱，
    /// 而"取回来的这一颗"与"清单描述的那一颗"必须是一件东西，否则哈希对得上也没意义。
    /// </summary>
    [Fact]
    public void AManifestNamingADifferentPayloadThanTheTagImpliesIsRefused()
    {
        using var key = new TestKey();
        var manifest = Manifest(package: "StarMark-1.0.1-win-x86.zip", packageHash: HexOf(Payload));
        Assert.Equal(UpdateIntegrity.Outcome.FieldRejected,
            UpdateIntegrity.Inspect(Bundle(key, manifest), Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    [Fact]
    public void AnOversizedManifestIsRefusedRatherThanParsed()
    {
        using var key = new TestKey();
        var padded = new byte[UpdateAssets.MaxManifestBytes + 1];
        UpdateManifestCodec.Write(Manifest()).CopyTo(padded, 0);
        var bundle = new FetchedPackage(padded, new byte[70], "/tmp/x.zip", HexOf(Payload), Payload.LongLength);
        Assert.Equal(UpdateIntegrity.Outcome.ManifestUnreadable,
            UpdateIntegrity.Inspect(bundle, Repository, Tag, "1.0.0", key.Anchor).Outcome);
    }

    // ===== 地址：仍然全部由我们拼 =====

    [Fact]
    public void TheAssetAddressIsBuiltFromRepositoryTagAndAssetName()
        => Assert.Equal($"https://github.com/{Repository}/releases/download/{Tag}/{UpdateAssets.ManifestAssetName}",
            UpdatePolicy.ReleaseAssetUrl(Repository, Tag, UpdateAssets.ManifestAssetName));

    [Theory]
    [InlineData("v1.0.1", "update-manifest.json", true)]
    [InlineData("v1/../2", "update-manifest.json", false)]
    [InlineData("v1.0.1 ", "update-manifest.json", false)]
    [InlineData("带中文的标签", "update-manifest.json", false)]
    [InlineData("v1.0.1", "sub/thing.json", false)]
    [InlineData("v1.0.1", "", false)]
    public void AnUnrecognisablePieceStopsTheWholeAddress(string tag, string asset, bool ok)
        => Assert.Equal(ok, UpdatePolicy.ReleaseAssetUrl(Repository, tag, asset) is not null);

    [Fact]
    public void TheOverrideRepositoryIsStillConfinedToOneSlash()
    {
        Assert.Equal("https://github.com/other/things/releases/download/v1.0.1/update-manifest.json",
            UpdatePolicy.ReleaseAssetUrl("other/things", "v1.0.1", UpdateAssets.ManifestAssetName));
        Assert.StartsWith($"https://github.com/{Repository}/releases/download/",
            UpdatePolicy.ReleaseAssetUrl("https://evil.test/a", "v1.0.1", UpdateAssets.ManifestAssetName),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AllThreeAddressesComeTogetherOrNotAtAll()
    {
        var addresses = UpdatePolicy.ReleaseAssetAddresses(Repository, Tag)!;
        Assert.Equal($"https://github.com/{Repository}/releases/download/{Tag}/update-manifest.json", addresses.ManifestUrl);
        Assert.Equal($"https://github.com/{Repository}/releases/download/{Tag}/update-manifest.sig", addresses.SignatureUrl);
        Assert.Equal($"https://github.com/{Repository}/releases/download/{Tag}/{PackageName}", addresses.PackageUrl);
        Assert.Null(UpdatePolicy.ReleaseAssetAddresses(Repository, "v1 0 1"));
        // 仓库那串不成话时不是"拼出一个能打到别处的地址"，也不是"什么都不发"——是折回默认仓库（RepositoryOf 的既有口径）
        Assert.StartsWith($"https://github.com/{Repository}/releases/download/",
            UpdatePolicy.ReleaseAssetAddresses("a/b/c", Tag)!.ManifestUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.0.1", "StarMark-1.0.1-win-x64.zip")]
    [InlineData("v1.0.1", "StarMark-1.0.1-win-x64.zip")]
    [InlineData("1.0", "StarMark-1.0.0-win-x64.zip")]
    public void ThePayloadNameIsDerivedFromTheVersion(string version, string expected)
    {
        Assert.True(AppVersion.TryParse(version, out var parsed));
        Assert.Equal(expected, UpdateAssets.PackageNameFor(AppVersion.Describe(parsed)));
    }

    // ===== 每一格都得有自己的那句话 =====

    [Theory]
    [MemberData(nameof(FetchStatuses))]
    public void EveryFetchOutcomeSaysSomethingOfItsOwn(PackageFetchStatus status)
    {
        var text = UpdatePolicy.Describe(status);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("失败", text, StringComparison.Ordinal);       // 并成一句"失败"就是要修的那个形状
        Assert.Equal(0, Enum.GetValues<PackageFetchStatus>()
            .Count(other => !other.Equals(status) && UpdatePolicy.Describe(other) == text));
    }

    [Theory]
    [MemberData(nameof(IntegrityOutcomes))]
    public void EveryIntegrityOutcomeSaysSomethingOfItsOwn(UpdateIntegrity.Outcome outcome)
    {
        var text = UpdatePolicy.Describe(outcome);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Equal(0, Enum.GetValues<UpdateIntegrity.Outcome>()
            .Count(other => !other.Equals(outcome) && UpdatePolicy.Describe(other) == text));
    }

    /// <summary>
    /// 签名与哈希那两格不是"再试一次就好"，而是"这里有人在换东西"：不许说成网络抖动那一类，
    /// 也不许让人去等——它们要被拒绝并且要被看见。
    /// </summary>
    [Theory]
    [InlineData(UpdateIntegrity.Outcome.PackageHashMismatch)]
    [InlineData(UpdateIntegrity.Outcome.SignatureInvalid)]
    public void TheSecurityArmsDoNotTellAnyoneToWait(UpdateIntegrity.Outcome outcome)
    {
        var text = UpdatePolicy.Describe(outcome);
        Assert.DoesNotContain("再试", text, StringComparison.Ordinal);
        Assert.DoesNotContain("连不上", text, StringComparison.Ordinal);
        Assert.Contains("已拒绝", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 审不过的每一格都必须说"没装"或"已拒绝"——这一层做的事只有"决定装不装"，说了别的就等于承诺了别的。
    /// <para>反过来那一刀更要紧：<b>哪一格都不许说"没动本机任何文件"</b>。走到这一层时临时载荷已经落过盘了，
    /// 那句话在这几格全是假话（能担保的只有"装目录里的东西没换"）。写测试时把安慰句当成真话钉上去，
    /// 就等于给将来的口径漂移发了一张通行证（#214 那一族：说出口的那句必须量得到）。</para>
    /// </summary>
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
    public void EveryRefusalSaysOnlyWhatThisLayerActuallyGuarantees(UpdateIntegrity.Outcome outcome)
    {
        var text = UpdatePolicy.Describe(outcome);
        Assert.DoesNotContain("没动本机任何文件", text, StringComparison.Ordinal);
        Assert.True(text.Contains("没装", StringComparison.Ordinal) || text.Contains("已拒绝", StringComparison.Ordinal),
            $"{outcome} 这一格没说要紧的那件事：到底装没装");
    }

    public static TheoryData<PackageFetchStatus> FetchStatuses()
    {
        var data = new TheoryData<PackageFetchStatus>();
        foreach (PackageFetchStatus value in Enum.GetValues<PackageFetchStatus>()) data.Add(value);
        return data;
    }

    public static TheoryData<UpdateIntegrity.Outcome> IntegrityOutcomes()
    {
        var data = new TheoryData<UpdateIntegrity.Outcome>();
        foreach (UpdateIntegrity.Outcome value in Enum.GetValues<UpdateIntegrity.Outcome>()) data.Add(value);
        return data;
    }

    // ===== 小工具 =====

    private static readonly byte[] Payload = Filler(0);
    private static readonly byte[] SameLengthButDifferentBytes = Filler(7);

    /// <summary>与清单里那颗 <c>packageBytes</c> 同长的假载荷（尺寸这一关必须先过，才谈得上哈希那一关）。</summary>
    private static byte[] Filler(int offset)
    {
        var bytes = new byte[244];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i + offset);
        return bytes;
    }

    private static string HexOf(byte[] bytes) => UpdateHashHex.ToHex(SHA256.HashData(bytes));

    private static ReleaseManifest Manifest(string version = "1.0.1", string package = PackageName,
        int schema = UpdateAssets.SupportedSchema, string? packageHash = null)
        => new(schema, version, package, packageHash ?? PayloadHash, 244, new[]
        {
            new ReleaseManifestFile("StarMark.UI.exe", EntryHash, 15),
            new ReleaseManifestFile("a/lib.dll", LibraryHash, 3),
        });

    private static string ManifestJson(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["schema"] = UpdateAssets.SupportedSchema.ToString(),
            ["version"] = "\"1.0.1\"",
            ["package"] = $"\"{PackageName}\"",
            ["packageSha256"] = $"\"{PayloadHash}\"",
            ["packageBytes"] = "244",
            ["files"] = $$"""
                [ { "path": "StarMark.UI.exe", "sha256": "{{EntryHash}}", "bytes": 15 },
                  { "path": "a/lib.dll", "sha256": "{{LibraryHash}}", "bytes": 3 } ]
                """,
        };
        if (overrides is not null) foreach (var (key, value) in overrides) fields[key] = value;
        return "{" + string.Join(",", fields.Select(f => $"\"{f.Key}\": {f.Value}")) + "}";
    }

    private static FetchedPackage Bundle(TestKey key, ReleaseManifest manifest, byte[]? payload = null)
    {
        var bytes = UpdateManifestCodec.Write(manifest);
        var body = payload ?? Payload;
        return new FetchedPackage(bytes, key.Sign(bytes), "/tmp/whatever.zip", HexOf(body), body.LongLength);
    }

    /// <summary>测试自己造的 P-256 钥匙对（发布那把私钥不入库，也不该进任何测试）。</summary>
    private sealed class TestKey : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly DSASignatureFormat _format;

        public TestKey(DSASignatureFormat format = DSASignatureFormat.Rfc3279DerSequence)
        {
            _format = format;
            Anchor = UpdateTrustAnchor.FromSpki(Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()))
                ?? throw new InvalidOperationException("自造的测试钥匙都不认，那 FromSpki 的判据就是空的");
        }

        public UpdateTrustAnchor Anchor { get; }

        public byte[] Sign(byte[] bytes) => _key.SignData(bytes, HashAlgorithmName.SHA256, _format);

        public void Dispose() => _key.Dispose();
    }
}
