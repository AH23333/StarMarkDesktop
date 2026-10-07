#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.I18n;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——桌面组件与布局这一头：组件行、布局行、全部显示/隐藏、应用与删除快照。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    /// <summary>组件实例增删后：延后一帧安全重建行（避免命中正在测量的元素）。</summary>
    private void OnInstancesChanged()
        => DispatcherQueue.TryEnqueue(BuildWidgetRows);

    /// <summary>布局增删后：布局列表与其快捷键行都要跟着变。</summary>
    private void OnLayoutsChanged()
        => DispatcherQueue.TryEnqueue(() => { BuildLayoutRows(); BuildHotkeyRows(); });

    /// <summary>
    /// 「桌面组件」卡片：一种组件一个可折叠 <see cref="Expander"/>（与「快捷键」页同形状）。
    /// <para>
    /// 此前是把 12 种类型连同各自的全部实例一次性铺开：类型越加越多，这张卡片越长，
    /// 而用户绝大多数时候只想找某一类。现在标题上直接写"已添加 N 个 / 未添加"，
    /// 展开后才看到「添加组件」与各实例的显示 / 移除。
    /// </para>
    /// </summary>
    private void BuildWidgetRows()
    {
        var mgr = WidgetManager();
        if (mgr is null || WidgetRows is null) return;

        WidgetRows.Children.Clear();
        var instances = mgr.Instances;

        foreach (var kind in WidgetStorage.AllKinds)
        {
            var kindInstances = instances.Where(i => i.Kind == kind).ToList();

            var content = new StackPanel { Spacing = 2 };

            // 这一族由 code-behind 现建，字一律<b>挂键</b>而不是写成品串（批次 WI-35）：
            // 挂进 LocText 那棵树之后，换语言那一刻这些行不用重建也跟着改口；写死串就把语言钉在建行那一刻。
            var addBtn = new Button
            {
                Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                Padding = new Thickness(10, 3, 10, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            LocText.SetKey(addBtn, "settings.widgets.addRow");
            var captured = kind;
            addBtn.Click += (_, _) => _ = mgr.AddInstanceAsync(captured);
            content.Children.Add(addBtn);

            // 该类型每个实例一行：显示 / 移除
            for (var idx = 0; idx < kindInstances.Count; idx++)
            {
                var inst = kindInstances[idx];
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.8,
                };
                // 一颗骨架两个槽：置顶那半句在哪种语言里都贴着序号，所以它是实参而不是第二颗键
                //（拆成两句就得在代码里拼分隔符＝P-171 那个形状）。
                LocText.SetKey(label, "settings.widgets.instanceLine", idx + 1,
                    inst.Topmost ? Locale.T("settings.widgets.instanceTopmost") : string.Empty);
                var id = inst.Id;
                var showBtn = new Button
                {
                    Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                LocText.SetKey(showBtn, "settings.widgets.showOne");
                showBtn.Click += (_, _) => _ = mgr.ShowAsync(id);
                var removeBtn = new Button
                {
                    Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                LocText.SetKey(removeBtn, "settings.widgets.removeOne");
                removeBtn.Click += (_, _) => _ = mgr.RemoveAsync(id);

                Grid.SetColumn(label, 0);
                Grid.SetColumn(showBtn, 1);
                Grid.SetColumn(removeBtn, 2);
                row.Children.Add(label);
                row.Children.Add(showBtn);
                row.Children.Add(removeBtn);
                content.Children.Add(row);
            }

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black),
            };
            // 类型名早就有键（组件注册表那颗 TitleKey，托盘子菜单在用）：这里挂<b>那颗键</b>而不是
            // 它渲染出来的串——现取的成品串会把语言钉在建行那一刻，而这一行会一直摆在屏幕上。
            LocText.SetKey(title, WidgetStorage.KindTitleKey(kind));
            header.Children.Add(title);
            var counted = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.7,
                Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 没加与加了 N 颗是两种话，也是两种形状：一颗不带实参、一颗带，⛔ 不许合成一句"已添加 0 个"。
            if (kindInstances.Count == 0) LocText.SetKey(counted, "settings.widgets.noneAdded");
            else LocText.SetKey(counted, "settings.widgets.addedCount", kindInstances.Count);
            header.Children.Add(counted);

            WidgetRows.Children.Add(new Expander
            {
                Header = header,
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4),
                // 一律收起：计数已写在标题上，展开后才出现实例行——这正是"不要始终显示所有实例"的落点。
                IsExpanded = false,
            });
        }
    }

    private void WidgetShowAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.ShowAllAsync();

    private void WidgetHideAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.HideAllAsync();

    // ==================== 布局方案 ====================

    private void BuildLayoutRows()
    {
        LayoutRowsItems.Clear();
        if (WidgetManager() is { } mgr)
            foreach (var l in mgr.GetLayouts())
                LayoutRowsItems.Add(new WidgetLayoutRow { Id = l.Id, Name = l.Name, Summary = l.Summary });
        RaisePropertyChanged(nameof(HasNoLayouts));
    }

    private async void ApplyLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && WidgetManager() is { } mgr)
            await mgr.ApplyLayoutAsync(id);
    }

    private async void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (WidgetManager() is not { } mgr) return;

        var row = LayoutRowsItems.FirstOrDefault(r => r.Id == id);
        // 统一走外部居中窗口（非 ContentDialog）：按用户主题着色、可拖动、不可重复。
        // 三句都在<b>弹出这一刻</b>现取（批次 WI-35）：一次性的框没有"后来换语言"的时刻，
        // 而按钮那两颗复用对话框那对键，不在这一栏里再造第二个出处。
        var confirm = await CenteredDialog.ConfirmAsync(
            Locale.T("settings.widgets.deleteLayoutTitle"),
            Locale.Fmt("settings.widgets.deleteLayoutBody", row?.Name ?? id),
            primaryText: Locale.T("dialog.delete"), cancelText: Locale.T("dialog.cancel"),
            owner: App.MainWindow, dedupeKey: $"deletelayout:{id}");
        if (!confirm) return;
        await mgr.DeleteLayoutAsync(id);
    }

    // ==================== 快捷键录制 ====================
    // 组合键的修饰键状态由底层钩子累计维护（KeyboardHookService.CurrentModifiers），
    // 因此 Ctrl/Alt/Shift/Win 任意组合都能录到，且不依赖 XAML 焦点。
    //
    // 录制规则（按用户要求）：
    // · 按钮未设置时只显示「未设置」，点击后**清空按钮文字**（保留按钮样式、不高亮）进入录制态；
    // · 依次按下按键，最多录 3 个键（修饰键 + 主键）：录到第 3 个键自动结束；
    // · 只录了 1~2 个键时不自动结束，需按 Esc 表示录入完成；一个键都没录时按 Esc = 取消；
    // · 录制结束**不立即生效**（避免录完「隐藏主界面」当场就把主界面藏了），
    //   必须点「保存快捷键」（或离开本页自动保存）后才注册生效；
    // · 点击已设置的按钮 = 重新录制（清空文字后再录）；
    // · **点已设置的按钮后直接按 Esc（未录入任何键）即表示「清除该快捷键」**；
    //   点「未设置」按钮后按 Esc 仅取消（无改动）；
    // · Backspace / Delete 同样可清除该动作的绑定（均需保存后生效）。

    /// <summary>一条快捷键最多允许几个按键（修饰键 + 主键一起算）。</summary>
    private const int MaxHotkeyKeys = 3;

    private const string UnsetText = "未设置";
}
