#nullable enable
using System;
using Microsoft.UI.Xaml;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Guide;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——「使用说明」那一栏（批次 VC-3）。
/// <para>这里<b>一颗文字都不写</b>：正文只在 <see cref="PageGuides"/> 一处，顶栏「？」与首启引导说的
/// 是同一批话。第二份文字的下场就是"三处说法不一样，用户不知道哪句作数"（#123 那一族）。</para>
/// </summary>
public sealed partial class SettingsPage
{
    private void BuildGuideTab()
    {
        GuideTabPanel.Children.Clear();
        foreach (var page in PageGuides.InOrder()) GuideUi.AppendPanel(GuideTabPanel, page);
        // 按键这一节从绑定表现场渲染：用户改过键之后这一栏要跟着改，抄一份默认键就是抄一份假话。
        GuideUi.AppendPanel(GuideTabPanel, Locale.T("guide.hotkey.section"), GuideUi.HotkeyHints(new SettingsStore()));
    }

    /// <summary>
    /// 重看那份引导。<b>看完仍然记"已看过"</b>：这一颗是"再看一遍"，不是"取消一次性"——
    /// 抹掉记号会让下次启动又弹一次，而用户刚刚才亲手把它关掉过。
    /// </summary>
    private async void ReplayGuideButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await GuideUi.ShowFirstRunAsync(XamlRoot, new SettingsStore(), () => SelectTab(PageGuides.SettingsTabKey));
        }
        catch (Exception ex)
        {
            StarLog.Error("重看首启引导失败", ex);
        }
    }
}
