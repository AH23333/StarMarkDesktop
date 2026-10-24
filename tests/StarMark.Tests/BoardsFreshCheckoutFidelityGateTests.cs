using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 新克隆那把尺（批次 Phase C 片102・账本 P-251 的②）：「盘上==清单」只在写这棵树的那台机上成立过——
/// <c>core.autocrlf=true</c> 的一台新机克隆仓库时，git 会把清单钉住的文本资产行尾重写而清单不知道
/// （片71 现读：改前 HEAD 新落干净工作树，140 颗里 4 颗对不上）。那把尺此前只在
/// <c>bin/WI98/clone71_check.py</c> 里手动跑＝每次发版<b>不会</b>真跑那一步；这一族把它搬进常跑闸门。
/// <para>
/// 方法论沿正本形状：<c>git worktree add --detach</c> 新落一棵干净工作树——<b>走真转换管线</b>；
/// <c>git show</c>／<c>git archive</c> 都不走转换，用它们量⛔ 到这个病。
/// </para>
/// <para>口径（P-251 ③，同句被钉在 <c>.gitattributes</c> 头注里）：凡「清单＋字节对账」的资产树，随包之前必须有一把新克隆尺。</para>
/// </summary>
public sealed class BoardsFreshCheckoutFidelityGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunGit(string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static string Git(string arguments)
    {
        var (code, stdout, stderr) = RunGit(arguments);
        Assert.True(code == 0, "git " + arguments.Split(' ')[0] + " 退出码 " + code + "：" + stderr.Trim());
        return stdout;
    }

    private sealed record Entry(string Path, long Bytes, string Sha256);

    private static List<Entry> LoadManifest(string assetRoot)
    {
        var manifestPath = Path.Combine(assetRoot, BoardsAssetPolicy.ManifestFileName);
        Assert.True(File.Exists(manifestPath), "随包清单不在：" + manifestPath);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        Assert.Equal(BoardsAssetPolicy.ManifestSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        var files = new List<Entry>();
        foreach (var e in root.GetProperty("files").EnumerateArray())
            files.Add(new Entry(e.GetProperty("path").GetString()!, e.GetProperty("bytes").GetInt64(), e.GetProperty("sha256").GetString()!));
        return files;
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 那把尺本体：把 HEAD 新落成一棵干净工作树（走真转换管线），新树逐档对<b>它自己的</b>清单
    /// （bytes＋sha256），主人两档（片21 ⑦ 那对）在新树上再量一次逐字节相等。
    /// 行尾转换回来＝新树字节对不上清单＝红；清单漂了＝同样红。
    /// </summary>
    [Fact]
    public void FreshCheckoutOfHeadSatisfiesItsOwnManifest()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "starmark_fresh_checkout_" + Guid.NewGuid().ToString("N"));
        try
        {
            Git("worktree add --detach \"" + scratch + "\" HEAD");
            var assetRoot = Path.Combine(scratch, "assets", "boards");
            var entries = LoadManifest(assetRoot);
            Assert.NotEmpty(entries);
            var mismatched = new List<string>();
            foreach (var e in entries)
            {
                var p = Path.Combine(assetRoot, e.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p))
                {
                    mismatched.Add(e.Path + " 缺档");
                    continue;
                }
                var bytes = new FileInfo(p).Length;
                if (bytes != e.Bytes)
                {
                    mismatched.Add(e.Path + " 盘上 " + bytes + " 清单 " + e.Bytes);
                    continue;
                }
                if (!string.Equals(Sha256(p), e.Sha256, StringComparison.Ordinal))
                    mismatched.Add(e.Path + " sha256 不符");
            }
            Assert.True(mismatched.Count == 0,
                "新落的工作树对不上自己的清单（" + mismatched.Count + " 颗）——行尾转换又回来了？：" + string.Join("; ", mismatched.Take(5)));
            foreach (var fileName in new[] { BoardsAssetPolicy.BridgeJsFileName, BoardsAssetPolicy.PageFileName })
            {
                var owner = File.ReadAllBytes(Path.Combine(scratch, "tools", "boards", fileName));
                var shipped = File.ReadAllBytes(Path.Combine(assetRoot, fileName));
                Assert.True(owner.SequenceEqual(shipped), "主人与随包那份在新落的工作树里不再逐字节相等：" + fileName);
            }
        }
        finally
        {
            RunGit("worktree remove --force \"" + scratch + "\"");
            RunGit("worktree prune");
        }
    }

    /// <summary>
    /// 机制那一面（P-251 ①）：被字节对账钉住的两棵树逐颗 <c>check-attr text</c> 必须是 <c>unset</c>
    /// （<c>-text</c> 生效的报法；没圈到报 <c>unspecified</c>）。上面那格量的是<b>结果</b>（新树字节），
    /// 这一格钉的是<b>机制</b>（attr 在）——在 autocrlf 关着的机上，删掉 -text 上面那格照绿而病已埋下。
    /// </summary>
    [Fact]
    public void EveryAccountedFileIsPinnedAgainstLineEndingConversion()
    {
        var paths = Directory.EnumerateFiles(Path.Combine(RepoRoot, "assets", "boards"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "tools", "boards"), "*", SearchOption.AllDirectories))
            .Select(p => Path.GetRelativePath(RepoRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(paths);
        var output = Git("check-attr text -- " + string.Join(" ", paths.Select(p => "\"" + p + "\"")));
        var reported = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(paths.Count, reported.Length);
        Assert.All(reported, line => Assert.True(line.TrimEnd('\r').EndsWith(": text: unset", StringComparison.Ordinal),
            "这颗档没被 .gitattributes 圈进 -text：" + line.TrimEnd('\r') + "——它若被转换，字节对账就只在写它的那台机上成立"));
    }

    /// <summary>
    /// 口径那一面（P-251 ③）：<c>.gitattributes</c> 里那两行 <c>-text</c> 与那句口径句都在——
    /// 删掉口径句，下一棵「清单＋字节对账」的树就会再走一遍片71 那条老路。
    /// </summary>
    [Fact]
    public void TheGitattributesKeepsTheFreshCheckoutMandate()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, ".gitattributes"));
        Assert.True(text.Contains("assets/boards/** -text", StringComparison.Ordinal), "assets/boards/** -text 那行不在了");
        Assert.True(text.Contains("tools/boards/** -text", StringComparison.Ordinal), "tools/boards/** -text 那行不在了");
        Assert.True(text.Contains("随包之前必须有一把新克隆尺", StringComparison.Ordinal),
            ".gitattributes 头注里那句口径（随包之前必须有一把新克隆尺）不在了——P-251 ③");
    }
}
