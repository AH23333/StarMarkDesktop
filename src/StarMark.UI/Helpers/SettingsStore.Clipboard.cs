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
/// SettingsStore 的这一段——剪贴板历史与图片采集那组：两个开关与两个上限。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>
    /// 内置剪贴板历史总开关（<b>默认关</b>）。关闭时连监听窗口都不创建——不读剪贴板、不落盘。
    /// </summary>
    public bool LoadClipboardHistoryEnabled() => Load() is { } d && d.ClipboardHistoryEnabled == true;

    public void SaveClipboardHistoryEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.ClipboardHistoryEnabled = enabled;
        Save(d);
    }

    // ────────── 剪贴板图片（批次 ClipIMG-P1-1d）──────────
    //
    // 这一组的读侧一律过一遍 ClipboardPolicy 的夹取口径：这个 JSON 文件用户可以手改，
    // 而"上限"被改成 0 或负数在语义上不是"关掉"，是"每记一条就删一条"——那种坏值必须在门口挡掉。
    // 写侧同样夹一次（存进去的就是能用的值，别让坏值在文件里过冬）。

    /// <summary>图片采集分开关（<b>默认关</b>，决议 §4）。</summary>
    public bool LoadClipboardImageEnabled() => Load() is { } d && d.ClipboardImageEnabled == true;

    public void SaveClipboardImageEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.ClipboardImageEnabled = enabled;
        Save(d);
    }

    /// <summary>图片条数上限（缺省/坏值 → 默认 200，并夹进 10–2000）。</summary>
    public int LoadClipboardImageMaxEntries()
        => StarMark.Abstractions.Clipboard.ClipboardPolicy.ClampImageMaxEntries(
            Load()?.ClipboardImageMaxEntries ?? StarMark.Abstractions.Clipboard.ClipboardPolicy.DefaultImageMaxEntries);

    /// <summary>文本条数上限（缺省/坏值 → 默认 500，并夹进 10–10000；500 就是改之前写死的那个常量）。</summary>
    public int LoadClipboardTextMaxEntries()
        => StarMark.Abstractions.Clipboard.ClipboardPolicy.ClampTextMaxEntries(
            Load()?.ClipboardTextMaxEntries ?? StarMark.Abstractions.Clipboard.ClipboardPolicy.MaxEntries);

    /// <summary>
    /// 两个上限<b>一次整档写入</b>（P-43 A 的口径：一次读档 + 最多一次落盘）。
    /// <para>为什么不给两个各写一格的方法：那两格数字在界面上是 <c>NumberBox</c>，每敲一位都会变更；
    /// 分开写就是"敲四次数字、八次整档读写"。而它们本来就是同一个决定的两半，
    /// 一起落盘也就不会出现"图片已改、文本还留着旧值"的中间态设置。</para>
    /// </summary>
    public void SaveClipboardMaxEntries(int imageEntries, int textEntries)
    {
        var d = Load() ?? new SettingsData();
        d.ClipboardImageMaxEntries = StarMark.Abstractions.Clipboard.ClipboardPolicy.ClampImageMaxEntries(imageEntries);
        d.ClipboardTextMaxEntries = StarMark.Abstractions.Clipboard.ClipboardPolicy.ClampTextMaxEntries(textEntries);
        Save(d);
    }
}
