#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Hotkeys;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——护眼/休息提醒那组：开关、间隔、强制与全屏时暂缓。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    // ────────── 护眼 / 休息提醒（批次 WA）──────────

    /// <summary>总开关（<b>默认关</b>）。关时不建定时器、不探前台窗口、屏幕上不会出现任何遮罩。</summary>
    public bool LoadEyeRestEnabled() => Load() is { } d && d.EyeRestEnabled == true;

    /// <summary>间隔（分钟）。缺省与非法值都走 <c>EyeRestPolicy</c> 的回落，界面上看到的与真用到的是同一个数。</summary>
    public int LoadEyeRestIntervalMinutes()
        => StarMark.Core.Health.EyeRestPolicy.ClampInterval(
            Load() is { } d
                ? d.EyeRestIntervalMinutes ?? StarMark.Core.Health.EyeRestPolicy.DefaultIntervalMinutes
                : StarMark.Core.Health.EyeRestPolicy.DefaultIntervalMinutes);

    /// <summary>强制模式（<b>默认关</b>：刚开护眼就吃一次锁屏是惊吓）。</summary>
    public bool LoadEyeRestEnforced() => Load() is { } d && d.EyeRestEnforced == true;

    /// <summary>全屏让路（默认开）。</summary>
    public bool LoadEyeRestDeferOnFullscreen() => Load() is not { } d || d.EyeRestDeferOnFullscreen != false;

    /// <summary>
    /// 四项一次落盘：这四条说的是同一件事（怎么提醒），分开写就是"改了间隔但没改开关"这类半套状态的来源，
    /// 而且设置页的自动保存按 350 ms 节奏整档读写，一次写完比四次省（P-43 同一口径）。
    /// </summary>
    public void SaveEyeRest(bool enabled, int intervalMinutes, bool enforced, bool deferOnFullscreen)
    {
        var d = Load() ?? new SettingsData();
        d.EyeRestEnabled = enabled;
        d.EyeRestIntervalMinutes = StarMark.Core.Health.EyeRestPolicy.ClampInterval(intervalMinutes);
        d.EyeRestEnforced = enforced;
        d.EyeRestDeferOnFullscreen = deferOnFullscreen;
        Save(d);
    }
}
