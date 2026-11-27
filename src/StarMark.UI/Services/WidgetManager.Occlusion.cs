#nullable enable
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的遮挡段（片281）：桌面被外来窗整屏盖住时，可见组件窗按隐藏同款宽限收掉、
/// 遮挡结束后分帧复原。DeskBox 那套 5 个 WinEvent 钩＋防抖不搬（P-302 之后不新增原生钩机器），
/// 改成 2 秒常转轮询：宽限是分钟量级，2 秒粒度的感知成本在账上为零，复原延迟 ≤2 秒可接受。
/// <para>本段 <b>不引 <c>WidgetReclaimPolicy</c></b>——能不能收的判据只在巡查那一处问
/// （<c>WidgetManager.Lifecycle.ReclaimHiddenWindows</c>），这里只负责"看见遮挡／标记遮挡／复原"三件事。</para>
/// <para>与 DeskBox 的有意分野：①它感知到遮挡只停 ticker 不关窗（它没有那 80 MB 的问题），
/// 我们只有关窗一条腿，所以判据之外必须带复原；②z-order 模拟段不搬——
/// StarMark 组件窗挂桌面图标层，永远在应用窗之下，"自家窗护住屏"的推断在这里不成立。</para>
/// </summary>
public sealed partial class WidgetManager
{
    /// <summary>有组件窗活着就让遮挡轮询转起来（幂等：表建过就不再建）。UI 线程调用。</summary>
    private void EnsureOcclusionPolling()
    {
        if (_occlusionTimer is not null) return;
        _occlusionTimer = BuildOcclusionTimer();
        _occlusionTimer.Start();
    }

    private DispatcherQueueTimer BuildOcclusionTimer()
    {
        // 与隐藏回收那张一次性表不同，这张常转：遮挡的"开始/结束"没有事件可听
        // （P-302 后不新增原生钩），只能按 PollInterval 的粒度去问桌面。
        var t = Ui().CreateTimer();
        t.IsRepeating = true;
        t.Interval = DesktopOcclusionPolicy.PollInterval;
        t.Tick += (_, _) => OnOcclusionPoll();
        return t;
    }

    /// <summary>每 2 秒一轮：看桌面 → 标/解遮挡 → 复原已收掉的。单轮整体兜错：一颗异常窗不许把常转的巡查打死。</summary>
    private void OnOcclusionPoll()
    {
        if (_windows.Count == 0) return;
        try
        {
            var occluded = EvaluateOcclusionNow();
            ApplyOcclusion(occluded);
            _ = RestoreOccludedReclaimedAsync(occluded);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[内存] 遮挡巡查失败（下一轮照常）：{ex.Message}");
        }
    }

    // ───────────────────────── 看见遮挡 ─────────────────────────

    /// <summary>
    /// 评估此刻哪些屏被整屏盖住。前台是自家窗/桌面壳＝用户正看着桌面，直接空集（省一轮枚举）；
    /// 独占全屏/演示模式（3|4 口径，不含 BUSY）时前台所在屏直接判被盖。
    /// </summary>
    private IReadOnlySet<long> EvaluateOcclusionNow()
    {
        var foreground = WindowInterop.GetForegroundWindow();
        long foregroundMonitorKey = 0;
        if (foreground != IntPtr.Zero)
        {
            var root = WindowInterop.GetAncestor(foreground, WindowInterop.GA_ROOT);
            if (root == IntPtr.Zero) root = foreground;
            if (DesktopOcclusionInterop.IsOwnProcessWindow(root)
                || DesktopOcclusionPolicy.IsDesktopShellClass(WindowInterop.GetClassName(root)))
                return new HashSet<long>();
            var fgMonitor = WindowInterop.MonitorFromWindow(root, DesktopOcclusionInterop.MONITOR_DEFAULTTONULL);
            foregroundMonitorKey = fgMonitor.ToInt64();
        }

        var monitors = new Dictionary<long, MonitorGeometry>();
        var candidates = new List<OccluderCandidate>();
        WindowInterop.EnumWindows((hwnd, _) =>
        {
            CollectOcclusionCandidate(hwnd, monitors, candidates);
            return true;
        }, IntPtr.Zero);

        return DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            monitors.Values.ToList(),
            candidates,
            foregroundIsOwnOrDesktopShell: false,
            DesktopOcclusionInterop.IsExclusiveFullscreenOrPresentationActive(),
            foregroundMonitorKey);
    }

    /// <summary>
    /// 枚举里挑出可能盖住整屏的窗（DeskBox 的排除名单照搬）：不可见/最小化/cloaked 的不算，
    /// 自家窗不算（挂桌面图标层，永远盖不住屏），鼠标穿透与近透明浮层不算，
    /// 工具窗/不抢焦点的辅助窗不算，壳类名不算。
    /// </summary>
    private static void CollectOcclusionCandidate(IntPtr hwnd,
        Dictionary<long, MonitorGeometry> monitors, List<OccluderCandidate> candidates)
    {
        if (!WindowInterop.IsWindowVisible(hwnd) || DesktopOcclusionInterop.IsIconic(hwnd)) return;
        if (DesktopOcclusionInterop.IsWindowCloaked(hwnd)) return;
        if (DesktopOcclusionInterop.IsOwnProcessWindow(hwnd)) return;
        var exStyle = WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64();
        if ((exStyle & WindowInterop.WS_EX_TRANSPARENT) != 0) return;   // 鼠标穿透窗
        if ((exStyle & WindowInterop.WS_EX_LAYERED) != 0
            && DesktopOcclusionInterop.GetLayeredAlpha(hwnd) <= 16) return;   // 近透明浮层（DeskBox 口径 alpha≤16≈没盖）
        if (((exStyle & WindowInterop.WS_EX_TOOLWINDOW) != 0 && (exStyle & DesktopOcclusionInterop.WS_EX_APPWINDOW) == 0)
            || ((exStyle & WindowInterop.WS_EX_NOACTIVATE) != 0 && (exStyle & DesktopOcclusionInterop.WS_EX_APPWINDOW) == 0))
            return;   // 工具窗 / 不抢焦点的辅助窗
        var className = WindowInterop.GetClassName(hwnd);
        if (DesktopOcclusionPolicy.IsExcludedShellClass(className)) return;
        if (!DesktopOcclusionInterop.TryGetWindowBounds(hwnd, out var rect)) return;
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
        var hMonitor = WindowInterop.MonitorFromWindow(hwnd, DesktopOcclusionInterop.MONITOR_DEFAULTTONULL);
        if (hMonitor == IntPtr.Zero) return;
        var key = hMonitor.ToInt64();
        if (!monitors.ContainsKey(key))
        {
            if (!DesktopOcclusionInterop.TryGetMonitorGeometry(hMonitor, out var geometry)) return;
            monitors[key] = geometry;
        }
        candidates.Add(new OccluderCandidate(
            key, DesktopOcclusionInterop.IsZoomed(hwnd), className,
            rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    // ───────────────────────── 标/解遮挡 ─────────────────────────

    /// <summary>
    /// 把评估结果落到每颗窗上：所在屏被盖住的<b>可见</b>窗起遮挡钟（隐藏窗的宽限由隐藏钟单独管，
    /// 两钟并存在巡查里取 max）；屏重新露出来的清钟。刚起钟时排一次巡查——复用隐藏那张一次性表
    /// （<c>ArmOcclusionReclaim</c> 在 Lifecycle 段，判据的钟表全在那边）。
    /// </summary>
    private void ApplyOcclusion(IReadOnlySet<long> occludedMonitors)
    {
        foreach (var (id, window) in _windows.ToList())
        {
            long key;
            try
            {
                key = WindowInterop.MonitorFromWindow(WindowInterop.GetHwnd(window),
                    DesktopOcclusionInterop.MONITOR_DEFAULTTONULL).ToInt64();
            }
            catch { continue; }   // 窗口正在关闭，这一轮跳过它
            if (occludedMonitors.Contains(key))
            {
                if (!window.IsVisible || window.IsOccluded) continue;
                window.SetOccluded(true, key);
                StarLog.Info($"[内存] {WidgetStorage.KindTitle(window.Kind)} 所在屏被整屏窗口盖住，开始计宽限");
                ArmOcclusionReclaim();
            }
            else if (window.IsOccluded)
            {
                window.SetOccluded(false);
            }
        }
    }

    // ───────────────────────── 复原 ─────────────────────────

    /// <summary>
    /// 遮挡结束后分帧复原被收掉的窗：一颗一拍（与启动恢复同款节奏），
    /// 只复原"收窗那一刻所在屏已经露出来"的那些——另一块屏还被盖着的那批继续等。
    /// </summary>
    private async Task RestoreOccludedReclaimedAsync(IReadOnlySet<long> occludedMonitors)
    {
        if (_occludedReclaimed.Count == 0) return;
        var toRestore = new List<string>();
        foreach (var (id, key) in _occludedReclaimed.ToList())
            if (!occludedMonitors.Contains(key)) toRestore.Add(id);
        if (toRestore.Count == 0) return;
        foreach (var id in toRestore) _occludedReclaimed.Remove(id);
        StarLog.Info($"[内存] 整屏遮挡结束：分帧复原 {toRestore.Count} 颗被收掉的组件窗");
        foreach (var id in toRestore)
        {
            await OnUiAsync(() => ShowInternal(id));
            // 复原与启动同款分帧：N 颗重建不占死 UI 线程（片243 起的口径）
            await Task.Delay(WidgetStartupPolicy.RevealIntervalMs);
        }
    }
}
