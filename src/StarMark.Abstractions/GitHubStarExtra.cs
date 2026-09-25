#nullable enable
using System.Text.Json;

namespace StarMark.Abstractions;

/// <summary>
/// 读 <see cref="Item.ExtraJson"/> 里那份 GitHub Star 元信息的<b>唯一</b>入口。
/// <para>
/// 为什么收成一处：同一件"解析 + 空值归一 + 坏 JSON 兜住"的事在项目里被抄过四遍
/// （语言直方图、趋势归属日、搜索侧语言筛选、语言下拉各一份），而"抄的这几遍会不会各自漂移"
/// 正是坏 JSON 在某一条路径上抛出来、把整份报告带走的入口。收成一个方法之后，
/// "坏 JSON 与没有元信息同义、绝不抛" 只需要说一次。
/// </para>
/// </summary>
public static class GitHubStarExtra
{
    /// <summary>
    /// 解析一条条目的 Star 元信息。非 GitHubStar / 空串 / 坏 JSON / 内容是 <c>null</c> ⇒ 一律回 <c>null</c>。
    /// <b>不抛</b>：这些调用点全在统计与筛选的循环里，一条被手改坏或被截断的记录不该带走整份结果。
    /// </summary>
    public static GitHubStarMeta? TryRead(Item? item)
    {
        if (item is null || item.Type != ItemType.GitHubStar || string.IsNullOrEmpty(item.ExtraJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<GitHubStarMeta>(item.ExtraJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>主语言。没有、空串、坏 JSON 都回 <c>null</c>——空串不能进语言直方图，也不能当筛选值。</summary>
    public static string? LanguageOf(Item? item)
    {
        var language = TryRead(item)?.Language;
        return string.IsNullOrEmpty(language) ? null : language;
    }
}
