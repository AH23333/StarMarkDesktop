#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.SystemTray;

namespace StarMark.Integrations.Clipboard;

/// <summary>
/// 从系统剪贴板读一帧内容（<see cref="ClipboardWatcher"/> 的 Win32 细节都收在这里）。
/// <para>
/// 刻意用裸 Win32（<c>OpenClipboard</c> + <c>GetClipboardData</c>）而不是 WinUI 的
/// <c>Clipboard.GetContent()</c>：后者要求 STA 且要在窗口生命周期内 await，在"提权会话 + 后台线程池"
/// 这条组合下比 Win32 脆得多（本仓库在文件选择器上已经付过一次学费）。文本与文件列表都是
/// "拷字节 + 交给 <see cref="ClipboardPayload"/> 解码"，没有别的逻辑可出错的地方。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ClipboardNative
{
    /// <summary>OpenClipboard 会被别的应用短暂占住：重试几次，别把"这次没拿到"当成"剪贴板是空的"。</summary>
    private const int OpenTries = 4;
    private const int OpenRetryMs = 40;

    /// <summary>
    /// 读当前剪贴板。返回的 <paramref name="raw"/> 为 null 表示这一帧没有可用内容（无文本、无文件，或被占用）。
    /// 同时带回复制发生时前台应用的进程名（取不到则 null）——策略层据此排除密码管理器。
    /// </summary>
    public static (string? Raw, string Format, string? App) ReadSnapshot()
    {
        // 前台窗口要在打开剪贴板之前取：之后可能已经被别的窗口抢走焦点。
        var app = ForegroundProcessName();

        for (var attempt = 0; attempt < OpenTries; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    if (ReadText() is { Length: > 0 } text) return (text, ClipboardEntry.FormatText, app);
                    if (ReadFiles() is { Count: > 0 } files)
                        return (string.Join("\n", files), ClipboardEntry.FormatFiles, app);
                    return (null, ClipboardEntry.FormatText, app);   // 空剪贴板/只有图片等：不重试，等下一次通知
                }
                finally { NativeMethods.CloseClipboard(); }
            }
            Thread.Sleep(OpenRetryMs);   // 调用方已在线程池上，这里让出真实时间而非占着 CPU 空转
        }

        StarLog.WarnThrottled("clip:busy", $"剪贴板被其他应用占用，本次采集跳过（重试 {OpenTries} 次）", windowMs: 60_000);
        return (null, ClipboardEntry.FormatText, app);
    }

    private static string? ReadText()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT)) return null;
        var payload = ReadGlobal(NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT));
        return payload is null ? null : ClipboardPayload.DecodeText(payload, ansi: false);
    }

    private static System.Collections.Generic.List<string> ReadFiles()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP)) return new();
        var payload = ReadGlobal(NativeMethods.GetClipboardData(NativeMethods.CF_HDROP));
        return payload is null ? new() : ClipboardPayload.ParseDropFiles(payload);
    }

    /// <summary>把 HGLOBAL 复制成托管字节数组。锁窗口期极短，且不假设句柄归我们所有（不 GlobalFree）。</summary>
    private static byte[]? ReadGlobal(IntPtr hMem)
    {
        if (hMem == IntPtr.Zero) return null;
        var size = (long)NativeMethods.GlobalSize(hMem);
        if (size <= 0 || size > 64L * 1024 * 1024) return null;   // 64 MB 以上的"剪贴板文本"不是我们要记的东西

        var ptr = NativeMethods.GlobalLock(hMem);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var buffer = new byte[size];
            Marshal.Copy(ptr, buffer, 0, (int)size);
            return buffer;
        }
        finally { NativeMethods.GlobalUnlock(hMem); }
    }

    private static string? ForegroundProcessName()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;   // 进程刚退出/无权限读名字：按"未知来源"处理，不因此丢整条历史
        }
    }
}
