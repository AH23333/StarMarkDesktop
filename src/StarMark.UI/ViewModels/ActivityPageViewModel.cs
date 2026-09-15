#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 活动时间线页面 ViewModel。对应浏览器扩展 activity tab + act-list。
/// </summary>
public partial class ActivityPageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;
    [ObservableProperty] private bool _hasItems;

    public ObservableCollection<ItemCardViewModel> Activities { get; } = new();

    public ActivityPageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Activities.Clear();
        try
        {
            var items = await _repository.GetRecentAsync(200, CancellationToken.None);
            foreach (var item in items)
                Activities.Add(new ItemCardViewModel(item));
            HasItems = Activities.Count > 0;
            EmptyHint = Activities.Count == 0 ? "暂无活动记录" : string.Empty;
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
}
