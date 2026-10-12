#nullable enable
using StarMark.Abstractions.I18n;
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

    /// <summary>
    /// 失败原因（仅 <see cref="Ok"/> 为 false 时有值）。
    /// <para>✅WI-65 片3 第二刀：这颗交「键＋实参」。引擎自己吐的那句话<b>是机器文本</b> ⇒ 它在
    /// <c>OcrPipeline</c> 那一处走 <c>common.rawValue</c> 那道数据门进来；我们说得出原因的那一句交键。
    /// ⛔ 把成品字交出去——那条提示卡挂着到下一次操作，宿主拿到念完的那句就等于把语言钉在算出它那一刻。</para>
    /// </summary>
    public LocMessage? Error { get; init; }

    public static OcrRead Fail(LocMessage error, string? language = null)
        => new() { Ok = false, Error = error, Language = language };

    /// <summary>
    /// 一行"怎么认的"。<b>为空表示没什么可交代的</b>（按用户配置语言、没放大、没换语言），
    /// 调用方据此省略括号，别在提示里留一对空壳。
    /// <para>⚠ 片段自己⛔ 带分隔符：从前每颗片段开头写死一颗全角逗号，于是"引擎没报语言、又放大了"那一刻
    /// 屏幕上会出现「，放大 2×」这种开头多一颗逗号的句子。分隔符现在是一颗独立的键（`ocr.how.join`），
    /// 只由拼接处放一次；词全部住在表里（批次 WI-46b，P-177 第二刀）。
    /// <para>✅WI-65 片3 第二刀：拼接交 <see cref="LocJoin"/> 而⛔ 在 C# 里 <c>string.Join</c> 念完——
    /// 那颗连接符（<c>ocr.how.join</c>）三列各不同形，念早了就把"这一串用哪门话的间隔"钉死在算出它那一刻。</para>
    /// </summary>
    public LocJoin How => new LocJoin(new[]
    {
        Language is null ? null : LocMessage.Of("ocr.how.byLanguage", Language),
        Upscale > 1 ? LocMessage.Of("ocr.how.upscale", Upscale) : null,
        SwappedLanguage ? LocMessage.Of("ocr.how.swapped") : null,
    }.Where(static m => m is not null).ToList(), "ocr.how.join");

    /// <summary>
    /// 带括号的版本，直接拼进提示文案；没有可交代的信息时回 <b>null</b>（＝"这一处没话说"，宿主据此连括号都不贴）。
    /// <para><b>括号形状只有这一颗</b>：`OcrService` 从前另写了一份 `Paren(...)`，两份"怎么包括号"迟早分岔
    /// （而 en 那一份该用半角，所以形状本身也得随语言走 ⇒ 词在 `ocr.how.paren`）。</para>
    /// </summary>
    public LocMessage? HowParenthesized => How.Items.Count == 0 ? null : LocMessage.Of("ocr.how.paren", How);
}
