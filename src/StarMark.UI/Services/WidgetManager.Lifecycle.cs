#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——一扇组件窗的一生：建、显、临时收、真关，以及每步之后要不要落盘（persist 那个旗标就在这条链上）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

    /// <summary>在 UI 线程上记录调度器（MainWindow 构造时调用）。</summary>
    public void Initialize(DispatcherQueue ui) => _ui = ui;

    /// <summary>新增一个指定类型的组件实例（可重复添加同类型）。</summary>
    public Task AddInstanceAsync(WidgetKind kind)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var cfg = CreateInstanceConfig(data, kind);
            data.Instances.Add(cfg);
            _storage.Save(data);
            InstancesChanged?.Invoke();   // 设置页立即出现新实例行
            ShowInternal(cfg.Id);
        });

    /// <summary>移除指定实例（标题栏 ✕ / 设置页移除 / 菜单"移除本组件"）。</summary>
    public Task RemoveInstanceAsync(string id)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == id);
            if (inst is not null)
            {
                data.Instances.Remove(inst);
                _storage.Save(data);
                InstancesChanged?.Invoke();   // 设置页立即移除该实例行
            }
            CloseInternal(id, persist: false);
        });

    /// <summary>移除实例的别名（与旧 API 同名，便于调用处过渡）。</summary>
    public Task RemoveAsync(string id) => RemoveInstanceAsync(id);

    /// <summary>显示指定实例（临时隐藏后恢复；实例不存在则忽略）。</summary>
    public Task ShowAsync(string id) => OnUiAsync(() => ShowInternal(id));

    /// <summary>临时隐藏（实例保留）。</summary>
    public Task HideTemporaryAsync(string id) => OnUiAsync(() =>
    {
        if (_windows.TryGetValue(id, out var w)) HideTemporaryAll(new[] { w });
    });

    /// <summary>新建实例配置：在同类已有实例基础上做级联偏移，避免叠在一起。</summary>
    private static WidgetInstanceConfig CreateInstanceConfig(WidgetStoreData data, WidgetKind kind)
    {
        var kindIndex = (int)kind;
        var sameKind = data.Instances.Count(i => i.Kind == kind);
        return new WidgetInstanceConfig
        {
            Kind = kind,
            // 新建实例默认采用描述符推荐的外壳模式（目前均为 Standard；未来个别组件可声明 Compact/Hidden）
            ChromeMode = WidgetRegistry.Default.Get(kind).DefaultChromeMode,
            X = 120 + kindIndex * 28 + sameKind * 26,
            Y = 90 + kindIndex * 28 + sameKind * 26,
            Width = WidgetStorage.DefaultWidth(kind),
            Height = WidgetStorage.DefaultHeight(kind),
        };
    }

    /// <summary>
    /// 除指定实例外的**可见**组件窗口。供 Ctrl+拖动协同移动（对齐 DeskBox CoordinatedMove）招募参与者。
    /// 只在 UI 线程调用。
    /// </summary>
    public IReadOnlyList<WidgetWindow> VisibleWindowsExcept(string excludeInstanceId) =>
        _windows.Values
            .Where(w => w.InstanceId != excludeInstanceId && w.IsVisible && !w.IsDragBusy)
            .ToList();

    /// <summary>
    /// 启动时恢复组件：若存在默认布局则自动套用它（恢复「最后一次选择的布局」），否则逐个显示全部实例。
    /// <para>
    /// <paramref name="loadOnStartup"/>＝设置页那颗「开机自动加载组件」（用户裁决：取舍给他自己选，默认开＝今天的观感）。
    /// 关掉时<b>一颗都不建</b>，而不是"先建再收"——后者退不回来是真机量出来的（报告 §二百零九：私有内存 0 变化）。
    /// </para>
    /// </summary>
    public Task RestoreOnStartupAsync(bool loadOnStartup = true) => OnUiAsync(() =>
    {
        var data = _storage.Load();

        if (!loadOnStartup)
        {
            // 这句日志不能省：开机内存突然少了几十兆，第一种猜测会是"组件功能坏了"，而不是"你上周关过那个开关"。
            StarLog.Info($"[内存] {WidgetStartupPolicy.DescribeSkipped(data.Instances.Count)}");
            StarMark.Abstractions.StartupProfile.Mark($"桌面组件未加载（{data.Instances.Count} 个实例在册）");
            return;
        }

        var layout = !string.IsNullOrEmpty(data.DefaultLayoutId)
            ? _storage.FindLayout(data.DefaultLayoutId!)
            : null;

        if (layout is not null)
        {
            try
            {
                ApplyLayoutCore(data, layout);
                _storage.Save(data);     // 保证默认布局状态与磁盘一致
            }
            catch (Exception ex)
            {
                StarLog.Error("应用默认布局失败，回退为逐个显示实例", ex);
                foreach (var inst in data.Instances)
                {
                    try { ShowInternal(inst.Id); }
                    catch (Exception iex) { StarLog.Error($"恢复桌面组件失败 ({inst.Kind})", iex); }
                }
            }
        }
        else
        {
            foreach (var inst in data.Instances)
            {
                try { ShowInternal(inst.Id); }
                catch (Exception ex) { StarLog.Error($"恢复桌面组件失败 ({inst.Kind})", ex); }
            }
        }
        // 这一段是启动里最重的一块（建窗口＋套材质），单独计时才有"改完有没有变快"的对照
        StarMark.Abstractions.StartupProfile.Mark($"桌面组件恢复（{data.Instances.Count} 个实例）");
    });

    public Task ShutdownAllAsync() => OnUiAsync(() =>
    {
        CloseAll(_windows.Keys.ToList(), persist: true);
    });

    // ───────────────────────── 内部 ─────────────────────────

    /// <summary>
    /// 显示某实例。<b>这一层只做计时</b>：启动要建十几颗组件窗口，"每颗多贵"必须由日志说话，
    /// 而不是靠"约 -50~150 ms"这类猜（超过 30 ms 才写行，正常环境一条都不出）。
    /// </summary>
    private void ShowInternal(string id)
        => StarMark.Abstractions.StartupProfile.Measure($"显示组件窗口 {id}", () => ShowNow(id), logWhenMs: 30);

    private void ShowNow(string id)
    {
        // 窗口创建/显示必须整体兜错：这里的异常会顺着 UI 线程冒到点击处理，
        // 演变成未处理异常让整个应用卡死崩溃（历史事故：外观设置读取失败导致「全部显示」崩溃）。
        try
        {
            if (!_windows.TryGetValue(id, out var window))
            {
                var data = _storage.Load();
                var cfg = data.Instances.FirstOrDefault(i => i.Id == id);
                if (cfg is null) return;
                window = new WidgetWindow(id, cfg, _storage, _repo, this);
                _windows[id] = window;
            }
            window.Reveal();
        }
        catch (Exception ex)
        {
            StarLog.Error($"显示桌面组件失败 ({id})", ex);
        }
    }

    /// <summary>
    /// 关闭一个实例。<paramref name="persist"/> 决定"要不要先把屏幕上的几何留在盘上"：
    /// 移除实例（那一行已从存档里删掉）与应用快照（磁盘刚按快照写好，不许用旧几何盖回去）时必须 false，
    /// 退出应用时必须 true。<b>这个旗标曾经被完全忽略</b>——于是每个调用点都各写一趟整档，
    /// 而"该不该写"根本没人说了算。
    /// </summary>
    private void CloseInternal(string id, bool persist = true)
    {
        if (!_windows.Remove(id, out var window)) return;
        if (persist) PersistBoundsFor(new[] { window });
        window.Shutdown();
    }

    /// <summary>整批关闭：<b>几何一次落盘</b>，再逐个关窗（逐个 PersistBounds 时 N 个窗口＝N 趟整档读写）。</summary>
    private void CloseAll(IReadOnlyList<string> ids, bool persist)
    {
        if (persist)
        {
            var targets = new List<WidgetWindow>();
            foreach (var id in ids)
                if (_windows.TryGetValue(id, out var w)) targets.Add(w);
            PersistBoundsFor(targets);
        }
        foreach (var id in ids.ToList()) CloseInternal(id, persist: false);
    }

    /// <summary>整批临时隐藏：同样先一次性把几何写好，再逐个隐藏。</summary>
    private void HideTemporaryAll(IReadOnlyList<WidgetWindow> windows)
    {
        if (windows.Count == 0) return;
        PersistBoundsFor(windows);
        foreach (var w in windows) w.HideTemporary();
        // 隐藏的窗口不释放＝12 颗 80 MB 私有／771 句柄的来源（真机量在报告 §二百零八）。
        // 这里起一次性定时器，而不是每颗挂一张表：一次巡查就能把整群做完。
        ArmHiddenWindowReclaim();
    }

    /// <summary>
    /// 就地收起并<b>释放</b>全部组件窗——给「开机自动加载组件」关掉那一下用，不要求重启
    /// （"要用户重启"在本项目里按缺陷算）。
    /// <para>
    /// 与「全部隐藏」不是一件事，也不复用它的出口：隐藏只是 <c>SW_HIDE</c>，窗口与组合面都留着，
    /// 那 ≈8 MB/颗 一分不退（真机数在报告 §二百零九）；这里走既有的整批关闭出口——
    /// 几何一次落盘、实例留在档里，下次点亮按存档重建。
    /// </para>
    /// </summary>
    public Task CollapseAllAsync() => OnUiAsync(() =>
    {
        var ids = _windows.Keys.ToList();
        if (ids.Count == 0) return;
        CloseAll(ids, persist: true);
        StarLog.Info($"[内存] 开机自动加载已关：收起并释放 {ids.Count} 颗组件窗"
                     + $"（约 {WidgetStartupPolicy.EstimatedPrivateMb(ids.Count)} MB 私有），实例仍在档里，点亮即重建");
    });

    /// <summary>
    /// 给"持续隐藏满宽限期"的组件窗排一次回收。<b>不在这个时刻收</b>：藏起来又马上调出来是最常做的动作，
    /// 而重建一颗窗口实测 33–52 ms——宽限期（<see cref="WidgetReclaimPolicy.HiddenGrace"/>）就是给这个动作留的。
    /// </summary>
    private void ArmHiddenWindowReclaim()
    {
        var timer = _reclaimTimer ??= BuildReclaimTimer();
        // 每次新的隐藏都从头给满整段宽限期（"你刚动过，说明还要用"）。
        timer.Stop();
        timer.Interval = WidgetReclaimPolicy.HiddenGrace;
        timer.Start();
    }

    private DispatcherQueueTimer BuildReclaimTimer()
    {
        // 必须挂在 UI 线程的队列上：回调里要 Close 窗口，线程池定时器做不到这件事（内存门禁那张表就是线程池的）。
        var t = Ui().CreateTimer();
        t.IsRepeating = false;
        t.Tick += (_, _) => ReclaimHiddenWindows();
        return t;
    }

    /// <summary>
    /// 关掉"持续隐藏满宽限期、且这一类被点名可收"的组件窗。
    /// <para>哪一类能收<b>不写在这里</b>——判据只有 <see cref="WidgetReclaimPolicy"/> 一颗：
    /// 宿主这里再写一份"隐藏的都可以收"，就会在新增组件类型的那天悄悄丢掉用户的字。</para>
    /// <para>没收的每一颗都记一句原因。少了这句，"点了清理但内存没降"永远归不了因，
    /// 而那恰好是这一条最可能被问到的事。</para>
    /// </summary>
    private void ReclaimHiddenWindows()
    {
        var reclaimed = 0;
        TimeSpan? remaining = null;      // 还差多久才有下一轮可收的
        foreach (var (id, window) in _windows.ToList())
        {
            if (window.IsVisible) continue;
            var hiddenFor = window.HiddenFor();
            if (!WidgetReclaimPolicy.ShouldReclaim(window.Kind, hiddenFor))
            {
                if (hiddenFor < WidgetReclaimPolicy.HiddenGrace)
                {
                    var left = WidgetReclaimPolicy.HiddenGrace - hiddenFor;
                    if (remaining is null || left < remaining) remaining = left;
                }
                StarLog.Info($"[内存] 不收 {WidgetStorage.KindTitle(window.Kind)}：{WidgetReclaimPolicy.Reason(window.Kind, hiddenFor)}");
                continue;
            }
            // persist:false——几何在 HideTemporaryAll 那一刻已经整批落过盘，隐藏期间它不会自己变。
            // 这里再写一趟就是每轮回收 N 趟整档读写（AU 那批专门收掉过这件事，别再引回来）。
            CloseInternal(id, persist: false);
            reclaimed++;
            StarLog.Info($"[内存] 回收隐藏组件窗口 {WidgetStorage.KindTitle(window.Kind)}"
                + $"（已隐藏 {WidgetReclaimPolicy.Format(hiddenFor)}，关闭后在册 {_windows.Count} 颗）");
        }
        if (reclaimed > 0)
            StarLog.Info($"[内存] 本轮回收 {reclaimed} 颗隐藏组件窗口，在册 {_windows.Count} 颗");
        // 还有"藏得还不够久"的：按剩下的时间再排一次（加 1 秒余量，免得差几十毫秒空转一轮）。
        if (remaining is { } wait)
        {
            _reclaimTimer!.Stop();
            _reclaimTimer.Interval = wait + TimeSpan.FromSeconds(1);
            _reclaimTimer.Start();
        }
    }

    /// <summary>
    /// 一批窗口各自的几何<b>共用一次读档、最多一次落盘</b>。<see cref="WidgetStorage.Mutate"/> 存在的理由就在这里：
    /// <c>WidgetWindow</c> 自己 Load+Save 的话，N 个窗口就是 N 趟整档读写（隐藏/关闭/协同拖动都会踩到）。
    /// 一个窗口都没写动到（实例已移除）时一次盘都不写。
    /// </summary>
    private void PersistBoundsFor(IReadOnlyList<WidgetWindow> windows) =>
        _storage.Mutate(data =>
        {
            var wrote = false;
            foreach (var w in windows) wrote |= w.WriteBoundsInto(data);
            return wrote;
        });
}
