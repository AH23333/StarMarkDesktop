#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 隐藏条目管理页面 ViewModel。对应浏览器扩展 hidden tab。
/// </summary>
public partial class HiddenPageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;
    [ObservableProperty] private bool _hasItems;

    public ObservableCollection<ItemCardViewModel> HiddenItems { get; } = new();

    public HiddenPageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        HiddenItems.Clear();
        try
        {
            var items = await _repository.GetHiddenAsync(CancellationToken.None);
            foreach (var item in items)
                HiddenItems.Add(new ItemCardViewModel(item));
            HasItems = HiddenItems.Count > 0;
            EmptyHint = HiddenItems.Count == 0 ? "没有隐藏的条目" : string.Empty;
        }
        catch (Exception ex)
        {
            EmptyHint = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(ItemCardViewModel item)
    {
        try
        {
            // 落库成功才从列表里摘掉。旧写法是"先摘再吞异常"：失败时界面当场少一条，
            // 用户以为还原好了，下次进这页它又回来——比报错更难查的是"看起来成功了"。
            // 0 行（那一行已经不在了）走同一条口径：不摘行，并把原因当面说出来，不靠这页没有的状态行（P-40）。
            if (!await _repository.SetHiddenAsync(item.Id, false, CancellationToken.None))
            {
                var word = "还原（取消隐藏）";
                App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.RowGone(word));
                return;
            }
            HiddenItems.Remove(item);
            HasItems = HiddenItems.Count > 0;
            EmptyHint = HiddenItems.Count == 0 ? "没有隐藏的条目" : string.Empty;
        }
        catch (Exception ex)
        {
            // 该页没有状态行可写原因（EmptyHint 只在空列表时可见），所以这里只保证"不骗人"；
            // 可见原因属该页 UI 增强，已单独登记待决策。
            StarLog.Error($"还原隐藏条目失败 (id={item.Id})", ex);
        }
    }
}
