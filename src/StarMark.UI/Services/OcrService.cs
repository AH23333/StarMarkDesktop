#nullable enable
using System;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Core.Ocr;
using StarMark.Integrations.Capture;
using StarMark.Integrations.Ocr;
using Windows.ApplicationModel.DataTransfer;

namespace StarMark.UI.Services;

/// <summary>
/// "把一块画面里的文字认出来、交给剪贴板"这条链的编排。
/// <para>
/// 三种落点（贴图右键 / 截图动作条 / 托盘与可绑热键）都汇到这里，因为它们要报的东西完全相同：
/// 引擎有没有、按哪种语言认的、认出了多少字、复制成没成。<b>这四件事少报一件，用户就得自己猜</b>。
/// </para>
/// <para>
/// 拼回文本的规则不在这里（在 <c>Core.Ocr.OcrText</c>，可单测）；这里只做"取像素 → 交引擎 → 写剪贴板 → 回报"。
/// </para>
/// </summary>
public static class OcrService
{
    /// <summary>认这块选区（从已经抓好的一帧里裁出来）。裁不动时 <see cref="ScreenshotService"/> 那条路径已经回报过，这里不再重复。</summary>
    public static Task CopyTextFromSelectionAsync(ScreenFrame frame, IntRect selection)
    {
        var problem = CaptureGeometry.CropProblem(selection, frame.Bounds);
        if (problem is not null)
        {
            TrayReporter.Report("识字", "没能识别", problem);
            return Task.CompletedTask;
        }
        var (ox, oy) = CaptureGeometry.CropOffset(selection, frame.Bounds);
        var pixels = GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, selection.Width, selection.Height));
        return CopyTextFromPixelsAsync(pixels, selection.Width, selection.Height, "识字");
    }

    /// <summary>认一块已经裁好的 BGRA 画面，并把文字复制进剪贴板。</summary>
    public static async Task CopyTextFromPixelsAsync(byte[] pixels, int width, int height, string category = "识字")
    {
        var outcome = await ScreenOcrReader.RecognizeAsync(pixels, width, height);
        var engineNote = string.IsNullOrEmpty(outcome.EngineLanguage) ? string.Empty : $"（按 {outcome.EngineLanguage} 识别）";
        if (!outcome.Ok)
        {
            TrayReporter.Report(category, "没能识别", (outcome.Error ?? "识别引擎没有给出原因") + engineNote);
            return;
        }

        var text = OcrText.Assemble(outcome.Lines);
        if (OcrText.ResultProblem(text) is { } problem)
        {
            // 这是"识别成功但没有文字"，与"引擎坏了"是两件事，必须分开说：
            // 前者让用户换一块有字的区域再试，后者是程序该修的问题
            TrayReporter.Report(category, "没认出文字", problem + engineNote);
            return;
        }

        var chars = OcrText.CountMeaningful(text);
        if (!TryCopyText(text, out var copyError))
        {
            TrayReporter.Report(category, "复制失败", $"认出 {chars} 字但没能写进剪贴板：{copyError}");
            return;
        }
        TrayReporter.Report(category, "已复制文字",
            $"{chars} 字{engineNote}：" + OcrText.Preview(text));
    }

    /// <summary>写剪贴板。失败必须报出来（用户下一步就是粘贴），不能只记日志。</summary>
    private static bool TryCopyText(string text, out string? error)
    {
        error = null;
        try
        {
            // 登记回声：这是本仓库既定的硬规则（App.NoteClipboardOwnWrite 的注释：任何往剪贴板写内容
            // 的地方都要先登记，否则用户正开着剪贴板历史页，列表会因为我们自己的一次复制而重排）。
            // 另一层好处是 OCR 结果不会以明文自动进历史——框选到密码框之类的画面时，那不该被静默归档。
            App.NoteClipboardOwnWrite(text);
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();     // 延迟渲染会在调用方窗口一关时丢掉内容
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("[Ocr] 写入剪贴板失败", ex);
            error = ex.Message;
            return false;
        }
    }
}
