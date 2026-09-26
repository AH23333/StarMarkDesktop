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
/// 窗口自动检测（按住 Ctrl 临时关掉）：候选边框的取与画。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 窗口自动检测（按住 Ctrl 临时关闭） ──────────

    private void UpdateDetected(PixelPoint physical)
    {
        foreach (var cand in _windowCandidates)
        {
            if (ContainsPoint(cand, physical))
            {
                if (_detected is { } d && d == cand) return;
                _detected = cand;
                DrawSelection(cand);
                return;
            }
        }
        ClearDetected();
    }

    private void ClearDetected()
    {
        if (_detected is null) return;
        _detected = null;
        ClearSelection();
    }

    private static bool ContainsPoint(IntRect rect, PixelPoint p)
        => p.X >= rect.X && p.X < rect.Right && p.Y >= rect.Y && p.Y < rect.Bottom;

    private List<IntRect> CollectWindowCandidates()
    {
        // 窗口检测是<b>尽力而为</b>：它只服务于"点一下选窗口"这条捷径，任何失败（枚举失败、
        // DWM 不可用、原生调用抛异常）都只能降级成"没有候选、退回手动拖框"，绝不能让截图
        // 会话在构造函数里夭折——SP 那次一个写错的 P/Invoke 入口名让每次 F1 都直接"截图失败"，
        // 就是缺这层兜底。
        try
        {
            var list = new List<IntRect>();
            var currentProcess = Environment.ProcessId;
            WindowInterop.EnumWindows((hwnd, _) =>
            {
                if (!WindowInterop.IsWindowVisible(hwnd)) return true;
                WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == currentProcess) return true;     // 不把我们自己的窗口当候选
                var r = WindowInterop.GetExtendedFrameBounds(hwnd);
                var rect = new IntRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (rect.Width < 24 || rect.Height < 24) return true;
                if (CaptureGeometry.Intersect(rect, _monitor) is { } clipped && !clipped.IsEmpty)
                    list.Add(rect);
                return true;
            }, IntPtr.Zero);
            // 小窗排前：小窗叠在大窗上时，点小窗不该被后面的大窗抢先
            list.Sort((a, b) => (a.Width * a.Height).CompareTo(b.Width * b.Height));
            return list;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[CaptureOverlay] 窗口候选收集失败，退回手动拖框：{ex.Message}");
            return new List<IntRect>();
        }
    }

}
