#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using Windows.Storage;

namespace StarMark.UI.Services;

/// <summary>一次物化的结局：真路径几条（进 AddDroppedFilesAsync 的那批，%TEMP% 暂存下）・几条没物化成。</summary>
public sealed record TodoVirtualFileMaterialization(IReadOnlyList<string> Paths, int Failed);

/// <summary>
/// 虚拟文件的<b>物化腿</b>（P-307）：WinUI 对浏览器拖图给的 StorageFile 常常 <c>path</c> 为空——
/// 只有流（<c>OpenReadAsync</c> 走 FileContents・片289 轮12 实证）没有盘上地址。这条腿把流抄到
/// %TEMP% 暂存拿到真路径，交给 AddDroppedFilesAsync 既有链——暂存在临时根里＝
/// <see cref="TodoAttachmentPolicy.NeedsForcedCopy"/> 必然强制 Copy 入托管树（引用临时目录＝悬空引用，P-305 已钉）。
/// <para>
/// 稳健机器（参数照 v6 参数表・行为规格参照）：预算——盘感知（<see cref="TodoVirtualFilePolicy"/>），
/// 单次拖放从预算里逐文件扣，明知放不下不落盘、流超余量中途断；魔数——名字缺扩展名按内容补（Core 判据），
/// 认不出保留原名不瞎猜、补名失败不弃件；空壳——一份没物化成不留它的半截、一批全灭不留空目录。
/// </para>
/// </summary>
public static class TodoVirtualFileMaterializer
{
    /// <summary>暂存根：%TEMP% 下的自留地。躺在临时根里＝P-305 的强制复制自动接管，既有链一行不改。</summary>
    private static string StagingRoot => Path.Combine(Path.GetTempPath(), "StarMark", "todo-drops");

    /// <summary>
    /// 把一批空路径 StorageFile 物化到 %TEMP% 暂存。逐文件隔离：一份失败（拒收/读流失败/写盘失败）
    /// 只算它自己的，不连坐整批；全灭时不留空目录。返回的真路径由调用方带进 AddDroppedFilesAsync，
    /// 并在 Copy 入托管树之后用 <see cref="DiscardStaged"/> 清掉暂存原件。
    /// </summary>
    public static async Task<TodoVirtualFileMaterialization> MaterializeAsync(IReadOnlyList<StorageFile> files)
    {
        var paths = new List<string>();
        if (files.Count == 0) return new TodoVirtualFileMaterialization(paths, 0);
        var remaining = TodoVirtualFilePolicy.ResolveStagingBudgetBytes(TryQueryStagingDriveFreeBytes());
        var dir = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"));
        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                if (remaining <= 0)
                {
                    failed++;
                    StarLog.Warn($"[待办] 暂存预算已尽（盘可用见底），这份不物化：{file.Name}");
                    continue;
                }
                var staged = await TryStageAsync(file, dir, remaining);
                if (staged is null) { failed++; continue; }   // 拒收的理由 TryStageAsync 自己记过了
                remaining -= staged.Value.Written;
                paths.Add(staged.Value.Path);
            }
            catch (Exception ex)
            {
                failed++;
                StarLog.Error($"[待办] 虚拟文件物化失败：{file.Name}", ex);
            }
        }
        if (paths.Count == 0 && failed > 0) TryDeleteEmptyDirQuietly(dir);   // 一批全灭：连空壳目录一起收走
        if (failed > 0)
            StarLog.Warn($"[待办] 虚拟文件物化：{paths.Count} 份成，{failed} 份没成");
        return new TodoVirtualFileMaterialization(paths, failed);
    }

    /// <summary>
    /// 物化暂存件<b>用完即清</b>：进托管树以后 %TEMP% 那份就是孤儿——每次虚拟拖放都留孤儿
    /// 直到系统清盘不体面。顺带暂存子目录空了就一并收走（只收真空壳，不递归）。
    /// 两步都只记日志：清不掉的孤儿归系统清理，绝不倒过来把挂附件这一步打断。
    /// </summary>
    public static void DiscardStaged(string stagedPath)
    {
        TryDeleteQuietly(stagedPath);
        try
        {
            var dir = Path.GetDirectoryName(stagedPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[待办] 收走空暂存目录失败（留给系统清理）：{stagedPath}：{ex.Message}");
        }
    }

    /// <summary>
    /// 物化一份：开流→预算预检（声明大小放不下就不落盘）→钳着余量抄流→缺扩展名按魔数补。
    /// 返回 null＝拒收（预算不足，理由已记日志）；抛异常＝失败（半截已由这里的 catch 清掉）。
    /// </summary>
    private static async Task<(string Path, long Written)?> TryStageAsync(StorageFile file, string dir, long capBytes)
    {
        using var winrtSource = await file.OpenReadAsync();
        var declared = (long)winrtSource.Size;
        if (declared > capBytes)
        {
            StarLog.Warn($"[待办] 虚拟文件物化拒收（暂存余量 {capBytes} 字节，这份声明 {declared} 字节）：{file.Name}");
            return null;
        }
        Directory.CreateDirectory(dir);   // 惰性建目录：一份真要落才建，整批全灭连目录都不出现
        var target = UniquePathIn(dir, SafeFileNameOf(file));
        long written = 0;
        try
        {
            await using var source = winrtSource.AsStreamForRead();
            await using var sink = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var header = new byte[TodoVirtualFilePolicy.HeaderLength];
            var headerFilled = 0;
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                if (written + read > capBytes)
                    // 声明谎报/流比声明长：断流弃件——单次拖放饿不死宿主盘
                    throw new IOException($"流长超过暂存余量（>{capBytes} 字节），断流弃件：{file.Name}");
                await sink.WriteAsync(buffer.AsMemory(0, read));
                if (headerFilled < header.Length)
                {
                    var take = Math.Min(read, header.Length - headerFilled);
                    buffer.AsSpan(0, take).CopyTo(header.AsSpan(headerFilled));
                    headerFilled += take;
                }
                written += read;
            }
            if (!Path.HasExtension(target))
            {
                var inferred = TodoVirtualFilePolicy.TryInferExtension(header.AsSpan(0, headerFilled));
                if (inferred is not null)
                {
                    var renamed = UniquePathIn(dir, Path.GetFileName(target) + inferred);
                    try { File.Move(target, renamed); target = renamed; }
                    catch (Exception ex)
                    {
                        // 补名失败不弃件：已物化的载荷不能因改个名就不可用（保留原名）
                        StarLog.Warn($"[待办] 魔数补扩展名失败，保留原名：{target}：{ex.Message}");
                    }
                }
            }
        }
        catch
        {
            TryDeleteQuietly(target);   // 空壳治理：半截不留盘
            throw;
        }
        return (target, written);
    }

    /// <summary>拖源给的「名字」只取文件名段再兜空名：带路径段的名字落到暂存目录外面就是脏手，空名字连文件都建不出。</summary>
    private static string SafeFileNameOf(StorageFile file)
    {
        var name = Path.GetFileName(file.Name);
        return name.Length == 0 ? "dropped" : name;
    }

    /// <summary>同名冲突加「 (2)」式后缀，不覆盖——与 <see cref="TodoAttachmentStorage.CopyIn"/> 同一口径。</summary>
    private static string UniquePathIn(string dir, string name)
    {
        var target = Path.Combine(dir, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var n = 2;
        while (File.Exists(target))
            target = Path.Combine(dir, $"{stem} ({n++}){ext}");
        return target;
    }

    /// <summary>查暂存盘可用空间（null＝查不到：UNC/盘没就绪/出错——走 Core 判据的兜底预算）。</summary>
    private static long? TryQueryStagingDriveFreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(StagingRoot));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[待办] 查暂存盘可用空间失败，按兜底预算走：{ex.Message}");
            return null;
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { StarLog.Warn($"[待办] 清理暂存件失败（留给系统清理）：{path}：{ex.Message}"); }
    }

    private static void TryDeleteEmptyDirQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[待办] 收走空暂存目录失败（留给系统清理）：{dir}：{ex.Message}");
        }
    }
}
