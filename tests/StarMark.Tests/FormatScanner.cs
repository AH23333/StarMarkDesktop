#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StarMark.Tests;

/// <summary>
/// 显示侧格式扫描器（批次 RY 立，批次 RZ 抽成共用）：把一份源码一次遍历成
/// <b>抹掉注释后的代码</b> + <b>全部字符串字面量（含内插孔里再套的串）</b>，并给出两种格式的识别口径。
/// <para>
/// 为什么要有这个类：日期闸门（RY）和数字闸门（RZ）要认的是<b>同一批语法</b>——普通串／内插／孔里嵌套串／
/// 逐字串／原始串／字符字面量里的引号／注释里的串。两处各写一份扫描器，就等于把 #190 那四个 bug 再写一遍；
/// 判据只许有一个主人，<b>扫描器也一样</b>。
/// </para>
/// </summary>
internal static class FormatScanner
{
    /// <summary>
    /// 认"这是日期/时间格式"的标记表。<b>只收多字符标记</b>：单收 <c>dd</c> 会把 <c>add</c> 算成格式，
    /// 单收 <c>tt</c>/<c>mm</c> 会把 <c>Settings</c>/<c>summary</c> 算成格式——误伤的代价是闸门天天红，
    /// 最后被人绕过（#144 那条"判据要能被遵守"）。
    /// </summary>
    private static readonly string[] DateTokens =
    {
        "yyyy", "yy-", "MM-dd", "dd-MM", "M-d", "MM/dd", "dd/MM", "HH:mm", "hh:mm", "mm:ss", "HHmmss", "ddd", "dddd",
    };

    /// <summary>
    /// 认"这是<b>带分隔符风险</b>的数字格式"。<b>口径来自实测表，不是猜</b>（见 <c>NumberTextTests</c> 的前提测）：
    /// <list type="bullet">
    /// <item><c>0</c>/<c>00</c>/<c>000</c> 这类纯占位符在 zh/en/de/ar/hi/th 下<b>逐字不变</b> ⇒ <b>不收</b>
    /// （收了会把 9 处 <c>Guid.ToString("N")</c> 和一堆补零误判成违规，天天红＝没人遵守）；</item>
    /// <item>自定义模式里出现 <c>.</c> <c>,</c> <c>%</c> 三者之一 ⇒ 会打小数点/分组符/百分号 ⇒ 收；</item>
    /// <item>标准说明符 <c>N0 F1 P0 C2 G3 E4</c>，<b>位数一或两位都算</b>（<c>G15</c> 打的就是带小数点的
    /// 读数，德式给它换成 <c>1234,5</c>——只认一位数会整族放过，#189 说的"同一件事还能怎么写"）；
    /// 裸 <c>N</c>/<c>D</c>/<c>G</c>（不带位数）在本仓只可能是 <c>Guid</c> 的形状串，实测逐文化不变 ⇒ <b>不收</b>。</item>
    /// </list>
    /// </summary>
    private static readonly Regex NumberCustom = new(@"^[0#,% .;]*[0#][0#,% .;Ee]*$");

    private static readonly Regex NumberStandard = new(@"^[NFPCGE][0-9]{1,2}$");

    /// <summary>日期/时间格式串（先剥掉 TimeSpan 的转义反斜杠，否则 <c>@"h\:mm\:ss"</c> 会躲过去）。</summary>
    internal static bool IsDateFormatLiteral(string literal)
        => DateTokens.Any(t => Strip(literal).Contains(t, StringComparison.Ordinal));

    /// <summary>带分隔符风险的数字格式串。</summary>
    internal static bool IsNumberFormatLiteral(string literal)
    {
        var f = Strip(literal).Trim();
        if (f.Length == 0) return false;
        return NumberStandard.IsMatch(f)
               || (NumberCustom.IsMatch(f) && f.Any(c => c is '.' or ',' or '%'));
    }

    private static string Strip(string literal) => literal.Contains('\\') ? literal.Replace("\\", string.Empty) : literal;

    /// <summary>
    /// 一次遍历同时产出两样东西：<b>抹掉注释后的代码</b>（字符串原样留着，闸门要看实参表）
    /// 和<b>全部字符串字面量</b>（含内插孔里再套的串）。
    /// <para>
    /// 为什么不能"按行切掉 <c>//</c> 之后"：那会把 <c>"https://…"</c> 里的双斜杠当成注释，
    /// 于是<b>同一行后半段的真违规一起消失</b>——注释要跳过，但不能靠截行（#190 的 RI9）。
    /// </para>
    /// </summary>
    internal static (string Code, List<string> Literals) Scan(string text)
    {
        var code = text.ToCharArray();
        var literals = new List<string>();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                var stop = CommentEnd(text, i);
                for (var k = i; k < stop; k++)
                    if (code[k] != '\n' && code[k] != '\r') code[k] = ' ';
                i = stop - 1;
                continue;
            }

            if (c != '"')
            {
                // 字符字面量 '<c>' 里可以就是一个引号（'"'）：不跨过去的话，那半个引号会冒充串起点，把整份文件扫歪
                if (c == '\'')
                {
                    var k = i + 1 < text.Length && text[i + 1] == '\\' ? i + 2 : i + 1;
                    if (k + 1 < text.Length && text[k + 1] == '\'') i = k + 1;
                }
                continue;
            }

            var literal = ReadLiteral(text, i);
            literals.Add(literal.Content);
            literals.AddRange(literal.Nested);
            i = literal.Next - 1;
        }

        return (new string(code), literals);
    }

    /// <summary>注释区间的结束下标（行注释不含那个换行，块注释含 <c>*/</c>；未闭合就到文件尾）。</summary>
    private static int CommentEnd(string text, int at)
    {
        if (text[at + 1] == '/')
        {
            var nl = text.IndexOf('\n', at);
            return nl < 0 ? text.Length : nl;
        }
        var close = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
        return close < 0 ? text.Length : close + 2;
    }

    /// <summary>
    /// 从某个引号起读一个字面量：认普通串、逐字串（<c>@"…"</c>，反斜杠原样、双写引号终止）、
    /// 原始串（<c>"""…"""</c>），并<b>递归收内插孔里嵌套的串</b>——
    /// <c>$"… {due.ToString("HH:mm")} …"</c> 这种嵌套是把扫描器截断的头号原因（#190 的 RI4/RI5）。
    /// </summary>
    private static (string Content, int Next, List<string> Nested) ReadLiteral(string text, int at)
    {
        var nested = new List<string>();

        if (at + 2 < text.Length && text[at + 1] == '"' && text[at + 2] == '"')   // 原始串 """…"""
        {
            var end = text.IndexOf("\"\"\"", at + 3, StringComparison.Ordinal);
            return end < 0
                ? (text[(at + 3)..], text.Length, nested)
                : (text[(at + 3)..end], end + 3, nested);
        }

        var verbatim = at > 0 && text[at - 1] == '@'
                       || at > 1 && text[at - 2] == '@' && text[at - 1] == '$';   // @"…" 与 @$"…"
        var content = new StringBuilder();
        var hole = 0;

        for (var j = at + 1; j < text.Length;)
        {
            var c = text[j];
            if (verbatim)
            {
                if (c != '"')
                {
                    content.Append(c);
                    j++;
                    continue;
                }
                if (j + 1 < text.Length && text[j + 1] == '"')
                {
                    content.Append('"');
                    j += 2;
                    continue;
                }
                return (content.ToString(), j + 1, nested);
            }

            if (c == '\\')
            {
                if (j + 1 < text.Length) content.Append(text[j + 1]);
                j += 2;
                continue;
            }

            if (c is '{' or '}')
            {
                if (c == '{') hole++;
                else if (hole > 0) hole--;
                content.Append(c);
                j++;
                continue;
            }

            if (c == '"')
            {
                if (hole == 0) return (content.ToString(), j + 1, nested);
                var inner = ReadLiteral(text, j);   // 孔里的嵌套串：自己收一条，游标跨过它，外层字面量不在这里断
                nested.Add(inner.Content);
                nested.AddRange(inner.Nested);
                j = inner.Next;
                continue;
            }

            content.Append(c);
            j++;
        }

        return (content.ToString(), text.Length, nested);
    }

    private static readonly Regex AnyHole = new(@"\{([^{}]*)\}");

    /// <summary>
    /// 取内插孔里"格式"那一段：<c>{due:HH:mm}</c> → <c>HH:mm</c>、<c>{x,5:MM-dd}</c> → <c>MM-dd</c>。
    /// <para><b>口径是第一枚冒号之后全是格式</b>。不能写成 <c>\{[^{}]*:([^{}]*)\}</c>——那颗前缀是贪心的，
    /// 一遇到 <c>HH:mm</c> 这种<b>自带两枚冒号</b>的格式就把 <c>due:HH</c> 当成表达式、只剩 <c>mm</c> 当格式
    /// （#190 的 RI12；数字侧的 <c>{ratio:P0}</c>、<c>{ms:F1}</c> 同理）。</para>
    /// </summary>
    internal static IEnumerable<(string Hole, string Format)> HoleFormats(string literal)
    {
        foreach (Match m in AnyHole.Matches(literal))
        {
            var body = m.Groups[1].Value;
            var at = body.IndexOf(':');
            if (at >= 0) yield return ("{" + body + "}", body[(at + 1)..]);
        }
    }

    /// <summary>取一次 <c>ToString(</c> 调用的实参表（括号配对，跨行也算）。</summary>
    internal static IEnumerable<string> ToStringArgumentLists(string code)
    {
        for (var at = code.IndexOf("ToString(", StringComparison.Ordinal); at >= 0;
             at = code.IndexOf("ToString(", at + 1, StringComparison.Ordinal))
        {
            var i = at + "ToString(".Length;
            var depth = 1;
            var start = i;
            for (; i < code.Length && depth > 0; i++)
            {
                if (code[i] == '(') depth++;
                else if (code[i] == ')') depth--;
            }
            yield return code[start..Math.Max(start, i - 1)];
        }
    }

    private static readonly Regex StringFormatAttr = new(@"StringFormat\s*=\s*(?:""(?<quoted>[^""]*)""|(?<raw>[^,\s]*))");

    /// <summary>
    /// XAML 的两种 <c>StringFormat</c> 写法：<c>StringFormat="{0:HH:mm}"</c>（属性文本，带引号）与
    /// <c>StringFormat={}{0:HH:mm}</c>（标记扩展，不带引号、以 <c>{}</c> 转义开头）。
    /// 不带引号那一支会连尾随的 <c>}</c> 一起吃掉——判的是"这段值里有没有格式"，尾巴多一个括号不影响结论；
    /// <b>宁可粗到多看一眼，不能细到漏判</b>。XML 注释里的例子不算行为，先整段剥掉。
    /// </summary>
    internal static IEnumerable<string> XamlStringFormats(string xaml)
    {
        var withoutComments = Regex.Replace(xaml, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        foreach (Match m in StringFormatAttr.Matches(withoutComments))
        {
            var value = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["raw"].Value;
            yield return value.TrimEnd('}', '"', '\'', ' ', '/');
        }
    }

    /// <summary>某目录下全部手写源码：扁平路径、抹掉注释后的代码、同一趟扫出的字面量表。</summary>
    internal static List<(string Path, string Code, List<string> Literals)> SourcesUnder(
        string dir, string? skipJudgeFileName = null)
        => SourceGate.ReadRepoUnder(dir)
            .Where(f => skipJudgeFileName == null || !f.RelativePath.EndsWith(skipJudgeFileName, StringComparison.Ordinal))
            .Select(f =>
            {
                var (code, literals) = Scan(f.Text);
                return (f.RelativePath, code, literals);
            })
            .ToList();

    /// <summary>
    /// "自己切片再补省略号"的形状（批次 SB 立、SC 搬进共用扫描器）：
    /// 切片族（<c>[..x]</c>／<c>AsSpan(0, x)</c>／<c>Substring(0, x)</c>）之后同行出现 <c>"…"</c>。
    /// <para>只认<b>写法</b>，所以认不出"逐枚累加到上限就停"那类（<c>OcrText.Preview</c>）——
    /// 那种由行为测兜（#193）。两处闸门（标题、家族）共用这一条，别各写一份正则。</para>
    /// </summary>
    internal static readonly Regex HandRolledEllipsis = new(
        @"(\[\s*\.\.\s*[\w]+\s*\]|AsSpan\(\s*0\s*,\s*[\w]+\s*\)|Substring\(\s*0\s*,\s*[\w]+\s*\))[^\n""]*""\s*…",
        RegexOptions.Compiled);
}
