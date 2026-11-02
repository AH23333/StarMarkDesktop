#nullable enable
using System;
using System.Runtime.InteropServices;
using Xunit;
using static StarMark.Tests.SourceGate;
using static StarMark.Integrations.SystemTray.NativeMethods;

namespace StarMark.Tests;

/// <summary>
/// 托盘那颗消息-only 窗口的<b>生命周期</b>闸门（批次 P-32 ①）。
/// <para>症状：托盘「关→开」之后右键没菜单，而屏幕上什么都不说。机制：类名固定，第二次
/// <c>RegisterClassExW</c> 一律以 <c>ERROR_CLASS_ALREADY_EXISTS</c> 失败，而那时类里留着的仍是
/// <b>第一轮那个实例</b>的委托（<c>lpfnWndProc</c> 绑闭包）⇒ 第二轮新建的窗口把消息全发给已经 Dispose 的实例；
/// 同时每轮 <c>GCHandle.Alloc(this)</c> 永不 Free（Free 只挂在 WM_DESTROY 上，而那一发到不了新实例）。</para>
/// <para>修法沿用 <c>ClipboardWatcher</c> 那颗<b>已经修好过的同一族形状</b>：类只注册一次・委托静态保活・
/// 派发看一个静态目标。<b>刻意不走 <c>GWL_USERDATA</c></b>——那颗原生偏移在 SDK 头文件里有两种并存写法，
/// 取错了既不编译报错也不抛异常，只会让 WndProc 永远读到 0（同一份警告住在那颗档的卷首）。</para>
/// <para>两颗尺各问一维：① 源码尺问"这一族现在长什么样"（正件与禁项都在，⛔ 一把只禁不放的尺）；
/// ② 运行时尺问"我据以推理的那个 Win32 前提是不是真的"——自己造一颗临时类名去撞第二次注册，
/// 并把消息发进窗口看它落到哪颗委托上。少了②，①就只是一段会说谎的注释（坑表：动作尺⛔ 正件）。</para>
/// </summary>
public sealed class TrayHostLifecycleGateTests
{
    private const string Tray = "src/StarMark.Integrations/SystemTray/TrayHost.cs";

    [Fact]
    public void TheTrayClassIsRegisteredOnceAndDispatchesThroughOneStaticTarget()
    {
        var src = Code(ReadRepoFile(Tray));

        // 正件：静态派发那一排零件各恰一颗
        Assert.Contains("private static TrayHost? _current;", src, StringComparison.Ordinal);
        Assert.Contains("_sharedProc ??= WndProcStatic;", src, StringComparison.Ordinal);
        Assert.Contains("lpfnWndProc = _sharedProc,", src, StringComparison.Ordinal);
        Assert.Equal(1, Count(src, "EnsureClassRegistered();"));                      // Initialize 只问那颗门
        Assert.Equal(1, Count(src, "RegisterClassExW("));                             // 而那颗调用点全档唯一
        // "类别已在场就返回"那一句现读两颗：进门前那道守卫＋注册成功后那道收尾。
        // ⛔ 把它们并成一颗：那颗数就是这一族的形状，将来谁挪走其中一道，这一格先红。
        Assert.Equal(2, Count(src, "if (_classAtom != 0) return;"));
        Assert.Contains("_current is { } host ? host.WndProc(", src, StringComparison.Ordinal);
        Assert.Equal(2, Count(src, "ReferenceEquals(_current, this)"));                // WM_DESTROY 与 Dispose 两支都摘
        Assert.Contains("ERROR_CLASS_ALREADY_EXISTS", src, StringComparison.Ordinal);
        Assert.Contains("Marshal.GetLastWin32Error()", src, StringComparison.Ordinal);  // 返回值有人读＝1410 之外绝不静默

        // 禁项：旧形状⛔ 长回来
        Assert.DoesNotContain("GCHandle", src, StringComparison.Ordinal);              // 整类句柄泄漏的来源
        Assert.DoesNotContain("lpfnWndProc = _wndProc", src, StringComparison.Ordinal); // 绑实例的委托＝路由到死实例
        Assert.DoesNotContain("GWL_USERDATA", src, StringComparison.Ordinal);            // 那颗"写了而⛔ 有人读"的死写
        Assert.DoesNotContain("_wndProc", src, StringComparison.Ordinal);               // 实例级委托字段本身
    }

    // ==================== ② 那颗 Win32 前提尺：用应用自己那套互操作去撞 ====================

    private const uint ProbeMessage = 0x0400 + 7;                 // WM_USER + 7
    private const int ErrorClassAlreadyExists = 1410;             // ERROR_CLASS_ALREADY_EXISTS

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    /// <summary>
    /// 前提（当场量，⛔ 从注释读）：同一类名第二次注册<b>必失败</b>（1410），而窗口过程<b>仍归第一颗委托</b>。
    /// 本文件①那一排禁项的全部理由就是这一发；用的是应用自己那颗 <c>WNDCLASSEX</c> 与同一批入口，
    /// ⛔ 在测试里另抄一份结构体（上一版正是那份"自己抄的 struct"漏了 <c>hIconSm</c> 才把第一次注册就弄失败）。
    /// </summary>
    [Fact]
    public void SecondRegistrationOfTheSameClassNameKeepsTheFirstWndProc()
    {
        var className = "StarMarkTrayGateProbe" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var instance = GetModuleHandleW(null);
        var firstHits = 0;
        var secondHits = 0;

        // 委托必须由这两个局部变量持住到方法结束：非托管侧只存函数指针，委托一旦被 GC 就是野指针回调。
        WndProcDelegate firstProc = (h, m, w, l) =>
        {
            if (m == ProbeMessage) firstHits++;
            return DefWindowProcW(h, m, w, l);
        };
        WndProcDelegate secondProc = (h, m, w, l) =>
        {
            if (m == ProbeMessage) secondHits++;
            return DefWindowProcW(h, m, w, l);
        };

        ushort Register(WndProcDelegate proc)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = proc,
                hInstance = instance,
                lpszClassName = className,
            };
            return RegisterClassExW(ref wc);
        }

        Assert.NotEqual((ushort)0, Register(firstProc));                 // 第一次：注册成功
        Assert.Equal((ushort)0, Register(secondProc));                   // 第二次：同名的那颗类⛔ 换得动
        Assert.Equal(ErrorClassAlreadyExists, Marshal.GetLastWin32Error());

        var hwnd = CreateWindowExW(0, className, "probe", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);                              // 窗口照样建得起来⇒失效点在派发，⛔ 在建窗
        try
        {
            SendMessageW(hwnd, ProbeMessage, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal(1, firstHits);                                  // 消息进的是第一颗委托
            Assert.Equal(0, secondHits);                                 // 本轮那颗压根没被叫过＝托盘"点了没反应"那一族
        }
        finally
        {
            DestroyWindow(hwnd);
            // 销毁窗口之后类别撤得掉——这一发顺带钉住"要么复用、要么注销重建"两条路都真实存在，
            // 而本仓选的是复用（派发目标静态化之后，抢注册本来就⛔ 必要）。
            Assert.True(UnregisterClassW(className, instance));
        }
        GC.KeepAlive(firstProc);
        GC.KeepAlive(secondProc);
    }
}
