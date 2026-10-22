using System;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板那一扇页的<b>出机边界</b>闸门（Phase C <b>片67</b>＝账本 <b>P-248</b> 的前半・<b>片69</b>＝后半）。
/// <para>三档各钉一面：① <b>页面</b>把引擎自家那颗 <c>validateEmbeddable</c> 交 <c>false</c>（钉它住在<b>顶层 prop</b>
/// 那一层，而不是「串在档里某处」——挪进 <c>UIOptions</c> 里照样搜得到而引擎读不到＝静默失效）；
/// ② <b>Core</b> 那颗分类判据（本地／压根不出机／别处／该不该挡），带逐颗相反下落；
/// ③ <b>宿主</b>那一侧的订阅形状：三颗门各有主人（出站桩・新窗・顶帧导航）、先订再看、计数各有出口。
/// </para>
/// <para>⚠ 片69 改口两处：<b>「只量⛔ 挡」</b>那句随名册落地一起退场（读数正本 <c>bin/WI98/roster69_r4</c>・
/// <c>roster69_r3</c>・正件 <c>roster69_ctl3</c>），而那颗分类判据<b>⛔ 再比方案</b>——那颗虚站映射按<b>站名</b>
/// 劫持，带端口带 http 也算本地供（由目录里那颗<b>独有</b>档名读回，⛔ 靠"服务器没记"那种负证据）。</para>
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
        // 片69 现读补的两颗<b>同族形状</b>（名字里那颗 InAnyCase 涵盖不到它们，而它们正是那颗映射的射程）：
        Assert.True(BoardsAssetPolicy.IsLocalAssetUri(U($"http://{h}/board.html"), h));
        Assert.True(BoardsAssetPolicy.IsLocalAssetUri(U($"https://{h}:8443/board.html"), h));
    }

    [Fact]
    public void LocalAssetUriRefusesOtherHostsAndLookAlikesNotSchemes()
    {
        var h = VirtualHost;
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U("https://esm.sh/react"), h));            // 别的主机
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U($"https://{h}.cdn.example.com/x"), h));  // 后缀冒充
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U($"https://evil-{h}/x"), h));            // 前缀冒充
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U("https://example.com/x"), h));
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(null, h));                               // 解不出＝⛔ 本地（挡不挡那一档另说）
        Assert.False(BoardsAssetPolicy.IsLocalAssetUri(U("https://x/y"), ""));                    // 没给名字⇒一颗也⛔ 算本地
        // ⚠ 「方案不对」那一颗<b>已由片69 的读数撤掉</b>（<c>http://127.0.0.1:8099</c> 那颗真请求由那颗映射按<b>站名</b>
        // 供的档＝目录里独有那颗档名读回）。这里⛔ 再钉它一次，是因为<b>钉错了的那一句</b>比没钉更贵：
        // 它会在下一次换宿主时把「本地供」判成「外站」，然后由那颗桩把画布挡白。
    }

    [Fact]
    public void OutboundDenyCoversExactlyTheForeignClassAndNothingElse()
    {
        var h = VirtualHost;
        Assert.True(BoardsAssetPolicy.IsOutboundDenyUri(U("https://esm.sh/react"), h));            // 该挡
        Assert.True(BoardsAssetPolicy.IsOutboundDenyUri(U($"http://{h}.cdn.example.com/x"), h));   // 后缀冒充也挡（⛔ 认它是本地）
        Assert.False(BoardsAssetPolicy.IsOutboundDenyUri(U($"https://{h}/board.html"), h));        // 随包那一份
        Assert.False(BoardsAssetPolicy.IsOutboundDenyUri(U($"http://{h}:8443/x"), h));             // 同上一档（片69 读数）
        foreach (var scheme in BoardsAssetPolicy.InlineUriSchemes)
            Assert.False(BoardsAssetPolicy.IsOutboundDenyUri(U($"{scheme}:something"), h), scheme + " 压根⛔ 出机⇒⛔ 挡");
        // ⛔ 拿一颗读不懂的串去挡一条也许本机的请求：代价是画布白屏，而收益一句也没有
        Assert.False(BoardsAssetPolicy.IsOutboundDenyUri(null, h));
        // 名字给空＝一颗也⛔ 算本地⇒外呼全数挡（fail-closed 那一侧，与 <c>IsLocalAssetUri</c> 同向；那颗名字由宿主常量给，⛔ 会空）
        Assert.True(BoardsAssetPolicy.IsOutboundDenyUri(U("https://example.com/x"), ""));
        // 三档<b>互斥</b>且<b>齐覆盖</b>一颗真 URL：漏一档＝计数互相偷数，多一档＝名册读不出脸
        foreach (var s in new[] { $"https://{h}/a", "data:text/html,x", "https://esm.sh/y" })
        {
            var u = U(s);
            int hits = (BoardsAssetPolicy.IsLocalAssetUri(u, h) ? 1 : 0)
                       + (BoardsAssetPolicy.IsInlineUri(u) ? 1 : 0)
                       + (BoardsAssetPolicy.IsOutboundDenyUri(u, h) ? 1 : 0);
            Assert.Equal(1, hits);
        }
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
    public void ObserverCountsFourClassesAndDeniesTheForeignOne()
    {
        var body = SourceGate.MethodBody(WindowCs, "private void OnWebResourceRequested");
        // 四颗计数各有一条上屏出口（⛔ 一颗只加不念的死计数＝片23 摘 sessionId 那一族）
        foreach (var needle in new[] { "_netLocal++", "_netInline++", "_netForeign++", "_netDenied++" })
            Assert.Equal(1, SourceGate.Count(body, needle));
        foreach (var slot in new[] { "n=\" + _netForeign", "local=\" + _netLocal", "inline=\" + _netInline", "denied=\" + _netDenied" })
            Assert.Contains(slot, body, StringComparison.Ordinal);
        // 片69 那一翻：挡的出口要<b>在</b>，且只由那颗 Core 判据开关（⛔ 在 UI 里另写一份「非本地且非内嵌」）
        Assert.Contains("BoardsAssetPolicy.IsOutboundDenyUri(uri, VirtualHostName)", body, StringComparison.Ordinal);
        Assert.Contains("e.Response = sender.Environment.CreateWebResourceResponse(", body, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(body, "e.Response"));
        Assert.Equal(1, SourceGate.Count(body, "_netDenied++"));
        // 分类照 Core 那颗判据，⛔ 在 UI 里再写一份方案／主机比较（第二个主人＝换判据时静默分岔）
        Assert.Contains("BoardsAssetPolicy.IsLocalAssetUri(uri, VirtualHostName)", body, StringComparison.Ordinal);
        Assert.Contains("BoardsAssetPolicy.IsInlineUri(uri)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("uri.Scheme ==", body, StringComparison.Ordinal);
        Assert.DoesNotContain("uri.Host ==", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Handled", body);   // 那颗门⛔ 归这颗方法管（订错地方＝两格各挡一半）
        Assert.DoesNotContain("Cancel", body);
    }

    [Fact]
    public void StatusConstantIsFourFiftyOneAndNamedOnceUsedOnce()
    {
        var code = SourceGate.Code(WindowCs);
        Assert.Equal(1, SourceGate.Count(code, "const int BlockedResponseStatus = 451;"));
        Assert.Equal(1, SourceGate.Count(code, "BlockedResponseStatus, \"Blocked By StarMark\""));
        // ⛔ 有第二颗写死的码混进那一族（403 是"站自己拒"那一族，混了就分不开）
        Assert.DoesNotContain("403", code);
    }

    [Fact]
    public void HostSubscribesEveryGateBeforeItNavigates()
    {
        var code = SourceGate.Code(WindowCs);
        var navigate = code.IndexOf("_web.CoreWebView2.Navigate(", StringComparison.Ordinal);
        Assert.True(navigate > 0, "宿主那颗 <c>Navigate</c> 找不到了＝顺序断言的锚点没了");
        // 三颗一对：新窗／顶帧／<b>子帧</b>（P-252）。那颗帧名里含 <c>NavigationStarting</c> 那一串，
        // 而计数用的是「事件 <c>+= On</c> 处理器」整颗串＝两颗不同形，⛔ 会互相顶掉（此处按整串数）。
        foreach (var pair in new[] { "NewWindowRequested", "NavigationStarting", "FrameNavigationStarting" })
        {
            Assert.Equal(1, SourceGate.Count(code, pair + " -= On" + pair));
            Assert.Equal(1, SourceGate.Count(code, pair + " += On" + pair));
            Assert.True(code.IndexOf(pair + " += On" + pair, StringComparison.Ordinal) < navigate,
                pair + " 排在 <c>Navigate</c> 之后＝第一发根本进不了那颗门");
        }
    }

    [Fact]
    public void NewWindowGateHandlesEveryCallAndNeverNarratesTheUrl()
    {
        var body = SourceGate.MethodBody(WindowCs, "private void OnNewWindowRequested");
        Assert.Contains("e.Handled = true;", body, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(body, "_newWindowBlocked++"));
        Assert.Contains("\"boards.newwindow.blocked n=\" + _newWindowBlocked", body, StringComparison.Ordinal);
        Assert.Contains("+ \" initiated=\" + e.IsUserInitiated", body, StringComparison.Ordinal);
        // ⛔ 那颗串进日志：它由页面说了算（P-13 那一族），又带着「他刚点过哪条链接」（P-193 那一族）
        Assert.Equal(0, SourceGate.Count(body, "e.Uri"));
    }

    [Fact]
    public void NavigationGateCancelsOnlyForeignAndReadsTheUrlJustOnce()
    {
        var body = SourceGate.MethodBody(WindowCs, "private void OnNavigationStarting");
        Assert.Contains("BoardsAssetPolicy.IsOutboundDenyUri(uri, VirtualHostName)", body, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", body, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(body, "_navBlocked++"));
        Assert.Contains("\"boards.nav.blocked n=\" + _navBlocked", body, StringComparison.Ordinal);
        // 那颗串只许被<b>读</b>一次（拿去解析），⛔ 第二次＝进了痕
        Assert.Equal(1, SourceGate.Count(body, "e.Uri"));
        // 放行那一支⛔ 留痕（每一趟开场都要走它一次，落行＝把噪声写成读数）
        Assert.True(body.IndexOf("return;", StringComparison.Ordinal) < body.IndexOf("e.Cancel = true;", StringComparison.Ordinal),
            "那颗门先挡再放行＝开场那一发也留下一行，读数里分不清哪一行是他按的");
    }

    /// <summary>
    /// 子帧那一头（账本 P-252）：顶帧那颗闸门<b>管不到</b> <c>iframe</c>——片72 臂 D 现读「摘掉 CSP 递一颗远程
    /// iframe，宿主观察者一颗也没听到」＝那一路连听到都⛔ 听到。口径逐条照 <see cref="NavigationGateCancelsOnlyForeignAndReadsTheUrlJustOnce"/>。
    /// </summary>
    [Fact]
    public void FrameGateCancelsForeignSubframesAndNarratesNothing()
    {
        var body = SourceGate.MethodBody(WindowCs, "private void OnFrameNavigationStarting");
        Assert.Contains("BoardsAssetPolicy.IsOutboundDenyUri(uri, VirtualHostName)", body, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", body, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(body, "_frameNavBlocked++"));
        Assert.Contains("\"boards.frame.nav.blocked n=\" + _frameNavBlocked", body, StringComparison.Ordinal);
        // 那颗串只许被<b>读</b>一次（拿去解析），⛔ 第二次＝进了痕
        Assert.Equal(1, SourceGate.Count(body, "e.Uri"));
        // ⛔ 那颗<b>帧名</b>进痕：它跟那颗串一样由页面说了算，且是页面自己起的一个名（P-13／P-193 同一条口径）
        Assert.Equal(0, SourceGate.Count(body, "e.Name"));
        // 放行那一支⛔ 留痕：开场那一发子帧（<c>about:blank</c>）与本机 iframe 每一趟都要走它
        // ⚠ 顺序断言⛔ 能自己兜存在：<c>IndexOf("return;")</c> 缺席时交回 -1，而 -1 ⛔ 小于任何下标＝那颗格恒绿
        Assert.Equal(1, SourceGate.Count(body, "return;"));
        Assert.True(body.IndexOf("return;", StringComparison.Ordinal) < body.IndexOf("e.Cancel = true;", StringComparison.Ordinal),
            "那颗门先挡再放行＝开场那一发也落一行，读数里分不清哪一行是他按的");
    }

    [Fact]
    public void BridgeTurnsOffTheEngineEmbeddableValidator()
    {
        // 只问「那颗 prop 交的是⛔ 是 false」这一维；它住在哪一层归下一格（两臂各打红一颗＝名册 411）
        Assert.Equal(1, SourceGate.Count(SourceGate.StripJsCommentsAndStrings(Js), "validateEmbeddable: false"));
    }

    [Fact]
    public void ThatValidatorSitsAtTheTopLevelOfBaseProps()
    {
        var stripped = SourceGate.StripJsCommentsAndStrings(Js);
        Assert.Equal(1, SourceGate.JsBraceDepthInBaseProps(stripped, "validateEmbeddable:"));
        // 同一颗尺的<b>对照件</b>：顶层兄弟一颗也读回 1，而 UIOptions 里那颗读回 3——
        // 少了这两颗，「深度 1」只是我数出来的一句自证（名册 408：借来的尺射程要本格正件自证）
        Assert.Equal(1, SourceGate.JsBraceDepthInBaseProps(stripped, "excalidrawAPI:"));
        Assert.Equal(3, SourceGate.JsBraceDepthInBaseProps(stripped, "image: false"));
    }
}
