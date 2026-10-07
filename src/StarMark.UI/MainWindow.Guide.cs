#nullable enable
using System;
using System.Threading.Tasks;
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
    /// 首启那份引导。<b>没看过才弹，弹失败不记"看过"</b>：对话框起不来时下次启动会再给一次，
    /// 而不是把用户永久留在"没见过引导"的状态里。
    /// <para><b>为什么要等 <c>RootGrid</c> 真的进了呈现树</b>：构造里排在导航之后调用，可那一刻
    /// "元素树挂上了没有"与"XamlRoot 拿到没有"是两件事。直接把对话框交给 <c>ShowAsync</c>，
    /// 冷启动（脚本／命令行拉起、窗口还没呈现）有两种坏法：XamlRoot 为 null 时它抛
    /// "这个元素没有 XamlRoot"；XamlRoot 有了而树还没呈现时它<b>立刻返回</b>——
    /// 于是"已看过"被记上，而用户从头到尾没看见引导。两种都是当着用户把一次说没了（P-54）。</para>
    /// <para><see cref="FrameworkElement.Loaded"/> 才是"可以往上盖东西"的那一刻；
    /// 只认第一次（<paramref name="_firstRunGuideShown"/> 兜住重复触发），不抹"已看过"。</para>
    /// </summary>
    private void MaybeShowFirstRunGuide()
    {
        if (_settings.LoadGuideShown()) return;
        if (RootGrid.IsLoaded) ShowFirstRunGuideOnce();
        else RootGrid.Loaded += OnFirstRunGuideRootLoaded;
    }

    private bool _firstRunGuideShown;

    private void OnFirstRunGuideRootLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnFirstRunGuideRootLoaded;
        ShowFirstRunGuideOnce();
    }

    private void ShowFirstRunGuideOnce()
    {
        if (_firstRunGuideShown) return;
        _firstRunGuideShown = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var root = RootGrid.XamlRoot;
                if (root is null)
                {
                    StarLog.Warn("首启引导没弹出来：窗口根没有 XamlRoot（没记「已看过」，下次启动再给一次）");
                    _firstRunGuideShown = false;
                    return;
                }
                await GuideUi.ShowFirstRunAsync(root, _settings, OpenFullGuide);
            }
            catch (Exception ex)
            {
                _firstRunGuideShown = false;
                StarLog.Error("首启引导没弹出来（没记「已看过」，下次启动再给一次）", ex);
            }
        });
    }

    private void OpenFullGuide() => OpenSettingsPage(PageGuides.SettingsTabKey);

    private Button GuideJumpButton()
    {
        var button = new Button
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // 复用首启向导最后那一步那颗键（同一句话，⛔ 不起第二个键名）；挂键而不是写中文串，
        // 否则英文界面上这一颗就是浮层里唯一没翻的一项。
        LocText.SetKey(button, "guide.primary");
        button.Click += (_, _) => OpenFullGuide();
        return button;
    }
}
