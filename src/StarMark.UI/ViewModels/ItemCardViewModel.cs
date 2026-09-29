#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
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
    /// 是否给「复制图片」（位图数据进剪贴板，与那颗"复制链接/路径"是两件事）。
    /// 判据全在 <see cref="ItemCardPolicy.CanCopyAsImage"/>，菜单两处读同一个属性——
    /// 分处各判一次就会出现"组件行右键有、主窗卡片没有"。
    /// </summary>
    public bool CanCopyAsImage => ItemCardPolicy.CanCopyAsImage(Uri);

    /// <summary>
    /// 「贴到桌面」这一项点不点得动。<b>可见性与可点性是两件事</b>：前者问"这类行有没有这个动作"
    /// （只认 <see cref="CanCopyAsImage"/>，不在这里第二判一次"什么算图片行"），后者多问一句
    /// "这一张的像素还拿得到吗"。
    /// <para>为什么这一句能白拿：<see cref="ClipboardImageMissing"/> 是构造卡片时算好的一个位
    /// （对账标记，或那次为缩略图做的 stat 顺带答出来的），不新增一次盘查——菜单构建是每行一次的批处理，
    /// 这条纪律就是这么保住的。</para>
    /// </summary>
    public bool CanPinAsImage => CanCopyAsImage && !ClipboardImageMissing;

    /// <summary>
    /// 灰项自己在标题上说原因（全程序口径）：置灰而不解释，用户只看到一个永远点不动的菜单项，
    /// 分不清是"这个功能坏了"还是"这一条本来就不行"。
    /// </summary>
    public string PinImageMenuText => CanPinAsImage
        ? "贴到桌面"
        : "贴到桌面（这张图的文件已不在本机）";

    /// <summary>
    /// 是否可"发送到桌面 · 快捷启动"。快捷启动存的是<b>可启动的 URI</b>，没有 URI 的条目
    /// （剪贴板正文、无链接的待办/随记）发过去只会是一条永远打不开的空入口，故不提供该动作。
    /// 启动器行与热榜候选同样排除（判据在 <see cref="ItemCardPolicy.CanSendToLauncher"/>）：
    /// 前者本来就在快捷启动里，后者会把"顺手一发"变成长期入口。
    /// </summary>
    public bool CanSendToWidget
        => ItemCardPolicy.CanSendToLauncher(IsLauncherMode, IsTrendingRepo, !string.IsNullOrWhiteSpace(Uri), IsRssCandidate);

    /// <summary>
    /// 是否提供"删除这一条"（<b>永久移除</b>，与「隐藏」相对）。只给<b>内置</b>剪贴板历史：
    /// 它的正文是可再复制的一次性内容，删了不心疼；而书签 / Star / 文件条目删掉要么被下次同步
    /// 再拉回来（＝看着无效的动作）、要么连不可重建的用户状态一起丢（＝危险），
    /// 那两类条目的正确动作是「隐藏」。待办 / 随记的删除在它们自己的页面里。
    /// <para>判据必须含 source：<b>Ditto 派条目类型也是 <see cref="ItemType.Clipboard"/></b>，
    /// 但那是外部程序的库，我们从这里删既越界也删不掉（仓储 WHERE 限定 <c>source='clipboard'</c>），
    /// 菜单上就会多出一个只会报"记录已经不在"的死项。"能删"与"删得掉"必须是同一个判据。</para>
    /// </summary>
    public bool CanDeletePermanently => !IsLauncherMode && ClipboardPolicy.IsBuiltinEntry(Source, Type);

    /// <summary>
    /// 这一行是不是 GitHub 热榜候选（外部数据、<b>不入库</b>）。判据收在 <see cref="ItemCardPolicy"/>：
    /// 它决定"哪些动作连出现都不该出现"——置顶/隐藏/笔记/标签对 Id=0 的候选会经按需登记把它写进 items。
    /// </summary>
    public bool IsTrendingRepo => ItemCardPolicy.IsTrendingRepo(Source);

    /// <summary>这一行是不是 RSS 源里的候选（外部数据、<b>不入库</b>）。与热榜同族，但动作集不同：
    /// 没有远端可 Star，也没有"预览"（要先抓正文），多一个"收藏到文件夹"。</summary>
    public bool IsRssCandidate => ItemCardPolicy.IsRssCandidate(Source);

    /// <summary>是否显示"库管理"类动作（置顶 / 隐藏 / 笔记 / 标签 / 发送到桌面）——单一判据，取代逐处写条件。</summary>
    public bool ShowsLibraryActions => ItemCardPolicy.ShowsLibraryActions(IsLauncherMode, IsTrendingRepo, IsRssCandidate);

    /// <summary>是否给「预览」：RSS 候选不给（用户裁决：点条目直接跳文章，网页正文解析暂不做）。
    /// 菜单项与操作栏按钮<b>读同一个属性</b>，两处各判一次就会出现"菜单里没有、按钮上却有"。</summary>
    public bool ShowsPreview => ItemCardPolicy.ShowsPreview(IsRssCandidate);

    /// <summary>是否给「收藏」（热榜与 RSS 两类候选共用这一个动作位，文案按种类分流）。</summary>
    public bool ShowsCollect => ItemCardPolicy.ShowsCollect(IsTrendingRepo, IsRssCandidate);

    partial void OnIsLauncherModeChanged(bool value) => OnPropertyChanged(nameof(ShowsLibraryActions));

    // ===== 热榜行的两个状态位（由宿主在加载与每次操作后回填；非热榜行永远为 false） =====

    private bool _isStarred;
    private bool _isCollected;
    private bool _hasToken = true;

    /// <summary>回填热榜状态。<paramref name="hasToken"/> 只影响提示文案——没 Token 时按钮照点，点了会说为什么要配。</summary>
    public void SetTrendingState(bool starred, bool collected, bool hasToken)
    {
        _isStarred = starred;
        _isCollected = collected;
        _hasToken = hasToken;
        foreach (var name in new[] { nameof(IsStarred), nameof(StarGlyph), nameof(StarLabel), nameof(StarTip),
                                     nameof(IsCollected), nameof(CollectLabel), nameof(CollectTip) })
            OnPropertyChanged(name);
    }

    public bool IsStarred => _isStarred;
    public bool IsCollected => _isCollected;
    public string StarGlyph => ItemCardPolicy.StarGlyph(_isStarred);
    public string StarLabel => ItemCardPolicy.StarLabel(_isStarred);
    public string StarTip => ItemCardPolicy.StarTip(_isStarred, _hasToken);
    public string CollectLabel => ItemCardPolicy.CollectLabelFor(IsRssCandidate, _isCollected);
    public string CollectTip => ItemCardPolicy.CollectTipFor(IsRssCandidate, _isCollected, RssFolderPath);

    /// <summary>RSS 候选收藏后的落点（"RSS订阅 / 源名"）。<b>取自这一行自带的 ExtraJson</b>，
    /// 不另算一遍：提示里说的文件夹与真正写进去的文件夹必须是同一个来源，否则 tooltip 会指一个不存在的路径。</summary>
    public string RssFolderPath
        => string.Join(" / ", FolderPathUtil.BookmarkSegments(_item));

    /// <summary>收藏态回填（两类候选共用这一个状态位；RSS 那边只会由未收过成"已收藏"，不会反向）。</summary>
    public void SetCollected(bool collected)
    {
        if (_isCollected == collected) return;
        _isCollected = collected;
        OnPropertyChanged(nameof(IsCollected));
        OnPropertyChanged(nameof(CollectLabel));
        OnPropertyChanged(nameof(CollectTip));
    }

    /// <summary>热榜行右键：Star / 取消 Star。</summary>
    public event Action<ItemCardViewModel>? StarRequested;

    /// <summary>热榜行右键：收进收藏 / 移出收藏。</summary>
    public event Action<ItemCardViewModel>? CollectRequested;

    /// <summary>由右键菜单工厂（组件里的紧凑行）转发动作；卡片自身的事件走 XAML 事件线。</summary>
    public void RaiseStarRequested() => StarRequested?.Invoke(this);

    public void RaiseCollectRequested() => CollectRequested?.Invoke(this);

    public string SourceIcon => ItemCardPolicy.GlyphOf(Type);

    /// <summary>更新时间（热榜候选不显示：它们没有"本机更新时间"，缺省 0 会印成 1970-01-01）。</summary>
    public string RelativeTime => ItemCardPolicy.ShowsTimeAndStarsLines(IsTrendingRepo, IsRssCandidate)
        ? RelativeTimeHelper.Format(UpdatedAt) : string.Empty;

    /// <summary>
    /// 本地文件的体积读数（Everything 已按 size 请求字段）。仅 File 类型且拿到体积时才非空——
    /// 0 字节文件也应当显示「0 B」而不是留白，故判定用 has-value 而非大小。
    /// </summary>
    public string SizeText => Type == ItemType.File && _item.FileSize is long b
        ? StarMark.Abstractions.FileSizeText.Human(b)
        : string.Empty;

    public bool HasSizeText => SizeText.Length > 0;

    // ────────── 剪贴板图片行（ClipIMG-2a）──────────

    /// <summary>
    /// 这一行是不是剪贴板图片。<b>判据只有一份</b>（<see cref="ClipboardEntry.IsImageOf"/>，
    /// 与仓储层轮转分桶用的是同一句）——卡片自己再认一遍"有没有 clipFile"就会分出两种"什么算图片行"。
    /// </summary>
    /// <remarks>图片那三条判据都在构造时算一次：每条都要解一遍 <c>extra_json</c>，写成表达式属性
    /// 就是让一张卡片付两三次反序列化，而历史页一次要摆几百张。</remarks>
    public bool IsClipboardImageRow { get; }

    /// <summary>
    /// 列表上那一张小图。<b>优先缩略图</b>（§3-Q5：4K 主图按行解码会把滚动的代价压到 UI 线程上），
    /// 缩略图缺了才回退主图，并用 <c>DecodePixelWidth</c> 把解码尺寸钉在 Core 那个长边上——
    /// 回退是为了"看得见"，钉尺寸是为了回退时仍然便宜。文件缺失（对账标的）一律不给图。
    /// </summary>
    public BitmapImage? ClipboardThumb { get; }

    /// <summary>有图可显（决定卡片左侧那块小图占位出不出现）。</summary>
    public bool HasClipboardThumb => ClipboardThumb is not null;

    /// <summary>
    /// 图<b>真的</b>不在了（对账打上的标记，或文件被用户手删）。这一格必须说出来：
    /// 空着不解释，看起来像程序坏了，而不是"文件没了但条目还在"。
    /// </summary>
    public bool ClipboardImageMissing { get; }

    /// <summary>
    /// 取这一行的缩略图源。<paramref name="flaggedMissing"/>＝对账打上的"文件缺失"标记；
    /// 返回值第二项＝<b>这一刻 stat 出来两张文件都不在</b>（用户刚手删，还没到下次对账）。
    /// 两种都得报，但成因不能混：extra 读不出名字时<b>不许</b>冒充"文件没了"。
    /// </summary>
    private (BitmapImage? Thumb, bool FileAbsent) BuildClipboardThumb(bool flaggedMissing)
    {
        if (!IsClipboardImageRow || flaggedMissing) return (null, false);
        var meta = ClipboardEntry.ImageOf(_item);
        if (meta is null) return (null, false);
        // 优先缩略图；缺了才回退主图——回退也必须 stat，否则给一个指不到文件的 UriSource，
        // 症状是"那一格永远空白且不解释"（解码失败不抛到我们能看见的地方）。
        var thumb = ClipAssets.FullPathOf(meta.Value.ThumbName);
        var main = ClipAssets.FullPathOf(meta.Value.MainName);
        var source = thumb is not null && System.IO.File.Exists(thumb) ? thumb
                   : main is not null && System.IO.File.Exists(main) ? main : null;
        if (source is null) return (null, true);
        try
        {
            // DecodePixelWidth 在这条回退路上才真正值钱：主图常是 4K PNG，按原尺寸解码一张
            // 就是几十 MB，而历史页一次摆几百张。
            return (new BitmapImage
            {
                DecodePixelWidth = ClipAssets.ThumbnailMaxEdge,
                UriSource = new Uri(source),
            }, false);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"剪贴板缩略图没法显示（{Title}）：{ex.Message}");
            return (null, false);
        }
    }

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasNotes => !string.IsNullOrEmpty(Notes);
    /// <summary>独立星数行（热榜候选的星数已在副标题里与语言、本期新增一并给出，不再重复一行）。</summary>
    public bool HasStars => ItemCardPolicy.ShowsTimeAndStarsLines(IsTrendingRepo, IsRssCandidate) && StarsCount is long s && s > 0;
    public string StarsText => HasStars && StarsCount is long s ? $"★ {s:N0}" : string.Empty;

    public Windows.UI.Color TagColor(string tag) => TagColorHelper.GetTagColor(tag);

    public ItemCardViewModel(Item item)
    {
        _item = item;
        IsHidden = item.Hidden;
        // 置顶状态必须从条目映射：否则置顶条目在主窗口永远显示为未置顶，
        // 「置顶/取消置顶」菜单点击只会再次写入 pinned=1，永远无法取消置顶。
        IsPinned = item.Pinned;
        IsClipboardImageRow = item.Type == ItemType.Clipboard && ClipboardEntry.IsImageOf(item.ExtraJson);
        var flaggedMissing = IsClipboardImageRow && ClipboardEntry.IsMissing(item);
        // 缩略图在这里建一次而不是绑定时建：BitmapImage 每被 x:Bind 重解析一次就多一个解码器实例，
        // 列表滚动会变成"反复新建同一张小图"。同一次 stat 顺带答出"文件还在不在"，不各查一遍。
        var built = BuildClipboardThumb(flaggedMissing);
        ClipboardThumb = built.Thumb;
        ClipboardImageMissing = flaggedMissing || built.FileAbsent;
    }

    public void SetHidden(bool value) => IsHidden = value;

    public void SetPinned(bool value) => IsPinned = value;

    public void ApplyNotes(string? notes) { _item.Notes = notes; OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(HasNotes)); }

    public void ApplyTags(IReadOnlyList<string> tags) { _item.Tags = tags.ToList(); OnPropertyChanged(nameof(Tags)); }

    public Item GetItem() => _item;
}
