#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace StarMark.Integrations.Everything;

/// <summary>
/// P-145 (A)：Everything 三处下载点的「下载点验」。
/// <para>背景（账本 P-145）：SDK zip / 安装器 / 便携包都走 https 直连 voidtools 官网下载，
/// 中间任何一环（被劫持的 Wi-Fi 门户、被投毒的镜像、CDN 错件、磁盘半截写入）都可能把
/// 一颗「看起来是 Everything」的可执行文件送进 <c>Process.Start</c>——那是一颗以当前用户权限
/// 静默运行的任意代码入口。MVP 之前没有任何一路读数能分辨「下到的是不是 voidtools 发的那颗」。</para>
/// <para>路线 (A)（已裁）：URL 带版本号的两包（安装器 / 便携版 zip）<b>硬钉 SHA-256</b>——
/// 内容钉死，一字节都不许变；滚动的 SDK zip <b>验解出 DLL 的 Authenticode</b>——
/// 「内容会滚、签名者不会滚」，判据跟签名者（voidtools）不跟字节。</para>
/// <para>哈希常量是<b>唯一出处</b>：升级 Everything 版本＝改 URL 时必须同时改这里（由
/// <c>EverythingDownloadTrustTests</c> 的形状钉与 <c>EverythingQueryQueue</c> 三处接线共同守住）。</para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class EverythingDownloadTrust
{
    /// <summary>voidtools 官方 1.4.1.1028 x64 安装器（Everything-1.4.1.1028.x64-Setup.exe）的 SHA-256。
    /// 2026-11-03 自官网原样下载实测；升级版本号时随 URL 一起换钉。</summary>
    public const string InstallerSha256 =
        "874e87dc9de6e99bcbf46400bf7eced0deda402fc79dd13e0cafabf2277c8149";

    /// <summary>voidtools 官方 1.4.1.1024 x64 便携版（Everything-1.4.1.1024.x64.zip）的 SHA-256。
    /// 2026-11-03 自官网原样下载实测；升级版本号时随 URL 一起换钉。</summary>
    public const string PortableZipSha256 =
        "4be0851752e195c9c7f707b1e0905cd01caf6208f4e2bfa2a66e43c0837be8f5";

    /// <summary>
    /// 验文件 SHA-256（hex 大小写不敏感）。安装器那臂的落盘不是原子写（<see cref="EverythingQueryQueue"/>
    /// 直写终件），截断包靠这里兜底：不匹配＝删包重下，绝不带着坏包去 <c>Process.Start</c>。
    /// </summary>
    public static bool VerifySha256(string path, string expectedHex)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expectedHex.Trim().ToUpperInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// WinVerifyTrust 验 Authenticode 证书链（不做签名者身份检查——「是谁签的」由
    /// <see cref="IsVoidtoolsSigned"/> 那层加）。无签名 / 链验失败 / 文件损坏均返回 false。
    /// <para>撤销检查刻意关死（<c>WTD_REVOKE_NONE</c>）：开着时每次验签都去抓 CRL/OCSP，
    /// 离线或网络受限一卡就是分钟级；这里要拦的是「换了一颗文件」，不是「原文件被事后吊销」——
    /// 后者的窗口由哈希钉（两包）与签名者判据（SDK）共同覆盖到可接受的程度。</para>
    /// <para>坑（本批实测）：<see cref="WinVerifyTrust"/> 第三参要的是 <see cref="WinTrustData"/>
    /// （内含 union choice 指到 <see cref="WinTrustFileInfo"/>），不是 FILE_INFO 本身——
    /// 直传 FILE_INFO 会按 WINTRUST_DATA 的字段序读串位，错误码随内存布局漂移且照常「成功返回」，
    /// 三份探针（python／PS／C#）曾同一形状全错。布局一律照抄本机 SDK wintrust.h。</para>
    /// </summary>
    public static bool IsAuthenticodeTrusted(string path)
    {
        var guid = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var info = new WinTrustFileInfo(path);
        var data = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            dwUIChoice = 2,            // WTD_UI_NONE：不弹任何 UI，成败只看返回码
            fdwRevocationChecks = 0,   // WTD_REVOKE_NONE：不查撤销（见上）
            dwUnionChoice = 1,         // WTD_CHOICE_FILE：pFile 指向下面那颗 FILE_INFO
            dwStateAction = 1,         // WTD_STATEACTION_VERIFY（验完须 CLOSE，见下）
            dwUIContext = 0,           // WTD_UICONTEXT_EXECUTE
        };
        var pGuid = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        var pInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var pData = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
        try
        {
            Marshal.StructureToPtr(guid, pGuid, fDeleteOld: false);
            Marshal.StructureToPtr(info, pInfo, fDeleteOld: false);
            data.pFile = pInfo;
            Marshal.StructureToPtr(data, pData, fDeleteOld: false);
            var trusted = WinVerifyTrust(IntPtr.Zero, pGuid, pData) == 0;   // ERROR_SUCCESS＝0
            // VERIFY 会在 hWVTStateData 留一份状态句柄；不 CLOSE＝每次验签漏一块原生内存。
            Marshal.WriteInt32(pData,
                (int)Marshal.OffsetOf<WinTrustData>(nameof(WinTrustData.dwStateAction)),
                2 /* WTD_STATEACTION_CLOSE */);
            try { WinVerifyTrust(IntPtr.Zero, pGuid, pData); } catch { /* CLOSE 失败也只是这枚状态句柄交还给系统 */ }
            return trusted;
        }
        finally
        {
            Marshal.DestroyStructure(pInfo, typeof(WinTrustFileInfo));   // 收回 StructureToPtr 为字符串编出的原生缓冲
            Marshal.FreeHGlobal(pGuid);
            Marshal.FreeHGlobal(pInfo);
            Marshal.FreeHGlobal(pData);
        }
    }

    /// <summary>
    /// 滚动 SDK zip 的判据：解出的 Everything64.dll 既要有有效 Authenticode（链验过），
    /// 签名者又得是 voidtools——「内容会滚、签名者不会滚」（P-145 (A)）。
    /// 只验链不验人＝任何一张自签证书都能冒充；只验人不验链＝被吊销/过期的证书也放行。
    /// </summary>
    public static bool IsVoidtoolsSigned(string path)
        => IsAuthenticodeTrusted(path)
            && TryGetSignerSubject(path)?.Contains("voidtools", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>读签名者 Subject（无签名 / 读不出证书＝null，不抛——调用方拿 null 当验不过）。</summary>
    internal static string? TryGetSignerSubject(string path)
    {
        try { return new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).Subject; }
        catch (CryptographicException) { return null; }   // 无签名／非 PE：验签那层已给了 false，这里不重复报错
    }

    // 布局照抄 wintrust.h 的 WINTRUST_FILE_INFO_（字段序＝cbStruct→pcwszFilePath→hFile→pgKnownSubject，
    // 2026-11-03 对照本机 Windows SDK 头文件核实——序错＝cbStruct 对不上＝WinVerifyTrust 拒收）。
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;   // 必填：按路径验
        public IntPtr hFile;           // 可选：给 0（IntPtr.Zero）即按上面那颗路径开文件
        public IntPtr pgKnownSubject;  // 可选：Subject 类型已知才填；给 0

        public WinTrustFileInfo(string path)
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            pcwszFilePath = path;
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }
    }

    // 布局照抄 wintrust.h 的 _WINTRUST_DATA（2026-11-03 对照本机 SDK 核实）。Win8+ 尾部还有
    // pSignatureSettings 一颗可选指针，靠 cbStruct 区分版本——这里刻意不带，系统按旧布局收。
    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;  // 0
        public IntPtr pSIPClientData;       // 0
        public uint dwUIChoice;             // WTD_UI_NONE＝2
        public uint fdwRevocationChecks;    // WTD_REVOKE_NONE＝0
        public uint dwUnionChoice;          // WTD_CHOICE_FILE＝1
        public IntPtr pFile;                // → WinTrustFileInfo
        public uint dwStateAction;          // WTD_STATEACTION_VERIFY＝1（CLOSE＝2 收状态）
        public IntPtr hWVTStateData;        // 0（VERIFY 后由系统写入，CLOSE 时读回）
        public IntPtr pwszURLReference;     // 0
        public uint dwProvFlags;            // 0＝默认策略
        public uint dwUIContext;            // WTD_UICONTEXT_EXECUTE＝0
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, IntPtr pgActionId, IntPtr pWvtData);
}
