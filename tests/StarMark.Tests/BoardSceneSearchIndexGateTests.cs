#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Core.Boards;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// 「场景 → 可检索」那一行（集成矩阵第五行＝Phase C 片146）：一块板上用户写过的字，
/// 今天必须能在收藏系统里被搜到——这是"桌面版凭什么是桌面版"那句话里唯一与"图"有关的一半。
/// <para>两头分别守：<b>判据那头</b>（<see cref="BoardPolicy.SceneTextDigest"/>）直接喂真形状的场景档，
/// 逐颗问它交出什么；<b>接线那头</b>（UI 层）由源码钉，因为测试工程⛔ 引用 <c>StarMark.UI</c>，
/// 而这一行的坏法恰恰是"判据全对而没人调用它"（记忆 ⑧：把判据搬进可测那一层⛔ 等于调用那一头自动正确）。</para>
/// </summary>
public sealed class BoardSceneSearchIndexGateTests : IDisposable
{
    private const string IndexPath = "src/StarMark.UI/Boards/BoardSceneIndex.cs";
    private const string WindowPath = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string ActionsPath = "src/StarMark.UI/Helpers/ItemCardActions.cs";
    private const string AppPath = "src/StarMark.UI/App.xaml.cs";
    private const string StorePath = "src/StarMark.UI/Boards/BoardSceneStore.cs";

    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public BoardSceneSearchIndexGateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_board_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    // ─────────────── ① 那颗正文抽取：吃什么、⛔ 吃什么 ───────────────

    /// <summary>一颗真形状的场景档（文本／画框名／箭头标签／被删的那颗／没字的形状各一颗）。</summary>
    private static byte[] Scene(params string[] elements)
        => Encoding.UTF8.GetBytes("""{"type":"excalidraw","version":2,"source":"https://excalidraw.com","elements":["""
            + string.Join(",", elements) + """],"appState":{"viewBackgroundColor":"#ffffff","gridSize":null},"files":{}}""");

    private const string TextElement = """
        {"id":"t1","type":"text","x":10,"y":20,"width":160,"height":45,"angle":0,"strokeColor":"#1e1e1e",
         "backgroundColor":"transparent","fillStyle":"solid","strokeWidth":2,"roughness":1,"opacity":100,
         "groupIds":[],"frameId":null,"index":"a0","roundness":null,"seed":1234,"version":1,"versionNonce":5678,
         "isDeleted":false,"updated":1730000000000,"link":null,"locked":false,
         "text":"周末去野餐","originalText":"周末去野餐","fontSize":28,"fontFamily":1,"containerId":null,"autoResize":true}
        """;
    private const string BoundLabelElement = """
        {"id":"t2","type":"text","x":40,"y":60,"width":60,"height":20,"angle":0,"seed":2222,"version":3,
         "isDeleted":false,"link":null,"locked":false,
         "text":"带伞","originalText":"带伞","fontSize":20,"fontFamily":1,"containerId":"arr1"}
        """;
    private const string FrameElement = """
        {"id":"f1","type":"frame","x":0,"y":0,"width":400,"height":300,"angle":0,"seed":3333,"version":2,
         "isDeleted":false,"link":null,"locked":false,"name":"左侧备忘"}
        """;
    private const string ArrowElement = """
        {"id":"arr1","type":"arrow","x":10,"y":10,"width":100,"height":50,"angle":0,"seed":4444,"version":2,
         "isDeleted":false,"link":null,"locked":false,"points":[[0,0],[100,50]],"lastCommittedPoint":null,
         "startArrowhead":null,"endArrowhead":"arrow","boundElements":[{"id":"t2","type":"text"}]}
        """;
    private const string DeletedTextElement = """
        {"id":"t9","type":"text","x":1,"y":1,"width":10,"height":10,"angle":0,"seed":9999,"version":9,
         "isDeleted":true,"link":null,"locked":false,"text":"已经擦掉的草稿","originalText":"已经擦掉的草稿"}
        """;
    private const string ImageElement = """
        {"id":"im1","type":"image","x":5,"y":5,"width":200,"height":120,"angle":0,"seed":7777,"version":2,
         "isDeleted":false,"link":"https://example.com/a.png","locked":false,"fileId":"abc123","status":"saved"}
        """;

    [Fact]
    public void SceneTextDigest_TakesCanvasTextAndFrameNamesInDocumentOrder()
    {
        var digest = BoardPolicy.SceneTextDigest(Scene(TextElement, FrameElement, ArrowElement, BoundLabelElement, ImageElement));
        // 箭头那颗⛔ 是正文（它没有文字属性），标签那颗本来就是 type:"text"（由 containerId 指回）⇒ 已经在串里；
        // 图那一颗的 link 是 URL⛔ 用户写的字 ⇒ 不吃（要搜外链归链接那一族）。
        Assert.Equal("周末去野餐 左侧备忘 带伞", digest);
    }

    [Fact]
    public void SceneTextDigest_NeverCarriesAnErasedElement()
    {
        var digest = BoardPolicy.SceneTextDigest(Scene(TextElement, DeletedTextElement))!;
        Assert.DoesNotContain("已经擦掉的草稿", digest, StringComparison.Ordinal);
        // ⚠ 这一句才是那颗证人存在过的正件：被删的那颗如果进了串，上面那句当场红。
        Assert.Equal("周末去野餐", digest);
    }

    [Fact]
    public void SceneTextDigest_FallsBackToTextWhenOriginalTextIsMissingOrBlank()
    {
        var onlyText = """{"type":"excalidraw","elements":[{"id":"t","type":"text","isDeleted":false,"text":"只带 text 的一颗"}]}""";
        Assert.Equal("只带 text 的一颗",
            BoardPolicy.SceneTextDigest(Encoding.UTF8.GetBytes(onlyText)));
        var blankOriginal = """{"type":"excalidraw","elements":[{"id":"t","type":"text","text":"退到这一颗","originalText":"   "}]}""";
        Assert.Equal("退到这一颗",
            BoardPolicy.SceneTextDigest(Encoding.UTF8.GetBytes(blankOriginal)));
    }

    /// <summary>
    /// 「这块板一个字也没有」与「这块板我读不懂」⛔ 许折成同一颗答案——前者交空串（真该入库，只是没正文），
    /// 后者交 null（调用方因此⛔ 动那一行，见 <c>BoardSceneIndex.RegisterAsync</c>）。四档读不懂逐档问。
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"type":"excalidraw"}""")]
    [InlineData("""{"type":"excalidraw","elements":{"id":"t"}}""")]
    public void SceneTextDigest_ReportsUnreadableAsNullNotAsEmpty(string doc)
        => Assert.Null(BoardPolicy.SceneTextDigest(Encoding.UTF8.GetBytes(doc)));

    [Fact]
    public void SceneTextDigest_ReportsAWritableButEmptyBoardAsAnEmptyStringNotNull()
    {
        Assert.Equal(string.Empty, BoardPolicy.SceneTextDigest(Scene()));
        Assert.Equal(string.Empty, BoardPolicy.SceneTextDigest(Encoding.UTF8.GetBytes(
            """{"type":"excalidraw","elements":[{"id":"r","type":"rectangle","isDeleted":false}]}""")));
        // ⛔ 一颗字节也没有／半截档＝读不懂，⛔ 是"空板"——那一档走上面那颗 Theory。
        Assert.Null(BoardPolicy.SceneTextDigest(Array.Empty<byte>()));
        Assert.Null(BoardPolicy.SceneTextDigest(null));
    }

    [Fact]
    public void SceneTextDigest_TruncatesAtTheCeilingItDeclaresWithoutAnEllipsis()
    {
        var big = new string('字', BoardPolicy.SceneTextDigestMaxChars + 500);
        var element = "{\"id\":\"t\",\"type\":\"text\",\"isDeleted\":false,\"originalText\":\"" + big + "\"}";
        var digest = BoardPolicy.SceneTextDigest(Scene(element))!;
        Assert.Equal(BoardPolicy.SceneTextDigestMaxChars, digest.Length);
        Assert.DoesNotContain("…", digest, StringComparison.Ordinal);   // 那三个字符会进索引，而这一列⛔ 直接上屏
    }

    // ─────────────── ② 那一行真的进得了全文索引（真库一轮） ───────────────

    /// <summary>
    /// 本行要交付的东西只有一句可以屏幕上验的话：<b>在画布上写过的中文，能在收藏系统里搜到那块板</b>。
    /// 这里拿真库跑（⛔ mock）：口径的主人正是那颗仓储——所以"能不能搜到"必须由它回答，⛔ 由我复述。
    /// </summary>
    [Fact]
    public async Task ABoardRowIsFoundByAChinesePhraseWrittenOnTheCanvas()
    {
        var repo = new ItemRepository(_factory);
        var digest = BoardPolicy.SceneTextDigest(Scene(TextElement, BoundLabelElement))!;
        var row = new Item
        {
            Type = ItemType.Board,
            Source = ItemSources.Board,
            SourceId = "261030-120000-abcd",
            Title = "261030-120000-abcd",
            Uri = LocalFileIdentity.UriForPath(Path.Combine("D:", "boards", "261030-120000-abcd.excalidraw")),
            Description = digest,
        };
        await repo.RecordItemAsync(row, CancellationToken.None);

        // 中间子串（unicode61 会把连续中文当一颗 token ⇒⛔ 展开就搜不到），而⛔ 是开头那颗词
        var hits = (await repo.SearchAsync("野餐", new SearchFilter { MaxResults = 20 }, CancellationToken.None)).Items;
        Assert.Contains(hits, h => h.Source == ItemSources.Board && h.SourceId == "261030-120000-abcd");
        Assert.Equal(ItemType.Board, hits.Single(h => h.SourceId == "261030-120000-abcd").Type);
    }

    // ─────────────── ③ 接线那头（UI 层只能由源码钉） ───────────────

    [Fact]
    public void TheBoardRowGoesThroughTheSingleSearchTextOwner_AndNeverBuildsItsOwnIndex()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(IndexPath));
        Assert.Contains("RecordItemAsync", code, StringComparison.Ordinal);      // 那颗唯一口径
        Assert.DoesNotContain("SearchText", code, StringComparison.Ordinal);     // 第二主人＝在这里自己拼索引
        Assert.DoesNotContain("CjkTokenizer", code, StringComparison.Ordinal);
        // Draft 每一列吃什么（⛔ 落库一句上屏话：Title 就是那颗 id，它与档名同一个东西）
        Assert.Contains("Type = ItemType.Board", code, StringComparison.Ordinal);
        Assert.Contains("Source = ItemSources.Board", code, StringComparison.Ordinal);
        Assert.Contains("Title = sceneId", code, StringComparison.Ordinal);
        Assert.Contains("Description = digest", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSaveBeatFeedsTheIndex_BehindTheSameSerialGate_AndCannotBendLanded()
    {
        // 锚点吃到那颗方法自己的签名（带参数表那一串）：文件里另有 <c><see cref="QueueSceneWrite"/></c> 的引用，
        // 只写方法名会命中 2 处⇒ 闸门按口径直接抛（它⛔ 猜哪一颗才是本体，SourceGate.MethodBody 那句自述）。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(WindowPath),
            "Task<SceneWrite> QueueSceneWrite(byte[] bytes)");
        Assert.Contains("BoardSceneIndex.RegisterAfterSave", body, StringComparison.Ordinal);
        // 那一手排在<b>同一颗串行闸的体内</b>（两发写库互踩＝两发写盘互踩换了个地方）
        Assert.Contains("BoardSceneStore.TrySave", body, StringComparison.Ordinal);
        // ⛔ 把索引件的结果拧进那颗 Ok：它说的是"用户的笔迹在不在盘上"
        Assert.Contains("new SceneWrite(ok, fault, rolledBackup, bytes)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnreadableSceneLeavesTheLastRowAlone_AndSaysSoInTheLog()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(IndexPath));
        Assert.Contains("boards.index.scene-unreadable", code, StringComparison.Ordinal);
        // 读不懂⇒不动那一行：交 false 之前⛔ 有任何一次写库
        var register = SourceGate.MethodBody(code, "Task<bool> RegisterAsync");
        Assert.Contains("return false", register, StringComparison.Ordinal);
        Assert.True(register.IndexOf("scene-unreadable", StringComparison.Ordinal)
                    < register.IndexOf("RecordItemAsync", StringComparison.Ordinal),
            "那颗「读不懂」必须在写库之前收口，否则旧正文被空串顶掉＝一块有字的板当场查无此板");
    }

    [Fact]
    public void OpeningABoardRowGoesToTheCanvas_NotToTheShellAssociation()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(ActionsPath), "Open(XamlRoot xamlRoot, long itemId)");
        Assert.Contains("BoardPolicy.OpensAsScene", body, StringComparison.Ordinal);
        Assert.Contains("BoardsWindows.OpenSceneAsync(item.SourceId)", body, StringComparison.Ordinal);
        Assert.Contains("LauncherEx.TryOpenDetailedAsync(item.Uri)", body, StringComparison.Ordinal);
        // 顺序也要钉：那颗分支如果落在系统打开之后，"点开一块板"已经先弹出记事本了（存在⛔ 等于走在前面）
        Assert.True(body.IndexOf("OpensAsScene", StringComparison.Ordinal)
                    < body.IndexOf("LauncherEx.TryOpenDetailedAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningABoardRowReportsFailureWhereTheUserCanSeeIt()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(ActionsPath), "Open(XamlRoot xamlRoot, long itemId)");
        Assert.Contains("boards.openFailedTitle", body, StringComparison.Ordinal);
        Assert.Contains("boards.openFailedBody", body, StringComparison.Ordinal);
        // ⛔ 新表：那两句托盘那两颗入口已经在用（加一颗键＝两句话指同一发坏）
        Assert.Equal(0, SourceGate.Count(body, "card.open.fail"));
    }

    [Fact]
    public void TheStartupBackfillIsWiredAndYieldsTheFirstScreen()
    {
        var app = SourceGate.Code(SourceGate.ReadRepoFile(AppPath));
        Assert.Equal(1, SourceGate.Count(app, "BoardSceneIndex.BackfillAsync"));
        // 只接"从现在起写的那一发"＝用户手上的旧板永远搜不到（本批要交付的正是那些旧板）
        Assert.Contains("await Task.Delay(6000);", app, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoardsFolderNameHasOneOwnerAcrossBothEnds()
    {
        // 落点那一段名字（"boards"）⛔ 许在两处各写一遍：导出带一棵树、载入读另一棵＝用户看着"备份成功"而那颗板没跟过去
        var all = SourceGate.ReadRepoUnder("src");
        var offenders = all.Where(f => SourceGate.Code(f.Text).Contains("""Subdirectory("boards")""", StringComparison.Ordinal))
                           .Select(f => f.RelativePath).ToList();
        Assert.Empty(offenders);
        // 那颗成品路径从此只住 Core 的一位主人（UI 的存储档只转发）：备份链与写入两头问的是同一棵树
        Assert.Contains("ScenesFolder() => StarMark.Abstractions.UserDataPaths.Subdirectory(BoardsDirectoryName)",
            SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Core/Boards/BoardPolicy.cs")), StringComparison.Ordinal);
        Assert.Contains("Folder => BoardPolicy.ScenesFolder()",
            SourceGate.Code(SourceGate.ReadRepoFile(StorePath)), StringComparison.Ordinal);
    }

    // ─────────────── ④ 那颗新类型本身：序号、来源、图标 ───────────────

    [Fact]
    public void BoardIsAppendedToItemType_WithItsOwnWireOrdinal_AndSourceThatNeverSyncs()
    {
        Assert.Equal(6, (int)ItemType.Board);
        var ordinals = Enum.GetValues<ItemType>().Select(t => (int)t).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(0, ordinals.Count), ordinals);   // 无缝 ⇒ 备份里那颗整数不会重映射
        Assert.Equal("board", ItemSources.Board);
        Assert.Equal("🎨", ItemCardPolicy.GlyphOf(ItemType.Board));
    }

    [Fact]
    public void TheBoardFamilyOpensOnlyAsACanvas_AndNothingElseClaimsThatType()
    {
        Assert.True(BoardPolicy.OpensAsScene(ItemType.Board));
        Assert.False(BoardPolicy.OpensAsScene(ItemType.Note));
        Assert.False(BoardPolicy.OpensAsScene(ItemType.File));
        Assert.False(BoardPolicy.OpensAsScene(ItemType.Clipboard));   // 剪贴板那一路归 ClipboardPolicy.OpensAsCopy
    }
}
