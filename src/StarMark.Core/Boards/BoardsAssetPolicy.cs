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

    /// <summary>字体树根（bundle 内注册表按 <c>./fonts/&lt;族&gt;/&lt;档&gt;</c> 相对引用）。</summary>
    public const string FontsRootName = "fonts";

    /// <summary>中文手写体族名（档 5＝Excalifont 的中文回退，§24.2）。</summary>
    public const string XiaolaiFamilyName = "Xiaolai";

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
        if (path == BundleFileName || path == StylesFileName || path == ManifestFileName) return true;
        var segs = path!.Split('/');
        return segs.Length == 3
            && segs[0] == FontsRootName
            && segs[1].Length > 0
            && (segs[1] == XiaolaiFamilyName || Array.IndexOf(RequiredFontFamilies, segs[1]) >= 0)
            && segs[2].EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
    }
}
