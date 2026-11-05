using System;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板上那条<b>链接</b>的两半（Phase C <b>片68</b>＝账本 <b>P-249</b> 的前半）。
/// <para>三档各钉一面：① <b>页面</b>把引擎默认那一支拦下来再递给宿主（钉它住在<b>顶层 prop</b> 那一层＝引擎读的
/// 那一层，名册 622 那一族第二次用）；② <b>页面⛔ 自己开窗、也⛔ 自己猜起哪个程序</b>（判据与打开都归宿主，⛔ 两份主人）；
/// ③ <b>宿主</b>那一格把"开成了还是没开成"念成一行痕（从前 <c>_ =</c> 一发就完事＝两种下落长同一张脸）。</para>
/// <para>本批⛔ 起窗：三处判据都读得自盘上原文（JS 进不了编译、测试工程⛔ 引用 StarMark.UI）⇒这一族 <c>--no-build</c> 合法（名册 625）。
/// 那颗 URL <b>本身</b>⛔ 上屏也⛔ 进痕（P-30）。"上屏那一句"片87 已落地（P-249 后半），而片108＝P-257 起它住在
/// 那块面板的<b>第二行</b>：归属从"面板空着才进"换成"一句一行"（旧话不带计时器时那两句会永久静音＝真机量到的那一格）。</para>
/// </summary>
public class BoardLinkGateTests
{
    private const string BridgeRel = "tools/boards/board-bridge.js";
    private const string WindowRel = "src/StarMark.UI/Boards/BoardWindow.cs";

    private static string Js => SourceGate.ReadRepoFile(BridgeRel);

    /// <summary>抹掉注释与字符串那份<b>只用来数标识符</b>（数层、数禁项）；
    /// ⚠ 带字符串字面量的针脚一律拿原文数——这颗抹法会把 <c>"open.external"</c> 抹成一串空格（本批第一跑就是被它绊的）。</summary>
    private static string Stripped => SourceGate.StripJsCommentsAndStrings(Js);

    private static string WindowCode => SourceGate.Code(SourceGate.ReadRepoFile(WindowRel));

    /// <summary>那颗钩子真正的方法体（抹注释后从形参表之后那一颗左括号起，到它自己的右括号止）。</summary>
    private static string HookBody()
    {
        var s = Stripped;
        var at = s.IndexOf("onLinkOpen: function", StringComparison.Ordinal);
        Assert.True(at >= 0, "页面那颗 onLinkOpen 钩子找不到了（改名／换写法要连这一族一起改）");
        var paramsOpen = s.IndexOf('(', at);
        var open = s.IndexOf('{', paramsOpen);
        Assert.True(open > at, "那颗钩子的方法体左括号读不到");
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return s[open..(i + 1)];
        }
        throw new InvalidOperationException("那颗钩子的方法体没收尾（花括号数不齐）");
    }

    /// <summary>① 页面把那颗点击递给宿主：那颗名字恰好一处，而且递的是元素上那颗链接本尊。</summary>
    [Fact]
    public void PageHandsTheLinkClickToTheHost()
    {
        Assert.Equal(1, SourceGate.Count(Js, "postIn(\"open.external\", { url: String(url) })"));
        Assert.Equal(1, SourceGate.Count(Stripped, "onLinkOpen: function"));
        // 递出去的那颗字段名＝协议里在册的那一颗（名字错了＝整条被 Check 判成 MissingField 而⛔ 响）
        Assert.Equal(["url"], BridgeProtocol.InboundFields["open.external"]);
    }

    /// <summary>② 拦下来排在开口之前：引擎那两处消费点问的都是 <c>defaultPrevented</c>，先递话后拦＝两边都慢一拍。</summary>
    [Fact]
    public void TheHookVetoesTheEngineDefaultBeforeItSpeaks()
    {
        var body = HookBody();
        var veto = body.IndexOf("event.preventDefault();", StringComparison.Ordinal);
        var send = body.IndexOf("postIn(", StringComparison.Ordinal);   // 方法体抹过字符串＝只问那颗调用在不在、别连字面量一起问
        Assert.True(veto >= 0, "那颗钩子里⛔ 有 preventDefault＝引擎默认那两支（_blank 开窗／_self 换掉这一页）照旧会走");
        Assert.True(send >= 0, "那颗钩子里⛔ 有递给宿主那一句");
        Assert.True(veto < send, "拦下来排在递出去<b>之后</b>＝那一发已经先走引擎默认了（veto=" + veto + " send=" + send + "）");
        // 没带链接的那一颗元素⛔ 该被替它做主（钩子被两处消费点共用，无差别 preventDefault＝把别的分支一起闷掉）
        Assert.Contains("if (!url) return;", body, StringComparison.Ordinal);
    }

    /// <summary>③ 它住在引擎读的那一层：与 <c>excalidrawAPI</c>／<c>onChange</c> 同层的顶层 prop（名册 622）。</summary>
    [Fact]
    public void ThatHookSitsAtTheTopLevelOfBaseProps()
    {
        Assert.Equal(1, SourceGate.JsBraceDepthInBaseProps(Stripped, "onLinkOpen:"));
        // 对照件（名册 408：借来的尺射程要本格正件自证）——顶层兄弟读回 1、UIOptions 里那颗读回 3
        Assert.Equal(1, SourceGate.JsBraceDepthInBaseProps(Stripped, "excalidrawAPI:"));
        Assert.Equal(3, SourceGate.JsBraceDepthInBaseProps(Stripped, "image: false"));
    }

    /// <summary>④ 页面⛔ 自己开窗、也⛔ 在这里再判一遍协议：开窗与"起哪个程序"各有主人，重复一份＝第二主人（名册 347／522）。</summary>
    [Fact]
    public void ThePageNeverOpensAWindowOrJudgesTheSchemeItself()
    {
        // 标识符那几颗拿<b>抹注释</b>那份数（本档的注释里正当地摘引着引擎自己那句 window.open＝名册 589 那句"摘引⛔ 等于残留"的反面用法）
        var s = Stripped;
        Assert.Equal(0, SourceGate.Count(s, "window.open"));
        Assert.Equal(0, SourceGate.Count(s, "showSaveFilePicker"));
        Assert.Equal(0, SourceGate.Count(s, "IsSafeExternalUrl"));
        // 而那颗协议字面量拿<b>原文</b>数：抹字符串那一份会把代码里的真字面量一起抹掉＝恒绿（那颗禁项格就白守了）
        Assert.Equal(0, SourceGate.Count(Js, "https://"));
        Assert.Equal(0, SourceGate.Count(Js, "http://"));
        Assert.Equal(0, SourceGate.Count(Js, "indexOf(\"http"));
    }

    /// <summary>⑤ 宿主那一格把开没开成念出来：三种下落各一行痕，颗数逐颗点名＝⛔ 由"总共几行"倒推（名册 427）。</summary>
    [Fact]
    public void HostLogsWhetherThatOpenActuallyHappened()
    {
        var code = WindowCode;
        Assert.Equal(1, SourceGate.Count(code, "boards.open.accepted"));
        Assert.Equal(1, SourceGate.Count(code, "boards.open.rejected"));
        Assert.Equal(1, SourceGate.Count(code, "boards.open.opened"));
        Assert.Equal(1, SourceGate.Count(code, "boards.open.failed"));
        // 那一手不再被 _ = 扔掉：case 里递的是那颗会念下落的助手，⛔ 是直接一发 TryOpenAsync 交回的东西没人读
        Assert.Equal(1, SourceGate.Count(code, "ReportExternalOpenAsync(envelope.Url)"));
        Assert.Equal(0, SourceGate.Count(code, "_ = Helpers.LauncherEx.TryOpenAsync(envelope.Url)"));
        var body = SourceGate.MethodBody(code, "private async System.Threading.Tasks.Task ReportExternalOpenAsync");
        Assert.Contains("Helpers.LauncherEx.TryOpenAsync(url)", body, StringComparison.Ordinal);
        Assert.Contains("why=\" + why", body, StringComparison.Ordinal);
    }

    /// <summary>⑥ 痕里⛔ 带那颗 URL 本身（P-30：查询串里可能挂着凭据；那句原因与它无关）。</summary>
    [Fact]
    public void TheTraceNeverCarriesThatUrlItself()
    {
        var body = SourceGate.MethodBody(WindowCode, "private async System.Threading.Tasks.Task ReportExternalOpenAsync");
        Assert.DoesNotContain("+ url", body, StringComparison.Ordinal);
        Assert.DoesNotContain("url +", body, StringComparison.Ordinal);
        // 两句各带一颗"这一扇"的身份（现读＝2 处；⛔ 是"总共几行"那种倒推＝名册 427）
        Assert.Equal(2, SourceGate.Count(body, "id=\" + _boardId"));
    }

    /// <summary>⑦ 文件头那句"真喊几颗"要跟得上页面真喊的颗数——它是正文里数自家列表的那颗数（名册 555）。
    /// <para>片206＝P-285 重做：引擎主菜单那一组的四颗索取上路（<c>export.request</c>／<c>pin.request</c>／
    /// <c>scale.request</c>／<c>background.request</c>），真喊的从四颗变八颗；片207＝P-285 残余又补上
    /// <c>folder.request</c>（白条退役后那颗「打开所在文件夹」的出口），八颗变九颗；在册而⛔ 接线的只剩
    /// <c>paste.query</c> 那一颗。</para></summary>
    [Fact]
    public void TheBridgeHeaderCountsWhatThePageActuallySends()
    {
        var names = Regex.Matches(Js, "postIn\\(\"([a-z.]+)\"")
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["background.request", "board.loaded", "bridge.ready", "export.bytes", "export.request", "folder.request",
             "open.external", "pin.request", "scale.request"], names);
        // 文件头那句颗数与它后面那份名单＝正文里数自家列表的那颗数（名册 555）⇒钉<b>整段字面量</b>，
        // 加一颗真发的而⛔ 改口＝上面那颗红；改了措辞＝这一颗红（两颗各守一头，⛔ 拿阿拉伯数字去解析汉字那颗数）
        Assert.Contains(
            "名单在册十一颗，页面今天真喊九颗（postIn 逐颗一处＝bridge.ready／board.loaded／export.bytes／\n   open.external／export.request／pin.request／scale.request／background.request／folder.request——\n   后五颗＝片206 引擎主菜单那一组的索取，末颗＝片207 白条退役后那颗「打开所在文件夹」的出口）\n   ＋send 那颗 bridge.reply",
            Js, StringComparison.Ordinal);
        // 在册而⛔ 接线的那一颗逐颗点名（这句⛔ 是"还有几颗"，列错了当场红）
        foreach (var idle in new[] { "paste.query" })
            Assert.DoesNotContain("postIn(\"" + idle + "\"", Js, StringComparison.Ordinal);
    }

    /// <summary>⑧ 递出去的名字逐颗在册：片56 那颗 <c>bridge.reply</c> 的死法＝名字没登记而⛔ 响（名册 547／556）。</summary>
    [Fact]
    public void EveryNameThePageSendsIsOnTheHostRoster()
    {
        var sent = Regex.Matches(Js, "postIn\\(\"([a-z.]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.True(sent.Length >= 4, "这一把尺扫到 " + sent.Length + " 颗＝射程坏了，⛔ 能冒充绿灯（名册 339）");
        foreach (var name in sent)
            Assert.True(BridgeProtocol.IsKnownName(name, BridgeProtocol.Direction.Inbound), "页面喊了一颗名单外的名字：" + name);
    }

    /// <summary>⑨ 那颗 URL 判据⛔ 有第二个主人：定义在 Core 恰一颗，宿主问它，页面⛔ 抄。</summary>
    [Fact]
    public void TheUrlJudgeHasExactlyOneOwnerInTheProduct()
    {
        var defs = SourceGate.ReadRepoUnder("src")
            .Where(f => f.Text.Contains("bool IsSafeExternalUrl("))
            .Select(f => f.RelativePath.Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(["src/StarMark.Core/Boards/BridgeProtocol.cs"], defs);
        Assert.Equal(1, SourceGate.Count(WindowCode, "BridgeProtocol.IsSafeExternalUrl(envelope.Url)"));
    }
}
