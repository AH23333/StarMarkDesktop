#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——画板总开关（Phase C 片20）。
/// <para>形状照抄 Capture/Rss 那两颗 partial：字段默认关（false），关掉时托盘里那一项整条消失
/// （「关掉就别留入口」口径），副作用在回灌期间抑制。</para>
/// </summary>
public partial class SettingsPageViewModel
{
    [ObservableProperty] private bool _boardsEnabled;

    /// <summary>LoadFromStore 回灌初值期间抑制写盘（同 Capture/Rss 那两颗的守卫）。</summary>
    private bool _suppressBoardsApply;

    partial void OnBoardsEnabledChanged(bool value)
    {
        if (_suppressBoardsApply) return;
        _settings.SaveBoardsEnabled(value);
        // 托盘当场跟着改：不重启（Capture/Rss 同一口径——多一步就算缺陷）。
        App.MainWindow?.ApplyTraySettings();
        // 片204＝P-286 处置①：开关 OFF ⇒ 已开的画板窗跟着收（关功能就该把它的窗收走，
        // 留着开着的窗＝开关在说谎）。收＝每扇窗自己的那套关窗协商（拦一次→藏→冲写→被拒
        // 那一支亮回「另存／继续编辑」面板＝「收前问一句要不要存」），主人是名册那一颗。
        // ON 那一臂⛔ 动已开的窗：开关只说"从今往后的入口"，已开的窗照旧活着。
        if (!value) StarMark.UI.Boards.BoardsWindows.CloseAll();
    }
}
