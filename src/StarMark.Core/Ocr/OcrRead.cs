#nullable enable
namespace StarMark.Core.Ocr;

/// <summary>
/// 一次识字的结果，连带的"这次是怎么认的"那几句话。
/// <para>
/// 为什么要把过程一起返回而不是只给文本：同一块画面按不同语言、不同放大倍数认出来的东西差别很大，
/// 用户反馈"认错字"时，<b>唯一的线索就是当时按哪种语言认的</b>。所以这句话不是日志装饰，是返回值的一部分。
/// </para>
/// </summary>
public sealed record OcrRead
{
    /// <summary>引擎是否正常返回（false 时看 <see cref="Error"/>）。</summary>
    public required bool Ok { get; init; }

    /// <summary>拼回好的文本（已按中英文规则去掉词间多余空格）。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>有内容的字符数（不是"词数"：中文没有词这个概念）。</summary>
    public int Chars { get; init; }

    /// <summary>实际生效的引擎语言；null＝引擎没报。</summary>
    public string? Language { get; init; }

    /// <summary>送引擎前的放大倍数（1＝没放大）。</summary>
    public int Upscale { get; init; } = 1;

    /// <summary>是否因为首语言几乎没认出字而换过语言。</summary>
    public bool SwappedLanguage { get; init; }

    /// <summary>失败原因（仅 <see cref="Ok"/> 为 false 时有值）。</summary>
    public string? Error { get; init; }

    public static OcrRead Fail(string error, string? language = null)
        => new() { Ok = false, Error = error, Language = language };

    /// <summary>
    /// 一行"怎么认的"。<b>为空表示没什么可交代的</b>（按用户配置语言、没放大、没换语言），
    /// 调用方据此省略括号，别在提示里留一对空壳。
    /// </summary>
    public string How
        => (Language is null ? string.Empty : $"按 {Language}")
         + (Upscale > 1 ? $"，放大 {Upscale}×" : string.Empty)
         + (SwappedLanguage ? "，已换语言" : string.Empty);

    /// <summary>带括号的版本，直接拼进提示文案；没有可交代的信息时返回空串。</summary>
    public string HowParenthesized => How.Length == 0 ? string.Empty : $"（{How}）";
}
