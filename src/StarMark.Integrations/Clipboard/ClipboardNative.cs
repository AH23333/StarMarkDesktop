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

    /// <summary>HGLOBAL 拷贝的绝对上界（F8 的 64MB 防护）。文本按这一条，图片另按 Policy 的 20MB 收窄。</summary>
    private const long CopyLimitBytes = 64L * 1024 * 1024;

    /// <summary>"PNG" 这个私有格式在本机的 id（没应用注册过就是 0，之后一直按 0 处理）。静态初始化只跑一次，CLR 保证线程安全。</summary>
    private static readonly uint PngFormatId = NativeMethods.RegisterClipboardFormatW("PNG");

    /// <summary>
    /// 读当前剪贴板。返回的 <paramref name="raw"/> 为 null 表示这一帧没有<b>文本/文件</b>可用内容
    /// （纯图片请走 <see cref="ReadImage"/>——§2 的格式优先级与 20MB 闸都在那边）。
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

    /// <summary>
    /// 一帧图片读数。<see cref="Dib"/> 与 <see cref="Png"/> <b>有且只有一个非空</b>：
    /// DIB 路线解到底交 BGRA（往下要编码），PNG 路线原样交字节（§2"校验魔数后按字节存"，不重编码）。
    /// </summary>
    public readonly record struct ImageRead(ClipboardPayload.ImageFrame? Dib, byte[]? Png,
                                            int Width, int Height, string Container);

    /// <summary>
    /// 按 <b>CF_DIBV5 → CF_DIB → CF_PNG</b> 的优先级读一帧图片（§2 裁决）。三种都没有 ⇒ <c>Frame</c> 为 null
    /// 且不给原因（"这一帧没有图片负载"不是故障）。
    /// <para><b>CF_BITMAP 刻意不试</b>（v1 明确放弃并写在这儿）：它是 HBITMAP，不走 GlobalAlloc，
    /// 读它要 SelectObject 进 DC 再 GetDIBits 一遍——为"少数应用才给的兼容格式"多开一条取像素的路，
    /// 就多一处只能真机验的错。</para>
    /// <para>某个格式<b>超限/解不动</b>时把原因记下来并继续试下一个：同一帧常常同时挂着 DIB 与 PNG，
    /// DIBV5 的头坏掉不代表那份 PNG 也坏；而"超 20MB"这类拒收必须带得出原因（沿用 F10 的"超限出声"）。</para>
    /// </summary>
    public static (ImageRead? Frame, string? Reason) ReadImage()
    {
        string? reason = null;
        for (var attempt = 0; attempt < OpenTries; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    foreach (var (id, container) in new[]
                             { (NativeMethods.CF_DIBV5, ClipboardPayload.FormatDibV5),
                               (NativeMethods.CF_DIB, ClipboardPayload.FormatDib),
                               (PngFormatId, ClipboardPayload.FormatPng) })
                    {
                        if (id == 0 || !NativeMethods.IsClipboardFormatAvailable(id)) continue;
                        var bytes = ReadGlobal(NativeMethods.GetClipboardData(id),
                            ClipboardPolicy.MaxImageBytes, out var overLimit);
                        if (bytes is null or { Length: 0 })
                        {
                            if (overLimit) reason ??= $"{container} 超过 {ClipAssets.DescribeBytes(ClipboardPolicy.MaxImageBytes)} 上限，未采集";
                            continue;
                        }

                        if (container == ClipboardPayload.FormatPng)
                        {
                            if (!ClipboardPayload.IsPng(bytes)) { reason ??= "CF_PNG 的魔数不对（不按 PNG 存）"; continue; }
                            if (!ClipboardPayload.TryReadPngSize(bytes, out var w, out var h))
                            { reason ??= "PNG 头读不出尺寸"; continue; }
                            return (new ImageRead(null, bytes, w, h, container), null);
                        }

                        if (!ClipboardPayload.TryDecodeDib(bytes, out var image, out var why))
                        {
                            reason ??= why;
                            continue;                                       // 换下一个格式再试一次
                        }
                        return (new ImageRead(image, null, image.Width, image.Height, container), null);
                    }
                    return (null, reason);
                }
                finally { NativeMethods.CloseClipboard(); }
            }
            Thread.Sleep(OpenRetryMs);
        }
        StarLog.WarnThrottled("clip:busy", $"剪贴板被其他应用占用，本次图片采集跳过（重试 {OpenTries} 次）", windowMs: 60_000);
        return (null, reason);
    }

    private static string? ReadText()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT)) return null;
        var payload = ReadGlobal(NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT), CopyLimitBytes, out _);
        return payload is null ? null : ClipboardPayload.DecodeText(payload, ansi: false);
    }

    private static System.Collections.Generic.List<string> ReadFiles()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP)) return new();
        var payload = ReadGlobal(NativeMethods.GetClipboardData(NativeMethods.CF_HDROP), CopyLimitBytes, out _);
        return payload is null ? new() : ClipboardPayload.ParseDropFiles(payload, AnsiText.DecodeSystemAnsi);
    }

    /// <summary>
    /// 把 HGLOBAL 复制成托管字节数组。锁窗口期极短，且不假设句柄归我们所有（不 GlobalFree）。
    /// <para><b>超限要单独报出来</b>（<paramref name="overLimit"/>）：一个 null 同时表示"句柄拿不到"与
    /// "这帧太大不要了"，采集侧就没法照 §3-Q1① 的要求给用户一句真话。</para>
    /// </summary>
    private static byte[]? ReadGlobal(IntPtr hMem, long limitBytes, out bool overLimit)
    {
        overLimit = false;
        if (hMem == IntPtr.Zero) return null;
        var size = (long)NativeMethods.GlobalSize(hMem);
        if (size <= 0) return null;
        if (size > limitBytes) { overLimit = true; return null; }   // 拷都不拷：上限本来就是"别把它搬进内存"

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
