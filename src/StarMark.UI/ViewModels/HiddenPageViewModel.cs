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
            await _repository.SetHiddenAsync(item.Id, false, CancellationToken.None);
            HiddenItems.Remove(item);
            HasItems = HiddenItems.Count > 0;
            EmptyHint = HiddenItems.Count == 0 ? "没有隐藏的条目" : string.Empty;
        }
        catch { }
    }
}
