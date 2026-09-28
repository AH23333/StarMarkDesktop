#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Backup;

namespace StarMark.UI.Helpers;

/// <summary>
/// 自动备份的巡查排程（批次 BK）。
/// <para>
/// 之所以是"巡查"而不是"定闹钟"：备份间隔从写死的 24 小时变成用户可调之后，只在启动时判一次
/// 就不再等于那个设置了——设成「每 6 小时」却连着开了一周的应用，实际只有一份。
/// 每 <see cref="ProbePeriod"/> 问一次"到点没"，<c>AutoBackupPolicy.ShouldRun</c> 仍是唯一判据，
/// 巡查只是把它多问几遍；每次巡查的代价是一趟 <c>auto-*.json</c> 目录列举（最多 7 个文件）。
/// </para>
/// <para>
/// 定时器放在线程池而不是 <c>DispatcherQueueTimer</c>：这条链整段都是同步磁盘 IO，
/// 放 UI 队列里就是"每十分钟冻一次界面"的来源。真正落盘那一段在 <see cref="BackupService"/>
/// 里已经是异步的。
/// </para>
/// </summary>
public static class AutoBackupScheduler
{
    /// <summary>
    /// 巡查周期。取最密档位（6 小时）的 1/36：备份实际落盘时刻最多比"到点"晚十分钟，
    /// 而这个精度对用户不可见（他看到的是列表里的日期）；再密就只是多扫盘。
    /// </summary>
    public static readonly TimeSpan ProbePeriod = TimeSpan.FromMinutes(10);

    private static Timer? _timer;

    /// <summary>一次巡查正在跑（跨 tick 重入的闩：启动那一次与定时器第一次 tick 可能撞上）。</summary>
    private static int _running;

    /// <summary>巡查是否已起（设置页据此说"程序会自己备份"还是"已按你的设置停止"）。</summary>
    public static bool IsRunning => _timer is not null;

    /// <summary>
    /// 按当前设置起巡查：关着就不起表（"看着关了其实还在扫盘/写文件"是最难发现的那类不诚实）。
    /// 重复调用安全——旧表先收掉再起新的，设置页每次改动都走这里。
    /// </summary>
    public static void Start(SettingsStore settings, BackupService service)
    {
        Stop();
        if (!settings.LoadAutoBackupEnabled()) return;
        _timer = new Timer(_ => _ = ProbeAsync(settings, service), null, ProbePeriod, ProbePeriod);
        StarLog.Info($"自动备份巡查已起，每 {ProbePeriod.TotalMinutes:0} 分钟问一次到点没有");
    }

    public static void Stop()
    {
        var old = Interlocked.Exchange(ref _timer, null);
        old?.Dispose();
    }

    /// <summary>
    /// 问一次"该不该落"，该落就落，并把结果写成一行日志（跳过也要写：
    /// "自动备份没动静"分成没开、未到点、空库三种，只有日志能区分）。
    /// <para>异常只进日志、不上抛——这条链在后台跑，抛出去就是没人接的 unobserved task。</para>
    /// </summary>
    public static async Task ProbeAsync(SettingsStore settings, BackupService service)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        try
        {
            var enabled = settings.LoadAutoBackupEnabled();
            var hours = settings.LoadAutoBackupIntervalHours();
            var written = await service.RunAutoBackupAsync(enabled, hours).ConfigureAwait(false);
            StarLog.Info(written is null
                ? enabled
                    ? $"自动备份：本次跳过（距上次不足 {hours} 小时，或库里没有可备份的内容）"
                    : "自动备份：设置里已关闭，不会落盘（手动导出与恢复前快照不受影响）"
                : $"自动备份已落盘：{written}（每 {hours} 小时一份，保留最近 {AutoBackupPolicy.Keep} 份）");
        }
        catch (Exception ex)
        {
            StarLog.Error("自动备份失败（不影响使用，下次巡查会再试）", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
