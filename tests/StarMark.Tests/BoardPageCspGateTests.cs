using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 随包那颗页的第一道 CSP 那面闸门（Phase C 片71）：形状由深析 §三十二那六臂量出来，这一档把<b>落下来的那一颗</b>钉住。
/// 射程＝那颗 <c>meta http-equiv</c> 在不在・排不排在所有脚本与样式之前・里面是哪几维・
/// 资源那一族（片70 现读会<b>新开</b>外呼的那一族）有没有被顺手加回来・以及那颗 <c>feature.flags</c> 的整颗替换前提。
/// 判据的出处：<c>frame-src／object-src／base-uri／form-action</c> 那一颗与基线<b>逐颗同形</b>（帧同一颗・请求 7↔7・违反 0），
/// 而资源那一族四臂各让页面多发 230 颗外站字体请求——所以它⛔ 许回来，这条禁项替那句话守著。
/// </summary>
public sealed class BoardPageCspGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string ReadRepo(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative));

    private const string PageRel = "tools/boards/board.html";
    private const string BridgeJs = "tools/boards/board-bridge.js";
    private const string WindowCs = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string RegistryCs = "src/StarMark.UI/Boards/BoardsWindows.cs";

    /// <summary>片70 六臂里唯一与基线逐颗同形的那一颗（深析 §32.2 表 G 行）。</summary>
    private static readonly string[] Allowed = ["frame-src", "object-src", "base-uri", "form-action"];

    /// <summary>资源那一族：片70 现读每一臂都让页面多发 230 颗外站字体请求＝它挡路的同时<b>新开路</b>。</summary>
    private static readonly string[] Forbidden =
        ["default-src", "script-src", "script-src-elem", "style-src", "style-src-elem", "img-src", "font-src", "connect-src", "worker-src", "manifest-src"];

    private static int Count(string hay, string needle)
    {
        int n = 0, at = 0;
        while ((at = hay.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }

    /// <summary>抹掉 HTML 注释（禁项扫描要用这一份：注释里提到某维⛔ 等于页挂上了那一维）。</summary>
    private static string WithoutComments(string html) => Regex.Replace(html, @"<!--[\s\S]*?-->", string.Empty);

    private static string CspContent(string html)
    {
        var m = Regex.Match(html, "<meta\\s+http-equiv=\"Content-Security-Policy\"\\s+content=\"([^\"]*)\"\\s*>");
        Assert.True(m.Success, "随包那颗页里那颗 CSP meta 找不到了（改名／换写法要连这一格与片70 那批读数一起重看）");
        return m.Groups[1].Value;
    }

    [Fact]
    public void PageCarriesExactlyOneCspMeta()
    {
        var html = ReadRepo(PageRel);
        Assert.Equal(1, Count(html, "http-equiv=\"Content-Security-Policy\""));
        Assert.Equal(1, Count(WithoutComments(html), "Content-Security-Policy"));
        // 这颗页里 <c><meta></c> 恰两颗（charset＋那颗 CSP）＝没人往头部再塞第三颗策略／刷新一发
        Assert.Equal(2, Count(html, "<meta"));
    }

    [Fact]
    public void CspMetaComesBeforeEveryScriptAndStyleOnThePage()
    {
        var html = ReadRepo(PageRel);
        int csp = html.IndexOf("Content-Security-Policy", StringComparison.Ordinal);
        Assert.True(csp >= 0, "那颗 CSP 压根不在页上");
        // 存在与顺序两颗断言分开写（名册 452：缺席即恒真的顺序断言⛔ 是尺）
        Assert.True(html.IndexOf("<style", StringComparison.Ordinal) > csp, "内联样式排在那颗 CSP 之前＝它护不到这一发");
        Assert.True(html.IndexOf("<script", StringComparison.Ordinal) > csp, "脚本排在那颗 CSP 之前＝它护不到这一发");
        Assert.True(html.IndexOf("<link", StringComparison.Ordinal) > csp, "样式表排在那颗 CSP 之前＝它护不到这一发");
    }

    [Fact]
    public void CspHasExactlyTheFourDirectivesMeasuredEquivalentToTheBaseline()
    {
        var content = CspContent(ReadRepo(PageRel));
        var parts = content.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Allowed.Length, parts.Length);
        foreach (var p in parts)
        {
            var kv = p.Split(' ', 2, StringSplitOptions.TrimEntries);
            Assert.Equal(2, kv.Length);
            Assert.Contains(kv[0], Allowed);
            Assert.Equal("'none'", kv[1]);
        }
        Assert.Equal(parts.Length, parts.Select(p => p.Split(' ')[0]).Distinct().Count());   // 同一维⛔ 许写两遍
    }

    [Fact]
    public void ResourceDimensionDirectivesStayOutOfThePage()
    {
        var html = ReadRepo(PageRel);
        var bare = WithoutComments(html);
        Assert.True(bare.Length < html.Length, "那颗抹注释的尺在这份页上没抹掉任何东西＝禁项扫描其实读的是原文（名册 458 那一族）");
        foreach (var d in Forbidden)
            Assert.Equal(0, Count(bare, d));
        // 那颗 meta 里也只许有允许的那几维（逐 token 过一遍＝⛔ 拿整颗串比，写法一改就漏）
        foreach (var token in CspContent(html).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(token.Split(' ')[0], Allowed);
    }

    [Fact]
    public void TheWholeUiOptionsReplacementPremiseIsPinned()
    {
        var js = ReadRepo(BridgeJs);
        // 桥那一手交的是<b>整颗</b> UIOptions 替换（⛔ 合并）：这是那颗「带全」要求的前提，写法一换这一格先响
        Assert.Equal(1, Count(js, "UIOptions: m.args && m.args.uiOptions"));
        var bare = Regex.Replace(js, @"/\*[\s\S]*?\*/|//[^\n]*", string.Empty);
        Assert.Equal(1, Count(bare, "UIOptions: m.args && m.args.uiOptions"));
        // 页面自己那份开场 UIOptions 的键（宿主一旦发那颗 feature.flags 就得带全这些）
        var keys = Regex.Matches(js, @"(loadScene|saveToActiveFile|export|saveAsImage|clearCanvas|changeViewBackgroundColor|toggleTheme)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(7, keys.Count);
        Assert.Contains("image: false", js, StringComparison.Ordinal);
    }

    [Fact]
    public void HostCurrentlySendsNoFeatureFlagsAtAll()
    {
        // 「带全」那颗证人在今天的形状＝宿主一颗也不发（BoardPolicy 那两句注释的前提）；一旦有人开始发，这一格先红，
        // 逼那一刀把上面那颗整颗替换的键名册一起带上（⛔ 让半份 UIOptions 盖掉页面那份＝把关掉的入口开回来）。
        foreach (var rel in new[] { WindowCs, RegistryCs, "src/StarMark.UI/Boards/BoardEnvelope.cs" })
        {
            var src = ReadRepo(rel);
            Assert.Equal(0, Count(src, "\"feature.flags\""));
            Assert.DoesNotContain("PostFeatureFlags", src, StringComparison.Ordinal);
        }
        Assert.Contains("\"feature.flags\"", ReadRepo("src/StarMark.Core/Boards/BridgeProtocol.cs"), StringComparison.Ordinal);
    }
}
