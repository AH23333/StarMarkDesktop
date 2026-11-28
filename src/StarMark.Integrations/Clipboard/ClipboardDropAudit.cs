#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;

namespace StarMark.Integrations.Clipboard;

/// <summary>
/// clip-drops 那一族的启动对账与"孤儿/临时件"清理路（片292・照 <see cref="ClipboardAssetAudit"/> 同一范式配齐）。
/// 判据本身在 <see cref="ClipDropAssets.Reconcile"/>（纯函数、可逐值断言）；这里只做三件 IO 事：
/// 读认领名单、读目录、（清理那条路）删件。
/// <para>
/// <b>启动那一条一件不删、一行库也不写</b>（片292・分叉一甲）：drops 的认领是"一行认 N 件"，
/// 行级缺失标记语义不清（一行三件缺一件，标谁？），也没有 UI 读那颗标记；无状态＝天然幂等——
/// 件被用户放回来，"认领缺失"那一类自动归零（§3-Q6 第三类反向免费得到）。
/// 删除只发生在 <see cref="CleanAsync"/>：那条路由设置页的确认框触发，且删前<b>自己重扫一次</b>。
/// </para>
/// <para>返回 null＝<b>没跑成</b>（权限、杀软、库被占）。调用方不许把 null 当成"一切正常"。</para>
/// </summary>
public static class ClipboardDropAudit
{
    /// <summary>一次扫描的两份材料：对账结果 + 目录清单（要算孤儿那几件的字节数得留着清单）。</summary>
    public readonly record struct Scan(ClipDropAssets.ReconcileDropResult Result, IReadOnlyList<(string Name, long Bytes)> Files);

    /// <summary>
    /// 读一次认领名单 + 读一次目录，对出三分类。<b>启动审计、设置页那句孤儿计数、清理前的预览与清理本身
    /// 全部只问这一处</b>：各处自己拼一份"什么算孤儿"，就会出现按旧名单删掉刚被历史认领的文件。
    /// </summary>
    public static async Task<Scan?> ScanAsync(IItemRepository repo, CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        try
        {
            var claims = await repo.GetClipboardDropClaimsAsync(ct);
            var files = ClipboardDropFileStore.ListFiles();
            if (claims.Count == 0 && files.Count == 0)
                return new Scan(ClipDropAssets.ReconcileDropResult.Empty, files);   // 从没拖放过：连目录都不必存在，别为它写日志

            return new Scan(ClipDropAssets.Reconcile(claims, files.Select(f => f.Name)), files);
        }
        catch (Exception ex)
        {
            StarLog.Error("拖入文件对账没跑成（不影响使用，下次启动再来）", ex);
            return null;
        }
    }

    /// <summary>孤儿 + 临时件各自的字节数（目录枚举已经带着每件的大小，这里只按名字归堆）。</summary>
    public static (long OrphanBytes, long TempBytes) BytesOf(Scan scan)
    {
        // OrdinalIgnoreCase：NTFS 大小写不敏感，Reconcile 也是按它认"谁被认领"的——两处不同就会算出两种体积。
        var want = new HashSet<string>(scan.Result.OrphanNames, StringComparer.OrdinalIgnoreCase);
        var temp = new HashSet<string>(scan.Result.TempNames, StringComparer.OrdinalIgnoreCase);
        long o = 0, t = 0;
        foreach (var (name, bytes) in scan.Files)
        {
            if (want.Contains(name)) o += bytes;
            else if (temp.Contains(name)) t += bytes;
        }
        return (o, t);
    }

    /// <summary>
    /// 启动对账：<b>只读只记日志</b>（分叉一甲——三类全是"数出来的事实"，零类动数据库、零类动文件）。
    /// 没事发生时一行日志也不写：安静不是隐瞒，是"没有需要说的事"。
    /// </summary>
    public static async Task<ClipDropAssets.ReconcileDropResult?> RunAsync(
        IItemRepository repo, CancellationToken ct = default)
    {
        if (await ScanAsync(repo, ct) is not { } scan) return null;
        var result = scan.Result;
        if (!result.NothingToDo)
            StarLog.Info($"[剪贴板] 拖入文件对账：认领缺失 {result.MissingNames.Count} 件、"
                + $"孤儿 {result.OrphanNames.Count} 件（启动只数不删）、"
                + $"临时件 {result.TempNames.Count} 件");
        return result;
    }

    /// <summary>
    /// 清理孤儿文件与自家临时残件。<b>不接界面传来的名单</b>：那份名单是用户读它那一刻的事实，
    /// 中间一次"同一件再拖"或备份恢复就可能让某个名字重新被历史条目认领——按旧名单删就等于删活文件。
    /// <para>每一个名字都过 <see cref="ClipboardDropFileStore.TryDelete"/>（内含"必须在 clip-drops 目录里、
    /// 名字必须过名册"那两道闸），所以这一条路在构造上删不到目录之外；删不动的（占用、权限）
    /// 计入 <c>Failed</c> 并逐件写日志，绝不当成"已经清理过了"。</para>
    /// </summary>
    public static async Task<CleanupOutcome?> CleanAsync(IItemRepository repo, CancellationToken ct = default)
    {
        if (await ScanAsync(repo, ct) is not { } scan) return null;
        var names = scan.Result.OrphanNames;
        var temps = scan.Result.TempNames;

        if (names.Count + temps.Count == 0)
            return new CleanupOutcome(0, 0, 0, 0, 0, 0);     // 没得清：这不是失败，也不许报"清理完成"

        var (ok, okBytes, bad) = DeleteAll(names, ct);
        var (okT, okTBytes, badT) = DeleteAll(temps, ct);
        StarLog.Info($"[剪贴板] 清理拖入文件孤儿：删掉 {ok} 件（约 {ClipAssets.DescribeBytes(okBytes)}）、"
            + $"临时件 {okT} 件（约 {ClipAssets.DescribeBytes(okTBytes)}），{bad + badT} 件没能删掉");
        return new CleanupOutcome(ok, okBytes, bad, okT, okTBytes, badT);
    }

    private static (int Deleted, long Bytes, int Failed) DeleteAll(IReadOnlyList<string> names, CancellationToken ct)
    {
        var deleted = 0; var failed = 0; var bytes = 0L;
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            // 体积要在删之前取：删完再问就什么都没有了，而"清掉了多少 MB"是用户按下去之后唯一想看的数。
            long size = 0;
            try { var p = ClipDropAssets.FullPathOf(name); if (p is not null && File.Exists(p)) size = new FileInfo(p).Length; }
            catch (Exception) { /* 报 0：那是句子里的一项，不该让删除本身失败 */ }
            if (ClipboardDropFileStore.TryDelete(name)) { deleted++; bytes += size; }
            else
            {
                failed++;
                StarLog.Warn($"[剪贴板] 这件拖入文件没能删掉：{name}（多半正被别的程序占用）");
            }
        }
        return (deleted, bytes, failed);
    }

    /// <summary>清理结果。<b>孤儿与临时件分开报</b>：合成一个数，用户分不清"历史残留"与"半件"这两种东西。</summary>
    public readonly record struct CleanupOutcome(
        int Deleted, long DeletedBytes, int Failed, int TempDeleted, long TempDeletedBytes, int TempFailed);
}
