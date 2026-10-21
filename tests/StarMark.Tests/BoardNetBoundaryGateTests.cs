using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板那一扇页的<b>出机边界</b>闸门（Phase C <b>片67</b>＝账本 <b>P-248</b> 的前半）。
/// <para>三档各钉一面：① <b>页面</b>把引擎自家那颗 <c>validateEmbeddable</c> 交 <c>false</c>（钉它住在<b>顶层 prop</b>
/// 那一层，而不是「串在档里某处」——挪进 <c>UIOptions</c> 里照样搜得到而引擎读不到＝静默失效）；
/// ② <b>Core</b> 那颗分类判据（本地／压根不出机／别处），带逐颗相反下落；
/// ③ <b>宿主</b>那一侧的订阅形状：先订再看、三颗计数各有出口、且<b>只量不挡</b>（挡不挡的下一翻要等真机名册）。
/// </para>
/// <para>⚠ 本批⛔ 起窗（桌面被占），所以「这页今天到底外呼谁」还是<b>欠读数</b>；这里能机检的只有形状与判据。</para>
/// </summary>
public sealed class BoardNetBoundaryGateTests
{
    private const string WindowCsRel = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string PolicyCsRel = "src/StarMark.Core/Boards/BoardsAssetPolicy.cs";
    private const string JsRel = "tools/boards/board-bridge.js";

    private static readonly string WindowCs = SourceGate.ReadRepoFile(WindowCsRel);
    private static readonly string Js = SourceGate.ReadRepoFile(JsRel);

    /// <summary>那颗虚拟站的名字⛔ 在测试里再抄一遍（抄＝第三份主人，换名字那一批这里静默失真）；
    /// 从宿主那颗常量<b>现读</b>，与 <c>TheIdleBeatIsOneNumberAcrossTheTwoLanguages</c> 同一口径。</summary>
    private static string VirtualHost => HostFromWindow();

    private static string HostFromWindow()
    {
        var m = Regex.Match(WindowCs, @"const string VirtualHostName = ""([^""]+)""");
        Assert.True(m.Success, "宿主那颗虚拟站常量找不到了（改名／换写法要连这一格一起改）");
        return m.Groups[1].Value;
    }

    private static Uri U(string s) => new(s, UriKind.Absolute);

    [Fact]
    public void LocalAssetUriAcceptsOurVirtualHostInAnyCaseAndAnyPath()
    {
        var h = VirtualHost;
        Assert.True(BoardsAssetPolicy.IsLocalAssetUri(U($"https://{h}/board.html"), h));
        Assert.True(BoardsAssetPolicy.IsLocalAssetUri(U($"https://{h}/fonts/Virgil/YvN.woff2"), h));
        // 方案与主机名都⛔ 区分大小写（<c>Uri</c> 自己会归一，但判据⛔ 依赖它归一＝换一颗解析器就漂）
        Assert.True(BoardsAssetPolicy.IsLocalAssetUri(U($"HTTPS://{h.ToUpperInvariant()}/x"), h));
    }

    [Fact]
    public void LocalAssetUriRefusesOtherHostsOtherSchemesAndLookAlikes()
    {
        var h = VirtualHost;
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U($"http://{h}/board.html"), h));         // 方案不对
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U("https://esm.sh/react"), h));            // 别的主机
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U($"https://{h}.cdn.example.com/x"), h));  // 后缀冒充
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U($"https://evil-{h}/x"), h));            // 前缀冒充
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U("https://example.com/x"), h));
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(null, h));                               // 解不出＝⛔ 本地（观察者那一头另有一档）
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U("https://x/y"), ""));                    // 没给名字⇒一颗也⛔ 算本地
    }

    [Fact]
    public void InlineSchemesAreTheirOwnClassNeitherLocalNorForeign()
    {
        foreach (var scheme in BoardsAssetPolicy.InlineUriSchemes)
        {
            var uri = U($"{scheme}:something");
            Assert.True(BoardsAssetPolicy.IsInlineUri(uri), scheme + " 该算「压根⛔ 出机」那一档");
            Assert.False(BoardsAssetPolicy.IsLocalAssetUri(uri, VirtualHost), scheme + " ⛔ 是随包那一份");
        }
        // ⚠ 那张表里混进一颗真走网络的方案＝画布白屏的配方（片21 的存储口径＝内嵌 base64，图全是 data:）
        Assert.DoesNotContain(BoardsAssetPolicy.InlineUriSchemes, s => s is "https" or "http" or "ws" or "wss");
        Assert.False(BoardsAssetPolicy.IsInlineUri(U("https://fonts.gstatic.com/x.css")));
        Assert.False(BoardsAssetPolicy.IsInlineUri(null));
    }

    [Fact]
    public void TraceTargetCarriesSchemeAndHostOnlyNeverAPath()
    {
        var foreign = U("https://fonts.gstatic.com/s/xiaolai.css?name=" + Uri.EscapeDataString("他的档名"));
        var described = BoardsAssetPolicy.DescribeRequestTarget(foreign);
        Assert.Equal("https://fonts.gstatic.com", described);
        Assert.DoesNotContain("xiaolai.css", described);
        Assert.DoesNotContain("他的档名", described);
        Assert.Equal("(无)", BoardsAssetPolicy.DescribeRequestTarget(null));
        // 一颗⛔ 出机的 data: 也走同一颗工厂：主机名为空时留一颗裸方案，读数仍然读得出它是哪一族
        Assert.Equal("data://", BoardsAssetPolicy.DescribeRequestTarget(U("data:image/png;base64,AAAA")));
    }

    [Fact]
    public void TheVirtualHostNameHasExactlyOneOwnerInTheProduct()
    {
        var hits = SourceGate.ReadRepoUnder("src")
            .Where(f => SourceGate.Code(f.Text).Contains(VirtualHost, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath)
            .ToList();
        Assert.Equal(new[] { WindowCsRel }, hits);
        // Core 那颗判据⛔ 认名字（它只收实参），所以随包判据那一档⛔ 需要第二份名字
        Assert.DoesNotContain(VirtualHost, SourceGate.Code(SourceGate.ReadRepoFile(PolicyCsRel)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostSubscribesTheObserverBeforeItNavigates()
    {
        var code = SourceGate.Code(WindowCs);   // 注释里那句「⛔ 在这里订 DownloadStarting」是<b>讲为什么</b>，⛔ 算订阅（#123）
        Assert.Equal(1, SourceGate.Count(code, "AddWebResourceRequestedFilter(\"*\", CoreWebView2WebResourceContext.All)"));
        Assert.Equal(1, SourceGate.Count(code, "WebResourceRequested -= OnWebResourceRequested"));
        Assert.Equal(1, SourceGate.Count(code, "WebResourceRequested += OnWebResourceRequested"));
        // 存在与顺序是两颗断言：一颗都没落定之前，「先订再看」只是愿望（漏看第一发＝名册缺第一行）
        Assert.True(code.IndexOf("WebResourceRequested += OnWebResourceRequested", StringComparison.Ordinal)
                    < code.IndexOf("_web.CoreWebView2.Navigate(", StringComparison.Ordinal),
            "那颗观察者排在 <c>Navigate</c> 之后＝第一发出机根本进不了尺");
    }

    [Fact]
    public void ObserverCountsThreeClassesAndBlocksNothing()
    {
        var body = SourceGate.MethodBody(WindowCs, "private void OnWebResourceRequested");
        // 三颗计数各有一条上屏出口（⛔ 一颗只加不念的死计数＝片23 摘 sessionId 那一族）
        foreach (var needle in new[] { "_netLocal++", "_netInline++", "_netForeign++" })
            Assert.Equal(1, SourceGate.Count(body, needle));
        foreach (var slot in new[] { "n=\" + _netForeign", "local=\" + _netLocal", "inline=\" + _netInline" })
            Assert.Contains(slot, body, StringComparison.Ordinal);
        // 这一批只量⛔ 挡：挡的那一翻要等真机名册（本批⛔ 起窗⇒欠读数）
        Assert.DoesNotContain(".Handled", body);
        Assert.DoesNotContain("e.Response", body);
        Assert.DoesNotContain("Cancel", body);
        // 分类照 Core 那颗判据，⛔ 在 UI 里再写一份方案／主机比较（第二个主人＝换判据时静默分岔）
        Assert.Contains("BoardsAssetPolicy.IsLocalAssetUri(uri, VirtualHostName)", body, StringComparison.Ordinal);
        Assert.Contains("BoardsAssetPolicy.IsInlineUri(uri)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("uri.Scheme ==", body, StringComparison.Ordinal);
        Assert.DoesNotContain("uri.Host ==", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeTurnsOffTheEngineEmbeddableValidator()
    {
        // 只问「那颗 prop 交的是⛔ 是 false」这一维；它住在哪一层归下一格（两臂各打红一颗＝名册 411）
        Assert.Equal(1, SourceGate.Count(StripJsCommentsAndStrings(Js), "validateEmbeddable: false"));
    }

    [Fact]
    public void ThatValidatorSitsAtTheTopLevelOfBaseProps()
    {
        var stripped = StripJsCommentsAndStrings(Js);
        Assert.Equal(1, BraceDepthOfBasePropsEntry(stripped, "validateEmbeddable:"));
        // 同一颗尺的<b>对照件</b>：顶层兄弟一颗也读回 1，而 UIOptions 里那颗读回 3——
        // 少了这两颗，「深度 1」只是我数出来的一句自证（名册 408：借来的尺射程要本格正件自证）
        Assert.Equal(1, BraceDepthOfBasePropsEntry(stripped, "excalidrawAPI:"));
        Assert.Equal(3, BraceDepthOfBasePropsEntry(stripped, "image: false"));
    }

    /// <summary><c>baseProps()</c> 返回的那颗对象里，这一条落在第几层花括号（1＝与 <c>excalidrawAPI</c>／<c>onChange</c>
    /// 同层＝Excalidraw 的<b>顶层 prop</b>）。⛔ 拿缩进当层级（格式化一遍就假失败），也⛔ 只问「那串在不在档里」
    /// （挪进 <c>UIOptions</c> 照样「在」，而引擎读的是顶层那颗＝静默失效）。</summary>
    private static int BraceDepthOfBasePropsEntry(string stripped, string token)
    {
        var fn = stripped.IndexOf("function baseProps()", StringComparison.Ordinal);
        Assert.True(fn >= 0, "baseProps() 找不到了（改名／换写法要连这一格一起改）");
        var open = stripped.IndexOf("return {", fn, StringComparison.Ordinal);
        Assert.True(open >= 0, "baseProps() 里那颗返回对象字面量找不到了");
        var at = stripped.IndexOf(token, open, StringComparison.Ordinal);
        var depth = 0;
        for (var i = open; i < stripped.Length; i++)
        {
            if (i == at) return depth;
            var c = stripped[i];
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
                throw new InvalidOperationException("「" + token + "」落在 baseProps() 返回的对象之外（at=" + at + "）");
        }
        throw new InvalidOperationException("baseProps() 那颗对象没收尾（花括号数不齐）");
    }

    /// <summary>抹掉 JS 注释与字符串字面量、<b>长度不变</b>（保住索引，好让上面那颗尺按位置数层）。
    /// 换行留着：行号形状与原文一致，报错时人对得上现场。</summary>
    private static string StripJsCommentsAndStrings(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                { sb.Append(text[i] == '\n' ? '\n' : ' '); i++; }
                sb.Append("  "); i += 2;
                continue;
            }
            if (c is '"' or '\'' or '`')
            {
                sb.Append(' '); i++;
                while (i < text.Length && text[i] != c)
                {
                    if (text[i] == '\\') { sb.Append(' '); i++; }
                    sb.Append(text[i] == '\n' ? '\n' : ' '); i++;
                }
                if (i < text.Length) { sb.Append(' '); i++; }
                continue;
            }
            sb.Append(c); i++;
        }
        return sb.ToString();
    }
}
