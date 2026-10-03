#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 VR：一条 <c>file://</c> -uri 的<b>两种病</b>各要有一名证人。
/// <para>
/// 病一＝<b>编码态印给人看</b>。库里 <c>file://</c> 有两类互补生产者（原样的 <c>UriForPath</c> 与 percent 编码的
/// <c>new Uri(path).AbsoluteUri</c>），而文件夹树的层级、卡片的 tooltip、"已经不在本机"那句提示以前都直接念
/// 库里那一串 ⇒ 用户看到的是 <c>D:\Visual%20Studio%20Code\…</c>（用户原话：「文件路径中的空格会被展示为%20」）。
/// 修法不是"到处 Unescape"：<b>选哪一格仍归 <c>LocalFileIdentity</c> 那一颗</b>（先问磁盘，原始那格在盘上就用它，
/// 否则真名叫 <c>100%20.txt</c> 的文件会被改名展示），显示侧只是改用那颗的出口。
/// </para>
/// <para>
/// 病二＝<b>坏消息再次被扔掉</b>。VQ 只收口了卡片那两条入口，六个组件/页面宿主照旧 <c>_ = LauncherEx.OpenAsync(...)</c>
/// ⇒ 同样的"点了没反应"换了个宿主复发。这一族的判据不是"记得接返回值"，是<b>那条不返回话的出口从 API 里消失</b>。
/// </para>
/// <para>
/// UI 层出不了契约测（<c>StarMark.Tests</c> 不引用 <c>StarMark.UI</c>，#184），所以宿主侧全部读磁盘源码，
/// 且一律钉在<b>方法体</b>里（#196）。
/// </para>
/// </summary>
public sealed class EncodedPathDisplayGateTests
{
    private const string Actions = "src/StarMark.UI/Helpers/ItemCardActions.cs";
    private const string Launcher = "src/StarMark.UI/Helpers/LauncherEx.cs";
    private const string Judge = "src/StarMark.Abstractions/LocalFileIdentity.cs";
    private const string TreeVm = "src/StarMark.UI/ViewModels/FolderTreePageViewModel.cs";
    private const string TreePolicy = "src/StarMark.Abstractions/FolderPathUtil.cs";
    private const string CardXaml = "src/StarMark.UI/Controls/ItemCard.xaml";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";

    /// <summary>手上只有一条 uri（没有视图模型／没有库身份可问）的宿主：必须走 <c>OpenUriAndReport</c>。</summary>
    private static readonly (string Path, string Signature)[] UriOnlyHosts =
    [
        ("src/StarMark.UI/Views/SearchWidget.xaml.cs", "private void ResultOpen_Click("),
        ("src/StarMark.UI/Views/QuickLaunchWidget.xaml.cs", "private void Card_OpenRequested("),
        ("src/StarMark.UI/Views/TrendingPage.xaml.cs", "private void Card_OpenRequested("),
        ("src/StarMark.UI/Views/GlanceWidget.xaml.cs", "private static void Item_Click("),
        ("src/StarMark.UI/Views/ActivityPage.xaml.cs", "private void Activity_Click("),
        ("src/StarMark.UI/Controls/PreviewHost.xaml.cs", "private void Open_Click("),
    ];

    /// <summary>全仓（抹掉注释后的每一颗手写 .cs）合成的一份文本——普查用它。</summary>
    private static string AllSrcCode() => string.Join("\n", FormatScanner.SourcesUnder("src").Select(s => s.Code));

    [Theory]
    [MemberData(nameof(UriOnlyHostData))]
    public void UriOnlyHostsHandTheReportToTheSingleExit(string path, string signature)
    {
        var body = MethodBody(Code(ReadRepoFile(path)), signature);
        Assert.Contains("OpenUriAndReport", body, StringComparison.Ordinal);
        // 裸 await／丢弃返回值＝又把话扔了（VQ 那四条症状在组件侧的原样复发形状）
        Assert.DoesNotContain("await LauncherEx.OpenAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = LauncherEx", body, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> UriOnlyHostData()
    {
        var data = new TheoryData<string, string>();
        foreach (var (path, signature) in UriOnlyHosts) data.Add(path, signature);
        return data;
    }

    /// <summary>
    /// 全仓普查：那条<b>把话扔掉的出口</b>与它的两种写法都不许再出现，同时收口处必须有足够多的真读者（反空转，#161）。
    /// </summary>
    [Fact]
    public void NoHostAnywhereThrowsTheReportAway()
    {
        var src = AllSrcCode();
        Assert.Equal(0, Count(src, "LauncherEx.OpenAsync"));
        Assert.Equal(0, Count(src, "_ = LauncherEx"));
        // 定义 1 处 ＋ 上面 6 个宿主 ＋ SearchWidget 的回车那条 = 8；少一处就是某个宿主的接线又被抽掉了
        Assert.True(Count(src, "OpenUriAndReport") >= 8,
            $"OpenUriAndReport 只被点到 {Count(src, "OpenUriAndReport")} 处（VR 收官当天 9）——有宿主退回自己 await 了");
    }

    /// <summary>
    /// 树里那一串文件夹名<b>必须先问过磁盘</b>：不许有"不注入判据就能算层级"的第二条路。
    /// <para>当年它走 <c>TryPathFromUri</c>（只剥前缀），于是编码态 URI 把 <c>Visual%20Studio%20Code</c>
    /// 当文件夹名印出来。留一条单参数的重载，就等于留一个"下次还这么写"的入口（键与显示同源这条不变式会被绕开）。</para>
    /// </summary>
    [Fact]
    public void TheTreeMustAskTheDiskBeforeNamingAFolder()
    {
        var policy = Code(ReadRepoFile(TreePolicy));
        Assert.Equal(1, Count(policy, "public static string[] GetSegments(Item item, Func<string, bool> existsOnDisk)"));
        Assert.Equal(1, Count(policy, "public static string[] FileSegments(Item item, Func<string, bool> existsOnDisk)"));
        Assert.DoesNotContain("GetSegments(Item item)", policy, StringComparison.Ordinal);   // 单参数重载不许回来

        // 调用点必须真的交出判据，而不是传个恒真/恒假的摆设（#250：钉整行调用文本会把"加了 using 之后简写"这种等价改写判成事故）
        var body = MethodBody(Code(ReadRepoFile(TreeVm)), "private async Task LoadCoreAsync()");
        Assert.Contains("GetSegments(item,", body, StringComparison.Ordinal);
        Assert.Contains("LauncherEx.ExistsOnDisk", body, StringComparison.Ordinal);
        var bare = Regex.Matches(AllSrcCode(), @"FolderPathUtil\.GetSegments\([^,)]*\)");
        Assert.True(bare.Count == 0,
            $"有 {bare.Count} 处用不带判据的老形状调 GetSegments——绕开磁盘就是这次症状的原样复发");
    }

    /// <summary>
    /// "这台机器上有没有这东西"只许有一处实现，且<b>两半都在</b>。
    /// <para>少写 <c>Directory.Exists</c> 的那一份拷贝，症状就是"文件夹明明还在、点开说已经不在了"（批次 210 同款）。
    /// 六处各写一遍时没人能保证它们同岁，收成一颗后由这一格保证不分裂。</para>
    /// </summary>
    [Fact]
    public void TheDiskFactHasExactlyOneOwnerAndItChecksBothArms()
    {
        Assert.Single(Regex.Matches(AllSrcCode(),
            @"File\.Exists\([^)]*\)\s*\|\|\s*(?:System\.IO\.)?Directory\.Exists"));
        var owner = Code(ReadRepoFile(Launcher));
        Assert.Contains("public static bool ExistsOnDisk(string? path)", owner, StringComparison.Ordinal);
        // 判据本体（Abstractions）仍然不碰磁盘：磁盘事实一律由宿主注入
        Assert.DoesNotContain("File.Exists", Code(ReadRepoFile(Judge)), StringComparison.Ordinal);
    }

    /// <summary>
    /// 给人看的那一格只许用在<b>显示</b>上：动作（打开／定位／拖出／复制）仍然问 <c>TryExistingPath</c>。
    /// <para>拿 <c>DisplayPath</c> 的结果去 <c>Process.Start</c> 就是"看着对、点开指到另一个名字"——
    /// 两格谁在盘上是事实判断，不许被"哪格好看"顶替。</para>
    /// </summary>
    [Fact]
    public void TheShownPathIsUsedForDisplayOnly()
    {
        var users = FormatScanner.SourcesUnder("src", Judge)
            .Where(s => s.Code.Contains("FileIdentity.DisplayPath", StringComparison.Ordinal))
            .Select(s => s.Path.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { Launcher, CardVm }, users);

        // 打开那颗拿的仍是"存在与否"的判据，不是显示串
        var open = MethodBody(Code(ReadRepoFile(Launcher)),
            "public static async Task<(OpenFailure Kind, string? Message)> TryOpenDetailedAsync(");
        Assert.Contains("TryExistingPath", open, StringComparison.Ordinal);
        Assert.Contains("DisplayPath(uri", open, StringComparison.Ordinal);   // 只有那句提示走显示串（第二参数怎么写不归它管）
        Assert.DoesNotContain("FileName = DisplayPath", open, StringComparison.Ordinal);
    }

    /// <summary>卡片 tooltip 与提示句读的是显示串，且 tooltip 不许又改回绑 uri 原文。</summary>
    [Fact]
    public void CardTooltipBindsTheShownPathNotTheStoredUri()
    {
        var xaml = Markup(ReadRepoFile(CardXaml));
        Assert.Equal(1, Count(xaml, "ToolTipService.ToolTip=\"{x:Bind ViewModel.UriForDisplay"));
        Assert.Equal(0, Count(xaml, "ToolTipService.ToolTip=\"{x:Bind ViewModel.Uri,"));

        var vm = Code(ReadRepoFile(CardVm));
        Assert.Contains("string UriForDisplay", vm, StringComparison.Ordinal);
        Assert.Contains("FileIdentity.DisplayPath", vm, StringComparison.Ordinal);
        // 动作侧仍交原文：改了这里，复制/打开就会跟着"显示什么就点什么"，那是错的
        Assert.Contains("public string Uri => _item.Uri", vm, StringComparison.Ordinal);
    }

    /// <summary>守门自己要有证人（#228/#236）：喂回旧写法，普查必须抓得住。</summary>
    [Fact]
    public void TheCensusesActuallyBitOnSamples()
    {
        var old = "private void Item_Click(object s, RoutedEventArgs e) { _ = LauncherEx.OpenAsync(uri); }";
        Assert.Contains("_ = LauncherEx", old, StringComparison.Ordinal);
        Assert.Contains("LauncherEx.OpenAsync", old, StringComparison.Ordinal);
        // 单参数的老签名会被那道正则当成"没带判据的调用点"
        Assert.Single(Regex.Matches("FolderPathUtil.GetSegments(item)", @"FolderPathUtil\.GetSegments\([^,)]*\)"));
        Assert.Empty(Regex.Matches("FolderPathUtil.GetSegments(item, LauncherEx.ExistsOnDisk)",
            @"FolderPathUtil\.GetSegments\([^,)]*\)"));
        // 只查文件不查目录的那半份磁盘判据
        Assert.Single(Regex.Matches("if (File.Exists(p) || Directory.Exists(p))",
            @"File\.Exists\([^)]*\)\s*\|\|\s*(?:System\.IO\.)?Directory\.Exists"));
        Assert.Empty(Regex.Matches("if (System.IO.File.Exists(p) && System.IO.Directory.Exists(p))",
            @"File\.Exists\([^)]*\)\s*\|\|\s*(?:System\.IO\.)?Directory\.Exists"));
    }

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
}
