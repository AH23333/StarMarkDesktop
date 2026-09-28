#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StarMark.UI.Helpers;

/// <summary>
/// 进程完整性/提权相关小工具。<b>本程序不再为了连上 Everything 而自我提权</b>（P-108 改判，2026-09-29 实测：
/// 我们自己拉起的 Everything 跑在普通 IL，同权限的 WM_COPYDATA 本来就不被 UIPI 拦，提权对"能不能搜到"零增益，
/// 代价却是资源管理器拖进／拖出双向失灵＋系统文件对话框调不起来＋每次启动弹 UAC）。
/// <para>现在只剩两个用途：<see cref="IsElevated"/> 给会话日志与"提权会话下选择器不可用"这类说实话的文案用；
/// <see cref="TryRelaunchSelfElevated"/> 只服务一处——收掉<b>权限比我们高</b>的上次残留进程（普通权限 end 不掉它，
/// 而它锁着数据目录），见 <c>App.xaml.cs</c> 的 <c>--resolve-ghost</c>。方向区别要说清：那是"对面先提了权"，
/// 不是"我们主动把权限抬上去"。</para>
/// </summary>
public static class Privilege
{
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20; // TOKEN_INFORMATION_CLASS.TokenElevation

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processToken, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int infoClass, out uint info, uint infoLength, out uint returned);

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);

    /// <summary>当前进程是否以管理员（提权）权限运行。用 OpenProcessToken + TokenElevation，避免引 <c>System.Security.Principal.Windows</c> 依赖。</summary>
    public static bool IsElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var tok)) return false;
        try
        {
            return GetTokenInformation(tok, TokenElevation, out var elev, sizeof(uint), out _) && elev == 1;
        }
        finally { CloseHandle(tok); }
    }

    /// <summary>
    /// 以 <c>runas</c>（触发 UAC）重启当前 exe，<paramref name="extraArgs"/> 用来把"新实例该办的那一件、也只办那一件"
    /// 交代清楚（现存唯一调用者交的是 <c>--resolve-ghost &lt;pid&gt;</c>＝收掉指定残留，因此不会再重复弹框）。
    /// 用户点"否" → <see cref="System.ComponentModel.Win32Exception"/>（error 1223）→ 返回 false，调用方继续普通运行。
    /// </summary>
    public static bool TryRelaunchSelfElevated(string? extraArgs = null)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = extraArgs ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("以管理员重启 StarMark 失败", ex);
            return false;
        }
    }
}
