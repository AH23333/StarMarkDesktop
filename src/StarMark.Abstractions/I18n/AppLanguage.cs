#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StarMark.Abstractions.I18n;

/// <summary>
/// 界面语言。<b>能选的那几格由表决定</b>：表里没有那一语言的整列，就不该出现在选项里——
/// 给用户一颗选了之后仍然满屏中文的「日本語」，等于用界面暗示一件还没做到的事。
/// <para>批次 WI-3 补齐了日文整列（351 行逐行有 Ja），所以这一格多了 <see cref="Japanese"/>；
/// 顺序即 <see cref="AppLanguages.Pickable"/> 的列序，新增一格而表里没那一列时会当场编不过
/// （<c>Entry</c> 的四个参数都是必填）。</para>
/// </summary>
public enum AppLanguage
{
    Chinese,
    English,
    Japanese,
}

/// <summary>
/// 语言代码的认与不认。<b>三个入口都只认得那几串死的</b>：认不得的一律回 <c>null</c>，
/// 由调用方决定回落哪一档——"像日文就先给中文"这种就近猜测会让用户找不到自己那一项。
/// </summary>
public static class AppLanguages
{
    public const string ChineseCode = "zh";
    public const string EnglishCode = "en";
    public const string JapaneseCode = "ja";

    /// <summary>语言默认档：这台机器上没有任何认得的信号时用哪一版（＝这份文案的作者语言）。</summary>
    public const AppLanguage Default = AppLanguage.Chinese;

    /// <summary>能选的语言（设置页那一颗下拉就按这个顺序列）。新语言要进这一列，表里必须先有整行。</summary>
    public static IReadOnlyList<AppLanguage> Pickable { get; } =
        new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };

    public static string CodeOf(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => ChineseCode,
        AppLanguage.English => EnglishCode,
        AppLanguage.Japanese => JapaneseCode,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "新增一格而这里没配，就该红在这里"),
    };

    /// <summary>设置档里那串代码：只认 <c>zh</c>／<c>en</c>／<c>ja</c>（大小写与首尾空白不敏感），其余 <c>null</c>。
    /// <c>null</c> 的含义是"这位用户从没表过态"，不是"给他一个近的"。</summary>
    public static AppLanguage? FromCode(string? code)
        => code?.Trim().ToLowerInvariant() switch
        {
            ChineseCode => AppLanguage.Chinese,
            EnglishCode => AppLanguage.English,
            JapaneseCode => AppLanguage.Japanese,
            _ => null,
        };

    /// <summary>
    /// 从一串 Windows 语言标签里挑第一个认得的（<c>ja-JP</c>／<c>zh-Hans-CN</c>／<c>en-GB</c> 都按主子标签算）。
    /// <para>按顺序取第一个认得的，而不是只看第一条：系统的语言列表是优先级表，
    /// 一个"日文为主、英文为二"的用户在没有日文时应当拿到英文，而不是被第一条语言判成中文。</para>
    /// </summary>
    public static AppLanguage? PickFrom(IEnumerable<string?> windowsLanguageTags)
    {
        foreach (var tag in windowsLanguageTags ?? Enumerable.Empty<string?>())
        {
            var trimmed = tag?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var dash = trimmed!.IndexOf('-');
            var primary = dash >= 0 ? trimmed[..dash] : trimmed;
            if (FromCode(primary) is { } found) return found;
        }
        return null;
    }

    /// <summary>这台机器的语言偏好（界面语言只看 UI 那一档；读不到不抛——拿不到就是"没有信号"）。
    /// <para>只在这里碰环境：<see cref="PickFrom"/> 是纯函数，测试给它任意表。</para></summary>
    public static IReadOnlyList<string> SystemLanguageTags()
    {
        var tags = new List<string>();
        try
        {
            tags.Add(CultureInfo.CurrentUICulture.Name);
            tags.Add(CultureInfo.CurrentCulture.Name);
        }
        catch (Exception)
        {
            // 极端环境（受限配置、奇异的 locale）下读不到文化：那就不猜，交给默认档。
        }
        return tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }

    /// <summary>
    /// 语言名<b>用它自己的写法</b>（简体中文／English），不跟着界面语言变。
    /// <para>理由不是惯例好看：换成英文之后，"日本語"这一项若被翻成 "Japanese"，
    /// 用户找的就不是他认得的那串字了——语言选择器是拿去认自己母语的。</para>
    /// </summary>
    public static string SelfName(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "简体中文",
        AppLanguage.English => "English",
        AppLanguage.Japanese => "日本語",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };
}
