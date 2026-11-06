#nullable enable
using System;
using StarMark.Integrations.Ocr;

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——截屏这一族（截图 / 贴图 / 识字）的设置出口。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>
    /// 截屏总开关（<b>默认开</b>：这是一条做完了的功能，"没表过态"不等于"关过"，同屏幕画布那条判据）。
    /// <para>这一处只回答"设置里是怎么写的"。<b>哪些入口因此消失、哪些键因此不注册</b>不在这里判——
    /// 那些判据在 <c>Core/Hotkeys/CaptureGate</c>，托盘、热键注册投影、<c>ScreenshotService.Start</c> 三个读者都问它，
    /// 谁在这里自己写一遍"关掉就藏哪几项"，三处迟早不一致（记忆 ⑧）。</para>
    /// </summary>
    public bool LoadCaptureEnabled() => Load() is not { } d || d.CaptureEnabled != false;

    public void SaveCaptureEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.CaptureEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 截图/贴图流的识字识别语言（✅P-82）。<b>null＝跟随系统</b>＝默认＝这一版之前唯一的行为。
    /// 走 <see cref="OcrLanguageOrNone"/> 校验：语言包被卸载后旧值在这里回落 null（跟系统），
    /// 而不是让引擎反复拿着一门建不出的语言失败。
    /// </summary>
    public string? LoadCaptureOcrLanguage() => OcrLanguageOrNone(Load()?.CaptureOcrLanguage);

    /// <summary>见 <see cref="LoadCaptureOcrLanguage"/>。null＝改回跟随系统。</summary>
    public void SaveCaptureOcrLanguage(string? language)
    {
        var d = Load() ?? new SettingsData();
        d.CaptureOcrLanguage = NormalizeOcrLanguageInput(language);
        Save(d);
    }

    /// <summary>
    /// 直认流（框选后直接复制文字）的识字识别语言（✅P-82）。与 <see cref="LoadCaptureOcrLanguage"/>
    /// 分格是拍板口径「按截图/识字分别记住」。null＝跟随系统＝默认。
    /// </summary>
    public string? LoadScreenOcrLanguage() => OcrLanguageOrNone(Load()?.ScreenOcrLanguage);

    /// <summary>见 <see cref="LoadScreenOcrLanguage"/>。null＝改回跟随系统。</summary>
    public void SaveScreenOcrLanguage(string? language)
    {
        var d = Load() ?? new SettingsData();
        d.ScreenOcrLanguage = NormalizeOcrLanguageInput(language);
        Save(d);
    }

    /// <summary>存入侧只做<b>修剪</b>，不做装包校验：引擎那一刻取不到语言列表（临时失败）时把用户的选择
    /// 存成 null 等于悄悄替他改回"跟随系统"。校验统一放读取侧（<see cref="OcrLanguageOrNone"/>），
    /// 临时失败最多让本会话回落默认，存档本身不被破坏。</summary>
    private static string? NormalizeOcrLanguageInput(string? language)
        => string.IsNullOrWhiteSpace(language) ? null : language.Trim();

    /// <summary>
    /// 读取侧校验：只认 <c>ScreenOcrReader.AvailableLanguages</c> 里在场的那一种
    ///（<see cref="StringComparison.OrdinalIgnoreCase"/>：BCP-47 大小写不敏感），
    /// 匹配时<b>返回系统给的原标签</b>（顺带把用户手改的大小写归一），不在场＝null＝跟随系统。
    /// </summary>
    private static string? OcrLanguageOrNone(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        var wanted = stored.Trim();
        foreach (var tag in ScreenOcrReader.AvailableLanguages())
        {
            if (string.Equals(tag, wanted, StringComparison.OrdinalIgnoreCase)) return tag;
        }
        return null;
    }
}
