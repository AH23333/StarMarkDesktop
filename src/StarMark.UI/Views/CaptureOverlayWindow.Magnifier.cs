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
/// Snipaste 式像素放大镜（选区边缘取数与画格）。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── Snipaste 式像素放大镜 ──────────

    private const int MagnifierLens = 15;   // 放大 15×15 源像素
    private const int MagnifierZoom = 10;   // 每源像素＝10 物理像素
    private static readonly byte[] GridLineColor = { 0x00, 0x00, 0x00, 0x66 };
    private static readonly byte[] CrossLineColor = { 0x30, 0x3B, 0xFF, 0xFF };

    private void UpdateMagnifier(PixelPoint physical)
    {
        if (_frame is null) return;
        var size = MagnifierLens * MagnifierZoom;
        if (_magnifierBitmap is null)
        {
            _magnifierBitmap = new WriteableBitmap(size, size);
            MagnifierImage.Source = _magnifierBitmap;
            // 位图按物理像素画，显示尺寸换算成 DIP，保证"每源像素＝10 物理像素"
            MagnifierImage.Width = size / _scale;
            MagnifierImage.Height = size / _scale;
        }

        var cx = physical.X - _frame.Bounds.X;
        var cy = physical.Y - _frame.Bounds.Y;
        var startX = cx - MagnifierLens / 2;
        var startY = cy - MagnifierLens / 2;
        var magnified = new byte[size * size * 4];
        for (var my = 0; my < MagnifierLens; my++)
        {
            for (var mx = 0; mx < MagnifierLens; mx++)
            {
                var sx = startX + mx;
                var sy = startY + my;
                byte b = 0, g = 0, r = 0;
                if (sx >= 0 && sy >= 0 && sx < _frame.Width && sy < _frame.Height)
                {
                    var sp = (sy * _frame.Width + sx) * 4;
                    b = _frame.Bgra[sp];
                    g = _frame.Bgra[sp + 1];
                    r = _frame.Bgra[sp + 2];
                }
                for (var by = 0; by < MagnifierZoom; by++)
                {
                    var dy = my * MagnifierZoom + by;
                    for (var bx = 0; bx < MagnifierZoom; bx++)
                    {
                        var dx = mx * MagnifierZoom + bx;
                        var dp = (dy * size + dx) * 4;
                        magnified[dp] = b;
                        magnified[dp + 1] = g;
                        magnified[dp + 2] = r;
                        magnified[dp + 3] = 255;
                    }
                }
            }
        }

        // 像素网格（每格一条暗线）＋中心红色十字
        for (var i = 0; i <= size; i += MagnifierZoom)
        {
            PaintHLine(magnified, size, i, GridLineColor);
            PaintVLine(magnified, size, i, GridLineColor);
        }
        var center = size / 2;
        PaintHLine(magnified, size, center, CrossLineColor);
        PaintVLine(magnified, size, center, CrossLineColor);

        using (var stream = _magnifierBitmap.PixelBuffer.AsStream())
            stream.Write(magnified, 0, magnified.Length);
        _magnifierBitmap.Invalidate();

        if (cx >= 0 && cy >= 0 && cx < _frame.Width && cy < _frame.Height)
        {
            var cp = (cy * _frame.Width + cx) * 4;
            MagnifierRgb.Text = $"RGB: {_frame.Bgra[cp + 2]},{_frame.Bgra[cp + 1]},{_frame.Bgra[cp]}";
        }
        MagnifierPos.Text = $"X: {physical.X} Y: {physical.Y}";

        PositionMagnifier(physical);
        Magnifier.Visibility = Visibility.Visible;
    }

    private void HideMagnifier() => Magnifier.Visibility = Visibility.Collapsed;

    private void PositionMagnifier(PixelPoint physical)
    {
        var win = WindowInterop.GetWindowRect(this);
        const int lensW = 162;
        const int lensH = 196;
        const int gap = 18;
        var px = physical.X - win.X + gap;
        var py = physical.Y - win.Y + gap;
        // 靠近右/下边缘时翻到光标的左/上方
        if (px + lensW > win.Width) px = physical.X - win.X - lensW - 6;
        if (py + lensH > win.Height) py = physical.Y - win.Y - lensH - 6;
        MagnifierTransform.X = Math.Max(0, px) / _scale;
        MagnifierTransform.Y = Math.Max(0, py) / _scale;
    }

    private static void PaintHLine(byte[] bgra, int width, int y, byte[] color)
    {
        if (y < 0 || y >= width) return;
        for (var x = 0; x < width; x++)
        {
            var p = (y * width + x) * 4;
            bgra[p] = color[0];
            bgra[p + 1] = color[1];
            bgra[p + 2] = color[2];
            bgra[p + 3] = color[3];
        }
    }

    private static void PaintVLine(byte[] bgra, int width, int x, byte[] color)
    {
        if (x < 0 || x >= width) return;
        for (var y = 0; y < width; y++)
        {
            var p = (y * width + x) * 4;
            bgra[p] = color[0];
            bgra[p + 1] = color[1];
            bgra[p + 2] = color[2];
            bgra[p + 3] = color[3];
        }
    }

}
