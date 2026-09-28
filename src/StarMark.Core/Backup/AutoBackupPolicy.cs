#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StarMark.Core.Backup;

/// <summary>
/// 自动备份的判定与保留策略（P-51，间隔与开关由批次 BK 变成用户可调）。
/// <para>
/// 之所以存在：设置页此前写的是「请定期导出备份」，而笔记/标签/隐藏/置顶/组件待办按同一页的说法
/// 不可重建 ⇒ 等于把"别丢数据"托付给人的记性。改成程序自己按天落一份，用户不需要任何一步。
/// </para>
/// <para>
/// 关键不变式：<b>只清 <c>auto-</c> 前缀的自动件</b>。用户手动导出的备份与导入前的
/// <c>pre-restore-</c> 快照永不在删除候选里——自动清理越过这条线就会变成"程序删了用户的备份"。
/// </para>
/// <para>
/// 「关掉自动备份」这条开关是<b>用户点名要的</b>（原先自主拍板不加，理由是"关了之后丢了别怪我"）。
/// 加它的同时必须把那句代价写进界面：关掉后程序不再落任何自动件，可回滚的只剩用户自己手动导出的那些份。
/// 关掉也不会删已有件——停止生成与清理历史是两件事，混在一起会让"先关一天看看"变成不可逆的操作。
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

    /// <summary>默认间隔（小时）。设置页的初始档、脏值/未知值回落的那一档、也是"没改过设置"时的行为。</summary>
    public const int DefaultIntervalHours = 24;

    /// <summary>
    /// 用户能选的间隔档位（小时）。<b>存的是小时数而不是枚举序号</b>：
    /// 数值本身就是语义，加一档或改顺序都不会让旧配置被解释成另一个间隔
    /// （持久化序号钉值那条规矩在这里没必要的道理就是它没必要存在）。
    /// </summary>
    public static readonly int[] IntervalOptions = { 6, 24, 72, 168 };

    /// <summary>下拉的档位文案（与 <see cref="IntervalOptions"/> 同序、同处生成）：
    /// 界面 <c>ItemsSource</c> 直接读它，XAML 里就没有第二份档位表——
    /// "下拉里有 120 小时但判据不认"这种分岔只能靠一处事实来防。</summary>
    public static IReadOnlyList<string> IntervalLabels { get; } =
        IntervalOptions.Select(h => "每 " + (h < 24 ? $"{h} 小时" : $"{h / 24} 天")).ToList();

    /// <summary>把落盘里读出来的间隔夹回合法档位；脏值（0、负数、没见过的数）一律回默认档。</summary>
    public static int ClampInterval(int hours)
        => Array.IndexOf(IntervalOptions, hours) >= 0 ? hours : DefaultIntervalHours;

    /// <summary>
    /// 档位序号 → 小时（界面上的 ComboBox 只有序号，持久化的是小时）。
    /// <para><b>越界回默认档，而不是夹到两端</b>：这里与护眼那条（夹到两端）刻意不同——
    /// WinUI 的 <c>SelectedIndex</c> 在还没选中时就是 <c>-1</c>，夹到端点等于
    /// "没选中＝选最密的那一档"，一次误触发的保存就会把间隔改成 6 小时。
    /// 默认档是谁都认识的那一档，宁可回到它。</para>
    /// </summary>
    public static int IntervalAt(int index)
        => index >= 0 && index < IntervalOptions.Length ? IntervalOptions[index] : DefaultIntervalHours;

    /// <summary>
    /// 小时 → 档位序号。脏值（落盘里出现没见过的小时数）回默认档所在的那一格，
    /// 于是界面永远不会"选中一个不存在的档"或干脆空着。
    /// </summary>
    public static int IntervalIndexOf(int hours)
    {
        var at = Array.IndexOf(IntervalOptions, ClampInterval(hours));
        return at >= 0 ? at : Array.IndexOf(IntervalOptions, DefaultIntervalHours);
    }

    /// <summary>
    /// 现在该不该落一份自动件。<b>开关关掉＝永不落盘</b>（界面上写着已关闭却还在生成自动件，是最难发现的一类不诚实）；
    /// 没有自动件＝该跑。
    /// <para>
    /// 用<b>绝对</b>差值而非 <c>now - newest &gt;= gap</c>：最新那份的时间戳在未来（用户调过系统时钟、
    /// 机器时钟先快后准、或该目录被云盘从别的机器同步进来）时，带符号差值恒为负 ⇒ 备份会
    /// <b>一直停摆到真实时间追平那个未来时刻</b>，而这恰好是时钟异常最可能伴随重装/迁移、最需要备份的时候。
    /// 取绝对值则"快一个间隔以上"与"慢一个间隔以上"同等对待；小于一个间隔的向前漂移仍按未到间隔处理，
    /// 免得每次唤起都重写一份。
    /// </para>
    /// </summary>
    public static bool ShouldRun(DateTimeOffset now, DateTimeOffset? newestAutoUtc, bool enabled, int intervalHours)
    {
        if (!enabled) return false;
        var gap = TimeSpan.FromHours(ClampInterval(intervalHours));
        return newestAutoUtc is null || (now - newestAutoUtc.Value).Duration() >= gap;
    }

    /// <summary>
    /// 当前设置下最多能回溯多少<b>天</b>（保留份数 × 间隔，向上取整）。
    /// <para>存在的理由：保留份数是写死的 7，间隔却变成了可调，于是"能回到多久以前"跟着设置变。
    /// 设置页必须把它算出来给用户看——只说"保留最近 7 份"的话，选了"每 7 天"的用户会以为
    /// 和每天备份时一样是一周的保险，实际上是四十九天；反选"每 6 小时"的人则拿不到他以为有的那一周。</para>
    /// </summary>
    public static int MaxLookbackDays(int hours)
        => (Keep * ClampInterval(hours) + 23) / 24;

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
    /// 「删除这一份」允许删谁：<b>必须正好是备份目录里那一份、且文件名认得出是备份件</b>。
    /// 返回 <c>null</c>＝可以删；否则返回拒绝原因（界面要能照着说清为什么没删）。
    /// <para>
    /// 这条判据放在 Core 且只有这一处：界面上那颗按钮拿到的是列表里的路径，而列表来自目录扫描——
    /// 一旦有人把删除做成"照传进来的路径删"，一条被改过的路径（或由云盘/重命名带出来的符号链接）
    /// 就成了"程序删掉用户任意文件"的入口。<b>目录必须逐个字符相等</b>（不能 <c>StartsWith</c>：
    /// 那样 <c>backups-evil</c> 会被当成 <c>backups</c> 里面）。
    /// </para>
    /// </summary>
    public static string? DeleteRefusal(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path)) return "没指定要删哪一份备份。";
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        var wanted = Path.GetFullPath(directory);
        if (!string.Equals(parent, wanted, StringComparison.OrdinalIgnoreCase))
            return $"{Path.GetFileName(full)} 不在备份目录里，程序不代删。";
        if (!BackupContainer.IsBackupPath(full))
            return $"{Path.GetFileName(full)} 不是备份件（只认 .json / .zip），不删。";
        return null;
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
