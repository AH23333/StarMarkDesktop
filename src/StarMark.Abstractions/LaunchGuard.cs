#nullable enable
using System;

namespace StarMark.Abstractions;

/// <summary>
/// 打开条目 URI 前的协议（scheme）白名单闸门。集中一处，供主程序与任何"交给 Shell 打开"的路径复用。
/// </summary>
public static class LaunchGuard
{
    /// <summary>
    /// 仅放行 StarMark 真实承载的三类协议：<c>http</c>/<c>https</c>（GitHub Star、浏览器书签）与
    /// <c>file</c>（本地文件）。其余（<c>javascript:</c> / <c>data:</c> / <c>vbscript:</c> /
    /// <c>ms-msdt:</c> / 任意自定义协议 / 非法或相对 URI）一律拒绝。
    /// <para>
    /// 目的：条目 URI 是可被导入/同步/外部数据影响的字符串，若原样交给 <c>Launcher.LaunchUriAsync</c>
    /// （最终走 ShellExecute），会让恶意或被污染的记录触发系统协议处理器。scheme 白名单是最小、无副作用的防线。
    /// 注：<c>file</c> 仅放行 scheme 本身；其"本地路径 vs 远程 UNC（<c>file://host/share</c> 可致 SMB
    /// NTLM 泄漏）"的进一步收敛涉及 <see cref="Uri.LocalPath"/> 对两/三斜杠的历史歧义，须真机回归，另列待决策。
    /// </para>
    /// </summary>
    public static bool IsAllowedScheme(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        return parsed.Scheme == Uri.UriSchemeHttp
            || parsed.Scheme == Uri.UriSchemeHttps
            || parsed.Scheme == Uri.UriSchemeFile;
    }

    /// <summary>
    /// 「打开所在位置」把本地路径拼成 <c>explorer.exe /select,"&lt;path&gt;"</c> 命令行；子进程会用
    /// <c>CommandLineToArgvW</c> 重新切分参数。路径若含双引号即可越出 <c>"..."</c> 边界、注入额外的
    /// <c>explorer.exe</c> 参数（被污染的备份 / 快照条目 <c>uri</c> 可携带此串）→ 任意本地程序被执行，
    /// 且该启动点不经 <see cref="IsAllowedScheme"/>。真实 NTFS 文件名恒不含 <c>"</c>，故拒绝含引号者
    /// 对合法「打开所在位置」零影响。空 / 空白路径同样拒绝。
    /// </summary>
    public static bool IsSafeShellSelectTarget(string? path)
        => !string.IsNullOrWhiteSpace(path) && path!.IndexOf('"') < 0;
}
