#nullable enable
using System;
using System.IO;

namespace StarMark.Core.Widgets;

/// <summary>
/// 一次「往待办行里拖文件」的结局（P-305）。四个数各答一问，回执与日志各取所需：
/// 拖进来几项（<see cref="Offered"/>）、真挂上几项（<see cref="Added"/>，Linked 与 Copy 都算）、
/// 几项因为路径已在清单里被跳过（<see cref="Duplicates"/>）、几项打开/复制失败（<see cref="Failed"/>）。
/// 与快捷启动的 <c>LauncherAddOutcome</c> 同一形状的第三份：结局说清楚，"拖了没反应"才不会伪装成"已经加好了"。
/// </summary>
public sealed record TodoAttachmentOutcome(int Offered, int Added, int Duplicates, int Failed);

/// <summary>
/// 待办附件的<b>纯策略</b>（P-305）：只做字符串与路径的判定，不碰磁盘、不读设置——
/// 磁盘动作归 UI 侧的 TodoAttachmentStorage，读设置归宿主注入。
/// <para>
/// 两条判定是删除护栏与「链接会不会变空头支票」的全部依据：
/// ① <see cref="NeedsForcedCopy"/>——临时目录里的源文件随时会被系统清掉，挂链接等于承诺一个明天就不在的地址，
///   所以 %TEMP% 里的东西<b>无论用户选没选「复制」都强制复制</b>成托管副本；
/// ② <see cref="IsUnder"/>——「这条路径是不是躺在那本目录里」唯一判据，删除时只回收满足它的那份：
///   跨条目的托管副本（别的行的目录）与 Linked 的用户原件一概不动。
/// </para>
/// </summary>
public static class TodoAttachmentPolicy
{
    /// <summary>
    /// 这个源文件必须复制成托管副本吗？临时目录里的文件必须——源随时会被系统清掉，
    /// 链接等于空头支票。<paramref name="tempRoot"/> 由调用方注入（<c>Path.GetTempPath()</c>），
    /// 判据不依赖本机状态活着。
    /// </summary>
    public static bool NeedsForcedCopy(string sourcePath, string tempRoot)
        => IsUnder(sourcePath, tempRoot);

    /// <summary>
    /// 路径是否落在 <paramref name="dirRoot"/> 目录<b>里面</b>（含恰好等于根本身）。
    /// 大小写不敏感（Windows 文件系统语义）；带分隔符比对，<c>C:\dirX</c> 不会冒充 <c>C:\dir</c> 的孩子。
    /// </summary>
    public static bool IsUnder(string path, string dirRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(dirRoot)) return false;
        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;   // 认不出形状的路径进不了"在目录里"这一格——删除护栏宁可少回收，不多碰
        }
        string root;
        try
        {
            root = Path.GetFullPath(dirRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
