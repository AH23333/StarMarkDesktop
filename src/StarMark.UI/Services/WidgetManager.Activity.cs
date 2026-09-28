#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——跨窗动作：记活动、请求全局搜索、打开主窗或设置页。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

    /// <summary>记录一条用户主动活动（快捷入口等非 items 资源）。仓库不可用时静默跳过，绝不打断交互。</summary>
    private async Task LogActivityAsync(ActivityKind kind, string title, string? uri)
    {
        if (_repo is null) return;
        try { await _repo.LogActivityAsync(kind, null, title, uri, CancellationToken.None); }
        catch (Exception ex) { StarLog.Error($"记录快捷入口活动失败 ({kind})", ex); }
    }

    /// <summary>批量记录用户主动活动：<b>整批一次连接</b>（逐条记的话拖 N 个快捷入口就要开 N 次库、
    /// 数 N 遍活动总数、裁 N 遍环形缓冲）。仓库不可用时同样静默跳过，绝不打断交互。</summary>
    private async Task LogActivitiesAsync(ActivityKind kind, IReadOnlyList<(string Title, string Uri)> rows)
    {
        if (_repo is null || rows.Count == 0) return;
        try
        {
            var events = new List<ActivityDraft>(rows.Count);
            foreach (var (title, uri) in rows) events.Add(new ActivityDraft(kind, null, title, uri));
            await _repo.LogActivitiesAsync(events, CancellationToken.None);
        }
        catch (Exception ex) { StarLog.Error($"记录快捷入口活动失败 ({kind} ×{rows.Count})", ex); }
    }

    // ───────────────────────── 跨窗口动作 ─────────────────────────

    public void RequestGlobalSearch(string query) => GlobalSearchRequested?.Invoke(query);

    public void OpenMainWindow() => App.PresentMainWindow();

    public void OpenWidgetSettings() => App.PresentMainWindow(settings: true);
}
