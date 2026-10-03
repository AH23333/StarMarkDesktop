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
    public const string PinnedGroup = "📌 已置顶";
    public const string OtherGroup = "其他";
    public const string OtherBookmarkGroup = "其他书签";
    public const string OtherFileGroup = "其他文件";

    /// <summary>
    /// 这一行在树里的层级。<paramref name="existsOnDisk"/> 是"哪一格才是真名字"的判据入口
    /// （由宿主注入，本类不碰磁盘）——见 <see cref="FileSegments"/>。
    /// </summary>
    public static string[] GetSegments(Item item, Func<string, bool> existsOnDisk)
    {
        var path = item.Type switch
        {
            ItemType.Bookmark => BookmarkSegments(item),
            ItemType.File => FileSegments(item, existsOnDisk),
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

    /// <summary>
    /// 文件行 → 目录层级（盘符作根，末段是文件名不算层级）。
    /// <para><b>批次 VR 之前这里走 <c>TryPathFromUri</c>（只剥前缀、刻意不解 <c>%XX</c>）</b>：那一条是为"含 <c>#</c>
    /// 的目录名别被 <c>LocalPath</c> 拦腰截断"设的，代价是<b>库里任何一条编码态 URI 会把 <c>Visual%20Studio%20Code</c>
    /// 这种字面串当成文件夹名印给用户</b>（用户真机报的"弹出的文件夹是编码的，不是解码的"，本机库里今天就有一条）。
    /// ⇒ 现在交给 <see cref="LocalFileIdentity.PreferredPathFromUri"/>：<b>先问磁盘哪一格真的在，两格都不在才用原样那格</b>——
    /// 既不截 <c>#</c>（原样那格本来就在盘上时不会被换掉），也不念 <c>%XX</c>，还顺带把"同一目录因两种拼法裂成两个节点"堵了。</para>
    /// <para>键与显示同源：分组用的就是这一串，所以不会出现"节点标题解开了、点进去又按原样找不到"。</para>
    /// </summary>
    public static string[] FileSegments(Item item, Func<string, bool> existsOnDisk)
    {
        if (!string.IsNullOrWhiteSpace(item.Uri) && item.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var local = LocalFileIdentity.PreferredPathFromUri(item.Uri, existsOnDisk);
            local = local.TrimStart('\\').Trim('/');
            var segs = local.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 2)
            {
                // 去掉文件名，保留目录层级（盘符 "C:" 做根）
                return segs.Take(segs.Length - 1).ToArray();
            }
        }
        return new[] { OtherFileGroup };
    }

    /// <summary>伪根/兜底分组的显示优先级（越靠前越先展示），普通文件夹排在最后。</summary>
    public static int RootOrder(string segment) => segment switch
    {
        PinnedGroup => -1,
        GitHubGroup => 0,
        ClipboardGroup => 1,
        OtherBookmarkGroup => 2,
        OtherFileGroup => 3,
        OtherGroup => 4,
        _ => 100,
    };
}