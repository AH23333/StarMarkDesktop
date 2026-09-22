#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StarMark.UI.Helpers;

/// <summary>
/// 进程完整性/提权相关小工具。给「本地磁盘搜索」用：当用户的 Everything 以管理员运行（High IL）时，
/// 普通 IL 的 StarMark 用 WM_COPYDATA 连不上它（UIPI），必须**同权限**才能对话。方案：让用户开启本地磁盘搜索后
/// StarMark 自动以管理员身份重启一次，与新 Everything 客户端保持同 IL。见 App.xaml.cs / SettingsPageViewModel。
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
    /// 以 <c>runas</c>（触发 UAC）重启当前 exe，附带 <paramref name="extraArgs"/> 以便新实例防死循环。
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
