#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Feed;

namespace StarMark.Core.Feed;

/// <summary>
/// 用户存的来源列表进/出设置档时要过的规范化。<b>放在 Core 而不是设置存储里</b>：
/// 那些判据决定"用户加了三次源，重启后还在不在"，而 <c>SettingsStore</c> 在 UI 层、测试项目引用不到它。
/// </summary>
public static class RssSourceList
{
    /// <summary>
    /// 清洗一份反序列化出来的源列表：<b>坏的数据丢掉，好的数据一条都不能少</b>。
    /// <list type="bullet">
    /// <item>丢掉 <c>Id&lt;=0</c> 或地址空白的行——它们连"该去哪抓"都没有，留着只是在界面上占一行空位。</item>
    /// <item>地址重复的只留第一条：<see cref="RssAggregator"/> 是按源逐条抓的，两行同址就是白跑一次网络，
    /// 而且两条都会往列表里塞同样的条目。</item>
    /// <item><b>id 重复的要重新编号</b>：抓取结果与校验符都按 id 对号（<c>Outcomes.Source.Id == row.Config.Id</c>），
    /// 两行同 id 会把状态显示到错误的源那一行上——这种错在界面上完全看不出来。</item>
    /// <item>名字空白时用域名兜底，别让列表出现无名行。</item>
    /// </list>
    /// <para>顺序原样保留（用户自己排的次序不该被清洗打乱）。</para>
    /// </summary>
    public static List<RssSourceConfig> Normalize(IEnumerable<RssSourceConfig>? stored)
    {
        var result = new List<RssSourceConfig>();
        if (stored is null) return result;

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<int>();
        var nextFreeId = 1;

        foreach (var raw in stored)
        {
            if (raw is null) continue;
            if (RssSourceConfig.UrlProblem(raw.Url) is not null) continue;

            var url = raw.Url.Trim();
            if (!seenUrls.Add(url)) continue;

            var id = raw.Id;
            if (id <= 0 || !usedIds.Add(id))
            {
                while (usedIds.Contains(nextFreeId)) nextFreeId++;
                id = nextFreeId;
                usedIds.Add(id);
            }

            var name = string.IsNullOrWhiteSpace(raw.Name) ? RssSourceConfig.FallbackName(url) : raw.Name.Trim();
            result.Add(raw with { Id = id, Name = name, Url = url });
        }

        return result;
    }
}
