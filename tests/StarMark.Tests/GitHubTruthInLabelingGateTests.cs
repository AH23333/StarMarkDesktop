#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// star 这一族"<b>说出去的话</b>"必须与实现对得上（批次 VP 立闸，账本 P-6／P-57／P-58；片214·P-6A 收口）。
/// <para>
/// ① 排序档那句「最近 Star」：数据腿 P-6A 起取的是 star+json 信封里的<b>真 starred_at</b>
///   （用户点 Star 的时刻），话与数据自此对上——这条闸门从此转为<b>守势</b>：
///   哪条腿退化（取值退回 pushed_at、标签改口、SQL 换键），这里都会红。
/// ② 一轮被 5000 条预算掐断时，必须复位 ETag 检查点＋把「没拉全」说出去——旧写法既不说
///   "只导入了前 5000 条"，又把页 1 带回的 ETag 留在原地 ⇒ 下一轮 304 短路，第 5001 条<b>永远拉不到</b>
///   （P-57 那个漏臂；P-58 要的正是"修数据面必须同时改文案"）。
/// </para>
/// <para>
/// 判据一律<b>钉形状不钉措辞的具体字</b>的地方就钉形状：标签里必须出现真正的排序键字样、封顶那一臂必须
/// 同时做"复位检查点 + 说一声"两件事、上限那个数全文件只许有一处出处。
/// </para>
/// </summary>
public sealed class GitHubTruthInLabelingGateTests
{
    private const string Client = "src/StarMark.Integrations/GitHub/GitHubClient.cs";
    private const string Source = "src/StarMark.Integrations/GitHub/GitHubSource.cs";

    /// <summary>取表里那一行。没登记就直接抛：这一族要的是"话与数据对得上"，
    /// 而取不到词的那一格既不算红也不算绿，静默放行等于把判据换成"什么都没查"。</summary>
    private static UiStrings.Entry Entry(string key)
    {
        Assert.True(UiStrings.TryGet(key, out var entry), $"界面文案没登记这颗键：{key}");
        return entry;
    }

    /// <summary>
    /// 「那一档叫什么」＝<b>它实际按什么排</b>。这条链 P-6A 起是五环，断哪环那句标签都成假话：
    /// 档名 <c>starred</c> → SQL 按 <c>$.StarredAt</c> 排 → <c>StarredAt</c> 填信封里的真 starred_at
    /// → 而那颗真值靠请求级 star+json Accept 讲价（客户端解信封盖进 <c>StarredAtRaw</c>）。
    /// <para>五环全钉住，这条闸门从此守势：<b>只改标签不改数据</b>（或反过来）都会当场红。</para>
    /// <para>批次 WI-1 之后 XAML 里不再有那句中文，它只许指向表里那颗键，所以这一格分成两段：
    /// <b>接线</b>（那一档确实从表里取词，而不是自带一份字面量）＋ <b>内容</b>（表里<b>两种说法</b>都得
    /// 说出真正的排序键）。</para>
    /// </summary>
    [Fact]
    public void TheStarSortTierIsNamedAfterWhatItActuallySortsBy()
    {
        // 档名的出处（批次 WI-2c 之后）：不是标记里的一颗 <ComboBoxItem>，而是 MainWindow.Search.cs
        // 里那份 SortOptions——那一格闭着的显示要跟着语言走，选项就必须是"数据项＋模板"。
        // ⚠ 这一处改的是<b>读点</b>，判据守的那件事没变（坑表 #286 同族：锚点只认旧形状时，改对了反而红）。
        var tier = Regex.Matches(Code(ReadRepoFile("src/StarMark.UI/MainWindow.Search.cs")),
                @"LocOption\.T\(.*", RegexOptions.Singleline)
            .Cast<Match>().Single(m => m.Value.Contains("\"sort.starred\""));

        // ① 接线：档名与那句解释都从表里来，那颗身份仍是 starred（数据层的排序键）
        Assert.Contains("LocOption.T(\"sort.starred\"", tier.Value, System.StringComparison.Ordinal);
        Assert.Contains("\"starred\"", tier.Value, System.StringComparison.Ordinal);
        Assert.Contains("tipKey: \"sort.starredTip\"", tier.Value, System.StringComparison.Ordinal);

        // ② 内容：两种说法都得说出真正的排序键——「最近 Star」（P-6A 起是数据真相，不再是愿景）
        var label = Entry("sort.starred");
        Assert.Contains("最近 Star", label.Zh, System.StringComparison.Ordinal);
        Assert.Contains("starred", label.En, System.StringComparison.OrdinalIgnoreCase);
        var tip = Entry("sort.starredTip");
        Assert.Contains("Star", tip.Zh, System.StringComparison.Ordinal);
        Assert.Contains("starred", tip.En, System.StringComparison.OrdinalIgnoreCase);

        // ③ 那一档排的确实是 StarredAt（浏览与搜索两条 SQL 都得是，否则"档名"与"排序键"在两条路径上分岔）
        foreach (var repo in new[] { "src/StarMark.Data/ItemRepository.Search.cs", "src/StarMark.Data/ItemRepository.Items.cs" })
        {
            var sql = Code(ReadRepoFile(repo));
            Assert.Equal(1, Count(sql, "\"starred\" =>"));
            Assert.Equal(1, Count(sql, "'$.StarredAt'"));
        }

        // ④ 而 StarredAt 今天装的是信封里的真 starred_at（star+json，P-6A）——只改标签不改数据
        // 是这条闸门要拦的方向；反过来把取值退回 PushedAt，这里与②一起红。
        var mapped = Code(ReadRepoFile(Source));
        Assert.Contains("var starredAt = ParseUnixTime(repo.StarredAtRaw) ?? now;", mapped, System.StringComparison.Ordinal);
        Assert.Contains("StarredAt = starredAt,", mapped, System.StringComparison.Ordinal);

        // ⑤ 信封腿（P-6A 新增）：请求级 star+json Accept ＋ 解信封盖真值——这条腿断了，
        // ④ 读到的就是空值退 now，「最近 Star」名存实亡。
        var pageBody = MethodBody(Code(ReadRepoFile(Client)),
            "public async Task<IReadOnlyList<GitHubStarApiModel>> GetStarredPageAsync");
        Assert.Contains("application/vnd.github.star+json", pageBody, System.StringComparison.Ordinal);
        Assert.Contains("repo.StarredAtRaw = envelope.StarredAt;", pageBody, System.StringComparison.Ordinal);
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
