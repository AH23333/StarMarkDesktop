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

    public ObservableCollection<ActivityItemViewModel> Activities { get; } = new();

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
            // 真·活动流：读 activity 表（新增/移除事件），而非「最近更新的 200 条条目」。
            // 见扩展对比方案 P1-3。
            var records = await _repository.GetActivityAsync(200, CancellationToken.None);
            foreach (var rec in records)
                Activities.Add(new ActivityItemViewModel(rec));
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
