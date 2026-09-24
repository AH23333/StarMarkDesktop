#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Controls;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 主窗「RSS」页：一个订阅源＝一个<b>可展开/收起的文件夹</b>（样式照「文件夹」页，用户裁决）。
/// <para>
/// 为什么条目区在代码里构建、不用 ItemsRepeater：容器会把 <see cref="ItemCard"/> 压扁（文件夹页早已踩过），
/// 而"展开时才建卡片"是这一页能同时挂几十个源的前提——
/// 上一版一次性把所有源的全部条目平铺出来，既滚不到底也收不回去（用户原话"文件夹只是个摆设"）。
/// </para>
/// <para>
/// <b>默认全部收起</b>：进入这一页看到的是"哪些源、各抓到几条、哪个坏了"，
/// 要看内容才点开那一个源。抓取仍是一次「刷新」跑完所有启用的源（那是那个按钮的语义）。
/// </para>
/// </summary>
public sealed partial class RssPage : Page
{
    public RssPageViewModel ViewModel { get; }

    /// <summary>展开状态按源 id 记住：刷新/换源之后，用户已经点开的那几个不该被收回去。</summary>
    private readonly HashSet<int> _expanded = new();

    /// <summary>每个源这一页已经渲染出多少条（「展开更多」之后重载不至于回到默认条数）。</summary>
    private readonly Dictionary<int, int> _shownBySource = new();

    public RssPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<RssPageViewModel>();
        // 主题切换会改变代码构建处的画笔解析，重建一次才能刷新颜色（与文件夹页同一处理）
        ActualThemeChanged += (_, _) => RebuildSources();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 订阅/退订成对：单例 VM 的事件若不在离开时退订，死页会持续收到重建事件（泄漏 + 操作已分离的元素）
        ViewModel.StructureChanged += RebuildSources;
        ViewModel.ReloadSources();
        RebuildSources();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.StructureChanged -= RebuildSources;
        ViewModel.CancelLoading();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RefreshAsync();

    private void GoSettings_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("settings");

    /// <summary>点标题＝直接跳文章（这一页不做预览）。打不开的原因由 VM 写回状态行。</summary>
    private void OpenRow(ItemCardViewModel vm) => _ = ViewModel.OpenAsync(vm);

    // ===== 手风琴 =====

    private void RebuildSources()
    {
        SourceRoot.Children.Clear();
        foreach (var section in ViewModel.Sections) SourceRoot.Children.Add(BuildSource(section));
    }

    /// <summary>
    /// 一个源＝头部（永远在）+ 体（展开后才建）。重活（建卡片）推到下一消息帧：
    /// 先让箭头与展开动画立刻响应，否则点一下要等几十张卡片建完，手感就是"卡"。
    /// </summary>
    private StackPanel BuildSource(RssSourceSection section)
    {
        var id = section.Config.Id;
        var body = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
        var itemsPanel = new StackPanel { Spacing = 6 };
        Button? moreButton = null;
        var built = false;

        var header = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 4),
            Content = HeaderContent(section, false),
        };
        header.Click += (_, _) =>
        {
            if (body.Visibility == Visibility.Visible) Collapse();
            else Expand();
        };

        void Expand()
        {
            body.Visibility = Visibility.Visible;
            header.Content = HeaderContent(section, true);
            _expanded.Add(id);
            DispatcherQueue.TryEnqueue(BuildOnce);
        }

        void Collapse()
        {
            body.Visibility = Visibility.Collapsed;
            header.Content = HeaderContent(section, false);
            _expanded.Remove(id);
            _shownBySource.Remove(id);
        }

        void BuildOnce()
        {
            if (built) return;
            built = true;
            body.Children.Add(itemsPanel);
            _shownBySource[id] = _shownBySource.TryGetValue(id, out var kept) && kept > 0
                ? Math.Min(kept, section.Rows.Count)
                : Math.Min(RssPageViewModel.InitialRows, section.Rows.Count);
            DispatcherQueue.TryEnqueue(RenderRows);
        }

        void RenderRows()
        {
            itemsPanel.Children.Clear();
            var shown = _shownBySource.TryGetValue(id, out var saved) ? saved : section.Rows.Count;
            foreach (var vm in section.Rows.Take(shown))
            {
                var card = new ItemCard { ViewModel = vm, SuppressTags = true };
                card.OpenRequested += (sender, _) =>
                {
                    if (sender is ItemCard { ViewModel: { } row }) OpenRow(row);
                };
                itemsPanel.Children.Add(card);
            }

            var remaining = section.Rows.Count - shown;
            if (remaining <= 0)
            {
                if (moreButton is not null) { body.Children.Remove(moreButton); moreButton = null; }
                return;
            }
            moreButton ??= NewMoreButton();
            moreButton.Content = $"展开更多（剩余 {remaining} 条）";
            if (!body.Children.Contains(moreButton)) body.Children.Add(moreButton);
        }

        Button NewMoreButton()
        {
            var button = new Button
            {
                Margin = new Thickness(6, 8, 0, 4),
                Padding = new Thickness(12, 4, 12, 4),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                // 仅 Style 查找（非画笔），不受主题冻结影响
                Style = (Style)Application.Current.Resources["SecondaryButton"],
            };
            button.Click += (_, _) =>
            {
                _shownBySource[id] = Math.Min(section.Rows.Count,
                    (_shownBySource.TryGetValue(id, out var shown) ? shown : 0) + RssPageViewModel.MoreStep);
                DispatcherQueue.TryEnqueue(RenderRows);
            };
            return button;
        }

        var whole = new StackPanel { Spacing = 2 };
        whole.Children.Add(header);
        whole.Children.Add(body);
        // 重建前就已经是展开着的（刷新会走这里），直接展开一次，别让用户看到"点开又自己合上"
        if (_expanded.Contains(id)) Expand();
        return whole;
    }

    /// <summary>
    /// 头部那一行：箭头 + 📁 + 源名 +（条数）+ 这一轮的状态。
    /// <b>状态必须写在标题上</b>——"这个文件夹是空的""这个源坏了""这个源被停用了"三件事
    /// 若都表现为"点开什么都没有"，用户就只能一个个点开去试。
    /// </summary>
    private StackPanel HeaderContent(RssSourceSection section, bool expanded)
    {
        var muted = ThemeBrush.For(ActualTheme, "AppMutedBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon
        {
            Glyph = expanded ? "\uE70D" : "\uE76C",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = muted,
        });
        row.Children.Add(new TextBlock { Text = "📁", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = section.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        row.Children.Add(new TextBlock
        {
            Text = $"({section.Rows.Count})",
            FontSize = 11,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = section.Status,
            FontSize = 11,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        ToolTipService.SetToolTip(row, $"{section.Url}　收藏会放进「{section.FolderPath}」");
        return row;
    }
}
