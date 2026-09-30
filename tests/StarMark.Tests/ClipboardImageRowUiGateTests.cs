#nullable enable
using System;
using System.Text.RegularExpressions;
using StarMark.Abstractions.Clipboard;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-2a（图片行的界面那一层）的守门。<b>测试工程不引用 StarMark.UI</b>，所以这里只能钉源码形状——
/// 偏偏这一层的四条"必坏线"全都长在形状上：分流的先后、登记回声的先后、解码尺寸的出处、坏消息给不给出口。
/// <para>每条都写明"破了会怎样"：这类闸门最坏的失效不是红，是安静地白过。</para>
/// </summary>
public sealed class ClipboardImageRowUiGateTests
{
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string CardXaml = "src/StarMark.UI/Controls/ItemCard.xaml";
    private const string PageVm = "src/StarMark.UI/ViewModels/ClipboardPageViewModel.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";

    // ────────── 分流：图片行不许掉进文本那条路 ──────────

    [Fact]
    public void ImageRowsLeaveTheTextPathBeforeAnyStringIsRead()
    {
        // 图片条目的 Description 刻意是空的（正文在文件里）。顺序写反——先读 Description 再分流——
        // 症状是"点一张历史图，得到一句『这条历史没有可复制的正文』"：功能没坏，说了一句假话。
        var body = MethodBody(ReadRepoFile(PageVm), "public async Task<bool> ReuseAsync(ItemCardViewModel vm)");
        var branch = body.IndexOf("IsClipboardImageRow", StringComparison.Ordinal);
        var reads = body.IndexOf("vm.Description", StringComparison.Ordinal);
        Assert.True(branch >= 0, "ReuseAsync 里没有图片行的分流");
        Assert.True(reads > branch, "分流必须在读 Description 之前");
        Assert.Contains("return await ReuseImageAsync(vm);", body);
    }

    [Fact]
    public void EchoIsRegisteredBeforeTheImageIsHandedToTheSystem()
    {
        // 这是整条链上唯一"顺序即正确性"的地方：WM_CLIPBOARDUPDATE 在 SetContent 之后就到了，
        // 登记晚一步，那一次通知就变成历史里多出来的一条自回声（而且每点一次多一条）。
        var body = MethodBody(ReadRepoFile(PageVm), "private async Task<bool> ReuseImageAsync(ItemCardViewModel vm)");
        var note = body.IndexOf("NoteClipboardOwnImageWrite", StringComparison.Ordinal);
        var setContent = body.IndexOf("Clipboard.SetContent", StringComparison.Ordinal);
        Assert.True(note >= 0 && setContent >= 0, "要么没登记回声，要么根本没写剪贴板");
        Assert.True(note < setContent, "回声登记必须早于 SetContent：晚一步就来不及挡那一帧通知");

        // 通货是位图<b>数据</b>，不是字符串、也不是文件位置：
        // SetText 会让图片行"复制成功、粘出来是空的"；而交 file URI 会被系统按"复制了一个文件"呈现，
        // 目标程序粘出来就是一串路径（真机坏法），采集侧还会把那行路径再记成一条新历史。
        Assert.Contains("SetBitmap", body);
        Assert.Contains("CreateFromStream", body);
        Assert.DoesNotContain("CreateFromUri", body);
        Assert.DoesNotContain("SetStorageItems", body);            // 那颗才是"复制文件"
        Assert.DoesNotContain("SetText", body);
        Assert.Contains("Clipboard.Flush()", body);              // 不 Flush，窗口一关内容就没了

        // 像素从我们自己那份 PNG 解出来（登记的是身份哈希的原料，不是文件字节），而且<b>只问那一处入口</b>：
        // UI 自己再读一次盘、再解一次码，就会与采集侧算出两种哈希（回声当场失效）。
        Assert.Contains("ClipboardImageStore.TryReadEntryImage", body);
        Assert.DoesNotContain("TryDecodePng", body);
        Assert.Contains("Task.Run", body);             // 读盘 + 解码整段离 UI 线程
    }

    [Fact]
    public void MissingFilesAreRefusedInTheirOwnWords()
    {
        // 报坏消息的那句要给出口（P-54 同口径）：只说"复制不回去"，用户只能去猜该怎么办。
        // 批次 SI 把那句整句搬进 ClipboardPolicy.DescribeMissingImageForReuse ⇒ 读数改由契约测逐字钉
        // （ClipboardMissingImageTextTests）；这里只守"这一处问了它、且没在原地留一份抄本"。
        var body = MethodBody(ReadRepoFile(PageVm), "private async Task<bool> ReuseImageAsync(ItemCardViewModel vm)");
        Assert.Contains("ClipboardPolicy.DescribeMissingImageForReuse()", body);
        Assert.DoesNotContain("不在本机", Code(body));
        // 先判文件在不在再去读它：漏了这一步，症状是一句"图片复制失败：找不到文件"式的系统原话。
        Assert.Contains("File.Exists", body);
        Assert.True(body.IndexOf("File.Exists", StringComparison.Ordinal)
                    < body.IndexOf("TryReadEntryImage", StringComparison.Ordinal));
        Assert.DoesNotContain("ReadAllBytes", body);   // 读盘留在入口那一处，UI 不自己开文件
    }

    [Fact]
    public void PixelIdentityIsCarriedThroughTheAppEntry()
    {
        // App 上那颗转发必须是<b>像素</b>重载：把它接成文件字节，登记与采集算的就不是同一种哈希，
        // 编译得过、看起来也在登记，真机上却每条都多一条回声。
        var body = MethodBody(ReadRepoFile(App), "public static void NoteClipboardOwnImageWrite(byte[]? pixels)");
        Assert.Contains("NoteOwnWrite(pixels)", body);
        Assert.DoesNotContain("FileName", body);
    }

    // ────────── 缩略图：一张小图的成本出处 ──────────

    [Fact]
    public void ThumbnailDecodeSizeComesFromTheCoreConstant()
    {
        var body = MethodBody(ReadRepoFile(CardVm),
            "private (BitmapImage? Thumb, bool FileAbsent) BuildClipboardThumb(bool flaggedMissing)");
        Assert.Contains("DecodePixelWidth = ClipAssets.ThumbnailMaxEdge", body);
        // 反向钉住写死的数字：Core 那个长边改档时，字面量会安静地留在原地，而"回退解码主图"
        // 正是唯一真正需要它的那条路（4K 主图按原尺寸解码一张就是几十 MB，列表一次摆几百张）。
        Assert.DoesNotContain("DecodePixelWidth = 1", body);
        // 优先缩略图、缺了才回退主图：两个来源都必须在名册里，且缩略图排在前面。
        Assert.True(body.IndexOf("ThumbName", StringComparison.Ordinal)
                    < body.IndexOf("MainName", StringComparison.Ordinal));
        Assert.Equal(2, Count(body, "File.Exists"));             // 两个来源各 stat 一次，谁都不许裸用
        Assert.Contains("UriSource = new Uri(source)", body);
    }

    [Fact]
    public void ThumbnailIsBuiltOncePerCardNotOncePerBinding()
    {
        var vm = ReadRepoFile(CardVm);
        // 只有构造函数那一次调用。绑定时建 = 每解析一次就多一个解码器实例（滚动变成反复新建同一张小图）。
        Assert.Equal(1, Count(vm, "BuildClipboardThumb(flaggedMissing);"));
        var ctor = MethodBody(vm, "public ItemCardViewModel(Item item)");
        Assert.Contains("BuildClipboardThumb(flaggedMissing)", ctor);
        // 判据一次解 extra_json 就够：历史页几百张卡片，三条判据各解一遍就是白付两次。
        Assert.Equal(1, Count(ctor, "ClipboardEntry.IsImageOf("));
        Assert.Equal(1, Count(ctor, "ClipboardEntry.IsMissing("));
    }

    // ────────── 那一格出现/说话的判据 ──────────

    [Fact]
    public void CardXamlShowsTheThumbAndExplainsItsAbsence()
    {
        var xaml = ReadRepoFile(CardXaml);
        // 逗号是必需的：少写它，这颗旗标会把下面那条 …MissingText 一起数进来（前缀相撞＝计数凭空多一）。
        Assert.Equal(1, Count(xaml, "ViewModel.ClipboardImageMissing,"));
        Assert.Equal(1, Count(xaml, "ViewModel.HasClipboardThumb"));
        Assert.Equal(1, Count(xaml, "ViewModel.ClipboardThumb"));

        // 缺失必须说出来（空着不解释＝看起来像程序坏了），并给出口。批次 SI 起句子不在 XAML 里：
        // 写死在页面上它就只属于这一格，而同一句事实还要在状态行与灰项标题上说（WM 是同一条）。
        Assert.Equal(1, Count(xaml, "ViewModel.ClipboardImageMissingText"));
        Assert.DoesNotContain("不在本机", xaml);                       // 反向钉：XAML 不许再抄一份
        // 读数（"条目仍保留，可直接删掉这一条"）改由契约测逐字钉，见 ClipboardMissingImageTextTests。

        // 有图与缺图两块不许同时出现：判据是同一个属性的两个极性，写成两个独立条件就会分岔。
        var thumbVis = xaml.Substring(xaml.IndexOf("ViewModel.HasClipboardThumb", StringComparison.Ordinal), 90);
        Assert.Contains("BoolToVis", thumbVis);
    }

    [Fact]
    public void CardBoxWidthMatchesTheDecodedEdge()
    {
        // 卡片那格的宽度与 Core 的长边是同一个决定的两处落点：分岔之后要么图被无谓缩小，要么留一块空白。
        var xaml = ReadRepoFile(CardXaml);
        var at = xaml.IndexOf("ViewModel.ClipboardThumb", StringComparison.Ordinal);
        Assert.True(at > 0);
        // 绑定写在 <Image ...> 标签<b>内部</b>，所以起点要往前找标签、终点往后找尖括号，不能从绑定处再找 <Image。
        var open = xaml.LastIndexOf("<Image", at, StringComparison.Ordinal);
        Assert.True(open >= 0, "ViewModel.ClipboardThumb 不在某枚 <Image> 标签里");
        var tag = xaml[open..xaml.IndexOf('>', at)];
        var width = Regex.Match(tag, "Width=\"(\\d+)\"");
        Assert.True(width.Success, "那格没写 Width：尺寸只能来自 Core 那个长边");
        Assert.Equal(ClipAssets.ThumbnailMaxEdge,
            int.Parse(width.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}
