#nullable enable
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 指向<b>随包那颗冻结树</b>的行号指针闸门（账本 P-250・Phase C 片85）。
/// <para>
/// <b>为什么单独立一把而⛔ 并进 <see cref="LedgerPointerGateTests"/></b>：UK 那把尺问的是「那颗档还找得到吗」，
/// 它<b>故意⛔ 查行号</b>（那一档的理由写得很清楚：行号随任何一次合法编辑漂移，罚它＝每批回来改文档）。
/// 而本条量的正好是<b>那一族唯一的例外</b>：随包那颗桥／页／bundle 的字节由清单钉死（<c>BoardsAssetGateTests</c>），
/// 可那颗桥的<b>注释</b>一改，正文里「<c>board-bridge.js:207</c>」这种号就把人领到另一发上去——
/// 片71 十发锚普查「35 颗引用只有 6 颗落在现读行上」量的就是这件事（正本 <c>bin/WI98/pointers71_census.txt</c>）。
/// </para>
/// <para>
/// <b>射程＝"下一手要照着做"的那三面</b>：账本＋深析＋画板那三档代码注释。而<b>批次快照⛔ 在射程内</b>
/// （报告／坑表里那些号是<b>当时</b>的现场，拿今天去罚它们＝逼下一手改写证据——UK 卷首那句同一口径）。
/// ⚠ 这颗射程是<b>本批现读之后扩的</b>：起手只写了账本＋三档注释，而 <c>bin/WI99/pointers85_census_r3.txt</c>
/// 量到深析里还住 16 颗指向冻结树的号（P-250 那颗现场点名的六档之一，且它每批都在被改）⇒
/// 本批把它纳进射程并同批改口。那把普查尺起手⛔ 认尾段简写、又⛔ 认目录前缀，两副缺口各造过一批假读数。
/// 结论：新句子一律写<b>可搜的锚</b>（那颗字面串＋它所在那颗函数），行号只留在 <c>bin/**</c> 的正本里。
/// </para>
/// <para>
/// ⚠ 这把尺自己也有<b>三颗正件</b>（⛔ 一把只数 0 的尺）：① 那颗正则对一颗已知形状必须命中一次（它⛔ 死）；
/// ② 本批改口后写进正文的每一颗锚，必须在<b>它指向的那一颗档</b>里逐字读得到——否则"改口"只是把号换成一串搜不到的字；
/// ③ 那颗尺⛔ 扫本档自己（正则与注释里那些形状是尺的词汇，⛔ 是正文指针）。
/// </para>
/// </summary>
public sealed class FrozenTreePointerGateTests
{
    /// <summary>射程内的活档（账本＋深析＋画板那三档代码注释）。</summary>
    private static readonly string[] Living =
    {
        "docs/待决策事项.md",
        "docs/excalidraw整合可行性深析.md",
        "src/StarMark.Core/Boards/BoardPolicy.cs",
        "src/StarMark.Core/Boards/BridgeProtocol.cs",
        "src/StarMark.UI/Boards/BoardWindow.cs",
    };

    /// <summary>随包那颗冻结树（字节由清单钉死的那五颗）＋那颗桥的两份源码所在树。</summary>
    private static readonly Regex LinePointer = new(
        @"[\w./-]*(?:board-bridge\.js|board\.html|excalidraw\.min\.js|index\.css|manifest\.json)\s*[:：]\s*[0-9]",
        RegexOptions.Compiled);

    /// <summary>片85 改口之后正文该念的那些锚——每一颗都必须在随包那颗桥里逐字在场。</summary>
    private static readonly string[] BridgeAnchors =
    {
        "function scheduleSave()",
        "AUTOSAVE_MS",
        "function runExport(",
        "var opts = {",
        "appState: Object.assign({}, API.getAppState(), prefs),",
        "function boardName()",
        "var toBlob = kind === \"svg\"",
        "function onOut(e)",
        "tools: { image: false }",
        "case \"feature.flags\":",
        "onLinkOpen: function (element, event)",
        "function baseProps()",
        "cmd: \"bridge.reply\"",
        "postIn(\"export.bytes\"",
        "postIn(\"board.loaded\"",
        "postIn(\"bridge.ready\"",
        "onChange: function",
        "validateEmbeddable: false",
    };

    /// <summary>那一族锚住在<b>随包 bundle</b>（⛔ 那颗桥）里：深析 §三十 那一路逐字引的就是它们。</summary>
    private static readonly string[] BundleAnchors =
    {
        "window.open(void 0,h)",
        "let h=kF(d)?",
        "h.defaultPrevented&&D.preventDefault()",
        "new CustomEvent(A,{detail:{nativeEvent:u},cancelable:!0}",
    };

    private static string Read(string rel)
    {
        var path = Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), "闸门射程里那颗档读不到＝先改这颗尺（⛔ 让" + rel + "缺席变成恒绿）：" + path);
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new FileInfo(typeof(FrozenTreePointerGateTests).Assembly.Location).Directory!;
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? System.AppContext.BaseDirectory;
    }

    /// <summary>① 禁项：活档里指向随包那颗冻结树的「档名＋冒号＋行号」一颗也⛔ 有。</summary>
    [Fact]
    public void LivingSurfacesNeverPointIntoTheFrozenTreeByLineNumber()
    {
        foreach (var rel in Living)
        {
            var hits = LinePointer.Matches(Read(rel)).Select(m => m.Value.Trim()).ToArray();
            Assert.True(hits.Length == 0,
                rel + " 里有 " + hits.Length + " 颗指向随包冻结树的行号指针（那些字节由清单钉死、注释一改号就漂）："
                + string.Join("、", hits.Take(6)) + " ⇒ 换成可搜的锚（那颗字面串＋它所在那颗函数），行号只留在 bin/** 正本里");
        }
    }

    /// <summary>② 那颗⛔ 是死尺：已知形状必须命中一次，且已知的那几档读得到、非空。</summary>
    [Fact]
    public void TheRulerItselfBitesOnAKnownShape()
    {
        Assert.Single(LinePointer.Matches("桥里 <c>board-bridge.js:207</c> 递的正是").Select(m => m.Value));
        Assert.Single(LinePointer.Matches("<c>assets/boards/excalidraw.min.js:467</c>").Select(m => m.Value));
        Assert.Single(LinePointer.Matches("那颗页 board.html：12 行").Select(m => m.Value));   // 全角冒号也认
        Assert.Empty(LinePointer.Matches("那颗桥 board-bridge.js 递的正是 tools").Select(m => m.Value));
        Assert.Empty(LinePointer.Matches("BoardWindow.cs:412 那一发").Select(m => m.Value));   // 冻结树之外⛔ 归本条
        foreach (var rel in Living)
            Assert.True(Read(rel).Length > 1000, rel + " 现读⛔ 像一份真档＝那颗「0 颗」的读数⛔ 可信");
    }

    /// <summary>③ 改口⛔ 许改成一串搜不到的字：每颗锚同时住在正文与<b>它指向的那一颗档</b>里。</summary>
    [Fact]
    public void EveryRewrittenAnchorIsStillFindableInTheBridgeItPointsAt()
    {
        var bridge = Read("tools/boards/board-bridge.js");
        Assert.True(bridge.Length > 10000, "随包那颗桥现读⛔ 像一份真档＝这颗对账⛔ 可信");
        var bundle = Read("assets/boards/excalidraw.min.js");
        Assert.True(bundle.Length > 4000000, "随包那颗 bundle 现读⛔ 像一份真档＝那颗「逐字在场」⛔ 可信");
        var body = string.Concat(Living.Select(Read));
        Assert.True(body.Length > 1000000, "射程那五档正文合起来现读⛔ 像真档＝「正文念它」那一半⛔ 可信");
        foreach (var anchor in BridgeAnchors)
        {
            Assert.Contains(anchor, bridge, System.StringComparison.Ordinal);
            Assert.True(body.Contains(anchor, System.StringComparison.Ordinal),
                "那颗锚「" + anchor + "」在桥里读得到，可正文一处也没念它＝改口没落到正文上（这颗尺的两半要同时在场）");
        }
        foreach (var anchor in BundleAnchors)
        {
            Assert.Contains(anchor, bundle, System.StringComparison.Ordinal);
            Assert.True(body.Contains(anchor, System.StringComparison.Ordinal),
                "那颗 bundle 里的逐字「" + anchor + "」正文一处也没念＝那颗引用又退回只给号不给锚");
        }
        // 名单颗数那颗⛔ 是判据（名册 414：数自己那份名单的断言恒绿）；它只许在名单变动时逼我重新对一次账。
        Assert.True(BridgeAnchors.Length == 18 && BundleAnchors.Length == 4,
            "锚名单颗数变了（桥 " + BridgeAnchors.Length + "・bundle " + BundleAnchors.Length
            + "）＝射程动过，先回来把这一颗改口与正文对齐");
    }
}
