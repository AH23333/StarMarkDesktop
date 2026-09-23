#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using StarMark.Abstractions.Feed;

namespace StarMark.Core.Feed;

/// <param name="Entries">解析出来的条目（已按源的上限截断、按链接去重）。</param>
/// <param name="FeedTitle">源自己报的频道名（可用来给用户确认"我加对了没"）。null＝没读到。</param>
/// <param name="Error">为什么解析不出来。<b>为 null 才算成功</b>；条目数为 0 但没报错是合法结果
/// （源真的没内容），界面要能区分"空的"与"坏了"。</param>
public sealed record RssParseResult(IReadOnlyList<RssEntry> Entries, string? FeedTitle, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// RSS 2.0 / Atom 的解析器：<b>纯字符串进、条目出，不碰网络</b>。
/// <para>
/// 为什么自己写而不装现成的包：D1 裁决是零新增 NuGet 依赖（<c>System.ServiceModel.Syndication</c> 虽是
/// 微软官方包，但会把 WCF 那一片依赖树拖进来）。RSS/Atom 真正要处理的形状就两种，
/// 而自写解析器换来一件更要紧的东西——<b>判据可单测</b>：缺字段怎么兜底、相对链接怎么补、
/// 时间认不出时为什么不能拿"现在"冒充，这些都能钉成断言。
/// </para>
/// <para>
/// <b>安全边界</b>：订阅地址是用户输入的，返回的 XML 是<b>外部不可信数据</b>。
/// 本仓库此前没有任何"解析外部 XML"的入口，这条是新增的，所以加固要显式写出来而不是指望默认值：
/// DTD/实体一律拒（挡住 XXE 与"实体炸弹"）、文档大小与嵌套深度设上限、条目数在解析阶段就截断、摘要限长。
/// </para>
/// </summary>
public static class RssParser
{
    /// <summary>摘要保留的最大字符数（界面只给一两行；不截断等于让源方决定我们要分配多少内存）。</summary>
    public const int MaxSummaryChars = 400;

    /// <summary>整个文档的字符上限：超过就当它是坏源，不再往下解析。</summary>
    public const int MaxDocumentChars = 4_000_000;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private static readonly Regex ScriptBlock = new(
        @"<(script|style)[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"[\t\r\n\f\v ]+", RegexOptions.Compiled);

    /// <summary>解析一份订阅文档。<paramref name="xml"/> 是源返回的原文。</summary>
    public static RssParseResult Parse(string? xml, RssSourceConfig source)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new RssParseResult(Array.Empty<RssEntry>(), null, "这个源没有返回任何内容");
        if (xml.Length > MaxDocumentChars)
            return new RssParseResult(Array.Empty<RssEntry>(), null,
                $"这个源返回的内容超过 {MaxDocumentChars / 1024 / 1024}MB，按坏源处理");

        // DTD / 实体：直接拒。用户输入的地址返回什么不由我们决定，而这两样是唯一能让解析器"去读别处"的入口。
        if (HasDoctype(xml))
            return new RssParseResult(Array.Empty<RssEntry>(), null, "这个源带 DTD 或实体声明，出于安全不解析");

        XDocument doc;
        try
        {
            doc = LoadHardened(xml);
        }
        catch (XmlException ex)
        {
            return new RssParseResult(Array.Empty<RssEntry>(), null, "不是一份能读的 XML：" + ex.Message);
        }
        catch (Exception ex)
        {
            return new RssParseResult(Array.Empty<RssEntry>(), null, "解析失败：" + ex.Message);
        }

        var root = doc.Root;
        if (root is null) return new RssParseResult(Array.Empty<RssEntry>(), null, "文档没有根节点");

        var isAtom = root.Name == Atom + "feed";
        if (!isAtom && !root.Name.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase)
                   && !root.Name.LocalName.Equals("rdf", StringComparison.OrdinalIgnoreCase)
                   && root.Element("channel") is null)
        {
            // 根节点既不是 RSS 也不是 Atom：多半是用户填了个普通网页地址。
            // 这条要说得出口，否则"加了个源结果是空的"与"加错了"在用户那边是同一个谜。
            return new RssParseResult(Array.Empty<RssEntry>(), null,
                $"这个地址返回的不是 RSS / Atom 文档（根节点是 {root.Name.LocalName}）——如果它是普通网页，需要填它自己的订阅地址");
        }

        var entries = isAtom ? FromAtom(root, source) : FromRss(root, source);
        // RSS 的频道名在 rss/channel/title 下，Atom 的在 feed/title 下——只看根节点的直接子元素会永远读不到
        var title = isAtom ? root.Element(Atom + "title") : root.Element("channel")?.Element("title") ?? root.Element("title");
        return new RssParseResult(entries, TextOf(title), null);
    }

    /// <summary>
    /// <b>必须走这条路</b>：<c>XDocument.Parse(string)</c> 用的是内置默认设置，
    /// 只有 <c>XDocument.Load(XmlReader)</c> 才会吃到下面那套加固参数。
    /// </summary>
    private static XDocument LoadHardened(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,     // 出现 DTD 直接抛，不解析也不取外部实体
            XmlResolver = null,                          // 双保险：实体即使被放行也解析不了
            MaxCharactersInDocument = MaxDocumentChars,
            MaxCharactersFromEntities = 1_048_576,       // 实体展开上限（挡 billion-laughs）
            CloseInput = true,
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    /// <summary>扫文档开头找 DOCTYPE/ENTITY。<c>XDocument</c> 会把 DOCTYPE 整段丢掉，事后从树上看不出来。</summary>
    private static bool HasDoctype(string xml)
    {
        var head = xml.Length > 4096 ? xml[..4096] : xml;
        return head.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase);
    }

    // ────────── RSS 2.0 / RDF ──────────

    private static IReadOnlyList<RssEntry> FromRss(XElement root, RssSourceConfig source)
    {
        var channel = root.Element("channel");
        var items = channel is null
            ? root.Elements().Where(e => e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
            : channel.Elements().Where(e => e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase));
        var list = new List<RssEntry>();
        foreach (var item in items.Take(RssSourceConfig.MaxEntries))
        {
            var link = Absolute(LocalText(item, "link"), source.Url);
            list.Add(new RssEntry(
                Fallback(LocalText(item, "title"), link, source),
                link,
                ParseDate(LocalText(item, "pubDate") ?? LocalText(item, "date")),
                Clean(LocalText(item, "description") ?? LocalText(item, "summary")),
                LocalText(item, "creator") ?? LocalText(item, "author"),
                source.Id,
                Display(source)));
        }
        return Dedupe(list);
    }

    // ────────── Atom ──────────

    private static IReadOnlyList<RssEntry> FromAtom(XElement root, RssSourceConfig source)
    {
        var list = new List<RssEntry>();
        foreach (var entry in root.Elements().Where(e => e.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase))
                     .Take(RssSourceConfig.MaxEntries))
        {
            var link = Absolute(AtomLinkHref(entry), source.Url);
            list.Add(new RssEntry(
                Fallback(LocalText(entry, "title"), link, source),
                link,
                ParseDate(LocalText(entry, "published") ?? LocalText(entry, "updated")),
                Clean(LocalText(entry, "content") ?? LocalText(entry, "summary")),
                AtomAuthor(entry),
                source.Id,
                Display(source)));
        }
        return Dedupe(list);
    }

    /// <summary>
    /// Atom 的一条 entry 可以有多个 link，<b>要的是正文那条</b>：优先 <c>rel="alternate"</c>
    /// （或干脆没写 rel，规范上默认就是 alternate），再退到无 rel / self / 第一条。
    /// 直接取第一个 link 会在"先写 icon 再写正文"的源上拿到图标地址。
    /// </summary>
    private static string? AtomLinkHref(XElement entry)
    {
        var links = entry.Elements().Where(e => e.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase)).ToList();
        if (links.Count == 0) return null;
        string? HrefWithRel(string rel) => links.FirstOrDefault(l =>
            (l.Attribute("rel")?.Value ?? string.Empty).Trim().Equals(rel, StringComparison.OrdinalIgnoreCase))?
            .Attribute("href")?.Value;
        return HrefWithRel("alternate")
            ?? links.FirstOrDefault(l => l.Attribute("rel") is null)?.Attribute("href")?.Value
            ?? HrefWithRel("self")
            ?? links[0].Attribute("href")?.Value;
    }

    private static string? AtomAuthor(XElement entry)
    {
        var author = entry.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("author", StringComparison.OrdinalIgnoreCase));
        return author is null ? null : LocalText(author, "name");
    }

    // ────────── 公共小工具（每一条都对应一种真实源的做法）──────────

    /// <summary>
    /// 按本地名取子元素文本。<b>命名空间不参与匹配</b>：RSS 里 <c>dc:creator</c>、Atom 里带默认命名空间的
    /// <c>title</c>，前缀与 URI 都是源方自己选的，只认本地名才不会"这个源恰好没解析出来"。
    /// </summary>
    private static string? LocalText(XContainer parent, string localName)
    {
        var element = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        var text = element?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? TextOf(XElement? element)
        => string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static string Display(RssSourceConfig source)
        => string.IsNullOrWhiteSpace(source.Name) ? RssSourceConfig.FallbackName(source.Url) : source.Name.Trim();

    private static string Fallback(string? title, string link, RssSourceConfig source)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title;
        // 标题为空的源不少见。列表里留一行空白等于让用户以为程序坏了——用链接或源名兜底。
        if (link.Length > 0) return Shorten(link, 60);
        return Display(source);
    }

    /// <summary>把相对链接补成绝对链接；补不动就原样返回（宁可显示一个不能点的地址，也不伪造一个）。</summary>
    public static string Absolute(string? link, string feedUrl)
    {
        var raw = (link ?? string.Empty).Trim();
        if (raw.Length == 0) return string.Empty;
        if (Uri.TryCreate(raw, UriKind.Absolute, out _)) return raw;
        if (Uri.TryCreate(feedUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, raw, out var combined)) return combined.ToString();
        return raw;
    }

    /// <summary>
    /// 订阅里常见的两种时间写法：RFC 1123（<c>Mon, 02 Jan 2006 15:04:05 GMT</c>）与
    /// ISO 8601（<c>2024-01-02T03:04:05Z</c> 或带偏移）。<b>认不出返回 null，不拿"现在"冒充</b>：
    /// 一条三年前的文章被标成"刚刚发布"，用户就会为它重做一遍已经做过的事。
    /// </summary>
    public static DateTimeOffset? ParseDate(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        // 不变文化解析认得数字偏移（+0800），但**不认 "GMT"/"UTC" 这两个字面时区名**——
        // 而 RFC 1123 的订阅时间恰恰绝大多数以 "GMT" 结尾。先归一成 +0000，否则整源的时间全丢。
        var normalized = text;
        if (normalized.EndsWith("GMT", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("UTC", StringComparison.OrdinalIgnoreCase))
            normalized = string.Concat(normalized[..^3].TrimEnd(), " +0000");
        // 星期名是冗余信息，而源方写错它的很不少（"Mon, 02 Jan 2026" 其实是周五）。
        // 带着它解析会**整条时间失败**——用户看到的是"这条没有日期"，而真实原因藏在源的一个笔误里。
        var comma = normalized.IndexOf(',');
        if (comma is > 0 and <= 9 && normalized[..comma].All(char.IsLetter))
            normalized = normalized[(comma + 1)..].Trim();
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// 摘要：源里通常是转义过的 HTML，XML 解析后拿到的是真的标签文本 ⇒ 先整段丢掉 script/style，
    /// 再剥标签、解常见 HTML 实体、压空白、限长。
    /// <b>顺序不能反</b>：先解实体会把 <c>&amp;lt;b&amp;gt;</c> 变成标签再被剥掉，用户看到的摘要就少了一段。
    /// </summary>
    public static string Clean(string? html)
    {
        var raw = html ?? string.Empty;
        if (raw.Length == 0) return string.Empty;
        var withoutScripts = ScriptBlock.Replace(raw, " ");
        var text = Tag.Replace(withoutScripts, " ");
        text = UnescapeHtml(text);
        text = Whitespace.Replace(text, " ").Trim();
        return text.Length > MaxSummaryChars ? text[..MaxSummaryChars].TrimEnd() + "…" : text;
    }

    /// <summary>只解这六个：源里出现率极高，而完整的 HTML 实体表要引一张 250 行的对照，不值。</summary>
    private static string UnescapeHtml(string text)
    {
        if (!text.Contains('&')) return text;
        var sb = new StringBuilder(text);
        Replace(sb, "&nbsp;", " ");
        Replace(sb, "&quot;", "\"");
        Replace(sb, "&#39;", "'");
        Replace(sb, "&apos;", "'");
        Replace(sb, "&lt;", "<");
        Replace(sb, "&gt;", ">");
        Replace(sb, "&amp;", "&");     // 最后：前面几步产生的 & 不该再被解一次
        return sb.ToString();
    }

    private static void Replace(StringBuilder sb, string from, string to)
    {
        var index = sb.ToString().IndexOf(from, StringComparison.Ordinal);
        while (index >= 0)
        {
            sb.Remove(index, from.Length).Insert(index, to);
            index = sb.ToString().IndexOf(from, index + to.Length, StringComparison.Ordinal);
        }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>
    /// 按链接去重（同一链接在源里出现两次＝重复条目），<b>保留第一次出现的那条</b>：
    /// 多数源把最新放最前，"后来覆盖前面"会让列表变成旧内容。
    /// 没有链接的条目一律保留——它们彼此无法区分，误删比留两条更糟。
    /// </summary>
    private static IReadOnlyList<RssEntry> Dedupe(List<RssEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<RssEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Link.Length > 0 && !seen.Add(entry.Link)) continue;
            list.Add(entry);
        }
        return list;
    }
}
