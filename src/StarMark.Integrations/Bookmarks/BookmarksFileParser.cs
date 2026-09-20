#nullable enable
using System.Text.Json;

namespace StarMark.Integrations.Bookmarks;

/// <summary>一条书签记录（解析自 Chrome/Edge Bookmarks JSON 导出）。</summary>
public sealed class BookmarkEntry
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;

    /// <summary>所在文件夹路径链（自根起，仅含真实文件夹名）。</summary>
    public List<string> FolderPaths { get; init; } = new();

    /// <summary>收藏时间（Unix 秒）。</summary>
    public long BookmarkedAt { get; init; }
}

/// <summary>
/// Chrome / Edge 书签 JSON 解析器。
/// 结构：{ "roots": { "bookmark_bar": {...}, "other": {...}, "synced": {...} } }
///   - type="url"    条目：字段 name / url / date_added
///   - type="folder" 文件夹：字段 name / children
/// date_added 为 Chrome 自 1601-01-01 起的微秒数（FILETIME）。
/// </summary>
public static class BookmarksFileParser
{
    /// <summary>1601-01-01 至 1970-01-01 的秒数。</summary>
    public const long FiletimeToUnixEpochSeconds = 11_644_473_600L;

    public static List<BookmarkEntry> ParseFile(string filePath)
        => string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)
            ? new List<BookmarkEntry>()
            : ParseJson(File.ReadAllText(filePath));

    public static List<BookmarkEntry> ParseJson(string json)
    {
        var result = new List<BookmarkEntry>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        using var doc = ParseSafe(json);
        if (doc is null) return result;
        if (!doc.RootElement.TryGetProperty("roots", out var roots) ||
            roots.ValueKind != JsonValueKind.Object) return result;

        foreach (var root in roots.EnumerateObject())
        {
            if (root.Value.ValueKind == JsonValueKind.Object)
                WalkNode(root.Value, new List<string>(), result, isRoot: true);
        }
        return result;
    }

    /// <summary>损坏的 JSON 视为空书签集合（不向同步链路抛异常）。</summary>
    private static JsonDocument? ParseSafe(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WalkNode(JsonElement node, List<string> ancestors, List<BookmarkEntry> result, bool isRoot = false)
    {
        if (!node.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String) return;
        var type = typeProp.GetString();

        if (type == "url")
        {
            var url = GetString(node, "url");
            if (string.IsNullOrWhiteSpace(url)) return;

            // date_added 既可能是字符串（Chrome 常见），也可能是未加引号的数字（部分导出/旧版）。
            // 直接 .GetString() 对数字型 JsonElement 会抛 InvalidOperationException——一旦抛出，
            // 整个书签树解析中断、上层 catch 吞掉后静默返回空，导致「整份书签一条都导不进来」。
            // 这里按 ValueKind 分别安全取值；数字用 TryGetInt64（避免 GetDouble 对 >2^53 微秒丢精度）。
            long added = 0;
            if (node.TryGetProperty("date_added", out var d))
            {
                long micros = 0;
                bool parsed = d.ValueKind switch
                {
                    JsonValueKind.String => long.TryParse(d.GetString(), out micros),
                    JsonValueKind.Number => d.TryGetInt64(out micros),
                    _ => false,
                };
                if (parsed) added = FromChromeTime(micros);
            }

            result.Add(new BookmarkEntry
            {
                Title = GetString(node, "name"),
                Url = url,
                FolderPaths = new List<string>(ancestors),
                BookmarkedAt = added,
            });
        }
        else if (type == "folder" &&
                 node.TryGetProperty("children", out var children) &&
                 children.ValueKind == JsonValueKind.Array)
        {
            // 根级文件夹（bookmark_bar / other / synced）名称不参与路径，避免带出本地化根名
            var sub = isRoot ? new List<string>(ancestors) : new List<string>(ancestors)
            {
                GetString(node, "name"),
            };

            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object)
                    WalkNode(child, sub, result);
            }
        }
    }

    /// <summary>Chrome 时间（1601 起微秒）→ Unix 秒。</summary>
    public static long FromChromeTime(long microseconds)
        => microseconds / 1_000_000L - FiletimeToUnixEpochSeconds;

    private static string GetString(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}