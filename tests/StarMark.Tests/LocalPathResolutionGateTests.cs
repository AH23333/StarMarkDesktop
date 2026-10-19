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
        //   （用户原话：「文件路径中的空格会被展示为%20」），所以它改成"先问磁盘哪一格是真的"＝走 PreferredPathFromUri。
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

    /// <summary>
    /// <b>P-236：那颗类的射程口径只许说在一处，且⛔ 许再把"本地路径"写进自己的射程</b>。
    /// <para>这一族的坏法⛔ 是判据错，是<b>注释把⛔ 认的输入写成自己收的输入</b>：
    /// <c>LauncherEx</c> 的类注释曾举着"本地路径（文件／文件夹）"当例子，而它底下那颗还原判据的入口条件是
    /// <c>file://</c> 前缀——照着那颗注释写新宿主的人，得到的是一句"图已不在本机"＋桌面多开一颗<b>主文件夹</b>窗
    /// （真机读数 <c>bin/WI91/readings_39_183824.txt</c>；事实那一半由
    /// <c>LocalPathResolutionTests.ABareDiskPathIsNotWhatThisJudgeAcceptsEvenWhenTheFileIsReallyThere</c> 钉着，
    /// 那句"闸门确实放裸路径过去"由同档下一颗钉着——三颗一起读才说得出真因在哪一处）。</para>
    /// <para>⚠ 这一格<b>故意⛔ 去 grep 调用点的实参名字</b>：<c>vm.Uri</c>／<c>envelope.Url</c> 那种形状既⛔ 证明不了
    /// 传进来的真是 URI，也抓不住一颗真叫 <c>path</c> 而内容是 URI 的串（名册 347：⛔ 用语法形状代替"谁有资格说"）。
    /// 八处调用点的<b>逐处定性</b>记在《开发进度报告》Phase C 片42 那一节。</para>
    /// </summary>
    [Fact]
    public void TheScopeOfLauncherExIsStatedOnceAndNoLongerPromisesBarePaths()
    {
        var raw = ReadRepoFile("src/StarMark.UI/Helpers/LauncherEx.cs");
        Assert.Equal(0, Count(raw, "本地路径（文件"));              // 那句把射程说大了的旧措辞⛔ 许回来
        Assert.Equal(1, Count(raw, "⛔ 在本类的射程里"));           // 完整口径只在一处（类文件头）
        Assert.Equal(1, Count(raw, "裸磁盘路径⛔ 在射程内"));       // 方法那一处只指回它，⛔ 重抄整段
        Assert.True(Count(raw, "LocalFileIdentity.UriForPath") >= 1);   // 给"手上只有磁盘路径"的人一颗现成的主人
        Assert.Contains("readings_39_183824", raw, StringComparison.Ordinal);   // 那次改口带着它的读数出处（UK／UL 那条口径）
    }

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
