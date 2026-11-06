#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StarMark.UI.Helpers;

/// <summary>
/// true → 整卡压暗（片215·P-5B）：「源头已经没有这条」的失联行标灰但照常显示——
/// 删是不许的（行上挂着用户的标签与笔记），一声不吭又等于让用户对着一条幽灵行做判断。
/// false → 1（原样）。ConvertBack 没有读者（OneWay 绑定），回同值只为履约。
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    /// <summary>失联卡的不透明度：压暗到一眼能分出「这几条不一样」，又不至于读不清标题。</summary>
    private const double Dimmed = 0.55;

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? Dimmed : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value;
}
