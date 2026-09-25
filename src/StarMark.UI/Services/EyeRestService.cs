#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Health;
using StarMark.UI.Helpers;
using StarMark.UI.Views;
using Windows.Graphics;

namespace StarMark.UI.Services;

/// <summary>
/// 护眼 / 休息提醒的编排：一张 15 秒的表看节拍，到点决定"弹／让路／安静重来一轮"，
/// 强制模式给每屏一张 20 秒暗幕，非强制模式只发一条托盘气泡。
/// <para>
/// 三条设计要点都是"只有真机才看得见"的坑，写在这里备忘：
/// ① <b>没到点不做任何 P/Invoke</b>（探前台窗口是每 15 秒的事，不是每秒的事——规格 §9 的性能条）；
/// ② <b>先重置节拍再展示</b>：遮罩窗建不起来（内存不足、显示器被拔掉）时宁可这次不提醒，
///    也不能每 15 秒重试一次变成轰炸；展示失败一律降级成气泡，不静默吞掉；
/// ③ <b>自己的窗不算"全屏应用"</b>：截图遮罩与暗幕本身是铺满屏幕的矩形，
///    把自己判成全屏会让护眼被自己延后掉（"自捕获顺序"那次教训的同一族）。
/// </para>
/// </summary>
public static class EyeRestService
{
    /// <summary>看节拍的间隔。正常永远在 2 分钟宽限期内被捞起，不会被"错过"那条规则吞掉。</summary>
    private static readonly TimeSpan WatchGap = TimeSpan.FromSeconds(15);

    /// <summary>倒数与淡出共用的表（250 ms 一档：倒数读数够顺，淡出够软，开销可忽略）。</summary>
    private static readonly TimeSpan RestGap = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan FadeSpan = TimeSpan.FromMilliseconds(800);

    private static SettingsStore? _settings;
    private static DispatcherQueue? _queue;
    private static DispatcherQueueTimer? _watch;
    private static DispatcherQueueTimer? _rest;
    private static EyeRestPolicy? _policy;

    private static readonly List<EyeRestOverlayWindow> _overlays = new();
    private static DateTimeOffset _restEndsAt;
    private static DateTimeOffset _fadeEndsAt;

    /// <summary>定时器是否挂着（设置页据此回报"开关开着但表没起来"）。</summary>
    public static bool IsRunning => _watch is not null;

    /// <summary>暗幕是否正在盖着屏幕。</summary>
    public static bool IsResting => _overlays.Count > 0;

    /// <summary>下一次大约几点提醒（关着时 null，供设置页显示）。</summary>
    public static DateTimeOffset? NextDueAt
    {
        get
        {
            if (_watch is null || _settings is null || _policy is null) return null;
            return _policy.DueAt(_settings.LoadEyeRestIntervalMinutes());
        }
    }

    /// <summary>
    /// 起表。必须在 UI 线程调用（<see cref="DispatcherQueueTimer"/> 属于主窗那条队列）。
    /// 重复调用只补引用不重建表；节拍起点<b>跨开关保留</b>——关掉再打开不该重新攒 45 分钟，
    /// 那一轮真到点了也会被宽限期规则安静重置（人已经离开过屏幕）。
    /// </summary>
    public static void Start(DispatcherQueue queue, SettingsStore settings)
    {
        _queue = queue;
        _settings = settings;
        _policy ??= new EyeRestPolicy(DateTimeOffset.Now);
        if (_watch is not null) return;

        _watch = queue.CreateTimer();
        _watch.Interval = WatchGap;
        _watch.Tick -= OnWatchTick;
        _watch.Tick += OnWatchTick;
        _watch.Start();
        StarLog.Info("[EyeRest] 护眼提醒已启动");
    }

    /// <summary>
    /// 停表并<b>立刻收幕</b>："我已经把护眼关掉了，屏幕还黑着"是这条链上最糟糕的一种收尾。
    /// </summary>
    public static void Stop()
    {
        if (_watch is null && _rest is null && _overlays.Count == 0) return;
        _watch?.Stop();
        _watch = null;
        _rest?.Stop();
        CloseOverlays();
        StarLog.Info("[EyeRest] 护眼提醒已停止");
    }

    private static void OnWatchTick(DispatcherQueueTimer sender, object args)
    {
        if (_settings is not { } settings || _policy is not { } policy) return;

        // 开关被从别处关掉（手改 settings.json / 另一处入口）：安静停表，不留下"关不掉"的表
        if (!settings.LoadEyeRestEnabled())
        {
            Stop();
            return;
        }

        var interval = settings.LoadEyeRestIntervalMinutes();
        var now = DateTimeOffset.Now;
        if (now < policy.DueAt(interval)) return;                 // 没到点：一个 P/Invoke 都不做

        var defer = settings.LoadEyeRestDeferOnFullscreen();
        var decision = policy.Decide(now, interval, defer, defer && IsForegroundFullscreen());
        if (decision != EyeRestPolicy.Decision.Due)
        {
            if (decision == EyeRestPolicy.Decision.Deferred)
                StarLog.Info("[EyeRest] 前台是全屏应用，本轮让路，稍后再探");
            return;
        }

        policy.Reset(now);                                        // 先重置再展示（见类型注释 ②）
        BeginRest(interval, settings.LoadEyeRestEnforced());
    }

    /// <summary>
    /// 演一次提醒：<b>开着强制就盖幕布，否则只发一条气泡</b>。回报"屏幕上真的有东西出来没有"——
    /// 到点那条路不需要这个答案（它只管演），但设置页的「试一试」必须能区分"演过了"和"什么都没发生"。
    /// </summary>
    private static bool BeginRest(int intervalMinutes, bool enforced)
    {
        if (_overlays.Count > 0)
        {
            // 上一轮的幕布还盖着（连按两次"试一试"）：叠第二层只会让屏幕更黑，而且不解释任何事
            StarLog.Info("[EyeRest] 幕布还盖着屏，忽略这一次的重复触发");
            return false;
        }

        if (!enforced)
        {
            var shown = Notify("该休息一下了", RestBody(intervalMinutes));
            StarLog.Info($"[EyeRest] 气泡提醒已发出（间隔 {intervalMinutes} 分钟，非强制）");
            return shown;
        }

        List<(string Device, RectInt32 Bounds, double Scale)> monitors;
        try
        {
            monitors = WindowInterop.ListMonitors().ToList();
        }
        catch (Exception ex)
        {
            StarLog.Error("[EyeRest] 取显示器列表失败，降级为气泡提醒", ex);
            return Notify("该休息一下了", RestBody(intervalMinutes));
        }
        if (monitors.Count == 0)
        {
            StarLog.Warn("[EyeRest] 系统没报告任何显示器，降级为气泡提醒");
            return Notify("该休息一下了", RestBody(intervalMinutes));
        }

        try
        {
            foreach (var monitor in monitors)
            {
                var overlay = new EyeRestOverlayWindow(monitor);
                overlay.SetMessage(intervalMinutes);
                _overlays.Add(overlay);
            }
        }
        catch (Exception ex)
        {
            CloseOverlays();                                       // 建到一半失败也不能留几屏暗幕挂在那儿
            StarLog.Error("[EyeRest] 遮罩窗没建起来，降级为气泡提醒", ex);
            return Notify("该休息一下了", RestBody(intervalMinutes));
        }

        _restEndsAt = DateTimeOffset.Now + TimeSpan.FromSeconds(EyeRestPolicy.RestSeconds);
        _fadeEndsAt = _restEndsAt + FadeSpan;
        if (_queue is null)
        {
            CloseOverlays();
            StarLog.Warn("[EyeRest] 主窗队列不见了，幕布没人倒数：已收幕");
            return false;
        }
        _rest ??= _queue.CreateTimer();
        _rest.Interval = RestGap;
        _rest.Tick -= OnRestTick;
        _rest.Tick += OnRestTick;
        _rest.Start();
        RestOnce();                                                // 读数先写上，别空一帧才出现"20"
        StarLog.Info($"[EyeRest] 强制休息开始：{EyeRestPolicy.RestSeconds} 秒 × {_overlays.Count} 屏");
        return true;
    }

    /// <summary>
    /// 设置页的「试一试」：按<b>当前设置</b>原样演一次（开着强制＝盖 20 秒幕布，否则＝一条气泡）。
    /// <b>不动节拍</b>——演一次不等于真休息过一轮，下一次该几点还是几点。
    /// 没有这条出口，用户只能等满间隔才知道自己配的到底是什么效果，而"等 15 分钟验证一个开关"
    /// 等于没给验证路径。
    /// </summary>
    public static bool Preview()
    {
        if (_settings is not { } settings)
        {
            StarLog.Info("[EyeRest] 护眼未开启（节拍表没挂上），试一试没有可演的东西");
            return false;
        }
        return BeginRest(settings.LoadEyeRestIntervalMinutes(), settings.LoadEyeRestEnforced());
    }

    /// <summary>四处出口共用一条文案：差别只在"用什么形式出现"，内容不一样会让人以为是两件事。</summary>
    private static string RestBody(int intervalMinutes)
        => $"已连续工作约 {intervalMinutes} 分钟。看看远处，放松一下眼睛。";

    private static void OnRestTick(DispatcherQueueTimer sender, object args) => RestOnce();

    private static void RestOnce()
    {
        if (_overlays.Count == 0)
        {
            _rest?.Stop();
            return;
        }
        var now = DateTimeOffset.Now;
        if (now < _restEndsAt)
        {
            var left = (int)Math.Ceiling((_restEndsAt - now).TotalSeconds);
            ForEachOverlay(overlay => overlay.SetSecondsLeft(Math.Max(1, left)));
            return;
        }
        if (now >= _fadeEndsAt)
        {
            _rest?.Stop();
            CloseOverlays();
            StarLog.Info("[EyeRest] 休息结束，节拍已重新起算");
            return;
        }
        var k = Math.Clamp((_fadeEndsAt - now) / FadeSpan, 0d, 1d);
        ForEachOverlay(overlay => overlay.ApplyOpacity(k));
    }

    /// <summary>
    /// 暗幕可能被外力收掉（拔掉显示器、注销、XAML 岛自己崩一角）——追着已死的窗报错会把这张定时器带崩，
    /// 所以这里断供就收幕：宁可这一轮少倒数几秒，也不能让"护眼"变成"弹一次崩一次"。
    /// </summary>
    private static void ForEachOverlay(Action<EyeRestOverlayWindow> paint)
    {
        try
        {
            foreach (var overlay in _overlays) paint(overlay);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[EyeRest] 暗幕已不可用，本轮提前收幕：{ex.Message}");
            _rest?.Stop();
            CloseOverlays();
        }
    }

    private static void CloseOverlays()
    {
        foreach (var overlay in _overlays)
        {
            try { overlay.CloseOverlay(); }
            catch (Exception ex) { StarLog.Warn($"[EyeRest] 暗幕没收掉（窗可能已被外部关闭）：{ex.Message}"); }
        }
        _overlays.Clear();
    }

    /// <summary>
    /// 托盘气泡优先（主窗收进托盘时也能看见），发不出去再用主窗的提示条兜底，
    /// 两条都不成就老实写日志——"提醒没弹出来"和"弹了但用户没看见"是两件事，日志里要分得出来。
    /// 回报有没有真的出现在屏幕上（<see cref="Preview"/> 据此说话）。
    /// </summary>
    private static bool Notify(string title, string body)
    {
        var channel = "日志";
        try
        {
            if (App.MainWindow?.TryShowTrayNotification(title, body) == true) channel = "托盘气泡";
        }
        catch (Exception ex) { StarLog.Warn($"[EyeRest] 托盘气泡没送出去：{ex.Message}"); }

        if (channel == "日志")
        {
            try
            {
                var window = App.MainWindow;
                if (window is not null)
                {
                    window.ShowNotice(title, body);
                    channel = "主窗提示条";
                }
            }
            catch (Exception ex) { StarLog.Warn($"[EyeRest] 主窗提示条也没能显示：{ex.Message}"); }
        }
        StarLog.Info($"[EyeRest] 提醒已发出（走的是{channel}）：{title}：{body}");
        return channel != "日志";
    }

    /// <summary>
    /// 前台窗口是否铺满某一块屏。<b>探不到就按"不是全屏"处理</b>：
    /// 一次 P/Invoke 失败就把护眼永久废掉（每次都让路）比多弹一次严重得多。
    /// </summary>
    private static bool IsForegroundFullscreen()
    {
        try
        {
            var foreground = WindowInterop.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            // 自家窗口不算（截图遮罩 / 暗幕 / 贴图都是铺满屏的矩形）
            WindowInterop.GetWindowThreadProcessId(foreground, out var pid);
            if (pid == Environment.ProcessId) return false;

            if (!WindowInterop.GetWindowRect(foreground, out var rect)) return false;
            var win = new IntRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (win.IsEmpty) return false;

            return WindowInterop.ListMonitors().Any(monitor =>
                EyeRestPolicy.CoversScreen(win,
                    new IntRect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height)));
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[EyeRest] 前台全屏判定失败，按非全屏处理：{ex.Message}");
            return false;
        }
    }
}
