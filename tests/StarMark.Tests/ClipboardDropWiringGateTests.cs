#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板格拖放（片285 / ✅P-306）的<b>接线</b>闸门。与 <see cref="TodoAttachmentWiringGateTests"/> 同族：
/// 「写入面一直在、入口没人接」不报错也不红——那张格点得开、拖得进，却没有一条接线把它递到落库出口，
/// 这种哑法只有照着源码念才查得出来，所以这里钉形状。
/// <para>分工：策略门的逐值判定（过短/敏感/空路径拒、join 形状）归 <see cref="ClipboardCaptureTests"/>；
/// 这里只问「挂没挂、交给谁、结局说不说、话在不在表里」。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class ClipboardDropWiringGateTests : IDisposable
{
    private const string WinPath = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";
    private const string DropPath = "src/StarMark.UI/Views/WidgetWindow.ClipboardGridDrop.cs";
    private const string CapturePath = "src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs";

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string Win() => SourceGate.ReadRepoPartials(WinPath);

    /// <summary>语料⛔ 把表自己算进来（否则"这颗键没人问"会被表里那一行自己满足＝仪器骗人，规则 #95/#102）。</summary>
    private static string AskedCorpus()
        => string.Join("\n", SourceGate.ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => f.Text));

    [Fact]
    public void SetupIsGatedToTheClipboardKindAndHooksTheFourEvents()
    {
        var setup = SourceGate.MethodBody(Win(), "private void SetupClipboardGridDrop(");

        // gate：不是剪贴板格、或没有仓储＝整段跳过（有接收没落库出口的窗不该存在）
        Assert.Contains("if (_kind != WidgetKind.Clipboard || _repo is null) return;", setup);
        // 四件接线一件不少（少一件＝「拖了没反应」的一种哑法）
        Assert.Contains("RootBorder.AllowDrop = true;", setup);
        Assert.Contains("RootBorder.DragEnter += ClipboardGrid_DragEnter;", setup);
        Assert.Contains("RootBorder.DragOver += ClipboardGrid_DragOver;", setup);
        Assert.Contains("RootBorder.Drop += ClipboardGrid_Drop;", setup);
        Assert.Contains("RootBorder.DragLeave += (_, _) => SetDropHintVisible(false);", setup);
        // 遮罩那句挂键（树里元素⛔ 直写 Text：语言冻在创建那一刻，坑表 #328 同族）
        Assert.Contains("LocText.SetKey(hintText, \"widget.clipdrop.hint\");", setup);
        // 回执槽与遮罩同一棵根树、同一生命周期
        Assert.Contains("_dropReceipt = new Border", setup);
        Assert.Contains("rootGrid.Children.Add(_dropReceipt);", setup);
    }

    [Fact]
    public void TheConstructorWiresTheSetupRightAfterTheQuickLaunchOne()
    {
        var ctor = SourceGate.MethodBody(Win(), "public WidgetWindow(");

        Assert.Contains("SetupQuickLaunchDrop();", ctor);
        Assert.Contains("SetupClipboardGridDrop();", ctor);   // 一窗只宿一种 kind：两颗 Setup 各自 gate，互不干扰
    }

    [Fact]
    public void TheDropHandsPayloadsToTheTwoImportExitsAndSaysEveryOutcome()
    {
        var drop = SourceGate.MethodBody(Win(), "private async void ClipboardGrid_Drop(");

        // 两条进路各交给那一口（⛔ 在 handler 里另起一份判据＝两处各写一遍策略门）；
        // 乙的认领名单跟着文件那一口递进去：行落上那一刻起，这次物化的件就有主了。
        Assert.Contains("ClipboardCapture.ImportDroppedFilesAsync(repo, paths, maxEntries,", drop);
        Assert.Contains("claimedDropNames: outcome.ClaimedNames)", drop);
        Assert.Contains("ClipboardCapture.ImportDroppedTextAsync(repo, raw, maxEntries)", drop);
        // 上限落库这一刻现读（与采集那一路同口径，设置页改完下一条就生效）——文本与图片两个桶各读各的。
        Assert.Contains("LoadClipboardTextMaxEntries();", drop);
        Assert.Contains("LoadClipboardImageMaxEntries();", drop);

        // 结局各说一句：没有可用路径（与快捷启动共用那句，一句一行多主）／文件那条腿可能几行
        // （ComposeDropReceipt 排行序：没记成的先说、图片其次、路径最后）／被门拒（一处翻译，
        // 两条进路共用 ClipDropReceipt——被拒那句再在支里各问一遍＝换词漏一半）／
        // 文本不带数／写库失败（原因走数据门，不编话）。
        Assert.Contains("SayDropReceipt(LocMessage.Of(\"widget.drop.receipt.noPaths\", items.Count));", drop);
        Assert.Contains("SayDropReceipt(ComposeDropReceipt(recorded is not null, paths.Count, outcome));", drop);
        Assert.Contains("SayDropReceipt(ClipDropReceipt(recorded is not null,", drop);
        Assert.Contains("LocMessage.Of(\"widget.clipdrop.receipt.text\")));", drop);
        Assert.Contains("SayDropReceipt(LocMessage.Of(\"widget.clipdrop.receipt.failed\", LocMessage.Of(\"common.rawValue\", ex.Message)));", drop);

        var receipt = SourceGate.MethodBody(Win(), "private static LocMessage ClipDropReceipt(");
        Assert.Contains("LocMessage.Of(\"widget.clipdrop.receipt.rejected\")", receipt);

        // 证词与结局成对：留痕在日志那一侧，说话在屏幕那一侧，两边都不许只剩零行
        Assert.Contains("项都没有可用路径，未落库。", drop);
        Assert.Contains("被策略门挡下（过短或敏感形状），未落库。", drop);
        Assert.Contains("已记入历史：{recorded.Title}", drop);
    }

    /// <summary>
    /// 分流（✅P-307 第二刀）：同一份 StorageItems 里，只有流没有盘上地址的件必须被认出来
    /// 交给物化那一腿——不分流的话，虚拟件（Outlook 附件、浏览器拖出的件）会被
    /// 「没有可用路径」一句话打发掉，而用户看到的是"拖了、没反应"。
    /// </summary>
    [Fact]
    public void VirtualFilesAreSplitOffToTheirOwnLegBeforeTheFilesExit()
    {
        var drop = SourceGate.MethodBody(Win(), "private async void ClipboardGrid_Drop(");
        Assert.Contains("if (item is StorageFile file && string.IsNullOrWhiteSpace(file.Path))", drop);
        Assert.Contains("virtualFiles.Add(file);", drop);
        Assert.Contains("await ImportVirtualFilesAsync(repo, virtualFiles, imageMaxEntries)", drop);
        // 行没落上＝这次物化的件没人认领：当场收回，不留孤儿（§3-Q6 第二类不由我们自己制造）。
        Assert.Contains("if (recorded is null && outcome.ClaimedNames.Count > 0)", drop);
        Assert.Contains("ClipboardDropFileStore.TryDelete(name);", drop);
    }

    /// <summary>
    /// MOTW 尽力标（✅P-307 第二刀）：安全加固不是记录链的一环——标不成不许让拖放失败，
    /// 返回值也<b>刻意丢弃</b>（`_ =`）：读了它就会有人想"失败时该报给谁"，那一报就报进了记录链，
    /// 于是"记没记上"与"标没标上"从两件事绞成了一件。
    /// </summary>
    [Fact]
    public void ZoneMarkingIsBestEffortAndItsFailureIsNotTheDropsFailure()
    {
        var body = SourceGate.MethodBody(Win(), "private static async Task<VirtualDropOutcome> ImportVirtualFilesAsync(");
        Assert.Contains("_ = TodoAttachmentStorage.TryMarkInternetZone(dropPath);", body);
    }

    /// <summary>
    /// 同源同目录（✅P-307 第二刀）：物化路径必须从 <c>ClipDropAssets.Folder</c> 那棵树拼——
    /// 写是 <c>ClipboardDropFileStore</c> 写的，目录、改道都问同一份。两处各 Combine 一遍，
    /// 改道（<c>FolderOverride</c>）只改一处时，路径就指不到刚写的那个件：收回与记行都会静默地对空气做事。
    /// </summary>
    [Fact]
    public void MaterializedPathsComeFromTheSameFolderTheStoreWritesTo()
    {
        var body = SourceGate.MethodBody(Win(), "private static async Task<VirtualDropOutcome> ImportVirtualFilesAsync(");
        Assert.Contains("Path.Combine(ClipDropAssets.Folder, name)", body);
    }

    [Fact]
    public void TheDragPathLeavesEvidenceEitherWay()
    {
        var enter = SourceGate.MethodBody(Win(), "private void ClipboardGrid_DragEnter(");

        // 与快捷启动同一分水岭：一行 DragEnter＝手势真进来了；零行＝根本没进来（UIPI 那一族）
        Assert.Contains("StarLog.Info($\"[拖放] 剪贴板格收到 DragEnter", enter);
        Assert.Contains("if (_dropEnterLogged) return;", enter);   // 一次拖拽只留一行：要事实，不要流量
    }

    [Fact]
    public void TheTwoImportExitsShareTheOnePolicyGateAndTheCaptureShapes()
    {
        var capture = SourceGate.Code(SourceGate.ReadRepoFile(CapturePath));

        var text = SourceGate.MethodBody(capture, "public static async Task<Item?> ImportDroppedTextAsync(");
        Assert.Contains("ClipboardPolicy.ShouldRecord(rawText, null, out var text)", text);   // 同一道门：过短/敏感拒
        Assert.Contains("ClipboardEntry.FormatText", text);
        Assert.Equal(0, SourceGate.Count(text, "ClipboardDedupe"));   // 拖进来＝显式动作，不走防重那一路

        var files = SourceGate.MethodBody(capture, "public static async Task<Item?> ImportDroppedFilesAsync(");
        Assert.Contains("string.Join(\"\\n\", usable)", files);        // 与采集帧同一口径：首行＝标题、正文＝全部路径
        Assert.Contains("ClipboardPolicy.ShouldRecord(raw, null, out var text)", files);
        Assert.Contains("ClipboardEntry.FormatFiles", files);
        Assert.Equal(0, SourceGate.Count(files, "ClipboardDedupe"));
    }

    /// <summary>「一句一行多主」的反空转＋口径钉死：那句没有可用路径的话是<b>一行</b>、被<b>两个</b>宿主问，
    /// 与 <c>common.loadFailed</c> 七主先例同形（⛔ 另立第二颗键＝一句话两个名字）。</summary>
    [Fact]
    public void TheSharedNoPathsSentenceStaysOneRowWithTwoHosts()
    {
        Assert.Equal(2, SourceGate.Count(AskedCorpus(), "\"widget.drop.receipt.noPaths\""));   // 快捷启动＋剪贴板格
        Assert.Single(UiStrings.All.Where(e => e.Zh == "拖入的 {0} 项都没有可用路径").ToArray());
    }

    [Theory]
    [InlineData("widget.clipdrop.hint")]
    [InlineData("widget.clipdrop.caption")]
    [InlineData("widget.clipdrop.receipt.text")]
    [InlineData("widget.clipdrop.receipt.files")]
    [InlineData("widget.clipdrop.receipt.rejected")]
    [InlineData("widget.clipdrop.receipt.failed")]
    [InlineData("widget.clipdrop.receipt.images")]
    [InlineData("widget.clipdrop.receipt.virtualFailed")]
    public void EveryClipDropWordIsAskedForOnceAndSaysItDifferentlyInEachLanguage(string key)
    {
        // 一颗键一个主人：语料里恰好一次（⛔ 一句字两个主人，也⛔ 没人问的死键）
        Assert.Equal(1, SourceGate.Count(AskedCorpus(), "\"" + key + "\""));
        Assert.True(UiStrings.TryGet(key, out var row), "表里没有这颗键：" + key);
        Assert.False(string.IsNullOrWhiteSpace(UiStrings.TextOf(row, AppLanguage.Chinese)));
        Assert.False(string.IsNullOrWhiteSpace(UiStrings.TextOf(row, AppLanguage.English)));
        Assert.False(string.IsNullOrWhiteSpace(UiStrings.TextOf(row, AppLanguage.Japanese)));

        var said = new System.Collections.Generic.List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var one = LocMessage.Of(key, "甲", "乙", "丙").Render();
            Assert.DoesNotContain("{0}", one, StringComparison.Ordinal);   // 填空没做＝屏幕上挂着键的形状
            said.Add(one);
        }
        Assert.Equal(3, said.Distinct(StringComparer.Ordinal).Count());    // 三语各说各的
    }

    /// <summary>口径「不用秒表」：回执不为收回自己引入定时器（⇒ 后一次覆盖前一次就是全部的「收」）。</summary>
    [Fact]
    public void TheReceiptComesWithoutAnyTimer()
    {
        var dropFile = SourceGate.ReadRepoFile(DropPath);
        Assert.Equal(0, SourceGate.Count(dropFile, "DispatcherQueueTimer"));
        Assert.Equal(0, SourceGate.Count(dropFile, "Timeline"));
    }
}
