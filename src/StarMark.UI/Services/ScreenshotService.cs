#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace StarMark.UI.Services;

/// <summary>一次框选放开之后的落点（见 <see cref="ScreenshotService.Start"/>）。</summary>
public enum CaptureMode
{
    /// <summary>给动作条，让用户挑复制 / 存图 / 贴图 / 识字。</summary>
    Toolbar,
    /// <summary>直接钉到桌面（F3）。</summary>
    Pin,
    /// <summary>直接识别并把文字复制走。</summary>
    Ocr,
}

/// <summary>
/// 截图会话编排：抓一帧 → 每屏铺一个遮罩窗 → 拿回这一屏的选区 → 裁剪 → 复制或存盘。
/// <para>
/// 会话是<b>串行且唯一</b>的：正在截图时再按一次热键不会叠第二层遮罩（两层暗幕叠加后
/// 用户看不出哪一层在收鼠标，只能强杀进程）。这条闸门必须写在服务里而不是靠"用户不会连按"。
/// </para>
/// <para>
/// 每个终结动作都回报结果（<see cref="Report"/>）：复制成了没说、存盘成了没说，
/// 用户就只能靠"打开画图粘一下"来确认程序到底做了什么。
/// </para>
/// </summary>
public static class ScreenshotService
{
    private static readonly List<CaptureOverlayWindow> Session = new();
    private static bool _busy;

    /// <summary>当前是否有一次截图会话在进行（供托盘/菜单决定要不要灰掉这一项）。</summary>
    public static bool IsCapturing => _busy;

    /// <summary>
    /// 按热键/托盘进入选区遮罩。抓不到画面时给原因，不铺一层空白暗幕。
    /// </summary>
    /// <param name="mode">
    /// 放开选区之后要做什么：<see cref="CaptureMode.Toolbar"/> 给动作条（复制/存图/贴图/识字）；
    /// <see cref="CaptureMode.Pin"/> 直接钉到桌面；<see cref="CaptureMode.Ocr"/> 直接把文字复制走。
    /// 三者共用同一套遮罩与几何，只差最后那一步——多一条链就要多写一遍"每屏一窗、退出收干净"，不值。
    /// </param>
    public static void Start(CaptureMode mode = CaptureMode.Toolbar)
    {
        // 遮罩窗必须在 UI 线程上建：热键回调本来就在，但托盘/菜单那类入口的回调线程不保证。
        // 在别的线程上 new Window 会直接崩，所以这里显式回主线程，而不是"指望调用方在对的线程"。
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is { HasThreadAccess: false })
        {
            queue.TryEnqueue(() => Start(mode));
            return;
        }
        if (_busy)
        {
            StarLog.Info("[Screenshot] 已有截图会话在进行，忽略这一次触发");
            return;
        }
        var captured = GdiScreenCapture.CaptureVirtualScreen();
        if (!captured.Ok || captured.Frame is not { } frame)
        {
            Report("截图失败", captured.Error ?? "系统没有返回画面");
            return;
        }
        var monitors = WindowInterop.ListMonitors();
        if (monitors.Count == 0)
        {
            Report("截图失败", "系统没有报告任何显示器");
            return;
        }

        _busy = true;
        try
        {
            foreach (var monitor in monitors)
            {
                var window = new CaptureOverlayWindow(frame, monitor, OnFinished, mode);
                Session.Add(window);
                // 热键回调本来就在 UI 线程，这里不必再排一帧：晚一帧Activate会让用户看到"按了没反应"
                window.Activate();
            }
        }
        catch (Exception ex)
        {
            CloseSession();
            StarLog.Error("[Screenshot] 建立遮罩失败", ex);
            Report("截图失败", ex.Message);
        }
    }

    /// <summary>
    /// 某个遮罩窗交回了结果或取消：整场会话到此收摊。
    /// 实际的复制/存盘由那一扇窗在 Settle 之后直接调用本服务的公开入口（它手里有那一帧），
    /// 这里只做关窗——顺序反了会让用户在裁图期间还被困在暗幕里。
    /// </summary>
    private static void OnFinished(CaptureOverlayWindow sender, IntRect? selection)
    {
        if (selection is { } box)
            StarLog.Info($"[Screenshot] 选区来自 {sender.DeviceName}：{CaptureGeometry.FormatSize(box.Width, box.Height)}");
        CloseSession();
    }

    // ────────── 终结动作 ──────────

    /// <summary>
    /// 把选区写进剪贴板（图片）。真正的编码与发送在 <see cref="CopyPixelsAsync"/>（贴图复制走同一份）。
    /// </summary>
    public static System.Threading.Tasks.Task CopySelectionAsync(ScreenFrame frame, IntRect selection)
        => CropSilently(frame, selection) is { } crop
            ? CopyPixelsAsync(crop.Pixels, crop.Width, crop.Height)
            : System.Threading.Tasks.Task.CompletedTask;

    /// <summary>把选区存成 PNG，存到 图片\StarMark 截图（首次自动建目录）。</summary>
    public static System.Threading.Tasks.Task SaveSelectionAsync(ScreenFrame frame, IntRect selection)
        => CropSilently(frame, selection) is { } crop
            ? SavePixelsAsync(crop.Pixels, crop.Width, crop.Height)
            : System.Threading.Tasks.Task.CompletedTask;

    /// <summary>
    /// 把选区那块画面钉到桌面上（F3 那条路径的落点）。
    /// 上限判定与回报都在 <see cref="PinManager"/>，这里只负责"从这一帧里把像素取出来"。
    /// </summary>
    public static void PinSelection(ScreenFrame frame, IntRect selection)
    {
        if (CropSilently(frame, selection) is not { } crop) return;
        PinManager.Add(crop.Pixels, crop.Width, crop.Height, selection);
    }

    /// <summary>把一份 BGRA 画面交给剪贴板。截图与贴图共用这一份实现（两份"从像素到剪贴板"迟早分岔）。</summary>
    public static async System.Threading.Tasks.Task CopyPixelsAsync(
        byte[] pixels, int width, int height, string category = "截图")
    {
        try
        {
            // 剪贴板要的是流引用：把同一份 PNG 编码器（落盘用的那个）产出的内存流交出去，
            // 不另写一份"从像素到剪贴板"的旁路实现
            if (await GdiScreenCapture.EncodePngAsync(pixels, width, height) is not { } stream)
            {
                ReportFor(category, "复制失败", "把画面编成图片时失败");
                return;
            }
            using (stream)
            {
                var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(package);
                // Flush：SetContent 默认延迟渲染，窗一关、应用被挂起时内容会丢（点了"复制"却没东西）
                Clipboard.Flush();
            }
            ReportFor(category, "已复制", CaptureGeometry.FormatSize(width, height) + " 的画面已进剪贴板");
        }
        catch (Exception ex)
        {
            StarLog.Error($"[{category}] 复制到剪贴板失败", ex);
            ReportFor(category, "复制失败", ex.Message);
        }
    }

    /// <summary>把一份 BGRA 画面存成 PNG（同名自动递增，不覆盖上一张）。</summary>
    public static async System.Threading.Tasks.Task SavePixelsAsync(
        byte[] pixels, int width, int height, string category = "截图")
    {
        string? path = null;
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "StarMark 截图");
            System.IO.Directory.CreateDirectory(directory);
            // 同一秒连按两次也要拿到两个文件：撞名就 (1)(2) 递增，不覆盖上一张
            var index = 0;
            while (index < 100)
            {
                path = System.IO.Path.Combine(directory, CaptureGeometry.BuildFileName(DateTimeOffset.Now, "png", index));
                if (!System.IO.File.Exists(path)) break;
                index++;
            }
            var saved = await GdiScreenCapture.SavePngAsync(path!, pixels, width, height);
            if (saved) ReportFor(category, "已存图", $"{width} × {height} → {path}");
            else ReportFor(category, "存图失败", "写入图片文件时出错（检查磁盘空间与目录权限）");
        }
        catch (Exception ex)
        {
            StarLog.Error($"[{category}] 存图失败（{path}）", ex);
            ReportFor(category, "存图失败", ex.Message);
        }
    }

    /// <summary>
    /// 裁出选区的像素。<b>裁不动时这里就已经回报过了</b>并返回 null——调用方是"点一下按钮就没了"的
    /// 事件处理器，异常抛出去等于静默失败（那些调用点没有 await，也没有 catch）。
    /// </summary>
    private static (byte[] Pixels, int Width, int Height)? CropSilently(ScreenFrame frame, IntRect selection)
    {
        try
        {
            if (CaptureGeometry.CropProblem(selection, frame.Bounds) is { } problem)
            {
                Report("截图失败", problem);
                return null;
            }
            var (ox, oy) = CaptureGeometry.CropOffset(selection, frame.Bounds);
            return (GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, selection.Width, selection.Height)),
                selection.Width, selection.Height);
        }
        catch (Exception ex)
        {
            StarLog.Error("[Screenshot] 取这一块的像素失败", ex);
            Report("截图失败", ex.Message);
            return null;
        }
    }

    // ────────── 收摊 ──────────

    private static void CloseSession()
    {
        _busy = false;
        if (Session.Count == 0) return;
        var windows = Session.ToList();
        Session.Clear();
        foreach (var window in windows)
        {
            try { window.CloseWindow(); }
            catch (Exception ex) { StarLog.Error($"[Screenshot] 关闭遮罩失败（{window.DeviceName}）", ex); }
        }
    }

    /// <summary>
    /// 结果回报：优先托盘气泡；托盘没启用时退到日志。
    /// 与番茄钟同一口径 —— 通道要报告它自己有没有真的把消息送出去，
    /// 否则"提示了"与"什么都没发生"在事后无从分辨。实现收在 <see cref="TrayReporter"/>（贴图共用）。
    /// </summary>
    private static void Report(string title, string body) => ReportFor("截图", title, body);

    private static void ReportFor(string category, string title, string body) => TrayReporter.Report(category, title, body);
}
