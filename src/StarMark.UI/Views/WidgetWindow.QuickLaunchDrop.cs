#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Health;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——快捷启动的 OLE 拖放这一头：挂接收、给可视提示、记那三行可区分的证词、把路径交回落库出口。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetWindow
{

    // ── 快捷启动格 ──

    /// <summary>拖放接收只挂接一次（重建内容时不会重复订阅/叠加遮罩）。</summary>
    private void SetupQuickLaunchDrop()
    {
        if (_kind != WidgetKind.QuickLaunch) return;

        RootBorder.AllowDrop = true;
        RootBorder.DragOver += QuickLaunch_DragOver;
        RootBorder.Drop += QuickLaunch_Drop;
        RootBorder.DragEnter += QuickLaunch_DragEnter;
        RootBorder.DragLeave += (_, _) => SetDropHintVisible(false);

        _dropHint = new Border
        {
            Background = WidgetBrush("WidgetDropHintBrush"),
            BorderBrush = ThemeBrush.For(RootBorder.ActualTheme, "AppAccentBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "松开以添加到快捷启动",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            },
        };
        Grid.SetRowSpan(_dropHint, 2);
        if (RootBorder.Child is Grid rootGrid) rootGrid.Children.Add(_dropHint);
    }

    private void SetDropHintVisible(bool visible)
    {
        if (_dropHint is not null) _dropHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 「拖了没反应」的分水岭证词：这一行<b>记下了</b>＝拖拽消息真的送到了本进程，问题在后面的落库；
    /// 一整段日志里<b>一行都没有</b>＝手势根本没进来（跨完整性级别被 UIPI 过滤，或组件没挂上放 target）。
    /// <para>以前这两种在日志里长得完全一样——都是零行，只能靠猜，而猜错过一次（把系统拦的当成组件坏的）。
    /// 只记第一次进入：DragEnter 在悬停进出时会反复触发，要的是"通不通"这一个事实，不是流量。</para>
    /// </summary>
    private void QuickLaunch_DragEnter(object sender, DragEventArgs e)
    {
        SetDropHintVisible(true);
        if (_dropEnterLogged) return;
        _dropEnterLogged = true;
        StarLog.Info($"[拖放] 快捷启动收到 DragEnter：{DropFormats(e.DataView)}");
    }

    /// <summary>把"载荷里有什么格式"写成一短句——只有格式名，不含路径，日志里不会带出用户文件名。</summary>
    private static string DropFormats(DataPackageView v)
    {
        var has = new List<string>();
        if (v.Contains(StandardDataFormats.StorageItems)) has.Add("文件");
        if (v.Contains(StandardDataFormats.WebLink)) has.Add("网页");
        if (v.Contains(StandardDataFormats.ApplicationLink)) has.Add("应用");
        if (v.Contains(StandardDataFormats.Text)) has.Add("文本");
        return has.Count > 0 ? "格式[" + string.Join("/", has) + "]" : "格式[空]";
    }

    private async void QuickLaunch_DragOver(object sender, DragEventArgs e)
    {
        var v = e.DataView;
        var ok = v.Contains(StandardDataFormats.WebLink)
                 || v.Contains(StandardDataFormats.ApplicationLink)
                 || v.Contains(StandardDataFormats.StorageItems)
                 || v.Contains(StandardDataFormats.Text);
        e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "添加到快捷启动";
        SetDropHintVisible(ok);
        await Task.CompletedTask;
    }

    private async void QuickLaunch_Drop(object sender, DragEventArgs e)
    {
        SetDropHintVisible(false);
        try
        {
            var v = e.DataView;

            if (v.Contains(StandardDataFormats.StorageItems))
            {
                // 一次拖入可能有几十项：先只收集，再交 AddPathsToLauncherAsync 整批登记 + 整批添加。
                // 逐项 await 的话，每一项都要开一次库、并把 widgets.json 整档读一遍写一遍（见 AddLinksAsync）。
                // 那条批处理与「选择文件」出口共用同一份（触发器2「拖入即入库」＝只写索引，绝不动磁盘文件）。
                var items = await v.GetStorageItemsAsync();
                var paths = new List<(string? Title, string Path)>();
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Path)) continue;
                    paths.Add((item.Name, item.Path));
                }
                if (paths.Count == 0)
                {
                    // 收到了 Drop 却一个路径都拿不到＝载荷形状与预期不同（远端 shell 扩展、占位符之类），
                    // 这种情况必须留下痕迹：否则它与"其实落库成功了但列表没变"在日志里一模一样。
                    StarLog.Warn($"[拖放] 快捷启动 Drop：{DropFormats(v)}，但 {items.Count} 项都没有可用路径，未落库。");
                    return;
                }
                // 「收到几项」与「净增几条」分开记：拖进来的东西早已在列表里时返回 0，
                // 界面上确实"什么都没发生"。没有这一行的话，那种正常去重和"落库失败"长得一模一样。
                var added = await _manager.AddPathsToLauncherAsync(_instanceId, paths);
                StarLog.Info($"[拖放] 快捷启动 Drop：{DropFormats(v)}，收到 {paths.Count} 项，净新增入口 {added} 条"
                             + (added == 0 ? "（这些都是已存在的入口，所以列表没变）" : ""));
            }
            else if (v.Contains(StandardDataFormats.WebLink))
            {
                var uri = await v.GetWebLinkAsync();
                await _manager.AddLinkAsync(_instanceId, uri.Host, uri.AbsoluteUri);
            }
            else if (v.Contains(StandardDataFormats.ApplicationLink))
            {
                var uri = await v.GetApplicationLinkAsync();
                await _manager.AddLinkAsync(_instanceId, uri.Host, uri.AbsoluteUri);
            }
            else if (v.Contains(StandardDataFormats.Text))
            {
                var text = (await v.GetTextAsync()).Trim();
                if (QuickLaunchWidgetViewModel.TryParseUri(text, out var uri) && uri is not null)
                    await _manager.AddLinkAsync(_instanceId,
                        // 文本拖进来的地址同样会被 new Uri() 规范化成 percent 编码 ⇒ 默认标题走判据（先问磁盘）。
                        uri.IsFile ? System.IO.Path.GetFileName(
                            StarMark.Abstractions.LocalFileIdentity.PreferredPathFromUri(uri.AbsoluteUri,
                                p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p))) : uri.Host,
                        uri.AbsoluteUri);
            }

            // 新增后 WidgetManager 触发 LinksChanged，QuickLaunchWidget 订阅后增量刷新 Links（R3）。
        }
        catch (Exception ex)
        {
            StarLog.Error("拖放添加快捷入口失败", ex);
        }
    }

    // 以下快捷启动格的手动构建方法（BuildQuickLaunch / LinkRow / ReloadLinks /
    // LoadPinnedAsync / AddLinkForm / ToggleAddLinkForm / SectionHeader / EmptyHint /
    // TryParseUri / OnLinksChanged / RebuildQuickLaunch）已迁移至 QuickLaunchWidget
    // （XAML + ViewModel + ItemsRepeater，R1 试点）；增量刷新由 LinksChanged 驱动。

    // 待办组件已迁移至 TodoWidget（XAML + ViewModel + ItemsRepeater，R3 收尾），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildTodo / ToggleTodo / DeleteTodo。

    // 随记组件已迁移至 QuickNoteWidget（XAML + ViewModel + ItemsRepeater，R3 收尾），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildQuickNote / DeleteNote。

    // 时钟组件已迁移至 ClockWidget（XAML + ViewModel，R3 收尾）：手工构建与每秒定时器
    // 均迁入组件内部，本类只在 Reveal / HideTemporary / 可见性变化 / 关闭时
    // 通过 IWidgetTicker（_ticker）启停刷新——不再按 WidgetKind 分支。

    // ── 快捷搜索 ──

    // 搜索组件已迁移至 SearchWidget（XAML + ViewModel + 标签 chip 多选，R2 试点），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildSearch / ActivateSearchBox。
}
