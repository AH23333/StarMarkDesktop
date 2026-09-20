#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace StarMark.Abstractions;

/// <summary>
/// 本地文件/文件夹的<b>身份基元</b>：由磁盘路径派生统一的 <c>source_id</c> 与 <c>uri</c>，
/// 作为「同一路径在系统各处（Everything 实时查询、后台全量索引、资源管理器/自绘拖拽登记）
/// 合并为同一条 items 记录」的唯一依据。
/// <para>
/// <b>为何要单一真源</b>：<c>items(source, source_id)</c> 上有唯一索引，去重与合并全部按 <c>source_id</c> 判定，
/// 与 <c>uri</c> 字符串无关。因此只要各来源对同一路径算出<b>相同</b>的 <c>source_id</c>，登记与同步就会天然合并、
/// 绝不产生重复行。<see cref="SourceIdForPath"/> 即沿用原 Everything 侧的哈希口径，供 Everything 适配器与
/// 拖拽登记共用，杜绝两处各写一份哈希造成的漂移。
/// </para>
/// <para><b>只登记索引记录，绝不改动磁盘上的实际文件</b>（不移动 / 改名 / 删除）。</para>
/// </summary>
public static class LocalFileIdentity
{
    /// <summary>文件系统来源标识（与 <see cref="ItemSources.FileSystem"/> 同值）。</summary>
    public const string Source = ItemSources.FileSystem;

    /// <summary>
    /// 路径的稳定哈希作为 <c>source_id</c>：UTF-8 小写归一 → SHA256 → 取前 8 字节的十六进制。
    /// 与 Everything 查询侧、全量索引侧完全一致，保证同一路径幂等合并。
    /// </summary>
    public static string SourceIdForPath(string fullPath)
    {
        var bytes = Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>统一 URI：两斜杠 <c>file://X:/dir/name</c>（反斜杠转正斜杠）。沿用 Everything 既有拼法。</summary>
    public static string UriForPath(string fullPath) => "file://" + fullPath.Replace('\\', '/');

    /// <summary>
    /// 由磁盘路径构造一条<b>未入库</b>（<c>Id=0</c>）的文件条目：<c>source_id</c>/<c>uri</c> 走本类基元，
    /// 标题取文件名、副标题取所在目录。供「拖入即登记」等按路径写入主库的入口使用；
    /// 落库交给 <c>IItemRepository.RecordItemAsync</c>（幂等 upsert，重建 search_text、保留用户态）。
    /// </summary>
    public static Item FromPath(string fullPath, string? title = null)
    {
        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrWhiteSpace(name)) name = fullPath;   // 目录或根：GetFileName 可能为空，退回整路径
        var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var shown = string.IsNullOrWhiteSpace(title) ? name : title!;
        return new Item
        {
            Type = ItemType.File,
            Source = Source,
            SourceId = SourceIdForPath(fullPath),
            Title = shown,
            Subtitle = dir,
            Uri = UriForPath(fullPath),
            SearchText = shown + ' ' + dir,
        };
    }
}
