#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「复制链接」菜单的接线闸门（片213·P-295）。
/// <para>缺陷形状：剪贴板页的 <c>Card_CopyRequested</c>（XAML 里 <c>CopyLinkRequested</c> 那颗菜单）
/// 从前接的也是 <c>ClipboardPageViewModel.ReuseAsync</c>——与点卡片本体（<c>Card_OpenRequested</c>）
/// 同一颗动作。「复制链接」的语义是<b>把这一条的 URI 原样复制走</b>（主窗条目卡同款
/// <c>ItemCardActions.CopyUri</c>），而不是「再复制一次这条剪贴板记录」：两条路各做各的事，
/// 接错的那颗菜单在界面上「能用」，所以编译、点击、肉眼看代码都发现不了。</para>
/// <para>测试工程不引用 StarMark.UI，钉的是源码形状（UI 行为一律 SourceGate 源码扫）：
/// ①菜单处理器体里是 <c>CopyUri</c> 而⛔ <c>ReuseAsync</c>；②点卡片本体的处理器保持
/// <c>ReuseAsync</c>（「打开＝再复制一次」是剪贴板条目那页的既定语义，⛔ 顺手改坏）；
/// ③XAML 两颗事件各自接到各自那颗处理器上——接线错了编译器不吭声。</para>
/// </summary>
public sealed class ClipboardCopyLinkGateTests
{
    private const string PageCs = "src/StarMark.UI/Views/ClipboardPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/ClipboardPage.xaml";

    /// <summary>①「复制链接」菜单＝把 URI 原样复制走（CopyUri），⛔ 再借用「再复制一次」那颗动作。</summary>
    [Fact]
    public void TheCopyLinkMenuCopiesTheUriInsteadOfReusingTheEntry()
    {
        var body = Code(MethodBody(ReadRepoFile(PageCs),
            "private void Card_CopyRequested(object? sender, ItemCardViewModel vm)"));

        Assert.Contains("ItemCardActions.CopyUri(vm)", body, StringComparison.Ordinal);   // 正件
        Assert.DoesNotContain("ReuseAsync", body, StringComparison.Ordinal);              // 旧接线⛔ 长回来
    }

    /// <summary>②点卡片本体保持「再复制一次」（ReuseAsync）：那是剪贴板条目「打开」的既定语义，
    /// 与菜单那条路分清，谁也别吞并谁。</summary>
    [Fact]
    public void OpeningTheCardItselfStillReusesTheEntry()
    {
        var body = Code(MethodBody(ReadRepoFile(PageCs),
            "private async void Card_OpenRequested(object sender, long itemId)"));

        Assert.Contains("await ViewModel.ReuseAsync(vm)", body, StringComparison.Ordinal);
    }

    /// <summary>③XAML 两颗事件各接各的处理器：菜单接 Copy 那颗、本体接 Open 那颗——
    /// XAML 的处理器名只是字符串，接错了要等运行时才炸。</summary>
    [Fact]
    public void TheXamlWiresBothEventsToTheirOwnHandlers()
    {
        var xaml = ReadRepoFile(PageXaml);

        Assert.Contains("OpenRequested=\"Card_OpenRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CopyLinkRequested=\"Card_CopyRequested\"", xaml, StringComparison.Ordinal);
    }
}
