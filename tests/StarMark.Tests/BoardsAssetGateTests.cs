using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 随包资产那面闸门（批次 Phase C 片18）：冻结清单（manifest.json）与盘上那棵
/// <c>assets/boards/</c> 逐档对账（路径集合相等＋逐档 bytes/sha256 相等）＋形状判据。
/// 清单是<b>现读</b>（stage-assets.py 每次重跑都会刷新 generatedAtUtc 与片数），
/// 所以这里⛔ 写死「几片／几 MB」——那类期望值属于清单本档，⛔ 属于测试。
/// </summary>
public sealed class BoardsAssetGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string AssetRoot => Path.Combine(RepoRoot, "assets", "boards");

    private sealed record Entry(string Path, long Bytes, string Sha256);

    private static (List<Entry> Entries, JsonElement Source) LoadManifest()
    {
        var manifestPath = Path.Combine(AssetRoot, BoardsAssetPolicy.ManifestFileName);
        Assert.True(File.Exists(manifestPath), "随包清单不在：" + manifestPath);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        Assert.Equal(BoardsAssetPolicy.ManifestSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        var files = new List<Entry>();
        foreach (var e in root.GetProperty("files").EnumerateArray())
            files.Add(new Entry(e.GetProperty("path").GetString()!, e.GetProperty("bytes").GetInt64(), e.GetProperty("sha256").GetString()!));
        return (files, root.GetProperty("source"));
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    [Fact]
    public void ManifestEntriesAreSafeKnownPathsAndUnique()
    {
        var (entries, _) = LoadManifest();
        Assert.NotEmpty(entries);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            Assert.True(BoardsAssetPolicy.IsSafeAssetRelativePath(e.Path), "清单里出现Unsafe相对路径：" + e.Path);
            Assert.True(BoardsAssetPolicy.IsKnownAssetPath(e.Path), "清单里出现未知形状的档：" + e.Path);
            Assert.True(seen.Add(e.Path), "清单里重复路径：" + e.Path);
        }
    }

    [Fact]
    public void DiskTreeEqualsManifestExactly()
    {
        var (entries, _) = LoadManifest();
        var manifestPaths = entries.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var diskPaths = Directory
            .EnumerateFiles(AssetRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(AssetRoot, p).Replace('\\', '/'))
            .Where(p => p != BoardsAssetPolicy.ManifestFileName)
            .ToHashSet(StringComparer.Ordinal);
        var missing = manifestPaths.Except(diskPaths).OrderBy(p => p).ToList();
        var orphan = diskPaths.Except(manifestPaths).OrderBy(p => p).ToList();
        Assert.True(missing.Count == 0, "清单有而盘上缺：" + string.Join(", ", missing.Take(5)));
        Assert.True(orphan.Count == 0, "盘上有而清单没有（冻结清单⛔ 容孤儿）：" + string.Join(", ", orphan.Take(5)));
    }

    [Fact]
    public void EveryFileBytesAndShaMatchManifest()
    {
        var (entries, _) = LoadManifest();
        Assert.All(entries, e =>
        {
            var p = Path.Combine(AssetRoot, e.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(p), "缺档：" + e.Path);
            var bytes = new FileInfo(p).Length;
            Assert.True(bytes == e.Bytes, $"字节数不符：{e.Path} 盘上 {bytes} 清单 {e.Bytes}");
            var sha = Sha256(p);
            Assert.True(string.Equals(sha, e.Sha256, StringComparison.Ordinal), $"sha256 不符：{e.Path}");
        });
    }

    [Fact]
    public void BundleExportsOurGlobalAndCarriesMermaidStubMarker()
    {
        var bundlePath = Path.Combine(AssetRoot, BoardsAssetPolicy.BundleFileName);
        Assert.True(File.Exists(bundlePath), "随包 bundle 不在");
        var text = File.ReadAllText(bundlePath);
        Assert.True(text.Contains(BoardsAssetPolicy.BundleGlobalName, StringComparison.Ordinal),
            "bundle 出口⛔ 是 " + BoardsAssetPolicy.BundleGlobalName + "（entry.js 那颗唯一出口）");
        Assert.True(text.Contains(BoardsAssetPolicy.MermaidStubMarker, StringComparison.Ordinal),
            "mermaid 摘除标记不在 bundle 里（决策 4：stub 那颗报错串必须能搜到）");
    }

    [Fact]
    public void ManifestCoversEveryRequiredFamilyAndXiaolai()
    {
        var (entries, _) = LoadManifest();
        var byFamily = entries
            .Where(e => e.Path.StartsWith(BoardsAssetPolicy.FontsRootName + "/", StringComparison.Ordinal))
            .GroupBy(e => e.Path.Split('/')[1])
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var fam in BoardsAssetPolicy.RequiredFontFamilies)
            Assert.True(byFamily.ContainsKey(fam), "必需字体族缺随包：" + fam);
        Assert.True(byFamily.ContainsKey(BoardsAssetPolicy.XiaolaiFamilyName) && byFamily[BoardsAssetPolicy.XiaolaiFamilyName] > 0,
            "Xiaolai 主区片⛔ 能是 0（决策 3 的中档）");
        var strayFamilies = byFamily.Keys
            .Where(f => f != BoardsAssetPolicy.XiaolaiFamilyName && Array.IndexOf(BoardsAssetPolicy.RequiredFontFamilies, f) < 0)
            .ToList();
        Assert.True(strayFamilies.Count == 0, "字体树里出现名单外的族：" + string.Join(", ", strayFamilies));
    }

    [Fact]
    public void UnsafeRelativePathsAreRejected()
    {
        Assert.True(BoardsAssetPolicy.IsSafeAssetRelativePath("fonts/Xiaolai/abc.woff2"));
        Assert.True(BoardsAssetPolicy.IsSafeAssetRelativePath(BoardsAssetPolicy.BundleFileName));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath(null));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath(""));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath("../escape.woff2"));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath("fonts/../escape.woff2"));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath("/abs/abs.woff2"));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath("fonts\\Xiaolai\\a.woff2"));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath("C:/x.woff2"));
        Assert.False(BoardsAssetPolicy.IsSafeAssetRelativePath("fonts//a.woff2"));
    }

    [Fact]
    public void TheTwoExportDialogsAreGatedByTheirOwnKnobs()
    {
        var text = File.ReadAllText(Path.Combine(AssetRoot, BoardsAssetPolicy.BundleFileName));
        const string imageGate =
            @"(?<![\w$.])[\w$]+=\(\)=>![\w$]+\.canvasActions\.saveAsImage\|\|[\w$]+\.openDialog\?\.name!==""imageExport""\?null:\(0,[\w$]+\.jsx\)\([\w$]+,";
        const string jsonGate =
            @"(?<![\w$.])[\w$]+=\(\)=>(?<options>[\w$]+)\.canvasActions\.export\?\(0,[\w$]+\.jsx\)\((?<component>[\w$]+),\{[^{}]*\}\):null(?=[,;])";
        var imageMatches = Regex.Matches(text, imageGate);
        var jsonMatches = Regex.Matches(text, jsonGate);
        Assert.True(imageMatches.Count == 1, "图片框必须由 saveAsImage 与 imageExport 共同控制，关闭时返回 null");
        Assert.True(jsonMatches.Count == 1, "JSON 组件必须仅在 export 为真时挂载，为假时返回 null");
        var jsonComponent = jsonMatches[0].Groups["component"].Value;

        // 不解析模板、正则或注释；出现这些语法时拒绝匹配，不能跨到相邻组件借条件。
        const string bodyPattern = """\{(?>[^{}"'`/]+|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|(?<brace>\{)|(?<-brace>\}))*(?(brace)(?!))\}""";
        var definitionPattern = @"(?<![\w$.])" + Regex.Escape(jsonComponent)
            + @"=\(\{elements:[\w$]+,appState:(?<state>[\w$]+),[^{}]*\}\)=>(?<body>" + bodyPattern + ")";
        var definitions = Regex.Matches(text, definitionPattern, RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.True(definitions.Count == 1, jsonComponent + " 必须有唯一且可完整读取的组件定义");
        var body = definitions[0].Groups["body"].Value;
        var state = Regex.Escape(definitions[0].Groups["state"].Value);
        var guard = @"return\(0,[\w$]+\.jsx\)\([\w$]+\.Fragment,\{children:" + state
            + @"\.openDialog\?\.name===""jsonExport""&&\(0,[\w$]+\.jsx\)\(";
        Assert.Matches(guard, body);
        var dialogs = Regex.Matches(body, @"openDialog\?\.name===""([\w$]+)""")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(new[] { "jsonExport" }, dialogs);
        Assert.DoesNotMatch(@"[\w$]+\.canvasActions\.export\|\|[\w$]+\.openDialog\?\.name!==""imageExport""", text);
        Assert.DoesNotMatch(@"[\w$]+\.canvasActions\.saveAsImage\?\(0,[\w$]+\.jsx\)\(", text);
    }
}
