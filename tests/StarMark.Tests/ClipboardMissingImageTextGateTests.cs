#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「那张图的文件不在了」这一族话的接线守门（批次 SI，P-131 清单 #6）。
/// <para>要守的只有一件事：<b>同一句事实不许有两个出处</b>。登记时这句记的是"两处各写一遍"，
/// 按后果复算其实是四处（读取侧的两个入口、历史页状态行、灰掉的菜单标题、卡片那一格）——
/// 与 #193 同一形状：普查要按后果扫，不能按写法扫。</para>
/// <para>句子本体已搬进 Abstractions，所以读数改由 <see cref="ClipboardMissingImageTextTests"/> 逐字钉；
/// 这里只守"每一处宿主确实问了政策层，而且别处没留抄本"（#196：接线断言钉在方法体里）。</para>
/// </summary>
public sealed class ClipboardMissingImageTextGateTests
{
    private const string Fact = "不在本机";          // 那颗事实的指纹：谁抄了这句话，扫字面就抓得到
    private const string Policy = "src/StarMark.Abstractions/Clipboard/ClipboardPolicy.cs";
    private const string Store = "src/StarMark.Integrations/Clipboard/ClipboardImageStore.cs";
    private const string PageVm = "src/StarMark.UI/ViewModels/ClipboardPageViewModel.cs";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";

    /// <summary>五个消费点 → 各自动作所在的方法签名（红的时候要说得出该打开哪一个）。</summary>
    private static readonly (string File, string Anchor, string Member)[] Outlets =
    {
        (Store, "public static bool TryReadEntryImage", "MissingFileClause"),
        (Store, "TryReadFileAsPngAsync(string? path, CancellationToken ct)", "MissingFileClause"),
        (PageVm, "private async Task<bool> ReuseImageAsync(ItemCardViewModel vm)", "DescribeMissingImageForReuse"),
        (CardVm, "public string PinImageMenuText => CanPinAsImage", "MissingFileClause"),
        (CardVm, "public string ClipboardImageMissingText", "DescribeMissingImageBanner"),
    };

    [Fact]
    public void ThePhraseExistsNowhereButThePolicy()
    {
        var files = ReadRepoUnder("src");
        Assert.True(files.Count >= 200, $"只扫到 {files.Count} 个源文件（普查路径错了＝闸门在空转）");
        var hits = files
            .Select(f => (f.RelativePath, Count(Code(f.Text), Fact)))
            .Where(f => f.Item2 > 0)
            .ToList();
        // 一整个 src 只剩政策层那一颗常数：任何一处再抄一遍（或把抄本留在原地）都在这里红。
        Assert.Equal(new[] { (Policy, 1) }, hits.Select(h => (h.RelativePath, h.Item2)).ToArray());
    }

    [Fact]
    public void NoXamlPageQuotesThatSentence()
    {
        var xaml = XamlUnderSrc().ToList();
        Assert.True(xaml.Count >= 30, $"只扫到 {xaml.Count} 个 xaml（扫描器没跑起来，别把这条当绿）");
        Assert.All(xaml, f => Assert.DoesNotContain(Fact, f.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void ThePolicyHoldsOneClauseAndTwoHostSentences()
    {
        var policy = Code(ReadRepoFile(Policy));
        Assert.Equal(1, Count(policy, "MissingFileClause ="));                       // 事实只有一个出处
        Assert.Equal(1, Count(policy, "DescribeMissingImageForReuse()"));            // 状态行那句
        Assert.Equal(1, Count(policy, "DescribeMissingImageBanner()"));              // 那一格那句
    }

    [Fact]
    public void EveryOutletAsksThePolicyFromInsideItsOwnMethod()
    {
        foreach (var (file, anchor, member) in Outlets)
        {
            var body = MethodBody(ReadRepoPartials(file), anchor);
            Assert.Contains($"ClipboardPolicy.{member}", body);
            Assert.DoesNotContain(Fact, Code(body));                                 // 原地不许留抄本
        }
    }

    /// <summary>
    /// 启动器那句"本机上的这个路径已经不在了"是<b>有意分岔</b>，按文件名点名钉住：
    /// 它说的是任意本地路径、还要带出地址，并且"可能被移动或删除"是给用户的成因猜测——
    /// 与"这一张图的像素拿不到了"不是同一件事。将来要合并，得先有人决定合并后那句话怎么说。
    /// </summary>
    [Fact]
    public void TheLauncherKeepsItsOwnDivergenceOnPurpose()
    {
        const string launcher = "src/StarMark.UI/Helpers/LauncherEx.cs";
        var code = Code(ReadRepoFile(launcher));
        Assert.Contains("本机上的这个路径已经不在了", code);
        Assert.DoesNotContain("MissingFileClause", code);
        Assert.DoesNotContain(Fact, code);
    }

    private static IEnumerable<(string Path, string Text)> XamlUnderSrc()
    {
        var root = Path.Combine(RepoRoot(), "src");
        Assert.True(Directory.Exists(root), "src 目录不存在（守门失效比红测更危险，故直接抛）");
        foreach (var file in Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/")) continue;
            yield return (normalized, File.ReadAllText(file));
        }
    }
}
