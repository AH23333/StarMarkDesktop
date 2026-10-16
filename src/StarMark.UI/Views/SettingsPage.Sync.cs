#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using StarMark.Abstractions.I18n;
using StarMark.Core.Sync;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页「常规 → GitHub」那张卡上的<b>上一次同步</b>那一行（批次 WI-82 片2，P-212 走 (B) 的后半）。
/// <para><b>为什么屏幕上早就有那句话了还要这一行</b>：主窗那张提示卡会逐源报"谁、为什么"，但它 9 秒自动收、
/// 也随时被人点掉；而人读到「凭据无效或已过期」时通常已经跳到这张卡来换 Token 了——那里恰好是
/// 唯一没说到"哪一源没成"的地方（登记时那句「摆到设置页那颗同步区」就是这一格）。</para>
/// <para><b>读数只有一个主人</b>：<see cref="SyncCoordinator.LastSummary"/>，这一行⛔ 自己再数一遍失败名单、
/// ⛔ 自己拼一句"为什么"（那正是 P-212 撞出来的形状：Core 里写死的成品中文＋界面只报颗数）。
/// 汇总句与逐源句都还是 <see cref="LocMessage"/>，取词推迟到贴上去那一刻（机制 (C)），
/// 所以换语言时由 <see cref="Locale.Changed"/> 回到 <see cref="RepasteSyncStatus"/> 重念一遍。</para>
/// <para><b>从没跑完过一次 ⇒ 整行收着</b>：摆一句"还没同步过"要新造一句措辞，而"这一场没有话要说"
/// 本来就是这一格的正确答案；⛔ 在这里冒充"刚刚完成"，也⛔ 落盘去把上一场的句子带回新的一启
/// （那就要回答"跨重启那句话还算不算数"，而屏幕要的是这一场）。</para>
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>挂上"每一发跑完"那颗事件，并把进页时已有的读数先贴一次（与 <see cref="UnsubscribeSyncStatus"/> 成对）。</summary>
    private void SubscribeSyncStatus()
    {
        var coordinator = App.Services.GetRequiredService<SyncCoordinator>();
        // 本页可能被反复导航回来：先退再订，否则同一颗事件挂 N 份，一发同步重贴 N 次。
        coordinator.SyncCompleted -= OnSyncCompleted;
        coordinator.SyncCompleted += OnSyncCompleted;
        RepasteGithubSyncStatus(coordinator.LastSummary);
    }

    private void UnsubscribeSyncStatus()
        => App.Services.GetRequiredService<SyncCoordinator>().SyncCompleted -= OnSyncCompleted;

    private void OnSyncCompleted(SyncSummary summary)
    {
        // 事件发在 SyncAllAsync 的续跑线上（调用方是界面上的命令，通常＝UI 线程），但"通常"⛔ 是判据
        // ⇒ 自己 marshal；跨线程摸 TextBlock 在 WinUI 里是异常，不是"偶尔显示不出来"。
        DispatcherQueue.TryEnqueue(() => RepasteGithubSyncStatus(summary));
    }

    /// <summary>换语言那一刻：拿同一颗汇总重贴（⛔ 重新采集、⛔ 重新跑一次同步来"刷新措辞"）。</summary>
    private void RepasteSyncStatus()
        => RepasteGithubSyncStatus(App.Services.GetRequiredService<SyncCoordinator>().LastSummary);

    private void RepasteGithubSyncStatus(SyncSummary? summary)
    {
        if (summary is null)
        {
            LocText.SetMessage(GithubSyncStatusText, null);
            GithubSyncStatusText.Visibility = Visibility.Collapsed;
            return;
        }

        var head = LocMessage.Of("settings.general.github.lastSync", summary.Message());
        var body = summary.FailureLines() is { } failures
            // 行之间那根分隔符是<b>布局</b> ⇒ 走实参⛔ 写进表里（规则 #100，同 sync.src.lineJoin／clip.clean.lineJoin）。
            ? LocMessage.Of("settings.general.github.lastSyncJoin", head, failures, "\n")
            : head;
        LocText.SetMessage(GithubSyncStatusText, body);
        GithubSyncStatusText.Visibility = Visibility.Visible;
    }
}
