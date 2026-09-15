#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Controls;

/// <summary>
/// 可复用结果卡片用户控件。对应浏览器扩展 ResultCard。
/// 通过 ViewModel 属性 + 事件回调与外部交互。
/// </summary>
public sealed partial class ItemCard : UserControl
{
    /// <summary>打开条目事件（点击标题/卡片主体时触发）。</summary>
    public event EventHandler<long>? OpenRequested;

    /// <summary>编辑笔记事件。</summary>
    public event EventHandler<ItemCardViewModel>? EditNoteRequested;

    /// <summary>编辑标签事件。</summary>
    public event EventHandler<ItemCardViewModel>? EditTagsRequested;

    /// <summary>隐藏切换事件。</summary>
    public event EventHandler<ItemCardViewModel>? HideRequested;

    private ItemCardViewModel? _viewModel;

    public ItemCardViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            DataContext = value;
            Bindings.Update();
        }
    }

    public ItemCard()
    {
        InitializeComponent();
    }

    private void Title_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel != null)
            OpenRequested?.Invoke(this, ViewModel.Id);
    }

    private void Menu_Open(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            OpenRequested?.Invoke(this, ViewModel.Id);
    }

    private void EditNote_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            EditNoteRequested?.Invoke(this, ViewModel);
    }

    private void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            EditTagsRequested?.Invoke(this, ViewModel);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            HideRequested?.Invoke(this, ViewModel);
    }
}