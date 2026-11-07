#nullable enable
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// AI 仪表盘（P-294）的守门：设置页 AI 栏的新卡——最近请求明细＋汇总读数＋按天趋势小图。
/// <para>
/// 被测的卡在 <c>StarMark.UI</c>，测试工程刻意不引用它（分层红线），所以卡的结构与接线读源文件比对；
/// 词条三语、趋势铺日纯函数与仓储读取口径则直接测 Abstractions／Data（见 <c>AiUsageTrendTests</c>／
/// <c>AiUsageRepositoryTests</c>）。
/// </para>
/// <para>钉的形状来自三条真机会坏的路：<b>卡嵌进了别的卡里</b>（视觉上缩在预算面板肚子里，用户拍板的
/// 「同级新卡」没兑现）、<b>仪表盘自己另起了一套计数</b>（预算栏与仪表盘两个数对不上——显示与判额分岔）、
/// <b>行与条的文案存了成品字</b>（换语言那一刻明细行还停在上一门，机制 (C) 的宿主漏登记）。</para>
/// </summary>
public sealed class AiDashboardGateTests
{
    private const string DashboardCode = "src/StarMark.UI/Views/SettingsPage.AiDashboard.cs";
    private const string AiPartial = "src/StarMark.UI/Views/SettingsPage.Ai.cs";
    private const string PageCode = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string ServiceCode = "src/StarMark.UI/Services/AiClassifyService.cs";
    private const string RepoCode = "src/StarMark.Data/AiUsageRepository.cs";

    // ────────── 词条：三语齐备 ──────────

    /// <summary>仪表盘那十颗词条三语齐备：少一列＝换语言后那一句永远说旧语言。</summary>
    [Fact]
    public void TheDashboardKeysExistInAllThreeLanguages()
    {
        foreach (var key in new[]
                 {
                     "settings.ai.dashboard.title", "settings.ai.dashboard.empty", "settings.ai.dashboard.summary",
                     "settings.ai.dashboard.trendTitle", "settings.ai.dashboard.recentTitle", "settings.ai.dashboard.barTip",
                     "settings.ai.dashboard.rowMain", "settings.ai.dashboard.rowTokens",
                     "settings.ai.dashboard.rowTokensEstimated", "settings.ai.dashboard.noModel",
                 })
        {
            Assert.True(UiStrings.TryGet(key, out var entry), $"{key} 不在词条表里");
            Assert.False(string.IsNullOrWhiteSpace(entry.Zh), $"{key} 缺中文列");
            Assert.False(string.IsNullOrWhiteSpace(entry.En), $"{key} 缺英文列");
            Assert.False(string.IsNullOrWhiteSpace(entry.Ja), $"{key} 缺日文列");
        }
    }

    // ────────── 卡的结构：同级卡、三块各有名字、字走键 ──────────

    /// <summary>
    /// 用户拍板（P-294）：卡长在 AI 栏、与主卡<b>同级</b>。嵌进主卡肚子里时，ColumnFlowPanel 换列的
    /// 摆法会让它缩成预算面板里的一小块——「明细＋汇总＋趋势」三项都挤没了。判据：明细列表收尾到
    /// 面板收尾之间只有仪表盘卡自己的那颗 <c>&lt;/Border&gt;</c>；嵌套时会多出主卡的那颗。
    /// </summary>
    [Fact]
    public void TheDashboardCardIsASiblingCardInsideTheAiTab()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        var aiTab = SourceGate.Between(xaml, "TabAi", "TabWidgets");

        // 卡用 Card 皮（与同栏别的卡同一张脸），不是随手一颗裸 Border
        Assert.Contains("<Border Style=\"{StaticResource Card}\">",
            SourceGate.Between(aiTab, "AI 仪表盘（P-294", "AiDashboardSummaryText"));

        // 三块各有名字：汇总读数、按天趋势、最近明细——少一块＝那一块永远空着
        Assert.Contains("AiDashboardSummaryText", aiTab);
        Assert.Contains("AiDashboardTrend", aiTab);
        Assert.Contains("AiDashboardRecentList", aiTab);

        // 同级判据：明细列表之后到面板收尾之间只有仪表盘卡自己的收尾
        var tail = SourceGate.Between(aiTab, "AiDashboardRecentList", "</controls:ColumnFlowPanel>");
        Assert.Equal(1, SourceGate.Count(tail, "</Border>"));
    }

    /// <summary>卡上常驻的三句走键，⛔ 在 XAML 里现写；明细行与趋势条的字由行类经 <c>{Binding}</c> 递。</summary>
    [Fact]
    public void TheDashboardXamlWiresItsTextThroughKeys()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        Assert.Contains("i18n:LocText.Key=\"settings.ai.dashboard.title\"", xaml);
        Assert.Contains("i18n:LocText.Key=\"settings.ai.dashboard.trendTitle\"", xaml);
        Assert.Contains("i18n:LocText.Key=\"settings.ai.dashboard.recentTitle\"", xaml);
        // 行与条的五条绑定：模板递出去的名字与行类的属性必须一一对上，差一颗＝那一格永远空白
        var template = SourceGate.Between(xaml, "AiDashboardRecentList", "</DataTemplate>");
        Assert.Contains("{Binding MainLine}", template);
        Assert.Contains("{Binding TokensLine}", template);
        var bars = SourceGate.Between(xaml, "AiDashboardTrend", "</DataTemplate>");
        Assert.Contains("{Binding TipText}", bars);
        Assert.Contains("{Binding BarHeight}", bars);
        Assert.Contains("{Binding DayLabel}", bars);
    }

    // ────────── 接线：进栏时读一次、每次整理后跟一拍 ──────────

    /// <summary>进 AI 栏初始化一次、每次整理跑完跟一拍——漏了后者＝刚烧完的几发要等下次进设置页才看得见。</summary>
    [Fact]
    public void TheDashboardRefreshesOnTabInitAndAfterEachOrganise()
    {
        var ai = SourceGate.ReadRepoFile(AiPartial);
        Assert.Contains("InitAiDashboard();", SourceGate.MethodBody(ai, "void InitAiSection()"));
        Assert.Contains("RefreshAiDashboardAsync();", SourceGate.MethodBody(ai, "async void AiOrganise_Click("));
    }

    /// <summary>
    /// 换语言的重贴成对：Loaded 里先退后订（防重复订）、Unloaded 里摘——摘漏了＝设置页关了之后
    /// 换语言还在给已卸载的行类发通知（P-175 那一族）。
    /// </summary>
    [Fact]
    public void RepasteIsSubscribedAndUnsubscribedInPairs()
    {
        var page = SourceGate.ReadRepoFile(PageCode);
        Assert.Equal(1, SourceGate.Count(page, "Locale.Changed += RepasteAiDashboardRows;"));
        Assert.Equal(2, SourceGate.Count(page, "Locale.Changed -= RepasteAiDashboardRows;"));
    }

    // ────────── 同一份账：⛔ 第二套计数 ──────────

    /// <summary>
    /// <b>仪表盘只经快照读，⛔ 另起计数</b>：code-behind 里再写一次 TotalsSince／Recent／ByDay，
    /// 后果是预算栏与仪表盘两个数各走各的——「显示没超但被熔断」的诞生方式（§20.1 判额与展示同一份账）。
    /// </summary>
    [Fact]
    public void TheDashboardCodeBehindReadsOnlyTheSnapshot_NeverRecounts()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(DashboardCode));
        Assert.Contains("DashboardSnapshotAsync(DashboardRecentTake, DashboardTrendDays)", code);
        Assert.DoesNotContain("TotalsSinceAsync", code);
        Assert.DoesNotContain("RecentAsync", code);
        Assert.DoesNotContain("ByDaySinceAsync", code);
        Assert.DoesNotContain("MaxRecordedAtAsync", code);
        Assert.DoesNotContain("SELECT", code);
    }

    /// <summary>服务侧一次读齐：全期合计＋最近明细＋按天趋势都从同一份仓储来，铺日交给纯函数。</summary>
    [Fact]
    public void TheServiceAssemblesTheSnapshotFromOneRepositoryInOnePass()
    {
        var body = SourceGate.MethodBody(SourceGate.Code(SourceGate.ReadRepoFile(ServiceCode)),
            "Task<AiDashboardSnapshot?> DashboardSnapshotAsync(");
        Assert.Contains("TotalsSinceAsync(0", body);                 // 汇总念全期：与预算栏的 30 天窗口各管一段
        Assert.Contains("RecentAsync(recentTake", body);
        Assert.Contains("ByDaySinceAsync(", body);
        Assert.Contains("AiUsageTrend.LastDays(", body);             // 缺日补零、窗外丢弃在纯函数里做
    }

    /// <summary>仓储两读的口径：明细新→旧（rowid 破平局）、按天用本地日切（用户过的那一天，不按 UTC）。</summary>
    [Fact]
    public void TheRepositoryOrdersRecentNewestFirst_AndGroupsByLocalDay()
    {
        var repo = SourceGate.Code(SourceGate.ReadRepoFile(RepoCode));
        Assert.Contains("ORDER BY at DESC, rowid DESC",
            SourceGate.MethodBody(repo, "Task<IReadOnlyList<AiUsageEntry>> RecentAsync("));
        Assert.Contains("date(at, 'unixepoch', 'localtime')",
            SourceGate.MethodBody(repo, "Task<IReadOnlyList<AiUsageDay>> ByDaySinceAsync("));
    }

    // ────────── 机制 (C)：行与条存「键＋实参」 ──────────

    /// <summary>
    /// 行与条都把 <b>键＋实参</b> 存进 <c>LocMessage</c>，渲染只经属性 <c>Render()</c>——
    /// 换语言那一刻 <see cref="Locale.Changed"/> 重贴才有的念。存成品字＝明细行永远停在造它那门语言。
    /// </summary>
    [Fact]
    public void RowsAndBarsStoreKeyPlusArgs_NeverRenderedStrings()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(DashboardCode));
        Assert.Contains("LocMessage.Of(\"settings.ai.dashboard.rowMain\"", code);
        Assert.Contains("LocMessage.Of(\"settings.ai.dashboard.barTip\"", code);
        Assert.Contains("\"settings.ai.dashboard.rowTokensEstimated\"", code);   // 三元选键：估算行念带（估算）的那句
        Assert.Contains("LocMessage.Of(\"settings.ai.dashboard.noModel\"", code);
        Assert.Contains("public void Repaste()", code);   // 两颗行类各自的换语言重贴出口
    }
}
