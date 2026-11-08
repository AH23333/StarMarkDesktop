#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions.I18n;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页「常规 → 界面语言」那一格（批次 WI-1；选项形状改自批次 WI-2c）。
/// <para><b>选项在代码里生成、不在 XAML 里列</b>：语言名单只有一个出处，就是
/// <see cref="AppLanguages.Pickable"/>。在 XAML 里多写一行 <c>&lt;ComboBoxItem Content="简体中文"/&gt;</c>，
/// "这一版发几门语言"就同时住在两个地方——补日文时改了表没改这里，界面上就多出一颗点了没反应的选项。</para>
/// <para><b>为什么是 <see cref="LocOption"/> 而不是 <c>ComboBoxItem</c></b>：闭着的那一格显示的是
/// 选中那一刻从 <c>Content</c> 抄走的快照，换语言时没人重贴它（坑表 #272／#275 登记的那处不彻底）。
/// 换成"数据项＋模板"之后，那颗格子里的元素自己带着键，重贴才落到它身上——这一格从此与
/// 主题／材质那几颗下拉走同一条路。</para>
/// <para><b>选项的标签用 <see cref="AppLanguages.SelfName"/>（简体中文／English），它故意不跟着界面语言翻</b>：
/// 语言选择器是拿去认自己母语的。把「日本語」印成 "Japanese"，用户要找的那串字就不在屏幕上了。
/// 所以那几项给的是 <see cref="LocOption.SelfName"/>（<b>没有键</b>），只有"跟随系统"那一项有键。</para>
/// <para><b>改完立刻生效</b>：先落设置档、再 <see cref="Locale.TrySet"/>，带键的元素当场重贴，不重启
/// （P-54 的口径里"请重启"算缺陷）。落盘排在生效前面是刻意的——万一重贴途中抛异常，
/// 下次启动仍是用户要的那一版，而不是"屏幕上改了、重启又变回去"。</para>
/// </summary>
public sealed partial class SettingsPage
{
    private const string FollowSystemKey = "settings.languageFollowSystem";

    /// <summary>语言下拉的数据项：第 0 项＝"跟随系统"（一句要翻的话，所以给键），
    /// 其余＝各语言的自名（不翻）＋那颗要落进设置档的语言代码。</summary>
    public IReadOnlyList<LocOption> LanguageOptions { get; } = BuildLanguageOptions();

    /// <summary>程序化选项目前抑制那一次 SelectionChanged（构造时回填当前语言会选中一项，那不是用户改的）。</summary>
    private bool _applyingLanguageChoice;

    private static IReadOnlyList<LocOption> BuildLanguageOptions()
    {
        var options = new List<LocOption> { LocOption.T(FollowSystemKey, value: null) };
        options.AddRange(AppLanguages.Pickable.Select(language => LocOption.SelfName(
            AppLanguages.SelfName(language), AppLanguages.CodeOf(language))));
        return options;
    }

    private void InitLanguageSection()
    {
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
        for (var i = 0; i < LanguageOptions.Count; i++)
        {
            if (LanguageOptions[i].Value is string tag
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
        var code = LanguageSelect.SelectedItem is LocOption option ? option.Value as string : null;
        // ✅P-170 取证（选项 A）：那笔没复现的沙盒 zh→en 写入发生时没有任何日志可查，"谁在什么状态下写了什么"
        // 判不了（规则 74：没拿到读数不许猜因）。这两行不改任何行为，只把事实记下来：旧值→新值、下拉与页面
        // 的状态、生效与否；拿到下一笔读数之前 ⛔ 顺手加任何护栏。
        var store = App.Services.GetRequiredService<SettingsStore>();
        var previous = store.LoadUiLanguageCode();
        StarMark.Abstractions.StarLog.Info($"[设置页] 界面语言写入取证：{previous ?? "跟随系统"} → {code ?? "跟随系统"}" +
            $"（SelectedIndex={LanguageSelect.SelectedIndex}，新增项={e.AddedItems.Count}，移除项={e.RemovedItems.Count}，页面已加载={IsLoaded}）");
        // 值没变（如 zh→zh）＝装载期回填或重复选择，不是用户改（片242 实锤读数：打开设置页后
        // 每次都带一发同步写盘——设置档原子替换在 UI 线程）。取证照记，写盘与生效链跳过。
        if (string.Equals(previous, code, StringComparison.Ordinal)) return;
        store.SetUiLanguageCode(code);
        var switched = Locale.TrySet(Locale.Resolve(code, AppLanguages.SystemLanguageTags()));
        StarMark.Abstractions.StarLog.Info($"[设置页] 界面语言生效取证：TrySet={switched}（{(code is null ? "跟随系统" : code)}）");
            UiLanguageOverride.Apply();   // 同一次用户动作里把框架链也带上（WI-34）；已铺出的树不追改，重进页现取——绑定族同一口径
    }
}
