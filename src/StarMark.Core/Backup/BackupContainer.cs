#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
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

    /// <summary>
    /// 画板场景那一条前缀（Phase C 片146＝集成矩阵「场景 → 备份链」那一行）。
    /// <para>它住在<b>第二层</b>而 <see cref="ClipPrefix"/> 住在第一层，两族因此互不吃到对方：
    /// 认前缀的那几处全部按<b>整颗</b> <c>boards/</c> 判，包外一颗 <c>boardx/</c> 进不来。</para>
    /// <para>⚠ 这颗名字与 <see cref="StarMark.Core.Boards.BoardPolicy.BoardsDirectoryName"/> <b>同串而各住各处</b>是
    /// 有意的：盘上那棵树叫什么归画板那一族，包里那一条前缀叫什么归备份这一族——换掉盘上那一段不该把已有备份包
    /// 里的条目一起作废（那是"改了程序就读不动自己三年前的包"那一族坏法）。</para>
    /// </summary>
    public const string BoardsPrefix = "boards/";

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
                ?? throw new BackupFormatException(LocMessage.Of("settings.data.backup.manifestMissing"));
            using var reader = new StreamReader(entry.Open());
            return await reader.ReadToEndAsync(ct);
        }
        catch (BackupFormatException) { throw; }
        catch (Exception ex)
        {
            // 半个 zip（导出途中断电/磁盘满）、被改过后缀的 .json、损坏的包都落到这里。
            // 报成"备份文件损坏"而不抛 InvalidDataFileException：调用方只把那句话贴到状态栏那一行。
            // 异常类型名是机器事实 ⇒ 走实参⛔ 抄进句子（✅P-178 口径，批次 WI-62／P-193）。
            throw new BackupFormatException(LocMessage.Of("settings.data.backup.packageUnopenable", ex.GetType().Name));
        }
    }

    /// <summary>
    /// 写出<b>带附件</b>的备份：清单 + 逐张 <c>clip/&lt;name&gt;</c> + 逐块 <c>boards/&lt;档名&gt;</c>（都不压缩）。
    /// 交回<b>真进了包</b>的两族条数与字节（以写完之后那份文件为准，而不是以"计划带几张"为准）。
    /// <para>原子性沿用导出侧一贯做法：先写 <c>*.tmp</c> 再同卷改名。半份 zip 留在磁盘上只会让人
    /// 以为"备份成功了"——而 zip 没有"条目不全"这种可见信号，读它的人只看到一份能打开的包。</para>
    /// <para>某一张读不到（被占用/被删）只意味着那一张不进包：笔记、标签、组件数据才是不可重建的部分。
    /// 画板那一族同理——那块板还在用户盘上，只是这一次没跟过去。</para>
    /// <para><paramref name="sceneFileNames"/> 里的每一颗都先过 <see cref="StarMark.Core.Boards.BoardPolicy.IsSceneFileName"/>：
    /// <c>.bak</c> 与 <c>*.tmp</c> 不进包（退路那份归退路，半途那一颗根本不算一块板），名字过不了形状判据的一颗也不进。</para>
    /// </summary>
    public static async Task<(int Clips, long ClipBytes, int Boards, long BoardBytes)> WriteAsync(
        string path, string manifestJson,
        IReadOnlyList<string> clipNames, IReadOnlyList<string> sceneFileNames, CancellationToken ct)
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
                foreach (var fileName in sceneFileNames)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!StarMark.Core.Boards.BoardPolicy.IsSceneFileName(fileName)) continue;
                    await using var source = TryOpenSceneForPackage(fileName);
                    if (source is null) continue;
                    var entry = zip.CreateEntry(BoardsPrefix + fileName, CompressionLevel.NoCompression);
                    await using var es = entry.Open();
                    await source.CopyToAsync(es, ct);
                }
            }
            File.Move(temp, path, overwrite: true);
            var (clips, clipBytes, boards, boardBytes) = ReadBackStats(path, clipNames.Count, sceneFileNames.Count);
            return (clips, clipBytes, boards, boardBytes);
        }
        catch
        {
            TryDeleteTemp(temp);
            throw;
        }
    }

    /// <summary>那块板的那一份档（读不到＝不进包：被人攥着、刚被删掉、或那棵目录被搬走）。⛔ 抛。</summary>
    private static Stream? TryOpenSceneForPackage(string fileName)
    {
        try
        {
            var full = Path.Combine(StarMark.Core.Boards.BoardPolicy.ScenesFolder(), fileName);
            return File.Exists(full) ? File.Open(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 写完之后再从<b>那份文件本身</b>数一次两族附件（条数与字节）。两个 <paramref name="planned"/> 只在重读失败时兜底。
    /// <para>为什么不直接报"计划几张就是几张"：报出去的数字要说的是"这个包里到底有什么"，
    /// 用户拿它判断"换机之后图与板到底跟没跟过去"。</para>
    /// </summary>
    private static (int Clips, long ClipBytes, int Boards, long BoardBytes) ReadBackStats(
        string path, int plannedClips, int plannedBoards)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var clips = 0;
            long clipBytes = 0, boardBytes = 0;
            var boards = 0;
            foreach (var e in zip.Entries)
            {
                if (e.FullName.StartsWith(ClipPrefix, StringComparison.Ordinal)) { clips++; clipBytes += e.Length; }
                else if (e.FullName.StartsWith(BoardsPrefix, StringComparison.Ordinal)) { boards++; boardBytes += e.Length; }
            }
            return (clips, clipBytes, boards, boardBytes);
        }
        catch (Exception) { return (plannedClips, 0L, plannedBoards, 0L); }   // 刚写完就读不出只是计数不准，不该让导出失败
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

    /// <summary>
    /// 把包里的 <c>boards/</c> 条目写回板档目录（<b>恢复</b>用，Phase C 片146＝「场景 → 备份链」那一行的后半）。
    /// 返回 (写回, 没动, 写不进, 包里几块, 包开没开成)。
    /// <para><b>本机已有同名的那块⛔ 动</b>：与图片那一族同一条规矩——用户在等这份备份的时候可能已经画了新板，
    /// 覆盖＝把刚画的东西用三年前的那块顶掉，而那句"已恢复"会把它说成一件好事。</para>
    /// <para><b>包里的内容是外部输入</b>，所以三道闸：档名过不了
    /// <see cref="StarMark.Core.Boards.BoardPolicy.IsSceneFileName"/>（那颗因此也挡住了 <c>.bak</c>、<c>*.tmp</c>
    /// 与一切带目录的形状）、体积超过 <see cref="StarMark.Core.Boards.BoardPolicy.SceneBytesCeilingBytes"/>、
    /// 以及那颗档⛔ 长得像一份场景（<see cref="StarMark.Core.Boards.BoardPolicy.LooksLikeSceneDoc"/>）
    /// ——第三道⛔ 加：写盘那一头今天本来就把"读得出却⛔ 是一颗档"的形状交给 Core 判（✅P-234 那半），
    /// 备份还原这一头如果放过那种东西，盘上就会出现一颗"本机自己从没写过的坏档"，而下一次载入那一路得替它兜底。</para>
    /// <para>写出去走 <c>*.tmp</c> ＋同卷改名（与 <c>BoardSceneStore</c> 那一条原子性同族）：半颗主档留在盘上
    /// ＝用户下次打开那块板看到的是"这块板没了"。</para>
    /// </summary>
    public static async Task<(int Written, int Skipped, int Failed, int Carried, bool Opened)>
        ExtractBoardsAsync(string path, CancellationToken ct)
    {
        if (!IsContainer(path)) return (0, 0, 0, 0, true);
        int written = 0, skipped = 0, failed = 0, carried = 0;
        var opened = false;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            opened = true;
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!entry.FullName.StartsWith(BoardsPrefix, StringComparison.Ordinal)) continue;
                var fileName = entry.FullName[BoardsPrefix.Length..];
                carried++;
                if (!StarMark.Core.Boards.BoardPolicy.IsSceneFileName(fileName)
                    || entry.Length > StarMark.Core.Boards.BoardPolicy.SceneBytesCeilingBytes)
                { skipped++; continue; }
                var target = Path.Combine(StarMark.Core.Boards.BoardPolicy.ScenesFolder(), fileName);
                if (File.Exists(target)) { skipped++; continue; }       // 本机那块板比包里这块新
                byte[] bytes;
                try
                {
                    await using var es = entry.Open();
                    using var buffer = new MemoryStream();
                    await es.CopyToAsync(buffer, ct);
                    bytes = buffer.ToArray();
                }
                catch (Exception ex)
                {
                    StarLog.Warn($"备份恢复：解出一块画板时出错（{fileName}）：{ex.GetType().Name} {ex.Message}");
                    failed++;
                    continue;
                }
                if (!StarMark.Core.Boards.BoardPolicy.LooksLikeSceneDoc(bytes)) { skipped++; continue; }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temp = target + StarMark.Core.Boards.BoardPolicy.SceneTempSuffix;
                    await File.WriteAllBytesAsync(temp, bytes, ct);
                    File.Move(temp, target, overwrite: true);
                    written++;
                }
                catch (Exception ex)
                {
                    StarLog.Warn($"备份恢复：画板写不回盘（{fileName}）：{ex.GetType().Name} {ex.Message}");
                    failed++;
                }
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("备份恢复：画板附件包打不开（条目本体已按无附件处理）", ex);
        }
        return (written, skipped, failed, carried, opened);
    }

    /// <summary>这个载体里带了几张图片本体（0＝没带；<c>.json</c> 恒 0）。只读目录区，不解压。</summary>
    public static int CountClipEntries(string path) => IsContainer(path) ? ReadBackStats(path, 0, 0).Clips : 0;

    /// <summary>这个载体里带了几块画板（0＝没带；<c>.json</c> 恒 0）。与上面那颗同一条路：只读目录区。</summary>
    public static int CountBoardEntries(string path) => IsContainer(path) ? ReadBackStats(path, 0, 0).Boards : 0;

    private static void TryDeleteTemp(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { /* 残件交给下次同名覆盖 */ }
    }
}
