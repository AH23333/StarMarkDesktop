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
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return;

            if (parsed.Scheme == Uri.UriSchemeFile)
            {
                var path = parsed.LocalPath;
                if (Directory.Exists(path))
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(path);
                    await Launcher.LaunchFolderAsync(folder);
                }
                else if (File.Exists(path))
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    await Launcher.LaunchFileAsync(file);
                }
                else
                {
                    // 路径已不存在：退化为 URI 激活（可能无效果，但至少不抛异常）
                    await Launcher.LaunchUriAsync(parsed);
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
}
