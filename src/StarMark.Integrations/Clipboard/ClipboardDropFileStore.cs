#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;

namespace StarMark.Integrations.Clipboard;

/// <summary>
/// <b>虚拟文件</b>拖进剪贴板格子之后的<b>写侧</b>（✅P-307 第二刀・乙）：物化、幂等重写、尽力删。
/// <para>
/// 与 <see cref="ClipboardImageStore"/> 同一颗范式：命名字典与校验是纯函数（<see cref="ClipDropAssets"/>，
/// 在 Abstractions，因为删侧的轮转/清空也要读同一份），这一边只做<b>会失败的那一半</b>——
/// 磁盘满、杀毒锁文件、用户手删目录，每一条都得失败得出原因且绝不把拖放链炸掉（UI 的 drop 体兜着）。
/// </para>
/// <para>
/// <b>幂等是这里的全部要点</b>：文件名由内容哈希决定（<see cref="ClipDropAssets.FileNameOf"/>），
/// 同一件再拖＝同一个名字＝同一个 <c>source_id</c>，合并回放；于是"写"必须做到
/// <b>第二次写同一个名字＝什么都不改、只报名字</b>——否则回放那一刻重写一遍，就把"本机那一份可能
/// 正被用户改着"的件悄悄换回拖进来的旧字节（图片目录那侧靠"MainExists 早退"守同一条，这里靠
/// <c>File.Move(overwrite: false)</c>，见 <see cref="TryWriteAsync"/>）。
/// </para>
/// </summary>
public static class ClipboardDropFileStore
{
    /// <summary>目录。<b>只是 <see cref="ClipDropAssets.Folder"/> 的别名</b>——写、删、改道都问同一份（理由同图片侧）。</summary>
    public static string Folder => ClipDropAssets.Folder;

    /// <summary>
    /// 写盘串行锁。<b>锁的是临时名，不是最终名</b>：最终名撞车有 <c>File.Move(overwrite: false)</c> 兜着，
    /// 而临时名同内容时也相同（<see cref="ClipDropAssets.TempNameOf"/>），两条并发腿各写一半再各自改名，
    /// 先改名的那条会把<b>另一条写到一半的字节</b>当成完整件搬走——锁住的是这个窗口，不是慢，是混。
    /// </summary>
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    /// <summary>
    /// 物化一个拖进来的虚拟文件。<b>幂等</b>：最终件已在（前一次拖放写的）就直接报名字，一个字节都不动——
    /// 见类注释那条"回放不得换掉本机的件"。
    /// <para>
    /// 先写 <c>*.tmp</c> 再 <c>File.Move(overwrite: false)</c>（同 <see cref="ClipboardImageStore"/> 的原子写）：
    /// 进程半途被杀，盘上只能多一个 <c>*.tmp</c> 残货，不会出现"半个文件顶着正式名字"；
    /// <c>overwrite: false</c> 撞上已存在的最终件时抛 <see cref="IOException"/>——同内容哈希相同，
    /// 撞名＝同一件已在，按成功收场（内容一致，谁写的都一样）。
    /// </para>
    /// <para>失败返回 <c>null</c> 并出声（磁盘满/路径被锁/用户手删了父目录都是用户该在回执里看到的"没记成"，
    /// 不是吞进日志的静默成功）。取消会把 <see cref="OperationCanceledException"/> 原样递出去——
    /// 关窗取消拖放是调用方的正当操作，不当"失败"记数。</para>
    /// </summary>
    /// <returns>最终文件名（认领名单用的那一个），失败为 <c>null</c>。</returns>
    public static async Task<string?> TryWriteAsync(string? originalName, byte[] content, CancellationToken ct)
    {
        var finalName = ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(content), originalName);
        await WriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var final = Path.Combine(Folder, finalName);
            if (File.Exists(final)) return finalName;               // 幂等：已在＝这次拖放不欠任何东西
            Directory.CreateDirectory(Folder);                      // 首次拖放前目录不存在，与图片侧同一条
            var temp = Path.Combine(Folder, ClipDropAssets.TempNameOf(finalName));
            try
            {
                await File.WriteAllBytesAsync(temp, content, ct).ConfigureAwait(false);
                File.Move(temp, final, overwrite: false);
                return finalName;
            }
            catch (IOException) when (File.Exists(final))
            {
                return finalName;                                   // 撞名＝同内容已在（哈希定的名）：按成功
            }
            finally
            {
                TryDeleteTemp(temp);                                // 成功的 Move 之后这是空操作；残货不留过夜
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StarLog.Warn($"[剪贴板] 拖入文件物化失败（{originalName ?? ClipDropAssets.FallbackOriginalName}）：{ex.GetType().Name} {ex.Message}");
            return null;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    /// <summary>
    /// 尽力删一个 clip-drops 件（<b>回滚用</b>：落库没成，物化出去的件当场收回，不留孤儿）。
    /// 名字不过 <see cref="ClipDropAssets.FullPathOf"/> 名册就直接报失败——<b>不抛、不碰盘</b>。
    /// </summary>
    public static bool TryDelete(string? name)
    {
        if (ClipDropAssets.FullPathOf(name) is not { } path) return false;
        try
        {
            if (!File.Exists(path)) return true;                    // 已不在＝目标达成
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[剪贴板] 拖入文件收回失败（{name}）：{ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch (Exception) { /* 残件清不掉归对账数，不挡主流程 */ }
    }

    /// <summary>目录里的文件名与字节数（占用统计与对账都读它；目录不存在就是空，不抛）。
    /// 与 <see cref="ClipboardImageStore.ListFiles"/> 同一颗范式：单文件属性读不到只跳过那一件，
    /// 整个目录枚举失败返回空表——占用显示成 0 比整页炸掉好。</summary>
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
        catch (Exception) { /* 权限/网络盘 */ }
        return list;
    }

    // ==================== 备份这一族（片292・与图片侧同一条链） ====================

    /// <summary>
    /// 备份要携带的拖入文件本体：<b>只挑此刻真在磁盘上、且过 <see cref="ClipDropAssets"/> 名字名册的那些</b>。
    /// 与 <see cref="ClipboardImageStore.PlanBackup"/> 同一颗范式：刻意只交名字、<b>不读内容</b>——
    /// 打包侧逐件流式拷（<see cref="TryOpenForPackage"/>），报出去的件数与字节都以"真进了包"为准。
    /// <para>认领名单是<b>并集</b>：同一件被两行认领（多认领混拖的常态）只带一份；名字不过名册的不带，也不报它是什么。</para>
    /// </summary>
    public static (List<string> Names, int Missing, int Failed) PlanBackup(IReadOnlyList<Item> items)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int missing = 0, failed = 0;
        foreach (var item in items)
        {
            foreach (var name in ClipboardEntry.DropFileNamesOf(item.ExtraJson))
            {
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                var path = ClipDropAssets.FullPathOf(name);
                if (path is null) { failed++; continue; }               // 名字不过名册：不带，也不报它是什么
                try
                {
                    if (!File.Exists(path)) { missing++; continue; }    // 行认了、件已被删：包里不该出现这个名字
                    names.Add(name);
                }
                catch (Exception ex)
                {
                    failed++;
                    StarLog.Warn($"备份计划跳过一件拖入文件（{name}）：{ex.GetType().Name} {ex.Message}");
                }
            }
        }
        return (names, missing, failed);
    }

    /// <summary>
    /// 打包前的一次"这件能不能带走"探问：名字不过名册、文件不在、或正被别的程序独占时返回 <c>null</c>，不抛。
    /// <b>顺序是这里的全部要点：先开流、后建条目</b>（与 <see cref="ClipboardImageStore.TryOpenForPackage"/> 同一条理由：
    /// zip 的 Create 模式建了就收不回，留下零字节成员等于在包里冒充"这件有"）。
    /// </summary>
    public static Stream? TryOpenForPackage(string name)
    {
        var path = ClipDropAssets.FullPathOf(name);
        if (path is null) return null;
        try
        {
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"备份带不走一件拖入文件（{name}）：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 备份恢复侧：把包里那一条流写回 clip-drops 目录。<b>只写被这一份备份的条目认领的名字</b>——
    /// 压缩包里的条目名是外部数据：第一道防线是 <see cref="ClipDropAssets.FullPathOf"/> 那道名册
    /// （前 13 位必须是"12 位小写哈希＋下划线"，构造上写不到目录之外），白名单是第二道。
    /// <para><b>已存在的同名文件不覆盖</b>：本机那件可能正是用户自己放回来的，拿一份旧备份换掉它是净损失
    /// （与图片侧同一条裁决）。写盘仍是"临时名＋同卷改名"，半途崩溃只留一个 <c>*.tmp</c>，对账按临时件收。</para>
    /// </summary>
    public static async Task<bool> WriteBackFromBackupAsync(
        string name, Stream source, HashSet<string> claimed, CancellationToken ct)
    {
        if (!claimed.Contains(name)) return false;                     // 条目不认它：不是我们的文件，别碰
        var path = ClipDropAssets.FullPathOf(name);
        if (path is null) return false;
        try
        {
            if (File.Exists(path)) return false;                       // 本机已有：不覆盖
            // clip-drops 目录是第一次拖放时才建的（TryWriteAsync 里那句 CreateDirectory），
            // 换机/新机器上它可能还不存在——少这一句，恢复就变成"条目都回来了、每件都报没能写入"。
            Directory.CreateDirectory(Folder);
            var temp = Path.Combine(Folder, ClipDropAssets.TempNameOf(name));
            await using (var fs = File.Create(temp))
                await source.CopyToAsync(fs, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            TryDeleteTemp(Path.Combine(Folder, ClipDropAssets.TempNameOf(name)));   // 半件不如没有：别让残货占着名册
            StarLog.Warn($"备份恢复：拖入文件写不进去（{name}）：{ex.GetType().Name} {ex.Message}");
            return false;
        }
    }
}
