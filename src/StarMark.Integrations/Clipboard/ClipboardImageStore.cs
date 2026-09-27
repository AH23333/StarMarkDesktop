#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Capture;

namespace StarMark.Integrations.Clipboard;

/// <summary>
/// 剪贴板图片的<b>文件侧</b>：写（临时名 + 同卷改名）、PNG/JPEG 编解码、尽力删、占用枚举。
/// <para>
/// 与 <see cref="ClipAssets"/> 的分工是刻意的：命名字典与分派规则是纯函数，"算错就能逐值断言"；
/// 而这里每一条都可能只因为磁盘、杀毒软件或用户手痒而失败，<b>失败必须带得出原因</b>且绝不让
/// 采集链抛出去（<c>ClipboardWatcher</c> 的读循环兜着）。两边各写一半，才是"看得见的那半坏不了"。
/// </para>
/// <para>
/// PNG 编码只调它，不改它——截图域在禁区里。
/// </para>
/// </summary>
/// <remarks>刻意不加 <c>[SupportedOSPlatform("windows")]</c>：那等于宣告"所有 Windows 版本都能用"，
/// 而 WinRT 成像只从 10240 起（CA1416 会为此刷一片警告）。工程级 <c>SupportedOSPlatformVersion</c>
/// 已经把下界钉在 19041，与 <c>GdiScreenCapture</c> 同一做法。</remarks>
/// <remarks><b>类是 public 的</b>：设置页要读 <see cref="ListFiles"/> 算占用。
/// "目录里到底有多少字节"这件事只有这一份枚举口径，给 UI 另开一份 <c>Directory.EnumerateFiles</c>
/// 就会分出两种"占用"数字。</remarks>
public static class ClipboardImageStore
{
    /// <summary>
    /// 图片目录。<b>只是 <see cref="ClipAssets.Folder"/> 的别名</b>——目录、改道、名字校验都住在 Abstractions，
    /// 因为"删文件的那一侧"（仓储层的轮转/清空/删单条）也要读同一个值；两边各算一份路径，
    /// 最早坏掉的就是"尽力删文件"这条（删不到、又不出声）。
    /// </summary>
    public static string Folder => ClipAssets.Folder;

    /// <summary>主图已经在了吗（同图再复制时靠这一句决定"只记一次回放，不再写文件"）。</summary>
    public static bool MainExists(string mainName) => SafeInFolder(mainName) is { } p && File.Exists(p);

    /// <summary>
    /// 把"从库里读出来的文件名"拼成绝对路径，<b>不合法就返回 null</b>。
    /// <para>这一步不能省：<c>clipFile</c> 是 <c>extra_json</c> 里的文本，而备份文件用户可以手改。
    /// 不校验就 <c>Path.Combine</c>，"..\\" 能把删除与写入都带到 clip 目录之外去。
    /// 判据本体在 <see cref="ClipAssets.FullPathOf"/>，删除侧共用同一句。</para>
    /// </summary>
    private static string? SafeInFolder(string? name) => ClipAssets.FullPathOf(name);

    /// <summary>
    /// 写一对文件（主图 + 缩略图）。<b>缩略图编码失败时只写主图并返回成功</b>：那一档失败是 WinRT
    /// 编码器的事，不该让用户刚复制的那张图因此从历史里消失；行里的 <c>clipThumb</c> 名字允许暂时
    /// 没有对应文件，列表读到缩略图拿不到就回退解码主图（2a 负责那条回退）。
    /// </summary>
    public static async Task<(bool Ok, string? Error)> WritePairAsync(string mainName, string thumbName,
        byte[] png, byte[]? jpeg)
    {
        if (SafeInFolder(mainName) is null || SafeInFolder(thumbName) is null)
            return (false, "文件名不安全，未写盘");
        try
        {
            Directory.CreateDirectory(Folder);
            await WriteAtomicAsync(mainName, png).ConfigureAwait(false);
            if (jpeg is not null)
            {
                try
                {
                    await WriteAtomicAsync(thumbName, jpeg).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    TryDelete(thumbName);              // 半张缩略图不如没有：改名没成功就别留下残留
                }
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>先写 <c>*.tmp</c> 再同卷改名：进程在编码中途被杀，留下的只能是临时件，对账按一类清掉。</summary>
    private static async Task WriteAtomicAsync(string name, byte[] bytes)
    {
        var final = Path.Combine(Folder, name);
        var temp = Path.Combine(Folder, ClipAssets.TempNameOf(name));
        await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
        File.Move(temp, final, overwrite: true);
    }

    /// <summary>尽力删（删除路径与轮转用）。<b>失败不抛</b>——文件删不掉不该让"删掉这一行"跟着失败。</summary>
    public static bool TryDelete(string? name)
    {
        if (SafeInFolder(name) is not { } path) return false;
        try
        {
            if (!File.Exists(path)) return true;                 // 已经不在了＝目标达成，不算失败
            File.Delete(path);
            return true;
        }
        catch (Exception) { return false; }                      // 调用方负责把失败落进日志
    }

    /// <summary>目录里的文件名与字节数（占用统计与对账都读它；目录不存在就是空，不抛）。</summary>
    public static IReadOnlyList<(string Name, long Bytes)> ListFiles()
    {
        var list = new List<(string, long)>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                long len = 0;
                try { len = new FileInfo(file).Length; } catch (Exception) { /* 单文件读不到不算目录读不到 */ }
                list.Add((Path.GetFileName(file), len));
            }
        }
        catch (Exception) { /* 权限/网络盘：占用显示成 0 比整页炸掉好 */ }
        return list;
    }

    // ==================== 编解码 ====================

    /// <summary>BGRA → PNG。<b>只走全仓唯一那处编码器</b>（<c>Capture/GdiScreenCapture</c>），取回字节。</summary>
    public static async Task<byte[]?> EncodePngAsync(byte[] bgra, int width, int height, CancellationToken ct)
    {
        var stream = await GdiScreenCapture.EncodePngAsync(bgra, width, height, ct).ConfigureAwait(false);
        return stream is null ? null : await ToBytesAsync(stream).ConfigureAwait(false);
    }

    /// <summary>
    /// 缩略图：长边缩到 <see cref="ClipAssets.ThumbnailMaxEdge"/> 的小 JPEG（§5 Q5）。
    /// <para>缩放交给 <c>BitmapTransform</c>——WinRT 这条路能在非 UI 线程跑，正是裁决点名的做法。</para>
    /// <para>
    /// <b>但 q60 这一档没拧上去</b>：<c>BitmapEncoder</c> 的类型上没有 <c>ImageProperties</c>，
    /// 运行时自定义属性通道（<c>ICustomPropertyProvider</c>）也不在这份 C# 投影里——两条路都编译不过。
    /// 现在吃的是 <b>JPEG 编码器的默认质量</b>。刻意不为一个数字去反射，也不改回 PNG
    /// （160px 的 PNG 常 15–30KB，而裁决要的是"列表每行都加载它也得便宜"）。
    /// 缩略图实际字节数已列入 §6 真机清单一起量，<see cref="ClipAssets.ThumbnailQuality"/> 保留着目标值。
    /// </para>
    /// </summary>
    public static async Task<byte[]?> EncodeThumbnailAsync(byte[] bgra, int width, int height, CancellationToken ct)
    {
        if (width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4) return null;
        var edge = ClipAssets.ThumbnailEdge(width, height);
        var scale = Math.Min(1.0, (double)ClipAssets.ThumbnailMaxEdge / edge);
        var sw = Math.Max(1, (int)Math.Round(width * scale));
        var sh = Math.Max(1, (int)Math.Round(height * scale));

        using var stream = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream).AsTask(ct);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                (uint)width, (uint)height, 96, 96, bgra);
            encoder.BitmapTransform.ScaledWidth = (uint)sw;
            encoder.BitmapTransform.ScaledHeight = (uint)sh;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            await encoder.FlushAsync().AsTask(ct);
            return await ToBytesAsync(stream).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;        // 调用方把"没缩略图"如实显示出来（列表宁可解码主图，也不要装作有）
        }
    }

    /// <summary>
    /// 条目 → 那张 PNG 的<b>内存字节</b> ＋ 归一后的像素。<b>全仓"为解码而读盘"与"解 PNG"各只有这一处</b>：
    /// 两处各一份，最坏的就是采集与读档对同一帧算出两个哈希 ⇒ 用户每点一次"再复制"，历史多一条自回声。
    /// <para>
    /// 为什么交字节而不只交像素：写回剪贴板要交出去的是<b>数据本身</b>。真机坏法是把
    /// <c>file://</c> URI 交给 <c>SetBitmap</c>——系统按"复制了一个文件"呈现，目标程序粘出来是一串路径，
    /// 而采集侧又把那行路径记成一条新历史（每点一次多一条，且都是路径）。
    /// </para>
    /// <para>
    /// 解码走 <see cref="ClipboardPayload.TryDecodePng"/>（§2「解码」裁决＝纯托管自研）。这里以前另有一份
    /// WinRT 解码，对照测试实测出两处底细：16 位的 PNG 它交不出"每像素 3 或 4 字节"的像素块 ⇒ 整帧被拒
    /// （那样的图根本进不了历史）；带 alpha 的帧它交出的像素与"alpha 一律 255"的归一口径不同。
    /// </para>
    /// </summary>
    public static bool TryReadEntryImage(Item item, out byte[]? pngBytes,
        out ClipboardPayload.ImageFrame frame, out string? reason)
    {
        pngBytes = null;
        frame = default;
        reason = null;
        var path = ClipAssets.FullPathOf(ClipboardEntry.FileName(item));
        if (path is null) { reason = "这一条的文件名不安全或没有文件"; return false; }
        try
        {
            pngBytes = File.ReadAllBytes(path);
            if (ClipboardPayload.TryDecodePng(pngBytes, out frame, out reason)) return true;
            pngBytes = null;      // 解不开的帧不许半交出去：调用方要么两样都拿到，要么一样都没有
            return false;
        }
        catch (Exception ex)
        {
            pngBytes = null;
            reason = $"文件读不出来（{ex.GetType().Name}）";
            return false;
        }
    }

    /// <summary>条目 → 它那张 PNG → 归一 BGRA。<b>P3 贴图要的就是这一份</b>：§Q4 终态把这条接口
    /// 冻结在这里，实装接合等标注域 S3 之后（本批一个贴图调用都不接）。</summary>
    public static bool TryReadEntryFrame(Item item, out ClipboardPayload.ImageFrame frame, out string? reason)
        => TryReadEntryImage(item, out _, out frame, out reason);

    /// <summary>字节 → WinRT 内存流。剪贴板只收流引用，不收 <c>byte[]</c>——这一句就是"数据不是路径"的那一步。</summary>
    public static Windows.Storage.Streams.InMemoryRandomAccessStream StreamOf(byte[] data)
    {
        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        // 必须 Flush：AsStreamForWrite 是一层带缓冲的包装，只 Write 不 Flush 时字节还没落进 WinRT 流，
        // 交出去的 Size＝0 ——症状是"复制成功、粘出来什么都没有"，比抛异常更难查（单测一把就红）。
        var writer = stream.AsStreamForWrite();
        writer.Write(data);
        writer.Flush();
        stream.Seek(0);
        return stream;
    }

    private static async Task<byte[]> ToBytesAsync(InMemoryRandomAccessStream stream)
    {
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
