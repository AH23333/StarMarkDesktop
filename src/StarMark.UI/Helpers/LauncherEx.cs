#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace StarMark.UI.Helpers;

/// <summary>
/// 统一打开条目 URI 的助手。集中处理 file:// 与 http(s):// 的差异：
/// <list type="bullet">
///   <item>文件/文件夹 URI 必须用 <see cref="Launcher.LaunchFileAsync"/>，
///   <see cref="Launcher.LaunchUriAsync"/> 对 <c>file://</c> 在多数环境下静默失效（"拖入快捷访问的文件打不开"根因）。</item>
///   <item>网页 URI 用 <see cref="Launcher.LaunchUriAsync"/>。</item>
/// </list>
/// 快捷启动格与搜索结果组件共用，避免重复逻辑。
/// </summary>
public static class LauncherEx
{
    public static async Task OpenAsync(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        // 协议白名单闸门：条目 URI 是可被导入/同步/外部数据影响的字符串，
        // 只放行 http/https/file，拒绝 javascript:/data:/ms-msdt:/自定义协议等借 Shell 协议处理器执行的记录。
        if (!StarMark.Abstractions.LaunchGuard.IsAllowedScheme(uri))
        {
            StarMark.Abstractions.StarLog.Warn($"拒绝以非法协议打开条目：{uri}");
            return;
        }
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return;

            if (parsed.Scheme == Uri.UriSchemeFile)
            {
                // file:// 有两类互补的生产者，单靠一种还原法都会错：
                //  · Everything / LocalFileIdentity.UriForPath → 原始两斜杠，'#'、空格、非 ASCII 一律不编码。
                //    只有 TryPathFromUri 能原样还原（parsed.LocalPath 会把裸 '#' 之后当片段截断）。
                //  · 快捷启动经 new Uri(path).AbsoluteUri → percent 编码（%20 / %23 / %E5%B7%A5…）。
                //    只有 parsed.LocalPath 能解码还原（TryPathFromUri 故意不解 %XX，以维持与 UriForPath 的往返契约）。
                // 取磁盘上确实存在的那个候选（优先原始形态），两类输入都能打开，且不改动任何存储格式。
                StarMark.Abstractions.LocalFileIdentity.TryPathFromUri(uri, out var rawPath);
                var decodedPath = SafeLocalPath(parsed);
                var path = FirstExisting(rawPath, decodedPath);
                if (path is null)
                {
                    // 两种还原都不存在（文件已删）：退化为 URI 激活（可能无效果，但不抛异常）
                    await Launcher.LaunchUriAsync(parsed);
                }
                else if (Directory.Exists(path))
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(path);
                    await Launcher.LaunchFolderAsync(folder);
                }
                else
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    await Launcher.LaunchFileAsync(file);
                }
                return;
            }

            await Launcher.LaunchUriAsync(parsed);
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error($"打开条目失败: {uri}", ex);
        }
    }

    private static string SafeLocalPath(Uri parsed)
    {
        try { return parsed.LocalPath; } catch { return string.Empty; }
    }

    /// <summary>返回第一个"磁盘上存在"的候选路径；都不存在返回 null。</summary>
    private static string? FirstExisting(string? a, string? b)
    {
        if (!string.IsNullOrEmpty(a) && (File.Exists(a) || Directory.Exists(a))) return a;
        if (!string.IsNullOrEmpty(b) && (File.Exists(b) || Directory.Exists(b))) return b;
        return null;
    }
}
