#nullable enable
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace StarMark.UI.Views;

/// <summary>
/// 只是把 <see cref="Microsoft.UI.Xaml.UIElement.ProtectedCursor"/> 开一个公开口子。
/// <para>
/// 为什么要有这么一层：WinUI 3 里没有 UWP 那套 <c>UIElement.PointerCursor</c>（这个版本的
/// <c>Microsoft.UI.Xaml</c> 元数据里只有 protected 的 <c>ProtectedCursor</c>），而截图遮罩
/// <b>必须靠光标形状说清"这一按是画还是改框"</b>——用户在框选完之后不看提示条就会动手，
/// 文字提示来不及，只有箭头变成十字那一刻才算告诉他"现在能动框"。
/// </para>
/// </summary>
public sealed class CursorLayer : Grid
{
    public InputCursor? Cursor
    {
        get => ProtectedCursor;
        set => ProtectedCursor = value;
    }
}
