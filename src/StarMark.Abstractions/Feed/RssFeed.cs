#nullable enable
using System;

namespace StarMark.Abstractions.Feed;

/// <summary>
/// 一个网址来源（RSS 2.0 或 Atom 订阅地址）。
/// <para>
/// <b>本仓库里它的定位是"信息来源"，不是"订阅流"</b>（D4 裁决）：源里的条目只是<b>候选</b>，
/// 用户点「收藏」才会写进库。所以这里没有"已读/未读""自动同步"这类状态——那些是阅读器的概念。
/// </para>
/// </summary>
/// <param name="Id">稳定标识（存进设置里的自增/哈希 id；改名不影响它）。</param>
/// <param name="Name">用户看到的名字。留空时由域名兜底——<b>不允许出现空白条目</b>。</param>
/// <param name="Url">订阅地址（http/https）。</param>
/// <param name="Enabled">关掉＝不抓取，但保留配置（用户常常是"暂时不想看"而不是"删掉"）。</param>
public sealed record RssSourceConfig(int Id, string Name, string Url, bool Enabled = true)
{
    /// <summary>单个源允许的最大条目数（解析与展示同一口径）：源方给 500 条时，
    /// 全部搬进内存再丢掉一半，不如在解析阶段就按这个数停手。</summary>
    public const int MaxEntries = 60;

    /// <summary>这一栏能不能用：只有 http/https 值得去抓。</summary>
    public static string? UrlProblem(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "地址是空的";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return "认不出这是一个完整地址（要带 http:// 或 https://）";
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return $"只支持 http / https，这个是 {parsed.Scheme}";
        return null;
    }

    /// <summary>没填名字时用什么显示：域名（去掉 www.），拿不到就退回原地址。</summary>
    public static string FallbackName(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
            return url?.Trim() ?? string.Empty;
        var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? parsed.Host[4..] : parsed.Host;
        return host.Length == 0 ? parsed.Host : host;
    }
}

/// <summary>
/// 一条从源里读到的条目。<b>它还没有进库</b>——进库要用户点「收藏」。
/// </summary>
/// <param name="Title">标题；源没给时由链接兜底。</param>
/// <param name="Link">条目自己的地址（已尽量补成绝对地址）。</param>
/// <param name="PublishedAt">发布时间；源没给或给的形式认不出时为 null（<b>不拿"现在"冒充</b>，
/// 否则用户会以为这是一条刚发布的内容）。</param>
/// <param name="Summary">摘要（纯文本，标签与实体都已剥掉）。可为空。</param>
/// <param name="Author">作者/来源署名。可为空。</param>
/// <param name="SourceId">来自哪个源（<see cref="RssSourceConfig.Id"/>）。</param>
/// <param name="SourceName">源名，随条目一起带出来：列表里要标"这条是哪个源给的"，
/// 否则用户无法判断该不该收藏。</param>
public sealed record RssEntry(
    string Title,
    string Link,
    DateTimeOffset? PublishedAt,
    string Summary,
    string? Author,
    int SourceId,
    string SourceName);
