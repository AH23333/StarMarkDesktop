#nullable enable
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// 活动事件三色归类（<see cref="ActivityChange"/>）→ 画刷（#51）：
/// <c>增=绿 / 删=红 / 改=黄</c>，即 Success / <b>Critical</b> / <b>Caution</b>。
/// <para>
/// 早期实现写作 Removed→Caution、Modified→Attention，看着像"警示色递进"，其实按 WinUI 语义
/// Caution 是琥珀黄、Attention 是中性的蓝灰，于是"删除条目"显示成黄色、"修改"既不是黄也不是红，
/// 与三色设计对不上（用户实测指出）。
/// </para>
/// 主题感知解析：IValueConverter 拿不到元素，按主窗当前暗色态从 ThemeDictionaries 取（同其余主题画笔用法）。
/// <para>
/// P-3 兜底：未映射的 ActivityKind（将来新增成员）归 <see cref="ActivityChange.Neutral"/>，
/// 解析到中性灰 SystemFillColorNeutralBrush——不冒充增删改三色。
/// </para>
/// </summary>
public sealed class ActivityChangeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var dark = ThemeManager.IsAppDark();
        var key = value is ActivityChange c
            ? c switch
            {
                ActivityChange.Removed => "SystemFillColorCriticalBrush",
                ActivityChange.Modified => "SystemFillColorCautionBrush",
                ActivityChange.Neutral => "SystemFillColorNeutralBrush",
                _ => "SystemFillColorSuccessBrush",
            }
            : "SystemFillColorNeutralBrush";
        return ThemeBrush.Resolve(dark, key) ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
