#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Insights;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using Windows.UI;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——剪贴板历史与图片采集这一头：开关的即时生效、两个上限的合并落盘、占用与孤儿清理。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    private void ApplyClipboardHistorySwitch(bool enabled)
    {
        _settings.SaveClipboardHistoryEnabled(enabled);
        var collecting = App.ApplyClipboardHistory(enabled);
        ClipboardHistoryStatus = enabled
            ? collecting
                ? "已开始记录。密码管理器复制的内容、以及私钥 / 登录令牌 / 银行卡号形态一律不入库；"
                  + "想临时停一下，去「剪贴板」页点「暂停记录」。"
                : "开关已打开，但系统剪贴板监听窗口没建起来（原因见日志）——当前仍不会记录任何内容。"
            : "已停止记录。之前存下的历史仍在「剪贴板」页，可在那里一键清空。";
        ClipboardImageStatus = ClipboardImageStatusText(collecting, ClipboardImageEnabled);
    }

    // ===== 剪贴板图片（批次 ClipIMG-P1-1d）=====

    /// <summary>
    /// 图片采集分开关，<b>默认关</b>（决议 §4）。它与总开关是两件事：
    /// 总开关管"读不读剪贴板"，这一颗管"要不要把屏幕上的像素写成 PNG 留在机器上"。
    /// </summary>
    [ObservableProperty] private bool _clipboardImageEnabled;

    /// <summary>图片条数上限（NumberBox 的 Value 是 double，落盘时才取整并夹住）。</summary>
    [ObservableProperty] private double _clipboardImageMaxValue;

    /// <summary>文本条数上限（默认 500＝改之前的写死值，这一格只是把它变成看得见的东西）。</summary>
    [ObservableProperty] private double _clipboardTextMaxValue;

    [ObservableProperty] private string _clipboardImageStatus = string.Empty;

    /// <summary>当前图片目录的实际占用 + 按上限的预估（决议 §3-Q1 第三层：让用户"按需配置"有数字可依）。</summary>
    [ObservableProperty] private string _clipboardUsage = string.Empty;

    /// <summary>
    /// 「清理孤儿文件」能不能按。<b>只有真扫出东西才允许按</b>：扫不出东西时灰着，而旁边那行占用文字
    /// 就是它灰着的理由（"另有 N 个文件不在历史里"要么出现要么不出现，不会出现"灰着却看不出为什么"）。
    /// </summary>
    [ObservableProperty] private bool _canCleanClipboardAssets;

    /// <summary>清理那一步的结果行：删了几件、腾出多少、还有几件删不掉。<b>不许写成一句"清理完成"</b>。</summary>
    [ObservableProperty] private string _clipboardCleanupStatus = string.Empty;

    /// <summary>
    /// 手动导出是否带上剪贴板图片本体（决议 §4：<b>默认开</b>）。
    /// 它只改"下一次导出"的载体（<c>.json</c> ↔ <c>.zip</c>），不改采集、不改已有备份文件，
    /// 所以这条改动除了落盘不需要推给任何运行中的东西。
    /// </summary>
    [ObservableProperty] private bool _backupClipboardImagesEnabled;

    /// <summary>回灌初值期间不许落盘/推参数，否则每次进设置页都把默认值写回用户设置里。</summary>
    private bool _suppressClipboardImageApply;

    /// <summary>
    /// 图片那一段的状态行。<b>总开关与分开关的四种组合各有不同事实要说</b>，
    /// 尤其"分开关开着但总开关关着"这一格——用户会以为图片正在被记录，而其实一条都没存。
    /// </summary>
    private static string ClipboardImageStatusText(bool collecting, bool imageOn) => !imageOn
        ? "图片采集未开启：只记录文本与文件列表。"
        : !collecting
            ? "图片采集已打开，但剪贴板历史总开关没开（或监听没建立）——现在一条图片都不会记录。"
            : "已开启：复制到的图片会存成 PNG，并预生成 160px 缩略图。"
              + "单张超过 20 MB 或短边小于 16px 的不收；密码管理器在前台时一律不收。";

    partial void OnClipboardImageEnabledChanged(bool value)
    {
        if (_suppressClipboardImageApply) return;
        _settings.SaveClipboardImageEnabled(value);
        App.RefreshClipboardLimits();
        ClipboardImageStatus = ClipboardImageStatusText(App.IsClipboardCollecting, value);
    }

    partial void OnClipboardImageMaxValueChanged(double value)
    {
        if (_suppressClipboardImageApply) return;
        QueueClipboardLimitsSave();
    }

    partial void OnClipboardTextMaxValueChanged(double value)
    {
        if (_suppressClipboardImageApply) return;
        QueueClipboardLimitsSave();
    }

    partial void OnBackupClipboardImagesEnabledChanged(bool value)
    {
        if (_suppressClipboardImageApply) return;
        _settings.SaveBackupClipboardImagesEnabled(value);
    }

    /// <summary>两个数字框的合并窗口。400ms 是"手停下"的量级，不是"等一轮刷新"的量级。</summary>
    private const int ClipboardLimitsMergeMs = 400;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clipLimitsTimer;

    /// <summary>
    /// 上限改动<b>延后一小段再落盘并推给采集器</b>。
    /// <para><c>NumberBox</c> 每敲一位数字都会变更：立刻生效的话，"想把 200 改成 2000"这件事的中间态
    /// 是 2 → 20 → 200 → 2000，而第一下就会被夹到 10 并推给采集器——下一次复制就会按 10 条轮转，
    /// 用户为了<b>调高</b>上限反而丢了一批历史。合并窗口同时也把"敲四次数字写四次整档"收成一次。</para>
    /// <para>主窗没有队列时（设置页刚关、进程在收尾）直接落，<b>不许把改动吞在等不到的定时器里</b>。</para>
    /// </summary>
    private void QueueClipboardLimitsSave()
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            ApplyClipboardLimitsSave();
            return;
        }

        if (_clipLimitsTimer is null)
        {
            _clipLimitsTimer = queue.CreateTimer();
            _clipLimitsTimer.Interval = TimeSpan.FromMilliseconds(ClipboardLimitsMergeMs);
            _clipLimitsTimer.IsRepeating = false;
            _clipLimitsTimer.Tick += (_, _) =>
            {
                _clipLimitsTimer!.Stop();
                ApplyClipboardLimitsSave();
            };
        }
        _clipLimitsTimer.Stop();
        _clipLimitsTimer.Start();
    }

    private void ApplyClipboardLimitsSave()
    {
        _settings.SaveClipboardMaxEntries((int)ClipboardImageMaxValue, (int)ClipboardTextMaxValue);
        App.RefreshClipboardLimits();
        ClipboardImageStatus = ClipboardImageStatusText(App.IsClipboardCollecting, ClipboardImageEnabled);
    }

    /// <summary>「重新统计」按钮：改动上限或清过历史之后，数字要能就地更新，而不是让人重开设置页。</summary>
    [RelayCommand]
    private void RefreshClipboardUsage() => ComputeClipboardUsage();

    /// <summary>
    /// 算一次目录占用。<b>放在池线程</b>：几百张图时的 stat 落到 UI 线程上，
    /// 症状就是"一打开设置页卡半秒"，而这一页本来已经有好几处要读盘。
    /// <para>顺带扫一次"图有行无"（§3-Q6 的第二类）：决议要的是<b>只数不删 + 数出来的看得见的</b>，
    /// 所以这一句直接接在占用那行后面，而旁边那颗按钮就是它的出口。</para>
    /// </summary>
    private void ComputeClipboardUsage()
    {
        ClipboardUsage = "正在统计图片目录…";
        _ = Task.Run(async () =>
        {
            string text;
            bool canClean;
            try
            {
                var scan = await Task.Run(() => ClipboardAssetAudit.ScanAsync(ClipboardRepo()));
                var files = scan?.Files ?? await Task.Run(StarMark.Integrations.Clipboard.ClipboardImageStore.ListFiles);
                var footprint = StarMark.Abstractions.Clipboard.ClipAssets.Summarize(files, out var tempBytes);
                text = StarMark.Abstractions.Clipboard.ClipAssets.DescribeUsage(footprint, tempBytes)
                     + " " + StarMark.Abstractions.Clipboard.ClipAssets
                         .DescribeProjection(footprint, _settings.LoadClipboardImageMaxEntries())
                     + " 文件就放在：" + StarMark.Abstractions.Clipboard.ClipAssets.Folder;
                canClean = false;
                if (scan is { } s)
                {
                    var (orphanBytes, _) = ClipboardAssetAudit.BytesOf(s);
                    var orphanSentence = StarMark.Abstractions.Clipboard.ClipAssets
                        .DescribeOrphans(s.Result.OrphanNames.Count, orphanBytes);
                    if (orphanSentence.Length > 0) text += " " + orphanSentence;
                    canClean = s.Result.OrphanNames.Count + s.Result.TempNames.Count > 0;
                }
                else
                {
                    // 扫不出差集时既不点亮按钮，也不写成"其实一个都没有"——那两种都会把用户引向一次错误的删除决定。
                    text += " 但没能核对哪些文件已不属于历史（对账没跑成，原因见日志），所以清理按钮暂时不可用。";
                }
            }
            catch (Exception ex)
            {
                StarLog.Error("统计剪贴板图片占用失败", ex);
                text = $"没能算出图片占用（{ex.Message}）——数字缺失比编一个数好。";
                canClean = false;
            }
            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
            {
                CanCleanClipboardAssets = canClean;      // 在 UI 线程那一侧回灌，池线程不碰绑定
                ClipboardUsage = text;
            });
        });
    }

    /// <summary>
    /// 对账与清理都要问仓储。参数less 构造（DI 之外）拿不到注入的那一个，就地从容器取——
    /// <b>这里不许回退成"当没有孤儿"</b>：那会把一句"可以清掉"挂在一个永远清不掉的数字旁边。
    /// </summary>
    private StarMark.Abstractions.IItemRepository ClipboardRepo()
        => _repository ?? App.Services.GetRequiredService<StarMark.Abstractions.IItemRepository>();

    /// <summary>
    /// 「清理孤儿文件」：<b>先扫、再列给用户看、点了确认才删</b>（§3-Q6 明写"不静默删用户目录"）。
    /// <para>预览用的那份名单<b>不交给删除那一步</b>——<c>CleanAsync</c> 自己重扫一次：中间可能又有
    /// 一次"同图再复制"把某个名字认领回去，按旧名单删就是删活文件。</para>
    /// <para>结果必须分开说：删掉几件、腾出多少、还有几件删不掉（占用）——一句"清理完成"是不够的，
    /// 那 K 件确实还在那里。</para>
    /// </summary>
    [RelayCommand]
    private async Task CleanClipboardAssetsAsync()
    {
        try
        {
            var scan = await Task.Run(() => ClipboardAssetAudit.ScanAsync(ClipboardRepo()));
            if (scan is null)
            {
                ClipboardCleanupStatus = "没能核对目录与历史的差（原因见日志），一件都没动。";
                return;
            }
            var names = scan.Value.Result.OrphanNames;
            var temps = scan.Value.Result.TempNames;
            if (names.Count + temps.Count == 0)
            {
                ClipboardCleanupStatus = "现在没有需要清理的文件。";
                return;
            }

            var (orphanBytes, tempBytes) = ClipboardAssetAudit.BytesOf(scan.Value);
            var body = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = StarMark.Abstractions.Clipboard.ClipAssets
                    .CleanupConfirmBody(names, orphanBytes, temps.Count, tempBytes,
                        StarMark.Abstractions.Clipboard.ClipAssets.Folder),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            var total = names.Count + temps.Count;
            var choice = await CenteredDialog.ShowContentAsync(
                $"清理 {total} 个不用的文件", body, owner: App.MainWindow,
                dedupeKey: "clip-asset-cleanup", width: 520, height: 420,
                primaryText: $"删掉这 {total} 个文件", cancelText: "先不删");
            if (choice != CenteredDialog.HostedDialogResult.Committed)
            {
                ClipboardCleanupStatus = "没删任何东西。";
                return;
            }

            var outcome = await Task.Run(() => ClipboardAssetAudit.CleanAsync(ClipboardRepo()));
            if (outcome is null)
            {
                ClipboardCleanupStatus = "清理没跑成（原因见日志），已删掉的部分不会回头补删——可以再点一次。";
                ComputeClipboardUsage();
                return;
            }
            var o = outcome.Value;
            ClipboardCleanupStatus = o.Deleted + o.TempDeleted == 0
                ? $"一件都没删掉：{o.Failed + o.TempFailed} 个文件正被别的程序占用。它们还在名单里，可以稍后再点一次。"
                : o.Failed + o.TempFailed == 0
                    ? $"已删掉 {o.Deleted} 个不属于历史的文件"
                      + (o.TempDeleted > 0 ? $"与 {o.TempDeleted} 个临时件" : "")
                      + $"，腾出约 {StarMark.Abstractions.Clipboard.ClipAssets.DescribeBytes(o.DeletedBytes + o.TempDeletedBytes)}。"
                    : $"已删掉 {o.Deleted + o.TempDeleted} 个，但还有 {o.Failed + o.TempFailed} 个正被占用没删掉——它们仍在名单里，可以稍后再点一次。";
            ComputeClipboardUsage();
        }
        catch (Exception ex)
        {
            StarLog.Error("清理剪贴板图片孤儿文件失败", ex);
            ClipboardCleanupStatus = $"清理失败：{ex.Message}";
        }
    }
}
