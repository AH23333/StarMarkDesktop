#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace StarMark.UI.Helpers;

/// <summary>
/// 组件「文本缩放」的<b>基准字号</b>记录（附加属性形式）。
/// <para>
/// 为什么必须用附加属性而不是窗口里的字典缓存：
/// <list type="bullet">
/// <item>基准与被缩放的元素<b>同生共死</b>，内容重建时旧元素连同基准一起被回收，
///       不存在「缓存清了但元素还在 / 元素换了但缓存还在」的错位；</item>
/// <item>基准只记录<b>一次</b>（首次见到该元素时），后续任何次数的套用都不会把
///       已缩放后的字号当成新基准——这正是「调到默认反而变大/变小」的成因。</item>
/// </list>
/// </para>
/// <para>
/// 记录的是「XAML/代码显式声明的字号」，并额外记住<b>当初有没有显式声明</b>：
/// 没声明的元素在系数回到 1.0 时必须 <c>ClearValue</c> 交还给继承链，
/// 写回一个数值会把继承值钉死，之后父级再改字号它就再也跟不上了。
/// </para>
/// </summary>
public static class WidgetTextScale
{
    /// <summary>基准字号。0 = 尚未记录。</summary>
    public static readonly DependencyProperty BaseFontSizeProperty =
        DependencyProperty.RegisterAttached(
            "BaseFontSize", typeof(double), typeof(WidgetTextScale), new PropertyMetadata(0d));

    /// <summary>该元素当初是否<b>显式</b>声明过 FontSize（XAML 属性或代码赋值）。</summary>
    public static readonly DependencyProperty HadLocalFontSizeProperty =
        DependencyProperty.RegisterAttached(
            "HadLocalFontSize", typeof(bool), typeof(WidgetTextScale), new PropertyMetadata(false));

    public static double GetBaseFontSize(DependencyObject obj) => (double)obj.GetValue(BaseFontSizeProperty);

    public static void SetBaseFontSize(DependencyObject obj, double value) => obj.SetValue(BaseFontSizeProperty, value);

    public static bool GetHadLocalFontSize(DependencyObject obj) => (bool)obj.GetValue(HadLocalFontSizeProperty);

    public static void SetHadLocalFontSize(DependencyObject obj, bool value) => obj.SetValue(HadLocalFontSizeProperty, value);

    /// <summary>兜底基准：极少数字号读到 0（元素尚未完成样式解析）时用系统默认正文号。</summary>
    private const double FallbackBase = 14d;

    /// <summary>
    /// 首次见到该文本元素时记录基准。返回是否已记录过（true = 这次是首次）。
    /// </summary>
    public static bool CaptureBase(TextBlock tb)
    {
        if (GetBaseFontSize(tb) > 0) return false;

        // ReadLocalValue 只返回「直接写在该元素上」的值：XAML 属性与代码赋值算，
        // 样式/继承来的不算（返回 UnsetValue）。据此区分两种还原方式。
        var local = tb.ReadLocalValue(TextBlock.FontSizeProperty);
        var declared = local is double;

        var size = declared ? (double)local! : tb.FontSize;
        if (size <= 0) size = FallbackBase;

        SetBaseFontSize(tb, size);
        SetHadLocalFontSize(tb, declared);
        return true;
    }

    /// <summary>
    /// 按系数套用字号。系数恰为 1.0 时走<b>精确还原</b>而不是乘法——
    /// 浮点乘法即便 ×1.0 也会引入「声明值 vs 继承值」的差异，
    /// 更关键的是它无法把继承型元素交还给继承链。
    /// </summary>
    public static void Apply(TextBlock tb, double scale)
    {
        var baseFontSize = GetBaseFontSize(tb);
        if (baseFontSize <= 0) return;   // 未记录基准就不动，宁可不缩放也不要缩错

        if (IsDefault(scale))
        {
            if (GetHadLocalFontSize(tb)) tb.FontSize = baseFontSize;        // 精确写回原值
            else tb.ClearValue(TextBlock.FontSizeProperty);                 // 交还继承链
            return;
        }

        tb.FontSize = baseFontSize * scale;
    }

    public static bool IsDefault(double scale) => Math.Abs(scale - 1.0) < 1e-6;
}
