#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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
            var name = HotkeyActions.DisplayName(action);
            lines.Add(bindings.TryGetValue(action, out var gesture) && gesture is not null
                ? Locale.Fmt("guide.hotkey.bound", name, HotkeyDisplay.Display(gesture))
                : Locale.Fmt("guide.hotkey.unbound", name));
        }
        if (!settings.LoadEnableGlobalHotKey())
            lines.Add(Locale.T("guide.hotkey.disabled"));
        return lines;
    }

    /// <summary>
    /// 首启那份引导＝<b>分步向导</b>（批次 WJ-1；批次 VC-3 那一版是一整屏项目符号）。
    /// <para><b>为什么要能走</b>：一整屏字什么都说了，也什么都不做——读完回到原样的界面，下一步还是不知道按哪儿。
    /// 向导每步只说一件事、配一张我们自己画的骨架示意图（<see cref="GuideArtView"/>），
    /// 再给一颗<b>真的能到那一页</b>的按钮：跳转走 <c>MainWindow.NavigateTo</c>，它本来就是那十页唯一的编程入口，
    /// 不在这里另起第二条导航路。</para>
    /// <para><b>「带我去这一页」只在有页可去的那几步出现</b>：给一步配一颗点了没反应的按钮，
    /// 比少一颗按钮坏得多（P-54 那一族）。</para>
    /// <para><b>主按钮到最后一步换成「看完整使用说明」</b>：终点即下一站，所以不需要第四颗按钮。
    /// 默认键仍停在"知道了"——一个介绍性的框不该把回车预置成跳去别的地方（这条口径从 VC-3 带来）。</para>
    /// <para><b>两个出口都把"看过"落盘、弹失败不记</b>：一次性是他定的口径，"点了跳过下次还弹"正是要避免的纠缠；
    /// 而先记后弹的话，弹不起来就永久没有了这一次。要重看，设置「使用说明」栏里有一颗按钮。</para>
    /// </summary>
    public static async Task ShowFirstRunAsync(XamlRoot? root, SettingsStore settings, Action openFullGuide)
    {
        var steps = FirstRunGuide.Steps();
        // 主题按<b>设置档折算出具体的 Light/Dark</b>，不写 ElementTheme.Default：
        // Default 会让画笔判定落到 OS 实时主题，于是「OS 深色＋应用强制浅色」时这张向导在浅底上画出近白的字。
        var theme = ThemeManager.ResolveEffectiveTheme(settings.LoadTheme());
        var ink = ThemeBrush.For(theme, "TextFillColorPrimaryBrush")
                  ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130));
        var accent = ThemeBrush.For(theme, "AccentFillColorDefaultBrush")
                     ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));

        var art = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var counter = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var pips = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var meta = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(counter, 0);
        Grid.SetColumn(pips, 1);
        meta.Children.Add(counter);
        meta.Children.Add(pips);

        var heading = new TextBlock
        {
            FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        };
        var body = new StackPanel { Spacing = 4 };
        var jump = new Button
        {
            Content = Locale.T("guide.jump"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0),
        };

        var layout = new StackPanel { Spacing = 3, MinWidth = 340, MaxWidth = 460 };
        layout.Children.Add(art);
        layout.Children.Add(meta);
        layout.Children.Add(heading);
        layout.Children.Add(body);
        layout.Children.Add(jump);

        var index = 0;
        string? pendingJump = null;

        var dialog = new ContentDialog
        {
            Title = FirstRunGuide.Title(),
            // 右侧留 16px：内容一超高就出垂直滚动条，而它压在进度点那一行上（真机截图查出来的，
            // 形状闸门看不见"被盖住"这件事）。
            Content = new ScrollViewer { Content = layout, MaxHeight = 470, Padding = new Thickness(0, 0, 16, 0) },
            // 主按钮的文字由 Render() 按当前这一步给（下一步／看完整使用说明），这里只给另外两颗。
            SecondaryButtonText = Locale.T("guide.prev"),
            CloseButtonText = Locale.T("guide.secondary"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };

        void Render()
        {
            var step = steps[index];
            art.Content = GuideArtView.Build(step.Art, ink, accent);
            counter.Text = Locale.Fmt("guide.step", index + 1, steps.Count);
            heading.Text = step.Title;
            body.Children.Clear();
            foreach (var line in step.Lines) body.Children.Add(Bullet(line));
            // 最后那几行按键从绑定表现场渲染：写进表里就是"引导说一颗键、用户改了键按了没反应"那种假话。
            if (step.Art == GuideArt.Keys)
                foreach (var hint in HotkeyHints(settings)) body.Children.Add(Bullet(hint));
            pips.Children.Clear();
            for (var i = 0; i < steps.Count; i++) pips.Children.Add(Pip(i == index ? accent : ink, i == index));
            jump.Visibility = step.JumpTag is null ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsSecondaryButtonEnabled = index > 0;
            // 走完最后一步，主按钮换成"去看完整说明"：终点即下一站，不必再多一颗按钮。
            if (index < steps.Count - 1) dialog.PrimaryButtonText = Locale.T("guide.next");
            else dialog.PrimaryButtonText = Locale.T("guide.primary");
        }

        // 前进一步／退后一步都取消对话框的默认关闭行为：那两颗按钮是"翻页"，不是"关掉我"。
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (index >= steps.Count - 1) return;
            e.Cancel = true;
            index++;
            Render();
        };
        dialog.SecondaryButtonClick += (_, e) =>
        {
            e.Cancel = true;
            if (index > 0) index--;
            Render();
        };
        jump.Click += (_, _) =>
        {
            pendingJump = steps[index].JumpTag;
            dialog.Hide();
        };

        Render();
        var result = await dialog.ShowAsync();
        settings.MarkGuideShown();
        if (pendingJump is { } tag) App.MainWindow?.NavigateTo(tag);
        else if (result == ContentDialogResult.Primary) openFullGuide();
    }

    private static TextBlock Bullet(string line) => new()
    {
        Text = "· " + line,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };

    /// <summary>进度点：当前这一步是<b>实心</b>，其余是<b>空心圈</b>。用形状而不是只用颜色区分，
    /// 是为了在高对比度主题与色弱下也读得出"走到哪儿了"。</summary>
    private static Ellipse Pip(Brush ink, bool current)
    {
        var pip = new Ellipse { Width = 8, Height = 8 };
        if (current) pip.Fill = ink;
        else { pip.Stroke = ink; pip.StrokeThickness = 1; }
        return pip;
    }
}
