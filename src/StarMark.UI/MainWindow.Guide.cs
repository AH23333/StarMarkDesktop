#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Guide;
using StarMark.UI.Helpers;

namespace StarMark.UI;

/// <summary>
/// MainWindow 的这一段——说明的接线（批次 VC-3）：顶栏「？」说当前这一页，首启那份引导只给一次。
/// <para>文字与渲染都不在这里（<see cref="PageGuides"/> 与 <see cref="GuideUi"/>）：这一处只负责
/// "此刻在哪一页"与"什么时候该弹"。</para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// 浮层内容<b>在打开那一刻现取</b>。不做"切页时顺手刷新一次"的缓存：那种写法一旦漏掉某个切页入口
    /// （搜索态、深链、托盘呼出后的回跳），「？」就会说出上一页的话，而这正是它唯一不能犯的错。
    /// </summary>
    private void GuideFlyout_Opening(object sender, object e)
    {
        GuidePanel.Children.Clear();
        GuideUi.AppendPanel(GuidePanel, PageGuides.ForOrSaySo(ViewModel.CurrentPageTag));
        GuidePanel.Children.Add(GuideJumpButton());
    }

    /// <summary>
    /// 首启那份引导。<b>没看过就弹，弹失败不记"看过"</b>：对话框起不来（例如 XamlRoot 还没就绪）时
    /// 下次启动会再给一次，而不是把用户永久留在"没见过引导"的状态里。
    /// </summary>
    private void MaybeShowFirstRunGuide()
    {
        if (_settings.LoadGuideShown()) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await GuideUi.ShowFirstRunAsync(RootGrid.XamlRoot, _settings, OpenFullGuide);
            }
            catch (Exception ex)
            {
                StarLog.Error("首启引导没弹出来（没记「已看过」，下次启动再给一次）", ex);
            }
        });
    }

    private void OpenFullGuide() => OpenSettingsPage(PageGuides.SettingsTabHeader);

    private Button GuideJumpButton()
    {
        var button = new Button
        {
            Content = "看完整使用说明",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => OpenFullGuide();
        return button;
    }
}
