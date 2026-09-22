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
    private IReadOnlyList<string>? _sources;

    /// <param name="bookmarksPath">显式指定书签文件（测试 / 特殊安装）；其所在目录名不是 Default 时不做 profile 发现。</param>
    protected BrowserBookmarksSource(string? bookmarksPath = null)
        => _bookmarksPath = bookmarksPath ?? DefaultPath();

    /// <summary>来源标识（chrome / edge）。</summary>
    public abstract string SourceId { get; }

    /// <summary>用户数据目录下浏览器名的子路径（如 Google/Chrome 或 Microsoft/Edge）。</summary>
    protected abstract string BrowserSubPath { get; }

    public virtual string DisplayName => "浏览器书签";

    /// <summary>
    /// 要读的书签文件：Default 之外，浏览器还常把真实配置放在 <c>Profile 1/2/…</c>（多账号机器上极常见）。
    /// 只读 Default 时，这类机器上"同步成功"却一条书签都没有（同步记录仍是 Success=true，不提示任何异常）。
    /// 只在目录形状确实是 Chromium 的 <c>…\User Data\Default\Bookmarks</c> 时才去枚举同级 profile
    /// （显式传别的路径时不多扫一次盘）；一次运行内 profile 目录视为不变。
    /// </summary>
    private IReadOnlyList<string> Sources() => _sources ??= DiscoverProfiles();

    private IReadOnlyList<string> DiscoverProfiles()
    {
        var list = new List<string> { _bookmarksPath };
        // _bookmarksPath 形如 …\User Data\Default\Bookmarks ⇒ 再往上一级就是 User Data
        var defaultDir = Path.GetDirectoryName(_bookmarksPath);
        if (defaultDir is null || !string.Equals(Path.GetFileName(defaultDir), "Default", StringComparison.Ordinal))
            return list;
        var userData = Path.GetDirectoryName(defaultDir);
        if (userData is null) return list;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(userData)
                         .Where(d => Path.GetFileName(d).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(d => d, StringComparer.Ordinal))   // 顺序稳定，同步幂等
            {
                var candidate = Path.Combine(dir, "Bookmarks");
                if (!list.Contains(candidate, StringComparer.OrdinalIgnoreCase)) list.Add(candidate);
            }
        }
        catch (Exception ex)
        {
            // 枚举失败（权限/网络盘）不该让书签源整体不可用：Default 那条仍然是好的
            StarLog.Warn($"枚举 {SourceId} 的浏览器 profile 目录失败，本次只读 Default（{userData}）：{ex.Message}");
        }
        return list;
    }

    public bool IsAvailable => Sources().Any(File.Exists);

    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
        // 书签文件常有数 MB：File.ReadAllText + JSON 解析 + 全量映射都是同步的，而点「立即同步」
        // 是从 UI 线程一路 await 下来的 ⇒ 不 offload 就是点一下冻一下。
        // 有意不把 ct 传给 Task.Run：本方法的契约是"任何情况下都不抛，只返回空"。
        => Task.Run(() =>
        {
            try
            {
                if (!IsAvailable) return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var items = new List<Item>();
                // 逐个 profile 读：某一个文件损坏/被占用只跳过它，不牵连其余（旧写法一份坏
                // Bookmarks 会让整台机器的书签一条都不进，且只留一行日志）。
                // 跨 profile 的同一 URL 由 SourceId=归一 URL 天然合并，不会互相覆盖成丢条目。
                foreach (var file in Sources())
                {
                    if (!File.Exists(file)) continue;
                    try
                    {
                        items.AddRange(BookmarksFileParser.ParseFile(file)
                            .Select(e => BookmarkItemFactory.MapItem(e, SourceId, now)));
                    }
                    catch (Exception ex)
                    {
                        // 必须留痕——否则「整份书签一条没导入」会毫无日志地静默发生
                        // （历史上解析器抛 InvalidOperationException 就被无声吞掉过）。
                        StarLog.Warn($"读取浏览器书签失败，跳过该 profile（{file}）：{ex.Message}");
                    }
                }
                return Task.FromResult<IReadOnlyList<Item>>(items);
            }
            catch (Exception ex)
            {
                StarLog.Warn($"同步 {SourceId} 书签出错，本次返回空（{_bookmarksPath}）：{ex.Message}");
                return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
            }
        });

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