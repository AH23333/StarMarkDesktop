#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——画板总开关（Phase C 片20）＋画板主题（片273）。
/// <para>开关形状照抄 Capture/Rss 那两颗 partial：字段默认关（false），关掉时托盘里那一项整条消失
/// （「关掉就别留入口」口径），副作用在回灌期间抑制。</para>
/// <para>主题那一格是片273 分家后的<b>唯一推送点</b>：换主窗主题那一拍已随分家退役
/// （<c>MainWindow.RefreshAppearance</c> 不再推板），已开的板换肤只能从这里走。</para>
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

    /// <summary>画板主题下拉的选中档（片273）：0 跟随系统／1 浅色／2 深色——序与主窗那张
    /// <c>ThemeOptions</c> 清单同一份（第 0 项那句「跟随系统」两处共用同一颗键），⛔ 另写一张同义清单。</summary>
    [ObservableProperty] private int _boardThemeIndex;

    /// <summary>LoadFromStore 回灌初值期间抑制写盘＋推送（同上面那颗的守卫）。</summary>
    private bool _suppressBoardThemeApply;

    /// <summary>
    /// 画板主题那一格的唯一推送点（片273）：先落盘再当帧经名册叫醒已开的板——不重启
    /// （同 BoardsEnabled「多一步就算缺陷」口径）。名册空转＝没开着的板，零副作用。
    /// </summary>
    partial void OnBoardThemeIndexChanged(int value)
    {
        if (_suppressBoardThemeApply) return;
        _settings.SaveBoardTheme(IndexToBoardTheme(value));
        StarMark.UI.Boards.BoardsWindows.ReapplyAppearance();
    }

    /// <summary>下拉第几项 ↔ 主题偏好（与主窗 Save() 里那颗 switch 同一副映射，乱序值一律回跟随系统）。</summary>
    private static ThemePreference IndexToBoardTheme(int index) => index switch
    {
        1 => ThemePreference.Light,
        2 => ThemePreference.Dark,
        _ => ThemePreference.Default,
    };
}
