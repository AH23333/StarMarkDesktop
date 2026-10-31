#nullable enable
using System;
using System.Runtime.InteropServices;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把<b>框架自带</b>那批屏幕字（滚动条部件名、数字框名里的"最小/最大"、宿主弹出窗口名）
/// 与我们的界面语言对齐的那一颗（P-169 第六族·#298）。
/// <para>这些字不是我们写的：它们住 WinUI 框架的资源链，与自建的 <see cref="Locale"/> 表互不相认，
/// 所以表与标记两把静态尺都管不到它们——英文屏肉眼全英文，屏幕阅读器仍念出中文。</para>
/// <para><b>本件是 WI-34 那句结案的翻案。</b>WI-34 当时试了两条链都哑，就写下"机制不可得"：
/// ① <c>ApplicationLanguages.PrimaryLanguageOverride</c> 在未打包 WinUI3 直接抛
/// <c>InvalidOperationException</c>（今天仍抛，2026-10-31 四行日志为证）；
/// ② 旧 MRT 的 <c>ResourceContext</c> 投影层没有进程级入口（CS0117）。
/// 片152 量到<b>第三条链是活的</b>：Win32 的 <c>SetProcessPreferredUILanguages</c> ＋
/// <c>SetThreadUILanguage</c> 在 <see cref="Apply"/> 那一刻（第一扇窗建出来之前）设好之后，
/// 英文档下 <c>ScrollBar</c> 那五颗部件名与 <c>Popup</c> 那两颗宿主名在真机可达性树里改口（八格设置页里六格归零），
/// 日文档下同一棵树那颗弹出窗口名念出 <c>ポップアップ</c>、⛔ 再念中文那一款（系统仍是 zh-CN）
/// ⇒ 这条链认的是我们的表。⚠ 这一发证到的是"它不再随系统语言"；那颗名三档各念什么，只英文与日文两档有读数。</para>
/// <para>⚠ <b>数字框那一句没跟着改口</b>：四颗 NumberBox 名里那句 <c>最小1 最大500</c> 仍是中文，
/// 而且是把能试的都试完之后现读的（进程表里确实躺着 <c>en-US</c>、又补了一次<b>线程档</b>
/// <c>SetThreadPreferredUILanguages</c>、同一次启动同一棵树上的读数）⇒ 那一族只剩这一颗，
/// 未试的候选也写明：自定义入口点抢在 WinUI bootstrap 之前设语言
/// （代价＝接管生成的 <c>Main</c>，为四颗屏幕阅读器才读得到的字不值得，登记不追）。
/// 那四颗⛔ 是我们的字：仓里逐档扫过，"最小/最大"那串只出现在这一件的注释里，出处在控件模板。</para>
/// <para><see cref="Apply"/> 每启动/切换各试一次并如实落日志；<see cref="LastApplied"/> 是"哪条链活着"，
/// 两链皆哑时为 null（那是报警器，不是断言）。</para>
/// </summary>
public static class UiLanguageOverride
{
    /// <summary>最近一次真正落上的链名；两条链都哑时每轮都是 null。</summary>
    public static string? LastApplied { get; private set; }

    /// <summary>是否至少有一条链活着（片152 起为真——第六族从"不可得"改成"部分可得"）。</summary>
    public static bool AnyChainAlive => LastApplied is not null;

    private const uint MuiLanguageName = 8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetProcessPreferredUILanguages(uint dwFlags, string pwszLanguageList, out uint pNumOfLanguages);

    [DllImport("kernel32.dll")]
    private static extern ushort SetThreadUILanguage(ushort langId);

    /// <summary>设定并幂等：启动与每次语言切换各试一次。失败是已知常态，不刷屏、不抛给主程序。</summary>
    public static void Apply()
    {
        LastApplied = null;
        // 两串都要：下面那条正门认框架限定名（zh-Hans/ja/en），Win32 那一条认 BCP-47 标签（zh-CN/ja-JP/en-US）。
        var (xamlTag, win32Tag, langId) = Locale.Current switch
        {
            AppLanguage.Chinese => ("zh-Hans", "zh-CN", (ushort)0x0804),
            AppLanguage.Japanese => ("ja", "ja-JP", (ushort)0x0411),
            _ => ("en", "en-US", (ushort)0x0409),
        };

        // 链①（活着的那条）。语言表是<b>双</b>零结尾：只补一颗 \0 时这颗 API 照样返回 true，
        // 而进程表是空的——那一发我量错过一次（读回的 procList 是空串，日志里的 0 颗是仪器坏，不是事实）。
        try
        {
            var ok = SetProcessPreferredUILanguages(MuiLanguageName, win32Tag + "\0\0", out _)
                     && SetThreadUILanguage(langId) == langId;
            if (ok)
            {
                LastApplied = "Win32PreferredUiLanguage";
                StarLog.Info($"[a11y语言覆盖] 链①落上了（{win32Tag}）：框架自带的滚动条／弹出窗口名跟着界面语言走");
            }
            else
            {
                StarLog.Info($"[a11y语言覆盖] 链①没咬住（{win32Tag}）：框架串仍随系统语言");
            }
        }
        catch (Exception ex)
        {
            StarLog.Info($"[a11y语言覆盖] 链①抛了（已知可能，未打包进程）：{ex.GetType().Name}");
        }

        // 链②（未打包 WinUI3 至今必抛；留着＝哪天框架修好了，日志第一个知道）。
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = xamlTag;
            LastApplied ??= "PrimaryLanguageOverride";
            StarLog.Info($"[a11y语言覆盖] 正门也开了一档（{xamlTag}）");
        }
        catch (Exception ex)
        {
            StarLog.Info($"[a11y语言覆盖] 正门仍锁着（已知形态）：{ex.Message}");
        }
    }
}
