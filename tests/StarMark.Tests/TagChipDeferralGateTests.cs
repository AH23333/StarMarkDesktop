#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// P-302 甲的对照判例（片277）：三颗宿主页的标签筛选 chip 交互<b>同款</b>钉住。
/// <para>① chip 的 ✕／一键清除都在 Click 里把「动 <c>Main.GlobalTagFilters</c>」延迟到 UI 消息队列末尾
/// （坑 #36 那族：被点元素仍在视觉树中正被测量时同步增删 <c>ItemsRepeater</c> 的 ItemsSource 会重入崩溃——
/// 搜索页与文件夹页先行（对照 <c>SearchPage.xaml.cs</c>／<c>FolderTreePage.xaml.cs</c>），本批补齐标签页第三面）；
/// 每臂只红自己那一页，缺一颗 handler 就缺一条证人。</para>
/// <para>② 标签页那条<b>状态条槽位恒占</b>（根修「选标签后无法清除」的云移位半边）：
/// 筛选栏出现/消失时标签云点击目标不漂移——槽本体⛔ 挂可见性折叠、槽高钉死，
/// chips 与空态提示在同一颗槽里切换。这一枚只钉<b>形状</b>，位置漂移的实机回归归复现脚本（P-302 判据①）。</para>
/// </summary>
public sealed class TagChipDeferralGateTests
{
    private const string SearchCs = "src/StarMark.UI/Views/SearchPage.xaml.cs";
    private const string FolderCs = "src/StarMark.UI/Views/FolderTreePage.xaml.cs";
    private const string TagsCs = "src/StarMark.UI/Views/TagsPage.xaml.cs";
    private const string TagsXaml = "src/StarMark.UI/Views/TagsPage.xaml";

    /// <summary>三页同款：移除 handler 与清除 handler 的名字（搜索页那颗移除门叫 <c>TagFilterChip_Click</c>），
    /// 加各自延迟体内必须出现的「真动作」——只钉 <c>TryEnqueue</c> 字样会放过排了个空队的假修。</summary>
    public static TheoryData<string, string, string, string, string> ChipHosts => new()
    {
        // 页、移除 handler、移除体真动作、清除 handler、清除体真动作
        { SearchCs, "private void TagFilterChip_Click", "RemoveGlobalTagFilter(tag))",
            "private void ClearTagFilters_Click", "ClearGlobalTagFilters())" },
        { FolderCs, "private void RemoveFilterChip_Click", "RemoveGlobalTagFilter(tagName))",
            "private void ClearTagFilters_Click", "ClearGlobalTagFilters())" },
        { TagsCs, "private void RemoveFilterChip_Click", "ToggleTagCommand.ExecuteAsync(tagName))",
            "private void ClearFilters_Click", "ClearFiltersCommand.ExecuteAsync(null))" },
    };

    [Theory]
    [MemberData(nameof(ChipHosts))]
    public void ChipClicksDeferTheFilterMutationToTheQueueTail(
        string rel, string removeHandler, string removeAction, string clearHandler, string clearAction)
    {
        var code = Code(ReadRepoFile(rel));
        foreach (var (handler, action) in new[] { (removeHandler, removeAction), (clearHandler, clearAction) })
        {
            var body = MethodBody(code, handler);
            Assert.False(body.Length == 0, $"{rel} 少了 {handler}（三页同款的 chip 门，缺一面就没人守）");
            Assert.Contains("DispatcherQueue.GetForCurrentThread()?.TryEnqueue", body, StringComparison.Ordinal);
            Assert.Contains(action, body, StringComparison.Ordinal);   // 排进队里的是真动作，⛔ 空队
        }
    }

    /// <summary>标签页状态条：Row 0 槽位恒占——槽本体⛔ 挂可见性折叠（一挂＝云随筛选出现/消失上下漂移＝「无法清除」回归），
    /// 槽高 <c>MinHeight</c> 钉死两态同高，chips 与空态提示同槽，旧位置（云下方）撤干净。</summary>
    [Fact]
    public void TheTagsStripKeepsItsSlotWhenTheFilterToggles()
    {
        var xaml = ReadRepoFile(TagsXaml);
        var row0 = xaml.IndexOf("Grid.Row=\"0\"", StringComparison.Ordinal);
        var row1 = xaml.IndexOf("Grid.Row=\"1\"", StringComparison.Ordinal);
        Assert.True(row0 >= 0 && row1 > row0, "TagsPage.xaml 的两行骨架变了，这一枚要先跟着改");
        var strip = xaml[row0..row1];
        var stripTag = System.Text.RegularExpressions.Regex.Match(xaml[xaml.LastIndexOf('<', row0)..row1], @"<[A-Za-z]+[^>]*>").Value;
        Assert.True(stripTag.Length > 0, "Row 0 那颗状态条的开始标记读不到");
        Assert.Contains("MinHeight=\"36\"", stripTag);                  // 槽高钉死：有筛选/无筛选两态同高
        Assert.DoesNotContain("Visibility", stripTag);                  // 槽本体恒在场：折叠只许发生在 chips/提示两颗子元素上
        Assert.Contains("x:Name=\"EmptyHintText\"", strip);             // 空态提示与 chips 同槽
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.ActiveFilterChips", strip);
        Assert.DoesNotContain("EmptyHintText", xaml[row1..]);           // 云下方那颗旧位撤干净（一留＝两颗门两个主人）
    }
}
