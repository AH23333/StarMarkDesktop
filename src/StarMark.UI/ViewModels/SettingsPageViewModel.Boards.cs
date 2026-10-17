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
    }
}
