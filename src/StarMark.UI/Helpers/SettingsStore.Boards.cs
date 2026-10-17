#nullable enable

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——画板（excalidraw，Phase C 片20）的总开关。
/// <para>形状照抄 <c>SettingsStore.Capture.cs</c> 那颗（partial 不持状态）。默认判据与那两颗相反：
/// <b>默认关</b>——画板是还没交付完的功能，「没表过态」等于关（v1 §21 的总闸）。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>画板总开关（默认关：未表过态＝关）。</summary>
    public bool LoadBoardsEnabled() => Load() is not { } d || d.BoardsEnabled == true;

    public void SaveBoardsEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.BoardsEnabled = enabled;
        Save(d);
    }
}
