#nullable enable
using System.Collections.Generic;

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

/// <summary>一次识别的结论。<see cref="Error"/> 非空时 <see cref="Lines"/> 不可信。</summary>
public sealed record OcrOutcome(bool Ok, IReadOnlyList<OcrLine> Lines, string? Error, string? EngineLanguage)
{
    public static OcrOutcome Fail(string reason, string? engineLanguage = null)
        => new(false, System.Array.Empty<OcrLine>(), reason, engineLanguage);

    public static OcrOutcome Success(IReadOnlyList<OcrLine> lines, string? engineLanguage)
        => new(true, lines, null, engineLanguage);
}
