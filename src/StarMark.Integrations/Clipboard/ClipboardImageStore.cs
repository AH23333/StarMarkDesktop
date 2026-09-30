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

    // "文件不在了"那一句原话住在政策层（ClipboardPolicy.MissingFileClause），这两个入口只引用它。
    // 这里曾经是两个入口共用一个私有常数——方向对，但范围太小：同一句事实还在历史页状态行、
    // 灰掉的菜单标题与卡片那一格各写了一份，四种说法照样长出来（批次 SI 收口）。

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
        // 先 stat 再去读：让 File.ReadAllBytes 抛，交出去的就是"文件读不出来（FileNotFoundException）"
        // 这种系统原话。两句在界面上都像程序坏了，而"这一张已经不在这台机器上"是用户能懂的事实。
        if (!File.Exists(path)) { reason = ClipboardPolicy.MissingFileClause; return false; }
        try
        {
            return TryHandOutPng(File.ReadAllBytes(path), out pngBytes, out frame, out reason);
        }
        catch (Exception ex)
        {
            pngBytes = null;
            reason = $"文件读不出来（{ex.GetType().Name}）";
            return false;
        }
    }

    /// <summary>
    /// 本机一个图片文件 → 交给剪贴板的 <b>PNG 字节 ＋ 归一像素</b>（右键「复制图片」用；历史行走
    /// <see cref="TryReadEntryImage"/>，因为那一侧的"是哪个文件"要过 <see cref="ClipAssets"/> 的名字名册）。
    /// <para><b>PNG 原样透传，不重新编码</b>：重编一次就换掉字节，而"粘出去的字节"与"登记回声的像素"
    /// 必须出自同一张。其余格式（bmp/jpg/gif/tiff）用 WinRT 解码后重编成 PNG——这里没有 System.Drawing，
    /// 也不为这一件事加 NuGet，用的就是 <see cref="EncodeThumbnailAsync"/> 那同一套成像栈。</para>
    /// <para>回声像素一律取<b>这次交出去的那份 PNG</b> 解出来的，不取解码器当场交出的：<b>身份只有一种算法</b>，
    /// 两边各算一次就是"复制成功、历史多一条"（同 <see cref="TryReadEntryImage"/> 的理由）。</para>
    /// <para>这一份不写盘、不进历史，只是把用户已有的文件换成位图负载。失败必须带得出原因。</para>
    /// </summary>
    public static async Task<(byte[]? Png, ClipboardPayload.ImageFrame Frame, string? Reason)>
        TryReadFileAsPngAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, default, "没有给出文件路径");
        try
        {
            if (!File.Exists(path!)) return (null, default, ClipboardPolicy.MissingFileClause);
            var size = new FileInfo(path!).Length;
            if (size > ClipboardPolicy.MaxImageBytes)
                return (null, default,
                    $"这张图 {ClipAssets.DescribeBytes(size)} 超过 {ClipAssets.DescribeBytes(ClipboardPolicy.MaxImageBytes)}，" +
                    "位图太大没有复制（与采集侧同一道上限）");
            var bytes = await File.ReadAllBytesAsync(path!, ct).ConfigureAwait(false);
            if (TryHandOutPng(bytes, out var png, out var frame, out _)) return (png, frame, null);

            // 到这里只剩两种可能：格式本来就不是 PNG（正常，走转码），或它带 PNG 头却解不开（坏文件）。
            // 两种都由转码那一支给出最终说法——解不开的 PNG 重编也解不开，下面的失败原因cover住。
            var transcoded = await EncodePngFromBytesAsync(bytes, ct).ConfigureAwait(false);
            if (transcoded is null)
                return (null, default,
                    "本机把它解不开（系统没有这种格式的解码器，或文件本身坏了），所以位图没能复制出去");
            return TryHandOutPng(transcoded, out png, out frame, out var why)
                ? (png, frame, null)
                : (null, default, $"转出来的 PNG 仍然读不出（{why}）");
        }
        catch (OperationCanceledException) { return (null, default, "复制已经取消"); }
        catch (Exception ex) { return (null, default, $"文件读不出来（{ex.GetType().Name}）"); }
    }

    /// <summary>
    /// 字节 → "能交出去的一份 PNG"（原字节 + 归一像素），<b>全类解 PNG 只有这一处调用点</b>。
    /// <para>先验魔数再解：<see cref="ClipboardPayload.IsPng"/> 与解码一起构成"我们只透传自己认得的容器"，
    /// 只验魔数会把截断文件当 PNG 交出去，只靠解码则让一份改过头的文件以 PNG 的名义进剪贴板。</para>
    /// </summary>
    private static bool TryHandOutPng(byte[] bytes, out byte[]? png,
        out ClipboardPayload.ImageFrame frame, out string? reason)
    {
        png = null;
        frame = default;
        reason = null;
        if (!ClipboardPayload.IsPng(bytes)) { reason = "不是 PNG"; return false; }
        if (!ClipboardPayload.TryDecodePng(bytes, out frame, out reason)) return false;
        png = bytes;
        return true;
    }

    /// <summary>
    /// 任意受支持图片字节 → PNG（<b>只当转码器用</b>：WinRT 在这里交出的像素一律不当身份）。
    /// <para>多帧文件（gif/tiff）取第 0 帧——"复制图片"要的就是用户在那一格看到的那张，
    /// 把动图整帧塞进位图负载既没有约定也没有对应语义。</para>
    /// </summary>
    private static async Task<byte[]?> EncodePngFromBytesAsync(byte[] source, CancellationToken ct)
    {
        try
        {
            using var input = new InMemoryRandomAccessStream();
            var writer = input.AsStreamForWrite();
            writer.Write(source, 0, source.Length);
            writer.Flush();                 // 不 Flush 交出去的是 Size=0 的空流（同 StreamOf 那条）
            input.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(input).AsTask(ct).ConfigureAwait(false);
            using var bitmap = await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore)
                .AsTask(ct).ConfigureAwait(false);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output)
                .AsTask(ct).ConfigureAwait(false);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
            return await ToBytesAsync(output).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;   // 失败形态只有"没有对应编解码器 / 文件坏了 / 编码器拒这个像素格式"，都由调用方说一句原话
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

    /// <summary>
    /// 备份要携带的图片本体（§3-Q2 a 期）：<b>只挑此刻真在磁盘上、且过 <see cref="ClipAssets"/> 名字名册的那些</b>。
    /// <para>刻意只交名字、<b>不读内容</b>：200 张 4K 截图一次全进内存是几百 MB 的瞬时峰值，
    /// 而备份那一刻正是"用户最不想让应用崩在这里"的时候。打包侧逐张流式拷（<see cref="TryOpenForPackage"/>），
    /// 报出去的张数与字节都以"真进了包"为准。</para>
    /// <para>带 <c>clipMissing</c> 标记的行仍然问一次磁盘：标着缺失而文件又被用户放回来的行是合法状态
    /// （§3-Q6 第三类反向），按标记跳过就会漏带它。</para>
    /// </summary>
    public static (List<string> Names, int Missing, int Failed) PlanBackup(IReadOnlyList<Item> items)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int missing = 0, failed = 0;
        foreach (var item in items)
        {
            if (item.Type != ItemType.Clipboard || !ClipboardEntry.IsImageOf(item.ExtraJson)) continue;
            var meta = ClipboardEntry.ImageOf(item);
            if (meta is null) continue;
            foreach (var name in new[] { meta.Value.MainName, meta.Value.ThumbName })
            {
                if (string.IsNullOrEmpty(name) || !seen.Add(name!)) continue;   // 旧行缩略图回落到主图名：别带两遍
                var path = ClipAssets.FullPathOf(name);
                if (path is null) { failed++; continue; }                       // 名字不过名册：不带，也不报它是什么
                try
                {
                    if (!File.Exists(path)) { missing++; continue; }            // 本就没图：备份里也不该出现这个名字
                    names.Add(name!);
                }
                catch (Exception ex)
                {
                    failed++;
                    StarLog.Warn($"备份计划跳过一张图片（{name}）：{ex.GetType().Name} {ex.Message}");
                }
            }
        }
        return (names, missing, failed);
    }

    /// <summary>
    /// 打包前的一次"这张能不能带走"探问：名字不过名册、文件不在、或正被别的程序独占时返回 <c>null</c>，不抛。
    /// <para><b>顺序是这里的全部要点：先开流、后建条目。</b>zip 的 Create 模式里条目一旦建出来就收不回，
    /// "先 CreateEntry 再往里拷"会让一张带不走的图在包里留下一个零字节成员——而下一个人只数得出
    /// "包里有 4 张"，数不出其中一张是空的。</para>
    /// </summary>
    public static Stream? TryOpenForPackage(string name)
    {
        var path = ClipAssets.FullPathOf(name);
        if (path is null) return null;
        try
        {
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"备份带不走一张图片（{name}）：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    /// <summary>这一行条目认领的文件名（恢复侧据此建白名单；名单外的条目名一个字节都不落盘）。</summary>
    public static IEnumerable<string> ClaimedNamesOf(Item item)
    {
        if (item.Type != ItemType.Clipboard || !ClipboardEntry.IsImageOf(item.ExtraJson))
            return Array.Empty<string>();
        var meta = ClipboardEntry.ImageOf(item);
        return meta is null || string.IsNullOrEmpty(meta.Value.MainName)
            ? Array.Empty<string>()
            : new[] { meta.Value.MainName, meta.Value.ThumbName };
    }

    /// <summary>
    /// 备份恢复侧：把包里那一条流写回 clip 目录。<b>只写被这一份备份的条目认领的名字</b>——
    /// 压缩包里的条目名是外部数据：第一道防线是 <see cref="ClipAssets.FullPathOf"/> 那道名册
    /// （它拒 <c>/</c>、<c>\\</c>、<c>:</c> 与超长名，所以构造上写不到目录之外；Zip Slip 那条审查结论走的正是这里），
    /// 白名单是第二道。
    /// <para><b>已存在的同名文件不覆盖</b>：本机那一张可能正是用户自己放回来的（§3-Q6 第三类反向），
    /// 拿一份旧备份把它换掉是净损失。写盘仍是"临时名 + 同卷改名"（与采集侧同一条路），
    /// 半途崩溃只会留下一个 <c>*.tmp</c>，下次启动对账按临时件清。</para>
    /// </summary>
    public static async Task<bool> WriteBackFromBackupAsync(
        string name, Stream source, HashSet<string> claimed, CancellationToken ct)
    {
        if (!claimed.Contains(name)) return false;                 // 条目不认它：不是我们的文件，别碰
        var path = ClipAssets.FullPathOf(name);
        if (path is null) return false;
        try
        {
            if (File.Exists(path)) return false;                   // 本机已有：不覆盖（上面写了为什么）
            // clip 目录是采集到第一张图时才建的（`WritePairAsync` 里那一句），换机/新机器上它可能还不存在。
            // 这一句不能省：目录不在时每张附件都会撞 DirectoryNotFoundException，
            // 于是"条目都回来了、N 个图片没能写入"——而换机恢复正是这批存在的理由。
            Directory.CreateDirectory(Folder);
            var temp = Path.Combine(Folder, ClipAssets.TempNameOf(name));
            await using (var fs = File.Create(temp))
                await source.CopyToAsync(fs, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            TryDelete(ClipAssets.TempNameOf(name));                // 半件不如没有：别让残货占着名册
            StarLog.Warn($"备份恢复：图片写不进去（{name}）：{ex.GetType().Name} {ex.Message}");
            return false;
        }
    }
    private static async Task<byte[]> ToBytesAsync(InMemoryRandomAccessStream stream)
    {
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
