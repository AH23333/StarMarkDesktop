#nullable enable
using System;
using System.Threading.Tasks;
using StarMark.Integrations.Ocr;
using StarLog = StarMark.Abstractions.StarLog;

namespace StarMark.Core.Ocr;

/// <summary>
/// 识字的正主这条链：<b>预处理 → 交引擎 → 弱结果换语言再试 → 拼回文本</b>。
/// <para>
/// 放在 Core 而不是 UI 有两个理由：① 判据（放大几倍、什么算弱、换不换语言）是可单测的决策，
/// 不该埋在拿不到像素的界面层；② 冒烟探针要量的就是这条链的改善幅度，
/// 探针另抄一份"看起来一样的"流程，量出来的就不是产品行为。
/// </para>
/// <para>
/// <b>本类绝不改动调用方传进来的像素</b>：贴图窗交出来的就是它正在显示的那份缓冲，
/// 就地转灰度等于"点一下识字，贴图变灰图"。所以无论是否放大，先复制再处理。
/// </para>
/// </summary>
public static class OcrPipeline
{
    /// <summary>
    /// 认一块已经裁好的 BGRA 画面。<paramref name="pixels"/> 按 <paramref name="width"/> × <paramref name="height"/> 逐行紧排。
    /// <para>
    /// 送引擎前三件事（判据全在 <see cref="OcrImagePrep"/>）：补边由调用方在裁切时做、
    /// 整数倍最近邻放大、灰度 + 对比拉伸。桌面文字基本都是 12–14px 的小字，小字号是这套引擎最主要的失败源。
    /// </para>
    /// </summary>
    public static async Task<OcrRead> ReadAsync(byte[] pixels, int width, int height)
    {
        var factor = OcrImagePrep.UpscaleFactor(width, height);
        byte[] work;
        int workWidth;
        int workHeight;
        try
        {
            if (factor <= 1)
            {
                work = (byte[])pixels.Clone();
                (workWidth, workHeight) = (width, height);
            }
            else
            {
                (work, workWidth, workHeight) = OcrImagePrep.Upscale(pixels, width, height, factor);
            }
        }
        catch (Exception ex)
        {
            // 放大失败（尺寸溢出/内存吃紧）不值得整件事失败：按原图认，至少不比不处理差
            StarLog.Warn($"[Ocr] 放大预处理退回原图：{ex.Message}");
            factor = 1;
            work = (byte[])pixels.Clone();
            workWidth = width;
            workHeight = height;
        }
        OcrImagePrep.StretchContrast(work, workWidth, workHeight);

        var first = await ScreenOcrReader.RecognizeAsync(work, workWidth, workHeight);
        if (!first.Ok) return OcrRead.Fail(first.Error ?? "识别引擎没有给出原因", first.EngineLanguage);

        var language = first.EngineLanguage;
        var text = OcrText.Assemble(first.Lines);
        var chars = OcrText.CountMeaningful(text);
        var swapped = false;

        // 认了个空、区域却明显够大 ⇒ 大概率是引擎语种与画面语种不符（本机装了多种语言包时尤其常见）
        if (OcrImagePrep.ShouldRetryWithOtherLanguage(chars, (long)width * height))
        {
            foreach (var tag in ScreenOcrReader.AlternativeLanguages(language))
            {
                var alt = await ScreenOcrReader.RecognizeAsync(work, workWidth, workHeight, tag);
                if (!alt.Ok) continue;
                var altText = OcrText.Assemble(alt.Lines);
                var altChars = OcrText.CountMeaningful(altText);
                if (altChars <= chars) continue;     // 并列时保持用户配置语言：换了也没多认出东西
                (language, text, chars, swapped) = (tag, altText, altChars, true);
            }
        }

        return new OcrRead
        {
            Ok = true,
            Text = text,
            Chars = chars,
            Language = language,
            Upscale = factor,
            SwappedLanguage = swapped,
        };
    }
}
