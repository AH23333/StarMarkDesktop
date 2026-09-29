#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 显示侧日期/时间的形状闸门（批次 RY，P-122）：把"跟着系统文化跑"这类写法钉死在源码里。
/// <para>
/// <b>六条规则各管一种语法</b>，这是照着 #189 的教训设计的——同一件事有好几种写法，只认一种等于没守：
/// ① 内插格式 <c>{due:HH:mm}</c> 只许出现在判据文件里；
/// ② <c>ToString("&lt;日期格式&gt;"</c> 必须在<b>同一次调用</b>里带 <c>InvariantCulture</c>；
/// ③ <b>UI 工程里一个日期格式串都不许出现</b>（连"已经锁了不变文化"也不行——那样显示口径又有了第二个主人）；
/// ④ 星期名标记 <c>ddd</c> 除判据外一律禁止，<b>即使锁了不变文化也禁</b>（那会打出 Sunday，中文界面里的另一种坏）；
/// ⑤ 判据自己得真的拿着格式串，且扫描器对<b>每种字符串写法</b>都看得见它（否则①～④会"因为扫不到而全绿"）；
/// ⑥ XAML 的 <c>StringFormat</c> 用的是绑定文化的历法，与①同源；今天全仓零命中，属反回归。
/// </para>
/// <para>
/// 为什么这条闸门存在：账本 P-122 的普查用的是 <c>grep ToString("</c>，于是<b>整类内插写法都不在它眼里</b>——
/// 本批把四种写法摊开对 HEAD 重数（凭据脚本 <c>census_ry_head.py</c>，逐条清单 <c>ry_head_census.txt</c>）：
/// 共 <b>29 条命中、26 个调用点</b>，而账本只列了 12 处。漏掉的一半里还包含<b>截图文件名</b>与<b>备份档名</b>
/// （非公历区域会生成"2569…"这种文件名）。⇒ 普查的语法面＝闸门的语法面，两边都得列全。
/// </para>
/// </summary>
public sealed class DisplayDateFormatGateTests
{
    private const string JudgeFile = "src/StarMark.Abstractions/DateTimeText.cs";
    private const string UiDir = "src/StarMark.UI";

    /// <summary>
    /// 认"这是日期/时间格式"的标记表。<b>只收多字符标记</b>：单收 <c>dd</c> 会把 <c>add</c> 算成格式，
    /// 单收 <c>tt</c>/<c>mm</c> 会把 <c>Settings</c>/<c>summary</c> 算成格式——误伤的代价是闸门天天红，
    /// 最后被人绕过（#144 那条"判据要能被遵守"）。代价是极端少见的写法可能漏，已在下面 ⑤ 里写明。
    /// </summary>
    private static readonly string[] DateTokens =
    {
        "yyyy", "yy-", "MM-dd", "dd-MM", "M-d", "MM/dd", "dd/MM", "HH:mm", "hh:mm", "mm:ss", "HHmmss", "ddd", "dddd",
    };

    private static bool IsDateFormatLiteral(string literal)
    {
        // 逐字串里的 `\:`（TimeSpan 的转义冒号）不参与匹配，否则 @"h\:mm\:ss" 会躲过规则③：
        // 先剥掉反斜杠再认标记。剥的是"匹配用的副本"，报错信息里仍给原文。
        var normalized = literal.Contains('\\') ? literal.Replace("\\", string.Empty) : literal;
        return DateTokens.Any(t => normalized.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>
    /// 一次遍历同时产出两样东西：<b>抹掉注释后的代码</b>（字符串原样留着，规则②要看实参表）
    /// 和<b>全部字符串字面量</b>（含内插孔里再套的串）。
    /// <para>
    /// 为什么不沿用"按行切掉 <c>//</c> 之后"的写法：那会把 <c>"https://…"</c> 里的双斜杠当成注释，
    /// 于是<b>同一行后半段的真违规一起消失</b>——注释要跳过，但不能靠截行。
    /// </para>
    /// </summary>
    private static (string Code, List<string> Literals) Scan(string text)
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
    /// <c>$"… {due.ToString("HH:mm")} …"</c> 这种嵌套是把扫描器截断的头号原因，RI4/RI5 两条变异就是这么漏的。
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
    /// 一遇到 <c>HH:mm</c> 这种<b>自带两枚冒号</b>的格式就把 <c>due:HH</c> 当成表达式、只剩 <c>mm</c> 当格式，
    /// 于是规则①对全仓最常见的钟点内插当场失明（变异 RI12 撞出来的）。
    /// 显示侧格式基本都带冒号，所以这一条不是边角。</para>
    /// </summary>
    private static IEnumerable<(string Hole, string Format)> HoleFormats(string literal)
    {
        foreach (Match m in AnyHole.Matches(literal))
        {
            var body = m.Groups[1].Value;
            var at = body.IndexOf(':');
            if (at >= 0) yield return ("{" + body + "}", body[(at + 1)..]);
        }
    }

    /// <summary>自检：多冒号格式必须整段取回，不能只取最后一段。</summary>
    [Theory]
    [InlineData(@"$""{due:HH:mm}""", "{due:HH:mm}", "HH:mm")]
    [InlineData(@"$""{x,5:MM-dd HH:mm}""", "{x,5:MM-dd HH:mm}", "MM-dd HH:mm")]
    public void HoleFormatTakesEverythingAfterTheFirstColon(string snippet, string hole, string format)
    {
        var found = HoleFormats(Scan(snippet).Literals.First());
        Assert.Contains((hole, format), found);
        Assert.Contains(found.Select(f => f.Format), IsDateFormatLiteral);   // 钟点内插当场算违规，不靠"整串里恰好有 HH:mm"
    }

    /// <summary>取一次 <c>ToString(</c> 调用的实参表（括号配对，跨行也算）。</summary>
    private static IEnumerable<string> ToStringArgumentLists(string code)
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

    /// <summary>
    /// 某目录下全部手写源码：扁平路径（<c>ReadRepoUnder</c> 只给到文件名，所以<b>不能再拿去重读</b>）、
    /// 抹掉注释后的代码、以及<b>同一趟</b>扫出的字面量表（规则②的实参表也复用这一套走法，不另写一份）。
    /// </summary>
    private static List<(string Path, string Code, List<string> Literals)> SourcesUnder(string dir, bool skipJudge = false)
        => ReadRepoUnder(dir)
            .Where(f => !skipJudge || !f.RelativePath.EndsWith(
                Path.GetFileName(JudgeFile), StringComparison.Ordinal))
            .Select(f =>
            {
                var (code, literals) = Scan(f.Text);
                return (f.RelativePath, code, literals);
            })
            .ToList();

    // ────────── ① 内插格式：除判据外一律禁止 ──────────

    [Fact]
    public void InterpolatedDateFormatsExistNowhereButTheJudge()
    {
        var hits = new List<string>();
        foreach (var (path, _, literals) in SourcesUnder("src", skipJudge: true))
            foreach (var (hole, format) in literals.SelectMany(HoleFormats))
                if (IsDateFormatLiteral(format))
                    hits.Add($"{path}: {hole}");

        Assert.True(hits.Count == 0,
            "内插写法又长出日期格式了（这类完全没锁文化，非公历区域会得到 2569/1447 这种年份）：\n"
            + string.Join("\n", hits));
    }

    // ────────── ② ToString 传日期格式必须锁不变文化 ──────────

    [Fact]
    public void EveryDateFormatToStringCarriesInvariantCulture()
    {
        var offenders = new List<string>();
        var pinned = 0;
        foreach (var (path, code, _) in SourcesUnder("src"))
            foreach (var args in ToStringArgumentLists(code))
            {
                // 实参表里的字面量识别复用同一次 Scan，不再各写一套走法
                if (!Scan(args).Literals.Any(IsDateFormatLiteral)) continue;
                if (args.Contains("InvariantCulture", StringComparison.Ordinal)) pinned++;
                else offenders.Add($"{path}: ToString({args.Trim()})");
            }

        // 反空转：一条扫不到任何东西的闸门会冒充绿灯（#161/#189）。文件名那一路本就有十来处锁定，扫不到＝判据错了。
        Assert.True(pinned >= 10,
            $"只认出 {pinned} 处「已锁不变文化的日期格式」（预期 ≥10）——标记表或扫描路径失效了，先修闸门再说结论");
        Assert.True(offenders.Count == 0,
            "有日期格式串没锁 InvariantCulture（显示会随系统文化变历法）：\n" + string.Join("\n", offenders));
    }

    // ────────── ③ UI 工程零日期格式串 ──────────

    /// <summary>
    /// UI 里<b>连"已锁不变文化"的格式串都不许留</b>：显示口径只能有一个主人（<c>DateTimeText</c>）。
    /// 否则同一段文字在卡片里是 <c>HH:mm</c>、在组件里是 <c>DateTimeText.Clock</c>，改一处不会让另一处变红。
    /// </summary>
    [Fact]
    public void UiProjectHoldsNoDateFormatLiteralsAtAll()
    {
        var offenders = SourcesUnder(UiDir)
            .SelectMany(f => f.Literals
                .Where(IsDateFormatLiteral)
                .Select(lit => $"{f.Path}: \"{lit}\""))
            .ToList();

        Assert.True(offenders.Count == 0,
            "UI 层又自己拿日期格式串了（该走 DateTimeText）：\n" + string.Join("\n", offenders));
    }

    // ────────── ④ 星期名只从判据那张表来 ──────────

    /// <summary>
    /// <c>ddd</c>/<c>dddd</c> 除判据外一律禁止，<b>即使锁了不变文化也禁</b>——
    /// 锁不变文化只会把"星期日"换成 <c>Sunday</c>，在中文界面里那是另一种坏（P-122 原文特意点出的那一类）。
    /// </summary>
    [Fact]
    public void WeekdayNamesComeOnlyFromTheJudge()
    {
        var offenders = SourcesUnder("src", skipJudge: true)
            .SelectMany(f => f.Literals
                .Where(lit => IsDateFormatLiteral(lit) && lit.Contains("ddd", StringComparison.Ordinal))
                .Select(lit => $"{f.Path}: \"{lit}\""))
            .ToList();

        Assert.True(offenders.Count == 0,
            "日期格式串里又出现了 ddd/dddd（星期名该走 DateTimeText.Weekday 那张表）：\n"
            + string.Join("\n", offenders));
    }

    // ────────── ⑤ 判据自己也得有东西可扫 + 扫描器自检 ──────────

    /// <summary>判据不许被掏空：它自己得真的拿着那些格式串（否则上面四条会因为"到处都没有格式串"而假绿）。</summary>
    [Fact]
    public void TheJudgeItselfHoldsTheFormats()
    {
        var (code, literals) = Scan(ReadRepoFile(JudgeFile));
        var held = literals.Count(IsDateFormatLiteral);
        Assert.True(held >= 8, $"判据文件里只剩 {held} 个日期格式串——它不该是空壳");
        Assert.Contains("InvariantCulture", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>扫描器自检</b>：五种字符串写法都得看得见格式串。
    /// <para>这条是①～④的前提——扫描器只认一种写法时，那四条会"因为扫不到而全绿"，
    /// 而绿得毫无意义（#189 在 RX 那批连撞三次：行首锚定、已知值集合、"出现过"）。
    /// 与其断言"本仓没有某种写法"（<c>SeedData.cs</c> 一开始就用原始串，那样写会假得很长），
    /// 不如直接把每种语法喂给扫描器验一遍。</para>
    /// <para>仍然管不到的两种，写在这里而不是藏着：把格式串拆开拼（<c>"HH" + ":" + "mm"</c>）、
    /// 以及 <c>@$"…"</c> 内插逐字串<b>孔里</b>再套的串（外层内容仍会带着那段格式文本，所以规则①③照样红，
    /// 只是归因归到整条串上）。真出现时正确做法是扩 <see cref="IsDateFormatLiteral"/> 的识别面，
    /// 而不是放宽那六条。</para>
    /// </summary>
    [Theory]
    [InlineData(@"var a = ""yyyy-MM-dd"";")]                        // 普通串
    [InlineData(@"var a = $""{x:yyyy-MM-dd}"";")]                    // 内插孔里的格式
    [InlineData(@"var a = $""下一次 {due.ToString(""HH:mm"")}。"";")] // 内插孔里再套字符串（RI4/RI5 就是这么漏的）
    [InlineData(@"var a = @""h\:mm\:ss"";")]                         // 逐字串 + TimeSpan 的转义冒号
    [InlineData(@"var a = """"""yyyy-MM-dd"""""";")]                 // 原始串 """…"""
    public void ScannerSeesThroughEveryStringSyntax(string snippet)
        => Assert.Contains(Scan(snippet).Literals, IsDateFormatLiteral);

    /// <summary>
    /// 自检的另一面：注释里举的格式串例子<b>不该</b>被当成违规（那是解释，不是行为），
    /// 而同一行<b>注释之后</b>的真代码仍然要扫得到——截行写法做不到这一点。
    /// </summary>
    [Fact]
    public void CommentsAreSkippedWithoutEatingTheRestOfTheLine()
    {
        const string snippet = """
            // 旧口径长这样：var legacy = "yyyy-MM-dd"; 如今归判据
            var url = "https://example.com/a"; var t = now.ToString("HH:mm");
            /* 也举一次 "dd-MM-yyyy" */ var m = "MM-dd";
            """;
        var (code, literals) = Scan(snippet);

        Assert.DoesNotContain("yyyy-MM-dd", literals);            // 行注释里的例子不是行为，不该被报成违规
        Assert.DoesNotContain("dd-MM-yyyy", literals);            // 块注释里的同理
        Assert.Contains("HH:mm", literals);                       // URL 的双斜杠不是注释：同行后半段仍要扫得到（截行写法在这里会丢）
        Assert.Contains("MM-dd", literals);                       // 块注释之后同一行也还在
        Assert.Contains(@"ToString(""HH:mm"")", code, StringComparison.Ordinal);   // 规则②要看实参表，字符串得原样留着
        Assert.DoesNotContain("var legacy", code, StringComparison.Ordinal);       // 注释本体确实被抹平了
    }

    /// <summary>
    /// 字符字面量 <c>'"'</c> 里那半个引号不能冒充串起点：一旦认错，扫描器会把整段真代码当成字面量吞掉，
    /// <b>后面的违规虽然可能仍被"碰巧"看到，但归因全错、且规则②的实参表扫描当场失效</b>。
    /// </summary>
    [Fact]
    public void CharLiteralHoldingAQuoteDoesNotDerailTheScan()
    {
        const string snippet = "var sep = '\"'; var t = now.ToString(\"HH:mm\", CultureInfo.InvariantCulture);";
        var literals = Scan(snippet).Literals;

        Assert.Contains("HH:mm", literals);
        Assert.DoesNotContain(literals, lit => lit.Contains("var t = now.ToString", StringComparison.Ordinal));
    }

    // ────────── ⑥ XAML 的 StringFormat ──────────

    /// <summary>
    /// 第六种写法：XAML 的 <c>StringFormat</c>——绑定格式化用的就是绑定文化的历法，与 C# 侧同一条病。
    /// 今天全仓零命中，所以这条是<b>反回归</b>；只抽 <c>StringFormat</c> 的值而不是整文件找标记，
    /// 因为设置页里有一句人话就写着"时钟上还会多显示一行「休息 HH:mm」"，整文件扫会把它误判成违规。
    /// <para>同时钉住"扫到了足够多个 xaml"，免得扫描路径写错时靠"什么都没扫到"冒充绿灯（#161）；
    /// 而抽取本身由 <see cref="StringFormatExtractorSeesBothXamlShapes"/> 自证，不靠"全仓为零"担保。</para>
    /// </summary>
    [Fact]
    public void XamlBindingsHoldNoDateFormatEither()
    {
        var xamls = Directory
            .GetFiles(Path.Combine(RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Replace(Path.DirectorySeparatorChar, '/').Contains("/obj/"))
            .ToList();
        Assert.True(xamls.Count >= 30, $"只扫到 {xamls.Count} 个 xaml（今天是 36）——扫描路径错了，这条绿灯不算数");

        var hits = xamls
            .SelectMany(f => StringFormatsIn(File.ReadAllText(f)).Select(fmt => (File: Path.GetFileName(f), Fmt: fmt)))
            .Where(x => IsDateFormatLiteral(x.Fmt))
            .Select(x => $"{x.File}: StringFormat=\"{x.Fmt}\"")
            .ToList();

        Assert.True(hits.Count == 0,
            "XAML 里出现了日期格式（StringFormat 会按绑定文化的历法渲染），请改绑 DateTimeText 产出的字符串：\n"
            + string.Join("\n", hits));
    }

    private static readonly Regex StringFormatAttr = new(@"StringFormat\s*=\s*(?:""(?<quoted>[^""]*)""|(?<raw>[^,\s]*))");

    /// <summary>
    /// 两种写法都认：<c>StringFormat="{0:HH:mm}"</c>（属性文本，带引号）与
    /// <c>StringFormat={}{0:HH:mm}</c>（标记扩展，不带引号、值以 <c>{}</c> 转义开头）。
    /// 不带引号那一支会连尾随的 <c>}</c> 一起吃掉——这里要判的是"这段值里有没有日期格式"，
    /// 尾巴多带一个括号不影响结论；<b>宁可粗到多看一眼，不能细到漏判</b>。
    /// </summary>
    private static IEnumerable<string> StringFormatsIn(string xaml)
    {
        var withoutComments = Regex.Replace(xaml, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        foreach (Match m in StringFormatAttr.Matches(withoutComments))
        {
            var value = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["raw"].Value;
            yield return value.TrimEnd('}', '"', '\'', ' ', '/');
        }
    }

    [Fact]
    public void StringFormatExtractorSeesBothXamlShapes()
    {
        const string markup = @"<TextBlock Text=""{x:Bind TimeText, StringFormat={}{0:HH:mm}}"" />";
        const string property = @"Binding StringFormat=""yyyy-MM-dd dddd""";

        Assert.Contains(StringFormatsIn(markup), IsDateFormatLiteral);           // 标记扩展那一支
        Assert.Contains("yyyy-MM-dd dddd", StringFormatsIn(property));           // 属性文本那一支
        Assert.Empty(StringFormatsIn(@"<!-- 旧写法 StringFormat=""yyyy-MM-dd"" 已废弃 -->"));   // 注释里的例子不算行为
    }
}
