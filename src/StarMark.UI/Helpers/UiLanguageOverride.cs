#nullable enable
using System;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

namespace StarMark.UI.Helpers;

/// <summary>
/// 应用级语言对齐的<b>实验结论件</b>（P-169 第六族·批次 WI-34 结案形态）——
/// <para>英文屏可达性树里那九行中文（滚动条自动化名、数字框名里的"最小/最大"、宿主弹出窗口）
/// 不是我们写的字：它们住 WinUI 框架的资源链，与自建的 <see cref="Locale"/> 表互不相认。</para>
/// <para><b>本轮实验对"能不能从应用层把手"给出的答案（两条都有号）：</b></para>
/// <list type="number">
/// <item><c>ApplicationLanguages.PrimaryLanguageOverride</c>（UWP 时代的正门）：
/// 在<b>未打包 WinUI3</b> 直接抛 InvalidOperationException（"Operation is not valid due to the current state
/// of the object"）——2026-10-07 四次启动日志为证，每次都是这同一条。</item>
/// <item>旧 MRT 的 <c>ResourceContext</c>：投影层没有可写的进程级入口（CS0117——静态 Default 不存在）；
/// 手工 new 一个实例设 override 是<b>假动作</b>——框架控件取词走各自根自带的上下文，不查我们造的副本。
/// ⛔ 故此路连"试一下"的死代码都不留，这句判词加编译号就是全部记录。</item>
/// </list>
/// <para>两条皆哑 ⇒ <c>AnyChainAlive</c> 恒 false，第六族如实结案：<b>机制不可得</b>。
/// 不做的替代也写明：不为这九行去逐控件撒 AutomationProperties.Name——那等于用一千次手工手术
/// 给一个"屏幕阅读器才读得到、且读到的内容与系统语言一致"的现象刷白；肉眼屏全英文这条不变。</para>
/// <para><see cref="Apply"/> 保留为<b>探针</b>：每启动/切换仍试一次第一条链并如实落日志——
/// 将来 WinAppSDK 把正门修好了，日志会第一个知道（LastApplied 变不是 null，本件注释随之改写）。</para>
/// </summary>
public static class UiLanguageOverride
{
    /// <summary>最近一次真正落上的链名；两链皆哑时每轮都是 null（真机日志与它同口径）。</summary>
    public static string? LastApplied { get; private set; }

    /// <summary>是否至少有一条链活着（本架构答案已知为 false；留这颗是给"哪天框架修好了"的报警器）。</summary>
    public static bool AnyChainAlive => LastApplied is not null;

    /// <summary>设定并幂等：启动与每次语言切换各试一次。失败是已知常态，不刷屏、不抛给主程序。</summary>
    public static void Apply()
    {
        LastApplied = null;
        var tag = Locale.Current switch
        {
            // 框架限定名的写法（zh-Hans/ja/en）——若哪天正门能用，这三串就是它的键值。
            AppLanguage.Chinese => "zh-Hans",
            AppLanguage.Japanese => "ja",
            _ => "en",
        };
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
            LastApplied = "PrimaryLanguageOverride";
            StarLog.Info($"[a11y语言覆盖] PrimaryLanguageOverride 设定了一档（{tag}）——正门可用，第六族欠账结案该翻案了");
        }
        catch (Exception ex)
        {
            // 已知常态（未打包 WinUI3 必走这里）：每次启动/切换各落一行，真机的九行树与这行日志互为证词。
            StarLog.Info($"[a11y语言覆盖] 正门仍锁着（已知形态，框架串随系统语言）：{ex.Message}");
        }
    }
}
