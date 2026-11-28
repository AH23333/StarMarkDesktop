#nullable enable
using System;
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
}
