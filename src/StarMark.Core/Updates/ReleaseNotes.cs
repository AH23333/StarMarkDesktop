#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;
using StarMark.Abstractions;

namespace StarMark.Core.Updates;

/// <summary>
/// 把"对方写的那一版说明"收成屏幕上能读的那几行字（批次 VX）。
/// <para>
/// 为什么要在 Core 而不是界面：<b>这一串是外部服务器的输入</b>。GitHub 的 <c>body</c> 是 markdown，
/// 而这份产物没有 markdown 渲染器（零 NuGet 那条裁决 D1），把原文直接塞进 <c>TextBlock</c>
/// 得到的是一屏 <c>## **[1.0.2](https://…)**</c> 这样的记号——那比没有说明更让人读不下去。
/// 收敛规则因此必须和措辞一样只有<b>一个出处</b>，才能被逐条契约测钉住；界面只许读结果
/// （<see cref="UpdateReport.Notes"/>），自己动一次 <c>Replace</c> 就等于开出第二份写法。
/// </para>
/// <para>
/// 三条取舍写在这儿，免得下次被"顺手补全"：
/// ① <b>链接只留下连接文字、扔掉括号里的地址</b>。<c>html_url</c> 那一格从解析起就不进数据结构
/// （<see cref="RemoteRelease"/> 的注释讲了为什么），这里同一件事的第二面：屏幕上出现一个能让人复制、
/// 粘进浏览器的地址，就等于把"这条链接归谁"重新交回对方。
/// ② <b>正文里裸写的地址照原样留着</b>。这一条不是疏忽：它只是文字，本程序不会去开它，
/// 而 <c>**Full Changelog**: https://…</c> 那一行确实是有人想找的下一步。
/// ③ <b>单个的 <c>*</c> 与 <c>_</c> 不当记号剥</b>。<c>2*3*4</c>、<c>a_b_c</c> 在说明里都是合法写法，
/// 剥了就是把内容改了；只剥成对的粗体记号（<c>**</c>／<c>__</c>）与反引号。
/// </para>
/// </summary>
public static class ReleaseNotes
{
    /// <summary>屏幕上最多给读这么多字。<b>这一屏不是阅读器</b>：一次发布的说明通常几百字，
    /// 4000 字装得下十几条要点；再长就该去发布页读，而不是把设置页挤成一堵墙。</summary>
    public const int MaxDisplayChars = 4000;

    // 每一条都是"这一行的形状"，没有嵌套量词，最坏情况线性扫过；输入本身另有字节上限（见 GitHubReleaseSource）。
    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+");
    private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]*\)");
    private static readonly Regex Link = new(@"\[([^\]]*)\]\([^)]*\)");
    private static readonly Regex Tag = new(@"</?[A-Za-z][^>]*>");
    private static readonly Regex Marker = new(@"\*\*|__|`{1,3}");
    private static readonly Regex BlankRun = new(@"\n{3,}");

    /// <summary>收成可读的那几行；清完什么都不剩（没写、只有徽章、只有注释）时回 <b>null</b>＝"没有东西可读"，
    /// 而不是回一枚空串让界面摆一个点开是空的折叠区。</summary>
    public static string? Clean(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;

        // 换行符先统一：下面按行处理，Windows 那对 \r\n 留在体内会让每一行都带一个看不见的尾字符。
        var text = Comment.Replace(markdown.Replace("\r\n", "\n").Replace('\r', '\n'), string.Empty);
        var lines = new StringBuilder(text.Length + 8);
        foreach (var raw in text.Split('\n'))
        {
            var line = Heading.Replace(raw.TrimEnd(), string.Empty);
            // 图先于链接：徽章写成 [![img](png)](link)，反过来会剩一个孤零零的 "!"。
            line = Image.Replace(line, string.Empty);
            line = Link.Replace(line, match => match.Groups[1].Value);
            line = Marker.Replace(Tag.Replace(line, string.Empty), string.Empty);
            lines.Append(line).Append('\n');
        }

        var cleaned = BlankRun.Replace(lines.ToString(), "\n\n").Trim();
        return cleaned.Length == 0 ? null : TextTrim.Ellipsize(cleaned, MaxDisplayChars);
    }
}
