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
using StarMark.Core.Canvas;   // 那块圆的半径：上下限与兜底只认 CursorCircle 一份
using StarMark.Integrations.Weather;
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——屏幕画布那组：总开关、「截图带不带画布」与光标那块圆的半径。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>屏幕画布总开关（<b>默认开</b>：这是一条做完了的功能，"没表过态"不等于"关过"）。</summary>
    public bool LoadCanvasEnabled() => Load() is not { } d || d.CanvasEnabled != false;

    public void SaveCanvasEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.CanvasEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 截图时画布算不算画面的一部分（<b>默认开</b>：从没表过态不等于"想让画布隐形"）。
    /// 关掉之后 <c>ScreenshotService.Start</c> 抓那一帧前先把玻璃收起来。
    /// 只影响"截屏抓到的那一帧"，不影响画布工具条上「贴图/复制/存图」那三条——那三条就是要把笔迹留下。
    /// </summary>
    public bool LoadCanvasInScreenshots() => Load() is not { } d || d.CanvasInScreenshots != false;

    public void SaveCanvasInScreenshots(bool include)
    {
        var d = Load() ?? new SettingsData();
        d.CanvasInScreenshots = include;
        Save(d);
    }

    /// <summary>
    /// 光标那块圆的半径（<b>DIP</b>，默认 160＝批次 S4-⑥ 定下并被真机接受的那个值）。
    /// <para>读侧也夹一次：档里那个数可能是手改过的、也可能是别处写进去的。<b>合法区间由
    /// <see cref="CursorCircle"/> 说</b>，这里不重抄一遍上下限（记忆 ⑧：同一判据两处各写一份必然分岔）。
    /// NaN／负数／0 若真的漏到换算处，症状是"屏幕上那块圆整个不见了"，而不是"圆小了一点"。</para>
    /// </summary>
    public double LoadCursorCircleRadiusDip()
        => CursorCircle.ClampRadiusDip(Load()?.CursorCircleRadiusDip ?? CursorCircle.DefaultRadiusDip);

    /// <summary>存盘侧同样夹一次：两处都夹，落进档里的就一定是能被读回来的那个数。</summary>
    public void SaveCursorCircleRadiusDip(double radiusDip)
    {
        var d = Load() ?? new SettingsData();
        d.CursorCircleRadiusDip = CursorCircle.ClampRadiusDip(radiusDip);
        Save(d);
    }
}
