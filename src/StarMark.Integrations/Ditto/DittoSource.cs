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

    public DittoSource(string? dbPath = null) => _dbPath = dbPath ?? DefaultDbPath();

    public static string DefaultDbPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ditto", "DB", "DittoDB.db");

    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
    {
        var clip = ReadAsync(r => r.ReadRecent(MaxSync), ct);
        if (!clip.Any()) return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
        return Task.FromResult<IReadOnlyList<Item>>(clip.Select(Map).ToList());
    }

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
            Description = isFiles ? text : null,
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