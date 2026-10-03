#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 闸门：一个 <c>file://</c> URI 还原成磁盘路径这件事，<b>只许有一个主人</b>（批次 SH，账本 P-131 清单 #8）。
/// <para>
/// 为什么这条必须有：改道之前"两类生产者互补、两个候选都试"的规则在 <c>LauncherEx</c> 与 <c>PreviewHost</c>
/// 各写一遍，而 <c>ItemCardActions</c>／<c>ItemDragHelper</c> 只写了一半 ⇒ 同一条"剪贴板图片行的路径"，
/// <b>打开是对的、复制是带 <c>%20</c> 的、拖出是退成文本的</b>。这正是清单 #8 写的"任一处补了新规则另两处不跟"，
/// 而且它今天已经在咬人。行为测能钉住判据本身，钉不住"别人有没有去调它"——那一半只能靠这里（#161：净要带凭据、并变成闸门）。
/// </para>
/// <para>
/// UI 工程的调用出不了契约测（<c>StarMark.Tests</c> 不引用 <c>StarMark.UI</c>，#184），
/// 所以宿主那一侧全部用<b>读磁盘源码</b>的方式断言，且一律钉在<b>方法体</b>里（#196：整文件 Contains 会被同文件另一处合法用法顶住）。
/// </para>
/// </summary>
public sealed class LocalPathResolutionGateTests
{
    private const string JudgeFile = "src/StarMark.Abstractions/LocalFileIdentity.cs";

    /// <summary>必须走"两个候选 + 宿主判定"的动作／显示宿主（文件 → 方法体锚点）。</summary>
    private static readonly (string Path, string Signature)[] ActionHosts =
    [
        // 批次 VQ 把这条出口拆成"只要话"与"话＋种类"两份，真正调用判据的那一份搬到 TryOpenDetailedAsync
        // （前者现在只是转发）。锚点跟着搬家，判据（方法体里必须真调 TryExistingPath）一字未动。
        ("src/StarMark.UI/Helpers/LauncherEx.cs", "public static async Task<(OpenFailure Kind, string? Message)> TryOpenDetailedAsync("),
        ("src/StarMark.UI/Controls/PreviewHost.xaml.cs", "private static bool TryGetLocalFile("),
        ("src/StarMark.UI/Helpers/ItemCardActions.cs", "public static async void OpenLocation("),
        ("src/StarMark.UI/Helpers/ItemCardActions.cs", "public static async void CopyUri("),
        ("src/StarMark.UI/Helpers/ItemCardActions.cs", "public static async void CopyImage("),
        ("src/StarMark.UI/Helpers/ItemCardActions.cs", "public static async void PinImageToDesktop("),
        ("src/StarMark.UI/Helpers/ItemDragHelper.cs", "public static void BeginFromUri("),
    ];

    /// <summary>不需要磁盘、只要"该显示哪一条"的两个宿主（快捷启动的默认标题）。</summary>
    private static readonly (string Path, string Signature)[] TitleHosts =
    [
        ("src/StarMark.UI/Views/QuickLaunchWidget.xaml.cs", "private async Task SubmitAddLinkAsync()"),
        ("src/StarMark.UI/Views/WidgetWindow.QuickLaunchDrop.cs", "private async void QuickLaunch_Drop("),
    ];

    /// <summary>
    /// <b>有意留在"只走原始形态"那一侧的名单</b>——不是漏收，改它们会把"同一文件算不算同一条"的<b>键</b>改掉（#188 同族）：
    /// <list type="bullet">
    /// <item><c>SearchService.CanonicalizeUri</c>＝去重键。键一旦跟着 <c>%XX</c> 解码变，
    ///   <c>My Doc\x</c> 与 <c>My%20Doc\x</c> 这两个<b>真实不同的文件</b>就会塌成一条；而键是存量行的主键，改它要迁移。</item>
    /// <item><c>ItemCardPolicy</c>／<c>ItemCardActions</c> 的"这张图能不能按路径读"两处——问的是后缀与能否读到，
    ///   且剪贴板图片行另有主路（按 <c>ClipAssets</c> 的名字名册读，不经 Uri）。</item>
    /// </list>
    /// </summary>
    private static readonly string[] IntentionalRawOnly =
    [
        "src/StarMark.Core/Search/SearchService.cs",
        "src/StarMark.Abstractions/ItemCardPolicy.cs",
        // ⚠ 批次 VR 把 `FolderPathUtil.cs` 从这份名单里<b>移出去了</b>，不是漏收：树里那一段是要印给人看的
        //   （用户真机报"弹出的文件夹是编码的"），所以它改成"先问磁盘哪一格是真的"＝走 PreferredPathFromUri。
        //   登记它当年留在 raw-only 的理由（"改它会动到键与层级"）依然成立一半——键与显示同源正是我们要的：
        //   名字解开了，分组也跟着换过去，绝不允许出现"标题是解码的、点进去按编码的找"。
    ];

    /// <summary>解码那格（<c>Uri.LocalPath</c>）只许出现在判据文件里——别处再写一遍就是第二主人。</summary>
    [Fact]
    public void DecodingExistsNowhereButTheJudge()
    {
        var hits = FormatScanner.SourcesUnder("src", JudgeFile)
            .Where(s => s.Code.Contains(".LocalPath", StringComparison.Ordinal))
            .Select(s => s.Path)
            .ToList();
        Assert.True(hits.Count == 0,
            $"除了判据，还在这些地方自己解 %XX：{string.Join("、", hits)}");
    }

    /// <summary>判据确实握着两格（不是空壳，#161）：原始格来自 <c>TryPathFromUri</c>，解码格来自 <c>LocalPath</c>。</summary>
    [Fact]
    public void TheJudgeHoldsBothCandidates()
    {
        var code = FormatScanner.Scan(ReadRepoFile(JudgeFile)).Code;
        var body = MethodBody(code, "public static (string Raw, string Decoded) PathCandidates(");
        Assert.Contains("TryPathFromUri", body, StringComparison.Ordinal);
        Assert.Contains(".LocalPath", body, StringComparison.Ordinal);
        // 两格各自为空都要认：缺盘符只有解码格、非 file 两格都空——退成"只给一格"就等于回到改道前
        Assert.Contains("string.Empty", body, StringComparison.Ordinal);
    }

    /// <summary>五个动作／显示宿主的方法体里必须真的调用那颗判据（接线断了＝这条宿主又回到半套规则）。</summary>
    [Theory]
    [MemberData(nameof(ActionHostData))]
    public void ActionHostsRouteThroughTheJudge(string path, string signature)
    {
        var body = MethodBody(FormatScanner.Scan(ReadRepoFile(path)).Code, signature);
        Assert.Contains("TryExistingPath", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TitleHostData))]
    public void TitleHostsRouteThroughTheJudge(string path, string signature)
    {
        var body = MethodBody(FormatScanner.Scan(ReadRepoFile(path)).Code, signature);
        Assert.Contains("PreferredPathFromUri", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryPathFromUri", body, StringComparison.Ordinal);   // 别再自己拼"要么这个要么那个"
    }

    /// <summary>名单里的每一处<b>不许</b>被"顺手统一"成走磁盘的还原——它们改的是键与资格，不是显示。</summary>
    [Theory]
    [MemberData(nameof(RawOnlyData))]
    public void TheNamedRawOnlySitesStayRaw(string path, string signature)
    {
        var body = MethodBody(FormatScanner.Scan(ReadRepoFile(path)).Code, signature);
        Assert.DoesNotContain("TryExistingPath", body, StringComparison.Ordinal);
        Assert.Contains("TryPathFromUri", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 反空转（#161）：接线测最怕"锚点全对但其实没人调"。这里数<b>正向证据</b>——
    /// 全仓读判据的文件数与调用点数都设地板，判据变成孤儿时立刻红。
    /// </summary>
    [Fact]
    public void TheJudgeHasRealReadersAndEnoughOfThem()
    {
        var files = new List<string>();
        var calls = 0;
        foreach (var (path, code, _, _) in FormatScanner.SourcesUnder("src", JudgeFile))
        {
            var n = Count(code, "TryExistingPath") + Count(code, "PreferredPathFromUri");
            if (n == 0) continue;
            files.Add(path);
            calls += n;
        }
        Assert.True(files.Count >= 5, $"只有 {files.Count} 个文件在读这颗判据（SH 收官当天实测 6）——接线大概被抽掉了");
        Assert.True(calls >= 8, $"判据被调用 {calls} 处（SH 收官当天实测 9）——宿主大概又退回自己写一份了");
    }

    /// <summary>名单不许无声变长：每一处"只走原始形态"都得在这里点名并带着理由。</summary>
    [Fact]
    public void TheRawOnlyListIsExactlyWhatWeSaidItIs()
    {
        var found = FormatScanner.SourcesUnder("src", JudgeFile)
            .Where(s => s.Code.Contains("TryPathFromUri", StringComparison.Ordinal)
                        && !s.Code.Contains("TryExistingPath", StringComparison.Ordinal))
            .Select(s => s.Path.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(IntentionalRawOnly.OrderBy(p => p, StringComparer.Ordinal), found);
    }

    public static TheoryData<string, string> ActionHostData()
        => Hosts(ActionHosts.Select(h => (h.Path, h.Signature)));

    public static TheoryData<string, string> TitleHostData()
        => Hosts(TitleHosts.Select(h => (h.Path, h.Signature)));

    public static TheoryData<string, string> RawOnlyData()
        => Hosts([
            ("src/StarMark.Core/Search/SearchService.cs", "private static string CanonicalizeUri("),
            // FolderPathUtil.FileSegments 已移出（批次 VR，理由见 IntentionalRawOnly）：它现在走 PreferredPathFromUri。
            ("src/StarMark.Abstractions/ItemCardPolicy.cs", "public static bool CanCopyAsImage("),
        ]);

    private static TheoryData<string, string> Hosts(IEnumerable<(string Path, string Signature)> hosts)
    {
        var data = new TheoryData<string, string>();
        foreach (var (path, signature) in hosts) data.Add(path, signature);
        return data;
    }
}
