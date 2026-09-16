#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.Services;

/// <summary>
/// 组件窗口层级服务：决定组件"贴在桌面上"而不是"永远压在屏幕上"。
/// 移植自 DeskBox <c>Services/WidgetLayerService.cs</c> 的三个核心动作：
/// <list type="number">
///   <item><b>挂载桌面图标层</b>：把窗口所有者设为 Explorer 的 <c>SHELLDLL_DefView</c>，
///         组件便落在"桌面层"内——在所有应用窗口之下、在桌面图标之上，
///         Win+D 与点击桌面都不会把它们挤走；</item>
///   <item><b>瞬态浮起</b>：需要临时置顶时先 <c>HWND_TOPMOST</c> 再立刻 <c>HWND_NOTOPMOST</c>，
///         窗口停在普通层级的顶部，却不占 topmost 属性（DeskBox 文档称 "transient raise"），
///         避免"永远压屏"；</item>
///   <item><b>整组排 Z 序</b>：多个组件重叠时按
///         <see cref="WidgetZOrderPolicy"/> 一次性排好，而不是逐个 <c>HWND_TOPMOST</c>。</item>
/// </list>
/// 所有 Win32 调用均为 best-effort：查不到桌面层时静默降级为普通窗口，不抛异常。
/// </summary>
public static class WidgetLayerService
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<IntPtr, IntPtr> s_originalOwners = new();
    private static IntPtr s_cachedDesktopIconView;

    private const string ProgmanClass = "Progman";
    private const string DefViewClass = "SHELLDLL_DefView";

    /// <summary>
    /// 查找桌面图标层窗口。
    /// 只使用 Explorer <b>已经创建</b> 的 SHELLDLL_DefView，绝不主动发送 0x052C
    /// 强制生成 WorkerW——DeskBox 明确指出那会与 Explorer 的图标布局恢复竞争，
    /// 可能打乱用户的桌面图标排列。
    /// </summary>
    public static IntPtr FindDesktopIconView()
    {
        lock (s_gate)
        {
            if (s_cachedDesktopIconView != IntPtr.Zero &&
                WindowInterop.IsWindow(s_cachedDesktopIconView))
            {
                return s_cachedDesktopIconView;
            }

            s_cachedDesktopIconView = IntPtr.Zero;

            IntPtr progman = WindowInterop.FindWindowW(ProgmanClass, null);
            if (progman != IntPtr.Zero)
            {
                IntPtr defView = WindowInterop.FindWindowExW(progman, IntPtr.Zero, DefViewClass, null);
                if (defView != IntPtr.Zero)
                {
                    s_cachedDesktopIconView = defView;
                    return defView;
                }
            }

            // 回退：某些桌面（壁纸轮换、多桌面）下 DefView 挂在 WorkerW 下。
            // 仅枚举已存在的窗口，不创建新的。
            WindowInterop.EnumWindows((topLevel, _) =>
            {
                IntPtr defView = WindowInterop.FindWindowExW(topLevel, IntPtr.Zero, DefViewClass, null);
                if (defView != IntPtr.Zero)
                {
                    s_cachedDesktopIconView = defView;
                    return false; // 停止枚举
                }

                return true;
            }, IntPtr.Zero);

            return s_cachedDesktopIconView;
        }
    }

    /// <summary>桌面层缓存失效（Explorer 重启后需重新查找）。</summary>
    public static void InvalidateDesktopCache()
    {
        lock (s_gate) s_cachedDesktopIconView = IntPtr.Zero;
    }

    public static bool IsAttached(IntPtr hwnd)
    {
        lock (s_gate) return s_originalOwners.ContainsKey(hwnd);
    }

    /// <summary>
    /// 把窗口挂到桌面图标层。成功返回 true；找不到桌面层时返回 false（保持普通窗口行为）。
    /// </summary>
    public static bool AttachToDesktopLayer(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !WindowInterop.IsWindow(hwnd)) return false;

        IntPtr defView = FindDesktopIconView();
        if (defView == IntPtr.Zero) return false;

        try
        {
            lock (s_gate)
            {
                if (!s_originalOwners.ContainsKey(hwnd))
                {
                    s_originalOwners[hwnd] = WindowInterop.GetWindowLong(hwnd, WindowInterop.GWLP_HWNDPARENT);
                }
            }

            WindowInterop.SetWindowLong(hwnd, WindowInterop.GWLP_HWNDPARENT, defView);
            WindowInterop.SetWindowPos(
                hwnd,
                WindowInterop.HWND_TOP,
                0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE |
                WindowInterop.SWP_NOSIZE |
                WindowInterop.SWP_NOACTIVATE |
                WindowInterop.SWP_NOOWNERZORDER |
                WindowInterop.SWP_SHOWWINDOW);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>脱离桌面层，恢复原始所有者。关闭窗口前必须调用，避免残留悬挂所有者。</summary>
    public static void DetachFromDesktopLayer(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            IntPtr original;
            lock (s_gate)
            {
                if (!s_originalOwners.TryGetValue(hwnd, out original)) return;
                s_originalOwners.Remove(hwnd);
            }

            WindowInterop.SetWindowLong(hwnd, WindowInterop.GWLP_HWNDPARENT, original);
            WindowInterop.SetWindowPos(
                hwnd,
                WindowInterop.HWND_NOTOPMOST,
                0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE |
                WindowInterop.SWP_NOSIZE |
                WindowInterop.SWP_NOACTIVATE);
        }
        catch
        {
            // 关闭路径上的失败不应影响退出流程
        }
    }

    /// <summary>
    /// 瞬态浮起：先 TOPMOST 再 NOTOPMOST，使窗口落在普通层级的顶部而不持有 topmost 属性。
    /// DeskBox 用它替代持久置顶，避免组件挡住全屏应用。
    /// </summary>
    public static void RaiseTransient(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !WindowInterop.IsWindow(hwnd)) return;

        try
        {
            WindowInterop.SetWindowPos(
                hwnd,
                WindowInterop.HWND_TOPMOST,
                0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
            WindowInterop.SetWindowPos(
                hwnd,
                WindowInterop.HWND_NOTOPMOST,
                0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        }
        catch
        {
            // 层级调整失败不影响交互
        }
    }

    /// <summary>
    /// 按给定顺序（首元素最上层）重新排列一组同级窗口。
    /// 注意：SetWindowPos 调用成功并不保证最终顺序——由 Explorer 拥有的窗口
    /// 可能保留旧的 owner-group 顺序，因此 DeskBox 会再做一次校验。
    /// </summary>
    public static bool EnsurePeerOrderHighestToLowest(IReadOnlyList<IntPtr> handles)
    {
        if (handles.Count == 0) return false;

        bool allOk = true;
        try
        {
            for (int i = 1; i < handles.Count; i++)
            {
                if (handles[i] == IntPtr.Zero || handles[i - 1] == IntPtr.Zero) continue;
                if (!WindowInterop.IsWindow(handles[i]) || !WindowInterop.IsWindow(handles[i - 1])) continue;

                // 把 handles[i] 放到 handles[i-1] 之后（更低一层）
                allOk &= WindowInterop.SetWindowPos(
                    handles[i],
                    handles[i - 1],
                    0, 0, 0, 0,
                    WindowInterop.SWP_NOMOVE |
                    WindowInterop.SWP_NOSIZE |
                    WindowInterop.SWP_NOACTIVATE |
                    WindowInterop.SWP_NOOWNERZORDER);
            }
        }
        catch
        {
            return false;
        }

        return allOk;
    }

    /// <summary>
    /// 按空闲期策略重排一组组件窗口：位置靠下的组件保持在上方，
    /// 使上方组件的投影不会压暗下方组件的顶边。
    /// </summary>
    public static bool ApplyIdleOrder(IReadOnlyList<WidgetZOrderCandidate> candidates)
    {
        IReadOnlyList<WidgetZOrderCandidate> ordered =
            WidgetZOrderPolicy.OrderHighestToLowest(candidates);
        if (ordered.Count == 0) return false;

        var handles = new List<IntPtr>(ordered.Count);
        foreach (WidgetZOrderCandidate candidate in ordered)
        {
            handles.Add(new IntPtr(candidate.WindowHandle));
        }

        return EnsurePeerOrderHighestToLowest(handles);
    }
}
