#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using StarMark.Abstractions.Capture;

namespace StarMark.Integrations.Capture;

/// <summary>一帧屏幕画面：虚拟桌面坐标下的原点与尺寸 + BGRA 像素（行主序，从左上开始）。</summary>
public sealed record ScreenFrame(IntRect Bounds, byte[] Bgra)
{
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;
}

/// <summary>带成败与原因的返回值（"什么都没截到"与"截到一张全黑的画面"必须不是一回事）。</summary>
public sealed record CaptureResult(bool Ok, ScreenFrame? Frame, string? Error)
{
    public static CaptureResult Fail(string reason) => new(false, null, reason);
    public static CaptureResult Success(ScreenFrame frame) => new(true, frame, null);
}

/// <summary>
/// GDI 抓屏：<c>GetDC(NULL)</c> + <c>BitBlt</c> 一次拿下整个虚拟桌面，零新增依赖。
/// <para>
/// <b>刻意不做的事</b>：① 不支持独占全屏的应用（D3X/部分游戏的flip-model 交换链，BitBlt 只会拿到黑帧——
/// 这条限制要在界面上说清楚，而不是静默给用户一张黑图）；② 不用 Windows.Graphics.Capture
/// （那条路要求每个显示器一个会话、还要系统权限横幅，L1 用不上）。
/// </para>
/// <para>
/// 进程必须是 Per-Monitor-V2 DPI 感知，否则系统会把我们看到的坐标虚拟化、抓回来的是缩放过的模糊画面；
/// 该设置在应用清单里，本类只负责照物理像素搬。
/// </para>
/// </summary>
public static class GdiScreenCapture
{
    /// <summary>像素缓冲上限（字节）。两屏 4K 约 66 MB；再大就说明坐标被算坏了，宁可不截。</summary>
    public const long MaxPixelBufferBytes = 512L * 1024 * 1024;

    /// <summary>抓整块虚拟桌面。<paramref name="ct"/> 只在等待编码时被尊重——BitBlt 本身不可取消。</summary>
    public static CaptureResult CaptureVirtualScreen()
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
            return CaptureResult.Fail("系统报告的桌面尺寸为 0，无法截取");

        var bytes = (long)width * height * 4;
        if (bytes > MaxPixelBufferBytes)
            return CaptureResult.Fail(
                $"桌面尺寸 {width} × {height} 超出可截取上限，已放弃（避免把内存吃满）");

        var screenDc = IntPtr.Zero;
        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldObject = IntPtr.Zero;
        var info = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return CaptureResult.Fail("取不到屏幕设备上下文（桌面可能正在切换）");
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) return CaptureResult.Fail("创建内存设备上下文失败");
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == IntPtr.Zero) return CaptureResult.Fail("创建位图失败（显存或桌面堆不足）");
            oldObject = SelectObject(memoryDc, bitmap);

            // SRCCOPY；CAPTUREBLT 一并带上——没有它，"始终置顶"的那类窗口（含我们自己的组件）
            // 会从截出来的画面里消失，用户会以为截图漏掉了那一块。
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SRCCOPY | CAPTUREBLT))
                return CaptureResult.Fail($"拷贝屏幕失败（Win32 {Marshal.GetLastWin32Error()}）");

            // BITMAPINFOHEADER 之后那块联合体区是系统可写的（BI_RGB 下不写，但会校验长度），
            // 所以按"头 + 余量"分配非托管缓冲，而不是直接把托管结构体 ref 过去——
            // 与批次 MK 那条同一课：原生要写的缓冲区，尺寸必须由我们自己留够。
            var headerSize = Marshal.SizeOf<BitmapInfoHeader>();
            info = Marshal.AllocHGlobal(headerSize + 1024);
            // AllocHGlobal 给的是未初始化内存，而系统会校验整段头部：先清零再逐字段写
            for (var i = 0; i < headerSize; i++) Marshal.WriteByte(info, i, 0);
            // 逐个字段按真实偏移写：DWORD 4 字节、WORD 2 字节，串起来是
            // biSize 0 / biWidth 4 / biHeight 8 / biPlanes 12 / biBitCount 14 / biCompression 16
            Marshal.WriteInt32(info, OffBiSize, headerSize);
            Marshal.WriteInt32(info, OffBiWidth, width);
            Marshal.WriteInt32(info, OffBiHeight, -height);            // 负数＝自上而下的行序
            Marshal.WriteInt16(info, OffBiPlanes, 1);
            Marshal.WriteInt16(info, OffBiBitCount, 32);
            // biCompression 保持 0（BI_RGB）：其余字段已在上面清零

            var pixels = new byte[width * height * 4];
            // 取像素前先把位图从 DC 上摘下来：位图仍被选进 DC 时 GetDIBits 在部分驱动/系统上直接返回 0
            // （返回 0 又不带错误码，光看日志查不出来），摘下来再读是各家截屏实现的共同做法。
            SelectObject(memoryDc, oldObject);
            oldObject = IntPtr.Zero;
            var lines = GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, info, DIB_RGB_COLORS);
            if (lines == 0)
                return CaptureResult.Fail($"读出像素失败（GetDIBits 返回 0 行，Win32 {Marshal.GetLastWin32Error()}）");

            PixelBuffers.MakeOpaque(pixels);
            return CaptureResult.Success(new ScreenFrame(new IntRect(x, y, width, height), pixels));
        }
        catch (Exception ex)
        {
            return CaptureResult.Fail("截屏异常：" + ex.Message);
        }
        finally
        {
            if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
            if (oldObject != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, oldObject);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// 把一帧（或裁剪后的一块）编成 PNG 落盘。像素按 BGRA 直送 <see cref="BitmapEncoder"/>，
    /// 不经任何中间格式（零 NuGet：Windows.Graphics.Imaging 是系统组件）。
    /// </summary>
    public static async Task<bool> SavePngAsync(string path, byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        try
        {
            if (await EncodePngAsync(bgra, width, height, ct) is not { } encoded) return false;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var file = File.Create(path);
            encoded.Seek(0);
            await encoded.AsStreamForRead().CopyToAsync(file, CancellationToken.None);
            encoded.Dispose();
            file.Flush();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { return false; }        // 调用方负责把"存不下"显示出来
    }

    /// <summary>
    /// 把 BGRA 编成 PNG 到一个内存流里（已 Seek(0)，可直接交文件系统或剪贴板）。
    /// <b>编码只这一处</b>：落盘与"复制到剪贴板"两边各写一遍编码器，早晚会写出两种尺寸或两种 alpha 处理。
    /// </summary>
    public static async Task<InMemoryRandomAccessStream?> EncodePngAsync(byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        if (width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4) return null;
        var stream = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(ct);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                (uint)width, (uint)height, 96, 96, bgra);
            await encoder.FlushAsync().AsTask(ct);
            stream.Seek(0);
            return stream;
        }
        catch (Exception)
        {
            stream.Dispose();     // 出错路径上也要释放：调用方拿到 null 就不会再管这个流了
            throw;
        }
    }

    /// <summary>裁剪一块像素：目标缓冲独立分配（贴图要能脱离原帧被单独持有与释放）。</summary>
    public static byte[] Crop(FrameCopyRequest request)
    {
        var source = request.Source;
        var pixels = new byte[request.Width * request.Height * 4];
        var sourceStride = source.Width * 4;
        var targetStride = request.Width * 4;
        // request.X / request.Y 是"相对这帧左上角"的偏移（CropOffset 已经减掉虚拟桌面的负原点）
        for (var row = 0; row < request.Height; row++)
        {
            var from = (request.Y + row) * sourceStride + request.X * 4;
            System.Buffer.BlockCopy(source.Bgra, from, pixels, row * targetStride, targetStride);
        }
        return pixels;
    }

    // BITMAPINFOHEADER 各字段相对结构体起点的偏移。
    // 这里曾经错过一次：把 biPlanes 记成偏移 10，于是一次 4 字节写入盖掉了 biHeight 的高半，
    // 结果是"负高度（自上而下）"被读成正的 129872 行，GetDIBits 返回 0 行且**不带错误码**——
    // 只有真机跑一次才看得见（错误码为 0 的失败在日志里和成功一样安静）。
    private const int OffBiSize = 0;
    private const int OffBiWidth = 4;
    private const int OffBiHeight = 8;
    private const int OffBiPlanes = 12;
    private const int OffBiBitCount = 14;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int BiSize;
        public int BiWidth;
        public int BiHeight;
        public short BiPlanes;
        public short BiBitCount;
        public int BiCompression;
        public int BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public int BiClrUsed;
        public int BiClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc,
        int x1, int y1, int rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint uStartScan, uint cScanLines,
        byte[] lpvBits, IntPtr lpbi, uint uUsage);
}

/// <summary>一次裁剪请求（源帧 + 源内偏移 + 尺寸）。用记录是为了让调用点先算好边界再传（越界由 <c>CaptureGeometry.CropProblem</c> 拦）。</summary>
public sealed record FrameCopyRequest(ScreenFrame Source, int X, int Y, int Width, int Height);
