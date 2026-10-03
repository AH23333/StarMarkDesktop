#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 账本里点名的源文件必须还找得到（批次 UK）。
/// <para>
/// 这条规矩是批次 R7 立的：「凡拆分/搬家一个被文档点过行号的文件，同批回扫 <c>docs/</c> 里的旧路径并回写」。
/// 当时数出 11 处 <c>文件:行号</c> 指不到东西，成因就是 S4 拆千行大文件之后没人回扫——
/// 而**形状闸门测不到这件事**：<c>SourceGate</c> 读的是磁盘文件，指针一漂只是"读不到东西"，不会测红。
/// 所以那条规矩一直只有话没有证人。本批给它配一个。
/// </para>
/// <para>
/// <b>只守 <c>docs/待决策事项.md</c> 一份，是判据范围不是偷懒</b>：账本是"下一次动手要照着做"的清单，
/// 指针断了就是把人领去改一颗不存在的地方（或者更糟：以为那条规则已经消失）。
/// 进度报告／坑表／审查报告是<b>历史快照</b>——那一行当时是真的，文件后来被删被搬是演化；
/// 拿今天的存在性去罚历史文档，等于逼下一手改写证据（坑表 #160 那一族的口径：快照要么更新，要么标"已被取代"，不能倒着改）。
/// </para>
/// <para>
/// <b>只查文件在不在，不查行号</b>：行号随任何一次合法编辑漂移，罚它＝每批都要回来改文档，
/// 这种闸门三批之后就会被下一手整条关掉——那才是真的把尺子弄丢。
/// </para>
/// </summary>
public sealed class LedgerPointerGateTests
{
    private const string Ledger = "docs/待决策事项.md";

    /// <summary>账本靠"点够得着的源文件"活着：掉到几十条就说明扫描器坏了或文档改名了，而不是指针都对了。</summary>
    private const int MinPointersExpected = 200;

    /// <summary>参照工程（ppInk）的文件：账本引它们是为了说明"别人那版是怎么做的"，本来就不在本仓里。</summary>
    private static readonly string[] ExternalProjectFiles = { "FormDisplay.cs", "FormCollection.cs", "Root.cs" };

    /// <summary>
    /// 只出现在"当初为什么删掉它"那类叙述里的文件——而且必须带着它的出处（<c>git log</c> 那条命令）出现，
    /// 否则就是有人把"今天也在这里"的指针重新写活了。豁免的<see cref="TheExemptionsAreStillNeededAndHonest">理由本身也被钉着</see>。
    /// <para><c>SeedData.cs</c> 是批次 VQ 删掉的那颗空库 seeder：账本要说清"用户看到的那条死路径从哪来"就绕不开它，
    /// 而它在今天的仓库里确实不存在——正是这一类豁免的用途（不是给闸门开洞，是它本来就属于那一类）。</para>
    /// </summary>
    private static readonly string[] GitHistoryOnlyFiles = { "TimestampConverter.cs", "SeedData.cs" };

    /// <summary>
    /// 形如 <c>Foo.cs</c>／<c>src/Bar/Foo.cs:123</c>／<c>Foo.xaml:40</c>／<c>run.ps1</c>／<c>StarMark.sln</c>。
    /// 三处刻意的讲究，少了任何一处都会把尺子读成假红（批次 UK 头一遍就全踩过）：
    /// ① <c>(?![\w])</c> 不吃 <c>StarMark.UI.csproj</c>（否则会被读成 <c>StarMark.UI.cs</c>）；
    /// ② 名字必须以单词字符开头，且整段吃到 <c>.xaml.cs</c> 而不切剩 <c>.cs</c>；
    /// ③ 账本用简写指 partial（<c>Search.cs</c> 实为 <c>ItemRepository.Search.cs</c>），所以解析要允许尾段匹配。
    /// </summary>
    private static readonly Regex Pointer = new(
        @"(?<![\w./-])([A-Za-z0-9_][A-Za-z0-9_.\-/]*\.(?:cs|xaml|ps1|sln))(?![\w])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] Roots = { "src", "tests", "tools", "scripts", "build" };

    private static List<string> LedgerLines() => SourceGate.ReadRepoFile(Ledger).Replace("\r\n", "\n").Split('\n').ToList();

    /// <summary>仓库里现存的、可被账本点名的文件。<b>必须跳过构建目录</b>：obj/ 里全是生成的 .xaml/.cs 副本，
    /// 连着它们一起认，"文件已被删掉"这一类断链就永远测不出来（闸门当场变成假绿）。</summary>
    private static List<string> RepoSourceFiles()
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "TestResults", ".vs", ".git" };
        var all = new List<string>();

        void Walk(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(f);
                if (Pointable(ext)) all.Add(Rel(f));
            }
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!skip.Contains(Path.GetFileName(sub))) Walk(sub);   // 递归但剪掉构建目录
        }

        foreach (var r in Roots) Walk(Path.Combine(SourceGate.RepoRoot(), r));
        foreach (var f in Directory.EnumerateFiles(SourceGate.RepoRoot()))
            if (f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                all.Add(Path.GetFileName(f));
        return all;
    }

    private static bool Pointable(string ext) =>
        ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) || ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase) || ext.Equals(".sln", StringComparison.OrdinalIgnoreCase);

    private static string Rel(string absolute) => Path.GetRelativePath(SourceGate.RepoRoot(), absolute).Replace('\\', '/');

    /// <summary>指针名 → 仓库里对得上的相对路径（对不上返回空表）。尾段匹配是为了账本那种简写。</summary>
    private static List<string> Resolve(string pointer, List<string> existing)
    {
        var name = pointer.TrimStart('/');
        var baseName = name.Split('/')[^1];
        return existing.Where(rel =>
        {
            var relBase = rel.Split('/')[^1];
            return string.Equals(relBase, baseName, StringComparison.OrdinalIgnoreCase)
                   || rel.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)
                   || relBase.EndsWith("." + baseName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static List<(int Line, string Pointer, string Context)> AllPointers()
    {
        var hits = new List<(int, string, string)>();
        var lines = LedgerLines();
        for (var i = 0; i < lines.Count; i++)
            foreach (Match m in Pointer.Matches(lines[i]))
                hits.Add((i + 1, m.Groups[1].Value, lines[i].Trim()));
        return hits;
    }

    /// <summary>扫描器要有牙：正对照必须命中，假名字必须判缺，两类陷阱（<c>.csproj</c> 与 partial 简写）要按今天的行为处理。</summary>
    [Fact]
    public void ThePointerScannerActuallyBites()
    {
        var existing = RepoSourceFiles();

        Assert.Equal("ItemRepository.Search.cs", Pointer.Match("依据见 `ItemRepository.Search.cs:622`。").Groups[1].Value);
        Assert.Equal("SettingsPage.xaml.cs", Pointer.Match("那一行在 SettingsPage.xaml.cs 里").Groups[1].Value);
        Assert.Empty(Pointer.Matches("跑 build StarMark.UI.csproj 就行"));      // 工程文件不是源文件指针（少了 (?![\w]) 就会被读成 StarMark.UI.cs）
        Assert.Single(Resolve("StartupProfileTests.cs", existing));             // tests/ 下的文件也算（头一遍只扫 src/ 就判了假断链）
        Assert.NotEmpty(Resolve("Search.cs", existing));                        // 账本用简写：ItemRepository.Search.cs
        Assert.StartsWith("src/StarMark.UI", Resolve("MainWindow.xaml", existing).Single(), StringComparison.Ordinal);
        Assert.NotEmpty(Resolve("StarMark.sln", existing));                     // 根上的解也算，别只认 src/
        Assert.Single(Resolve("ItemRepository.Search.cs", existing));           // 全名要精确到那一颗，不是"任何 .Search.cs"
        Assert.Empty(Resolve("NoSuchFileEverExisted.cs", existing));            // 这才叫"能判缺"
        Assert.Empty(Resolve("FormDisplay.cs", existing));                      // 白名单那几颗确实不在本仓
    }

    /// <summary>账本确实靠指针活着；命中数掉了说明扫描器坏了，而不是"指针都对了"。</summary>
    [Fact]
    public void TheLedgerIsFullOfPointersWorthGuarding()
    {
        var hits = AllPointers();
        Assert.True(hits.Count >= MinPointersExpected,
            $"账本只解析出 {hits.Count} 处文件指针（预期至少 {MinPointersExpected}）⇒ 先怀疑扫描器或文档改名，别改这条阈值");
    }

    /// <summary>主判据：账本点名的源文件必须还在，除非它带着写在豁免表里的那两个理由之一。</summary>
    [Fact]
    public void EverySourceFileNamedInTheLedgerStillExists()
    {
        var existing = RepoSourceFiles();
        var exempt = ExternalProjectFiles.Concat(GitHistoryOnlyFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var broken = AllPointers()
            .Where(h => Resolve(h.Pointer, existing).Count == 0)
            .ToList();

        var unjustified = broken.Where(b => !exempt.Contains(b.Pointer.Split('/')[^1])).ToList();
        Assert.True(unjustified.Count == 0,
            "账本里有指针指不到任何文件（搬家/删文件时忘了回写，规矩见 R7 第七节）：\n" +
            string.Join("\n", unjustified.Take(12).Select(u => $"  第 {u.Line} 行 `{u.Pointer}` ⇒ {u.Context}")));
    }

    /// <summary>
    /// 豁免不许烂：每一颗都必须①仍在账本里出现、②仍然指不到本仓文件、③带着它自己的理由出现。
    /// <para>ppInk 那三颗的理由是"参照工程只读引用"；<c>TimestampConverter.cs</c> 的理由只能是
    /// "它出现在 <c>git log --follow</c> 那类查历史的句子里"——所以那句上下文也一起钉住，
    /// 免得下一手把"今天也在这里"的指针借豁免写活。</para>
    /// </summary>
    [Fact]
    public void TheExemptionsAreStillNeededAndHonest()
    {
        var existing = RepoSourceFiles();
        var lines = LedgerLines();

        foreach (var name in ExternalProjectFiles.Concat(GitHistoryOnlyFiles))
        {
            var where = lines.Select((t, i) => (i + 1, t)).Where(x => x.t.Contains(name, StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(where);                                        // 不再被提到 ⇒ 该把这颗从豁免表里删掉
            Assert.Empty(Resolve(name, existing));                         // 突然又能指到了 ⇒ 同上
            if (GitHistoryOnlyFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                Assert.All(where, w => Assert.Contains("git ", w.t, StringComparison.Ordinal));
        }
    }
}
