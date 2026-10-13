#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

    /// <summary>
    /// 「那张图的文件不在了」这一族话的接线守门（批次 SI，P-131 清单 #6；批次 WI-2y 改键后口径升级）。
    /// <para>要守的只有一件事：<b>同一句事实不许有两个出处</b>。登记时这句记的是"两处各写一遍"，
    /// 按后果复算其实是四处（读取侧的两个入口、历史页状态行、灰掉的菜单标题、卡片那一格）——
    /// 与 #193 同一形状：普查要按后果扫，不能按写法扫。</para>
    /// <para>WI-2y 之后事实句的<b>唯一出处是措辞表里的那一行</b>（政策层改成交键）：本格从
    /// "只剩政策层一颗常数"升级为"只剩表里一行"——宿主（含政策层自己）再抄任何一处字面都在这里红。
    /// 句子本体由 <see cref="ClipboardMissingImageTextTests"/> 在默认语言下逐字钉；
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
    public void ThePhraseExistsNowhereButTheTable()
    {
        var files = ReadRepoUnder("src");
        Assert.True(files.Count >= 200, $"只扫到 {files.Count} 个源文件（普查路径错了＝闸门在空转）");
        const string Strings = "src/StarMark.Abstractions/I18n/UiStrings.cs";
        var hits = files
            .Select(f => (f.RelativePath, Count(Code(f.Text), Fact)))
            .Where(f => f.Item2 > 0)
            .ToList();
        // WI-2y 后的形状：一整个 src 只剩措辞表那一行（政策层改成交键——它自己抄回来也在这里红）。
        Assert.Equal(new[] { (Strings, 1) }, hits.Select(h => (h.RelativePath, h.Item2)).ToArray());
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

    /// <summary>
    /// ✅WI-65 片5a（P-194 那一族的第五刀第一颗）：这四颗政策句交的是<b>键＋实参</b>，政策层自己⛔ 再念一遍。
    /// <para>⚠ 这一格数的⛔ 是"合数掉了多少"（那是 <c>spoken_65.py</c> 那把尺的活），它钉的是<b>形状</b>：
    /// <c>Locale.T(</c>／<c>Locale.Fmt(</c> 在这两档的代码里一颗都不许留，而那颗 <c>{0}</c> 槽里放的必须是
    /// <c>LocMessage</c>（逐字与"槽里交键"两条真测在 <see cref="ClipboardMissingImageTextTests"/>）。</para>
    /// <para>⚠ 取词落在<b>宿主那一拍</b>：读取侧那两处 <c>.Render()</c> 也钉在这里——那一条载体
    /// （<c>ClipboardImageStore</c> 的 reason）同一口气里还装着四句没进表的裸中文（那是 P-192 的账），
    /// 把它整个换成 <c>LocMessage</c> ＝把另一票的账拖进这一刀＝半搬，所以本批只换到"话由谁决定"为止。</para>
    /// </summary>
    [Fact]
    public void ThePolicyLayerHandsOutKeysWhileTheHostsDoTheReading()
    {
        var policy = Code(ReadRepoFile(Policy));
        Assert.Equal(0, Count(policy, "Locale.T(") + Count(policy, "Locale.Fmt("));
        Assert.Equal(3, Count(policy, "LocMessage.Of(\"clip.miss."));                 // 事实＋那两句宿主话
        Assert.Equal(1, Count(policy, "LocMessage.Of(\"clip.miss.clause\")"));         // 那颗事实自己也只在一处成串
        Assert.Equal(2, Count(policy, ", MissingFileClause)"));                       // 另外两句把它当 {0} 槽递出去

        var cardPolicy = Code(ReadRepoFile("src/StarMark.Abstractions/ItemCardPolicy.cs"));
        Assert.Equal(0, Count(cardPolicy, "Locale.T(") + Count(cardPolicy, "Locale.Fmt("));
        Assert.Equal(1, Count(cardPolicy, "LocMessage.Of(\"card.missRow\""));

        // 宿主侧：读取那两处当场念完再交出（载体还是字符串，理由见上一段），卡片与状态行那两处在 UI 念
        Assert.Equal(2, Count(Code(ReadRepoFile(Store)), "ClipboardPolicy.MissingFileClause.Render()"));
        Assert.Contains("ClipboardPolicy.DescribeMissingImageForReuse().Render()",
            Code(ReadRepoFile(PageVm)), StringComparison.Ordinal);
        Assert.Contains("ClipboardPolicy.DescribeMissingImageBanner().Render()",
            Code(ReadRepoFile(CardVm)), StringComparison.Ordinal);
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
    /// 启动器那一句「本机上的这个路径已经不在了」在批次 SI 记的是<b>有意分岔</b>，批次 VQ <b>把它合掉了</b>：
    /// 用户报"点开没反应"之后，同一件事要在三处说（打开失败的提示卡、卡片/右键那句删行说明、设置页那句失联条数），
    /// 再留两份说法＝同一件事两个主人。SI 那条注释等的就是"先有人决定合并后那句话怎么说"——
    /// 决定落在 Abstractions 的 <c>ItemCardPolicy.MissingRowMessage</c>：
    /// 事实半截仍引 <c>ClipboardPolicy.MissingFileClause</c>，再补"打开没有东西可开、条目还留在库里可以删掉这一行"。
    /// <para>所以本格从"分岔不许被合并"翻成"<b>分岔不许回来</b>"：宿主抄回自己那一句、或另写一句新的，都在这里红。</para>
    /// </summary>
    [Fact]
    public void TheLauncherAsksTheSharedRowSentence()
    {
        const string launcher = "src/StarMark.UI/Helpers/LauncherEx.cs";
        var code = Code(ReadRepoFile(launcher));
        Assert.Contains("ItemCardPolicy.MissingRowMessage", code, StringComparison.Ordinal);
        Assert.DoesNotContain("本机上的这个路径已经不在了", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MissingFileClause", code, StringComparison.Ordinal);   // 也不许越过政策句直接引剪贴板那颗片段
        Assert.DoesNotContain(Fact, code, StringComparison.Ordinal);
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
