#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 注释里那颗「测试方法名」指针的<b>在场</b>闸门（批次 Phase C 片65）。
/// <para>
/// 来历：片64 把画板那一族的名册从 23 行加到 27 行，于是把 <c>BoardWordGateTests</c> 里那格<b>改了名</b>，
/// 而 <c>BoardWindow.cs</c> 的注释还指着旧名 <c>…TwentyThreeRows…</c>——全量一颗也没红（那颗红名压根不在测试里），
/// 而下一位读者拿它去查名册会扑空。同一把普查还抓到第二颗更早的：<c>NumberText.cs</c> 从 批次 RZ 起就指着
/// 一颗<b>从来没存在过</b>的方法名（还带省略号缩写）。账本那侧早有 <c>待决策事项.md</c> 的指针闸门（批次 UK），
/// 代码注释这一侧此前<b>一颗尺也没有</b>。
/// </para>
/// <para>
/// 判据问的是<b>对面那一颗在不在</b>，⛔ 问「我这句话好不好」：① 每颗 <c>XxxTests.Method</c> 引用都得在那颗测试档里
/// 读到 <c>void Method(</c> 的定义；② ⛔ 许用省略号或尾下划线把指针截断（截断＝下一批改名时它⛔ 会跟着红，
/// 而它<b>本来</b>就该红）；③ 本批修完的那两颗逐颗点名＝防这把尺扫了个空冒充绿灯。
/// </para>
/// <para>⚠ 口径两处：只扫 <c>src/**.cs</c>（注释⛔ 会被编译器检查，<c>see cref</c> 那种由编译器的 CS1574 管，
/// 这里补的是<b>裸文本</b>那一族）；读的是<b>整档原文</b>而⛔ <see cref="Code"/>——指针本来就住在注释里，
/// 抹注释等于把要查的东西抹掉（名册 496 那条规矩的反面用法）。</para>
/// </summary>
public sealed class TestPointerGateTests
{
    /// <summary>「<c>XxxTests.cs</c>」那种<b>档名</b>里的后缀⛔ 是方法名——不排掉就会把每一次提到档名都读成一颗坏指针。</summary>
    private static readonly string[] NotAMethod =
        ["cs", "js", "jsx", "ts", "tsx", "json", "md", "xaml", "py", "ps1", "html", "csproj", "sln", "txt"];

    private static readonly Regex Cite = new(@"\b([A-Za-z0-9_]*Tests)\.([A-Za-z0-9_]+)", RegexOptions.Compiled);

    private record CiteHit(string File, int Line, string TestClass, string Method, string Tail);

    /// <summary>现扫 <c>src</c> 那一族档里的每一颗引用（带<b>后随三颗字符</b>，那颗省略号尺就靠它）。</summary>
    private static List<CiteHit> Citations()
    {
        var hits = new List<CiteHit>();
        foreach (var (rel, text) in ReadRepoUnder("src"))
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in Cite.Matches(lines[i]))
                {
                    var meth = m.Groups[2].Value;
                    if (NotAMethod.Contains(meth, StringComparer.OrdinalIgnoreCase)) continue;
                    var end = m.Index + m.Length;
                    var tail = end + 3 <= lines[i].Length ? lines[i].Substring(end, 3) : lines[i][end..];
                    hits.Add(new CiteHit(rel.Replace('\\', '/'), i + 1, m.Groups[1].Value, meth, tail));
                }
            }
        }
        return hits;
    }

    [Fact]
    public static void EveryTestNameCitationInSourceResolvesToARealTestMethod()
    {
        var cites = Citations();
        Assert.True(cites.Count > 0, "一把⛔ 扫到任何引用的尺＝恒绿的那把尺（普查路径错了，⛔ 是被测对象干净）");
        var bad = new List<string>();
        foreach (var c in cites)
        {
            var testPath = $"tests/StarMark.Tests/{c.TestClass}.cs";
            if (!File.Exists(Path.Combine(RepoRoot(), testPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                bad.Add($"{c.File}:{c.Line} 引 {c.TestClass}.{c.Method}＝⛔ 读不到那颗测试档");
                continue;
            }
            var body = ReadRepoFile(testPath);
            if (!body.Contains("void " + c.Method + "(", StringComparison.Ordinal))
                bad.Add($"{c.File}:{c.Line} 引 {c.TestClass}.{c.Method}＝那颗档里⛔ 有这颗方法定义");
        }
        Assert.True(bad.Count == 0, "注释里⛔ 存在的测试方法指针 " + bad.Count + " 颗：\n  " + string.Join("\n  ", bad));
    }

    /// <summary>指针⛔ 许截断：<c>…XxxTests.SomeName_…</c> 那种写法在改名时⛔ 会跟着红，而它<b>本来</b>就该红。</summary>
    [Fact]
    public static void TestNameCitationsAreNeverTruncated()
    {
        var bad = Citations()
            .Where(c => c.Method.EndsWith("_", StringComparison.Ordinal) || c.Tail.StartsWith("…", StringComparison.Ordinal))
            .Select(c => $"{c.File}:{c.Line} 引 {c.TestClass}.{c.Method}（紧跟的那三颗字符＝「{c.Tail}」）")
            .ToList();
        Assert.True(bad.Count == 0, "被截断的测试方法指针 " + bad.Count + " 颗：\n  " + string.Join("\n  ", bad));
    }

    /// <summary>
    /// 本批修完的那两颗<b>逐颗点名</b>（旧名一颗⛔ 许留在档上）。
    /// <para>这一格是<b>反恒绿</b>的那一颗：上一格只问「扫到的都⛔ 存在」，如果哪天扫描路径写歪、扫到 0 颗，
    /// 它只会因为 <c>cites.Count > 0</c> 那句而红一次；这一格则要求<b>这两处引用确实被扫到</b>，路径错了同样红。</para>
    /// </summary>
    [Fact]
    public static void TheTwoPointersThatBrokeAreStillPointing()
    {
        var cites = Citations();
        Assert.Contains(cites, c => c.File == "src/StarMark.UI/Boards/BoardWindow.cs"
            && c.TestClass == "BoardWordGateTests" && c.Method == "TheBoardFamilyHasExactlyTwentyEightRowsAndTheyAreNamed");
        Assert.Contains(cites, c => c.File == "src/StarMark.Abstractions/NumberText.cs"
            && c.TestClass == "NumberTextTests" && c.Method == "WhichShapesActuallyMove");
        Assert.DoesNotContain("TwentyThreeRows", ReadRepoFile("src/StarMark.UI/Boards/BoardWindow.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ImplicitNumericFormatting", ReadRepoFile("src/StarMark.Abstractions/NumberText.cs"), StringComparison.Ordinal);
    }
}
