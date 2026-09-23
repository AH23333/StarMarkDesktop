#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using StarMark.Abstractions.Ocr;
using StarMark.Abstractions.Text;

namespace StarMark.Core.Ocr;

/// <summary>
/// 把系统 OCR 吐出的"一行一个词表"拼回**可读的文字**。
/// <para>
/// 为什么这件事必须自己做：实测本机的 Windows.Media.Ocr 在 30/30 行上都满足
/// <c>line.Text == string.Join(" ", words)</c> —— 也就是它给中文也**逐词加空格**。
/// 屏幕上的"本场据"会被切成 <c>本|场|据</c> 三个词，直接用 <c>Text</c> 就得到
/// "本 场 据"，粘回去没法用。所以引擎的行文本我们不看，只拿词表自己拼。
/// </para>
/// <para>
/// 拼法只有两条规则，且都来自真实输出而不是猜想：
/// ① <b>两侧都是"黏字符"（中日韩文字或全角标点）时不加空格</b>——中文词块由此复原；
/// ② <b>前一个词以全角标点收尾时也不加空格</b>——实测有 <c>：|R</c> 这种"全角冒号后跟拉丁"的切法，
/// 按规则①会加出一个 <c>： R</c>，而原文是 <c>：R</c>。
/// 其余一律加空格：这样 <c>是|p5</c> 拼成 <c>是 p5</c>（保留空格），
/// 而不会黏成 <c>是p5</c>——中文与拉丁/数字相邻时加空格是可接受排版，反过来把两个英文词黏成一个才是要命的错。
/// </para>
/// </summary>
public static class OcrText
{
    /// <summary>提示气泡里能放下的预览长度（再长就看不见重点了；复制到剪贴板的始终是全文）。</summary>
    public const int PreviewChars = 48;

    /// <summary>一行的词表拼成该行文本（规则见类型注释）。</summary>
    public static string JoinWords(IReadOnlyList<string>? words)
    {
        if (words is null || words.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var raw in words)
        {
            var word = (raw ?? string.Empty).Trim();
            if (word.Length == 0) continue;
            if (sb.Length > 0 && NeedsSpace(sb[^1], word[0])) sb.Append(' ');
            sb.Append(word);
        }
        return sb.ToString();
    }

    /// <summary>整次识别拼成一段可复制的多行文本：空行丢掉（引擎会给一批只有空格的行）。</summary>
    public static string Assemble(IReadOnlyList<OcrLine>? lines)
    {
        if (lines is null || lines.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var text = JoinWords(line?.Words);
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 有内容的字符数（不含任何空白，含全角空格）。回报里说"识别出 N 字"用的就是这个数：
    /// <b>不能用空格分词算"词数"</b>——中文本来没有空格，那样算出来永远是 1，是假数字。
    /// </summary>
    public static int CountMeaningful(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 0;
        foreach (var c in text) if (!char.IsWhiteSpace(c)) count++;
        return count;
    }

    /// <summary>压成一行、截到 <see cref="PreviewChars"/> 字的预览（气泡用；剪贴板里始终是全文）。</summary>
    public static string Preview(string? text, int maxChars = PreviewChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var pending = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) pending = true;
                continue;
            }
            if (pending) { sb.Append(' '); pending = false; }
            sb.Append(c);
            if (sb.Length >= maxChars) break;
        }
        // 只有"确实还有内容没显示"才加省略号：正好在结尾处截断会给出一个假"还有更多"
        return sb.ToString().TrimEnd() + (HasMoreThan(text, maxChars) ? "…" : string.Empty);    }

    /// <summary>识别结果能不能用。返回 null＝可以；否则是给界面直接显示的原因。</summary>
    public static string? ResultProblem(string assembled)
        => assembled.Length == 0 ? "这块画面里没有认出文字（图形、手写或太小的字都不在这套引擎的能力里）" : null;

    /// <summary>
    /// 黏字符：中日韩文字（含假名/韩文音节）与全角标点/全角形式。
    /// 判"文字"复用 <see cref="CjkTokenizer"/>（索引侧同一份定义），全角标点这里单独补：
    /// 索引侧不需要它，而拼接时 <c>据|：</c> 与 <c>（|）</c> 必须知道标点该跟着黏。
    /// </summary>
    public static bool IsGlue(char c) => CjkTokenizer.IsCjk(c) || IsFullWidthPunctuation(c);

    /// <summary>全角标点与全角形式区（CJK 符号标点 、。「」・ 也在这里）。写成码点范围，不靠字面字符。</summary>
    public static bool IsFullWidthPunctuation(char c)
        => (c >= '　' && c <= '〿')
        || (c >= '！' && c <= '｠')
        || (c >= '￠' && c <= '￦');

    private static bool NeedsSpace(char previous, char next)
        => !(IsGlue(previous) && IsGlue(next)) && !IsFullWidthPunctuation(previous);

    /// <summary>
    /// 预览之外是否还有内容。判据与 <see cref="Preview"/> 同一口径（<b>数有内容的字符</b>）：
    /// 先前按"扫过多少字符"算，前导空格会被计入，于是 "　　ab" 会带着一个假的省略号。
    /// </summary>
    private static bool HasMoreThan(string text, int maxChars) => CountMeaningful(text) > maxChars;
}
