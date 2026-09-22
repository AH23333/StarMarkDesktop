#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StarMark.Core.Backup;

/// <summary>
/// 每日自动备份的判定与保留策略（P-51）。
/// <para>
/// 之所以存在：设置页此前写的是「请定期导出备份」，而笔记/标签/隐藏/置顶/组件待办按同一页的说法
/// 不可重建 ⇒ 等于把"别丢数据"托付给人的记性。改成程序自己按天落一份，用户不需要任何一步。
/// </para>
/// <para>
/// 关键不变式：<b>只清 <c>auto-</c> 前缀的自动件</b>。用户手动导出的备份与导入前的
/// <c>pre-restore-</c> 快照永不在删除候选里——自动清理越过这条线就会变成"程序删了用户的备份"。
/// </para>
/// </summary>
public static class AutoBackupPolicy
{
    /// <summary>自动备份文件名片段前缀，也是"哪些件允许被自动清理"的唯一判据。</summary>
    public const string Prefix = "auto-";

    /// <summary>
    /// 导入/覆盖恢复前自动快照的前缀。两个用途共用这一个常量：界面上标成"恢复前快照"，
    /// 以及它与 <c>auto-</c> 一起构成"永不进自动清理候选"的第二条臂。
    /// 写入侧（<c>BackupService.WriteSnapshotAsync</c>）必须引用它，否则分类与文件名会各说各话。
    /// </summary>
    public const string SnapshotPrefix = "pre-restore-";

    /// <summary>保留最近多少份自动件。</summary>
    public const int Keep = 7;

    /// <summary>备份件的归类（决定列表里怎么标，也决定能不能被自动清理）。</summary>
    public enum BackupKind { Auto, PreRestore, Manual }

    /// <summary>两份自动件之间的最小间隔。</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromHours(24);

    /// <summary>
    /// 现在该不该落一份自动件。没有自动件＝该跑。
    /// <para>
    /// 用<b>绝对</b>差值而非 <c>now - newest &gt;= MinGap</c>：最新那份的时间戳在未来（用户调过系统时钟、
    /// 机器时钟先快后准、或该目录被云盘从别的机器同步进来）时，带符号差值恒为负 ⇒ 备份会
    /// <b>一直停摆到真实时间追平那个未来时刻</b>，而这恰好是时钟异常最可能伴随重装/迁移、最需要备份的时候。
    /// 取绝对值则"快 24 小时以上"与"慢 24 小时以上"同等对待；小于 24 小时的向前漂移仍按未到间隔处理，
    /// 免得每次唤起都重写一份。
    /// </para>
    /// </summary>
    public static bool ShouldRun(DateTimeOffset now, DateTimeOffset? newestAutoUtc)
        => newestAutoUtc is null || (now - newestAutoUtc.Value).Duration() >= MinGap;

    /// <summary>是否自动件（按文件名前缀，路径无关）。</summary>
    public static bool IsAuto(string path)
        => Path.GetFileName(path).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按文件名归类一份备份。<b>先判 <c>pre-restore-</c> 再判 <c>auto-</c></b>：两者前缀互不包含，
    /// 顺序看着无关，但"回滚点"是唯一一个"用户会点它来撤销上一步"的类别，判错方向的代价最大，
    /// 所以让它先走。前缀匹配一律大小写不敏感（与 <see cref="IsAuto"/> 同口径，避免云盘/重命名改出大写）。
    /// </summary>
    public static BackupKind Classify(string pathOrFileName)
    {
        var name = Path.GetFileName(pathOrFileName);
        if (name.StartsWith(SnapshotPrefix, StringComparison.OrdinalIgnoreCase)) return BackupKind.PreRestore;
        if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return BackupKind.Auto;
        return BackupKind.Manual;
    }

    /// <summary>
    /// 给出要删除的过期自动件（保留最新 <paramref name="keep"/> 份）。非 <c>auto-</c> 前缀的一律
    /// 不进候选，哪怕它更旧；<c>keep=0</c> 表示清空全部自动件。
    /// </summary>
    public static IReadOnlyList<string> PrunePlan(IEnumerable<(string Path, DateTimeOffset ModifiedUtc)> files, int keep = Keep)
        => files.Where(f => IsAuto(f.Path))
                .OrderByDescending(f => f.ModifiedUtc)
                .Skip(Math.Max(0, keep))
                .Select(f => f.Path)
                .ToList();
}
