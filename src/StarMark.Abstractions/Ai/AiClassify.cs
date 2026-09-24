#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace StarMark.Abstractions.Ai;

/// <summary>一条待整理的条目。<b>只带标题/副标题/摘要/已有标签</b>：不带正文——一是本地文件与剪贴板条目
/// 根本没有"正文"这个概念，二是把整段内容发出去远超"给它贴个标签"所需的量。</summary>
public sealed record ClassifyItem(
    long Id,
    string Title,
    string? Subtitle,
    string? Description,
    string Source,
    IReadOnlyList<string> ExistingTags);

/// <summary>某一条被建议打上的标签。<b>追加而非替换</b>：条目原有的标签一律保留——
/// "整理"不等于"覆盖用户亲手打的东西"，那是不可逆的破坏。</summary>
public sealed record TagProposal(long Id, IReadOnlyList<string> Tags);

/// <summary>标签文本自己的规则。<b>这一层不是美化：不挡住就会写出读不回来的标签</b>——
/// 标签在读 SQL 里是用 char 31 拼成一列再拆的（见 ItemRepository 的 GROUP_CONCAT），
/// 名字里含这个字符会把一个标签劈成两个。</summary>
public static class TagText
{
    /// <summary>与 SQL 侧同一个分隔符：标签名里绝对不许出现。</summary>
    public const char ListSeparator = (char)31;

    /// <summary>标签上限 24 字。模型给的短语一般 2–6 字，超出多半是它把一句话塞成了标签。</summary>
    public const int MaxLength = 24;

    /// <summary>把模型给的一个词收成合法标签；不合法则 null。<b>宁可丢掉一个标签，
    /// 也不要写进一个读不出来的</b>。</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var builder = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var ch in raw.Trim())
        {
            if (char.IsControl(ch) || ch == ListSeparator) continue;      // 控制字符一律去掉（模型偶尔夹换行与制表）
            if (ch is ' ' or '\t' or '　')                                 // 半角 / 制表 / 全角空格
            {
                if (builder.Length > 0) pendingSpace = true;
                continue;
            }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(ch);
        }

        var text = builder.ToString();
        // 只剩符号的东西（"—"、"「"）当标签既搜不到也没法读
        if (!text.Any(char.IsLetterOrDigit)) return null;
        if (text.Length > MaxLength) text = text[..MaxLength].Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>一组标签的收口：规范化 → 大小写不敏感去重 → 按原顺序保留 → 截到 max。
    /// <b>上限取 3（对齐扩展侧实测）</b>：一条给四个已经够分类用了，而多出来的那一个往往就是
    /// "给这一条单独造的专有词"——正是整理要消灭的东西。</summary>
    public static IReadOnlyList<string> Sanitize(IEnumerable<string?> proposed, int max = 3)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in proposed)
        {
            var tag = Normalize(raw);
            if (tag is null || !seen.Add(tag)) continue;
            result.Add(tag);
            if (result.Count >= max) break;
        }
        return result;
    }
}

/// <summary>一轮答复的解读结果。</summary>
/// <param name="Proposals">这一批里给出了标签的那些条（<b>绝不含批次之外的 id</b>）。</param>
/// <param name="MissingIds">批次里有、答复里没给的那些——<b>不能当成"模型觉得不用打标签"</b>，
/// 它更可能是漏抄了几条，界面要能看出来"这批只答了 43/50"。</param>
/// <param name="UnknownIds">答复里出现了但本批没有的编号（模型自己编的行号）。丢掉并回报：
/// 否则一个幻觉编号会去改一条用户根本没选的东西。</param>
/// <param name="NewTags">目录里原本没有的标签名。界面上与"沿用既有标签"分开确认——
/// 造新词是这次整理最容易后悔的部分。</param>
/// <param name="Error">整份答复读不出来时的原因；null＝解析成功（哪怕一条都没提）。</param>
public sealed record ClassifyParseResult(
    IReadOnlyList<TagProposal> Proposals,
    IReadOnlyList<long> MissingIds,
    IReadOnlyList<long> UnknownIds,
    IReadOnlyList<string> NewTags,
    string? Error = null)
{
    public bool Readable => Error is null;
}

/// <summary>
/// 提示词构建与答复解读。<b>整类纯函数</b>：批量分类真正的风险全在这里（预算超了、答复形状变了、
/// 编号对不上），而这些都必须能在没有模型的情况下断言。
/// </summary>
public static class ClassifyPrompt
{
    /// <summary>一批最多几条。50 条沿用扩展侧的实测口径：再多模型会开始漏条，
    /// 而漏条在批量场景里表现为"安静地少整理了一批"。</summary>
    public const int MaxItemsPerBatch = 50;

    /// <summary>参考类别上限：把库里最常用的标签锚进提示词，<b>让模型优先沿用已有的</b>——
    /// 否则同一件事会出现"前端 / 前端开发 / 前端技术"三个标签，那正是用户要整理的原因。</summary>
    public const int MaxReferenceTags = 40;

    /// <summary>用户那一段的字符预算。<b>刻意小于 <see cref="AiRequest.MaxPromptChars"/></b>：
    /// 留出的余量给系统那一段与 JSON 标点。两处各算一次预算，结果一定是一处超。</summary>
    public const int UserBudgetChars = 18_000;

    public static string SystemPrompt(IReadOnlyList<string> referenceTags)
    {
        var vocabulary = referenceTags is null or { Count: 0 }
            ? "库里还没有标签，请自行给出简短、可复用的类别名"
            : "优先从下列已有标签里选，确实不合适才新造：" + string.Join("、", referenceTags.Take(MaxReferenceTags));

        return "你在为一个人的资料库整理标签。" + vocabulary + "。"
            + "规则：每条给 1-3 个标签；标签 2-6 个字；同一批里相同含义必须用同一个词；"
            + "标签要能成类：一个标签至少要能套上 3 条条目，只适合一两次的宁可不给，"
            + "绝不为个别条目造专有词——宁可少分类，也不要一条一个标签；"
            + "不要重复该条已有的标签；不要给无信息量的词（如\u201c其他\u201d\u201c资料\u201d\u201c重要\u201d）。"
            + "输出：只输出一个 JSON 对象，不要解释文字、不要代码围栏。格式："
            + "{ \"items\": [ { \"id\": 1, \"tags\": [ \"标签一\" ] } ] }";
    }

    /// <summary>一批的正文。<b>编号用条目在批次内的序号而不是数据库 id</b>：数据库 id 常常是九位数，
    /// 模型抄错一位就会指到另一条真实存在的条目上；而序号错位会被"批次外编号一律丢掉"挡住。</summary>
    public static string UserPrompt(IReadOnlyList<ClassifyItem> batch)
    {
        var builder = new StringBuilder();
        builder.Append("按编号逐条给标签：\n");
        for (var i = 0; i < batch.Count; i++)
        {
            var item = batch[i];
            builder.Append(i + 1).Append(". ").Append(OneLine(item.Title, 90));
            if (!string.IsNullOrWhiteSpace(item.Subtitle)) builder.Append(" | ").Append(OneLine(item.Subtitle, 60));
            if (!string.IsNullOrWhiteSpace(item.Description)) builder.Append(" | ").Append(OneLine(item.Description, 120));
            builder.Append(" | 来源=").Append(item.Source);
            if (item.ExistingTags.Count > 0)
                builder.Append(" | 已有=").Append(string.Join("、", item.ExistingTags.Take(6)));
            builder.Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>摘要里的换行必须压掉：一行一条的清单被塞进多行文本后就对不上编号，
    /// 而"对不上编号"表现为标签打到别的条目上——静默的错。</summary>
    private static string OneLine(string? text, int max)
    {
        var flat = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    /// <summary>按"每批最多 50 条 <b>且</b> 提示词不超预算"分批。<b>两个上限都要判</b>：
    /// 只看条数时，一批全是长摘要的条目会整批发不出去。</summary>
    public static IReadOnlyList<IReadOnlyList<ClassifyItem>> Batches(IReadOnlyList<ClassifyItem> items)
    {
        var batches = new List<IReadOnlyList<ClassifyItem>>();
        if (items.Count == 0) return batches;

        var current = new List<ClassifyItem>();
        var used = 0;
        foreach (var item in items)
        {
            var cost = CostOf(item);
            if (current.Count > 0 && (current.Count >= MaxItemsPerBatch || used + cost > UserBudgetChars))
            {
                batches.Add(current);
                current = new List<ClassifyItem>();
                used = 0;
            }
            current.Add(item);
            used += cost;
        }
        if (current.Count > 0) batches.Add(current);
        return batches;
    }

    private static int CostOf(ClassifyItem item)
        => 24 + (item.Title?.Length ?? 0)
            + Math.Min(60, item.Subtitle?.Length ?? 0)
            + Math.Min(120, item.Description?.Length ?? 0);

    /// <summary>
    /// 解读答复。<b>三种形状都认</b>（扩展侧实测：同一个模型在不同批次里会给出三种之一）：
    /// ① <c>{"items":[{"id":1,"tags":["a"]}]}</c>；② <c>{"categories":{"前端":[1,2]}}</c>（按标签分组）；
    /// ③ 裸数组 <c>[["a"],["b"]]</c>（按顺序对位）。先剥掉代码围栏与解说文字再按形状分支。
    /// </summary>
    public static ClassifyParseResult Parse(string? reply, IReadOnlyList<ClassifyItem> batch, IReadOnlyList<string>? catalog = null)
    {
        var ids = batch.Select(item => item.Id).ToList();
        var empty = new ClassifyParseResult(Array.Empty<TagProposal>(), ids, Array.Empty<long>(), Array.Empty<string>());

        var json = ExtractJson(reply);
        if (json is null)
            return empty with { Error = "答复里没有 JSON（模型可能只回了自然语言）" };

        var proposals = new Dictionary<long, List<string>>();
        var unknown = new List<long>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                ReadPositional(root, ids, proposals);
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var itemsNode))
            {
                if (itemsNode.ValueKind != JsonValueKind.Array)
                    return empty with { Error = "items 不是数组，读不出逐条结果" };
                ReadItems(itemsNode, ids, proposals, unknown);
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("categories", out var categoriesNode))
            {
                if (categoriesNode.ValueKind != JsonValueKind.Object)
                    return empty with { Error = "categories 不是对象，读不出按标签分组的结果" };
                ReadCategories(categoriesNode, ids, proposals, unknown);
            }
            else
                return empty with { Error = "JSON 里没有 items / categories，读不出哪条配哪些标签" };
        }
        catch (JsonException ex)
        {
            return empty with { Error = "JSON 读不下去：" + ex.Message };
        }

        var result = proposals
            .Select(pair => new TagProposal(pair.Key, TagText.Sanitize(pair.Value)))
            .Where(proposal => proposal.Tags.Count > 0)
            .OrderBy(proposal => ids.IndexOf(proposal.Id))
            .ToList();

        var hit = new HashSet<long>(result.Select(proposal => proposal.Id));
        var proposedTags = result.SelectMany(proposal => proposal.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // 目录为空（库里还没有标签）时，所有词都是"新"的——这时候单独标出来没有信息量，只会吓到人
        var fresh = catalog is null or { Count: 0 }
            ? new List<string>()
            : proposedTags.Where(tag => !catalog.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        return new ClassifyParseResult(
            result,
            ids.Where(id => !hit.Contains(id)).ToList(),
            unknown,
            fresh);
    }

    private static void ReadItems(JsonElement itemsNode, IReadOnlyList<long> ids,
        Dictionary<long, List<string>> proposals, List<long> unknown)
    {
        foreach (var row in itemsNode.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var ordinal = Ordinal(row.TryGetProperty("id", out var id) ? id : default);
            if (!WithinBatch(ordinal, ids.Count))
            {
                if (ordinal is { } stray && stray > ids.Count) unknown.Add(stray);
                continue;
            }
            AddTo(proposals, ids[(int)ordinal!.Value - 1], TagsOf(row));
        }
    }

    private static void ReadCategories(JsonElement categoriesNode, IReadOnlyList<long> ids,
        Dictionary<long, List<string>> proposals, List<long> unknown)
    {
        foreach (var group in categoriesNode.EnumerateObject())
        {
            var tag = TagText.Normalize(group.Name);
            if (tag is null) continue;
            if (group.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var cell in group.Value.EnumerateArray())
            {
                var ordinal = Ordinal(cell);
                if (!WithinBatch(ordinal, ids.Count))
                {
                    if (ordinal is { } stray && stray > ids.Count) unknown.Add(stray);
                    continue;
                }
                AddTo(proposals, ids[(int)ordinal!.Value - 1], new[] { tag });   // 用规范化后的名字，不重复收一遍
            }
        }
    }

    /// <summary>裸数组按顺序对位。<b>多出来的元素直接不看</b>：模型有时会自己多加几条，
    /// 而对位关系一错位，标签就会挂到隔壁那条上——宁可不给也不能给错。</summary>
    private static void ReadPositional(JsonElement array, IReadOnlyList<long> ids,
        Dictionary<long, List<string>> proposals)
    {
        var at = 0;
        foreach (var row in array.EnumerateArray())
        {
            if (at >= ids.Count) break;
            var tags = row.ValueKind switch
            {
                JsonValueKind.Array => StringsOf(row),
                JsonValueKind.String => new[] { row.GetString() },
                JsonValueKind.Object => TagsOf(row),
                _ => Array.Empty<string?>(),
            };
            AddTo(proposals, ids[at], tags);
            at++;
        }
    }

    private static IEnumerable<string?> TagsOf(JsonElement row)
    {
        if (!row.TryGetProperty("tags", out var tags)) return Array.Empty<string?>();
        return tags.ValueKind switch
        {
            JsonValueKind.Array => StringsOf(tags),
            JsonValueKind.String => SplitLoose(tags.GetString()),      // 有的模型给 "前端, 工具" 这样一行
            _ => Array.Empty<string?>(),
        };
    }

    private static IEnumerable<string?> StringsOf(JsonElement array)
        => array.EnumerateArray()
            .Select(cell => cell.ValueKind == JsonValueKind.String ? cell.GetString() : cell.ToString())
            .ToList();

    private static IEnumerable<string?> SplitLoose(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string?>()
            : text.Split(new[] { ',', '，', '、', ';', '；', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>编号是否落在本批范围内（1 起）。<b>0 与负数也在拒绝之列</b>：
    /// 有的模型从 0 开始编号，直接减一就会指到批次末尾那条上去。</summary>
    private static bool WithinBatch(long? ordinal, int count)
        => ordinal is { } value && value >= 1 && value <= count;

    /// <summary>编号：数字与"数字字符串"都收（有些模型会给 <c>"id":"3"</c>）。</summary>
    private static long? Ordinal(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Number when cell.TryGetInt64(out var n) => n,
        JsonValueKind.String when long.TryParse(cell.GetString()?.Trim(), out var s) => s,
        _ => null,
    };

    private static void AddTo(Dictionary<long, List<string>> proposals, long id, IEnumerable<string?> tags)
    {
        if (!proposals.TryGetValue(id, out var list)) proposals[id] = list = new List<string>();
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;   // 空位由 Sanitize 之前就先挡掉，不让 null 流进 List<string>
            list.Add(tag);
        }
    }

    /// <summary>从答复里抠出第一段完整的 JSON。<b>必须先剥代码围栏再找括号</b>：
    /// 模型很规矩地回 <c>```json … ```</c> 时，直接 Parse 整个字符串必炸，
    /// 而那看起来就像"这个模型不会输出 JSON"。</summary>
    private static string? ExtractJson(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var text = reply.Trim();
        if (text.StartsWith("```"))
        {
            var firstBreak = text.IndexOf('\n');
            if (firstBreak > 0) text = text[(firstBreak + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) text = text[..fence];
        }

        var start = text.IndexOfAny(new[] { '{', '[' });
        if (start < 0) return null;
        var open = text[start];
        var close = open == '{' ? '}' : ']';

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }
            switch (ch)
            {
                case '"': inString = true; break;
                case var c when c == open: depth++; break;
                case var c when c == close:
                    depth--;
                    if (depth == 0) return text[start..(i + 1)];
                    break;
            }
        }
        return null;        // 括号没闭合：半截 JSON（答复被 max_tokens 切断）——交给上层按"读不出来"处理
    }
}
