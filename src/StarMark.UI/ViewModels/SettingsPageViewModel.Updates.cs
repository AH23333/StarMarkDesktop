#nullable enable
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Core.Updates;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——「关于与更新」（批次 UE）：版本号、自动检查开关、上次检查的那句话。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"。</para>
/// <para>
/// 这一段最要紧的一条是<b>进页面不许顺手发请求</b>：这一屏打开时读的是上一次检查留在设置档里的那条答复
/// （<see cref="UpdateService.LastReport"/>，不联网）。否则"我只是想看一眼版本"就变成了每进一次设置页
/// 上一次网，而匿名配额每小时只有 60 次——用不了几次就会被对方限流，届时界面只能说"现在问不了"。
/// </para>
/// </summary>
public partial class SettingsPageViewModel
{
    /// <summary>「每天自动检查一次」那颗开关。默认开＝这条功能刚上线时的行为；关掉之后程序一次网也不上。</summary>
    [ObservableProperty] private bool _updateAutoCheckEnabled;

    /// <summary>状态行（<b>只给界面读</b>：名字含 Status，因此不会被那次防抖整页保存当成待写项）。</summary>
    [ObservableProperty] private string _updateStatus = string.Empty;

    private bool _suppressUpdateApply;

    /// <summary>本机版本号。读不到就照实说"未知"——写 0.0.0 会让界面看着像一个很旧的版本。</summary>
    public string LocalVersionText => AppVersion.LocalDisplay;

    partial void OnUpdateAutoCheckEnabledChanged(bool value)
    {
        if (_suppressUpdateApply) return;
        ApplyUpdateAutoCheck(value);
    }

    /// <summary>
    /// 落盘 + <b>当场</b>把巡查表按新设置重排。
    /// <para>"关掉之后别再偷偷上网问"与"开着就别要我重启才生效"是同一条要求的两面，
    /// 与自动备份那颗开关完全同理（<see cref="AutoBackupScheduler"/>）。</para>
    /// </summary>
    private void ApplyUpdateAutoCheck(bool enabled)
    {
        Updates().AutoCheckEnabled = enabled;
        UpdateScheduler.Start(_settings, Updates());
        RefreshUpdateStatus();
    }

    private static UpdateService Updates() => App.Services.GetRequiredService<UpdateService>();

    /// <summary>问一次（他点了「立即检查」）。手动那一发绕过节奏，但同一个版本仍然只提醒一次。</summary>
    public Task<UpdateReport> CheckForUpdatesAsync() => Updates().CheckAsync(manual: true);

    /// <summary>这一版有没有东西可下：只有"确实有新版"才出现那颗「打开下载页」（判据在 Core，这里不重写）。</summary>
    public bool HasDownloadableRelease
        => Updates().LastReport() is { } report && UpdatePolicy.HasDownloadableRelease(report.Verdict);

    /// <summary>「打开下载页」要去的那个地址——<b>按配置里的仓库拼出来</b>，不是远端回的连接串。</summary>
    public string ReleasePageUrl => Updates().PageUrl(Updates().LastReport()?.RemoteTag);

    /// <summary>把状态行按当前存档重算一遍（进页面时、每次检查之后各走一次）。</summary>
    public void RefreshUpdateStatus()
    {
        UpdateStatus = BuildUpdateStatus();
        OnPropertyChanged(nameof(LocalVersionText));
        OnPropertyChanged(nameof(HasDownloadableRelease));
    }

    /// <summary>
    /// 那句状态：<b>开着就说清多久问一次、上一次问出了什么；关着就说清代价</b>（不再自己上网，
    /// 于是新版本只能靠他主动点那一下发现）。上一次的答案照 <see cref="UpdatePolicy.Describe"/> 原样念，
    /// 界面这里不另写一套措辞。
    /// </summary>
    private string BuildUpdateStatus()
    {
        var cadence = UpdateAutoCheckEnabled
            ? $"自动检查开着：距上次问满 {UpdatePolicy.ProbeGapHours} 小时就会自己问一次（联网才行）。"
            : "已关闭：程序不会自己上网问，新版本只能靠点「立即检查」发现。";
        var last = Updates().LastReport();
        return last is null
            ? cadence + " 这台机器上还没检查过。"
            : $"{cadence} 上次检查 {DateTimeText.Relative(last.CheckedUtc.ToUnixTimeSeconds(), DateTimeOffset.Now, TimeZoneInfo.Local)}：{last.Text}。";
    }

    /// <summary>
    /// 这一次检查没走到尽头（<see cref="UpdateService.CheckAsync"/> 已把绝大多数故障折成一句结局，
    /// 漏出来的这一种属意外）。界面必须当场说出来：留着上一次那句话会看着像"刚查过，还是那个答案"。
    /// </summary>
    public void ReportCheckCrash(Exception ex)
    {
        StarLog.Error("检查更新没走完", ex);
        UpdateStatus = $"这次检查没走完（{ex.GetType().Name}），原因在日志里。";
    }

    /// <summary>「打开下载页」那句返回值是"系统有没有找到能处理它的程序"，不接住它就是一次看不见的点击。</summary>
    public void ShowOpenReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return;
        UpdateStatus = reason;
    }

    /// <summary>
    /// 回灌只读那两格：<b>不因为"显示"而去起停定时器、也不落盘</b>（否则每次打开设置页都把巡查表收掉再起，
    /// 与自动备份、护眼那两条同一口径）。
    /// </summary>
    public void BackfillUpdateState()
    {
        _suppressUpdateApply = true;
        UpdateAutoCheckEnabled = Safe(() => _settings.LoadUpdateState().AutoCheckEnabled, true, "自动检查更新");
        _suppressUpdateApply = false;
        RefreshUpdateStatus();
    }
}
