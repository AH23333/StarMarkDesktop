using Microsoft.Win32;
using StarMark.Abstractions;

namespace StarMark.UI.Services;

/// <summary>
/// 开机自启（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）：
/// 用户级注册表，无需管理员权限；值指向当前进程 exe（发布/调试产物均可）。
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StarMark";

    /// <summary>注册表 Run 键里是否已有 StarMark 条目。</summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string path && !string.IsNullOrWhiteSpace(path);
    }

    /// <summary>启用：写入当前进程 exe 路径（带引号，防路径空格）。</summary>
    public void Enable()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前可执行文件路径");
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\"");
        StarLog.Info($"开机自启已启用: {exe}");
    }

    /// <summary>停用：删除 Run 键条目（不存在时静默）。</summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        StarLog.Info("开机自启已停用");
    }

    /// <summary>按开关设置（幂等：仅在状态变化时写注册表）。</summary>
    public void SetEnabled(bool enabled)
    {
        var current = IsEnabled();
        if (enabled == current) return;
        if (enabled) Enable();
        else Disable();
    }
}
