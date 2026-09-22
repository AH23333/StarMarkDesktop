#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.SystemTray;

namespace StarMark.Integrations.Clipboard;

/// <summary>
/// 一次剪贴板通知的<b>裁决与落库</b>链路：去重 → 策略 → 建条目 → 写库。
/// <para>
/// 与 Win32 完全解耦（<c>nowMs</c> 与前台进程名都由调用方给），因此这条链能在单机上被完整单测：
/// 真实采集窗口只是"读到什么"的来源。安全闸门集中在策略层，采集窗口本身没有绕过它的能力。
/// </para>
/// </summary>
public static class ClipboardCapture
{
    /// <summary>
    /// 处理一帧剪贴板内容。返回被记录/更新的条目；被挡掉（自身回声、突发重复、过短、敏感内容、
    /// 密码管理器来源）时返回 null。
    /// </summary>
    /// <param name="foregroundApp">复制发生时前台应用的进程名（null＝取不到）。排除清单据此生效。</param>
    public static async Task<Item?> CaptureAsync(
        IItemRepository repo, ClipboardDedupe dedupe, string? rawText, string format,
        string? foregroundApp, long nowMs, CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (dedupe is null) throw new ArgumentNullException(nameof(dedupe));

        if (dedupe.ShouldSkip(rawText, nowMs)) return null;
        if (!ClipboardPolicy.ShouldRecord(rawText, foregroundApp, out var text)) return null;

        var draft = ClipboardEntry.Build(text, foregroundApp, format,
            DateTimeOffset.FromUnixTimeMilliseconds(nowMs));
        return await repo.RecordClipboardAsync(draft, ct);
    }
}

/// <summary>
/// 剪贴板采集窗口：一个 <c>HWND_MESSAGE</c> 消息窗口 + <c>AddClipboardFormatListener</c>，
/// 收到 <c>WM_CLIPBOARDUPDATE</c> 后读内容、交给 <see cref="ClipboardCapture"/> 落库。
/// <para>
/// <b>为什么不挂在托盘窗口上</b>：托盘受「常驻托盘」开关控制，关掉托盘就等于悄悄停掉历史采集，
/// 而用户看到的仍是"剪贴板历史已开启"。独立窗口让两个开关各管各的。
/// </para>
/// <para>
/// <b>WndProc 里绝不做 IO</b>：读剪贴板要 OpenClipboard（可能被别的应用短暂占住），落库要写 SQLite，
/// 两者都可能阻塞。全部丢线程池；回调只做"置脏位 + 按需起一轮"，于是同一时刻最多一轮在途，
/// 期间到达的通知合并成一轮——既不丢最后一次复制，也不会为一次连发通知起 N 个任务。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClipboardWatcher : IDisposable
{
    private const string ClassName = "StarMarkClipboardListener";

    // 类只注册一次、委托静态保活：非托管侧只持有函数指针，委托一旦被 GC 就成了野指针回调 ⇒ 进程崩。
    private static NativeMethods.WndProcDelegate? _sharedProc;
    private static ushort _classAtom;

    /// <summary>
    /// 当前唯一的派发目标。<b>刻意不用 <c>GWLP_USERDATA</c> 传实例</b>：
    /// <c>CreateWindowExW</c> 的 <c>lpParam</c> 只会出现在 <c>WM_NCCREATE</c> 的 CREATESTRUCT 里，
    /// <b>不会</b>自动落进 GWLP_USERDATA；而该原生偏移在 SDK 头文件里有"等于 GWL_USERDATA(-21)"与
    /// "-(sizeof(LONG_PTR)*2)+1"两种并存写法，取错了不会编译报错、也不会抛异常，只会让 WndProc
    /// 永远读到 0 ⇒ <b>一条都不记，而界面照样显示"已开始记录"</b>——最难查的那种失效。
    /// 本监听窗全进程只有一个，静态目标在结构上排除了这一整类失效（也顺带消掉 GCHandle 与
    /// 消息派发的释放时序竞争：不再有"非托管持有的裸指针可能已 Free"这回事）。
    /// </summary>
    private static ClipboardWatcher? _current;

    private readonly IItemRepository _repo;
    private readonly ClipboardDedupe _dedupe = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private IntPtr _hwnd;
    private int _inFlight;    // 0/1：是否有在途读取轮
    private int _dirty;       // 0/1：在途期间又来过通知

    /// <summary>
    /// 已停用。Stop 之后在途那一轮必须立刻收手——否则"用户刚关掉剪贴板历史"与"最后一次复制"
    /// 赛跑，关掉之后仍会多落一条，而这正是用户关掉这个开关想避免的东西。
    /// </summary>
    private volatile bool _stopped;

    /// <summary>用户主动暂停（临时粘贴私密内容）。暂停期间通知照收、内容不落库。</summary>
    public volatile bool Paused;

    /// <summary>
    /// 是否已在采集。<b>名字刻意不叫 IsAvailable</b>，且语义严格限定为"监听窗口已建立、派发已接上、
    /// 未被停用"——三者是一起成立或一起不成立的（见 <see cref="TryStart"/> 里的赋值顺序）。
    /// 它仍然证明不了"系统真的在给我们发通知"，那一条只能真机验收。
    /// </summary>
    public bool IsRunning => !_stopped && Volatile.Read(ref _hwnd) != IntPtr.Zero;

    public ClipboardWatcher(IItemRepository repo) => _repo = repo;

    /// <summary>
    /// 建监听窗口并开始采集。<b>必须在有消息泵的线程上调用</b>（主窗口 UI 线程）：消息窗口不显示，
    /// 但它的 WndProc 靠所属线程的消息队列驱动。
    /// </summary>
    public bool TryStart()
    {
        if (IsRunning) return true;
        try
        {
            EnsureClassRegistered();
            var hwnd = NativeMethods.CreateWindowExW(
                0, ClassName, "StarMark 剪贴板采集", NativeMethods.WS_POPUP,
                0, 0, 0, 0, NativeMethods.HWND_MESSAGE, IntPtr.Zero,
                NativeMethods.GetModuleHandleW(null), IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                StarLog.Error($"剪贴板采集窗口创建失败（CreateWindowExW=0, Win32={Marshal.GetLastWin32Error()}），历史不会记录");
                return false;
            }

            // 顺序要紧：先接上派发目标、最后才挂监听。反过来会留一条窄缝——监听已生效而派发还
            // 认不出这个窗口，期间到来的那条通知就永久丢了（WM_CLIPBOARDUPDATE 不会重发）。
            _stopped = false;
            _current = this;
            _hwnd = hwnd;

            if (!NativeMethods.AddClipboardFormatListener(_hwnd))
            {
                StarLog.Error($"AddClipboardFormatListener 失败（Win32={Marshal.GetLastWin32Error()}），历史不会记录");
                Stop();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("启动剪贴板采集失败", ex);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        // 先置停用位：让在途那一轮在下一句检查就收手，不再落库。
        _stopped = true;
        // 先把句柄摘走再销毁：DestroyWindow 会同步送回 WM_DESTROY，若此时 _hwnd 仍是它自己，
        // 派发路径会再 DestroyWindow 一次（同一句柄二次销毁）；摘零后 WM_DESTROY 分支的
        // 句柄比对自然失败，重入在结构上不可能发生。
        var hwnd = Interlocked.Exchange(ref _hwnd, IntPtr.Zero);
        if (ReferenceEquals(_current, this)) _current = null;
        if (hwnd == IntPtr.Zero) return;

        try { NativeMethods.RemoveClipboardFormatListener(hwnd); }
        catch (Exception ex) { StarLog.Warn($"移除剪贴板监听失败（忽略）：{ex.Message}"); }
        try { NativeMethods.DestroyWindow(hwnd); }
        catch (Exception ex) { StarLog.Warn($"销毁剪贴板采集窗口失败（忽略，随线程退出回收）：{ex.Message}"); }
    }

    /// <summary>
    /// 窗口在外面被毁掉（线程退出等）时的自清：<b>只摘状态，绝不再 DestroyWindow</b>
    /// ——在 WM_DESTROY 里对同一句柄再调一次是无效的（Win32 会直接返回失败）。
    /// </summary>
    private void OnWindowDestroyed()
    {
        Interlocked.Exchange(ref _hwnd, IntPtr.Zero);
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;

        _sharedProc ??= WndProcStatic;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _sharedProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = ClassName,
        };
        _classAtom = NativeMethods.RegisterClassExW(ref wc);
        if (_classAtom != 0) return;

        // 1410 = ERROR_CLASS_ALREADY_EXISTS：上一轮采集注册的类仍在（关掉托盘/开关来回切是常态），复用即可。
        if (Marshal.GetLastWin32Error() != 1410)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "注册剪贴板监听窗口类失败");
    }

    private static IntPtr WndProcStatic(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 原生 WNDPROC 里逃出的异常会直接终结常驻进程（与 TrayHost 同一教训）：全程兜住。
        try
        {
            // 句柄比对是为了挡住"旧窗口的尾讯"：Stop 之后系统仍可能把那条 WM_DESTROY/WM_CLIPBOARDUPDATE
            // 派进来，此时 _current 可能已指向重新开启的同一个实例（它的新句柄不同）⇒ 不认，直接丢。
            var self = _current;
            if (self is not null && Volatile.Read(ref self._hwnd) == hWnd && hWnd != IntPtr.Zero)
            {
                if (msg == NativeMethods.WM_CLIPBOARDUPDATE) self.ScheduleRead();
                else if (msg == NativeMethods.WM_DESTROY) self.OnWindowDestroyed();
            }
        }
        catch { /* 采集失败不能影响宿主 */ }
        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>置脏位并按需起一轮读取；在途期间的多次通知合并成一轮。</summary>
    private void ScheduleRead()
    {
        if (_stopped) return;
        Interlocked.Exchange(ref _dirty, 1);
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
        _ = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stopped && Interlocked.Exchange(ref _dirty, 0) == 1)
            {
                if (Paused) continue;

                var (raw, format, app) = ClipboardNative.ReadSnapshot();
                if (raw is null) continue;   // 这一帧没内容/被占用：等下一次通知

                // 读一帧要几十毫秒，期间用户可能刚把开关关掉：落库前再判一次，
                // 否则"关掉之后仍多记一条"正好是这个开关要避免的事。
                if (_stopped) break;

                await _writeGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_stopped) break;
                    var item = await ClipboardCapture.CaptureAsync(
                        _repo, _dedupe, raw, format, app,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CancellationToken.None).ConfigureAwait(false);
                    // 只记标题（首行、已折控制符），正文绝不进日志——日志会把历史变成明文副本的第二份。
                    if (item is not null) StarLog.Info($"剪贴板历史已记录：{item.Title}");
                }
                finally { _writeGate.Release(); }
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("剪贴板采集轮次失败（不影响下一次通知）", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
            // 在途结束时又来过的话补跑一轮，避免"最后一次复制没记上"。
            if (_dirty == 1 && !_stopped && !Paused) ScheduleRead();
        }
    }

    /// <summary>登记"这段内容是 StarMark 自己写进剪贴板的"（点条目复制回剪贴板时调用），采集时按回声挡掉。</summary>
    public void NoteOwnWrite(string? text) => _dedupe.NoteOwnWrite(text);

    public void Dispose()
    {
        Stop();
        _writeGate.Dispose();
    }
}
