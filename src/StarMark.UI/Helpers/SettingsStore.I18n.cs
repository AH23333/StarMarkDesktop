#nullable enable
using StarMark.Abstractions.I18n;

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——界面语言那一格（批次 WI-1）。
/// <para>按访问面拆出来的 partial：不持状态，字段与构造住在主文件里。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>
    /// 用户为界面语言表过态没有。<b>null＝没表过态</b>（不是"跟随系统失败"，也不是"选中文"），
    /// 于是启动时那一格仍会去看系统语言。<b>认不得的代码一律读成 null</b>：
    /// 这一格在设置档里是用户可手改的字符串，把它原样留着就等于让他下次启动仍然看不见自己的选项。
    /// </summary>
    public string? LoadUiLanguageCode() => Load() is { } d ? AppLanguages.FromCode(d.UiLanguageCode) is { } parsed
        ? AppLanguages.CodeOf(parsed)
        : null
        : null;

    /// <summary>
    /// 记下界面语言；传 null＝"改回跟随系统"（清空这一格），不引入一个额外的"自动"哨兵值。
    /// <b>存代码不存序号</b>：往 <see cref="AppLanguages.Pickable"/> 中间插一门语言就会把序号全平移一位，
    /// 而那种坏是读不出来的——它看起来完全合法。
    /// </summary>
    public void SetUiLanguageCode(string? code)
    {
        var d = Load() ?? new SettingsData();
        var parsed = AppLanguages.FromCode(code);
        d.UiLanguageCode = parsed is null ? null : AppLanguages.CodeOf(parsed.Value);
        Save(d);
    }
}
