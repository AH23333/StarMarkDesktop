#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarMark.Abstractions.Ocr;
using Windows.Media.Ocr;
// Windows.Media.Ocr 里也有一个 OcrLine（引擎给的那一行）。本文件要在两者之间做映射，
// 不起别名的话"OcrLine"就是二义名；我们的模型才是这个仓库里的正主，所以别名指给它。
using OcrLine = StarMark.Abstractions.Ocr.OcrLine;

namespace StarMark.Integrations.Ocr;

/// <summary>
/// 用系统自带的 <c>Windows.Media.Ocr</c> 认一块画面里的文字：零新增依赖（引擎在 Windows SDK 里）、
/// 完全离线、不发任何网络请求。
/// <para>
/// <b>本文件不做任何"怎么拼成一段话"的判断</b>——那在 <c>StarMark.Core.Ocr.OcrText</c>（可单测，
/// 且判据要能钉住引擎实际给词的形状）。这里只负责：拿不拿得到引擎、把像素交出去、
/// 把引擎给的行/词原样搬成 Abstractions 的模型，以及**说清失败是哪一种**。
/// </para>
/// <para>
/// 引擎语言取**用户配置语言**（<see cref="OcrEngine.TryCreateFromUserProfileLanguages"/>）并随结果回报：
/// 同一张图按中文与按英文认出来的东西不一样，"按哪种认的"是事后判断结果的唯一线索。
/// </para>
/// </summary>
public static class ScreenOcrReader
{
    /// <summary>这台机器装了哪些 OCR 语言包。取不到就返回空表（调用方据此给"没有能力"的原因）。</summary>
    public static IReadOnlyList<string> AvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(language => language.LanguageTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Array.Empty<string>();
        }
    }

    /// <summary>最近一次原生调用失败的原因（没有则 null）。可用性判断与失败原因必须能分开看。</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// 没有可用引擎时给用户的说法。<b>要具体到"去哪一步"</b>：OCR 语言包是 Windows 的可选功能，
    /// 程序无法替用户装上（那要系统级权限与下载），所以能做的只有把路径说清楚。
    /// </summary>
    public static string NoEngineReason(IReadOnlyList<string> available)
        => available.Count == 0
            ? "这台 Windows 没有安装任何文字识别（OCR）语言包：设置 → 时间和语言 → 语言和区域 →"
              + " 在用到的语言上点「…」→ 语言选项 → 添加「光学字符识别」，装好后无需重启即可用"
            : $"系统报告有 {available.Count} 种 OCR 语言（{string.Join("、", available)}），但仍没能创建识别引擎";

    /// <summary>
    /// 认一块 BGRA 画面。返回的 <see cref="OcrOutcome.Lines"/> 是引擎原样的词表，
    /// 拼成文本请走 <c>OcrText.Assemble</c>。
    /// </summary>
    public static async Task<OcrOutcome> RecognizeAsync(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return OcrOutcome.Fail($"画面尺寸不合法（{width} × {height}），交给引擎只会拿到空结果");
        if (bgra.Length < (long)width * height * 4)
            return OcrOutcome.Fail("像素缓冲比声明的尺寸短，交给引擎会读到越界数据");

        var available = AvailableLanguages();
        OcrEngine? engine;
        try
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return OcrOutcome.Fail("创建识别引擎时出错：" + ex.Message, available.Count > 0 ? available[0] : null);
        }
        if (engine is null)
            return OcrOutcome.Fail(NoEngineReason(available), available.Count > 0 ? available[0] : null);

        try
        {
            var buffer = Write(bgra);
            using (var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                       buffer, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, width, height,
                       Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied))
            {
                var result = await engine.RecognizeAsync(bitmap);
                var lines = result.Lines
                    .Select(line => new OcrLine(line.Words.Select(word => word.Text ?? string.Empty).ToList()))
                    .ToList();
                LastError = null;
                return OcrOutcome.Success(lines, engine.RecognizerLanguage?.LanguageTag);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return OcrOutcome.Fail("识别没有完成：" + ex.Message, engine.RecognizerLanguage?.LanguageTag);
        }
    }

    /// <summary>
    /// byte[] → WinRT IBuffer。<b>刻意不 dispose 这个 DataWriter</b>：<c>DetachBuffer()</c> 之后
    /// writer 已经交出所有权，再 Close/Dispose 就是使用已失效的对象（实测能跑通、也实测不必收尾）。
    /// </summary>
    private static Windows.Storage.Streams.IBuffer Write(byte[] bgra)
    {
        var writer = new Windows.Storage.Streams.DataWriter();
        writer.WriteBytes(bgra);
        return writer.DetachBuffer();
    }
}
