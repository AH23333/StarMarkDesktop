#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;

namespace StarMark.Core.Backup;

/// <summary>
/// 备份的<b>封装</b>：一份 <c>backup.json</c>（清单，字节与老版本完全一致）＋ 可选的 <c>clip/</c> 附件目录。
/// <para>
/// 为什么是 zip 而不是"把图片塞进 JSON"：§3-Q2 的终态写的是 <c>zip(store)</c>——PNG 已经是无损压缩，
/// 再 deflate 一遍平均省不到 5%，<b>打包的真实价值是封装完整性</b>（一次导出＝一个文件，不会只拷走一半）。
/// 用 <see cref="CompressionLevel.NoCompression"/> 正是那条裁决的字面执行：不假装省空间。
/// </para>
/// <para>
/// 两条不可省的性质：① <b>清单字节不变</b>——老的 <c>.json</c> 备份照常能读，新的 <c>.zip</c> 里的清单
/// 也是同一套序列化与校验和，导入路径不需要知道附件存在；② <b>逐条流式</b>——几百张图一次进内存
/// 是几百 MB 峰值，而备份/恢复正是最不该崩的那一刻，所以这里只搬流，不 <c>ReadAllBytes</c>。
/// </para>
/// </summary>
public static class BackupContainer
{
    /// <summary>清单在包里的条目名（也是 zip 与 .json 两种载体唯一的公共入口）。</summary>
    public const string ManifestEntryName = "backup.json";

    /// <summary>纯清单载体的扩展名（老的、也是不带附件的那一种）。</summary>
    public const string ManifestExtension = ".json";

    /// <summary>带附件载体的扩展名。<b>这两个常量是"哪种内容用哪个名字"的唯一出处</b>：
    /// 认扩展、换扩展、以及导出时给用户的建议名各写一份，就会出现"列表认得、默认值认不得"的分岔。</summary>
    public const string ContainerExtension = ".zip";

    /// <summary>附件目录前缀。<b>只认这一层</b>：其它条目（含 <c>../</c>、绝对路径、反斜杠形式）一概不当图片读。</summary>
    public const string ClipPrefix = "clip/";

    /// <summary>单条附件的体积上限：与采集侧同一道线（<see cref="ClipboardPolicy.MaxImageBytes"/>）。</summary>
    public const long MaxEntryBytes = ClipboardPolicy.MaxImageBytes;

    /// <summary>是否 zip 载体（按扩展名，大小写不敏感）。</summary>
    public static bool IsContainer(string path)
        => string.Equals(Path.GetExtension(path), ContainerExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 这个路径是不是"一份备份"。<b>"哪些文件算备份"只有这一个出处</b>：盘点列表、导入时的校验、
    /// "取最近一份"的目录扫描三处各写一份扩展名表，就会有一处漏掉 <c>.zip</c>——
    /// 而漏的那一处恰好是唯一下过图片的那份，用户看到的是"我导出过三份，列表只剩两份"。
    /// </summary>
    public static bool IsBackupPath(string? path)
        => !string.IsNullOrEmpty(path)
           && (string.Equals(Path.GetExtension(path), ManifestExtension, StringComparison.OrdinalIgnoreCase)
               || IsContainer(path!));

    /// <summary>把"用户要的目标路径"换成与之同名的另一种载体（带图导出时 .json → .zip）。</summary>
    public static string SwapExtension(string path)
        => IsContainer(path)
            ? Path.ChangeExtension(path, ManifestExtension)
            : Path.ChangeExtension(path, ContainerExtension);

    /// <summary>读出清单原文（.json 直接读文件；.zip 取 <see cref="ManifestEntryName"/> 那一条）。</summary>
    public static async Task<string> ReadManifestAsync(string path, CancellationToken ct)
    {
        if (!IsContainer(path)) return await File.ReadAllTextAsync(path, ct);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(ManifestEntryName)
                ?? throw new BackupFormatException("这个压缩包里没找到备份清单（backup.json），已拒绝导入。");
            using var reader = new StreamReader(entry.Open());
            return await reader.ReadToEndAsync(ct);
        }
        catch (BackupFormatException) { throw; }
        catch (Exception ex)
        {
            // 半个 zip（导出途中断电/磁盘满）、被改过后缀的 .json、损坏的包都落到这里。
            // 报成"备份文件损坏"而不抛 InvalidDataFileException：调用方只把消息显示到状态栏。
            throw new BackupFormatException($"这个备份包打不开（{ex.GetType().Name}）：文件可能没写完或已损坏，已拒绝导入。");
        }
    }

    /// <summary>
    /// 写出<b>带附件</b>的备份：清单 + 逐张 <c>clip/&lt;name&gt;</c>（不压缩）。返回真进了包的那几张的
    /// <b>条数与字节数</b>（以写完之后那份文件为准，而不是以"计划带几张"为准）。
    /// <para>原子性沿用导出侧一贯做法：先写 <c>*.tmp</c> 再同卷改名。半份 zip 留在磁盘上只会让人
    /// 以为"备份成功了"——而 zip 没有"条目不全"这种可见信号，读它的人只看到一份能打开的包。</para>
    /// <para>某一张读不到（被占用/被删）只意味着那一张不进包：笔记、标签、组件数据才是不可重建的部分。</para>
    /// </summary>
    public static async Task<(int Count, long Bytes)> WriteAsync(string path, string manifestJson,
        IReadOnlyList<string> clipNames, CancellationToken ct)
    {
        var temp = path + ".tmp";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        try
        {
            using (var fs = File.Create(temp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var manifest = zip.CreateEntry(ManifestEntryName, CompressionLevel.NoCompression);
                using (var w = new StreamWriter(manifest.Open()))
                    await w.WriteAsync(manifestJson.AsMemory(), ct);
                foreach (var name in clipNames)
                {
                    ct.ThrowIfCancellationRequested();
                    // 先开流、后建条目：带不走的那张根本不该在包里出现（zip 的 Create 模式建了就收不回，
                    // 留下一个零字节成员等于在包里冒充"这张有"）。
                    await using var source = ClipboardImageStore.TryOpenForPackage(name);
                    if (source is null) continue;
                    var entry = zip.CreateEntry(ClipPrefix + name, CompressionLevel.NoCompression);
                    await using var es = entry.Open();
                    await source.CopyToAsync(es, ct);
                }
            }
            File.Move(temp, path, overwrite: true);
            return ReadBackClipStat(path, clipNames.Count);
        }
        catch
        {
            TryDeleteTemp(temp);
            throw;
        }
    }

    /// <summary>
    /// 写完之后再从<b>那份文件本身</b>数一次附件（张数与字节）。<paramref name="planned"/> 只在重读失败时兜底。
    /// <para>为什么不直接报"计划几张就是几张"：报出去的数字要说的是"这个包里到底有什么"，
    /// 用户拿它判断"换机之后图到底跟没跟过去"。</para>
    /// </summary>
    private static (int Count, long Bytes) ReadBackClipStat(string path, int planned)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var n = 0;
            long bytes = 0;
            foreach (var e in zip.Entries)
                if (e.FullName.StartsWith(ClipPrefix, StringComparison.Ordinal)) { n++; bytes += e.Length; }
            return (n, bytes);
        }
        catch (Exception) { return (planned, 0L); }      // 刚写完就读不出只是计数不准，不该让导出失败
    }

    /// <summary>
    /// 把包里的 <c>clip/</c> 条目写回图片目录（<b>恢复</b>用）。返回 (写出, 跳过, 失败)：
    /// "跳过"含两种事实——本机已有同名文件（不覆盖）与条目不认领它；两者都必须与"写失败"分开报，
    /// 否则用户分不清"图都回来了"与"一张都没回来"。
    /// <para>名单外的条目、体积超过单张上限的条目、以及名字过不了 <see cref="ClipAssets"/> 名册的条目
    /// 一律不写：压缩包内容是不可信的外部输入。</para>
    /// </summary>
    public static async Task<(int Written, int Skipped, int Failed, IReadOnlyList<string> PackageNames, bool Opened)>
        ExtractClipsAsync(string path, HashSet<string> claimedNames, CancellationToken ct)
    {
        if (!IsContainer(path) || claimedNames.Count == 0)
            return (0, 0, 0, Array.Empty<string>(), true);     // 没东西可开＝没有失败：.json 与"条目不认领任何名字"都是这一档
        int written = 0, skipped = 0, failed = 0;
        var names = new List<string>();
        var opened = false;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            opened = true;                                      // 打开成功了才有资格说"这份包里没带图片"
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!entry.FullName.StartsWith(ClipPrefix, StringComparison.Ordinal)) continue;
                var name = entry.FullName[ClipPrefix.Length..];
                names.Add(name);        // "包里到底有哪几条"要交给调用方：只有它能算出"哪几条历史没被这张包覆盖"
                if (name.Length == 0 || entry.Length > MaxEntryBytes) { skipped++; continue; }
                bool ok;
                try
                {
                    await using var es = entry.Open();
                    ok = await ClipboardImageStore.WriteBackFromBackupAsync(name, es, claimedNames, ct);
                }
                catch (Exception ex)
                {
                    StarLog.Warn($"备份恢复：解出一条附件时出错（{name}）：{ex.GetType().Name} {ex.Message}");
                    failed++;
                    continue;
                }
                // WriteBackFromBackup 对"不认领/名册不过/已存在"与"写失败"都回 false，
                // 所以这里按名册自己再过一遍来分箱：能算清的就别说成失败。
                if (ok) written++;
                else if (!claimedNames.Contains(name) || ClipAssets.FullPathOf(name) is null) skipped++;
                else if (File.Exists(ClipAssets.FullPathOf(name)!)) skipped++;
                else failed++;
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("备份恢复：附件包打不开（条目本体已按无附件处理）", ex);
        }
        return (written, skipped, failed, names, opened);
    }

    /// <summary>这个载体里带了几张图片本体（0＝没带；<c>.json</c> 恒 0）。只读目录区，不解压。</summary>
    public static int CountClipEntries(string path) => IsContainer(path) ? ReadBackClipStat(path, 0).Count : 0;

    private static void TryDeleteTemp(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { /* 残件交给下次同名覆盖 */ }
    }
}
