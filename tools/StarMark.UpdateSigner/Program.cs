#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;

// 给一颗发布产物 zip 生成"应用认账"的清单与签名。
//
//   dotnet run --project tools/StarMark.UpdateSigner -c Release -- <package.zip> <version> <out-dir> [private-key.pem]
//
// 缺省私钥位置：%USERPROFILE%\.starmark\update-signing.pk8.pem
//
// 三件事按顺序做，每件都可能让工具**拒绝出东西**（宁可发不出版，也不要发出一份装不上的包）：
//   ① 逐颗文件算哈希、并确认包里有主程序（UpdateAssets.EntryExeName）；
//   ② 用 Core 那颗唯一的 <see cref="UpdateManifestCodec.Write"/> 写出清单字节（验签侧读的是同一颗函数）；
//   ③ 签完之后<b>立刻用内嵌的那把公钥自验一遍</b>——对不上就说明手上的私钥与源码里的公钥不是一对，
//      那种组合的后果是"发出去的每一版所有安装都拒绝更新"，所以它必须在这里而不是在用户机器上被发现。

var usage = "用法：UpdateSigner <package.zip> <version> <out-dir> [private-key.pem]";
if (args.Length < 3) { Fail(usage); return 1; }

var packagePath = Path.GetFullPath(args[0]);
var version = args[1].Trim();
var outDir = Path.GetFullPath(args[2]);
var keyPath = args.Length >= 4 ? Path.GetFullPath(args[3]) : DefaultKeyPath();

if (!File.Exists(packagePath)) { Fail($"找不到载荷：{packagePath}"); return 1; }
if (!AppVersion.TryParse(version, out var parsed)) { Fail($"版本号读不懂：{version}"); return 1; }
if (!File.Exists(keyPath)) { Fail($"找不到签名私钥：{keyPath}（它刻意不入库）"); return 1; }

Directory.CreateDirectory(outDir);
var packageBytes = new FileInfo(packagePath).Length;
if (packageBytes <= 0 || packageBytes > UpdateAssets.MaxPackageBytes)
{
    Fail($"载荷字节数不在这档：{packageBytes}");
    return 1;
}

var packageSha256 = UpdateHashHex.ToHex(SHA256.HashData(File.ReadAllBytes(packagePath)));
var files = ReadZip(packagePath);

var canonicalVersion = AppVersion.Describe(parsed);
// 载荷那颗的名字是**算出来的**（应用那边也这么算），zip 叫什么不由这里自由发挥：
// 名字对不上，用户机器上取的就是 404，而那边只会说"这一版没带更新包"。
var expectedPackage = UpdateAssets.PackageNameFor(canonicalVersion);
if (!string.Equals(Path.GetFileName(packagePath), expectedPackage, StringComparison.Ordinal))
{
    Fail($"载荷必须叫 {expectedPackage}（现在是 {Path.GetFileName(packagePath)}）：应用那边按标签算名字，不按清单说");
    return 1;
}

var manifest = new ReleaseManifest(
    UpdateAssets.SupportedSchema,
    canonicalVersion,                                  // 写规范化后的那一串，不写传进来的原样（v1.0 与 1.0.0 只能留一个）
    expectedPackage,
    packageSha256,
    packageBytes,
    files);

var manifestBytes = UpdateManifestCodec.Write(manifest);
var signature = Sign(keyPath, manifestBytes);

// ③ 自验：这一步就是"发出去的这一版，装着的这个程序认不认"。
if (!UpdateTrustAnchor.Embedded.Verify(manifestBytes, signature))
{
    Fail($"清单签好了但内嵌公钥不认——私钥与 {nameof(UpdateIntegrity)} 里的公钥不是一对，这份东西发出去只会让所有安装报“签名对不上”");
    return 1;
}

var manifestOut = Path.Combine(outDir, UpdateAssets.ManifestAssetName);
var signatureOut = Path.Combine(outDir, UpdateAssets.SignatureAssetName);
File.WriteAllBytes(manifestOut, manifestBytes);
File.WriteAllBytes(signatureOut, signature);

Console.WriteLine($"清单：{manifestOut}（{manifestBytes.Length} 字节，{files.Count} 颗文件）");
Console.WriteLine($"签名：{signatureOut}（{signature.Length} 字节，ECDSA-P256-SHA256 裸 DER）");
Console.WriteLine($"版本：{manifest.Version}　载荷：{manifest.Package}（{manifest.PackageBytes} 字节）");
Console.WriteLine($"公钥指纹：{UpdateTrustAnchor.Embedded.Fingerprint()}");
return 0;

static IReadOnlyList<ReleaseManifestFile> ReadZip(string packagePath)
{
    var read = new List<ReleaseManifestFile>();
    using var archive = ZipFile.OpenRead(packagePath);
    foreach (var entry in archive.Entries)
    {
        var path = UpdateAssets.NormalizeEntryName(entry.FullName);
        if (!UpdateAssets.IsFileEntry(path)) continue;                       // 目录项不是文件（判据与摊包那侧同一颗）
        if (!UpdateManifestCodec.IsSafeRelativePath(path))
            throw new InvalidDataException($"包里的名字不成话，装的时候会把文件放到安装目录之外：{path}");

        using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[64 * 1024];
        long total = 0;
        while (stream.Read(chunk) is var got and > 0)
        {
            total += got;
            hash.AppendData(chunk, 0, got);
        }
        read.Add(new ReleaseManifestFile(path, UpdateHashHex.ToHex(hash.GetHashAndReset()), total));
    }

    if (read.Count == 0) throw new InvalidDataException("包里一颗文件都没有");
    if (!read.Exists(f => string.Equals(f.Path, UpdateAssets.EntryExeName, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException($"包里没有 {UpdateAssets.EntryExeName}，这份东西装上去就没有能启动的程序");

    read.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));       // 排序＝同一堆字节永远签出同一个串
    return read;
}

static byte[] Sign(string keyPath, byte[] manifestBytes)
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(keyPath).AsSpan());
    // 格式与验签那侧一样写死成 DER，不吃默认值（默认值给的是 64 字节的 P1363，openssl 不认）
    return key.SignData(manifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
}

static string DefaultKeyPath() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".starmark", "update-signing.pk8.pem");

static void Fail(string message) => Console.Error.WriteLine($"[UpdateSigner] {message}");
