#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarMark.Abstractions.I18n;

/// <summary>
/// 当前界面语言＋取词的唯一入口（批次 WI-1）。
/// <para>进程里<b>只有这一颗可变状态</b>：谁要按语言取词都从这里走，于是"换语言"这一件事
/// 只有一个要重画的时刻，而不是每个控件自己记得去读一遍。</para>
/// <para>它住在 Abstractions 是为了让 Core 的措辞（更新的结局那句话）将来能进同一张表，
/// 而不必反过来让 Abstractions 看见 WinUI。UI 侧怎么"当场重画"见 <c>StarMark.UI/Helpers/LocText.cs</c>。</para>
/// </summary>
public static class Locale
{
    private static readonly object Gate = new();

    /// <summary>没登记的键只报一次：这一类问题的表现是"某个按钮点了什么也没变"，
    /// 刷屏不会更容易发现，只会把日志里真正那条挤掉（#236 那条节流口径）。</summary>
    private static readonly HashSet<string> ReportedUnknown = new(StringComparer.Ordinal);

    private static AppLanguage _current = AppLanguages.Default;

    /// <summary>界面当前用哪一版文案。启动时由 <c>App</c> 按设置档与系统语言各算一次。</summary>
    public static AppLanguage Current
    {
        get { lock (Gate) return _current; }
    }

    /// <summary>语言换成的那一刻触发一次。<b>订阅方只许做"把已注册的元素重贴一遍"这一件事</b>：
    /// 重建页面、重新查库都不属于这里。</summary>
    public static event Action? Changed;

    /// <summary>
    /// 该用哪一版：<b>用户表过态优先</b>，没表过态才看系统，两样都读不到才落默认档。
    /// <para>顺序反过来就是"这台机器的语言设置比用户的选择大"——那是用户改不动的一次覆盖。</para>
    /// </summary>
    public static AppLanguage Resolve(string? savedCode, IEnumerable<string?> windowsLanguageTags)
        => AppLanguages.FromCode(savedCode)
           ?? AppLanguages.PickFrom(windowsLanguageTags)
           ?? AppLanguages.Default;

    /// <summary>换语言。<b>相同就不触发</b>：打开设置页看完又原样点回去，不该让整窗重贴一遍。</summary>
    public static bool TrySet(AppLanguage next)
    {
        lock (Gate)
        {
            if (_current == next) return false;
            _current = next;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>取一句界面文案。<b>键没登记</b>不抛也不装没看见：屏幕上出现 <c>⟨nav.tree⟩</c> 这种形状，
    /// 用户会截图来问，日志里也留着那句——比"那一颗按钮显示成上一个语言的词"更容易查。</summary>
    public static string T(string key)
    {
        if (!UiStrings.TryGet(key, out var entry))
        {
            WarnUnknownOnce(key);
            return "⟨" + key + "⟩";
        }
        return UiStrings.TextOf(entry, Current);
    }

    /// <summary>带读数的句子。占位符是位置式，各语言那一行必须给同一组编号——这条由闸门逐行比对钉住。
    /// <b>刻意不 catch 格式异常</b>：表是编译期数据，配错号的行应该在同一条闸门上红，不该到运行期兜。</summary>
    public static string Fmt(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, T(key), args);

    private static void WarnUnknownOnce(string key)
    {
        lock (Gate)
        {
            if (!ReportedUnknown.Add(key)) return;
        }
        StarLog.Warn($"界面文案没登记这颗键：{key}（界面上会显示成 ⟨{key}⟩，不是漏翻，是没写进表）");
    }
}
