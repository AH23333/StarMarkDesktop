#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Hotkeys;
using StarMark.UI.Helpers;

namespace StarMark.UI.Services;

/// <summary>
/// 全局快捷键服务（Win32 <c>RegisterHotKey</c> + 窗口子类化接收 <c>WM_HOTKEY</c>）。
/// 移植并简化自 DeskBox <c>GlobalHotkeyService</c>：手势模型 + 动作映射 + 冲突允许。
/// <list type="bullet">
///   <item>同一手势可绑定多个动作，触发时全部执行（用户可借此一次完成多个操作）。</item>
///   <item>冲突不阻止保存：<see cref="HotkeyBindings.GetConflicts"/> 仅用于 UI 提示。</item>
///   <item>注册失败（被其它程序占用）→ 该手势跳过，不影响其余手势，并记入 <see cref="RegistrationFailures"/> 供界面标注。</item>
/// </list>
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<string, Func<Task>> _handlers = new();
    private readonly Dictionary<int, HotkeyGesture> _idToGesture = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _gestureToActions = new();
    private IntPtr _hwnd;
    private readonly Dictionary<IntPtr, Win32Hotkey.SubclassProc> _procKeepAlive = new();
    private Win32Hotkey.SubclassProc? _proc;
    private bool _disposed;

    /// <summary>最近一次应用的绑定（Resume 时据此恢复）。</summary>
    private IReadOnlyDictionary<string, HotkeyGesture> _lastBindings
        = new Dictionary<string, HotkeyGesture>();

    /// <summary>是否处于挂起态（录制快捷键时临时停用，避免按下的键命中已生效的热键而误触发动作）。</summary>
    private bool _suspended;

    /// <summary>
    /// 最近一次注册<b>失败</b>的手势（组合键已被其它程序占用 ⇒ RegisterHotKey 返回 false）。
    /// <para>
    /// 暴露它是因为"只写日志"等于没说：设置页那一行照样显示着用户刚录的组合、保存也算成功，
    /// 用户看到的却是"设了但永远不生效"，且没有任何地方解释原因（P-54 口径：失败要看得见、点得动）。
    /// </para>
    /// <para>挂起（录制中）不清空——录制不改变注册结果；<see cref="Resume"/> 与下一次应用绑定会刷新。</para>
    /// </summary>
    public IReadOnlyList<HotkeyGesture> RegistrationFailures { get; private set; } = Array.Empty<HotkeyGesture>();

    /// <summary>
    /// 挂起所有已注册热键（仅注销 OS 层注册，不丢弃绑定）。
    /// 录入快捷键时必须先挂起：否则用户按下的组合键若命中某个已生效的热键
    /// （例如正在重录「隐藏主界面」却按下了它的旧键），会当场执行该动作并打断录制。
    /// </summary>
    public void Suspend()
    {
        if (_hwnd == IntPtr.Zero) { _suspended = true; return; }
        foreach (var regId in _idToGesture.Keys.ToList()) Win32Hotkey.UnregisterHotKey(_hwnd, regId);
        _idToGesture.Clear();
        _gestureToActions.Clear();
        _suspended = true;
    }

    /// <summary>恢复热键（按最近一次应用的绑定重新注册）。</summary>
    public void Resume()
    {
        _suspended = false;
        ApplyBindings(_lastBindings);
    }

    /// <summary>注册动作处理器（动作 id → 执行逻辑）。</summary>
    public void RegisterHandler(string actionId, Func<Task> handler) => _handlers[actionId] = handler;

    /// <summary>
    /// 兜底处理器：未在 <see cref="_handlers"/> 中登记的动作（如用户事后新增的布局方案动作）
    /// 走这里，因此布局增删后无需逐个重新注册。
    /// </summary>
    public Func<string, Task>? FallbackHandler { get; set; }

    /// <summary>子类化主窗口以接收 WM_HOTKEY。主窗口常驻（最小化到托盘时句柄仍有效）。</summary>
    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = OnSubclassProc;
        _procKeepAlive[hwnd] = _proc;
        Win32Hotkey.SetWindowSubclass(hwnd, _proc, UIntPtr.Zero, UIntPtr.Zero);
    }

    /// <summary>应用一组绑定：先按手势归并（<see cref="HotkeyBindings.GroupByGesture"/>），再逐个 RegisterHotKey。</summary>
    public void ApplyBindings(IReadOnlyDictionary<string, HotkeyGesture> bindings)
    {
        _lastBindings = bindings;
        // 挂起期间只记录最新绑定，不真正注册；等 Resume() 再落地，避免录制中途热键反复挂摘。
        if (_suspended) return;
        if (_hwnd == IntPtr.Zero) return;
        foreach (var regId in _idToGesture.Keys.ToList()) Win32Hotkey.UnregisterHotKey(_hwnd, regId);
        _idToGesture.Clear();
        _gestureToActions.Clear();

        var id = 1;
        var failed = new List<HotkeyGesture>();
        foreach (var group in HotkeyBindings.GroupByGesture(bindings))
        {
            var g = group.Gesture;
            if (Win32Hotkey.RegisterHotKey(_hwnd, id, (uint)g.Modifiers, g.VirtualKey))
            {
                _idToGesture[id] = g;
                _gestureToActions[group.Key] = group.Actions;
                id++;
            }
            else
            {
                failed.Add(g);
                StarLog.Warn($"[Hotkey] 注册失败（可能被其它程序占用）: {HotkeyDisplay.Display(g)}");
            }
        }
        RegistrationFailures = failed;
    }

    private IntPtr OnSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == Win32Hotkey.WM_HOTKEY)
            _ = DispatchAsync((int)wParam);
        return Win32Hotkey.DefSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }

    private async Task DispatchAsync(int id)
    {
        if (!_idToGesture.TryGetValue(id, out var g)) return;
        var key = HotkeyGesture.GestureKey(g);
        if (!_gestureToActions.TryGetValue(key, out var actions)) return;
        foreach (var action in actions)
        {
            if (_handlers.TryGetValue(action, out var handler))
            {
                try { await handler(); }
                catch (Exception ex) { StarLog.Error($"[Hotkey] 动作执行失败: {action}", ex); }
            }
            else if (FallbackHandler is not null)
            {
                try { await FallbackHandler(action); }
                catch (Exception ex) { StarLog.Error($"[Hotkey] 动作执行失败: {action}", ex); }
            }
            else
            {
                StarLog.Warn($"[Hotkey] 未登记的动作: {action}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var id in _idToGesture.Keys.ToList()) Win32Hotkey.UnregisterHotKey(_hwnd, id);
            if (_proc is not null) Win32Hotkey.RemoveWindowSubclass(_hwnd, _proc, UIntPtr.Zero);
            _procKeepAlive.Remove(_hwnd);
        }
        _idToGesture.Clear();
        _gestureToActions.Clear();
    }
}
