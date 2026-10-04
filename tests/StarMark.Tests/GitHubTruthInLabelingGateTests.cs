#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// star 这一族"<b>说出去的话</b>"必须与实现对得上（批次 VP，账本 P-6／P-57／P-58）。
/// <para>
/// 两处都不是数据面缺陷，而是"数据面已经这样了，话却说得比实现满"：
/// ① 排序档那颗写着「最近 Star」，实际排的是<b>仓库最后一次 push</b>（<c>GitHubSource.MapToItem</c>
///   把 <c>StarredAt</c> 填成 <c>repo.PushedAt</c>）——老仓库被新近 Star 会排到很后面，那句话对它是假话；
/// ② 一轮被 5000 条预算掐断时，旧写法既不说"只导入了前 5000 条"，又把页 1 带回的 ETag 留在原地
///   ⇒ 下一轮 304 短路，第 5001 条<b>永远拉不到</b>（P-57 那个漏臂；P-58 要的正是"修数据面必须同时改文案"）。
/// </para>
/// <para>
/// 判据一律<b>钉形状不钉措辞的具体字</b>的地方就钉形状：标签里必须出现真正的排序键字样、封顶那一臂必须
/// 同时做"复位检查点 + 说一声"两件事、上限那个数全文件只许有一处出处。
/// </para>
/// </summary>
public sealed class GitHubTruthInLabelingGateTests
{
    private const string MainWindowXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string Client = "src/StarMark.Integrations/GitHub/GitHubClient.cs";
    private const string Source = "src/StarMark.Integrations/GitHub/GitHubSource.cs";

    private static string Markup()
        => Regex.Replace(ReadRepoFile(MainWindowXaml), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>取表里那一行。没登记就直接抛：这一族要的是"话与数据对得上"，
    /// 而取不到词的那一格既不算红也不算绿，静默放行等于把判据换成"什么都没查"。</summary>
    private static UiStrings.Entry Entry(string key)
    {
        Assert.True(UiStrings.TryGet(key, out var entry), $"界面文案没登记这颗键：{key}");
        return entry;
    }

    /// <summary>
    /// 「那一档叫什么」＝<b>它实际按什么排</b>。这条链有三环，缺一环那句标签就成了假话：
    /// 档名 <c>starred</c> → SQL 按 <c>$.StarredAt</c> 排 → 而 <c>StarredAt</c> 填的是 <c>repo.PushedAt</c>。
    /// <para>三环都当<b>前提</b>钉住：<b>哪天真去取 starred_at（P-6 的选项 A），这一格会红——那是故意的</b>。
    /// 数据变准确的同一天，才轮得到把标签改回"最近 Star"；只改标签不改数据，才是这条闸门要拦的方向。</para>
    /// <para>批次 WI-1 之后 XAML 里不再有那句中文，它只许指向表里那颗键，所以这一格分成两段：
    /// <b>接线</b>（那一档确实从表里取词，而不是自带一份字面量）＋ <b>内容</b>（表里<b>两种说法</b>都得
    /// 说出真正的排序键）。第二段是净增的覆盖面：原来只查中文那一列，将来补译文时把英文写成
    /// "Recently starred"，这里就会红。</para>
    /// </summary>
    [Fact]
    public void TheStarSortTierIsNamedAfterWhatItActuallySortsBy()
    {
        var tier = Regex.Matches(Markup(), @"<ComboBoxItem\b[^>]*>", RegexOptions.Singleline)
            .Cast<Match>().Single(m => m.Value.Contains("Tag=\"starred\""));

        // ① 接线：档名与那句解释都从表里来
        Assert.Contains("i18n:LocText.Key=\"sort.starred\"", tier.Value, System.StringComparison.Ordinal);
        Assert.Contains("i18n:LocText.ToolTipKey=\"sort.starredTip\"", tier.Value, System.StringComparison.Ordinal);

        // ② 内容：两种说法都说出 push，且都不说"最近 Star"
        var label = Entry("sort.starred");
        Assert.Contains("推送", label.Zh, System.StringComparison.Ordinal);
        Assert.Contains("push", label.En, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("最近 Star", label.Zh, System.StringComparison.Ordinal);
        var tip = Entry("sort.starredTip");
        Assert.Contains("push", tip.Zh, System.StringComparison.Ordinal);
        Assert.Contains("push", tip.En, System.StringComparison.OrdinalIgnoreCase);

        // ③ 那一档排的确实是 StarredAt（浏览与搜索两条 SQL 都得是，否则"档名"与"排序键"在两条路径上分岔）
        foreach (var repo in new[] { "src/StarMark.Data/ItemRepository.Search.cs", "src/StarMark.Data/ItemRepository.Items.cs" })
        {
            var sql = Code(ReadRepoFile(repo));
            Assert.Equal(1, Count(sql, "\"starred\" =>"));
            Assert.Equal(1, Count(sql, "'$.StarredAt'"));
        }

        // ④ 而 StarredAt 今天装的是 push 时间（改这条代码的人请连同上面几句一起改，别只改标签）
        var mapped = Code(ReadRepoFile(Source));
        Assert.Contains("var starredAt = ParseUnixTime(repo.PushedAt) ?? now;", mapped, System.StringComparison.Ordinal);
        Assert.Contains("StarredAt = starredAt,", mapped, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 封顶那一臂必须<b>两件事一起做</b>：复位 ETag 检查点（不然下一轮 304 永久短路）＋ 说一声
    /// （不报成功也不报失败，就报"到上限了、其余没导入"）。少任何一件，用户看到的都是一句不成立的话。
    /// <para>判据<b>不钉措辞也不钉常量名</b>（#33 那条纪律）：那句提醒要"带一个数"就行（措辞随人改），
    /// 上限从哪个标识符读来也不问——把 <c>MaxStarredItems</c> 改成别的名字必须原样放过。</para>
    /// </summary>
    [Fact]
    public void TheItemBudgetCapResetsTheCheckpoint_AndSaysSoOutLoud()
    {
        var body = MethodBody(Code(ReadRepoFile(Client)),
            "public async Task<IReadOnlyList<GitHubStarApiModel>> GetAllStarredAsync");

        // 取消/失败那一臂（catch）＋ 封顶这一臂：两处都得把检查点按回去
        Assert.Equal(2, Count(body, "CachedETag = etagBefore;"));
        Assert.Contains("truncated = true", body, System.StringComparison.Ordinal);
        // "说一声"＝那句提醒必须带上被丢掉的**数量**（内插一个变量），而不是"到上限了"四个字
        Assert.Matches(@"StarLog\.Warn\(\$\s*""[^""\n]*\{\w+", body);
        // 而那个数不许在这一臂里再写一遍字面量（上限全文件只许有一处出处，见下一格）
        Assert.DoesNotContain("5000", body, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 上限那个数<b>全文件只许有一处出处</b>：写两遍 5000，改了常量没改日志，
    /// 就是"报出来的上限与真正生效的上限不一致"。
    /// <para>钉的是<b>形状</b>（有一处 <c>const int 某名 = 5000;</c>）而不是那颗的名字：
    /// 改名是合理的重构，把名字钉死只会让下一次改名变成一次假事故。</para>
    /// <para>反自证：这一格靠 <see cref="Count"/> 数代码（注释已被 <c>Code()</c> 抹掉），
    /// 注释里那些"5000"是给人读的叙述，不算第二处出处。</para>
    /// </summary>
    [Fact]
    public void TheItemBudgetNumberHasExactlyOneSource()
    {
        var code = Code(ReadRepoFile(Client));
        Assert.Matches(@"const\s+int\s+\w+\s*=\s*5000\s*;", code);
        Assert.Equal(1, Count(code, "5000"));
    }
}
