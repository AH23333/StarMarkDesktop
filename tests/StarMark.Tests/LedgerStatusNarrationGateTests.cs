#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 活档里那句「某颗 P 现在还没闭」必须和<b>它自己题行那张牌</b>对上（批次 Phase C 片106）。
/// <para>
/// 这一族已经栽过两次，所以本批给它配证人：片100 抓到「照旧 ⛔ 闭」清单里那颗「片87 真机腿 (B)」
/// 系幻影（P-249 早已 <c>4db151c</c> 量毕转 ✅）；片105 又写出「P-255 照旧开放＝归它自己」，
/// 而 P-255 的题行是 <c>✅P-255（片82 立・片83 落地并结案）</c>——正本 <c>bin/WI99/bench83_console.txt</c>
/// 与 <c>bin/WI99/board83_console_r2.txt</c> 都在盘上（现读那颗档在场）。
/// 成因不是手滑，是一条<b>抄写通路</b>：新一行的「照旧 ⛔ 闭」清单照抄上一批那一行，
/// 而清单里引用的那颗号已经在别的批次结案了。只补一句更正＝下一批照抄时再犯一次。
/// </para>
/// <para>
/// <b>射程＝只数「说现在的那几行」</b>：账本里每颗条目从<b>圈号最大的那颗「本批落地」行</b>起、到本节末；
/// 报告只认那颗末节。它上面的行是历史证据——按坑表 #981 那一课的口径（快照⛔ 倒着改），本尺一个字⛔ 报。
/// </para>
/// <para>
/// <b>两颗刻意留下的豁免</b>，少一颗就是成片假红：
/// ① 紧跟在那颗 P 后面的圈号（<c>P-249 ⑩ 两颗在册未验证项照旧</c>）说的是<b>那条目节里的半颗</b>而⛔ 条目本身，
/// 已结案条目登记自己那半颗未验证项是合法形状；
/// ② 题行没打 ✅ 的那颗（真开放）本尺⛔ 碰，它归账本计数器那道等式管——本尺只问「话与牌对不对得上」。
/// </para>
/// </summary>
public sealed class LedgerStatusNarrationGateTests
{
    private const string Ledger = "docs/待决策事项.md";
    private const string Report = "docs/开发进度报告.md";

    /// <summary>那一族的词形。加一颗词形＝放宽射程，本批按现读到的形状写，⛔ 预铺。</summary>
    private static readonly string[] OpenWords = { "照旧开放", "照旧 ⛔ 闭", "照旧未闭", "仍开放", "还开着" };

    private static readonly Regex Heading = new(@"^## (?<done>✅)?P-(\d+)（", RegexOptions.Compiled);
    private static readonly Regex Row = new(@"^- <b>([\u2460-\u2473\u3251-\u325f]) 本批落地", RegexOptions.Compiled);
    private static readonly Regex Pid = new(@"P-(\d+)", RegexOptions.Compiled);

    /// <summary>主语窗口：那颗词往回 60 字内找最后一颗 P。窗口颗数由现读到的句子形状定，⛔ 随手放宽。</summary>
    private const int SubjectWindow = 60;

    private readonly record struct Phantom(string Doc, int Line, int Pid, string Heading);

    /// <summary>真行切分：<b>孤 CR 也算行尾</b>（名册 407——同一颗档两种读法行数差三倍，这里要的是"一句一行"那一族）。</summary>
    private static string[] RealLines(string text) => Regex.Split(text, "\r\n|\r|\n");

    /// <summary>题行那张牌：编号 → 是不是已经打了 ✅。认不到题行的那颗交给 <see cref="ClosedOnly"/> 之外那一档放过。</summary>
    private static Dictionary<int, bool> Statuses(string ledgerText)
    {
        var map = new Dictionary<int, bool>();
        foreach (var line in RealLines(ledgerText))
        {
            var m = Heading.Match(line);
            if (m.Success) map[int.Parse(m.Groups[1].Value)] = m.Groups["done"].Success;
        }
        return map;
    }

    /// <summary>圈号那颗字符 → 它是第几行（①…⑳ 之后接 ㉑…㉟）；认不得的那一副交回 0＝不算登记行。</summary>
    private static int Ordinal(string circled)
    {
        var c = circled[0];
        if (c is >= '\u2460' and <= '\u2473') return c - '①' + 1;
        if (c is >= '\u3251' and <= '\u325f') return c - '㉑' + 21;
        return 0;
    }

    /// <summary>那一行里，每颗词各自的<b>主语</b>：该词往前 60 字里最后一颗 P；第二列＝那颗 P 后面紧跟圈号（子项形状）。</summary>
    private static List<(int Pid, bool SubItem)> Subjects(string line)
    {
        var found = new List<(int, bool)>();
        foreach (var w in OpenWords)
        {
            for (var at = line.IndexOf(w, StringComparison.Ordinal); at >= 0; at = line.IndexOf(w, at + 1, StringComparison.Ordinal))
            {
                var window = line[Math.Max(0, at - SubjectWindow)..at];
                var toks = Pid.Matches(window).ToList();
                if (toks.Count == 0) continue;                       // 那句没有主语（整节标题那一行）
                var last = toks[^1];
                var after = window[(last.Index + last.Length)..];
                found.Add((int.Parse(last.Groups[1].Value), after.Any(c => (c is >= '①' and <= '⑳') || (c is >= '㉑' and <= '㉟'))));
            }
        }
        return found;
    }

    /// <summary>「说现在」那几行的行号。<paramref name="allRows"/>＝整档（只用来证明这把尺认得到那一族）。</summary>
    private static IEnumerable<int> CurrentLines(string text, bool ledger, bool allRows)
    {
        var lines = RealLines(text);
        var prefix = ledger ? "## " : "### ";
        var starts = Enumerable.Range(0, lines.Length).Where(i => lines[i].StartsWith(prefix, StringComparison.Ordinal)).ToList();
        starts.Add(lines.Length);
        for (var s = 0; s + 1 < starts.Count; s++)
        {
            var (a, b) = (starts[s], starts[s + 1]);
            if (allRows) { for (var i = a; i < b; i++) yield return i; continue; }
            if (ledger)
            {
                var rows = Enumerable.Range(a, b - a).Where(i => Row.IsMatch(lines[i])).ToList();
                if (rows.Count == 0) continue;                      // 盘点／卷首那类整档快照节⛔ 说现在
                var max = rows.Max(i => Ordinal(Row.Match(lines[i]).Groups[1].Value.ToString()));
                var cur = rows.First(i => Ordinal(Row.Match(lines[i]).Groups[1].Value.ToString()) == max);
                for (var i = cur; i < b; i++) yield return i;
            }
            else if (s + 2 == starts.Count)                          // 报告：只有末节算现在
            {
                for (var i = a; i < b; i++) yield return i;
            }
        }
    }

    private static List<Phantom> Scan(string docName, string text, bool ledger, IReadOnlyDictionary<int, bool> statuses, bool allRows = false)
    {
        var lines = RealLines(text);
        var hits = new List<Phantom>();
        var headOf = new string?[lines.Length];
        string? last = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) || lines[i].StartsWith("### ", StringComparison.Ordinal)) last = lines[i];
            headOf[i] = last;
        }
        foreach (var i in CurrentLines(text, ledger, allRows))
        {
            foreach (var (pid, subItem) in Subjects(lines[i]))
            {
                if (subItem) continue;                                             // 豁免①：说的是那半颗
                if (!statuses.TryGetValue(pid, out var done) || !done) continue;    // 豁免②：真开放／认不到题行
                hits.Add(new Phantom(docName, i + 1, pid, headOf[i] ?? "<认不到归属节>"));
            }
        }
        return hits;
    }

    private static string Read(string rel) => SourceGate.ReadRepoFile(rel);

    /// <summary>
    /// 正负件各四格：这把尺<b>报得出那颗幻影</b>，也<b>认得出那三副不该报的样子</b>——
    /// 少了后三格，这一格红就分不清是判据咬到了还是扫描器把整族一锅端。
    /// </summary>
    [Fact]
    public void TheScannerBitesOnlyThePhantomShape()
    {
        var synthetic = string.Join("\n",
            "## ✅P-901（片1 立·片2 结案）：已结案那颗",
            "- <b>① 本批落地（片2）</b>：本条已结案。",
            "## ✅P-902（片3 立·片4 结案）：结案了但节里还挂着半颗未验证项",
            "- <b>① 本批落地（片4）</b>：本条已结案。",
            "## P-900（片1 登记）：真开放那颗",
            "- <b>① 本批落地（片5）</b>：那行写「P-901 照旧开放」＝历史证据，本尺⛔ 报它。",
            "- <b>② 本批落地（片6）</b>：照旧 ⛔ 闭的那几半：P-901（屏幕上那句话）照旧开放＝幻影・P-902 ⑩ 那半颗未验证项仍开放＝子项形状・P-900 自己照旧开放＝真开放。",
            "");
        var statuses = Statuses(synthetic);

        var hits = Scan("synthetic", synthetic, ledger: true, statuses);
        Assert.Single(hits);                                                   // ②那一行里只有 P-901 够格
        Assert.Equal(901, hits[0].Pid);
        Assert.Equal(7, hits[0].Line);                                         // ①那一行的同样句子被当成历史证据放过
        Assert.StartsWith("## P-900", hits[0].Heading, StringComparison.Ordinal);
        Assert.True(statuses[901] && statuses[902] && !statuses[900], "题行那张牌读错了：✅／不带✅这一维是判据的地基");

        // 整档口径（⛔ 现时段）里那句历史证据照旧认得到 ⇒ 同一把尺、只是不罚它
        Assert.Contains(Scan("synthetic", synthetic, ledger: true, statuses, allRows: true), h => h.Line == 6 && h.Pid == 901);

        // 报告那一档只认末节：幻影在倒数第二节＝历史，进不了射程
        var report = string.Join("\n",
            "### 第九节 · 上一批：P-901 照旧开放＝历史快照",
            "",
            "### 第十节 · 这一批：P-901 照旧开放＝现时陈述",
            "");
        var inReport = Scan("report", report, ledger: false, statuses);
        Assert.Single(inReport);
        Assert.Equal(3, inReport[0].Line);
    }

    /// <summary>这把尺在<b>真档</b>上认得到那一族（⛔ 恒绿＝"剩下比实际少"那一族伪装，名册 715～721）。</summary>
    [Fact]
    public void TheScannerIsNotDeafOnTheRealLedger()
    {
        var statuses = Statuses(Read(Ledger));
        Assert.NotEmpty(statuses);

        var anywhere = Scan(Ledger, Read(Ledger), ledger: true, statuses, allRows: true);
        Assert.True(anywhere.Count >= 1,
            $"整档扫「P-NNN＋那一族词」而题行已 ✅ 的格子一颗都没有 ⇒ 先怀疑扫描器或那一族词形变了，⛔ 把「现时段 0 颗」当成已经守住");
    }

    /// <summary>
    /// 主判据：<b>说现在的那几行</b>里，⛔ 许出现「某颗题行已打 ✅ 的条目照旧开放」。基线＝0（颗数与"现时段"的定义绑死）。
    /// </summary>
    [Fact]
    public void NoPhantomOpenClaimInRowsThatSpeakNow()
    {
        var ledgerText = Read(Ledger);
        var statuses = Statuses(ledgerText);

        var offenders = Scan(Ledger, ledgerText, ledger: true, statuses)
            .Concat(Scan(Report, Read(Report), ledger: false, statuses))
            .ToList();

        Assert.True(offenders.Count == 0,
            "活档里那几行「说现在」的话把已结案的条目写成了还开着：" +
            string.Join(" ・ ", offenders.Select(o => $"{o.Doc} 第 {o.Line} 行・P-{o.Pid}・归属「{o.Heading[..Math.Min(40, o.Heading.Length)]}」")) +
            "——处置＝就地补一句〔片NN 更正〕并在最新那一行把清单重写（⛔ 删历史原文）。");
    }
}
