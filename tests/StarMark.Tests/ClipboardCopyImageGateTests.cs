#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-2d（右键「复制图片」）的守门。<b>测试工程不引用 StarMark.UI</b>，这一层只能钉源码形状——
/// 而这一批的三条"必坏线"恰好全长在形状上：
/// <para>① 交出去的必须是<b>位图数据</b>（交 <c>file://</c> 会被系统按"复制了一个文件"呈现＝用户报的那个症状）；</para>
/// <para>② 回声登记必须早于 <c>SetContent</c>（晚一步就挡不住那一帧通知＝每右键一次历史多一条）；</para>
/// <para>③ 菜单有<b>两处</b>宿主（组件行共用 <c>ItemContextMenu</c> 与卡片自己的 <c>ContextFlyout</c>），
/// 一处补了另一处没补＝"剪贴板页有、文件夹树没有"，正是用户点名要的那种不一致。</para>
/// </summary>
public sealed class ClipboardCopyImageGateTests
{
    private const string Actions = "src/StarMark.UI/Helpers/ItemCardActions.cs";
    private const string Menu = "src/StarMark.UI/Helpers/ItemContextMenu.cs";
    private const string CardXaml = "src/StarMark.UI/Controls/ItemCard.xaml";
    private const string CardCs = "src/StarMark.UI/Controls/ItemCard.xaml.cs";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string Store = "src/StarMark.Integrations/Clipboard/ClipboardImageStore.cs";

    private const string CopyImageAnchor = "public static async void CopyImage(ItemCardViewModel vm)";

    // ────────── ① 交数据，不交路径 ──────────

    [Fact]
    public void CopyImageHandsOverBytesNotAFilePath()
    {
        var body = MethodBody(ReadRepoFile(Actions), CopyImageAnchor);
        Assert.Contains("SetBitmap", body);
        Assert.Contains("CreateFromStream", body);
        // 反向钉死那三种"看起来在复制图片、其实复制的是别的东西"的写法：
        Assert.DoesNotContain("CreateFromUri", body);      // 那一颗交的是文件位置 → 粘出来是一串路径
        Assert.DoesNotContain("SetStorageItems", body);    // 那颗才是"复制文件"
        Assert.DoesNotContain("SetText", body);            // 图片行交文本＝粘出来什么都没有
        Assert.Contains("Clipboard.Flush()", body);        // 不 Flush，窗口一挂起内容就没了

        // 读盘与解码不许出现在 UI 这一层：两处各读一次就会与采集侧算出两种哈希（回声当场失效）。
        Assert.DoesNotContain("ReadAllBytes", body);
        Assert.DoesNotContain("TryDecodePng", body);
        Assert.Contains("Task.Run", body);                 // 整段离 UI 线程（几十 MB 的 TIFF 也在树上）
    }

    [Fact]
    public void EchoIsRegisteredBeforeTheImageIsHandedOver()
    {
        var body = MethodBody(ReadRepoFile(Actions), CopyImageAnchor);
        var note = body.IndexOf("NoteClipboardOwnImageWrite", StringComparison.Ordinal);
        var setContent = body.IndexOf("Clipboard.SetContent", StringComparison.Ordinal);
        Assert.True(note >= 0 && setContent >= 0, "要么没登记回声，要么根本没写剪贴板");
        Assert.True(note < setContent, "回声登记必须早于 SetContent：晚一步就来不及挡那一帧通知");
        // 登记的是像素，不是文件字节（系统会把 PNG 重排成 CF_DIB 再广播回来）。
        Assert.Contains("NoteClipboardOwnImageWrite(frame.Bgra)", body);
    }

    [Fact]
    public void TheActionRefusesRowsThatHaveNoImageAndSaysWhy()
    {
        var body = MethodBody(ReadRepoFile(Actions), CopyImageAnchor);
        // 判据只有一份：菜单上不该有这一项的行，动作自己也要收住（宿主自己挂按钮时绕过规则是必然发生的事）。
        Assert.Contains("if (!vm.CanCopyAsImage) return;", body);
        // 坏消息要给出口（P-54 同口径）：只回一句"复制失败"，用户分不清是程序坏了还是这张图坏了。
        Assert.Contains("没能复制图片", body);
        Assert.Contains("ShowError", body);
        Assert.Contains("why", body);                      // 原因来自那一处读文件的入口，不在这里另编一句
    }

    // ────────── ③ 两处菜单都得有，而且位置一致 ──────────

    [Fact]
    public void SharedContextMenuOffersItRightAfterTheCopyItem()
    {
        var body = MethodBody(ReadRepoFile(Menu), "private static MenuFlyout Build(ItemCardViewModel vm, XamlRoot root)");
        var copy = body.IndexOf("ItemCardActions.CopyUri(vm)", StringComparison.Ordinal);
        var image = body.IndexOf("ItemCardActions.CopyImage(vm)", StringComparison.Ordinal);
        var preview = body.IndexOf("vm.ShowsPreview", StringComparison.Ordinal);
        Assert.True(copy >= 0 && image >= 0 && preview >= 0, "菜单里少了复制路径 / 复制图片 / 预览其中一项");
        Assert.True(copy < image && image < preview, "「复制图片」必须紧跟在\"复制链接/路径\"之后、预览之前");
        // 出现与否只看那一处判据，不许在这里再 AND 上类型/来源（那会变成第二种"什么算图片行"）。
        Assert.Contains("if (vm.CanCopyAsImage)", body);
        // ClipIMG-P3：「贴到桌面」与「复制图片」相邻且同判据——两出口一族挂，缺一个就是 2d 的坏形状。
        var pin = body.IndexOf("ItemCardActions.PinImageToDesktop(vm)", StringComparison.Ordinal);
        Assert.True(pin > image && pin < preview, "「贴到桌面」要紧跟复制图片之后、预览之前");
    }

    [Fact]
    public void CardFlyoutMirrorsTheSharedMenu()
    {
        var xaml = ReadRepoFile(CardXaml);
        var copy = xaml.IndexOf("Click=\"Menu_CopyLink\"", StringComparison.Ordinal);
        var image = xaml.IndexOf("Click=\"Menu_CopyImage\"", StringComparison.Ordinal);
        Assert.True(copy > 0 && image > copy, "卡片的 ContextFlyout 里「复制图片」要紧跟在复制链接之后（与组件行同序）");
        Assert.Contains("Text=\"复制图片\"", xaml);
        // 1→2 是 ClipIMG-P3 决议过的出口增长（「贴到桌面」与「复制图片」共用同一颗判据）：
        // 这里钉的不再是"只有一处图片出口"，而是"每一出口都只认同一颗属性、且各出口顺序一致"。
        Assert.Equal(2, Count(xaml, "ViewModel.CanCopyAsImage"));
        Assert.Contains("Text=\"贴到桌面\"", xaml);
        var pinAt = xaml.IndexOf("Click=\"Menu_PinToDesktop\"", StringComparison.Ordinal);
        Assert.True(pinAt > image, "「贴到桌面」要紧跟在「复制图片」之后（与组件行同序，两处菜单不许各排各的）");
        Assert.Contains("BoolToVis", xaml[image..(xaml.IndexOf("/>", image, StringComparison.Ordinal) + 2)]);

        // 代码侧那颗必须自包含：卡片被十余处复用，走页面事件就会有一处"菜单里有、点了没反应"。
        var cs = ReadRepoFile(CardCs);
        var handler = MethodBody(cs, "private void Menu_CopyImage(object sender, RoutedEventArgs e)");
        Assert.Contains("ItemCardActions.CopyImage(ViewModel)", handler);
        Assert.DoesNotContain("Requested?.Invoke", handler);
    }

    [Fact]
    public void TheCardModelOnlyAsksCore()
    {
        var vm = ReadRepoFile(CardVm);
        Assert.Contains("public bool CanCopyAsImage => ItemCardPolicy.CanCopyAsImage(Uri);", vm);
        // 扩展名清单不许在 UI 再长一份（预览那份是"能不能画出一格"，与"能不能编成位图"不是同一件事）。
        Assert.DoesNotContain(".webp", vm);
        Assert.DoesNotContain(".tiff", vm);
    }

    // ────────── ④ 文件侧：转码只当转码器，身份只有一种算法 ──────────

    [Fact]
    public void WinrtDecoderIsATranscoderNotAnIdentitySource()
    {
        var store = ReadRepoPartials(Store);
        // 解 PNG 只有一处，且交出去的字节与登记的像素出自同一张（两处各解一次＝两种哈希＝回声挡不住）。
        Assert.Equal(1, Count(store, "ClipboardPayload.TryDecodePng("));
        Assert.DoesNotContain("BuildImageSourceId", store);
        var transcode = MethodBody(store,
            "private static async Task<byte[]?> EncodePngFromBytesAsync");
        Assert.Contains("BitmapDecoder", transcode);
        Assert.Contains("BitmapEncoder.PngEncoderId", transcode);
        Assert.Equal(1, Count(store, "BitmapDecoder"));                 // 只许出现在那一个方法里
        // WinRT 的写流同样是带缓冲的包装：漏了 Flush 交出去的是 Size=0，症状是"复制成功、粘出来空的"。
        Assert.Contains("writer.Flush();", transcode);
        // 不引 NuGet、不用 System.Drawing：这条转码路必须走已经在用的那套成像栈。
        // 只钉 using，不钉 "System.Drawing" 这个词——产品注释里就写着"这里没有 System.Drawing"，
        // 数整个词会把注释算进去（#123 同族：假失败最坏，因为它教人把闸门改松而不是改对）。
        Assert.DoesNotContain("using System.Drawing", store);
    }

    [Fact]
    public void BothFileEntriesShareOneMissingFileSentence()
    {
        // "文件不在了"在历史行与本机文件行是同一件事，两份措辞就会让用户以为有两种成因。
        var store = ReadRepoPartials(Store);
        Assert.Equal(1, Count(store, "private const string MissingFileReason"));   // 一句真话只有一个出处
        Assert.Equal(3, Count(store, "MissingFileReason"));                          // 声明 + 两个入口各用一次
        Assert.DoesNotContain("这个文件已经不在本机", store);                          // 反向钉：不许出现第二份写法
    }
}
