#nullable enable
using Microsoft.UI.Xaml.Controls;

namespace StarMark.UI.Views;

/// <summary>
/// 弹窗卡片外壳：背景与边框使用 XAML <c>ThemeResource</c>，自动跟随窗口根的
/// <see cref="Microsoft.UI.Xaml.FrameworkElement.RequestedTheme"/>（由
/// <see cref="Helpers.ThemeManager.Apply"/> 在 <c>win.Content</c> 设置之后套用）。
/// 这与 <c>WidgetWindow</c> 的根 <c>Border</c> 同款机制，已在生产环境验证可靠，
/// 不会像代码侧 <c>ThemeBrush.For</c> 那样解析不到正确的浅色画刷。
/// </summary>
public sealed partial class PopupCard : UserControl
{
    public PopupCard() => InitializeComponent();

    /// <summary>卡片内部承载的内容（标题 / 正文 / 编辑器主体等）。</summary>
    public object? CardContent
    {
        get => InnerPresenter.Content;
        set => InnerPresenter.Content = value;
    }
}
