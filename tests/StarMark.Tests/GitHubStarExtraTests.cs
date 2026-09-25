#nullable enable
using System.Text.Json;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="GitHubStarExtra"/> 的解析契约（PO-5）。这条"解析 + 空值归一 + 坏 JSON 兜住"的事
/// 原先在项目里被抄了四遍（语言直方图、趋势归属日、搜索侧语言筛选、语言下拉各一份），
/// 抄本会各自漂移——漂移的代价是坏 JSON 在某一条路径上抛出来，把整份健康报告或整页结果带走。
/// 收成一处之后，这里钉的就是"唯一那份"的语义。
/// </summary>
public sealed class GitHubStarExtraTests
{
    private static Item Star(string json) => new()
    {
        Type = ItemType.GitHubStar,
        ExtraJson = json,
        CreatedAt = 1_700_000_000,
    };

    [Fact]
    public void ReadsLanguageAndStarredAt()
    {
        var meta = GitHubStarExtra.TryRead(Star("""{"Language":"C#","StarredAt":1600000000}"""));

        Assert.Equal("C#", meta!.Language);
        Assert.Equal(1_600_000_000, meta.StarredAt);
    }

    [Theory]
    [InlineData(null)]                       // 整条条目都没有
    [InlineData("")]                         // ExtraJson 空串
    [InlineData("{")]                        // 被截断的坏 JSON
    [InlineData("not json at all")]
    [InlineData("null")]                     // JSON 字面量 null ⇒ 反序列化给出 null
    public void BrokenOrMissingExtra_NeverThrows_AndReadsAsNoMeta(string? json)
    {
        var item = json is null ? null : Star(json);

        Assert.Null(GitHubStarExtra.TryRead(item));
        Assert.Null(GitHubStarExtra.LanguageOf(item));
    }

    [Fact]
    public void EmptyLanguageString_CollapsesToNull_SoItCannotEnterTheHistogram()
        // 空串若原样返回，会进语言直方图变成"有一条语言为空的记录"，也会成为下拉里的空白项。
        => Assert.Null(GitHubStarExtra.LanguageOf(Star("""{"Language":""}""")));

    [Fact]
    public void NonStarItems_AreNotParsed_EvenWhenTheirJsonCarriesALanguage()
    {
        // 书签/文件的 ExtraJson 形状不同（这里故意用与 Star 完全同名的 PascalCase 字段）；
        // 按类型短路既省一次反序列化，也不给"猜形状"留口子。
        var note = new Item
        {
            Type = ItemType.Note,
            ExtraJson = """{"Language":"Python"}""",
        };

        Assert.Null(GitHubStarExtra.TryRead(note));
        Assert.Null(GitHubStarExtra.LanguageOf(note));
    }

    [Fact]
    public void WronglyTypedField_IsCaughtNotThrown()
        // Language 是数字：System.Text.Json 抛 JsonException ⇒ 归一成"没有元信息"。
        => Assert.Null(GitHubStarExtra.TryRead(Star("""{"Language":123}""")));

    // ────────── 收敛闸门：四份抄本必须只剩一份实现，且整表统计每条只解析一次 ──────────

    [Fact]
    public void HealthReport_ParsesEachItemOnce()
    {
        // 语言分布与趋势归属过去各解析一遍（整表 ×2）。现在解析发生在建 parsed 那一步，
        // 两个统计都从它取值 ⇒ 这个文件里不该再出现任何直接反序列化。
        var source = SourceGate.ReadRepoFile("src/StarMark.Core/Insights/InsightsService.cs");

        Assert.Equal(0, SourceGate.Count(source, "Deserialize<GitHubStarMeta>"));
        Assert.Contains("GitHubStarExtra.TryRead(item)", source);
        Assert.Contains("foreach (var (item, meta) in parsed)", source);
    }

    [Theory]
    [InlineData("src/StarMark.Core/Search/SearchService.cs", "private static string? GetLanguage(Item item)")]
    [InlineData("src/StarMark.UI/ViewModels/SearchPageViewModel.cs", "private static string? TryGetLanguage(Item it)")]
    public void SearchSide_HasNoPrivateCopyOfTheParser(string file, string helper)
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(file), helper);

        Assert.Equal(0, SourceGate.Count(body, "Deserialize"));
        Assert.Contains("GitHubStarExtra.LanguageOf", body);
    }
}
