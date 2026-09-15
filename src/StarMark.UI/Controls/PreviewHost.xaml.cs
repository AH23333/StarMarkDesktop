#nullable enable
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using StarMark.Abstractions;
using StarMark.UI.ViewModels;
using Windows.Storage;
using Windows.Storage.Streams;

namespace StarMark.UI.Controls;

/// <summary>
/// QuickLook 风格内嵌预览：文本 / 图片 / PDF（Windows.Data.Pdf 首页渲染）/ 链接。
/// 由 ItemCard 自包含调用（ContentDialog），无需各页面接线。
/// </summary>
public sealed partial class PreviewHost : UserControl
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico" };

    private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cs", ".xaml", ".json", ".xml", ".yml", ".yaml", ".toml", ".ini",
        ".cfg", ".log", ".sql", ".js", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css",
        ".cpp", ".h", ".hpp", ".c", ".java", ".kt", ".py", ".rb", ".go", ".rs", ".sh",
        ".bat", ".ps1", ".sln", ".csproj", ".props", ".targets", ".gitignore", ".editorconfig",
    };

    private static readonly HashSet<string> PdfExts = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public ItemCardViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            _ = RenderAsync();
        }
    }

    private ItemCardViewModel? _viewModel;
    private bool _rendered;

    public PreviewHost()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (_viewModel != null && !_rendered) _ = RenderAsync(); };
    }

    private async System.Threading.Tasks.Task RenderAsync()
    {
        _rendered = true;
        var vm = _viewModel;
        if (vm == null)
        {
            ShowFallback("无可预览内容", string.Empty, false);
            return;
        }

        LoadingRing.IsActive = true;
        try
        {
            if (TryGetLocalFile(vm.Uri, out var path))
            {
                var ext = Path.GetExtension(path);
                if (PdfExts.Contains(ext))
                {
                    await ShowPdfAsync(path);
                    return;
                }
                if (ImageExts.Contains(ext))
                {
                    await ShowImageAsync(path);
                    return;
                }
                if (TextExts.Contains(ext))
                {
                    await ShowTextFileAsync(path);
                    return;
                }
            }

            if (vm.Uri.StartsWith("http://") || vm.Uri.StartsWith("https://"))
            {
                ShowWeb(vm);
                return;
            }

            // 通用文本（描述 / 笔记 / 剪贴板内容）
            var body = FirstNonEmpty(vm.Description, vm.Subtitle, vm.Notes, vm.Title);
            if (!string.IsNullOrEmpty(body))
                ShowText(body);
            else
                ShowFallback(vm.Title, "该条目没有可预览的内容", false);
        }
        catch (Exception ex)
        {
            ShowFallback(vm.Title ?? string.Empty, $"无法预览：{ex.Message}", false);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private static bool TryGetLocalFile(string? uri, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var local = new Uri(uri).LocalPath;
            if (Path.GetFullPath(local) != local) return false;
            if (!File.Exists(local)) return false;
            path = local;
            return true;
        }
        catch { return false; }
    }

    private async System.Threading.Tasks.Task ShowImageAsync(string path)
    {
        ImageScroll.Visibility = Visibility.Visible;
        var bmp = new BitmapImage { DecodePixelWidth = 900 };
        try
        {
            await bmp.SetSourceAsync(await FileRandomAccessStream(path));
            ImagePreview.Source = bmp;
        }
        catch
        {
            ShowFallback(Path.GetFileName(path), "图片解码失败", false);
        }
    }

    private async System.Threading.Tasks.Task ShowPdfAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            if (doc.PageCount == 0)
            {
                ShowFallback(Path.GetFileName(path), "PDF 没有可渲染的页面", false);
                return;
            }

            using var page = doc.GetPage(0);
            var pageStream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(pageStream);

            ImageScroll.Visibility = Visibility.Visible;
            var bmp = new BitmapImage();
            pageStream.Seek(0);
            await bmp.SetSourceAsync(pageStream);
            ImagePreview.Source = bmp;
        }
        catch (Exception ex)
        {
            ShowFallback(Path.GetFileName(path), $"PDF 渲染失败: {ex.Message}", false);
        }
    }

    private async System.Threading.Tasks.Task ShowTextFileAsync(string path)
    {
        var bytes = new byte[Math.Min(new FileInfo(path).Length, 200_000)];
        await using var fs = File.OpenRead(path);
        var read = await fs.ReadAsync(bytes.AsMemory(0, bytes.Length));
        var text = Encoding.UTF8.GetString(bytes, 0, read);
        ShowText(text);
    }

    private static async System.Threading.Tasks.Task<IRandomAccessStream> FileRandomAccessStream(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        return await file.OpenReadAsync();
    }

    private void ShowText(string text)
    {
        TextScroll.Visibility = Visibility.Visible;
        TextPreview.Text = string.IsNullOrWhiteSpace(text) ? "（空）" : text.Trim('\uFEFF').TrimEnd('\r', '\n');
    }

    private void ShowWeb(ItemCardViewModel vm)
    {
        FallbackPanel.Visibility = Visibility.Visible;
        FallbackTitle.Text = FirstNonEmpty(vm.Title, vm.Uri) ?? vm.Uri;
        FallbackDetail.Text = vm.Uri;
        OpenButton.Visibility = Visibility.Visible;
    }

    private void ShowFallback(string title, string detail, bool showOpen)
    {
        FallbackPanel.Visibility = Visibility.Visible;
        FallbackTitle.Text = title;
        FallbackDetail.Text = detail;
        OpenButton.Visibility = showOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var uri = _viewModel?.Uri;
        if (!string.IsNullOrWhiteSpace(uri))
        {
            try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uri)); }
            catch { }
        }
    }
}