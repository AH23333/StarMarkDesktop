#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——各组件自己的那份额外数据：外观覆盖、标题、计算器历史、时区、倒数日、专注与监控指标。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

    /// <summary>保存某实例的外观覆盖（材质/颜色/边框/圆角/文本缩放）。appearance 为 null 表示清除覆盖、回退全局。</summary>
    public Task SaveInstanceAppearanceAsync(string id, WidgetAppearanceOverride? appearance)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == id);
            if (inst is null) return;
            inst.Appearance = appearance;
            _storage.Save(data);
        });

    /// <summary>
    /// 重命名组件实例（右键菜单「重命名…」）。title 传 null / 空白表示恢复组件类型的默认标题。
    /// 持久化方式与其它每实例字段一致：重新载入 widgets.json → 改实例 → 落盘。
    /// </summary>
    public Task SaveInstanceTitleAsync(string id, string? title)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == id);
            if (inst is null) return;
            inst.Title = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
            _storage.Save(data);
        });

    /// <summary>
    /// 保存计算器某实例的历史带（整带替换）。与快捷入口同走实例配置，但<b>不发 LinksChanged 那类广播</b>：
    /// 历史只有产生它的那个窗口在用，广播会让同类型其它实例无谓重绘。
    /// </summary>
    public Task SaveCalcHistoryAsync(string instanceId, IReadOnlyList<CalcHistoryItem> history)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.CalcHistory = history.ToList();
            _storage.Save(data);
        });

    /// <summary>保存世界时钟点位（整表替换）。空表也照写：那是"用户删光了"，与"从没配过"必须可分辨。</summary>
    public Task SaveWorldClockZonesAsync(string instanceId, IReadOnlyList<WorldClockCity> cities)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.WorldClockZones = cities.ToList();
            _storage.Save(data);
        });

    /// <summary>保存倒计时列表（整表替换）。到点提醒键写在条目里，因此刷新循环触发提醒后也要走这里落一次盘。</summary>
    public Task SaveCountdownsAsync(string instanceId, IReadOnlyList<CountdownItem> items)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.Countdowns = items.ToList();
            _storage.Save(data);
        });

    /// <summary>
    /// 保存闹钟列表（整表替换，理由同倒计时：到点标记写在条目里，弹过之后必须落盘，否则重启会再弹一次）。
    /// 空表照写——那是"用户把闹钟都删了"，与"从没配过"要能分辨（同 <see cref="SaveWorldClockZonesAsync"/> 那条口径）。
    /// </summary>
    public Task SaveAlarmsAsync(string instanceId, IReadOnlyList<AlarmItem> alarms)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.Alarms = alarms.ToList();
            _storage.Save(data);
        });

    /// <summary>保存番茄钟时长设置（只存时长；进行中的轮次刻意不持久化）。</summary>
    public Task SaveFocusConfigAsync(string instanceId, FocusTimerConfig config)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.Focus = new FocusTimerConfig { FocusMinutes = config.FocusMinutes, RestMinutes = config.RestMinutes };
            _storage.Save(data);
        });

    /// <summary>保存系统监控的指标勾选（整数形态的旗标集合）。0 照写：那是"用户全取消了"，与 null＝没配过必须可分辨。</summary>
    public Task SaveMonitorMetricsAsync(string instanceId, int wire)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.MonitorMetrics = wire;
            _storage.Save(data);
        });

    /// <summary>
    /// 从别处（待办条目右键「开始专注」）拉起番茄钟：落到首个番茄钟实例，无则新建并显示。
    /// <b>成败与原因由那个组件自己显示在界面上</b>（本窗口被唤起到前台，提示文本就在里面）——
    /// 返回值只用于日志，不要在调用点再叠一层弹窗：两处都说一遍会互相矛盾。
    /// </summary>
    public Task<bool> StartFocusAsync(string? title) => OnUiAsync(() =>
    {
        var data = _storage.Load();
        var inst = data.Instances.FirstOrDefault(i => i.Kind == WidgetKind.Focus);
        if (inst is null)
        {
            inst = CreateInstanceConfig(data, WidgetKind.Focus);
            data.Instances.Add(inst);
            _storage.Save(data);
            InstancesChanged?.Invoke();
        }
        ShowInternal(inst.Id);   // 窗口没有就创建、隐藏的就唤起到前台
        var started = _windows.TryGetValue(inst.Id, out var win)
            && win.FindContent<StarMark.UI.Views.FocusTimerWidget>()?.StartFromTodo(title) == true;
        if (!started) StarLog.Info("番茄钟未能开始本轮（组件窗口创建失败，或本轮已在计时）");
        return started;
    });
}
