#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.I18n;
using StarMark.Core.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// ✅P-210 文件夹树伪根 id 化那一批的形状闸门：<b>六颗伪根的身份是稳定 ASCII id</b>
/// （落盘／<see cref="FolderPathUtil.RootOrder"/> 排序／树节点合并键全是身份用途），<b>显示侧唯一出口
/// <see cref="FolderPathUtil.DisplayName"/> 念表</b>；RSS 根在库里钉死「RSS订阅」零迁移、只有显示念表；
/// AI 提示词与内置分类标签<b>始终为英文</b>（他裁，与界面语言无关）。
/// <para>本把尺防的是<b>半搬</b>：身份换了 id 而某一处显示/排序/标签还念着旧中文——
/// 那一形状在编译期全绿，屏幕上表现为「英文界面里那一格还是中文」或「旧收藏落进另一棵树」。</para>
/// </summary>
[Collection("LocaleGlobal")]   // 有一格会当场换到英文档量七臂，⛔ 与别的换语言的证人交错跑
public sealed class FolderPseudoRootGateTests : IDisposable
{
    private readonly AppLanguage _localeBefore = Locale.Current;
    public void Dispose() => Locale.TrySet(_localeBefore);

    /// <summary>汉字＋假名（<b>窄尺</b>，⛔ <see cref="SourceGate.Cjk"/> 那把宽尺）：
    /// 提示词与标签的英文判据用这一把——<c>ItemLine</c> 的「｜」(U+FF5C) 与「…」(U+2026) 是载荷形状不是屏幕话，
    /// 宽尺（全角那一族也在射程）会把它们误伤成「提示词里有中文」。</summary>
    private static readonly Regex HanKana = new(@"[぀-ヿ㐀-䶿一-鿿]", RegexOptions.Compiled);

    [Fact]
    public void PseudoRootIdentitiesAreStableAsciiIds()
    {
        // 身份逐颗钉死（值即落盘键）：改任何一颗＝旧收藏查不回自己的分组，那一刀要走库迁移而不是改 const。
        Assert.Equal("github-stars", FolderPathUtil.GitHubGroup);
        Assert.Equal("clipboard", FolderPathUtil.ClipboardGroup);
        Assert.Equal("pinned", FolderPathUtil.PinnedGroup);
        Assert.Equal("other", FolderPathUtil.OtherGroup);
        Assert.Equal("other-bookmarks", FolderPathUtil.OtherBookmarkGroup);
        Assert.Equal("other-files", FolderPathUtil.OtherFileGroup);
    }

    [Fact]
    public void RootOrderRanksIdentitiesNotWords()
    {
        // 排序交的是身份：六颗 id 各归各位；用户文件夹（含旧库残留的中文分组名——那是数据）一律 100。
        Assert.Equal(-1, FolderPathUtil.RootOrder(FolderPathUtil.PinnedGroup));
        Assert.Equal(0, FolderPathUtil.RootOrder(FolderPathUtil.GitHubGroup));
        Assert.Equal(1, FolderPathUtil.RootOrder(FolderPathUtil.ClipboardGroup));
        Assert.Equal(2, FolderPathUtil.RootOrder(FolderPathUtil.OtherBookmarkGroup));
        Assert.Equal(3, FolderPathUtil.RootOrder(FolderPathUtil.OtherFileGroup));
        Assert.Equal(4, FolderPathUtil.RootOrder(FolderPathUtil.OtherGroup));
        Assert.Equal(100, FolderPathUtil.RootOrder("用户文件夹"));
        // ⛔ 半搬证人：换 id 之前的中文词如今只是「恰叫这个名字的用户文件夹」——不再吃伪根排序位。
        Assert.Equal(100, FolderPathUtil.RootOrder("⭐ GitHub Stars"));
        Assert.Equal(100, FolderPathUtil.RootOrder("其他"));
        Assert.Equal(100, FolderPathUtil.RootOrder("已置顶"));
    }

    [Fact]
    public void DisplayNameCoversAllSevenKnownIdentities()
    {
        // 七臂全覆盖在<b>英文档</b>下量：RSS 根的中文档显示值与库里的字面量同形（零视觉变化是刻意的），
        // 中文档断「≠原样」会把那一臂误伤；英文档下七颗全部该换话。
        Locale.TrySet(AppLanguage.English);
        var known = new[]
        {
            FolderPathUtil.GitHubGroup, FolderPathUtil.ClipboardGroup, FolderPathUtil.PinnedGroup,
            FolderPathUtil.OtherGroup, FolderPathUtil.OtherBookmarkGroup, FolderPathUtil.OtherFileGroup,
            RssFolders.Root,
        };
        foreach (var id in known)
            Assert.True(!string.Equals(FolderPathUtil.DisplayName(id), id, StringComparison.Ordinal),
                $"{id} 原样上屏——显示侧七臂少了一臂（半搬：身份换了、显示没跟上，或表键缺失落进默认臂）");
        // 默认臂是数据出口：用户文件夹名任何语言下原样交回。
        Assert.Equal("技术", FolderPathUtil.DisplayName("技术"));
    }

    [Fact]
    public void FolderPathUtilItselfCarriesNoScreenWords()
    {
        // id 化之后那一档不该再有任何屏幕字（doc 注释由 Code 抹掉；日志行按既定口径不在此列）。
        var code = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/FolderPathUtil.cs"));
        Assert.Empty(SourceGate.ScreenWords(code));
    }

    [Fact]
    public void TreePageShowsNamesOnlyThroughDisplayNameAndFollowsLocaleChanges()
    {
        var page = SourceGate.Code(SourceGate.ReadRepoPartials("src/StarMark.UI/Views/FolderTreePage.xaml.cs"));
        Assert.Contains("FolderPathUtil.DisplayName(node.Name)", page);   // 显示唯一出口
        Assert.DoesNotContain("Text = node.Name", page);                  // 裸 Name 上屏＝半搬漏网
        // 换语言跟随：订阅与摘除必须成对（ctor 订、OnNavigatedFrom 摘——单边就是泄漏或死页重建，坑表 #386）。
        Assert.Contains("Locale.Changed += OnLocaleChanged", page);
        Assert.Contains("Locale.Changed -= OnLocaleChanged", page);
    }

    [Fact]
    public void RssDisplayPathsGoThroughDisplayName()
    {
        var feed = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/Feed/RssFeed.cs"));
        Assert.Contains("Select(FolderPathUtil.DisplayName)", feed);      // 收藏提示那条路径
        var card = SourceGate.Code(SourceGate.ReadRepoPartials("src/StarMark.UI/ViewModels/ItemCardViewModel.cs"));
        Assert.Contains("Select(FolderPathUtil.DisplayName)", card);      // 卡片 tooltip 那条路径
    }

    [Fact]
    public void RssRootStaysThePinnedLiteralInStorage()
    {
        // 库里的根钉死「RSS订阅」（零迁移：老收藏一颗不动）；只有显示那一格念表。
        // 这颗字面量同时是 BuildInfoWordGate「RssFeed.cs 仍有 CJK」那一格的正件——⛔ 顺手"美化"。
        Assert.Equal("RSS订阅", RssFolders.Root);
    }

    [Fact]
    public void PromptsAreEnglishInBothVocabularyBranches()
    {
        // 两个分支（有词库／空词库）都要量：CJK 藏在没走到的那一臂里，单分支断言是假绿。
        Assert.Empty(HanKana.Matches(ClassifyPrompt.SystemPrompt(new[] { "dev", "docs" })));
        Assert.Empty(HanKana.Matches(ClassifyPrompt.SystemPrompt(Array.Empty<string>())));
        Assert.Empty(HanKana.Matches(ClassifyPrompt.UserPrompt(
            new[] { new ClassifyItem(1, "Some long enough title", null, null, "test", Array.Empty<string>()) })));
    }

    [Fact]
    public void BuiltinRuleTagsAreEnglishWhileSourceNamesStayData()
    {
        // 标签词＝分类的工作语言，始终英文（✅P-210 他裁）；SourceEquals 匹配的是来源数据
        // （「本地文件」是 AiClassifyService.NameOf 产出的源名），照旧中文——两件事别互相误伤。
        Assert.Contains(ClassifyRules.Defaults, r => r.SourceEquals == "本地文件");
        foreach (var rule in ClassifyRules.Defaults)
            Assert.True(rule.Tags.All(t => !HanKana.IsMatch(t)),
                $"内置标签词必须始终是英文：{string.Join(",", rule.Tags)}");
    }
}
