#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using StarMark.Integrations.Clipboard;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——剪贴板格的 OLE 拖放这一头（✅P-306）：
/// 文件/文本拖进格＝记一条历史，那条历史立刻可以再贴出（点它复制回剪贴板）。
/// 挂接收、给可视提示、留可区分的证词、把载荷交给 <see cref="ClipboardCapture"/> 的两个拖入口。
/// <para>与 <see cref="WidgetWindow.QuickLaunchDrop.cs"/> 同族：不持有任何状态——遮罩、回执槽、
/// 证词开关全部复用主文件那几颗字段（<c>_dropHint</c>／<c>_dropReceipt</c>／<c>_dropReceiptText</c>／
/// <c>_dropEnterLogged</c>）与那一段已有的出口（<c>SetDropHintVisible</c>／<c>SayDropReceipt</c>／
/// <c>DropFormats</c>）。一窗只宿一种 kind，两套接线不会同窗共存。</para>
/// <para>口径：拖进来＝只记历史，<b>⛔ 不写系统剪贴板</b>（要贴出去由用户点那一条历史走 ReuseAsync）；
/// 也不受采集开关/Paused 限制——那是"后台顺手记"那一路的闸，拖进来是用户亲手指定的那一下，
/// 落库后 <c>DataChangeHub</c> 自动广播，格里的历史实时刷新。</para>
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
        // 剪贴板格只认两种载荷：文件（走 files 口径）与文本。其余格式收下也只会得到"被拒"那句回执，
        // 不如入口就亮 None，让系统画那道"不许放"的光标。
        var ok = v.Contains(StandardDataFormats.StorageItems) || v.Contains(StandardDataFormats.Text);
        e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
        // 拖拽跟随光标的那句是系统画的，不是树里的元素：每次 DragOver 现取词（同快捷启动那一路）。
        e.DragUIOverride.Caption = Locale.T("widget.clipdrop.caption");
        SetDropHintVisible(ok);
        await Task.CompletedTask;
    }

    /// <summary>结局 → 回执那一句：被门拒／记上了。一处翻译，文件与文本两条进路共用
    /// （<c>ReceiptFor</c> 同族：被拒那句只住在这里，两条支各问一遍＝换词漏一半）。</summary>
    private static LocMessage ClipDropReceipt(bool recorded, LocMessage success)
        => recorded ? success : LocMessage.Of("widget.clipdrop.receipt.rejected");

    private async void ClipboardGrid_Drop(object sender, DragEventArgs e)
    {
        SetDropHintVisible(false);
        try
        {
            var repo = _repo;
            if (repo is null) return;

            var v = e.DataView;
            // 上限在这一刻现读（与采集那一路同口径）：设置页刚改的值下一条就生效，没有"要重启才生效"那种事。
            var maxEntries = App.Services.GetRequiredService<SettingsStore>().LoadClipboardTextMaxEntries();

            if (v.Contains(StandardDataFormats.StorageItems))
            {
                var items = await v.GetStorageItemsAsync();
                var paths = new List<string>();
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Path)) paths.Add(item.Path);
                }
                if (paths.Count == 0)
                {
                    // 收到了 Drop 却一个路径都拿不到＝载荷形状与预期不同，必须留痕：
                    // 否则它与"落库成功但列表没变"在日志里一模一样。
                    StarLog.Warn($"[拖放] 剪贴板格 Drop：{DropFormats(v)}，但 {items.Count} 项都没有可用路径，未落库。");
                    SayDropReceipt(LocMessage.Of("widget.drop.receipt.noPaths", items.Count));
                    return;
                }
                var recorded = await ClipboardCapture.ImportDroppedFilesAsync(repo, paths, maxEntries);
                StarLog.Info(recorded is null
                    ? $"[拖放] 剪贴板格 Drop：{paths.Count} 个路径被策略门挡下（过短或敏感形状），未落库。"
                    : $"[拖放] 剪贴板格 Drop：{paths.Count} 个路径已记入历史：{recorded.Title}");
                SayDropReceipt(ClipDropReceipt(recorded is not null,
                    LocMessage.Of("widget.clipdrop.receipt.files", paths.Count)));
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
}
