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
/// SettingsStore 的这一段——屏幕画布那组：总开关与「截图带不带画布」。
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
}
