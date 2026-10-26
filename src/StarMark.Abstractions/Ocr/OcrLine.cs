#nullable enable
using System.Collections.Generic;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions.Ocr;

/// <summary>
/// 引擎读出的一行文字：<b>只有词表，没有拼好的整句</b>。
/// <para>
/// 之所以不把"这一行的字符串"直接传进来：系统 OCR 给行文本的方式是**逐词加空格拼接**
/// （中文也一样，实测 30/30 行都是 <c>Text == string.Join(" ", words)</c>），
/// 于是"你 好 世 界"这种没法读的东西必须由我们自己重拼。留着词表，拼法才是可测、可改的判据。
/// </para>
/// <para>放 <c>Abstractions</c> 是因为写它的 <c>Integrations</c>（WinRT OCR）与判它的 <c>Core</c>（拼回文本）
/// 之间不许有依赖。</para>
/// </summary>
public sealed record OcrLine(IReadOnlyList<string> Words);

/// <summary>
/// 一次识别的结论。<see cref="Error"/> 非空时 <see cref="Lines"/> 不可信。
/// <para><b>颗类型是 <see cref="LocMessage"/> 而⛔ <c>string</c></b>（批次 片117）：这位主人递的六句原因里
/// 只有「引擎自己的异常正文」是机器文本，其余五句（去哪装语言包・几种语言在场・尺寸不合法・缓冲不够长・
/// 按某个语言建不出引擎）都是<b>我们自己写的句子</b>。交 <c>string</c> 时下游只能把它们整个套进
/// <c>common.rawValue</c> 那道数据门里（<c>OcrPipeline</c> 那一处曾经就这么写），于是英文与日文界面上
/// 会出现一整句中文。交键＋实参，取词就回到贴上去的那一刻。</para>
/// </summary>
public sealed record OcrOutcome(bool Ok, IReadOnlyList<OcrLine> Lines, LocMessage? Error, string? EngineLanguage)
{
    public static OcrOutcome Fail(LocMessage reason, string? engineLanguage = null)
        => new(false, System.Array.Empty<OcrLine>(), reason, engineLanguage);

    public static OcrOutcome Success(IReadOnlyList<OcrLine> lines, string? engineLanguage)
        => new(true, lines, null, engineLanguage);
}
