#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions.I18n;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页「常规 → 界面语言」那一格（批次 WI-1）。
/// <para><b>选项在代码里生成、不在 XAML 里列</b>：语言名单只有一个出处，就是
/// <see cref="AppLanguages.Pickable"/>。在 XAML 里多写一行 <c>&lt;ComboBoxItem Content="简体中文"/&gt;</c>，
/// "这一版发几门语言"就同时住在两个地方——补日文时改了表没改这里，界面上就多出一颗点了没反应的选项。</para>
/// <para><b>选项的标签用 <see cref="AppLanguages.SelfName"/>（简体中文／English），它故意不跟着界面语言翻</b>：
/// 语言选择器是拿去认自己母语的。把「日本語」印成 "Japanese"，用户要找的那串字就不在屏幕上了。</para>
/// <para><b>改完立刻生效</b>：先落设置档、再 <see cref="Locale.TrySet"/>，带键的元素当场重贴，不重启
/// （P-54 的口径里"请重启"算缺陷）。落盘排在生效前面是刻意的——万一重贴途中抛异常，
/// 下次启动仍是用户要的那一版，而不是"屏幕上改了、重启又变回去"。</para>
/// </summary>
public sealed partial class SettingsPage
{
    private const string FollowSystemKey = "settings.languageFollowSystem";

    /// <summary>程序化选项目前抑制那一次 SelectionChanged（构造时回填当前语言会选中一项，那不是用户改的）。</summary>
    private bool _applyingLanguageChoice;

    private void InitLanguageSection()
    {
        // 第 0 项＝"跟随系统"：它是一句要翻的话，所以给键、不给字面量——换英文后这一项也得改口。
        var followSystem = new ComboBoxItem { Tag = null };
        LocText.SetKey(followSystem, FollowSystemKey);
        LanguageSelect.Items.Add(followSystem);

        foreach (var language in AppLanguages.Pickable)
        {
            LanguageSelect.Items.Add(new ComboBoxItem
            {
                Content = AppLanguages.SelfName(language),
                Tag = AppLanguages.CodeOf(language),
            });
        }

        SelectLanguageItem(App.Services.GetRequiredService<SettingsStore>().LoadUiLanguageCode());
    }

    /// <summary>把界面选项目前设置档里那一格（null＝跟随系统，落在第 0 项）。</summary>
    private void SelectLanguageItem(string? code)
    {
        var index = code is null ? 0 : IndexOfCode(code);
        _applyingLanguageChoice = true;
        try
        {
            LanguageSelect.SelectedIndex = index;
        }
        finally
        {
            _applyingLanguageChoice = false;
        }
    }

    private int IndexOfCode(string code)
    {
        for (var i = 0; i < LanguageSelect.Items.Count; i++)
        {
            if (LanguageSelect.Items[i] is ComboBoxItem item && item.Tag is string tag
                && string.Equals(tag, code, StringComparison.Ordinal)) return i;
        }
        // 认不得的项不该被静默当成"跟随系统"：那一格是用户手改或旧版本写进来的，
        // 悄悄改口会让设置页说的话与下次启动做的事不一致（这里回落到第 0 项，但留下话）。
        StarMark.Abstractions.StarLog.Warn($"[设置页] 界面语言「{code}」不在选项里，按跟随系统显示");
        return 0;
    }

    private void LanguageSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguageChoice) return;
        var code = LanguageSelect.SelectedItem is ComboBoxItem item ? item.Tag as string : null;
        App.Services.GetRequiredService<SettingsStore>().SetUiLanguageCode(code);
        Locale.TrySet(Locale.Resolve(code, AppLanguages.SystemLanguageTags()));
    }
}
