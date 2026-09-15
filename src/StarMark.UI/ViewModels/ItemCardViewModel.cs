#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 单条结果卡片 ViewModel。对应浏览器扩展 ResultCard。
/// 在搜索、文件夹树、标签云、活动、隐藏等所有页面复用。
/// </summary>
public partial class ItemCardViewModel : ObservableObject
{
    private readonly Item _item;

    public long Id => _item.Id;
    public string Title => _item.Title;
    public string Subtitle => _item.Subtitle;
    public string Uri => _item.Uri;
    public string? Description => _item.Description;
    public long? StarsCount => _item.StarsCount;
    [ObservableProperty] private bool _isHidden;
    public string? Notes => _item.Notes;
    public IReadOnlyList<string> Tags => _item.Tags;
    public ItemType Type => _item.Type;
    public string Source => _item.Source;
    public long UpdatedAt => _item.UpdatedAt;

    public string HideMenuText => IsHidden ? "显示" : "隐藏";

    partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(HideMenuText));

    public string SourceIcon => Type switch
    {
        ItemType.GitHubStar => "⭐",
        ItemType.Bookmark => "🔖",
        ItemType.File => "📄",
        ItemType.Clipboard => "📋",
        _ => "•",
    };

    public string RelativeTime => RelativeTimeHelper.Format(UpdatedAt);

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasNotes => !string.IsNullOrEmpty(Notes);
    public bool HasStars => StarsCount is long s && s > 0;
    public string StarsText => HasStars && StarsCount is long s ? $"★ {s:N0}" : string.Empty;

    public Windows.UI.Color TagColor(string tag) => TagColorHelper.GetTagColor(tag);

    public ItemCardViewModel(Item item)
    {
        _item = item;
        IsHidden = item.Hidden;
    }

    public void SetHidden(bool value) => IsHidden = value;

    public void ApplyNotes(string? notes) { _item.Notes = notes; OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(HasNotes)); }

    public void ApplyTags(IReadOnlyList<string> tags) { _item.Tags = tags.ToList(); OnPropertyChanged(nameof(Tags)); }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!string.IsNullOrEmpty(Uri))
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(Uri)); }
            catch { }
        }
    }

    [RelayCommand]
    private Task ToggleHiddenAsync()
    {
        // 由调用方（页面 ViewModel）处理
        return Task.CompletedTask;
    }

    public Item GetItem() => _item;
}
