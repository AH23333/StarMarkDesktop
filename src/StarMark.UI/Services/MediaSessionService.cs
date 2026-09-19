#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Core.Media;
using Windows.Media.Control;

namespace StarMark.UI.Services;

/// <summary>当前媒体会话的一次快照（曲目 + 播放状态 + 进度）。</summary>
public sealed class MediaSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    /// <summary>来源应用（SMTC 的 SourceAppUserModelId，如 Spotify.exe 的 AUMID）。</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>来源应用的友好名（SMTC 的 AUMID 是一长串包名，直接展示用户读不懂）。</summary>
    public string SourceName { get; set; } = string.Empty;
    public bool IsPlaying { get; set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    /// <summary>支持哪些控制（部分播放器不提供上一首/进度）。UI 据此禁用按钮。</summary>
    public bool CanPlayPause { get; set; }
    public bool CanSkipNext { get; set; }
    public bool CanSkipPrevious { get; set; }
    /// <summary>是否允许跳转进度（播放器上报了 IsPlaybackPositionEnabled 且时间轴长度已知）。</summary>
    public bool CanSeek { get; set; }
    /// <summary>是否允许切换随机播放。</summary>
    public bool CanChangeShuffle { get; set; }
    /// <summary>是否允许切换重复模式。</summary>
    public bool CanChangeRepeat { get; set; }
    /// <summary>当前播放模式（普通 / 随机 / 列表循环）。</summary>
    public MusicPlaybackMode PlaybackMode { get; set; } = MusicPlaybackMode.Normal;
    /// <summary>
    /// 时间轴起点（100ns 为单位）。SMTC 的 seek 用的是<b>绝对</b>时间轴刻度，
    /// 而 <see cref="Position"/> 是相对起点的偏移，跳转时必须把起点加回去。
    /// </summary>
    public long TimelineStartTicks { get; set; }

    /// <summary>
    /// <see cref="Position"/> 经 <c>LastUpdatedTime</c> 补偿后所对应的<b>真实读取时刻</b>（墙钟）。
    /// 进度推算基准必须锚定到这个时刻，而不是 UI 渲染时刻——否则读取延迟（100~900ms）会
    /// 让进度条系统性慢拍、并在每次回源校正的瞬间被拉回，表现为「在相差约 1 秒的两个时刻间反复横跳」。
    /// </summary>
    public DateTimeOffset TimelineUpdatedAt { get; set; }

    /// <summary>标题为空时说明「没有正在播放的会话」，组件据此显示占位。</summary>
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);

    /// <summary>展示用的艺人/专辑行，两者都有时用「·」连接。</summary>
    public string Subtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Album))
                return $"{Artist} · {Album}";
            return !string.IsNullOrWhiteSpace(Artist) ? Artist : Album;
        }
    }
}

/// <summary>
/// 音源下拉里的一项。<paramref name="SessionId"/> 是「应用 AUMID + 同名序号」合成的稳定标识：
/// 同一个播放器可以开多个会话（比如多个浏览器窗口），光靠 AUMID 区分不开。
/// </summary>
public sealed record MediaSessionOption(
    string SessionId,
    string DisplayName,
    bool IsPlaying,
    bool IsSystemCurrent);

/// <summary>
/// Windows 系统媒体传输控制（SMTC）的封装。
/// <para>
/// SMTC 是系统级聚合层：Spotify / 网易云 / 浏览器里的 YouTube 等只要向系统上报了播放状态，
/// 都能在这里读到并控制，因此音乐组件<b>不需要对接任何具体播放器</b>——
/// 这也是 DeskBox <c>MusicSessionService</c> 的做法。
/// </para>
/// <para>
/// 注意：非打包（unpackaged）桌面应用调用 <c>RequestAsync()</c> 在部分系统上会失败，
/// 此时 <see cref="IsAvailable"/> 为 false，组件显示占位而不是崩。
/// </para>
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _preferredSessionId;
    private bool _disposed;

    /// <summary>
    /// UI 线程调度器（在 <see cref="InitializeAsync"/> 里抓取，那时必在 UI 线程）。
    /// <para>
    /// 为什么必须有它：SMTC 是套间亲和的 WinRT 对象，<c>MediaPropertiesChanged</c> /
    /// <c>PlaybackInfoChanged</c> / <c>TimelinePropertiesChanged</c> 由系统在<b>工作线程</b>上派发。
    /// 早先直接在这些回调里读会话并同步刷新 UI，结果 XAML 侧 <c>TextBlock.Text</c> 赋值抛
    /// <c>RPC_E_WRONG_THREAD (0x8001010E)</c>，异常一路冒泡回 <c>RefreshAsync</c> 被吞掉 ——
    /// 表现为「曲目/进度只在你点按钮时才更新」（点按钮是在 UI 线程发起的，那次刷新能成功）。
    /// </para>
    /// 因此：凡是碰 WinRT 会话对象、或会触发 <see cref="Changed"/> 的调用，一律封送回 UI 线程。
    /// </summary>
    private DispatcherQueue? _ui;

    /// <summary>会话缓存：(合成 id, WinRT 会话)。会话变化时整体刷新一次，避免开菜单时反复枚举。</summary>
    private readonly List<(string Id, GlobalSystemMediaTransportControlsSession Session)> _sessions = new();
    private List<MediaSessionOption> _sessionOptions = new();

    /// <summary>可选的播放来源列表（不含「跟随系统」这一项，UI 自行加）。</summary>
    public IReadOnlyList<MediaSessionOption> SessionOptions => _sessionOptions;

    /// <summary>重叠刷新闸门（几个 SMTC 事件常连发，合并成一次读取）。</summary>
    private bool _refreshing;
    /// <summary>
    /// 闸门期间又来了新事件 —— 早先的做法是<b>直接丢弃</b>，结果「曲目事件」被「时间轴事件」的
    /// 在途刷新吞掉，用户看到的就是**切了歌但组件还显示旧曲目**。改成记脏位、在途结束后补跑一次。
    /// </summary>
    private bool _refreshPending;

    /// <summary>会话/曲目/播放状态任一变化。UI 订阅它做刷新。</summary>
    public event EventHandler? Changed;

    /// <summary>系统里的会话增删（播放器开关）。UI 据此重建音源下拉。</summary>
    public event EventHandler? SessionsChanged;

    /// <summary>
    /// 用户手选的音源。null = 跟随系统当前会话（默认行为，和 DeskBox 一致）。
    /// 选了某个播放器后就不再被"谁最后播放"抢走。
    /// </summary>
    public string? PreferredSessionId => _preferredSessionId;

    /// <summary>SMTC 是否可用（初始化失败时为 false）。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>最近一次读到的快照；未读到时为 null。</summary>
    public MediaSnapshot? Current { get; private set; }

    // ── 线程封送 ──
    // 说明见 _ui 字段注释：SMTC 回调在工作线程，所有会话访问与 UI 通知都必须回到 UI 线程。

    private Task RunOnUiAsync(Func<Task> work)
    {
        if (_ui is null || _ui.HasThreadAccess) return work();

        var tcs = new TaskCompletionSource();
        if (!_ui.TryEnqueue(async () =>
        {
            try { await work(); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }))
        {
            tcs.TrySetResult();   // 队列已关闭（应用退出）：不让它挂住调用方
        }
        return tcs.Task;
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> work)
    {
        if (_ui is null || _ui.HasThreadAccess) return work();

        var tcs = new TaskCompletionSource<T>();
        if (!_ui.TryEnqueue(async () =>
        {
            try { tcs.TrySetResult(await work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }))
        {
            tcs.TrySetResult(default!);
        }
        return tcs.Task;
    }

    /// <summary>
    /// 初始化并订阅系统会话变化。
    /// 失败一律吞掉并置 <see cref="IsAvailable"/> = false —— 组件是常驻 UI，
    /// 这里冒异常会直接把崩溃甩到 UI 线程。
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            // 必须在 UI 线程调用：后面的封送要以它为基准
            _ui = DispatcherQueue.GetForCurrentThread();
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager is null)
            {
                IsAvailable = false;
                return false;
            }

            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            IsAvailable = true;
            RefreshSessionOptions();
            AttachSession(ResolveSession());
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("SMTC 初始化失败（非打包应用在某些系统上无法访问系统媒体会话）", ex);
            IsAvailable = false;
            return false;
        }
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session)) return;

        if (_session is not null)
        {
            // 换会话必须退订旧的，否则旧会话的回调还会继续触发刷新
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }

        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => _ = RunOnUiAsync(async () =>
        {
            // 手选了音源就不跟着系统切：用户明确要听某个播放器时，别的软件开播不该抢走组件。
            RefreshSessionOptions();
            if (_preferredSessionId is null) AttachSession(sender.GetCurrentSession());
            await RefreshCoreAsync();
        });

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => _ = RunOnUiAsync(async () =>
        {
            RefreshSessionOptions();
            SessionsChanged?.Invoke(this, EventArgs.Empty);
            await RefreshCoreAsync();
        });

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    // ── 音源（会话）选择 ──

    /// <summary>
    /// 重新枚举系统会话。两个坑：
    /// <para>
    /// 1) 同一个播放器可能有多个会话（多窗口），因此 id 用「AUMID + 同名序号」合成，而不是直接用 AUMID。
    /// 2) 会话可能在枚举后立刻消失（播放器退出），读它的状态时 rt 会抛；这里逐项 try，
    ///    跳过失效项即可 —— 少一个音源条目远好过把异常甩到 UI 线程。
    /// </para>
    /// </summary>
    private void RefreshSessionOptions()
    {
        if (_manager is null) return;

        _sessions.Clear();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var session in _manager.GetSessions())
            {
                var appId = session.SourceAppUserModelId ?? string.Empty;
                var ordinal = seen.TryGetValue(appId, out var n) ? n : 0;
                seen[appId] = ordinal + 1;
                _sessions.Add((CreateSessionId(appId, ordinal), session));
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("枚举媒体会话失败", ex);
        }

        var rawNames = _sessions.Select(s => GetSourceDisplayName(GetAppId(s.Session))).ToList();
        var displayNames = DisambiguateSourceDisplayNames(rawNames);

        // 读「系统当前会话」也可能因播放器刚退出而抛（枚举成功不代表会话还活着），
        // 这里单独兜住：少一个「播放中」标记远好过把异常甩出去打断整次刷新。
        GlobalSystemMediaTransportControlsSession? systemCurrent = null;
        try { systemCurrent = _manager.GetCurrentSession(); }
        catch (Exception ex) { StarLog.Error("读取系统当前媒体会话失败", ex); }

        var options = new List<MediaSessionOption>(_sessions.Count);
        for (var i = 0; i < _sessions.Count; i++)
        {
            try
            {
                var playback = _sessions[i].Session.GetPlaybackInfo();
                options.Add(new MediaSessionOption(
                    _sessions[i].Id,
                    displayNames[i],
                    playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    IsSameSession(_sessions[i].Session, systemCurrent)));
            }
            catch (Exception ex)
            {
                StarLog.Error("读取媒体会话状态失败（播放器可能刚退出）", ex);
            }
        }

        _sessionOptions = options;

        // 手选的音源若已随播放器退出一起消失，自动回到"跟随系统"，否则组件会一直显示空占位。
        if (_preferredSessionId is { } id && !_sessions.Any(s => s.Id == id))
            _preferredSessionId = null;
    }

    /// <summary>
    /// SMTC 把「随机」和「循环」拆成两个独立开关，UI 只要一个按钮，这里折叠成三态。
    /// 循环包含 Track/List 两种，都算 Repeat（用户只关心"会不会重播"）。
    /// </summary>
    private static MusicPlaybackMode MapPlaybackMode(GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback)
    {
        if (playback is null) return MusicPlaybackMode.Normal;
        if (playback.IsShuffleActive == true) return MusicPlaybackMode.Shuffle;
        return playback.AutoRepeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.None
            ? MusicPlaybackMode.Normal
            : MusicPlaybackMode.Repeat;
    }

    private static string GetAppId(GlobalSystemMediaTransportControlsSession session)
    {
        try { return session.SourceAppUserModelId ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>切换音源。<paramref name="sessionId"/> 为 null 时回到「跟随系统」，成功后立刻重新读曲目。</summary>
    public Task<bool> SetPreferredSessionAsync(string? sessionId)
        => RunOnUiAsync(() => SetPreferredSessionCoreAsync(sessionId));

    private async Task<bool> SetPreferredSessionCoreAsync(string? sessionId)
    {
        if (!IsAvailable) { _preferredSessionId = sessionId; return false; }

        RefreshSessionOptions();
        if (sessionId is null)
        {
            _preferredSessionId = null;
            AttachSession(_manager?.GetCurrentSession());
            await RefreshAsync();
            return true;
        }

        var match = _sessions.FirstOrDefault(s => s.Id == sessionId).Session;
        if (match is null) return false;

        _preferredSessionId = sessionId;
        AttachSession(match);
        await RefreshAsync();
        return true;
    }

    /// <summary>手选优先，其次系统当前会话。</summary>
    private GlobalSystemMediaTransportControlsSession? ResolveSession()
    {
        if (_preferredSessionId is { } id)
        {
            var match = _sessions.FirstOrDefault(s => s.Id == id).Session;
            if (match is not null) return match;
        }
        return _manager?.GetCurrentSession();
    }

    /// <summary>AUMID + 分隔符 + 同名序号。分隔符用不可见控制字符，避免与应用名里的字符撞车。</summary>
    public static string CreateSessionId(string appUserModelId, int sourceOrdinal) =>
        string.Concat(
            appUserModelId,
            "\u001F",
            Math.Max(0, sourceOrdinal).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// 把 AUMID 翻成"人话"应用名。SMTC 的 SourceAppUserModelId 长这样：
    /// <c>Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic</c>，直接给用户看很难读。
    /// 常见播放器逐个映射，其余按顺序取短名。
    /// </summary>
    public static string GetSourceDisplayName(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId)) return string.Empty;

        var normalized = sourceAppUserModelId.Trim();
        var lower = normalized.ToLowerInvariant();

        // 用 Contains 而非相等字符串流程：AUMID 前后可能带包名/版本，精确匹配会漏。
        (string Key, string Name)[] known =
        {
            ("qqmusic", "QQ音乐"),
            ("cloudmusic", "网易云音乐"),
            ("netease", "网易云音乐"),
            ("msedge", "Microsoft Edge"),
            ("chrome", "Google Chrome"),
            ("firefox", "Mozilla Firefox"),
            ("spotify", "Spotify"),
            ("foobar2000", "foobar2000"),
            ("itunes", "iTunes"),
            ("vlc", "VLC"),
            ("potplayer", "PotPlayer"),
            ("zunemusic", "Windows Media Player"),
            ("media.player", "Windows Media Player"),
        };
        foreach (var (key, name) in known)
        {
            if (lower.Contains(key, StringComparison.Ordinal)) return name;
        }

        var firstSegment = normalized.Split('!')[0];
        if (firstSegment.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(firstSegment);

        var dotted = firstSegment.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return dotted.Length > 0 ? dotted[^1] : firstSegment;
    }

    /// <summary>同名音源补序号：两个 Chrome 会话要显示成「Google Chrome (1)/(2)」才分得清。</summary>
    public static IReadOnlyList<string> DisambiguateSourceDisplayNames(IReadOnlyList<string> displayNames)
    {
        var totals = displayNames
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new string[displayNames.Count];

        for (var i = 0; i < displayNames.Count; i++)
        {
            var name = displayNames[i];
            if (totals[name] <= 1)
            {
                result[i] = name;
                continue;
            }

            var ordinal = ordinals.TryGetValue(name, out var current) ? current + 1 : 1;
            ordinals[name] = ordinal;
            result[i] = $"{name} ({ordinal})";
        }

        return result;
    }

    private static bool IsSameSession(
        GlobalSystemMediaTransportControlsSession? left,
        GlobalSystemMediaTransportControlsSession? right) =>
        left is not null && right is not null &&
        (ReferenceEquals(left, right) || left.Equals(right));

    /// <summary>
    /// 重新读取当前会话快照并触发 <see cref="Changed"/>。
    /// 入口统一走这里封送到 UI 线程（SMTC 回调在工作线程，直接刷 UI 会抛 RPC_E_WRONG_THREAD）。
    /// </summary>
    public Task RefreshAsync() => RunOnUiAsync(RefreshCoreAsync);

    /// <summary>真正的读取逻辑。调用方必须已在 UI 线程。</summary>
    private async Task RefreshCoreAsync()
    {
        if (!IsAvailable) return;
        // 多个事件（曲目 / 播放状态 / 时间轴）常在几毫秒内连发，
        // 重叠刷新只会重复打 WinRT 并让 UI 反复重排，这里合并成一次。
        // 但合并≠丢弃：期间到来的事件置脏位，结束后补跑，保证最后一次变化一定被读到。
        if (_refreshing) { _refreshPending = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                await ReadSnapshotAsync();
            }
            while (_refreshPending && IsAvailable && !_disposed);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>读一次会话快照并触发 <see cref="Changed"/>。调用方必须已在 UI 线程。</summary>
    private async Task ReadSnapshotAsync()
    {
        try
        {
            var session = ResolveSession();
            // 顺手把当前解析到的会话挂上（引用没变时 AttachSession 会直接返回，不会有退订/重订抖动）
            AttachSession(session);
            if (session is null)
            {
                Current = null;
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var snapshot = new MediaSnapshot
            {
                Title = props?.Title ?? string.Empty,
                Artist = props?.Artist ?? string.Empty,
                Album = props?.AlbumTitle ?? string.Empty,
                AppId = session.SourceAppUserModelId ?? string.Empty,
                SourceName = GetSourceDisplayName(GetAppId(session)),
                IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = playback?.Controls?.IsPauseEnabled == true || playback?.Controls?.IsPlayEnabled == true,
                CanSkipNext = playback?.Controls?.IsNextEnabled == true,
                CanSkipPrevious = playback?.Controls?.IsPreviousEnabled == true,
                CanChangeShuffle = playback?.Controls?.IsShuffleEnabled == true,
                CanChangeRepeat = playback?.Controls?.IsRepeatEnabled == true,
                PlaybackMode = MapPlaybackMode(playback),
            };

            // 时间轴以 100ns 为单位（与 TimeSpan 的 tick 一致）；部分播放器不上报 EndTime，
            // 此时 EndTime 会等于 StartTime，这里判等避免算出一个 0 长度还拿去显示进度。
            if (timeline is not null)
            {
                var start = timeline.StartTime.Ticks;
                snapshot.TimelineStartTicks = start;

                // 总长优先取 EndTime；浏览器/部分播放器只给 MinSeekTime~MaxSeekTime，
                // 这时 EndTime==StartTime，长度会算成 0（进度条永远 0%），故回退到可跳转区间。
                var end = timeline.EndTime.Ticks;
                if (end <= start && timeline.MaxSeekTime.Ticks > timeline.MinSeekTime.Ticks)
                    end = timeline.MaxSeekTime.Ticks;

                snapshot.Position = TimeSpan.FromTicks(Math.Max(0, timeline.Position.Ticks - start));
                if (end > start) snapshot.Duration = TimeSpan.FromTicks(end - start);

                // 时间轴是播放器「上一次上报」的快照（多数 1 秒一次，个别更慢），
                // 直接用 Position 画出来的进度永远慢半拍 —— 用户看到的就是「进度条和实际不符」。
                // 播放中按 LastUpdatedTime 把到此刻的流逝补回去（只在合理区间内补，防止脏时间戳把条拉爆）。
                if (snapshot.IsPlaying)
                {
                    var updated = timeline.LastUpdatedTime;
                    if (updated > DateTimeOffset.MinValue)
                    {
                        var delta = DateTimeOffset.Now - updated;
                        if (delta > TimeSpan.Zero && delta < TimeSpan.FromSeconds(15))
                            snapshot.Position += delta;
                    }
                }

                if (snapshot.Duration > TimeSpan.Zero && snapshot.Position > snapshot.Duration)
                    snapshot.Position = snapshot.Duration;
            }

            // 记录「补偿后 Position 所对应的真实读取时刻」：进度推算基准必须锚定到这里，
            // 而不是 UI 渲染时刻（见 MusicWidget.ResetTickBase）。
            snapshot.TimelineUpdatedAt = DateTimeOffset.Now;

            // 只有「播放器允许跳进度」且「时间轴长度已知」时才让进度条可拖，
            // 否则拖了也跳不动，用户会以为组件坏了。
            snapshot.CanSeek = playback?.Controls?.IsPlaybackPositionEnabled == true && snapshot.Duration > TimeSpan.Zero;

            Current = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StarLog.Error("读取媒体会话失败", ex);
        }
    }

    public Task<bool> TogglePlayPauseAsync() => RunOnUiAsync(TogglePlayPauseCoreAsync);

    private async Task<bool> TogglePlayPauseCoreAsync()
    {
        if (_session is null) return false;
        try
        {
            var playback = _session.GetPlaybackInfo();
            return playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                ? await _session.TryPauseAsync()
                : await _session.TryPlayAsync();
        }
        catch (Exception ex)
        {
            StarLog.Error("播放/暂停失败", ex);
            return false;
        }
    }

    public Task<bool> NextAsync() => RunOnUiAsync(() => TryControlAsync(s => s.TrySkipNextAsync()));

    public Task<bool> PreviousAsync() => RunOnUiAsync(() => TryControlAsync(s => s.TrySkipPreviousAsync()));

    /// <summary>
    /// 跳转到指定进度。<paramref name="position"/> 是<b>相对起点</b>的偏移（与 <see cref="MediaSnapshot.Position"/> 同口径），
    /// 而 SMTC 的 <c>TryChangePlaybackPositionAsync</c> 收的是时间轴<b>绝对</b>刻度，所以这里要把起点加回去。
    /// 少加这一步在起点不为 0 的播放器上会跳到错误位置。
    /// </summary>
    public Task<bool> SeekAsync(TimeSpan position) => RunOnUiAsync(() => SeekCoreAsync(position));

    private async Task<bool> SeekCoreAsync(TimeSpan position)
    {
        var session = ResolveSession() ?? _session;
        if (session is null) return false;

        var snapshot = Current;
        if (snapshot is null || !snapshot.CanSeek) return false;

        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero
            : position > snapshot.Duration ? snapshot.Duration
            : position;

        try
        {
            return await session.TryChangePlaybackPositionAsync(snapshot.TimelineStartTicks + clamped.Ticks);
        }
        catch (Exception ex)
        {
            StarLog.Error("跳转进度失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 循环切换播放模式（普通 → 随机 → 列表循环 → 普通），缺哪个能力就跳过哪一态。
    /// 随机与循环在 SMTC 里是两个独立开关，切到某一态时必须把另一个关掉，否则会同时亮着。
    /// </summary>
    public Task<bool> CyclePlaybackModeAsync() => RunOnUiAsync(CyclePlaybackModeCoreAsync);

    private async Task<bool> CyclePlaybackModeCoreAsync()
    {
        var session = ResolveSession() ?? _session;
        if (session is null) return false;

        try
        {
            var playback = session.GetPlaybackInfo();
            if (playback?.Controls is null) return false;

            var current = Current?.PlaybackMode ?? MusicPlaybackMode.Normal;
            var next = MusicPlaybackModeMath.NextMode(
                current,
                playback.Controls.IsShuffleEnabled,
                playback.Controls.IsRepeatEnabled);
            if (next == current) return false;

            return await ApplyPlaybackModeAsync(session, playback, next);
        }
        catch (Exception ex)
        {
            StarLog.Error("切换播放模式失败", ex);
            return false;
        }
    }

    private static async Task<bool> ApplyPlaybackModeAsync(
        GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playback,
        MusicPlaybackMode mode)
    {
        var controls = playback.Controls;
        var shuffleOn = playback.IsShuffleActive == true;
        var repeatOn = playback.AutoRepeatMode != Windows.Media.MediaPlaybackAutoRepeatMode.None;
        var changed = false;

        // 目标态是随机/普通时都要先关循环，是循环/普通时都要先关随机 —— 两个开关互斥。
        var wantShuffle = mode == MusicPlaybackMode.Shuffle;
        var wantRepeat = mode == MusicPlaybackMode.Repeat;

        if (wantShuffle && !controls.IsShuffleEnabled) return false;
        if (wantRepeat && !controls.IsRepeatEnabled) return false;

        if (!wantRepeat && repeatOn && controls.IsRepeatEnabled)
            changed |= await session.TryChangeAutoRepeatModeAsync(Windows.Media.MediaPlaybackAutoRepeatMode.None);
        if (!wantShuffle && shuffleOn && controls.IsShuffleEnabled)
            changed |= await session.TryChangeShuffleActiveAsync(false);
        if (wantShuffle && !shuffleOn)
            changed |= await session.TryChangeShuffleActiveAsync(true);
        if (wantRepeat && !repeatOn)
            changed |= await session.TryChangeAutoRepeatModeAsync(Windows.Media.MediaPlaybackAutoRepeatMode.List);

        return changed;
    }

    /// <summary>
    /// 注意委托的返回类型是 <see cref="Windows.Foundation.IAsyncOperation{TResult}"/> 而不是
    /// <c>Task&lt;bool&gt;</c>：SMTC 是 WinRT API，TrySkipNextAsync 等方法返回的是 IAsyncOperation，
    /// 它不能隐式转成 Task（CS0266）。await 两者都行，但要作为委托返回值就必须写 WinRT 类型。
    /// </summary>
    private async Task<bool> TryControlAsync(
        Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> action)
    {
        if (_session is null) return false;
        try { return await action(_session); }
        catch (Exception ex)
        {
            StarLog.Error("媒体控制失败", ex);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            AttachSession(null);
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
            }
        }
        catch { /* 退订失败不影响退出 */ }
    }
}
