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
/// SettingsStore 的这一段——本地磁盘搜索那组：开关、索引根目录的取舍与上限。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>本地磁盘搜索总开关（默认关）。关闭时后台提权服务/Everything 不启动、不加载索引（0 内存），搜索也不含本地文件。</summary>
    public bool LoadLocalDiskSearchEnabled() => Load() is { } d && d.LocalDiskSearchEnabled == true;

    public void SaveLocalDiskSearchEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.LocalDiskSearchEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 本地文件索引根目录（P0-1b）。未配置时返回默认（桌面/下载/文档中存在的目录）。
    /// <para>
    /// P-56：<b>这里不再按 <c>Directory.Exists</c> 过滤</b>。旧行为会在盘没插时把"移动盘/U 盘上的目录"
    /// 从返回结果里抹掉，而设置页那个多行文本框正是读这份结果再写回去的 ⇒ 用户在插回盘之前只要保存过
    /// 任意一项设置，那条根就永久消失了，全程零提示。暂不可用的目录由索引侧逐根跳过
    /// （<c>EverythingSource.FetchAsync</c> 早有 Exists 闸门），配置本身保持用户写下的原样。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LoadFileIndexRoots()
    {
        var d = Load();
        if (d?.FileIndexRoots is { Count: > 0 } list)
            return NormalizeRoots(list);
        return DefaultFileIndexRoots();
    }

    /// <summary>
    /// 已配置但<b>当前</b>不可用的根目录（不存在 / 无权限探测）。设置页据此照实说明"这条暂时不索引进库"，
    /// 而不是像过去那样悄悄从列表里删掉它（P-56）。
    /// </summary>
    public IReadOnlyList<string> UnavailableFileIndexRoots()
        => LoadFileIndexRoots().Where(r => !IsDirectoryUsable(r)).ToList();

    private static bool IsDirectoryUsable(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }   // 探测本身就抛（无权限、坏网络路径）同样按"当前不可用"处理
    }

    private static List<string> NormalizeRoots(IEnumerable<string> roots)
        => roots.Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                // Windows 路径大小写不敏感：OrdinalIgnoreCase 去重，否则 "d:\Docs" 与 "D:\Docs" 算两条。
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>保存用户写下的根目录：只做 trim / 去空行 / 去重，<b>不</b>以"当前是否存在"为准入门（见 <see cref="LoadFileIndexRoots"/>）。</summary>
    public void SaveFileIndexRoots(IReadOnlyList<string> roots)
    {
        var d = Load() ?? new SettingsData();
        d.FileIndexRoots = NormalizeRoots(roots);
        Save(d);
    }

    /// <summary>每目录索引数量上限（P0-1b）。未配置或非法时返回默认 5000。</summary>
    public int LoadMaxFileIndexCount()
        => Load() is { } d && d.MaxFileIndexCount is > 0 ? d.MaxFileIndexCount.Value : 5000;

    public void SaveMaxFileIndexCount(int count)
    {
        var d = Load() ?? new SettingsData();
        d.MaxFileIndexCount = count > 0 ? count : 5000;
        Save(d);
    }

    private static List<string> DefaultFileIndexRoots()
    {
        // P-50：下载目录以前写死 %USERPROFILE%\Downloads。开了 OneDrive「已知文件夹移动」的机器上
        // 真实下载目录是 …\OneDrive\Downloads ⇒ 该根恒不存在，被下面的 Exists 过滤静默剔除，
        // 用户表现为"下载里的文件永远搜不到"（桌面/文档走 GetFolderPath 会自动跟随，只有这条不会）。
        // 口径：.NET 没暴露 Downloads 这个已知文件夹（只有 Desktop/Documents 等），而 OneDrive
        // 「已知文件夹移动」就是把 Downloads 挪到用户 OneDrive 根下 ⇒ 显式收三种 OneDrive 形态
        // （个人版/企业版环境变量 + 配置文件目录下的 OneDrive）再兜老路径，全部按存在与否过滤。
        var candidates = new List<string>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var oneDrive in new[] { "OneDrive", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(oneDrive);
            if (!string.IsNullOrWhiteSpace(root)) AddIfUsable(candidates, () => Path.Combine(root, "Downloads"));
        }
        AddIfUsable(candidates, () => Path.Combine(profile, "OneDrive", "Downloads"));
        AddIfUsable(candidates, () => Path.Combine(profile, "Downloads"));
        AddIfUsable(candidates, () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddIfUsable(candidates, () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        return candidates;
    }

    private static void AddIfUsable(List<string> list, Func<string> pick)
    {
        string path;
        try { path = pick(); } catch { return; }        // 个别机器上已知文件夹解析会抛
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path); }
        catch { /* 无权限探测的目录（网络盘/重定向被拦）：跳过它，不影响其余根目录 */ }
    }
}
