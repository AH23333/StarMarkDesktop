#nullable enable

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——画板（excalidraw，Phase C 片20）的总开关。
/// <para>形状照抄 <c>SettingsStore.Capture.cs</c> 那颗（partial 不持状态）。默认判据与那两颗相反：
/// <b>默认关</b>——画板是还没交付完的功能，「没表过态」等于关（v1 §21 的总闸）。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>画板总开关（默认关：未表过态＝关）。
    /// <para>⚠ 片204＝P-289（本批新立同批结案）：这颗判据曾经<b>整颗倒置</b>——旧文是
    /// <c>Load() is not { } d || d.BoardsEnabled == true</c>，那是 Capture 那颗「默认开」的句式
    /// （文件缺失回 null 那一臂交 <b>true</b>），照抄过来只翻了比较子（<c>!= false</c>→<c>== true</c>）
    /// 而⛔ 翻句式 ⇒ 未表过态＝<b>开</b>，与本档头注、闸门名与片20 提交语三处口径全部相反。
    /// 正确的「默认关」形状＝<c>is { } d &amp;&amp; … == true</c>：null 那一臂交 false。</para></summary>
    public bool LoadBoardsEnabled() => Load() is { } d && d.BoardsEnabled == true;

    public void SaveBoardsEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.BoardsEnabled = enabled;
        Save(d);
    }
}
