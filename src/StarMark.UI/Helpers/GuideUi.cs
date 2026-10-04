#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions.I18n;
using StarMark.Core.Guide;
using StarMark.Core.Hotkeys;

namespace StarMark.UI.Helpers;

/// <summary>
/// 说明的<b>渲染</b>：文字全在 <see cref="PageGuides"/>／<see cref="FirstRunGuide"/> 一处，
/// 这里只负责把它铺到三个宿主上——顶栏「？」的浮层、首启那颗对话框、设置页「使用说明」栏。
/// <para><b>为什么三个宿主共用一份渲染</b>：三处各自写一遍"标题加粗、正文换行、项目符号"，
/// 就会漂成三种观感；而内容分岔（某一处少一条）比观感不一致更坏——用户不知道哪句作数。</para>
/// </summary>
public static class GuideUi
{
    /// <summary>把一页的说明铺进面板（浮层与设置页那一栏都用它）。</summary>
    public static void AppendPanel(Panel target, string title, IReadOnlyList<string> lines)
    {
        target.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        foreach (var line in lines) target.Children.Add(Bullet(line));
    }

    public static void AppendPanel(Panel target, PageGuide guide) => AppendPanel(target, guide.Title, guide.Lines);

    public static void AppendPanel(Panel target, GuideSection section) => AppendPanel(target, section.Title, section.Lines);

    /// <summary>
    /// 首启那份引导要列的按键行。<b>按键文本不在文案里写死</b>：这里从绑定表现场渲染，
    /// 用户改过键之后这句话跟着改；总开关关着时补一句实话，而不是报一串按了没反应的键。
    /// </summary>
    public static IReadOnlyList<string> HotkeyHints(SettingsStore settings)
    {
        var bindings = settings.GetHotkeyBindings();
        var lines = new List<string>();
        foreach (var action in FirstRunGuide.HotkeyActionsToShow)
        {
            // 取不到绑定就说"没有绑定"，而不是把这一行悄悄丢掉：少一行与多一行"没绑"是两件事，
            // 前者用户永远不会知道。
            lines.Add(bindings.TryGetValue(action, out var gesture) && gesture is not null
                ? $"{HotkeyActions.DisplayName(action)}：{HotkeyDisplay.Display(gesture)}"
                : $"{HotkeyActions.DisplayName(action)}：没绑定按键（在设置「快捷键」里给一颗）");
        }
        if (!settings.LoadEnableGlobalHotKey())
            lines.Add("（全局热键现在关着：在设置「快捷键」里打开才生效。）");
        return lines;
    }

    /// <summary>
    /// 首启那份引导。<b>两个出口都把"看过"落盘</b>：一次性是用户裁决的口径，
    /// "点了跳过下次还弹"正是要避免的纠缠；真要重看，设置「使用说明」栏里有一颗按钮。
    /// </summary>
    public static async Task ShowFirstRunAsync(XamlRoot? root, SettingsStore settings, Action openFullGuide)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var step in FirstRunGuide.Steps) AppendPanel(panel, step);
        AppendPanel(panel, "这几件事的按键", HotkeyHints(settings));

        var dialog = new ContentDialog
        {
            Title = FirstRunGuide.Title,
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            PrimaryButtonText = Locale.T("guide.primary"),
            CloseButtonText = Locale.T("guide.secondary"),
            // 默认停在"知道了"：一个介绍性的框不该把回车预置成"跳去别的地方"。
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        var result = await dialog.ShowAsync();
        settings.MarkGuideShown();
        if (result == ContentDialogResult.Primary) openFullGuide();
    }

    private static TextBlock Bullet(string line) => new()
    {
        Text = "· " + line,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };
}
