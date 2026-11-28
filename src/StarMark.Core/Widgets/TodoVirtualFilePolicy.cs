#nullable enable
using System;

namespace StarMark.Core.Widgets;

/// <summary>
/// 虚拟文件物化的<b>纯判据</b>（P-307）：暂存预算、魔数→扩展名、MOTW 正文——三件全是纯函数，
/// 不碰磁盘、不查盘、不读设置；磁盘动作（流复制、ADS 写）归 UI 侧的
/// <c>TodoVirtualFileMaterializer</c> 与 <c>TodoAttachmentStorage</c>。
/// <para>
/// 背景（片289 轮12 定谳）：浏览器拖图给 WinUI 的 StorageFile 往往 <c>path</c> 为空——只有流
/// 没有盘上地址（GetStorageItemsAsync 收得到、item.Path 得空串）。物化腿＝把流落到 %TEMP%
/// 暂存拿到真路径，再回 AddDroppedFilesAsync 既有链；这里钉的是那条腿上的三颗判据，
/// 参数照 v6 参数表（学思路不搬码・行为规格参照，✅P-301 红线口径）。
/// </para>
/// </summary>
public static class TodoVirtualFilePolicy
{
    /// <summary>安全保留：盘可用充裕时给宿主留的余量——单次拖放饿不死宿主盘。</summary>
    public const long SafetyReserveBytes = 512L * 1024 * 1024;

    /// <summary>临界保留：可用已 ≤512MB 时改留的底线——盘越满让得越少，但再省也不贴着零写盘。</summary>
    public const long CriticalReserveBytes = 64L * 1024 * 1024;

    /// <summary>盘查询失败时的兜底预算：2GB——查不到 ≠ 不给拖，但也不放任无界。</summary>
    public const long FallbackBudgetBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>MOTW 的互联网区（Zone.Identifier 的 ZoneId 值）。</summary>
    public const int InternetZone = 3;

    /// <summary>虚拟拖放没有可取回的原始网址时的通用标记（MOTW 的 HostUrl 一行不能空着——空标等于没标）。</summary>
    public const string VirtualDropSource = "starmark://drag-drop/virtual";

    /// <summary>头 64 字节足够覆盖下面全部签名的判读。</summary>
    public const int HeaderLength = 64;

    /// <summary>
    /// 一次拖放的暂存预算（字节）。可用充裕（&gt; <see cref="SafetyReserveBytes"/>）＝可用 − 安全保留；
    /// 可用吃紧（≤ 安全保留且 &gt; <see cref="CriticalReserveBytes"/>）＝可用 − 临界底线；
    /// 可用见底（≤ <see cref="CriticalReserveBytes"/>）＝0——<b>拒收</b>，宁可这次拖不上也不写穿宿主盘；
    /// 查不到（<paramref name="availableFreeBytes"/> 为 null，盘查询失败/UNC）＝<see cref="FallbackBudgetBytes"/> 兜底。
    /// </summary>
    public static long ResolveStagingBudgetBytes(long? availableFreeBytes)
        => availableFreeBytes is not { } free ? FallbackBudgetBytes
         : free <= CriticalReserveBytes ? 0
         : free <= SafetyReserveBytes ? free - CriticalReserveBytes
         : free - SafetyReserveBytes;

    // 前缀签名表：字节串→扩展名，先到先得。RIFF/ftyp 两族签名带结构偏移，走下面的结构判读。
    private static readonly (byte[] Prefix, string Extension)[] s_prefixRules =
    {
        (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png"),              // PNG：8 字节签名
        (new byte[] { 0xFF, 0xD8, 0xFF }, ".jpg"),                                              // JPEG：SOI
        (new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, ".gif"),                            // GIF87a
        (new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif"),                            // GIF89a
        (new byte[] { 0x42, 0x4D }, ".bmp"),                                                    // BMP：「BM」
        (new byte[] { 0x00, 0x00, 0x01, 0x00 }, ".ico"),                                        // ICO：图标组
        (new byte[] { 0x00, 0x00, 0x02, 0x00 }, ".ico"),                                        // ICO：光标组
        (new byte[] { 0x49, 0x49, 0x2A, 0x00 }, ".tif"),                                        // TIFF：小端
        (new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, ".tif"),                                        // TIFF：大端
        (new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-' }, ".pdf"),         // %PDF-
    };

    /// <summary>
    /// 按魔数推断扩展名（带点，如「.png」）。浏览器拖图常只给 stem，内容却是完整可识别的文件——
    /// <b>只在名字缺扩展名时补</b>，已知扩展名不推翻；认不出的内容返回 null＝保留原名不瞎猜，
    /// 补名失败也不许让已物化的载荷不可用（调用方兜底）。
    /// <para>
    /// 十二组签名：PNG／JPEG／GIF（87a・89a 两版）／BMP／ICO（图标・光标两组）／
    /// TIFF（小端・大端两种字节序）／WebP（RIFF…WEBP）／AVIF（avif・avis 两个品牌）／
    /// HEIC 族（heic・heix・hevc・hevx・mif1・msf1 六个品牌）／PDF——签名本身全是公开文件格式规范。
    /// </para>
    /// </summary>
    public static string? TryInferExtension(ReadOnlySpan<byte> header)
    {
        foreach (var (prefix, extension) in s_prefixRules)
        {
            if (header.StartsWith(prefix)) return extension;
        }
        if (HasAsciiAt(header, 0, "RIFF") && HasAsciiAt(header, 8, "WEBP")) return ".webp";
        if (HasAsciiAt(header, 4, "ftyp"))
        {
            if (HasAsciiAt(header, 8, "avif") || HasAsciiAt(header, 8, "avis")) return ".avif";
            if (HasAsciiAt(header, 8, "heic") || HasAsciiAt(header, 8, "heix")
                || HasAsciiAt(header, 8, "hevc") || HasAsciiAt(header, 8, "hevx")
                || HasAsciiAt(header, 8, "mif1") || HasAsciiAt(header, 8, "msf1")) return ".heic";
        }
        return null;
    }

    /// <summary>
    /// 拼一段 Zone.Identifier 的正文（MOTW・ZoneId=3＋HostUrl）。物化出的文件若不带这一段，
    /// 就比浏览器下载同一文件少了 SmartScreen 审查＝<b>安全降级</b>——所以 HostUrl 一行永远要写，
    /// 拖源给不出原始网址（浏览器虚拟拖放取不回）时用 <see cref="VirtualDropSource"/> 通用标记。
    /// <paramref name="hostUrl"/> 里的 CR/LF 一律剥掉：换行能伪造 [ZoneTransfer] 之后的行（正文注入）。
    /// 行尾 CRLF；编码（UTF-8 无 BOM）归写盘那侧，这里只负责内容。
    /// </summary>
    public static string BuildZoneIdentifierContent(string? hostUrl)
    {
        var url = (hostUrl ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (url.Length == 0) url = VirtualDropSource;
        return "[ZoneTransfer]\r\nZoneId=" + InternetZone + "\r\nHostUrl=" + url + "\r\n";
    }

    /// <summary>header 从 offset 起的那段是否恰好是这段 ASCII（不够长＝不是）。</summary>
    private static bool HasAsciiAt(ReadOnlySpan<byte> header, int offset, string ascii)
    {
        if (offset < 0 || header.Length < offset + ascii.Length) return false;
        for (var i = 0; i < ascii.Length; i++)
            if (header[offset + i] != ascii[i]) return false;
        return true;
    }
}
