#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Controls;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 主窗「RSS」页：一个订阅源一个文件夹，文件夹里是它当前的条目（用户裁决的形状）。
/// <para>页面只做装配：源列表、抓取轮次、收藏落点全在 <see cref="RssPageViewModel"/> 与
/// <c>RssItemActions</c> / <c>RssAggregator</c>，这里只负责把按钮接到命令上。</para>
/// </summary>
public sealed partial class RssPage : Page
{
    public RssPageViewModel ViewModel { get; }

    public RssPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<RssPageViewModel>();
    }

    /// <summary>进入页面按当前设置重读源列表（开关与源都可能在设置页刚被改动），但<b>不自动抓取</b>：
    /// 订阅地址是第三方站点，"打开这一页"不构成去访问它们的授权。</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ReloadSources();
    }

    /// <summary>离开页面只掐掉在途请求：已经抓到的那一轮留着，回来不必重抓。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.CancelLoading();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RefreshAsync();

    private void GoSettings_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("settings");

    /// <summary>点标题＝直接跳文章（这一页不做预览）。打不开的原因由 VM 写回状态行。</summary>
    private void Card_OpenRequested(object sender, long itemId)
    {
        if (sender is ItemCard { ViewModel: { } vm }) _ = ViewModel.OpenAsync(vm);
    }
}
