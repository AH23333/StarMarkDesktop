#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarMark.Core.Boards;

/// <summary>
/// 画板<b>随包资产</b>那一面的判据主人（批次 Phase C 片18）。
/// <para>
/// 这里只住<b>已经判过的事</b>：随包清单的档名形状、必需字体族、bundle/css/清单三颗名字、
/// 清单 schema 号。三样东西刻意<b>⛔ 在这里出现</b>：
/// ① Xiaolai 主区裁切规则——那颗规则的主人是 <c>tools/boards/stage-assets.py</c>（§九 裁决 3 的落地形状；
/// 这里⛔ 复写一份第二主人），本类只认「清单说了什么」；
/// ② 片数／字节总数的期望值——111／209／12.08 MB 那些是<b>现读读数</b>，随每次重跑清单漂，闸门认「盘上==清单」，
/// ⛔ 认「盘上==某个写死的数」；
/// ③ 上屏句子——本档⛔ 成品字。
/// </para>
/// </summary>
public static class BoardsAssetPolicy
{
    /// <summary>冻结清单的 schema 号。加字段先加号（与更新清单那条口径同族）。</summary>
    public const int ManifestSchemaVersion = 1;

    /// <summary>随包资产根的清单档名（<c>assets/boards/manifest.json</c>）。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>那颗引擎 bundle 的随包档名（<c>window.StarMarkBoards</c> 出口那颗）。</summary>
    public const string BundleFileName = "excalidraw.min.js";

    /// <summary>引擎样式表（相对路径点字体，随 bundle 一起走）。</summary>
    public const string StylesFileName = "index.css";

    /// <summary>桥的页面入口（board.html）与页面侧桥（board-bridge.js）——片19 起随包走。</summary>
    public const string PageFileName = "board.html";
    public const string BridgeJsFileName = "board-bridge.js";

    /// <summary>字体树根（bundle 内注册表按 <c>./fonts/&lt;族&gt;/&lt;档&gt;</c> 相对引用）。</summary>
    public const string FontsRootName = "fonts";

    /// <summary>中文手写体族名（档 5＝Excalifont 的中文回退，§24.2）。</summary>
    public const string XiaolaiFamilyName = "Xiaolai";

    /// <summary>随包资产那颗虚拟站的方案名（宿主把整棵树钉在 <c>https://&lt;虚拟站&gt;/…</c>；
    /// 名字的主人是 <c>BoardWindow.VirtualHostName</c>，本类只认方案）。账本 P-248 用得到它。
    /// ⚠ 片69 起这颗常量<b>⛔ 再参与判据</b>（<see cref="IsLocalAssetUri"/> 只比站名）——留着是因为它是
    /// 「宿主开场那一发该钉哪个方案」的那句话的主人（<c>BoardWindow.OpenAsync</c> 拼 URL 用的就是它），
    /// ⛔ 因为它在分类那一面没了消费者就把「该用哪个方案」这句一并删掉。</summary>
    public const string AssetUriScheme = "https";

    /// <summary>
    /// 「这一发压根⛔ 走出这台机器」（账本 <b>P-248</b> 后半那颗判据的主人；片69 现读改口）。
    /// <para>
    /// 判的只有<b>站名</b>一颗：随包资产全部走那颗虚拟站映射，所以一颗<b>真</b>的本地请求长这样
    /// <c>https://appassets.local/fonts/…</c>；任何<b>别的</b>主机（<c>esm.sh</c>、<c>fonts.gstatic.com</c>、
    /// 一颗档里带的远程 iframe）都是<b>假</b>。⛔ 看路径——路径归随包清单管（<c>ManifestFileName</c> 那一族），
    /// 在这里比一次＝第二个主人。
    /// </para>
    /// <para>
    /// <b>⛔ 再比方案（片68 之前它比，那是一颗没量过的假设）</b>：隐藏宿主那一跑量到那颗映射是按<b>站名</b>劫持的——
    /// 我把 <c>http://127.0.0.1:8099/ctl.html</c>（<b>真 http、带端口</b>）递给宿主，页面读回的是
    /// <c>--www</c> 目录里那一棵档（正本 <c>roster69_ctl3</c> 的 <c>dom</c> 行 <c>SPIKE69-FOLDER</c>＋那颗
    /// 内容差＝由档名分家，⛔ 靠"服务器没记"那种负证据），而那颗真服务器一颗请求也没记。
    /// ⇒ 认方案会把一颗<b>由本地供的</b>那一发判进外呼那一档；把它翻成 deny 就＝宿主自己把画布挡白。
    /// </para>
    /// <para>⚠ 那颗虚拟站的名字由调用方递进来（UI 那颗常量是它唯一的主人），⛔ 在这里抄一份：抄了就出现两颗名字，
    /// 换名字那一批只改一处而这里静默失真（与「名字比射程大」那颗 P-246 同族）。</para>
    /// </summary>
    public static bool IsLocalAssetUri(Uri? uri, string virtualHostName)
        => uri is not null
           && virtualHostName.Length > 0
           && string.Equals(uri.Host, virtualHostName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 「这一发该挡」（账本 <b>P-248</b> 后半）：外呼那一档<b>且解得出那颗串</b>。
    /// <para>
    /// 解不出（<c>null</c>）的一律<b>⛔ 挡</b>：那颗串读不懂时「它也许是本地那一份」这句话我没能判掉，
    /// 而挡错的代价是画布白屏、不挡的代价是那一名照旧进名册由痕说——安全＞稳定那一序在这里给的是<b>不挡</b>，
    /// 因为「挡住一颗本就本地的请求」是这一批唯一能自己造出来的坏。
    /// </para>
    /// <para>⛔ 在 UI 里再写一份「非本地且非内嵌」的比较：那颗分档由本类说，宿主只负责照它落桩。</para>
    /// </summary>
    public static bool IsOutboundDenyUri(Uri? uri, string virtualHostName)
        => uri is not null && !IsLocalAssetUri(uri, virtualHostName) && !IsInlineUri(uri);

    /// <summary>那四颗「压根⛔ 走网络」的方案：<c>data</c>／<c>blob</c>／<c>about</c>／<c>filesystem</c>。
    /// 它们既⛔ 是随包那一份、也⛔ 是一次出机外呼，所以在 P-248 那把尺里自成一档——
    /// ⚠ 混进「外呼」那一档，下一批就拿着被污染的名册去翻 deny，而 <c>data:</c> 正是引擎画布上那些图的形状
    /// （片21 的存储口径＝内嵌 base64），挡了＝白屏。</summary>
    public static readonly string[] InlineUriSchemes = { "data", "blob", "about", "filesystem" };

    /// <summary>「这一发压根⛔ 出机」（理由见 <see cref="InlineUriSchemes"/>）。</summary>
    public static bool IsInlineUri(Uri? uri)
        => uri is not null && Array.IndexOf(InlineUriSchemes, uri.Scheme) >= 0;

    /// <summary>那颗痕里该念的目标：<b>只有方案＋主机名</b>——路径与 query 是用户数据（一颗远程档名进了日志＝
    /// 把「他打开过哪张图」写进一台人看的文件，P-13/P-193 那条口径），而主机名⛔ 会带换行（<c>Uri</c> 解析不过）。</summary>
    public static string DescribeRequestTarget(Uri? uri)
        => uri is null ? "(无)" : uri.Scheme + "://" + uri.Host;

    /// <summary>决策 4（§九〔裁决 2026-10-17·代裁〕）的摘除标记：stub 里那颗报错串的前缀。
    /// bundle 里必须能搜到它（＝mermaid 那颗插件确实被 alias 摘了）。</summary>
    public const string MermaidStubMarker = "StarMarkBoards.mermaidStub";

    /// <summary>bundle 出口的全局名（<c>tools/boards/entry.js</c> 那颗的唯一出口）。</summary>
    public const string BundleGlobalName = "window.StarMarkBoards=";

    /// <summary>
    /// 八个非 Xiaolai 字体族（决策 3：全随）。Xiaolai⛔ 在这张表里——它走主区裁切，片数以清单为准。
    /// </summary>
    public static readonly string[] RequiredFontFamilies =
    {
        "Assistant", "Cascadia", "ComicShanns", "Excalifont", "Liberation", "Lilita", "Nunito", "Virgil",
    };

    /// <summary>
    /// 随包档的相对路径形状（正斜杠 POSIX，清单里的唯一形状）。判据：<b>⛔ 是</b>「这串像不像路径」，
    /// 是「这串会不会走出随包根」——<c>..</c> 段、反斜杠、盘符、绝对前缀、空串一律拒。
    /// </summary>
    public static bool IsSafeAssetRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 260) return false;
        if (path[0] is '/' or '\\') return false;
        if (path.Contains('\\')) return false;
        if (path.Contains(':')) return false;
        foreach (var seg in path.Split('/'))
        {
            if (seg.Length == 0) return false;
            if (seg is "." or "..") return false;
        }
        return true;
    }

    /// <summary>
    /// 随包档名那颗形状判据（字体片 <c>fonts/&lt;族&gt;/&lt;名&gt;.woff2</c> 还是根上那三颗），
    /// 给对账用；家族目录名先过 <see cref="IsSafeAssetRelativePath"/> 再按这张表认。
    /// </summary>
    public static bool IsKnownAssetPath(string? path)
    {
        if (!IsSafeAssetRelativePath(path)) return false;
        if (path == BundleFileName || path == StylesFileName || path == ManifestFileName
            || path == PageFileName || path == BridgeJsFileName) return true;
        var segs = path!.Split('/');
        return segs.Length == 3
            && segs[0] == FontsRootName
            && segs[1].Length > 0
            && (segs[1] == XiaolaiFamilyName || Array.IndexOf(RequiredFontFamilies, segs[1]) >= 0)
            && segs[2].EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
    }
}
