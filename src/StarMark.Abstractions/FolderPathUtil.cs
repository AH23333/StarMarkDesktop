#nullable enable
using System.Text.Json;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions;

/// <summary>
/// 文件夹树分组路径计算（纯函数，便于单元测试）。
/// 书签 → ExtraJson.BookmarkMeta.FolderPaths（'/' 分隔）；文件 → file:// 目录层级；GitHub/剪贴板 → 伪根分组。
/// <para><b>六颗伪根的名字是稳定 ASCII id，不是界面话</b>（✅P-210）：这一串会落进库（FolderPaths）、
/// 做排序（<see cref="RootOrder"/>）与树节点合并的键，全是<b>身份</b>用途——身份里装一门语言，
/// 换语言那天就多出一批"查无此组"的旧行。显示侧唯一出口是 <see cref="DisplayName"/>：当场念表，
/// 换语言由订阅 <c>Locale.Changed</c> 的那一页重建树来跟随（坑表 #386）。id 与用户文件夹名撞车
/// （用户恰好建了名为 <c>clipboard</c> 的文件夹）属于预先存在的同形碰撞——从前的「已置顶」同形状。</para>
/// </summary>
public static class FolderPathUtil
{
    public const string GitHubGroup = "github-stars";
    public const string ClipboardGroup = "clipboard";
    public const string PinnedGroup = "pinned";
    public const string OtherGroup = "other";
    public const string OtherBookmarkGroup = "other-bookmarks";
    public const string OtherFileGroup = "other-files";

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
    /// 这种字面串当成文件夹名印给用户</b>（本机库里今天就躺着这样一条历史行，树上那个节点因此就叫这个名字）。
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

    /// <summary>
    /// 树上那一格显示的名字（✅P-210 显示侧唯一出口）：六颗伪根 id 与 <see cref="RssFolders.Root"/>
    /// 交给表按当前语言念；默认臂原样交回——落进来的用户文件夹名（含 RSS 的源名一级）是<b>数据</b>，⛔ 翻。
    /// <para>取词发生在<b>这一格要贴上屏幕的那一刻</b>，所以换语言重画自然拿到新话（⛔ 缓存进节点）。
    /// RSS 根是特意的第三种身份：它真的写库（收藏路径第一级），只是显示时念表——零迁移（根照旧叫
    /// <c>RSS订阅</c>，老收藏一颗不动）。</para>
    /// </summary>
    public static string DisplayName(string segment) => segment switch
    {
        GitHubGroup => Locale.T("folder.group.githubStars"),
        ClipboardGroup => Locale.T("folder.group.clipboard"),
        PinnedGroup => Locale.T("folder.group.pinned"),
        OtherBookmarkGroup => Locale.T("folder.group.otherBookmarks"),
        OtherFileGroup => Locale.T("folder.group.otherFiles"),
        OtherGroup => Locale.T("folder.group.other"),
        RssFolders.Root => Locale.T("rss.folder.root"),
        _ => segment,
    };
}