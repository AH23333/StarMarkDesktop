#nullable enable
using StarMark.Abstractions;

namespace StarMark.Integrations.Bookmarks;

/// <summary>
/// 浏览器书签源基类（Chrome / Edge 共用）。
/// 读取本地 Bookmarks JSON → 全量映射为 Item。
/// </summary>
public abstract class BrowserBookmarksSource : IItemSource
{
    private readonly string _bookmarksPath;

    protected BrowserBookmarksSource(string? bookmarksPath = null)
        => _bookmarksPath = bookmarksPath ?? DefaultPath();

    /// <summary>来源标识（chrome / edge）。</summary>
    public abstract string SourceId { get; }

    /// <summary>用户数据目录下浏览器名的子路径（如 Google/Chrome 或 Microsoft/Edge）。</summary>
    protected abstract string BrowserSubPath { get; }

    public virtual string DisplayName => "浏览器书签";

    public bool IsAvailable => File.Exists(_bookmarksPath);

    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
    {
        try
        {
            if (!IsAvailable) return Empty();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var items = BookmarksFileParser.ParseFile(_bookmarksPath)
                .Select(e => BookmarkItemFactory.MapItem(e, SourceId, now))
                .ToList();
            return Task.FromResult<IReadOnlyList<Item>>(items);
        }
        catch (Exception ex)
        {
            // 书签文件损坏等：不阻断整体同步，返回空。但必须留痕——否则「整份书签一条没导入」
            // 会毫无日志地静默发生（历史上解析器抛 InvalidOperationException 就被这里无声吞掉）。
            StarLog.Warn($"读取浏览器书签失败，本次 {SourceId} 同步返回空（{_bookmarksPath}）：{ex.Message}");
            return Empty();
        }
    }

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
        => Empty();

    private static Task<IReadOnlyList<Item>> Empty()
        => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());

    private string DefaultPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, BrowserSubPath, "User Data", "Default", "Bookmarks");
    }
}