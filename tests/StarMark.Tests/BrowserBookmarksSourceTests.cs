#nullable enable
using StarMark.Abstractions;
using StarMark.Integrations.Bookmarks;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="BrowserBookmarksSource"/>（Chrome/Edge 共用基类）源壳契约护栏——补覆盖·非修缺陷。
/// <para><see cref="BookmarksFileParser"/> 与 <see cref="BookmarkItemFactory"/> 各自已有专测，
/// 但把「解析 → 逐条 MapItem(带 SourceId) → 汇聚」缝合成 <see cref="IItemSource"/> 的这层**源壳**
/// 此前仅在 <c>StarMark.SmokeTest</c> 里被 new 出来跑一遍（不进本 xUnit 套件），无任何断言。
/// 若有人把 <c>MapItem(e, SourceId, now)</c> 的来源实参写错、或漏掉 <c>.Select</c> 汇聚、
/// 或改动 <c>IsAvailable</c> 缺失文件闸门 / <c>SearchAsync</c> 恒空契约，现有 <c>StarMark.Tests</c>
/// 一律测不到。本文件以真实临时文件端到端钉死这层编排契约。</para>
/// </summary>
public sealed class BrowserBookmarksSourceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* 临时文件清理失败不影响断言 */ }
    }

    private string WriteTempBookmarks(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "starmark-bw-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    // 三棵树根下共 3 条 url：Bar 直下 GitHub、Bar>Dev 下一条空名 url、Other 下一条 Docs（无 date_added）。
    private const string ValidJson =
        @"{""roots"":{""bookmark_bar"":{""type"":""folder"",""name"":""Bar"",""children"":[" +
        @"{""type"":""url"",""name"":""GitHub"",""url"":""https://github.com/"",""date_added"":""13300000000000000""}," +
        @"{""type"":""folder"",""name"":""Dev"",""children"":[{""type"":""url"",""name"":"""",""url"":""https://example.com/a"",""date_added"":""0""}]}]}," +
        @"""other"":{""type"":""folder"",""name"":""Other"",""children"":[{""type"":""url"",""name"":""Docs"",""url"":""https://learn.microsoft.com/dotnet""}]}}}";

    [Fact]
    public async Task Fetch_ValidFile_MapsEveryUrlAndStampsSourceOnAllItems()
    {
        var source = new EdgeBookmarksSource(WriteTempBookmarks(ValidJson));
        Assert.True(source.IsAvailable);                 // File.Exists 闸门

        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Equal(3, items.Count);                     // 跨三棵根树汇聚全部 url
        Assert.All(items, i => Assert.Equal("edge", i.Source));   // SourceId 实参须逐条写入（写错即此挂）
        Assert.All(items, i => Assert.Equal(ItemType.Bookmark, i.Type));
    }

    [Fact]
    public async Task Fetch_DelegatesItemShapingToFactory_UriPreservedAndTitleFallsBackToUrl()
    {
        var items = await new EdgeBookmarksSource(WriteTempBookmarks(ValidJson))
            .FetchAsync(new SyncContext(), CancellationToken.None);

        var gh = Assert.Single(items, i => i.Uri == "https://github.com/");   // Uri 保留原始串（不吞锚点/深链）
        Assert.Equal("GitHub", gh.Title);

        // 空 name 须经工厂回落到 URL——此断言证源壳确调用 MapItem 而非裸拼 BookmarkEntry
        var blankName = Assert.Single(items, i => i.Uri == "https://example.com/a");
        Assert.Equal("https://example.com/a", blankName.Title);
    }

    [Fact]
    public async Task Fetch_MissingFile_IsNotAvailableAndReturnsEmpty()
    {
        var source = new EdgeBookmarksSource(Path.Combine(Path.GetTempPath(), "starmark-nope-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.False(source.IsAvailable);

        Assert.Empty(await source.FetchAsync(new SyncContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Fetch_GarbageContent_ReturnsEmptyWithoutThrowing()
    {
        // 损坏 JSON：解析器吞 JsonException→空集合→源壳返回空，绝不冒泡打断整体同步
        var source = new EdgeBookmarksSource(WriteTempBookmarks("{ this is ] not valid json"));
        Assert.True(source.IsAvailable);                 // 文件确实在（走解析而非缺失闸门）

        Assert.Empty(await source.FetchAsync(new SyncContext(), CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_AlwaysEmpty_BookmarksAreFetchOnly()
    {
        // 书签源不做增量搜索（统一搜索靠既有 items 表）——SearchAsync 恒空的契约
        var items = await new EdgeBookmarksSource(WriteTempBookmarks(ValidJson))
            .SearchAsync("anything", new SearchFilter(), CancellationToken.None);
        Assert.Empty(items);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("edge")]
    public async Task Subclass_SourceId_FlowsIntoEveryItem(string which)
    {
        var path = WriteTempBookmarks(ValidJson);
        BrowserBookmarksSource source = which == "edge" ? new EdgeBookmarksSource(path) : new ChromeBookmarksSource();
        Assert.Equal(which, source.SourceId);

        if (which == "edge")   // Chrome 无路径注入构造器，端到端注入仅对 Edge 验；Chrome 仅钉常量与映射一致
            Assert.All(await source.FetchAsync(new SyncContext(), CancellationToken.None), i => Assert.Equal(which, i.Source));
    }

    [Fact]
    public void ChromeAndEdge_HaveDistinctDisplayNames()
    {
        Assert.Equal("Chrome 书签", new ChromeBookmarksSource().DisplayName);
        Assert.Equal("Edge 书签", new EdgeBookmarksSource().DisplayName);
    }
}
