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
/// SettingsStore 的这一段——GitHub 热榜与网址来源（RSS）那组。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    // ===== GitHub 热榜（批次 KA→）=====

    /// <summary>热榜总开关（<b>默认关</b>）。关着时导航项不显示、页面也不发任何请求。</summary>
    public bool LoadTrendingEnabled() => Load() is { } d && d.TrendingEnabled == true;

    public void SaveTrendingEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.TrendingEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 上次的周期。<b>存了个不认识的值 ⇒ 回落默认周榜</b>：设置文件可被手改、也可能来自更老的版本，
    /// 把异常值一路传下去只会得到一个"点开是空白"的页面。
    /// </summary>
    public TrendingPeriod LoadTrendingPeriod()
        => TrendingPeriods.TryParse(Load()?.TrendingPeriod, out var p)
            ? p : TrendingPeriod.Weekly;

    /// <summary>周期代码（daily/weekly/monthly），与缓存键、界面筛选共用同一套写法。</summary>
    public string LoadTrendingPeriodCode() => TrendingPeriods.Code(LoadTrendingPeriod());

    public void SaveTrendingPeriod(string code)
    {
        if (!TrendingPeriods.TryParse(code, out var period)) return;
        var d = Load() ?? new SettingsData();
        d.TrendingPeriod = TrendingPeriods.Code(period);
        Save(d);
    }

    /// <summary>「今日速览」是否显示热榜块（默认关——开启热榜功能时由弹窗征询，之后设置里可改）。</summary>
    public bool LoadTrendingGlanceEnabled() => Load() is { } d && d.TrendingGlanceEnabled == true;

    public void SaveTrendingGlanceEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.TrendingGlanceEnabled = enabled;
        Save(d);
    }

    // ===== 网址来源（RSS / Atom，批次 NF）=====

    /// <summary>
    /// 已配置的网址来源。<b>解析不出来时返回空表而不是抛</b>：设置文件可被手改、也可能来自更老的版本，
    /// 而"设置页整页打不开"比"这一栏看着像没配过"严重得多（坏档仍原样留在磁盘上，不会被这次保存悄悄覆盖掉——
    /// 只有用户真的改动源列表时才会重写这一栏）。
    /// </summary>
    public List<RssSourceConfig> LoadRssSources() => DeserializeRssSources(Load()?.RssSourcesJson);

    /// <summary>读侧只有一条解析路径：<see cref="LoadRssEnabled"/> 也要看同一份源列表，
    /// 各写一遍就会出现"开关判定与列表内容对不上"的那种错。</summary>
    private static List<RssSourceConfig> DeserializeRssSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<RssSourceConfig>();
        try
        {
            var list = JsonSerializer.Deserialize<List<RssSourceConfig>>(json) ?? new List<RssSourceConfig>();
            // 剩下的清洗（空地址、重复地址、id 撞车）搬去了 Core 的 RssSourceList.Normalize：UI 层测试引用不到，判据只能放在引得到的那一侧
            return RssSourceList.Normalize(list);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[Settings] 网址来源列表读不出，按未配置处理：{ex.Message}");
            return new List<RssSourceConfig>();
        }
    }

    /// <summary>
    /// RSS 这一栏算不算开着。<b>一次读档</b>同时取开关与源列表：分两次 <c>Load()</c> 的话，
    /// 中间有人改了设置文件，就会出现"开关按的是 A 列表，判的是 B 列表"。
    /// </summary>
    public bool LoadRssEnabled()
    {
        var d = Load();
        return RssActivation.IsOn(d?.RssEnabled, DeserializeRssSources(d?.RssSourcesJson));
    }

    /// <summary>写下用户的表态（此后"没表过态"那条兜底不再生效，见 <c>RssActivation</c>）。</summary>
    public void SaveRssEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.RssEnabled = enabled;
        Save(d);
    }

    public void SaveRssSources(IReadOnlyList<RssSourceConfig> sources)
    {
        var d = Load() ?? new SettingsData();
        d.RssSourcesJson = JsonSerializer.Serialize(sources);
        Save(d);
    }
}
