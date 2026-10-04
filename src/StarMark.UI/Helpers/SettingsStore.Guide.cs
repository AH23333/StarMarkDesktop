#nullable enable

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——首启引导那一次性的记号（批次 VC-3）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>，字段与构造都住在主文件里，这里只放"同一件事的那几条出口"。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>这份引导是不是已经给这位用户看过了。<b>默认（没这一格的老配置）＝没看过</b>，所以他会看一次。</summary>
    public bool LoadGuideShown() => Load() is { } d ? d.FirstRunGuideShown ?? false : false;

    /// <summary>
    /// 记"看过了"。<b>关掉与跳过都走这一条</b>：一次性意味着两条出口都得落盘，
    /// 否则下次启动又弹一次，而"跳过了还来"是用户明确不要的那种纠缠。
    /// </summary>
    public void MarkGuideShown()
    {
        var d = Load() ?? new SettingsData();
        d.FirstRunGuideShown = true;
        Save(d);
    }
}
