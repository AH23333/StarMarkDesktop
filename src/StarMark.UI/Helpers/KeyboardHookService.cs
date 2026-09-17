#nullable enable
using System;
using System.Runtime.InteropServices;

namespace StarMark.UI.Helpers;

/// <summary>
/// 低层级键盘钩子（WH_KEYBOARD_LL），专供「快捷键录制」使用。
/// <para>
/// 为什么不用 WinUI 的 <c>KeyDown</c>（即使挂在被点击的按钮上）：
/// WinUI 3 的键事件路由受焦点宿主（Page / Frame / TabView / ScrollViewer）影响，
/// 录制按钮即便拿到焦点也可能收不到按键；且 Win(WinLogo) 键在按下瞬间常被系统
/// （开始菜单等）截走，WinUI 层很难录到 Win 组合键。
/// </para>
/// WH_KEYBOARD_LL 在系统派发按键前回调安装它的线程，与 XAML 焦点树完全无关，
/// 因此录制稳健，且 Win 键、无焦点场景均被覆盖。
/// 注意：必须由带消息泵的线程（UI 线程）安装；回调在该线程执行。
/// <para>
/// 修饰键状态由钩子自身累计（按下累加、抬起移除），而不是在回调里临时调用 GetAsyncKeyState：
/// 后者依赖调用时序且历史语义（"上次调用以来按过"）会产生脏位，组合键录制不可靠。
/// </para>
/// </summary>
internal sealed class KeyboardHookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private IntPtr _hook;
    // 必须长期持有委托引用，否则 GC 后原生回调会崩进程。
    private HookProc? _proc;

    private readonly System.Collections.Generic.HashSet<uint> _down = new();

    /// <summary>按键按下回调，参数为虚拟键码（含修饰键本身）。在 UI 线程上触发。</summary>
    public event Action<uint>? KeyDown;

    /// <summary>按键抬起回调，参数为虚拟键码。</summary>
    public event Action<uint>? KeyUp;

    /// <summary>当前按住的修饰键组合（由钩子累计维护，不含 NoRepeat）。</summary>
    public HotkeyModifiers CurrentModifiers { get; private set; } = HotkeyModifiers.None;

    public bool Started => _hook != IntPtr.Zero;

    public bool TryStart()
    {
        if (_hook != IntPtr.Zero) return true;
        _down.Clear();
        CurrentModifiers = ReadSystemModifiers();
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            _proc = null;
            return false;
        }
        return true;
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _proc = null;
        }
        _down.Clear();
        CurrentModifiers = HotkeyModifiers.None;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && lParam != IntPtr.Zero)
            {
                // KBDLLHOOKSTRUCT 第一个字段是 vkCode（DWORD）
                var vk = (uint)Marshal.ReadInt32(lParam);
                var msg = wParam.ToInt32();

                if (msg is WmKeyDown or WmSysKeyDown)
                {
                    TrackDown(vk);
                    KeyDown?.Invoke(vk);
                }
                else if (msg is WmKeyUp or WmSysKeyUp)
                {
                    TrackUp(vk);
                    KeyUp?.Invoke(vk);
                }
            }
        }
        catch
        {
            // 回调里绝不向外抛异常：异常穿越原生边界会直接崩进程
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void TrackDown(uint vk)
    {
        _down.Add(vk);
        CurrentModifiers |= ModifierOf(vk);
    }

    private void TrackUp(uint vk)
    {
        _down.Remove(vk);
        // 左右同名修饰键：任一侧仍按住即保留该修饰位
        CurrentModifiers = RecomputeModifiers();
    }

    private HotkeyModifiers RecomputeModifiers()
    {
        var m = HotkeyModifiers.None;
        foreach (var vk in _down) m |= ModifierOf(vk);
        return m;
    }

    private static HotkeyModifiers ModifierOf(uint vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,  // Ctrl / LControl / RControl
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,      // Alt / LMenu / RMenu
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,    // Shift / LShift / RShift
        0x5B or 0x5C => HotkeyModifiers.Windows,          // LWin / RWin
        _ => HotkeyModifiers.None,
    };

    /// <summary>Win32 层读取「当前是否按住」（高位为 1 才是按下，避免历史脏位误判）。</summary>
    public static bool IsKeyPressed(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

    /// <summary>启动钩子时的兜底：直接问系统当前按住的修饰键（钩子未记录到按下之前的状态）。</summary>
    public static HotkeyModifiers ReadSystemModifiers()
    {
        var m = HotkeyModifiers.None;
        if (IsKeyPressed(0x11) || IsKeyPressed(0xA2) || IsKeyPressed(0xA3)) m |= HotkeyModifiers.Control;
        if (IsKeyPressed(0x12) || IsKeyPressed(0xA4) || IsKeyPressed(0xA5)) m |= HotkeyModifiers.Alt;
        if (IsKeyPressed(0x10) || IsKeyPressed(0xA0) || IsKeyPressed(0xA1)) m |= HotkeyModifiers.Shift;
        if (IsKeyPressed(0x5B) || IsKeyPressed(0x5C)) m |= HotkeyModifiers.Windows;
        return m;
    }

    public void Dispose() => Stop();
}
