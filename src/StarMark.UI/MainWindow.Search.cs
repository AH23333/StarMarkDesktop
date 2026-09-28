#nullable enable
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Abstractions.Language;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemTray;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI;

/// <summary>
/// MainWindow 的这一段——搜索这一头：输入合并、排序/来源/文件类型/语言四条筛选，以及数据变化时要刷哪一页。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    /// <summary>数据变更后的主界面刷新：计数恒刷，当前页按类型重载（见 <see cref="RefreshCurrentPageForData"/>）。</summary>
    private async Task RefreshOnDataChangedAsync()
    {
        await ViewModel.LoadCountsAsync();
        RefreshCurrentPageForData();
    }

    /// <summary>
    /// 只重载「当前正显示、且内容来自条目库」的页，避免无谓打库。
    /// <para>
    /// 刻意<b>不</b>自动重载 <see cref="SearchPage"/>：用户可能正在搜索框逐字输入，
    /// 广播一来就重跑查询会打断编辑（且结果本由 OnQueryChanged 驱动，无需外部刷新）；
    /// <see cref="SettingsPage"/> 无条目列表，同样跳过。
    /// </para>
    /// </summary>
    private void RefreshCurrentPageForData()
    {
        switch (ContentFrame?.Content)
        {
            case FolderTreePage ftp: _ = ftp.ViewModel.LoadCommand.ExecuteAsync(null); break;
            case TagsPage tp: tp.ViewModel.LoadCommand.Execute(null); break;
            case HiddenPage hp: hp.ViewModel.LoadCommand.Execute(null); break;
            case ActivityPage ap: ap.ViewModel.LoadCommand.Execute(null); break;
        }
    }

    private void SearchFromWidget(string query)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Present(false);
            SearchBox.Text = query;
            if (string.IsNullOrWhiteSpace(query)) return;
            ViewModel.Query = query;
            ViewModel.CurrentPageTag = "search";
            NavView.SelectedItem = null;
            SetNavVisible(false);
            ContentFrame.Navigate(typeof(SearchPage));
            PushToolbarToContent();
            if (ContentFrame.Content is SearchPage sp)
                sp.ViewModel.Query = query;
        });
    }

    // ===== 搜索框防抖 =====

    private void SearchBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        // 单实例：本方法按击键频率触发，续期只需 Stop/Start，不必每键 new 一个计时器再另挂委托。
        var timer = _debounceTimer ??= BuildDebounceTimer();
        timer.Stop();
        timer.Start();
    }

    private DispatcherTimer BuildDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (s, args2) =>
        {
            timer.Stop();
            var q = SearchBox.Text;
            ViewModel.Query = q;

            if (!string.IsNullOrWhiteSpace(q) && ViewModel.CurrentPageTag != "search")
            {
                // 顶部搜索框常驻：输入自动切到搜索页（搜索页不在导航菜单内）
                ViewModel.CurrentPageTag = "search";
                NavView.SelectedItem = null;
                SetNavVisible(false);
                DispatcherQueue.TryEnqueue(() =>
                {
                    ContentFrame.Navigate(typeof(SearchPage));
                    PushToolbarToContent();
                    if (ContentFrame.Content is SearchPage sp)
                        sp.ViewModel.Query = q;
                });
            }
            else if (ContentFrame.Content is SearchPage sp)
            {
                sp.ViewModel.Query = q;
                // 清空关键词后恢复导航栏，否则用户被困在搜索页无法切回浏览页
                if (string.IsNullOrWhiteSpace(q)) SetNavVisible(true);
            }
        };
        return timer;
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (ContentFrame.Content is not SearchPage sp || sp.ViewModel.Results.Count == 0) return;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                sp.MoveKeyboardSelection(1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                sp.MoveKeyboardSelection(-1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when sp.ViewModel.SelectedItem is { } selected:
                var ctrl = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (ctrl) ItemCardActions.OpenLocation(selected);
                else ItemCardActions.Open(SearchBox.XamlRoot, selected);   // 传视图模型：Everything 文件结果是 Id=0 虚拟条目，按 Id 查库会静默失效
                e.Handled = true;
                break;
        }
    }

    // ===== 工具栏事件（全局唯一：排序、来源、显示隐藏）=====

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PushToolbarToContent();
    }

    private string _currentSource = "all";

    private void SourceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        _currentSource = tag;
        SetSourceButtonsHighlight(tag);
        // 语言是 Star 的维度：来源切到本地文件时它无从生效，直接禁用而不是"选了没反应"。
        LanguageCombo.IsEnabled = tag != "file";
        PushToolbarToContent();
    }

    // ───────── 「类型」多选（本地文件；翻译成 Everything 检索式，不进搜索框）─────────

    private bool _suppressFileKindEvents;

    private void FileKind_Changed(object sender, RoutedEventArgs e) => ApplyFileKinds();

    private void FileKind_Clear_Click(object sender, RoutedEventArgs e)
    {
        // 逐个取消会各触发一次 Unchecked；不抑制就会为"清除"这一动作重跑 N 次搜索。
        _suppressFileKindEvents = true;
        try
        {
            foreach (var box in FileKindBoxes()) box.IsChecked = false;
        }
        finally
        {
            _suppressFileKindEvents = false;
        }
        ApplyFileKinds();
    }

    private IEnumerable<CheckBox> FileKindBoxes()
        => FileKindPanel is { } a && FileCondPanel is { } b
            ? a.Children.OfType<CheckBox>().Concat(b.Children.OfType<CheckBox>())
            : Enumerable.Empty<CheckBox>();

    /// <summary>当前勾选的类型（面板 Tag 存枚举名）。面板尚未创建时视为无勾选。</summary>
    private IReadOnlyList<FileKind> CurrentFileKinds()
    {
        var kinds = new List<FileKind>(8);
        foreach (var box in FileKindBoxes())
            if (box.IsChecked == true && box.Tag is string tag
                && Enum.TryParse<FileKind>(tag, out var kind)) kinds.Add(kind);
        return kinds;
    }

    private void ApplyFileKinds()
    {
        if (_suppressFileKindEvents || FileKindButton == null) return;
        var kinds = CurrentFileKinds();
        FileKindButton.Content = kinds.Count == 0 ? "类型筛选" : $"类型筛选 · {kinds.Count}";
        // 只下发给搜索页：书签/Star 没有扩展名与体积概念，类型筛选对它们无意义。
        if (ContentFrame?.Content is SearchPage sp) sp.ViewModel.SetFileKinds(kinds);
    }

    private string CurrentSourceTag() => _currentSource;

    // ───────── 语言筛选（常态显示于工具栏；选项由搜索页结果聚合）─────────

    private const string LanguageAllItem = "语言：全部";
    private bool _syncingLanguageCombo;
    private bool _languageHooked;

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLanguageCombo) return;
        var selected = LanguageCombo.SelectedItem as string;
        var lang = (selected is null || selected == LanguageAllItem) ? string.Empty : selected;
        // 单一事实来源：主界面语言下拉同时驱动浏览（文件夹/star）与搜索两套过滤
        ViewModel.CurrentLanguage = lang;
        if (ContentFrame?.Content is SearchPage sp) sp.ViewModel.CurrentLanguage = lang;
    }

    private void HookLanguageOptions(SearchPageViewModel vm)
    {
        if (_languageHooked) return;
        _languageHooked = true;
        // SearchPageViewModel 是单例：选项集合变化（每次搜索后重建）时同步下拉框
        vm.AvailableLanguages.CollectionChanged += (_, _) => SyncLanguageCombo(vm);
    }

    /// <summary>
    /// 把语言选项同步到工具栏下拉框：目录（始终直选）+ 搜索页聚合到的语言去重合并；
    /// 当前选中以 <see cref="MainViewModel.CurrentLanguage"/> 为准（null/空 = 「语言：全部」）。
    /// </summary>
    private void SyncLanguageCombo(SearchPageViewModel? vm)
    {
        _syncingLanguageCombo = true;
        try
        {
            // 只取 star 条目中真实存在的语言（由 LoadStarLanguagesAsync 从库里聚合），
            // 不再使用 LanguageCatalog.AllNames —— 避免出现当前 star 项目不存在的语言选项。
            var set = new HashSet<string>(_starLanguages, StringComparer.OrdinalIgnoreCase);
            // 搜索结果里实际出现的语言一并合并（同样是真实存在的语言）
            if (vm is not null) foreach (var l in vm.AvailableLanguages) set.Add(l);
            var items = new List<string> { LanguageAllItem };
            items.AddRange(set);
            LanguageCombo.ItemsSource = items;
            var current = ViewModel.CurrentLanguage;
            LanguageCombo.SelectedIndex = string.IsNullOrEmpty(current)
                ? 0
                : Math.Max(0, items.IndexOf(current));
        }
        finally { _syncingLanguageCombo = false; }
    }

    /// <summary>
    /// 从库里聚合 star 条目中<b>真实存在</b>的编程语言，作为语言下拉的数据源。
    /// 保证下拉里每一项都至少对应一个 star 项目，不会出现未被任何 star 使用的语言。
    /// </summary>
    private async Task LoadStarLanguagesAsync()
    {
        try
        {
            var repo = App.Services.GetRequiredService<IItemRepository>();
            if (repo is null) return;
            var langs = await repo.GetStarLanguagesAsync();
            _starLanguages.Clear();
            _starLanguages.AddRange(langs);
            SyncLanguageCombo(ContentFrame?.Content as SearchPageViewModel);
        }
        catch (Exception ex)
        {
            StarLog.Error("加载 star 语言列表失败", ex);
        }
    }

    private string CurrentSortTag()
        => SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "recent";
}
