#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 冻结清单（<c>starmark-tree.json</c>，P-209）那一族的闸门。
/// <para><b>为什么这一族值得守</b>：v1.0.3 的 <c>stage\1.0.3</c> 在 zip 打完之后被重新产过一次树，
/// 事后量出 403 条目里 23 颗 CRC 不同——"审包认的字节"与"用户装到的字节"从那一刻起就不是同一批，
/// 而链上一句错误都没喊。所以产树那一侧把整棵树冻结成一份清单（名字↔sha256↔大小＋版本＋commit），
/// 三颗调用点（产完自证、打包对账、编安装包之前）用<b>同一把尺</b>读它。</para>
/// <para>钉的东西分两层：<b>形状</b>（名字进得了更新链、清单字节只由树内容决定、每句比较都带拒绝、
/// 三处调用共用一颗验证器）与<b>行为</b>（真跑那颗脚本：诚实的树绿，改一字节／缺清单／颗数不平都红——
/// ⛔ 绿＝这把尺是装饰；行为测是这一族的牙，形状钉只是它的形状）。</para>
/// </summary>
public sealed class FrozenTreeManifestGateTests
{
    private const string TreeScript = "scripts/publish-tree.ps1";
    private const string VerifyScript = "scripts/verify-tree.ps1";
    private const string PackageScript = "scripts/release-package.ps1";
    private const string MakerScript = "scripts/make-installer.ps1";
    private const string ManifestName = "starmark-tree.json";

    // ===== 名字：进得了更新链，且产／读两侧同一串 =====

    [Fact]
    public void TheManifestNameSurvivesTheUpdateChainAndIsSpelledTheSameWay()
    {
        // 清单会作为 zip 条目被签名工具枚进 update-manifest.json 的 files，随后要过 IsSafeRelativePath——
        // 那颗判据拒点开头的叶名：带点前缀的名字（比如 ".starmark-tree.json"）会让签名工具当场 throw，
        // 更新链整个断掉。名字能不能活，用应用自己那颗判据说了算，不靠肉眼。
        Assert.True(UpdateManifestCodec.IsSafeRelativePath(ManifestName),
            $"{ManifestName} 过不了 IsSafeRelativePath——这颗名字会让更新链拒收它自己（点开头的叶名是被拒的）");
        // 产清单（publish-tree）与读清单（verify-tree）两侧的字面必须是同一串：
        // 一处改名一处没跟，读的那侧只会报"缺清单"，没人知道名字为什么对不上。
        Assert.Contains(ManifestName, PsCode(ReadRepoFile(TreeScript)), StringComparison.Ordinal);
        Assert.Contains(ManifestName, PsCode(ReadRepoFile(VerifyScript)), StringComparison.Ordinal);
    }

    // ===== 冻结：清单字节只由树内容决定 =====

    [Fact]
    public void TheTreeIsFrozenDeterministicallyWithoutAnyTimestamp()
    {
        var code = PsCode(ReadRepoFile(TreeScript));
        // 排序钉死成 Ordinal：同一棵树两次产出的清单必须逐字节相同，
        // 否则"重产一次"与"被人动过"从清单上再也分不开（验收协议里那步"覆写自证"靠的就是它）。
        Assert.Contains("[Array]::Sort", code, StringComparison.Ordinal);
        Assert.Contains("[System.StringComparer]::Ordinal", code, StringComparison.Ordinal);
        // ⛔ 任何时间来源：清单里混进"产出的那一刻"，同一棵树就签不出同一份账。
        Assert.DoesNotMatch(@"Get-Date|DateTime|generatedAt|LastWriteTime", code);
        // 版本与 commit 也进账：版本还是 props 那颗唯一出处（同一正则），commit 由 git 说。
        Assert.Contains("<Version>([^<]+)</Version>", code, StringComparison.Ordinal);
        Assert.Contains("rev-parse HEAD", code, StringComparison.Ordinal);
        // 写盘那一下：无 BOM 的 UTF-8（清单是给机器读的数据，不是给控制台的）。
        Assert.Contains("UTF8Encoding", code, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::WriteAllText", code, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json", code, StringComparison.Ordinal);
    }

    // ===== 那把尺自己的纪律：只读、只比、比完就拒 =====

    [Fact]
    public void TheVerifierOnlyReadsAndEveryComparisonRefusesOnItsOwnLine()
    {
        var code = PsCode(ReadRepoFile(VerifyScript));
        // 尺只读不建也不搬：它若自己产过一棵树，"读回自证"就成了"自己验自己刚写的东西"。
        Assert.DoesNotContain("dotnet", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item", code, StringComparison.Ordinal);
        // 每一句比较都同行 throw：只查不拒＝那句比较是装饰（红的时候它还绿着）。
        var naked = code.Split('\n').Where(l => l.Contains(" -ne ") && !l.Contains("throw")).ToList();
        Assert.True(naked.Count == 0,
            "verify-tree 里有只比不拒的行：" + string.Join(" / ", naked));
    }

    // ===== 三颗调用点：同一把尺，一处实现 =====

    [Fact]
    public void TheSameRulerIsUsedWhereverTheTreeIsAudited()
    {
        foreach (var script in new[] { TreeScript, PackageScript, MakerScript })
        {
            var code = PsCode(ReadRepoFile(script));
            // 三处各自枚举＋比对，三把尺必漂成三个样（#189/#193）——所以只许调用，不许各自实现。
            Assert.Equal(1, Count(code, "verify-tree.ps1"));
            var at = code.IndexOf("verify-tree.ps1", StringComparison.Ordinal);
            var after = code[(at + "verify-tree.ps1".Length)..Math.Min(code.Length, at + 400)];
            var guard = after.IndexOf("-not $?", StringComparison.Ordinal);
            Assert.True(guard >= 0, $"{script} 调完那把尺没立刻看结果");
            Assert.Contains("throw", after[guard..Math.Min(after.Length, guard + 200)], StringComparison.Ordinal);
        }
        // 打包那一路必须把 zip 也拉进来对（P-209 的案发现场正是"zip 之后树被重产"）；
        // 产树那一路还没有 zip、造安装包那一路读的是同一棵树，两处都只对树。
        Assert.Contains("-ZipPath $zipPath", PsCode(ReadRepoFile(PackageScript)), StringComparison.Ordinal);
    }

    // ===== 行为：真跑那颗脚本——诚实的树绿，每一种谎都红 =====

    /// <summary>临时树：三颗文件，含一颗子目录里的——相对路径与递归两件事一次盖住。</summary>
    private static (string Dir, (string Path, byte[] Bytes)[] Files) HonestTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "starmark-p212-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var files = new (string, byte[])[]
        {
            ("StarMark.UI.exe", Encoding.UTF8.GetBytes("entry-bytes")),
            ("App.xbf", Encoding.UTF8.GetBytes("compiled-xaml")),
            ("Updater/StarMark.Updater.exe", Encoding.UTF8.GetBytes("updater-bytes")),
        };
        foreach (var (relative, bytes) in files)
        {
            var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return (dir, files);
    }

    /// <summary>写那份冻结清单：与 publish-tree 产的字段同一形状（version／commit／files{path,sha256,size}，
    /// 路径用更新链那种正斜杠规范形）。</summary>
    private static void WriteManifest(string treeDir, (string Path, byte[] Bytes)[] files, string version = "9.9.9")
    {
        var sb = new StringBuilder();
        sb.Append("{\"version\": \"").Append(version)
          .Append("\", \"commit\": \"").Append(new string('a', 40))
          .Append("\", \"files\": [");
        for (var i = 0; i < files.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("{\"path\": \"").Append(files[i].Path)
              .Append("\", \"sha256\": \"").Append(Sha256(files[i].Bytes))
              .Append("\", \"size\": ").Append(files[i].Bytes.Length).Append('}');
        }
        sb.Append("]}");
        File.WriteAllText(Path.Combine(treeDir, ManifestName), sb.ToString());
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>造那颗对照 zip：条目名用反斜杠（Compress-Archive 在 PS 5.1 里的形状），
    /// 清单在根（它本来就是树里的文件，打的是整棵树）。</summary>
    private static string MakeZip(string treeDir, (string Path, byte[] Bytes)[] files, string? extraEntry = null)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "starmark-p212-" + Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (relative, bytes) in files)
        {
            var entry = zip.CreateEntry(relative.Replace('/', '\\'));
            using var s = entry.Open();
            s.Write(bytes, 0, bytes.Length);
        }
        var manifestBytes = File.ReadAllBytes(Path.Combine(treeDir, ManifestName));
        var manifestEntry = zip.CreateEntry(ManifestName);
        using (var s = manifestEntry.Open())
            s.Write(manifestBytes, 0, manifestBytes.Length);
        if (extraEntry is not null)
        {
            var extra = zip.CreateEntry(extraEntry.Replace('/', '\\'));
            using var s = extra.Open();
            s.Write(new byte[] { 1 }, 0, 1);
        }
        return zipPath;
    }

    private static (int Exit, string Output) RunVerifier(string treeDir, string? expectVersion = null, string? zipPath = null)
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-tree.ps1");
        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -TreeDir \"{treeDir}\"";
        if (expectVersion is not null) args += $" -ExpectVersion {expectVersion}";
        if (zipPath is not null) args += $" -ZipPath \"{zipPath}\"";
        using var p = Process.Start(new ProcessStartInfo("powershell", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            throw new InvalidOperationException("verify-tree.ps1 两分钟没跑完——是挂了，不是红：" + output);
        }
        return (p.ExitCode, output);
    }

    [Fact]
    public void AnHonestTreeAndAnHonestZipPass()
    {
        var (tree, files) = HonestTree();
        WriteManifest(tree, files);
        var (exit, output) = RunVerifier(tree, "9.9.9");
        Assert.True(exit == 0, "树与清单是同一批字节时必须绿，却红了：" + output);

        // zip 那一半也得真绿过一次：绿＝这把尺是装饰的反面（见验收协议第一步）。
        var zipPath = MakeZip(tree, files);
        (exit, output) = RunVerifier(tree, "9.9.9", zipPath);
        Assert.True(exit == 0, "zip 与树同一批字节时必须绿，却红了：" + output);
    }

    [Fact]
    public void AByteTouchedAfterTheFreezeIsRefused()
    {
        var (tree, files) = HonestTree();
        WriteManifest(tree, files);
        // 清单记的还是旧字节：改一颗文件的一颗字节，尺必须当场红——
        // 这正是 P-209 的案发现场（"签出去的字节"与"树里的字节"被人悄悄换开）。
        File.WriteAllBytes(Path.Combine(tree, "App.xbf"), Encoding.UTF8.GetBytes("changed-after-freeze"));
        var (exit, output) = RunVerifier(tree);
        Assert.True(exit != 0, "清单冻结之后改过字节，尺却还绿着——那它就是装饰：" + output);
    }

    [Fact]
    public void AMissingManifestIsRefused()
    {
        var (tree, files) = HonestTree();
        WriteManifest(tree, files);
        File.Delete(Path.Combine(tree, ManifestName));
        var (exit, output) = RunVerifier(tree);
        Assert.True(exit != 0, "树里没有冻结清单，尺却还绿着——那它量的是空气：" + output);
    }

    [Fact]
    public void CountAndZipAndVersionMismatchesAreAllRefused()
    {
        // 树里多一颗清单没记的（或反过来少记），颗数先不平。
        var (tree, files) = HonestTree();
        WriteManifest(tree, files);
        File.WriteAllBytes(Path.Combine(tree, "extra.dll"), Encoding.UTF8.GetBytes("not-in-manifest"));
        var (exit, output) = RunVerifier(tree);
        Assert.True(exit != 0, "树里多了一颗清单没记的文件，尺却还绿着：" + output);

        // zip 里多一条清单没记的条目：审包那侧的"逐颗全等"会拒收，这里必须先红。
        var (tree2, files2) = HonestTree();
        WriteManifest(tree2, files2);
        var zipPath = MakeZip(tree2, files2, extraEntry: "smuggled.dll");
        (exit, output) = RunVerifier(tree2, "9.9.9", zipPath);
        Assert.True(exit != 0, "zip 里混进了清单没记的条目，尺却还绿着：" + output);

        // 版本对不上：要出的是 8.8.8、清单记的是 9.9.9，这一棵就不是"这一版"的树。
        (exit, output) = RunVerifier(tree2, "8.8.8");
        Assert.True(exit != 0, "清单记的版本与要出的那一版对不上，尺却还绿着：" + output);
    }
}
