#nullable enable
using System.Text.Json;

namespace StarMark.Abstractions;

/// <summary>
/// 文件夹树分组路径计算（纯函数，便于单元测试）。
/// 书签 → ExtraJson.BookmarkMeta.FolderPaths（'/' 分隔）；文件 → file:// 目录层级；GitHub/剪贴板 → 伪根分组。
/// </summary>
public static class FolderPathUtil
{
    public const string GitHubGroup = "⭐ GitHub Stars";
    public const string ClipboardGroup = "📋 剪贴板";
    public const string OtherGroup = "其他";
    public const string OtherBookmarkGroup = "其他书签";
    public const string OtherFileGroup = "其他文件";

    public static string[] GetSegments(Item item)
    {
        var path = item.Type switch
        {
            ItemType.Bookmark => BookmarkSegments(item),
            ItemType.File => FileSegments(item),
            ItemType.GitHubStar => new[] { GitHubGroup },
            ItemType.Clipboard => new[] { ClipboardGroup },
            _ => new[] { OtherGroup },
        };
        return path.Length == 0 ? new[] { OtherGroup } : path;
    }

    public static string[] BookmarkSegments(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.ExtraJson)) return new[] { OtherBookmarkGroup };
        try
        {
            var meta = JsonSerializer.Deserialize<BookmarkMeta>(item.ExtraJson);
            if (meta?.FolderPaths is { Count: > 0 } paths)
            {
                // FolderPaths 数组本身即层级（每个元素 = 一级文件夹名），
                // 不再按 '/' 自动切分——文件夹名含 '/' 时不应被拆出错误层级。
                var segs = paths.Select(p => p.Trim()).Where(s => !string.IsNullOrEmpty(s) && s.Trim('/').Length > 0).ToArray();
                if (segs.Length > 0) return segs;
            }
        }
        catch { }
        return new[] { OtherBookmarkGroup };
    }

    public static string[] FileSegments(Item item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uri) && item.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var local = new Uri(item.Uri).LocalPath.TrimStart('\\').Trim('/');
                var segs = local.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length >= 2)
                {
                    // 去掉文件名，保留目录层级（盘符 "C:" 做根）
                    return segs.Take(segs.Length - 1).ToArray();
                }
            }
            catch { }
        }
        return new[] { OtherFileGroup };
    }

    /// <summary>伪根/兜底分组的显示优先级（越靠前越先展示），普通文件夹排在最后。</summary>
    public static int RootOrder(string segment) => segment switch
    {
        GitHubGroup => 0,
        ClipboardGroup => 1,
        OtherBookmarkGroup => 2,
        OtherFileGroup => 3,
        OtherGroup => 4,
        _ => 100,
    };
}