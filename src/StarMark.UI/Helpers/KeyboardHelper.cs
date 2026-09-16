#nullable enable
namespace StarMark.UI.Helpers;

/// <summary>键盘状态辅助（WinUI 3 桌面端无 KeyRoutedEventArgs.IsControlKeyDown，需读线程键盘状态）。</summary>
public static class KeyboardHelper
{
    public static bool IsCtrlDown()
    {
        try
        {
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }
        catch { return false; }
    }
}
