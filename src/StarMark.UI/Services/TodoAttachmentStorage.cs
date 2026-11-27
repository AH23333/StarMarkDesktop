#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using StarMark.Abstractions;
using StarMark.Core.Widgets;

namespace StarMark.UI.Services;

/// <summary>
/// 待办附件的<b>磁盘侧</b>（P-305）：托管目录布局、往里复制、把托管副本送进回收站。
/// 判定（要不要复制、是不是托管副本）归 <see cref="TodoAttachmentPolicy"/> 一颗，这里只做 I/O。
/// <para>
/// 托管目录布局：<c>%LOCALAPPDATA%\StarMark\attachments\todo\&lt;行 id&gt;\</c>——每条待办一行目录，
/// 行 id 是 items 表的真实行 id（<see cref="TodoAttachmentStorage"/> 不关心它怎么来）。
/// 与 Everything 下载目录（EverythingQueryQueue 的 downloads）同一颗 LOCALAPPDATA 根的拼法。
/// </para>
/// <para>
/// 回收走 <c>SHFileOperationW</c>（FO_DELETE＋FOF_ALLOWUNDO）——.NET 托管侧没有"进回收站"的内置出口
/// （<c>File.Delete</c> 是直接抹掉），而附件是用户刚拖进来的真实文件，删错了应该能捞回来。
/// 护栏在 <see cref="RecycleManagedCopy"/> 自己身上：路径不在这条待办自己的托管目录里就整个不动，
/// 调用方多调一次也不会碰 Linked 的原件。
/// </para>
/// </summary>
public static class TodoAttachmentStorage
{
    /// <summary>待办附件托管的根目录（attachments\todo 一层在 <see cref="ManagedDirFor"/> 里拼）。</summary>
    private static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppName, "attachments", "todo");

    /// <summary>某条待办自己的托管目录。行 id 唯一，目录名不与别的行相撞。</summary>
    public static string ManagedDirFor(long itemId) => Path.Combine(RootDir, itemId.ToString());

    /// <summary>
    /// 用户开了「拖入文件复制为托管副本」吗（P-305 的 Copy 模式）？
    /// 走 DI 里那个 <see cref="StarMark.UI.Helpers.SettingsStore"/> 单例（读缓存是实例级的，
    /// 每次拖放都 new 冷实例等于每次整档反序列化 settings.json）；取不到或读失败＝false＝Linked 默认。
    /// </summary>
    public static bool CopyWanted()
    {
        try
        {
            var store = App.Services is { } sp
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetService<StarMark.UI.Helpers.SettingsStore>(sp)
                : null;
            return store?.LoadTodoAttachmentCopyEnabled() ?? false;
        }
        catch (Exception ex)
        {
            StarLog.Error("[待办] 读附件复制开关失败，按 Linked 处理", ex);
            return false;
        }
    }

    /// <summary>把源文件复制进这条待办的托管目录，返回副本的完整路径。同名冲突加「 (2)」式后缀，不覆盖。</summary>
    public static string CopyIn(long itemId, string sourcePath)
    {
        var dir = ManagedDirFor(itemId);
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(dir, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var n = 2;
        while (File.Exists(target))
            target = Path.Combine(dir, $"{stem} ({n++}){ext}");
        File.Copy(sourcePath, target, overwrite: false);
        return target;
    }

    /// <summary>
    /// 把一份托管副本送进回收站；顺带把这条待办的托管目录若是空目录就一并收走。
    /// <para>护栏自带：<paramref name="path"/> 不在 <paramref name="itemId"/> 自己的托管目录里＝直接返回
    /// （Linked 的用户原件、别的行的托管副本都动不着——调用方就算把两份路径拿反了送进来，这里也不会误删）。
    /// 回收失败只记 Warn：附件记录那边已经移除，停在"盘上多一份没人引用的副本"好过停在"界面显示移除失败"。</para>
    /// </summary>
    public static void RecycleManagedCopy(string path, long itemId)
    {
        var dir = ManagedDirFor(itemId);
        if (!TodoAttachmentPolicy.IsUnder(path, dir)) return;
        if (!File.Exists(path)) return;
        var rc = TryRecycle(path);
        if (rc != 0)
            StarLog.Warn($"[待办] 托管副本送回收站失败（rc={rc}）：{path}——记录已移除，这份副本成了盘上的孤儿，不阻塞界面");
        if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
        {
            try { Directory.Delete(dir); }
            catch (Exception ex) { StarLog.Error($"[待办] 收走空托管目录失败：{dir}", ex); }
        }
    }

    // ── SHFileOperationW：托管副本进回收站的唯一出口 ──
    // .NET 托管侧没有"进回收站"的内置 API（File.Delete 是直接抹掉）；
    // SHFileOperationW + FOF_ALLOWUNDO 是 Windows 桌面的标准途径（同一颗调用两斜杠路径照收）。

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOERRORUI = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    /// <summary>
    /// 送回收站；返回 0＝成功。pFrom 必须以<b>双</b> NUL 收尾（ CharSet 编组自带一个，
    /// 这里再补一个），这是 SHFileOperationW 的契约——少一个就有机会把后面的栈内存当地址表读。
    /// </summary>
    private static int TryRecycle(string path)
    {
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            };
            return SHFileOperationW(ref op);
        }
        catch (Exception ex)
        {
            StarLog.Error($"[待办] SHFileOperationW 调用失败：{path}", ex);
            return -1;
        }
    }
}
