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
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* 临时文件清理失败不影响断言 */ }
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* 同上 */ }
    }

    private string WriteTempBookmarks(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "starmark-bw-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>造一个 Chromium 风格的 …\&lt;root&gt;\User Data\&lt;profileDir&gt;\Bookmarks，返回该文件路径。</summary>
    private string WriteProfileBookmarks(string profileDir, string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "starmark-prof-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "User Data", profileDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Bookmarks");
        File.WriteAllText(path, json);
        _tempDirs.Add(root);
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

    // ===== 多 profile（真实环境里"书签一条都没同步进来"的头号成因）=====

    private const string SecondProfileJson =
        @"{""roots"":{""bookmark_bar"":{""type"":""folder"",""name"":""Bar"",""children"":[" +
        @"{""type"":""url"",""name"":""Work"",""url"":""https://work.example/intranet""}]}}}";

    [Fact]
    public async Task Fetch_MultiProfile_ReadsDefaultAndProfileSiblings()
    {
        // 多账号机器上真实书签常在 "Profile 1"，Default 是空的/只有几条。旧实现只读 Default
        // ⇒ 同步"成功"却一条都没进来（且无任何异常可查）。
        var defaultPath = WriteProfileBookmarks("Default", ValidJson);
        WriteProfileBookmarksFrom(defaultPath, "Profile 1", SecondProfileJson);

        var items = await new EdgeBookmarksSource(defaultPath)
            .FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Equal(4, items.Count);                                  // 3（Default）+ 1（Profile 1）
        Assert.Contains(items, i => i.Uri == "https://work.example/intranet");
        Assert.All(items, i => Assert.Equal("edge", i.Source));
    }

    [Fact]
    public async Task Fetch_OneProfileCorrupt_OthersStillImport()
    {
        // 旧形状是一份坏 Bookmarks 拖垮整源（返回空 + 仅一行日志）；改后按 profile 隔离失败。
        var defaultPath = WriteProfileBookmarks("Default", ValidJson);
        WriteProfileBookmarksFrom(defaultPath, "Profile 9", "{ 这不是 JSON ");

        var items = await new EdgeBookmarksSource(defaultPath)
            .FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task Fetch_IgnoresSiblingsThatAreNotBrowserProfiles()
    {
        // 只认 "Profile *"（Chromium 的用户配置命名）；System Profile 等目录不该被当书签来源。
        var defaultPath = WriteProfileBookmarks("Default", ValidJson);
        WriteProfileBookmarksFrom(defaultPath, "System Profile", SecondProfileJson);
        WriteProfileBookmarksFrom(defaultPath, "Profile 1", SecondProfileJson);

        var items = await new EdgeBookmarksSource(defaultPath)
            .FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Equal(4, items.Count);   // 3 + 仅 Profile 1 那 1 条
    }

    /// <summary>在同一个临时 User Data 根下再写一个 profile（复用上一行造的根目录）。</summary>
    private string WriteProfileBookmarksFrom(string existingProfilePath, string profileDir, string json)
    {
        var userData = Path.GetDirectoryName(Path.GetDirectoryName(existingProfilePath))!;
        var dir = Path.Combine(userData, profileDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Bookmarks");
        File.WriteAllText(path, json);
        return path;
    }
}
