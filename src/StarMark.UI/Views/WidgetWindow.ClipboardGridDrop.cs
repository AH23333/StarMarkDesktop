#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using StarMark.Integrations.Clipboard;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——剪贴板格的 OLE 拖放这一头（✅P-306 第一刀，✅P-307 第二刀扩到虚拟文件）：
/// 文件/文本拖进格＝记一条历史，那条历史立刻可以再贴出（点它复制回剪贴板）。
/// 挂接收、给可视提示、留可区分的证词、把载荷交给 <see cref="ClipboardCapture"/> 的拖入口。
/// <para>与 <see cref="WidgetWindow.QuickLaunchDrop.cs"/> 同族：不持有任何状态——遮罩、回执槽、
/// 证词开关全部复用主文件那几颗字段（<c>_dropHint</c>／<c>_dropReceipt</c>／<c>_dropReceiptText</c>／
/// <c>_dropEnterLogged</c>）与那一段已有的出口（<c>SetDropHintVisible</c>／<c>SayDropReceipt</c>／
/// <c>DropFormats</c>）。一窗只宿一种 kind，两套接线不会同窗共存。</para>
/// <para>口径：拖进来＝只记历史，<b>⛔ 不写系统剪贴板</b>（要贴出去由用户点那一条历史走 ReuseAsync）；
/// 也不受采集开关/Paused 限制——那是"后台顺手记"那一路的闸，拖进来是用户亲手指定的那一下，
/// 落库后 <c>DataChangeHub</c> 自动广播，格里的历史实时刷新。</para>
/// <para>第二刀的分流（✅P-307）：同一份 StorageItems 里，<b>有盘上地址的</b>走文件路径那一口
/// （与 CF_HDROP 采集同形）；<b>只有流没有地址的</b>（Outlook 附件、浏览器拖出的虚拟件）先进
/// <see cref="ImportVirtualFilesAsync"/>——图片字节直进图片桶（甲），其余物化进 clip-drops 再按文件记（乙）。
/// 不分流的话，虚拟件会被「没有可用路径」一句话打发掉，而用户看到的是"拖了、没反应"。</para>
/// </summary>
public sealed partial class WidgetWindow
{
    /// <summary>剪贴板格的拖放接收（✅P-306）。只挂接一次；不是剪贴板格、或没有仓储＝这扇窗根本不收拖放。</summary>
    private void SetupClipboardGridDrop()
    {
        if (_kind != WidgetKind.Clipboard || _repo is null) return;

        RootBorder.AllowDrop = true;
        RootBorder.DragOver += ClipboardGrid_DragOver;
        RootBorder.Drop += ClipboardGrid_Drop;
        RootBorder.DragEnter += ClipboardGrid_DragEnter;
        RootBorder.DragLeave += (_, _) => SetDropHintVisible(false);

        var hintText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        // 树里的元素挂键：遮罩只在构造时建一次，直写 Text 就是把语言钉在创建那一刻（坑表 #328 同族）。
        LocText.SetKey(hintText, "widget.clipdrop.hint");

        _dropHint = new Border
        {
            Background = WidgetBrush("WidgetDropHintBrush"),
            BorderBrush = ThemeBrush.For(RootBorder.ActualTheme, "AppAccentBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = hintText,
        };
        Grid.SetRowSpan(_dropHint, 2);

        // 回执槽与快捷启动那一段同一口径（P-93）：同一棵根树、同一生命周期、后一次覆盖前一次，
        // ⛔ 不为收回它引入任何定时器。
        var receiptText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        };
        _dropReceiptText = receiptText;
        _dropReceipt = new Border
        {
            Background = WidgetBrush("WidgetDropHintBrush"),
            BorderBrush = ThemeBrush.For(RootBorder.ActualTheme, "AppAccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 8, 6),
            Child = receiptText,
        };
        Grid.SetRowSpan(_dropReceipt, 2);
        if (RootBorder.Child is Grid rootGrid)
        {
            rootGrid.Children.Add(_dropHint);
            rootGrid.Children.Add(_dropReceipt);
        }
    }

    /// <summary>同快捷启动那一段的分水岭证词：一行 DragEnter＝手势真进来了，问题只可能在后面的落库；
    /// 一行都没有＝根本没进来（跨完整性级别被 UIPI 过滤那一族）。只记第一次进入，要事实不要流量。</summary>
    private void ClipboardGrid_DragEnter(object sender, DragEventArgs e)
    {
        SetDropHintVisible(true);
        if (_dropEnterLogged) return;
        _dropEnterLogged = true;
        StarLog.Info($"[拖放] 剪贴板格收到 DragEnter：{DropFormats(e.DataView)}");
    }

    private async void ClipboardGrid_DragOver(object sender, DragEventArgs e)
    {
        var v = e.DataView;
        // 剪贴板格只认两种载荷：文件（走 files 口径——有地址的真实文件与只有流的虚拟件都在 StorageItems 里）
        // 与文本。其余格式收下也只会得到"被拒"那句回执，不如入口就亮 None，让系统画那道"不许放"的光标。
        var ok = v.Contains(StandardDataFormats.StorageItems) || v.Contains(StandardDataFormats.Text);
        e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
        // 拖拽跟随光标的那句是系统画的，不是树里的元素：每次 DragOver 现取词（同快捷启动那一路）。
        e.DragUIOverride.Caption = Locale.T("widget.clipdrop.caption");
        SetDropHintVisible(ok);
        await Task.CompletedTask;
    }

    /// <summary>结局 → 回执那一句：被门拒／记上了。一处翻译，两条进路共用
    /// （<c>ReceiptFor</c> 同族：被拒那句只住在这里，两条支各问一遍＝换词漏一半）。</summary>
    private static LocMessage ClipDropReceipt(bool recorded, LocMessage success)
        => recorded ? success : LocMessage.Of("widget.clipdrop.receipt.rejected");

    /// <summary>
    /// 虚拟文件那一腿的结算单（✅P-307 第二刀）：几张进了图片桶、认领了哪些件、物化到了哪、几项没记成。
    /// 四个数各走各的回执行，不合成一个"成功数"——用户分得清"几张图"与"几个文件"，
    /// 分不清"成功 3"里有没有他最想要的那一件。
    /// </summary>
    private readonly record struct VirtualDropOutcome(
        int Images, List<string> ClaimedNames, List<string> MaterializedPaths, int Failed)
    {
        /// <summary>没有虚拟件时的占位（drop 体据此跳过整条乙腿，也不多读一次设置）。</summary>
        public static readonly VirtualDropOutcome Empty =
            new(0, new List<string>(), new List<string>(), 0);
    }

    private async void ClipboardGrid_Drop(object sender, DragEventArgs e)
    {
        SetDropHintVisible(false);
        try
        {
            var repo = _repo;
            if (repo is null) return;

            var v = e.DataView;
            // 上限在这一刻现读（与采集那一路同口径）：设置页刚改的值下一条就生效，没有"要重启才生效"那种事。
            var settings = App.Services.GetRequiredService<SettingsStore>();
            var maxEntries = settings.LoadClipboardTextMaxEntries();

            if (v.Contains(StandardDataFormats.StorageItems))
            {
                var imageMaxEntries = settings.LoadClipboardImageMaxEntries();
                var items = await v.GetStorageItemsAsync();
                // 分流（✅P-307 第二刀）：Path 非空＝盘上有地址的真实文件；Path 空但有流＝虚拟件。
                // 混在一条腿里，虚拟件会被"没有可用路径"一句话打发掉——用户看到的就是"拖了、没反应"。
                var paths = new List<string>();
                var virtualFiles = new List<StorageFile>();
                foreach (var item in items)
                {
                    if (item is StorageFile file && string.IsNullOrWhiteSpace(file.Path))
                        virtualFiles.Add(file);
                    else if (!string.IsNullOrWhiteSpace(item.Path))
                        paths.Add(item.Path);
                }
                if (paths.Count == 0 && virtualFiles.Count == 0)
                {
                    // 收到了 Drop 却一个路径都拿不到＝载荷形状与预期不同，必须留痕：
                    // 否则它与"落库成功但列表没变"在日志里一模一样。
                    StarLog.Warn($"[拖放] 剪贴板格 Drop：{DropFormats(v)}，但 {items.Count} 项都没有可用路径，未落库。");
                    SayDropReceipt(LocMessage.Of("widget.drop.receipt.noPaths", items.Count));
                    return;
                }
                var outcome = virtualFiles.Count > 0
                    ? await ImportVirtualFilesAsync(repo, virtualFiles, imageMaxEntries)
                    : VirtualDropOutcome.Empty;
                if (virtualFiles.Count > 0)
                    StarLog.Info($"[拖放] 剪贴板格 Drop：{virtualFiles.Count} 项虚拟文件，"
                        + $"图片 {outcome.Images} 张、物化 {outcome.MaterializedPaths.Count} 件、未记成 {outcome.Failed} 项。");
                // 乙物化出来的件与真实路径同一条腿进同一行（同一次拖放＝同一条历史，与 CF_HDROP 那一口同形）：
                // 真实路径在前（标题取首行，用户认得出拖的是哪件），物化件排后。
                paths.AddRange(outcome.MaterializedPaths);
                Item? recorded = null;
                if (paths.Count > 0)
                    recorded = await ClipboardCapture.ImportDroppedFilesAsync(repo, paths, maxEntries,
                        claimedDropNames: outcome.ClaimedNames);
                // 行没落上＝这次物化的件没人认领：当场收回，不留孤儿（§3-Q6 第二类不由我们自己制造）。
                // 同内容哈希同名、落库是确定性的：此前记成过的件必然已有一行认领它，这次落上只是合并回放。
                if (recorded is null && outcome.ClaimedNames.Count > 0)
                    foreach (var name in outcome.ClaimedNames)
                        ClipboardDropFileStore.TryDelete(name);
                StarLog.Info(recorded is null
                    ? $"[拖放] 剪贴板格 Drop：{paths.Count} 个路径被策略门挡下（过短或敏感形状），未落库。"
                    : $"[拖放] 剪贴板格 Drop：{paths.Count} 个路径已记入历史：{recorded.Title}");
                SayDropReceipt(ComposeDropReceipt(recorded is not null, paths.Count, outcome));
            }
            else if (v.Contains(StandardDataFormats.Text))
            {
                var raw = await v.GetTextAsync();
                var recorded = await ClipboardCapture.ImportDroppedTextAsync(repo, raw, maxEntries);
                // 只记标题（首行），正文绝不进日志——日志会把历史变成明文副本的第二份（与采集轮同一界线）。
                StarLog.Info(recorded is null
                    ? "[拖放] 剪贴板格 Drop：文本被策略门挡下（过短或敏感形状），未落库。"
                    : $"[拖放] 剪贴板格 Drop：文本已记入历史：{recorded.Title}");
                SayDropReceipt(ClipDropReceipt(recorded is not null,
                    LocMessage.Of("widget.clipdrop.receipt.text")));
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("拖放记入剪贴板历史失败", ex);
            // 落库失败也是「没记上」的一种，回执必须说原因（原因走数据门，不编话）。
            SayDropReceipt(LocMessage.Of("widget.clipdrop.receipt.failed", LocMessage.Of("common.rawValue", ex.Message)));
        }
    }

    /// <summary>
    /// 两条腿的结局 → 回执（可能几行，✅P-307 第二刀起文件那条腿不再只有一行）。
    /// 行序：没记成的先说（那是需要用户想办法的）、图片其次、路径最后——越"没动静"的越往前。
    /// <para>多行才走 <see cref="LocLines"/>：换行是布局不是语言（规则 #100，表里不收控制字符），
    /// 几行话在贴上屏幕那一刻逐行取词。单行直接交回——一行也套一层拼接，只会多一次无意义的格式化。</para>
    /// <para>行恒非空由 drop 体那句早退保证（两项都没有早退了，走到这里至少有一条腿带账）。</para>
    /// </summary>
    private static LocMessage ComposeDropReceipt(bool filesRecorded, int pathCount, VirtualDropOutcome outcome)
    {
        var lines = new List<object?>();
        if (outcome.Failed > 0)
            lines.Add(LocMessage.Of("widget.clipdrop.receipt.virtualFailed", outcome.Failed));
        if (outcome.Images > 0)
            lines.Add(LocMessage.Of("widget.clipdrop.receipt.images", outcome.Images));
        if (pathCount > 0)
            lines.Add(ClipDropReceipt(filesRecorded, LocMessage.Of("widget.clipdrop.receipt.files", pathCount)));
        return lines.Count == 1 && lines[0] is LocMessage single
            ? single
            : LocMessage.Of("common.rawValue", new LocLines(lines));
    }

    /// <summary>
    /// 虚拟文件那一腿（✅P-307 第二刀）：只有流没有盘上地址的件，读进内存后按内容分家——
    /// 解得出图片的直进图片桶（甲，<see cref="ClipboardCapture.ImportDroppedImageAsync"/>），
    /// 其余物化进 clip-drops 再按文件路径记（乙，<see cref="ClipboardDropFileStore.TryWriteAsync"/>）。
    /// 图片被门拒也落乙：拖进来是用户亲手指定的动作，能记就记，门拒的只是"当作图片记"这一种记法。
    /// <para>预算与读帽都在这一层把守：暂存预算借 <see cref="TodoVirtualFilePolicy"/> 那份判据
    /// （盘见底就不收），流按余量断流弃件——一次拖放不该把内存或系统盘吃穿。</para>
    /// <para><b>全程不设 ConfigureAwait(false)</b>：这条腿的后半程（回执、收回）要回 UI 线程，
    /// 与采集那条线程池的链路刻意不同。</para>
    /// </summary>
    private static async Task<VirtualDropOutcome> ImportVirtualFilesAsync(
        IItemRepository repo, IReadOnlyList<StorageFile> files, int imageMaxEntries)
    {
        var claimed = new List<string>();
        var materialized = new List<string>();
        int images = 0, failed = 0;
        var budget = TodoVirtualFilePolicy.ResolveStagingBudgetBytes(TryQueryDropDriveFreeBytes());
        foreach (var file in files)
        {
            try
            {
                var declared = await TryQueryDeclaredSizeAsync(file);
                if (declared > budget)
                {
                    // 余量不够就拒收这一件，但不连坐：后面的件照常问自己的账。
                    StarLog.Warn($"[拖放] 虚拟文件拒收（暂存余量 {budget} 字节，这份声明 {declared} 字节）：{file.Name}");
                    failed++;
                    continue;
                }
                using var stream = (await file.OpenReadAsync()).AsStreamForRead();
                var bytes = await TryReadCappedAsync(stream, budget);
                if (bytes is null || bytes.Length == 0)
                {
                    failed++;          // 超余量断流／读失败／空内容：回执里都是同一句"没记成"
                    continue;
                }
                budget -= bytes.LongLength;
                // 甲：解得出图片就直进图片桶。门拒（尺寸/超限）不在这层说——落乙当普通文件记。
                var (png, frame, _) = await ClipboardImageStore.TryReadBytesAsPngAsync(bytes, CancellationToken.None);
                if (png is not null
                    && await ClipboardCapture.ImportDroppedImageAsync(repo, png, frame, file.Name,
                        imageMaxEntries, CancellationToken.None) is not null)
                {
                    images++;
                    continue;
                }
                // 乙：物化进 clip-drops，认领名跟着行走（drop 体把它并进那条文件行）。
                var name = await ClipboardDropFileStore.TryWriteAsync(file.Name, bytes, CancellationToken.None);
                if (name is null)
                {
                    failed++;
                    continue;
                }
                claimed.Add(name);
                // 物化路径与写侧同源同目录：写是 ClipboardDropFileStore 写的，路径也从它那棵目录树拼——
                // 两处各 Combine 一遍，改道（FolderOverride）只改一处时路径就指不到刚写的那个件。
                materialized.Add(Path.Combine(ClipDropAssets.Folder, name));
                // MOTW 尽力标：拖出方（浏览器/邮件）给的文件按 Windows 口径是"来自不可信源"。
                // 标不成也不让这次拖放失败——那半句是安全加固，不是记录链的一环。
                if (ClipDropAssets.FullPathOf(name) is { } dropPath)
                    _ = TodoAttachmentStorage.TryMarkInternetZone(dropPath);
            }
            catch (Exception ex)
            {
                failed++;
                StarLog.Warn($"[拖放] 虚拟文件没记成（{file.Name}）：{ex.GetType().Name} {ex.Message}");
            }
        }
        return new VirtualDropOutcome(images, claimed, materialized, failed);
    }

    /// <summary>拖源报的体积（预算判据用）。取不到就当 0：判据后面还有断流那一道兜底，
    /// 而"报不出体积"不该等于"不许记"。</summary>
    private static async Task<long> TryQueryDeclaredSizeAsync(StorageFile file)
    {
        try
        {
            var props = await file.GetBasicPropertiesAsync();
            return (long)props.Size;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>把一条流读成字节，<b>超过余量即弃</b>（返回 null）。81920 一段地读：
    /// 超大件不用先整块进内存才知道装不下（同 <see cref="TodoVirtualFileMaterializer"/> 那条的量法）。</summary>
    private static async Task<byte[]?> TryReadCappedAsync(Stream stream, long cap)
    {
        try
        {
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                if (ms.Length + read > cap) return null;   // 超余量：断流弃件，别让一次拖放吃穿预算
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }
        catch (Exception)
        {
            return null;        // 读失败与超余量在回执里是同一句"没记成"，细节进不了那条行——按件数即可
        }
    }

    /// <summary>clip-drops 所在盘的剩余字节（预算判据的实参）。取不到（UNC、盘不就绪、异常）返回 null：
    /// 判据那头有 2GB 兜底预算，NULL 只是"不知道"，不是"没有"。</summary>
    private static long? TryQueryDropDriveFreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(ClipDropAssets.Folder);
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
