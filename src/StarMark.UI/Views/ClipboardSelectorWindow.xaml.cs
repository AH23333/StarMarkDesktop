#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace StarMark.UI.Views;

/// <summary>
/// 剪贴板历史选择器（P-63：全局热键唤起的键呼浮层，对标 Win+V）——输入筛选、↑↓ 选择、
/// 回车把选中的那条复制回剪贴板，Esc／点到别处即收起。
/// <para>
/// <b>复制语义零重写</b>：列表加载与「再复制」全部委托 <see cref="ClipboardPageViewModel"/>
/// （<c>LoadAsync</c>＋<c>ReuseAsync</c>，含图片行 <c>SetBitmap</c> 分流与回声登记）——这里只添
/// 「键呼」的那层皮。把复制逻辑再抄一份的后果是两条出口各修各的：文字行改了图片行不知道。
/// </para>
/// <para>
/// 单例＋隐藏不关闭：热键每按一次只可能有一个浮层在屏幕上；复用同一扇窗也把首建的
/// 样式开销留在第一次（后续唤起只是显示，与组件窗的 Reveal/HideTemporary 同一口径）。
/// </para>
/// </summary>
public sealed partial class ClipboardSelectorWindow : Window
{
    private const double WidthDip = 420;
    private const double HeightDip = 480;

    private static ClipboardSelectorWindow? _instance;

    private readonly ClipboardPageViewModel _vm;
    private readonly List<ItemCardViewModel> _all = new();
    private List<ItemCardViewModel> _view = new();

    private ClipboardSelectorWindow()
    {
        InitializeComponent();
        _vm = new ClipboardPageViewModel(App.Services.GetRequiredService<IItemRepository>());
        WindowInterop.RemoveDefaultWindowFrame(this);
        WindowInterop.SetTopmost(this, true);
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        SearchBox.KeyDown += Search_KeyDown;
        List.KeyDown += List_KeyDown;
        // 点到浮层之外（无论点了谁）就收起：这是键呼浮层的收起语义——留着不关，
        // 下一次唤起它就挡在用户刚切换到的那个程序上面（topmost）。
        // WinUI 3 的 Window 没有 Deactivated 事件，失焦那一下从 Activated 的状态位读（Deactivated＝失焦）。
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) HideSelector();
        };
        Closed += (_, _) => _instance = null;
    }

    /// <summary>全局热键的入口（App 里注册）：唤起选择器。永远单例——连按两次热键是"刷新"，不是叠一扇窗。</summary>
    public static void Present()
    {
        _instance ??= new ClipboardSelectorWindow();
        _instance.ShowSelector();
    }

    private void ShowSelector()
    {
        // 上一次失败回执与筛选词不带进这一轮：键呼浮层每次都从"全量、无话"开始。
        SearchBox.Text = string.Empty;
        StatusLine.Visibility = Visibility.Collapsed;
        PositionNearCursor();
        AppWindow.Show();
        WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(this));
        Activate();
        SearchBox.Focus(FocusState.Programmatic);
        _ = ReloadAsync();
    }

    private void HideSelector()
    {
        WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE);
        AppWindow.Hide();
    }

    /// <summary>出现在光标所在的那块屏上（Win+V 的落点习惯），取不到光标就退回第一块。</summary>
    private void PositionNearCursor()
    {
        var bounds = default(RectInt32);
        var scale = 1d;
        var monitors = WindowInterop.ListMonitors();
        if (monitors.Count > 0)
        {
            bounds = monitors[0].Bounds;
            scale = monitors[0].Scale;
        }
        if (WindowInterop.GetCursorPos(out var cursor))
            foreach (var m in monitors)
                if (cursor.X >= m.Bounds.X && cursor.X < m.Bounds.X + m.Bounds.Width
                    && cursor.Y >= m.Bounds.Y && cursor.Y < m.Bounds.Y + m.Bounds.Height)
                {
                    bounds = m.Bounds;
                    scale = m.Scale;
                    break;
                }
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var width = Math.Min((int)Math.Round(WidthDip * scale), bounds.Width);
        var height = Math.Min((int)Math.Round(HeightDip * scale), bounds.Height);
        AppWindow.MoveAndResize(new RectInt32(
            bounds.X + Math.Max(0, (bounds.Width - width) / 2),
            bounds.Y + Math.Max(0, (bounds.Height - height) / 2),
            width, height));
    }

    private async Task ReloadAsync()
    {
        try
        {
            // 与历史页同一只命令（[RelayCommand] 生成的 LoadCommand）：加载语义零重写，
            // 页面那边改了排序/上限，浮层跟着走。
            await _vm.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StarLog.Error("剪贴板选择器加载历史失败", ex);
        }
        _all.Clear();
        foreach (var entry in _vm.Entries) _all.Add(entry);
        ApplyFilter();
    }

    /// <summary>按筛选词裁列表；首条默认选中——回车复制的是"看得见亮着的那条"，不是"最近的一条"。</summary>
    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        _view = query.Length == 0
            ? _all.ToList()
            : _all.Where(r => Matches(r.Title, query) || Matches(r.Description, query)).ToList();
        List.ItemsSource = _view;
        List.SelectedIndex = _view.Count > 0 ? 0 : -1;
        EmptyText.Visibility = _view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(string? text, string query)
        => text is not null && text.Contains(query, StringComparison.OrdinalIgnoreCase);

    // ────────── 键盘流转：焦点留在筛选框（继续输入），↑↓ 动的是列表的选中位 ──────────

    private void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                _ = CommitSelectionAsync();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                HideSelector();
                e.Handled = true;
                break;
        }
    }

    private void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                _ = CommitSelectionAsync();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                HideSelector();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_view.Count == 0) return;
        var index = List.SelectedIndex;
        index = index < 0
            ? (delta > 0 ? 0 : _view.Count - 1)
            : Math.Clamp(index + delta, 0, _view.Count - 1);
        List.SelectedIndex = index;
        List.ScrollIntoView(_view[index]);
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemCardViewModel vm) _ = CommitAsync(vm);
    }

    private async Task CommitSelectionAsync()
    {
        if (List.SelectedItem is ItemCardViewModel vm) await CommitAsync(vm);
    }

    /// <summary>
    /// 复制选中的那条：成功即收起（回车复制这条链的终点就是"回到刚才在做的那个程序里粘贴"）；
    /// 失败<b>留着窗</b>并把 <see cref="ClipboardPageViewModel"/> 状态行的那句原因亮出来——
    /// 静默收起等于"按了回车什么都没发生"，与功能坏了无法区分。
    /// </summary>
    private async Task CommitAsync(ItemCardViewModel vm)
    {
        try
        {
            if (await _vm.ReuseAsync(vm))
            {
                HideSelector();
                return;
            }
        }
        catch (Exception ex)
        {
            StarLog.Error($"剪贴板选择器再复制失败 (id={vm.Id})", ex);
        }
        if (_vm.StatusText is { } status)
        {
            LocText.SetMessage(StatusLine, status);
            StatusLine.Visibility = Visibility.Visible;
        }
    }
}
