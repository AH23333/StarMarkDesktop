#nullable enable
using StarMark.Abstractions;

namespace StarMark.Integrations.Ditto;

/// <summary>
/// Ditto 剪贴板源：直读 %APPDATA%\Ditto\DB\DittoDB.db（只读）。
/// 文本剪贴板 → Clipboard 条目；文件剪贴板（CF_HDROP）→ 取第一个文件做副标题与 Uri。
/// </summary>
public sealed class DittoSource : IItemSource
{
    private const int MaxSync = 500;

    private readonly string? _dbPath;

    public string SourceId => ItemSources.Ditto;

    public string DisplayName => "Ditto 剪贴板";

    public bool IsAvailable => _dbPath != null && File.Exists(_dbPath);

    /// <summary>Ditto 是外部程序：没装就没有数据库，这一项本来就该是空的（与内置剪贴板历史是两回事）。</summary>
    public string? AvailabilityHint
        => $"本机没有 Ditto 的数据库（找过 {_dbPath}）；没用过 Ditto 属正常，StarMark 自带的「剪贴板历史」不依赖它";

    public DittoSource(string? dbPath = null) => _dbPath = dbPath ?? DefaultDbPath();

    public static string DefaultDbPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ditto", "DB", "DittoDB.db");

    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
        // 「立即同步」由 UI 线程一路 await 到这里，而下面读的是 Ditto 的 SQLite 文件（同步实现，
        // 且可能被 Ditto 自身持写锁）——留在调用线程上就是一次点击冻一次。
        => Task.Run(() =>
        {
            var clip = ReadAsync(r => r.ReadRecent(MaxSync), ct);
            if (clip.Count == 0) return (IReadOnlyList<Item>)Array.Empty<Item>();
            return clip.Select(Map).ToList();
        });

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
    {
        var matched = ReadAsync(r =>
            string.IsNullOrWhiteSpace(query)
                ? r.ReadRecent(Math.Max(1, filter.MaxResults))
                : r.Search(query, Math.Max(1, filter.MaxResults)), ct);
        return Task.FromResult<IReadOnlyList<Item>>(matched.Select(Map).ToList());
    }

    private List<DittoClip> ReadAsync(Func<DittoDatabaseReader, List<DittoClip>> query, CancellationToken ct)
    {
        if (!IsAvailable) return new List<DittoClip>();
        using var reader = new DittoDatabaseReader(_dbPath!);
        return reader.IsOpen ? query(reader) : new List<DittoClip>();
    }

    private static Item Map(DittoClip clip)
    {
        var text = clip.Text ?? string.Empty;
        var firstLine = FirstLine(text, 140);
        var isFiles = clip.Files.Count > 0;

        var item = new Item
        {
            Type = ItemType.Clipboard,
            Source = ItemSources.Ditto,
            SourceId = $"ditto:{clip.Id}",
            Title = isFiles ? string.Join(", ", clip.Files.Take(1)) : firstLine,
            Subtitle = isFiles ? clip.Files[0] : "Ditto 剪贴板",
            Uri = isFiles ? ToFileUri(clip.Files[0]) : string.Empty,
            SearchText = isFiles ? string.Join("\n", clip.Files) + "\n" + text : text,
            CreatedAt = clip.Date,
            UpdatedAt = clip.Date,
            // 正文必须落在 Description：ItemRepository.UpsertOne 落库时用 title+description+notes+tags
            // 重算 search_text（并 CJK 展开），**无条件忽略**来源自带的 SearchText。旧实现把整段正文只塞进
            // SearchText、文本 clip 的 Description 留 null → 首行(≤140字符)之外的正文永远进不了 FTS 索引，
            // 表现为「关键词写在第二行，SQLite 侧搜不到；但 Ditto 实时 LIKE 能搜到」两路径结果不一致，
            // 且 Ditto 未运行/DB 被占用时彻底搜不到。文件 clip 本就把 text 放 Description，故仅文本分支受害。
            Description = text,
        };
        return item;
    }

    private static string FirstLine(string text, int maxLen)
    {
        var idx = text.IndexOfAny(new[] { '\r', '\n' });
        var line = idx >= 0 ? text[..idx] : text;
        line = line.Trim();
        if (line.Length > maxLen)
            line = line[..maxLen] + "…";
        return line;
    }

    private static string ToFileUri(string path)
    {
        try
        {
            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith("/") ? $"file://{normalized}" : $"file:///{normalized}";
        }
        catch
        {
            return string.Empty;
        }
    }
}