#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class SearchPage : Page
{
    public SearchPageViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(SearchPageViewModel)) as SearchPageViewModel)
            ?? new SearchPageViewModel(App.Services.GetService(typeof(StarMark.Core.Search.SearchService))
                as StarMark.Core.Search.SearchService
                ?? throw new InvalidOperationException("SearchService 未注册"));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 不再在此自动重跑搜索：进入搜索页时全局搜索框会立即设置 ViewModel.Query，
        // 由 OnQueryChanged 触发搜索。此前用单例 VM 里残留的旧 Query 重搜，
        // 正是「清空搜索栏后仍闪现上一条搜索”未找到“」的根因。
    }

    /// <summary>键盘 ↑↓ 移动选中项，并把选中卡片滚入视野。</summary>
    public void MoveKeyboardSelection(int delta)
    {
        if (ViewModel.Results.Count == 0) return;
        ViewModel.MoveSelection(delta);
        if (ViewModel.SelectedItem is not { } selected) return;
        var index = ViewModel.Results.IndexOf(selected);
        if (index < 0) return;
        try
        {
            // 分段布局：扁平索引映射到「精确匹配 / 相关结果」两段的局部索引
            var exactCount = ViewModel.ExactResults.Count;
            var (repeater, localIndex) = index < exactCount
                ? (ExactRepeater, index)
                : (RelatedRepeater, index - exactCount);
            var element = repeater.GetOrCreateElement(localIndex);
            element.StartBringIntoView();
        }
        catch (Exception ex)
        {
            StarLog.Error("键盘导航滚动失败", ex);
        }
    }

    private void Card_OpenRequested(object sender, long itemId)
        => ItemCardActions.Open(this.XamlRoot, itemId);

    private void Card_EditNoteRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object sender, ViewModels.ItemCardViewModel vm)
    {
        var wasHidden = vm.IsHidden;
        var nowHidden = await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
        if (!wasHidden && nowHidden && !ViewModel.ShowHidden)
            ViewModel.RemoveItem(vm.Id);
    }

    private void Card_PinRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.TogglePin(vm);

    private void Card_CopyLinkRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.CopyUri(vm);

    private void Card_OpenLocationRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.OpenLocation(vm);

    /// <summary>
    /// 卡片标签点击：在搜索页内叠加标签过滤，而不是跳去标签页。
    /// 原实现 NavigateTo("tags") 会丢掉当前关键词、且只能选一个标签；
    /// 现在与关键词组合成「搜 X 且带 #ai」的 AND 过滤，对齐扩展侧边栏行为。
    /// </summary>
    private void Card_TagFilterRequested(object sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
        => ViewModel.AddTagFilter(e.Tag);

    private void Card_TagRemoveRequested(object sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);

    // ───────── 吸顶标签筛选条 ─────────

    private void TagFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            ViewModel.RemoveTagFilter(tag);
    }

    private void ClearTagFilters_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearTagFilters();
}
