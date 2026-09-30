#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把组件结果行<b>拖出</b>到桌面 / 资源管理器（EverythingToolbar 式）。
/// <list type="bullet">
///   <item>本地文件 / 文件夹（含 Everything 实时源的 <c>Id=0</c> 虚拟行）：以 <see cref="IStorageItem"/> 引用提供，
///   由 Shell 完成落盘。<c>RequestedOperation=Copy</c> 作默认，避免同盘拖放<b>移动</b>掉源文件——本应用只交出引用，绝不改动磁盘。</item>
///   <item>网页 / GitHub：以 <see cref="DataPackage.SetWebLink"/> 提供，拖出为 <c>.url</c> 快捷方式。</item>
///   <item>其它：退化为纯文本 URI。</item>
/// </list>
/// 拖出是<b>跨进程</b>到 Explorer/桌面，属 WinUI 3 已知脆弱路径，需在真机复验（构建绿≠验收）。
/// </summary>
public static class ItemDragHelper
{
    public static void BeginFromUri(string? uri, DragStartingEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        var data = args.Data;
        data.RequestedOperation = DataPackageOperation.Copy;
        data.SetText(uri);

        // 本地文件 / 文件夹：取 IStorageItem 需要异步，挂 deferral 保活到填充完成后再放行拖放。
        // 还原走 LocalFileIdentity 那颗（两类互补的 file:// 生产者）——只试原始形态时，剪贴板图片行那种
        // percent 编码的 Uri 判不出"存在"，拖出去会静默退成一行文本（用户以为交出了文件）。
        if (LocalFileIdentity.TryExistingPath(uri, ExistsOnDisk, out var path))
        {
            var deferral = args.GetDeferral();
            _ = FillStorageItemAsync(data, path, deferral);
            return;
        }

        // 网页：拖出为快捷方式
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            data.SetWebLink(parsed);
        }
    }

    private static bool ExistsOnDisk(string path) => File.Exists(path) || Directory.Exists(path);

    private static async Task FillStorageItemAsync(DataPackage data, string path, DragOperationDeferral deferral)
    {
        try
        {
            IStorageItem item = Directory.Exists(path)
                ? await StorageFolder.GetFolderFromPathAsync(path)
                : await StorageFile.GetFileFromPathAsync(path);
            data.SetStorageItems(new IStorageItem[] { item });
        }
        catch (Exception ex)
        {
            StarLog.Error($"拖出准备存储项失败 (path={path})", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
