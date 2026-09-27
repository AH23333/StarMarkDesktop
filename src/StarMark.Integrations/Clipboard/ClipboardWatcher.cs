#nullable enable
using System;
using System.Linq;
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
    /// <param name="textMaxEntries">文本/文件那一路的保留上限（设置页的值经 <see cref="ClipboardWatcher.TextMaxEntries"/> 递进来）。
    /// 默认＝现行为，所以既有文本采集测试不必传它。</param>
    public static async Task<Item?> CaptureAsync(
        IItemRepository repo, ClipboardDedupe dedupe, string? rawText, string format,
        string? foregroundApp, long nowMs,
        int textMaxEntries = ClipboardPolicy.MaxEntries, CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (dedupe is null) throw new ArgumentNullException(nameof(dedupe));

        if (dedupe.ShouldSkip(rawText, nowMs)) return null;
        if (!ClipboardPolicy.ShouldRecord(rawText, foregroundApp, out var text)) return null;

        var draft = ClipboardEntry.Build(text, foregroundApp, format,
            DateTimeOffset.FromUnixTimeMilliseconds(nowMs));
        return await repo.RecordClipboardAsync(draft, ct, maxEntries: textMaxEntries);
    }

    /// <summary>
    /// 处理一帧<b>图片</b>。被挡掉（回声、突发重复、门禁、密码管理器来源、解不出像素）时返回 null。
    /// <para>
    /// 顺序是刻意的：<b>先解像素 → 门禁 → 去重 → 算身份 → 落库 → 最后才写文件</b>。
    /// 写盘放最后，是因为落库会告诉我们"这一条其实早就在历史里、该用哪个文件名"
    /// （<see cref="ClipboardEntry.MergeForReplay"/> 保住旧名字）——先写文件就会每次回放都落一个新名字，
    /// 旧文件立刻变孤儿，而 §3-Q6 明令不许在用户目录里攒没人认领的东西。
    /// </para>
    /// <para>全程线程池：PNG 编码与缩略图都是 WinRT 的异步调用，<b>不碰 UI 线程也不碰 STA</b>（§2/§5）。</para>
    /// </summary>
    internal static async Task<Item?> CaptureImageAsync(
        IItemRepository repo, ClipboardDedupe dedupe, ClipboardNative.ImageRead frame,
        string? foregroundApp, DateTimeOffset now,
        int imageMaxEntries = ClipboardPolicy.DefaultImageMaxEntries, CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (dedupe is null) throw new ArgumentNullException(nameof(dedupe));

        // 像素是这条链的唯一通货：身份哈希、缩略图、将来的贴图都吃它。PNG 路线也要先解出像素，
        // 否则"我们写回的图被系统重排成 DIB 再回来"就会字节不同 ⇒ 一次自回声（§2 回声那条的根据）。
        ClipboardPayload.ImageFrame? pixels = frame.Dib;
        string? whyNot = null;
        if (pixels is null && frame.Png is { Length: > 0 } png
            && ClipboardPayload.TryDecodePng(png, out var decoded, out whyNot)) pixels = decoded;
        if (pixels is not { } f)
        {
            // 原因要一起进日志：这一帧"没记上"是可接受的，"没人知道为什么没记上"不是
            //（历史上最难查的那类故障，最后都是一条日志里的原话定下来的）。
            StarLog.WarnThrottled("clip:image-decode",
                $"图片帧解不出像素（{frame.Container}{(string.IsNullOrEmpty(whyNot) ? "" : "：" + whyNot)}），这一帧没有记录",
                windowMs: 60_000);
            return null;
        }

        if (!ClipboardPolicy.ShouldRecordImage(f.Width, f.Height, f.Bgra.Length, foregroundApp, out var why))
        {
            StarLog.Info($"[剪贴板] 图片未记录：{why}");
            return null;
        }
        var nowMs = now.ToUnixTimeMilliseconds();
        if (dedupe.ShouldSkip(f.Bgra, nowMs)) return null;

        var sourceId = ClipboardPolicy.BuildImageSourceId(f.Bgra);
        // PNG 路线原样存字节（§2：不重编码，省下一次全图编解码，也保证"库里那份就是系统里那份"）；
        // DIB 路线才走全仓唯一那处编码器。字节数记的是**将要落盘的那份**，不是 DIB 的原始大小。
        var pngBytes = frame.Png ?? await ClipboardImageStore.EncodePngAsync(f.Bgra, f.Width, f.Height, ct);
        if (pngBytes is null or { Length: 0 })
        {
            StarLog.WarnThrottled("clip:image-encode", "PNG 编码失败，这一帧图片没有进历史", windowMs: 60_000);
            return null;
        }

        var draft = ClipboardEntry.BuildImage(sourceId,
            new ClipboardEntry.ImageMeta(ClipAssets.MainNameOf(sourceId, now), ClipAssets.ThumbNameOf(sourceId, now),
                f.Width, f.Height, pngBytes.LongLength),
            foregroundApp, now);
        // 上限按具名参数递进去（写成位置参数＝把它交给了文本桶，图片桶照旧吃默认值）：
        // 仓储层是按"这一行的 clipFormat"选桶的，两个上限各归各的，串了位不会报错、只会少留或多留。
        var item = await repo.RecordClipboardAsync(draft, ct, imageMaxEntries: imageMaxEntries);

        // 名字以合并后的行为准（回放保旧名）。文件已经在＝纯粹的一次"又复制了同一张图"，不用重写。
        var main = ClipboardEntry.FileName(item);
        if (main is null || ClipboardImageStore.MainExists(main)) return item;

        var thumb = ClipboardEntry.ThumbFileName(item);
        var jpeg = await ClipboardImageStore.EncodeThumbnailAsync(f.Bgra, f.Width, f.Height, ct);
        var (ok, error) = await ClipboardImageStore.WritePairAsync(main, thumb ?? main, pngBytes, jpeg);
        if (!ok)
            // 行已经在了、文件没写成：这是 §3-Q6 第一类的另一半来源。不静默——下一轮对账会打上
            // clipMissing，而此刻的日志是唯一能说清"为什么库里多了一行打不开的图"的东西。
            StarLog.Warn($"[剪贴板] 图片行已记录但文件写失败（{error}）：{main}");
        else if (jpeg is null)
            StarLog.Info($"[剪贴板] 缩略图没编出来（{frame.Container}），列表将回退解码主图：{main}");
        return item;
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
    /// 图片采集分开关。<b>默认 false</b>（§4：图片采集默认关），关掉时纯图片帧照旧丢弃——
    /// 那正是这个功能存在之前的行为，所以"没开"与"没装"在磁盘上完全一样。
    /// </summary>
    public volatile bool ImageCapture;

    private int _textMaxEntries = ClipboardPolicy.MaxEntries;
    private int _imageMaxEntries = ClipboardPolicy.DefaultImageMaxEntries;

    /// <summary>
    /// 文本/文件那一路的保留上限（§4：默认 500，设置页可改）。
    /// <para><b>写入口就夹住</b>（<see cref="ClipboardPolicy.ClampTextMaxEntries"/>）：设置页给的是用户手打的数字，
    /// 夹在读取侧等于"0 或负数也能存进去，只是用的时候再说"——而 0 会让轮转每记一条删一条。</para>
    /// </summary>
    public int TextMaxEntries
    {
        get => Volatile.Read(ref _textMaxEntries);
        set => Volatile.Write(ref _textMaxEntries, ClipboardPolicy.ClampTextMaxEntries(value));
    }

    /// <summary>图片那一路的保留上限（§4：默认 200，与文本各自一条线——一张 4K PNG 常有几百 KB）。</summary>
    public int ImageMaxEntries
    {
        get => Volatile.Read(ref _imageMaxEntries);
        set => Volatile.Write(ref _imageMaxEntries, ClipboardPolicy.ClampImageMaxEntries(value));
    }

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
                ClipboardNative.ImageRead? image = null;
                if (raw is null)
                {
                    // 文本/文件都没有才轮到图片：顺序就是优先级——一次复制既有文字又有图，
                    // 记下来的是文字那条（图片那一路只在没有文本可记时才存在）。
                    if (!ImageCapture) continue;        // 分开关关着＝与这个功能存在之前完全一样（连读都不读）
                    var (frame, why) = ClipboardNative.ReadImage();
                    if (frame is null)
                    {
                        if (why is not null) StarLog.Info($"[剪贴板] 图片帧没接：{why}");
                        continue;
                    }
                    image = frame;
                }

                // 读一帧要几十毫秒，期间用户可能刚把开关关掉：落库前再判一次，
                // 否则"关掉之后仍多记一条"正好是这个开关要避免的事。
                if (_stopped) break;

                await _writeGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_stopped) break;
                    var at = DateTimeOffset.UtcNow;
                    // 上限在这一刻现读（不是启动时快照一份）：用户刚在设置页把 200 改成 50，
                    // 下一条复制就该按 50 裁——采集与设置之间不该有"要重启才生效"那种事。
                    var item = image is { } img
                        ? await ClipboardCapture.CaptureImageAsync(
                            _repo, _dedupe, img, app, at, ImageMaxEntries, CancellationToken.None).ConfigureAwait(false)
                        : await ClipboardCapture.CaptureAsync(
                            _repo, _dedupe, raw, format, app,
                            at.ToUnixTimeMilliseconds(), TextMaxEntries, CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>
    /// 登记"这段内容是 StarMark 自己写进剪贴板的"（点条目复制回剪贴板时调用），采集时按回声挡掉。
    /// 时钟在这里现取：调用方（UI）不该关心毫秒口径，但令牌必须有寿命，否则一次失败的写入
    /// 会把该文本永久屏蔽掉（见 <see cref="ClipboardDedupe.NoteOwnWrite"/>）。
    /// </summary>
    public void NoteOwnWrite(string? text)
        => _dedupe.NoteOwnWrite(text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>
    /// 登记"这张图是 StarMark 自己写回剪贴板的"。<b>吃像素不吃文件名</b>：身份哈希算在归一后的 BGRA 上，
    /// 而系统会把我们写的 PNG 重排成 CF_DIB 再广播回来，字节必然不同——按字节或按名字登记都挡不住这一次。
    /// </summary>
    public void NoteOwnWrite(byte[]? pixels)
        => _dedupe.NoteOwnWrite(pixels, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public void Dispose()
    {
        Stop();
        _writeGate.Dispose();
    }
}

/// <summary>
/// 启动对账（批次 ClipIMG-1e，§3-Q6 三分类）：把"库里的图片行"与"clip 目录里的文件"对一次。
/// <para>
/// 判据本身在 <see cref="ClipAssets.Reconcile"/>（纯函数、可逐值断言）；这里只做三件 IO 事：
/// 读行、读目录、写回标记。<b>只有"行有图无"那一类允许写库</b>——而"图有行无"的孤儿
/// <b>一件都不删</b>：目录是给用户看的，静默批量删除是任何一次"我在帮你清理"都换不回来的一类动作。
/// </para>
/// <para>返回 null＝<b>没跑成</b>（权限、杀软、库被占）。调用方不许把 null 当成"一切正常"。</para>
/// </summary>
public static class ClipboardAssetAudit
{
    public static async Task<ClipAssets.ReconcileResult?> RunAsync(
        IItemRepository repo, CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        try
        {
            var rows = await repo.GetClipboardImageAssetsAsync(ct);
            var files = ClipboardImageStore.ListFiles();
            if (rows.Count == 0 && files.Count == 0)
                return ClipAssets.ReconcileResult.Empty;      // 没开过图片采集：连目录都不必存在，别为它写日志

            var names = new string[files.Count];
            for (var i = 0; i < files.Count; i++) names[i] = files[i].Name;
            var result = ClipAssets.Reconcile(rows, names);

            var requested = result.MissingRowIds.Count + result.RestoredRowIds.Count;
            if (requested > 0)
            {
                var changed = await repo.SetClipboardMissingFlagsAsync(
                    result.MissingRowIds, result.RestoredRowIds, ct);
                if (changed < requested)
                    StarLog.Warn($"[剪贴板] 对账有 {requested - changed} 行的缺失标记没能写上去（多半是 extra_json 坏了，下一轮还会再来）");
            }

            if (!result.NothingToDo)
                StarLog.Info($"[剪贴板] 图片对账：标为缺失 {result.MissingRowIds.Count} 条、"
                    + $"恢复 {result.RestoredRowIds.Count} 条、孤儿 {result.OrphanNames.Count} 件（按裁决未清理）、"
                    + $"临时件 {result.TempNames.Count} 件");
            return result;
        }
        catch (Exception ex)
        {
            StarLog.Error("剪贴板图片对账没跑成（不影响使用，下次启动再来）", ex);
            return null;
        }
    }
}
