#nullable enable
using System;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Core.Ocr;
using StarMark.Integrations.Capture;
using Windows.ApplicationModel.DataTransfer;

namespace StarMark.UI.Services;

/// <summary>
/// "把一块画面里的文字认出来、交给剪贴板"这条链的编排。
/// <para>
/// 三种落点（贴图工具条 / 截图动作条 / 托盘与可绑热键）都汇到这里，因为它们要报的东西完全相同：
/// 引擎有没有、按哪种语言认的、认出了多少字、复制成没成。<b>这四件事少报一件，用户就得自己猜</b>。
/// </para>
/// <para>
/// 预处理与拼回文本的判据都不在这里（<c>Core.Ocr.OcrPipeline</c> / <c>OcrText</c>，可单测，
/// 而且冒烟探针必须能原样跑到那条链，量出来的才是产品行为而不是探针行为）。
/// 这里只做"取像素 → 交出去 → 写剪贴板 → 把结果说清楚"。
/// </para>
/// </summary>
public static class OcrService
{
    /// <summary>
    /// 认这块选区（从已经抓好的一帧里裁出来）。裁不动时 <see cref="ScreenshotService"/> 那条路径已经回报过，这里不再重复。
    /// <para>
    /// 识字裁的是<b>补过边</b>的那块：用户贴着字边沿框选时，第一笔/最后一笔会被切掉，
    /// 而"少半笔"正是这套引擎读错的高频来源。补边只会把相邻的空白多带一点进来，不伤识别。
    /// </para>
    /// </summary>
    public static Task CopyTextFromSelectionAsync(ScreenFrame frame, IntRect selection)
    {
        var boxed = OcrImagePrep.Pad(selection, frame.Bounds);
        var problem = CaptureGeometry.CropProblem(boxed, frame.Bounds);
        if (problem is not null)
        {
            TrayReporter.Report("识字", "没能识别", problem);
            return Task.CompletedTask;
        }
        var (ox, oy) = CaptureGeometry.CropOffset(boxed, frame.Bounds);
        var pixels = GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, boxed.Width, boxed.Height));
        return CopyTextFromPixelsAsync(pixels, boxed.Width, boxed.Height, "识字");
    }

    /// <summary>认一块已经裁好的 BGRA 画面，并把文字复制进剪贴板。</summary>
    public static async Task CopyTextFromPixelsAsync(byte[] pixels, int width, int height, string category = "识字")
    {
        var read = await OcrPipeline.ReadAsync(pixels, width, height);
        if (!read.Ok)
        {
            TrayReporter.Report(category, "没能识别",
                (read.Error ?? "识别引擎没有给出原因") + Paren(read.How));
            return;
        }

        if (OcrText.ResultProblem(read.Text) is { } problem)
        {
            // 这是"识别成功但没有文字"，与"引擎坏了"是两件事，必须分开说：
            // 前者让用户换一块有字/更大的区域再试，后者是程序该修的问题
            TrayReporter.Report(category, "没认出文字", problem + Paren(read.How));
            return;
        }

        if (!TryCopyText(read.Text, out var copyError))
        {
            TrayReporter.Report(category, "复制失败", $"认出 {read.Chars} 字但没能写进剪贴板：{copyError}");
            return;
        }
        TrayReporter.Report(category, "已复制文字",
            $"{read.Chars} 字{Paren(read.How)}：" + OcrText.Preview(read.Text));
    }

    /// <summary>有内容才包括号：没有可交代的信息时，提示里不该出现一对空壳。</summary>
    private static string Paren(string note) => note.Length == 0 ? string.Empty : $"（{note}）";

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
