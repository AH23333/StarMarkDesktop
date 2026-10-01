#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Updates;
using StarMark.UI.Services;

namespace StarMark.UI.Helpers;

/// <summary>
/// 检查更新的排程与出声（批次 UE）。
/// <para>
/// 为什么要有"巡查"这一层：<see cref="UpdatePolicy.ProbeGapHours"/> 是 24 小时，而一个人开着应用
/// 一周不重启是常态。只在开机那一刻判一次，"每天检查一次"这句话就成了假的——它实际是"每次开机检查一次"，
/// 而那意味着常年开着机的人永远查不到新版。与自动备份那条同一个道理（<see cref="AutoBackupScheduler"/>）。
/// </para>
/// <para>
/// 三条硬约束：① <b>开关关掉就连定时器都不建</b>（"看着关了其实还在上网问"是最难发现的不诚实，
/// 也是这条功能唯一会往外发请求的那条路）；② <b>启动后让出首屏那几秒</b>再问第一次
/// ——检查更新要走网络，而首屏那 1~3 秒正被"启动后 UI 冻结"那条链盯着量，不该再给它添一趟；
/// ③ <b>只有"确实有新版且这一版还没提醒过"才出声</b>，判据全在 <see cref="UpdatePolicy"/>，这里一行都不重写。
/// </para>
/// </summary>
public static class UpdateScheduler
{
    /// <summary>
    /// 巡查周期。取判据（24 小时）的 1/24：真到点之后最多晚一小时才问，用户看不见这个差别，
    /// 而再密就只是每小时多一次"读一遍设置档 + 判一个时间差"。
    /// </summary>
    public static readonly TimeSpan ProbePeriod = TimeSpan.FromHours(1);

    /// <summary>开机之后第一次问之前让出的时间（与自动备份同一口径：抢首屏那几秒的磁盘与网络都不合适）。</summary>
    public static readonly TimeSpan FirstProbeDelay = TimeSpan.FromSeconds(20);

    private static Timer? _timer;

    /// <summary>一次巡查正在跑（跨 tick 重入的闩：启动那一次与定时器第一次 tick 可能撞上）。</summary>
    private static int _running;

    /// <summary>巡查是否已起（设置页据此说"程序会自己问"还是"已按你的设置停止"）。</summary>
    public static bool IsRunning => _timer is not null;

    /// <summary>
    /// 按当前设置起巡查；重复调用安全（先收旧表再起新表），设置页每按一次开关都走这里，
    /// 所以"改完要重启才认"在这条功能上不存在。
    /// </summary>
    public static void Start(SettingsStore settings, UpdateService service)
    {
        Stop();
        if (!settings.LoadUpdateState().AutoCheckEnabled) return;
        _timer = new Timer(_ => _ = ProbeAsync(settings, service), null, ProbePeriod, ProbePeriod);
        StarLog.Info($"更新检查巡查已起，每 {ProbePeriod.TotalHours:0} 小时问一次到点没有（判据：满 "
            + $"{UpdatePolicy.ProbeGapHours} 小时才真的上一次网）");
    }

    public static void Stop()
    {
        var old = Interlocked.Exchange(ref _timer, null);
        old?.Dispose();
    }

    /// <summary>
    /// 到点就问一次；没到点／开关关掉都回 null（<b>那不是失败，是"这次不该问"</b>）。
    /// 异常只进日志、不上抛——这条链在池线程跑，抛出去就是没人接的 unobserved task。
    /// </summary>
    public static async Task<UpdateReport?> ProbeAsync(SettingsStore settings, UpdateService service)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return null;
        try
        {
            var report = await service.TryAutoProbeAsync().ConfigureAwait(false);
            if (report is null)
            {
                StarLog.Info(settings.LoadUpdateState().AutoCheckEnabled
                    ? $"更新检查：本次跳过（距上次问不足 {UpdatePolicy.ProbeGapHours} 小时）"
                    : "更新检查：设置里已关闭，不会上网问");
                return null;
            }
            if (report.Announced) Announce(report);
            return report;
        }
        catch (Exception ex)
        {
            StarLog.Error("更新检查失败（不影响使用，下一次巡查会自己再问）", ex);
            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>启动那一次的入口：先让出首屏，再按判据问一次（之后交给巡查表）。</summary>
    public static void ScheduleFirstProbe(SettingsStore settings, UpdateService service)
        => _ = Task.Run(async () =>
        {
            await Task.Delay(FirstProbeDelay).ConfigureAwait(false);
            await ProbeAsync(settings, service).ConfigureAwait(false);
        });

    /// <summary>
    /// 他主动问的那一次（托盘「检查更新」）：<b>无论结果是什么都要当场看得见</b>——他刚做完一个动作，
    /// 而"点了没反应"与"正在后台跑"在用户侧长得一样（IF／P-54 那一族）。
    /// 只有"确实有新版"才附带那颗打开发布页的动作：在"还没发布过"那一格给一个下载出口，
    /// 等于承诺一个不存在的产物。
    /// </summary>
    public static void AnnounceOnDemand(UpdateReport report)
        => ShowCard("检查更新", report,
            UpdatePolicy.HasDownloadableRelease(report.Verdict) ? report.PageUrl : null);

    /// <summary>
    /// 把"有新版本"这件事贴上屏幕，并留一条能对得上的日志。
    /// <para><b>回 UI 线程</b>：提示卡是一扇 XAML 窗，建窗与摆窗都要用调度队列，从池线程直接发会当场失败；
    /// 而失败必须落到日志——这一批的教训是"API 说发了、屏幕上没有"（托盘气泡那一版）。</para>
    /// </summary>
    public static void Announce(UpdateReport report) => ShowCard("发现新版本", report, report.PageUrl);

    private static void ShowCard(string title, UpdateReport report, string? actionUrl)
    {
        var pump = App.MainWindow?.DispatcherQueue;
        if (pump is null)
        {
            StarLog.Info($"[提示卡] 主窗已不在，这条更新提示只留在日志：{title}：{report.Text}");
            return;
        }
        pump.TryEnqueue(() =>
        {
            var shown = false;
            try
            {
                // 地址交出去的是 report.PageUrl——本程序按配置里的仓库拼的那一个，不是远端回的连接串。
                shown = NoticeCard.Show(title, report.Text, actionUrl, "发布页");
            }
            catch (Exception ex) { StarLog.Warn($"[更新] 提示卡没能贴上屏幕：{ex.Message}"); }
            if (!shown) StarLog.Info($"（提示卡没能贴上屏幕，结果只留在日志）{title}：{report.Text}");
        });
    }
}
