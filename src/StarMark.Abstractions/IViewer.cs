#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions;

/// <summary>
/// 预览器接口。借鉴 QuickLook <c>IViewer</c> 设计（CanHandle/Init/View/Cleanup）。
/// 此处仅定义契约；具体 UI 元素类型由 UI 层的 IPreviewHost 接收 <see cref="PreviewResult"/> 渲染。
/// 对应技术文档 §3.2。
/// </summary>
public interface IViewer
{
    /// <summary>是否能处理该扩展名 / MIME 类型。</summary>
    bool CanHandle(string extension, string? mimeType);

    /// <summary>初始化预览资源。文件读取、图像解码等耗时操作放此处。</summary>
    Task InitializeAsync(string filePath, CancellationToken ct);

    /// <summary>
    /// 生成预览内容。返回的 <see cref="PreviewResult"/> 由 UI 层负责具体渲染：
    /// ImageBytes → Image 控件；TextContent → TextBlock；HtmlContent → WebView。
    /// </summary>
    Task<PreviewResult> ViewAsync(double availableWidth, double availableHeight, CancellationToken ct);

    /// <summary>清理资源（关闭文件句柄、释放图像）。</summary>
    void Cleanup();
}

/// <summary>预览结果。UI 层据 Kind 选择渲染控件。</summary>
public sealed class PreviewResult
{
    public enum PreviewKind { Image, Text, Pdf, Html, Unsupported }

    public PreviewKind Kind { get; init; } = PreviewKind.Unsupported;

    /// <summary>图像字节（PNG/JPEG），仅 Image 类型。</summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>文本内容（UTF-8），仅 Text 类型。</summary>
    public string? TextContent { get; init; }

    /// <summary>HTML 内容，仅 Html 类型。</summary>
    public string? HtmlContent { get; init; }

    /// <summary>文件路径（用于 WebView 直接加载），仅 Pdf/Html 类型。</summary>
    public string? FilePath { get; init; }

    public string? ErrorMessage { get; init; }

    public bool Success => Kind != PreviewKind.Unsupported && string.IsNullOrEmpty(ErrorMessage);
}
