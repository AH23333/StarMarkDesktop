#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions.I18n;
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
    public TrendingPageViewModel ViewModel { get; }

    /// <summary>
    /// 榜单周期那三档走「数据项＋模板」（批次 WI-2c 那颗共用模板）：闭着的那一格才跟着语言当场改口。
    /// <para>⚠ <see cref="LocOption.Value"/> 那串代码是**存进设置档的东西**（<c>SaveTrendingPeriod</c>），
    /// 而清单顺序只是展示顺序——按序号存的话，中间插一档会让所有已存的档安静地指向另一个周期。</para>
    /// </summary>
    public IReadOnlyList<LocOption> PeriodOptions { get; } =
    [
        LocOption.T("trending.daily", "daily"),
        LocOption.T("trending.weekly", "weekly"),
        LocOption.T("trending.monthly", "monthly"),
    ];

    /// <summary>
    /// 回填周期下拉时抑制一次"选择变化"事件。没有这个闸门，进入页面回填选中项就会立刻再发一次抓取，
    /// 表现为每次切回这一页都白抓一遍（缓存明明命中）。
    /// </summary>
    private bool _syncing;

    public TrendingPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TrendingPageViewModel>();
        // 状态行与空态那两行由 VM 交回"键＋实参"，这里只负责钉到元素上（换语言的重贴由 LocText 那一条订阅做）。
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.EmptyHint)) ApplyEmptyHint();
            if (e.PropertyName == nameof(ViewModel.StatusText)) ApplyStatusText();
        };
        ApplyEmptyHint();
        ApplyStatusText();
    }

    /// <summary>
    /// 把 VM 那句"键＋实参"推给 <see cref="LocText"/>。<b>没有话要说时也要设一次空键</b>：
    /// 清空会把注册项一起摘掉，否则留着上一条的注册，换语言那一刻会把已经不该出现的句子又贴回来。
    /// </summary>
    private void ApplyEmptyHint() => LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);

    private void ApplyStatusText() => LocText.SetMessage(StatusTextText, ViewModel.StatusText);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 开关可能刚在设置页被改动 ⇒ 每次进入按当前设置重读，不靠构造那一次。
        ViewModel.Enabled = App.Services.GetRequiredService<SettingsStore>().LoadTrendingEnabled();

        _syncing = true;
        var restored = IndexOfPeriod(ViewModel.PeriodCode);
        if (restored >= 0) PeriodCombo.SelectedIndex = restored;   // 认不得的代码不就近给一档
        _syncing = false;

        await ViewModel.ReloadAsync(force: false);
    }

    /// <summary>离开页面只掐掉在途请求：榜单数据与已回填的两态留在单例 VM 上，回来时不必重抓。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.CancelLoading();
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e) => ApplyPeriod();

    /// <summary>按代码找那一档的下标；找不到回 -1（＝下拉保持未选中，⛔ 不就近挑一个"近的周期"）。</summary>
    private int IndexOfPeriod(string? code)
    {
        for (var i = 0; i < PeriodOptions.Count; i++)
            if (PeriodOptions[i].Value is string value && value == code) return i;
        return -1;
    }

    private void ApplyPeriod()
    {
        if (_syncing || PeriodCombo.SelectedItem is not LocOption { Value: string code }) return;
        ViewModel.PeriodCode = code;
        App.Services.GetRequiredService<SettingsStore>().SaveTrendingPeriod(code);
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
        // 与"抓取消那一条出口"共用同一颗键（trending.statusCancelled）：这句话曾有两份可各自被改的抄本
        // （一份在按钮里、一份在 catch 里），差一个字（停止／取消）也是差一处出处（坑表 #290 同一族）。
        ViewModel.StatusText = LocMessage.Of("trending.statusCancelled");
    }

    private void GoSettings_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("settings");

    /// <summary>
    /// 点标题 / 右键「打开」：候选行没有库 Id（Id=0），按 URI 打开——与组件右键那条路径同源。
    /// 统一走 <see cref="LauncherEx"/>，协议白名单只有一处，不在这里各写一份。
    /// </summary>
    private void Card_OpenRequested(object sender, long itemId)
    {
        // 批次 VR：统一回报链——热榜候选点开没反应时，也要说得出是"浏览器没注册 http"还是"这行没地址"。
        if (sender is Controls.ItemCard { ViewModel: { } vm }) ItemCardActions.OpenUriAndReport(vm.Uri, vm.Id);
    }

    private void Card_CopyRequested(object? sender, ItemCardViewModel vm) => ItemCardActions.CopyUri(vm);
}
