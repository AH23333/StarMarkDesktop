#nullable enable
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using StarMark.Abstractions;
using StarMark.Abstractions.Text;
using StarMark.Integrations.Clipboard;
using StarMark.UI.ViewModels;
using Windows.Storage;
using Windows.Storage.Streams;

namespace StarMark.UI.Controls;

/// <summary>
/// QuickLook 风格内嵌预览：文本 / 图片 / PDF（Windows.Data.Pdf 首页渲染）/ 压缩包清单 /
/// 音频视频（MediaPlayerElement 内嵌播放）/ 链接。
/// 由 ItemCard 自包含调用（ContentDialog），无需各页面接线。
/// 不支持的格式不硬猜解码器——给出「用默认程序打开」的出口，交回 Windows 关联程序。
/// </summary>
public sealed partial class PreviewHost : UserControl
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico" };

    private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".rst", ".adoc", ".cs", ".xaml", ".json", ".xml", ".yml", ".yaml",
        ".toml", ".ini", ".cfg", ".conf", ".properties", ".log", ".sql", ".csv", ".tsv", ".js", ".mjs",
        ".ts", ".tsx", ".jsx", ".html", ".htm", ".css", ".scss", ".less",
        ".cpp", ".cc", ".h", ".hpp", ".c", ".java", ".kt", ".kts", ".py", ".rb", ".go", ".rs", ".sh",
        ".bat", ".cmd", ".ps1", ".psm1", ".pl", ".php", ".swift", ".lua", ".dart", ".scala", ".r",
        ".vue", ".svelte", ".graphql", ".proto", ".tex", ".cmake", ".gradle", ".vim", ".editorconfig",
        ".gitignore", ".gitattributes", ".sln", ".csproj", ".fsproj", ".vbproj", ".vcxproj",
        ".props", ".targets", ".nuspec", ".config",
    };

    private static readonly HashSet<string> PdfExts = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    /// <summary>zip：BCL 列条目即有用预览。docx/xlsx/pptx 虽同为 zip 容器，列内部 XML 对办公用户是噪音，故不列。</summary>
    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase) { ".zip" };

    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".wma", ".ogg", ".opus" };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".mpg", ".mpeg", ".3gp" };

    private Windows.Media.Playback.MediaPlayer? _player;

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
        // 对话框关闭即卸载：不停掉播放器会变成「预览窗口没了、声音还在放」。
        Unloaded += (_, _) => ReleasePlayer();
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

        ReleasePlayer();
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
                if (ArchiveExts.Contains(ext))
                {
                    ShowArchive(path);
                    return;
                }
                if (AudioExts.Contains(ext) || VideoExts.Contains(ext))
                {
                    await ShowMediaAsync(path, AudioExts.Contains(ext) ? "音频" : "视频");
                    return;
                }
                // 本地文件但没打算支持解码的格式（rar/7z、office 旧格式、exe、字体…）：
                // 不猜、不把路径当正文糊上去，直接说明并交回系统关联程序。
                ShowFallback(Path.GetFileName(path),
                    $"暂不支持该格式文件预览{(string.IsNullOrEmpty(ext) ? "（无扩展名）" : $"（{ext}）")}",
                    true, "用默认程序打开");
                return;
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

    /// <summary>
    /// 预览只接"磁盘上真的存在、且已归一的绝对路径"那一个候选。
    /// 还原规则本身（两类互补的 <c>file://</c> 生产者）归 <c>LocalFileIdentity.TryExistingPath</c> 一颗，
    /// 这里只交本宿主的判定语义：<b>文件</b>（目录不预览）＋ <see cref="Path.GetFullPath"/> 相等（拒绝 <c>..</c>／相对遍历）。
    /// </summary>
    private static bool TryGetLocalFile(string? uri, out string path)
        => StarMark.Abstractions.LocalFileIdentity.TryExistingPath(uri, IsPreviewableFile, out path);

    private static bool IsPreviewableFile(string p)
        => !string.IsNullOrEmpty(p) && File.Exists(p) && Path.GetFullPath(p) == p;

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
            ShowFallback(Path.GetFileName(path), "图片解码失败", true, "用默认程序打开");
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
                ShowFallback(Path.GetFileName(path), "PDF 没有可渲染的页面", true, "用默认程序打开");
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
            ShowFallback(Path.GetFileName(path), $"PDF 渲染失败: {ex.Message}", true, "用默认程序打开");
        }
    }

    /// <summary>
    /// 音频 / 视频内嵌播放（尽力而为）：解码失败不静默——回落到「用默认程序打开」。
    /// 走 StorageFile 而非 <c>new Uri(path)</c>：文件名里的 <c>#</c> 会被 Uri 当片段截断。
    /// </summary>
    private async System.Threading.Tasks.Task ShowMediaAsync(string path, string kind)
    {
        var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var file = await StorageFile.GetFileFromPathAsync(path);
        var player = new Windows.Media.Playback.MediaPlayer
        {
            Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file),
            AutoPlay = false,
        };
        player.MediaFailed += (_, args) => ui?.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_player, player)) return;   // 已被新一次渲染取代
            ReleasePlayer();
            ShowFallback(Path.GetFileName(path),
                $"{kind}无法在此播放（{args.ErrorMessage}）", true, "用默认程序打开");
        });
        _player = player;
        MediaPreview.SetMediaPlayer(player);
        MediaPreview.Visibility = Visibility.Visible;
    }

    /// <summary>停掉并释放播放器：预览对话框关掉后不该还在放音、握着文件句柄。</summary>
    private void ReleasePlayer()
    {
        var p = _player;
        _player = null;
        if (p is null) return;
        p.Pause();
        p.Source = null;
        p.Dispose();
        MediaPreview.Visibility = Visibility.Collapsed;
    }

    /// <summary>zip：BCL 直接列条目，让用户不进资源管理器就知道包里有什么。</summary>
    private void ShowArchive(string path)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var sb = new StringBuilder();
            sb.AppendLine($"{Path.GetFileName(path)}  ·  {StarMark.Abstractions.FileSizeText.Human(new FileInfo(path).Length)}"
                + $"  ·  {zip.Entries.Count} 项");
            const int cap = 80;
            foreach (var entry in zip.Entries.Take(cap))
                sb.AppendLine($"  {entry.FullName}  {StarMark.Abstractions.FileSizeText.Human(entry.Length)}");
            if (zip.Entries.Count > cap)
                sb.AppendLine($"  … 其余 {zip.Entries.Count - cap} 项未列出");
            ShowText(sb.ToString());
        }
        catch
        {
            ShowFallback(Path.GetFileName(path), "压缩包无法读取（可能已加密或损坏）", true, "用默认程序打开");
        }
    }

    private async System.Threading.Tasks.Task ShowTextFileAsync(string path)
    {
        var bytes = new byte[Math.Min(new FileInfo(path).Length, 200_000)];
        await using var fs = File.OpenRead(path);
        var read = await fs.ReadAsync(bytes.AsMemory(0, bytes.Length));
        // P-132：磁盘上的文本文件不归我们定编码——BOM / 声明 / UTF-8 探测都不通才跟随本机 ANSI 码页。
        var text = ExternalText.Decode(bytes, read, null, AnsiText.Decode, AnsiText.SystemAnsiCodePage).Text;
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
        OpenButton.Content = "在浏览器中打开";
    }

    /// <param name="openText">主按钮文案；省略时按 uri 是网页还是本地文件自动选「在浏览器中打开」/「用默认程序打开」。</param>
    private void ShowFallback(string title, string detail, bool showOpen, string? openText = null)
    {
        FallbackPanel.Visibility = Visibility.Visible;
        FallbackTitle.Text = title;
        FallbackDetail.Text = detail;
        OpenButton.Visibility = showOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!showOpen) return;
        var isWeb = _viewModel?.Uri is { } u
            && (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        OpenButton.Content = openText ?? (isWeb ? "在浏览器中打开" : "用默认程序打开");
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var uri = _viewModel?.Uri;
        if (!string.IsNullOrWhiteSpace(uri))
        {
            // 经统一入口打开：套用协议白名单闸门，并让 file:// 走 LaunchFile/Folder（直调 LaunchUriAsync 对 file:// 静默失效）。
            _ = StarMark.UI.Helpers.LauncherEx.OpenAsync(uri);
        }
    }
}