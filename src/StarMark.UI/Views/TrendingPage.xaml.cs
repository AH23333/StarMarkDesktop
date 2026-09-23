#nullable enable
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 「GitHub 热榜」页：导航栏「剪贴板」右侧的可选浏览面（开关关闭时该项不显示）。
/// <para>
/// 页面只做装配：抓取 / 缓存 / 失败回读在 <c>TrendingService</c>，两个动作的实现在
/// <see cref="TrendingItemActions"/>（主窗按钮、卡片右键、组件右键三处同源）。
/// </para>
/// </summary>
public sealed partial class TrendingPage : Page
{
    private const string AllLanguages = "全部";

    public TrendingPageViewModel ViewModel { get; }

    /// <summary>
    /// 回填下拉时抑制一次"选择变化"事件。没有这个闸门，进入页面回填选中项就会立刻再发一次抓取，
    /// 表现为每次切回这一页都白抓一遍（缓存明明命中）。
    /// </summary>
    private bool _syncing;

    public TrendingPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TrendingPageViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 开关可能刚在设置页被改动 ⇒ 每次进入按当前设置重读，不靠构造那一次。
        ViewModel.Enabled = App.Services.GetRequiredService<SettingsStore>().LoadTrendingEnabled();

        _syncing = true;
        for (var i = 0; i < PeriodCombo.Items.Count; i++)
            if (PeriodCombo.Items[i] is ComboBoxItem { Tag: string code } && code == ViewModel.PeriodCode)
            { PeriodCombo.SelectedIndex = i; break; }
        _syncing = false;

        await LoadLanguagesAsync();
        await ViewModel.ReloadAsync(force: false);
    }

    /// <summary>离开页面只掐掉在途请求：榜单数据与已回填的两态留在单例 VM 上，回来时不必重抓。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.CancelLoading();
    }

    private async System.Threading.Tasks.Task LoadLanguagesAsync()
    {
        _syncing = true;
        try
        {
            LanguageCombo.ItemsSource = await ViewModel.LoadLanguageOptionsAsync();
            var lang = ViewModel.Language;
            LanguageCombo.SelectedItem = lang.Length == 0 ? AllLanguages : null;
            LanguageCombo.Text = lang.Length == 0 ? AllLanguages : lang;
        }
        finally { _syncing = false; }
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e) => ApplyPeriod();

    private void Language_Changed(object sender, SelectionChangedEventArgs e) => ApplyLanguage();

    /// <summary>手输的语言没有"选中项变化"，用失焦提交；连打几个字不该触发几次抓取。</summary>
    private void Language_Committed(object sender, RoutedEventArgs e) => ApplyLanguage();

    private void ApplyPeriod()
    {
        if (_syncing || PeriodCombo.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        ViewModel.PeriodCode = code;
        App.Services.GetRequiredService<SettingsStore>().SaveTrendingPeriod(code);
    }

    private void ApplyLanguage()
    {
        if (_syncing) return;
        var text = LanguageCombo.Text?.Trim() ?? string.Empty;
        if (text == AllLanguages) text = string.Empty;
        if (string.Equals(text, ViewModel.Language, StringComparison.OrdinalIgnoreCase)) return;

        ViewModel.Language = text;                      // 变化即重载（OnLanguageChanged）
        App.Services.GetRequiredService<SettingsStore>().SaveTrendingLanguage(text);
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        ViewModel.Filter = FilterBox.Text ?? string.Empty;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadAsync(force: true);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelLoading();
        ViewModel.StatusText = "已停止本次抓取，列表保持原样。";
    }

    private void GoSettings_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("settings");

    /// <summary>
    /// 点标题 / 右键「打开」：候选行没有库 Id（Id=0），按 URI 打开——与组件右键那条路径同源。
    /// 统一走 <see cref="LauncherEx"/>，协议白名单只有一处，不在这里各写一份。
    /// </summary>
    private void Card_OpenRequested(object sender, long itemId)
    {
        if (sender is Controls.ItemCard { ViewModel: { } vm }) _ = LauncherEx.OpenAsync(vm.Uri);
    }

    private void Card_CopyRequested(object? sender, ItemCardViewModel vm) => ItemCardActions.CopyUri(vm);
}
