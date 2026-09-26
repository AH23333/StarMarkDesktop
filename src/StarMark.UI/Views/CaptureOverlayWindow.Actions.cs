#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

namespace StarMark.UI.Views;

/// <summary>
/// 动作条：复制 / 存图 / 贴图 / 识字这些出口。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 动作 ──────────

    private void Copy_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Copy);

    private void Save_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Save);

    private void Pin_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Pin);

    private void Ocr_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Ocr);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_pinned) Close();          // 贴图态的 ✕＝关闭这张图（与 Esc 同一件事）
        else Settle(null);
    }

    private void Through_Click(object sender, RoutedEventArgs e) => PinManager.ToggleClickThrough();

    private enum CommitAction { Copy, Save, Pin, Ocr }

    /// <summary>
    /// 提交这一屏的选区：复制、存图、钉住，或认字并复制文字。四个落点交的都是<b>带上标注的那一份画面</b>。
    /// 先算好像素再 Settle：服务收到结果就会关掉所有遮罩窗（包括本窗），
    /// 反过来先干活会让用户在裁图期间还被困在暗幕里。
    /// </summary>
    private void Commit(CommitAction action)
    {
        // 没按 Enter 就点动作按钮：刚打的那行字要跟着图一起走，而不是被丢掉（四条落点同一条出口）。
        EndTextEditing(commit: true);
        if (_selection is not { } selection)
        {
            Settle(null);
            return;
        }
        if (!_pinned && CaptureGeometry.SelectionProblem(selection, _monitor) is { } reason)
        {
            ShowError(reason);
            return;
        }
        if (FinalPixels() is not { } final) return;       // 原因已经在里面报过了，这里只负责不再往下走
        if (!_pinned) Settle(selection);
        // 贴图态交出去的也是"当前这一份"：贴图显示的就是合成预览，所见即所存这条不变。
        var category = _pinned ? "贴图" : "截图";
        switch (action)
        {
            case CommitAction.Copy:
                _ = ScreenshotService.CopyPixelsAsync(final.Pixels, final.Width, final.Height, category);
                break;
            case CommitAction.Save:
                _ = ScreenshotService.SavePixelsAsync(final.Pixels, final.Width, final.Height, category);
                break;
            case CommitAction.Ocr:
                _ = OcrService.CopyTextFromPixelsAsync(final.Pixels, final.Width, final.Height,
                    _pinned ? "贴图识字" : "截图识字");
                break;
            default: ScreenshotService.PinPixels(final.Pixels, final.Width, final.Height, selection); break;
        }
    }

    /// <summary>
    /// 要交出去的那份画面。两态两条路：
    /// <b>贴图态</b>拿底图重烤一次标注（连"一条都没画"也走这条路，结果就是原样），尺寸取底图自己的
    /// （<see cref="_contentWidth"/>，<b>不取选区的显示尺寸</b>——贴图 2.5× 时按显示尺寸渲染是放大糊图）；
    /// <b>截图态</b>确认过选区后直接从合成图（帧＋标注＋选区外压暗）里<b>裁出选区</b>——
    /// 裁的是选区内那块，天然不含压暗，标注越出选区的部分也在这里被裁掉（批次 PU）；
    /// 还没确认选区（Enter 直提交）或没有合成图时，退回"直接从这一帧里裁"。失败都要报原因，不静默少一张图。
    /// </summary>
    private (byte[] Pixels, int Width, int Height)? FinalPixels()
    {
        if (_pinned && _base is { } basePixels)
        {
            try
            {
                return (AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, _history.Marks),
                    _contentWidth, _contentHeight);
            }
            catch (Exception ex)
            {
                StarLog.Error("[CaptureOverlay] 交图前合成标注失败", ex);
                ShowError("标注没能合成：" + ex.Message);
                return null;
            }
        }
        if (!_pinned && _annotating && _composed is { } composed && _selection is { } selection)
        {
            var (ox, oy) = CaptureGeometry.CropOffset(selection, _monitor);
            try
            {
                return BitmapTransform.Crop(composed, _contentWidth, _contentHeight,
                    ox, oy, selection.Width, selection.Height);
            }
            catch (Exception ex)
            {
                StarLog.Error("[CaptureOverlay] 从合成图裁选区失败", ex);
                ShowError("选区那块画面没能裁出来：" + ex.Message);
                return null;
            }
        }
        return _selection is { } sel && _frame is { } frame
            ? ScreenshotService.TryCrop(frame, sel)
            : null;
    }

}
