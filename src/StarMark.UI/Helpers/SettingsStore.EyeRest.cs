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
/// SettingsStore 的这一段——护眼/休息提醒那组：开关、间隔、提醒形式（气泡／暗幕／强制）与全屏时暂缓。
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

    /// <summary>
    /// 提醒形式（<b>默认暗幕＋可点一下提前结束</b>）。批次 RS 之前这里是两个布尔：强制开＝暗幕且不能退出，
    /// 强制关＝只有气泡——于是"想看看暗幕什么效果"必须先接受"被扣 20 秒"。
    /// <para>存档里那个整数<b>只认 0/1/2</b>（<c>IsKnownNotice</c>）：认不得的一律回默认档，<b>不夹到最近一端</b>——
    /// 万一将来多了第四档，旧版本读到它若静默变成"强制不可跳"，就是把一处数据损坏放大成"关掉所有退出出口"。</para>
    /// </summary>
    public StarMark.Core.Health.EyeRestNotice LoadEyeRestNotice()
        => StarMark.Core.Health.EyeRestPolicy.ClampNotice(Load()?.EyeRestNotice);

    /// <summary>全屏让路（默认开）。</summary>
    public bool LoadEyeRestDeferOnFullscreen() => Load() is not { } d || d.EyeRestDeferOnFullscreen != false;

    /// <summary>
    /// 四项一次落盘：这四条说的是同一件事（怎么提醒），分开写就是"改了间隔但没改开关"这类半套状态的来源，
    /// 而且设置页的自动保存按 350 ms 节奏整档读写，一次写完比四次省（P-43 同一口径）。
    /// 间隔与提醒形式<b>进门就夹</b>，不指望调用方先兜一遍（与 <see cref="LoadEyeRestIntervalMinutes"/> 两头各夹一次，
    /// 漏一头就会出现"界面显示的与实际用的不是同一个数"）。
    /// </summary>
    public void SaveEyeRest(bool enabled, int intervalMinutes, StarMark.Core.Health.EyeRestNotice notice, bool deferOnFullscreen)
    {
        var d = Load() ?? new SettingsData();
        d.EyeRestEnabled = enabled;
        d.EyeRestIntervalMinutes = StarMark.Core.Health.EyeRestPolicy.ClampInterval(intervalMinutes);
        d.EyeRestNotice = (int)StarMark.Core.Health.EyeRestPolicy.ClampNotice(notice);
        d.EyeRestDeferOnFullscreen = deferOnFullscreen;
        Save(d);
    }
}
