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
    /// 注：<c>file</c> 仅放行 scheme 本身；"本地路径 vs 远程 UNC（<c>file://host/share</c> 可致 SMB
    /// NTLM 泄漏）"的收敛已按决策 L4 落进 <see cref="IsLocalOnlyPath"/>
    /// （正本 <c>docs/决策-L4-UNC凭据泄漏.md</c>，批次 片196 销账）。
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

    /// <summary>
    /// 「只在本地命名空间」判定（决策 L4：正本 <c>docs/决策-L4-UNC凭据泄漏.md</c>）。
    /// 条目 URI 可被导入/备份/同步污染：指向 UNC（<c>\\server\share</c>、<c>//server/share</c>、
    /// <c>file://host/…</c>、<c>\\?\UNC\…</c>）时，Windows 在<b>解析路径的那一刻</b>就主动发起 SMB
    /// 协商并向对方主机出示 NTLM 凭据——不需要执行任何东西，「打开所在位置」点一下凭据哈希就出网。
    /// <para>
    /// 口径＝<b>默认拒绝、显式路径放行</b>（误拒代价＝用户改用映射盘一步操作；误放代价＝凭据泄漏不可撤销）：
    /// 放行盘符路径（含用户显式映射的网络盘 <c>Z:\</c>——凭据走既有会话，攻击者也需先知道映射关系）、
    /// <c>\\?\C:\</c> 本地长路径（<c>\\?\</c> 仅后接盘符时放）、host 为空或 localhost 的 <c>file:///C:/</c>；
    /// 裸 UNC、正斜杠 UNC、<c>file://host</c>（host 非空且非 localhost）、<c>\\.\</c> 设备命名空间、
    /// <c>\\?\UNC\</c> 及其余认不出的形态一律拒。
    /// </para>
    /// </summary>
    public static bool IsLocalOnlyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path!.Trim();
        // URI 形态（纵深防御，供宿主把原始 uri 直接递进来时也兜得住）：file://host 的 host
        // 非空且非 localhost ⇒ SMB 面，拒；解析不出 ⇒ 拒。真实链路里宿主递进来的是还原后的路径。
        if (p.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(p, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeFile) return false;
            return parsed.Host.Length == 0
                || string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }
        var norm = p.Replace('/', '\\');
        if (norm.StartsWith("\\\\?\\", StringComparison.Ordinal))
            // \\?\ 只在后接盘符时放（\\?\C:\ 本地长路径）；\\?\UNC\server\share 是 UNC 的显式形态，一律拒。
            return norm.Length >= 6 && char.IsLetter(norm[4]) && norm[5] == ':';
        if (norm.StartsWith("\\\\", StringComparison.Ordinal)) return false;   // \\server\share、\\.\PhysicalDrive0
        // 盘符（含映射盘）放行；其余（相对路径等认不出形态）按默认拒绝。
        return p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':';
    }
}
