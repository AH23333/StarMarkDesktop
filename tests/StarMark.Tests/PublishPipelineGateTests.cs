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
/// 发布那一刀 MSBuild target 的闸门（批次 UH）。
/// <para><b>为什么单独立一颗</b>：坑表 #237 那条缺陷的形状是"每一层各自都对，合起来出货是坏的"——
/// 构建 0 Warning、5137 格全绿、从 <c>bin</c> 跑的界面一切正常，只有 <c>dotnet publish</c> 出来的那棵树
/// 少了 45 颗 <c>.xbf</c> 与 <c>StarMark.UI.pri</c>，双击只抛一句 <c>XamlParseException</c>。
/// 修它的那颗 target 同样只能被"形状"看到（MSBuild 不在测试能引用的任何工程里），
/// 所以这里钉的是它<b>搬得对不对</b>：通配、保子目录、只有一处出处。</para>
/// <para>判据都落在 XML 元素与属性上（<c>SourceFiles</c>／<c>DestinationFiles</c>／<c>Include</c>），
/// 不落在注释与散文里——那是 <c>CopyCompiledXamlToPublishDirectory</c> 真正被 MSBuild 读取的部分。</para>
/// </summary>
public sealed class PublishPipelineGateTests
{
    private const string Targets = "Directory.Build.targets";
    private const string UpdaterProject = "src/StarMark.Updater/StarMark.Updater.csproj";
    private const string TargetName = "CopyCompiledXamlToPublishDirectory";

    /// <summary>抹掉 XML 注释：判据只许读元素与属性（注释里重述一遍不算第二处出处，也不该被当成出处）。</summary>
    private static string Elements(string text) => Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);

    /// <summary>切出那颗 target 的体（从它的 &lt;Target 到下一个 &lt;/Target&gt;）。切不到就抛——守门失效比红测危险。</summary>
    private static string TargetBody(string elements)
    {
        var open = elements.IndexOf("<Target Name=\"" + TargetName, StringComparison.Ordinal);
        Assert.True(open >= 0, $"找不到 target {TargetName}（XAML 产物没人搬了）");
        var close = elements.IndexOf("</Target>", open, StringComparison.Ordinal);
        Assert.True(close > open, $"{TargetName} 没有闭合的 </Target>");
        return elements[open..close];
    }

    /// <summary>仓里所有工程文件（props/targets/csproj），跳过产物目录。</summary>
    private static IReadOnlyList<(string Relative, string Text)> EveryProjectFile()
    {
        var root = RepoRoot();
        var prefix = root.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');
        var list = new List<(string, string)>();
        foreach (var pattern in new[] { "*.targets", "*.props", "*.csproj" })
            foreach (var file in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
            {
                var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
                if (normalized.Contains("/bin/") || normalized.Contains("/obj/")
                    || normalized.Contains("/publish/") || normalized.Contains("/dist/")) continue;
                list.Add((normalized[(prefix.Length + 1)..], File.ReadAllText(file)));
            }
        Assert.True(list.Count >= 8, $"只扫到 {list.Count} 份工程文件，普查路径错了");
        return list;
    }

    // ===== 搬得对不对：通配 + 保子目录 + 落在发布目录 =====

    [Fact]
    public void CompiledXamlIsCopiedByWildcardAndKeepsItsSubfolders()
    {
        var body = TargetBody(Elements(ReadRepoFile(Targets)));
        // 挂在 Publish 之后：挂在别处（或干脆不挂）的表现是"这道搬运从来不跑"，而没人会红
        Assert.Contains("AfterTargets=\"Publish\"", body, StringComparison.Ordinal);
        Assert.Contains("Include=\"$(OutDir)**\\*.xbf\"", body, StringComparison.Ordinal);
        // 目的路径必须带 %(RecursiveDir)：Views/ Themes/ Controls/ 那些子目录一旦拍平，
        // 表现不是"起不来"而是"某个页面打不开"——那种坏只在点进去的那一下露头（#189/#193：列清单就会漏）。
        Assert.Contains("$(PublishDir)%(RecursiveDir)%(Filename)%(Extension)", body, StringComparison.Ordinal);
        // 钉属性驱动的那两侧，不钉 item 的名字（改个局部名字是合法重构，不该逼出假失败，#123）
        Assert.Contains("SourceFiles=\"@(", body, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublishTargetNamesNoIndividualXamlFileAndHardNothingElse()
    {
        var body = TargetBody(Elements(ReadRepoFile(Targets)));
        // 逐颗点名 = 下一颗新页面必然漏在这里；通配才是"少一颗当场红"的那一侧。
        foreach (var banned in new[] { "App.xbf", "MainWindow.xbf", "StarMark.UI.pri" })
            Assert.False(body.Contains(banned, StringComparison.Ordinal),
                $"搬运 target 里点名了 {banned}：该按通配搬，新增页面不该改这里");
        // 搬不到东西必须失败，不许静默出一棵起不来的树
        Assert.Contains("<Error ", body, StringComparison.Ordinal);
        Assert.Matches(@"@\(_\w+->Count\(\)\) == 0", body);   // 数量判据在，但不钉 item 的名字
    }

    [Fact]
    public void OnlyOnePlaceInTheRepositoryCopiesCompiledXamlIntoPublish()
    {
        // 两处搬就会漂成"一处搬 xbf、另一处搬 pri"，而两边各自都觉得自己搬完了。
        var owners = EveryProjectFile()
            .Where(f => Elements(f.Text).Contains("*.xbf", StringComparison.Ordinal))
            .Select(f => f.Relative)
            .ToList();
        Assert.Equal(new[] { Targets }, owners);
    }

    [Fact]
    public void TheUpdaterStaysAFolderThatIsMovedNotABundleThatExtractsItself()
    {
        var csproj = Elements(ReadRepoFile(UpdaterProject));
        // 更新器唯一的活是"在别处把一棵树改名过去"。自解压/单文件跑法会把它自己的解包目录
        // 变成第二个"它站着的、又可能要它挪走的东西"，那是这条链上最难复现的一种坏。
        foreach (var banned in new[] { "PublishSingleFile", "IncludeAllContentForSelfExtract", "SelfContained" })
            Assert.False(csproj.Contains(banned, StringComparison.Ordinal), $"更新器工程里出现了 {banned}");
    }
}
