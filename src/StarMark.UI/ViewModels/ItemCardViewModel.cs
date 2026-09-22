#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
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

    /// <summary>
    /// 预览弹窗的去重键。Everything 本地文件结果是不入库的虚拟条目（一律 <see cref="Id"/>＝0），
    /// 只按 Id 组键会让「预览 A 后预览 B」被当成重复请求——B 的窗口不出现，只把 A 置顶，故退到 Uri。
    /// </summary>
    public string PreviewDedupeKey => Id > 0 ? $"preview:{Id}" : $"preview:uri:{Uri}";

    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private bool _isPinned;

    /// <summary>键盘导航时的选中高亮（搜索结果 ↑↓ 选择）。</summary>
    [ObservableProperty] private bool _isKeyboardSelected;

    /// <summary>
    /// 启动器（快捷启动格自定义入口）模式。为 true 时卡片代表一个没有主库 Item 的
    /// 合成条目，必须隐藏「隐藏 / 置顶 / 编辑笔记 / 编辑标签 / 发送到桌面」等会误写主库的操作。
    /// </summary>
    [ObservableProperty] private bool _isLauncherMode;

    /// <summary>
    /// 搜索关键词。设置后 <see cref="TitleSegments"/> / <see cref="SubtitleSegments"/>
    /// 会按命中位置切分，供卡片做字段高亮。留空表示不高亮（浏览态卡片）。
    /// </summary>
    [ObservableProperty] private string _highlightQuery = string.Empty;
    partial void OnHighlightQueryChanged(string value)
    {
        OnPropertyChanged(nameof(TitleSegments));
        OnPropertyChanged(nameof(SubtitleSegments));
    }

    public IReadOnlyList<Core.Text.TextSegment> TitleSegments => Core.Text.Highlighter.Split(Title, HighlightQuery);
    public IReadOnlyList<Core.Text.TextSegment> SubtitleSegments => Core.Text.Highlighter.Split(Subtitle, HighlightQuery);
    public string? Notes => _item.Notes;
    public IReadOnlyList<string> Tags => _item.Tags;
    public ItemType Type => _item.Type;
    public string Source => _item.Source;
    public long UpdatedAt => _item.UpdatedAt;

    public string HideMenuText => IsHidden ? "显示" : "隐藏";
    public string PinMenuText => IsPinned ? "取消置顶" : "置顶";

    /// <summary>
    /// 「打开」这一项对该条目到底做什么。剪贴板历史条目没有可启动的目标，
    /// 它的"打开"＝把正文再复制回剪贴板；文案若仍写"打开"，用户点之前无从知道会发生什么。
    /// </summary>
    public string OpenMenuText => ClipboardPolicy.OpensAsCopy(Type, Uri) ? "复制到剪贴板" : "打开";

    /// <summary>同上：对没有链接的条目，这一项复制的是正文而不是"链接/路径"。</summary>
    public string CopyMenuText => ClipboardPolicy.OpensAsCopy(Type, Uri) ? "复制内容" : "复制链接/路径";

    partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(HideMenuText));
    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinMenuText));

    /// <summary>是否可“打开所在位置”（仅本地文件条目）。</summary>
    public bool HasOpenLocation
        => Type == ItemType.File && Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否可"发送到桌面 · 快捷启动"。快捷启动存的是<b>可启动的 URI</b>，没有 URI 的条目
    /// （剪贴板正文、无链接的待办/随记）发过去只会是一条永远打不开的空入口，故不提供该动作。
    /// </summary>
    public bool CanSendToWidget => !IsLauncherMode && !string.IsNullOrWhiteSpace(Uri);

    public string SourceIcon => Type switch
    {
        ItemType.GitHubStar => "⭐",
        ItemType.Bookmark => "🔖",
        ItemType.File => "📄",
        ItemType.Clipboard => "📋",
        ItemType.Todo => "✅",
        ItemType.Note => "📝",
        _ => "•",
    };

    public string RelativeTime => RelativeTimeHelper.Format(UpdatedAt);

    /// <summary>
    /// 本地文件的体积读数（Everything 已按 size 请求字段）。仅 File 类型且拿到体积时才非空——
    /// 0 字节文件也应当显示「0 B」而不是留白，故判定用 has-value 而非大小。
    /// </summary>
    public string SizeText => Type == ItemType.File && _item.FileSize is long b
        ? StarMark.Abstractions.FileSizeText.Human(b)
        : string.Empty;

    public bool HasSizeText => SizeText.Length > 0;

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
        // 置顶状态必须从条目映射：否则置顶条目在主窗口永远显示为未置顶，
        // 「置顶/取消置顶」菜单点击只会再次写入 pinned=1，永远无法取消置顶。
        IsPinned = item.Pinned;
    }

    public void SetHidden(bool value) => IsHidden = value;

    public void SetPinned(bool value) => IsPinned = value;

    public void ApplyNotes(string? notes) { _item.Notes = notes; OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(HasNotes)); }

    public void ApplyTags(IReadOnlyList<string> tags) { _item.Tags = tags.ToList(); OnPropertyChanged(nameof(Tags)); }

    public Item GetItem() => _item;
}
