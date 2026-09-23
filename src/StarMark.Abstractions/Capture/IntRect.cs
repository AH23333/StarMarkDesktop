#nullable enable
using System;

namespace StarMark.Abstractions.Capture;

/// <summary>
/// 一个矩形（像素或 DIP 坐标；<b>允许原点为负</b>——副屏在主屏左边时虚拟桌面坐标就是负数）。
/// <para>
/// <b>不变量：宽高非负</b>（左上角 + 尺寸的形式），由 <c>CaptureGeometry.Normalize</c> 从
/// 「鼠标拖过的两个点」产生。把负宽高的矩形直接送进求交会得到反向边界（Right 比 X 还小），
/// 那种输入下得出的结论是错的，不是"容错"。
/// </para>
/// <para>放在 <c>Abstractions</c> 是因为它同时被 <c>Core</c>（几何判据）与 <c>Integrations</c>（抓屏结果）使用，
/// 而分层规定 <c>Integrations</c> 不许引用 <c>Core</c>。</para>
/// </summary>
public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// 一个像素坐标。<b>与 <see cref="IntRect"/> 同处一层</b>的理由一样：标注的判据在 <c>Core</c>，
/// 而抓屏与 GDI 绘制在 <c>Integrations</c>，两边要拿同一个点说话（用 <c>Windows.Graphics.PointInt32</c>
/// 就把 WinRT 类型漏进了纯判据层，单测与换算都会跟着变复杂）。
/// <para>允许为负：选区内的标注允许被拖到画面外面去， clipping 是绘制那一步的事，不是模型的事。</para>
/// </summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>像素缓冲层的通用处理（GDI 与编码器的格式差异常在这里出问题）。</summary>
public static class PixelBuffers
{
    /// <summary>
    /// 把 BGRA 缓冲里每像素的 alpha 从"未定义"（GDI 抓屏填的通常是 0）改成不透明。
    /// <b>不做这一步，存出来的 PNG 会是一张全透明图</b>：GDI 不写 alpha，而 PNG 带 alpha 通道。
    /// </summary>
    public static void MakeOpaque(Span<byte> bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
    }
}
