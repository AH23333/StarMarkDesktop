#nullable enable
using System;
using System.Threading.Tasks;
using StarMark.Abstractions;
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
    public bool IsPlaying { get; set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    /// <summary>支持哪些控制（部分播放器不提供上一首/进度）。UI 据此禁用按钮。</summary>
    public bool CanPlayPause { get; set; }
    public bool CanSkipNext { get; set; }
    public bool CanSkipPrevious { get; set; }

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
    private bool _disposed;

    /// <summary>会话/曲目/播放状态任一变化。UI 订阅它做刷新。</summary>
    public event EventHandler? Changed;

    /// <summary>SMTC 是否可用（初始化失败时为 false）。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>最近一次读到的快照；未读到时为 null。</summary>
    public MediaSnapshot? Current { get; private set; }

    /// <summary>
    /// 初始化并订阅系统会话变化。
    /// 失败一律吞掉并置 <see cref="IsAvailable"/> = false —— 组件是常驻 UI，
    /// 这里冒异常会直接把崩溃甩到 UI 线程。
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager is null)
            {
                IsAvailable = false;
                return false;
            }

            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            IsAvailable = true;
            AttachSession(_manager.GetCurrentSession());
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
    {
        AttachSession(sender.GetCurrentSession());
        _ = RefreshAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    /// <summary>重新读取当前会话快照并触发 <see cref="Changed"/>。</summary>
    public async Task RefreshAsync()
    {
        if (!IsAvailable) return;
        try
        {
            var session = _manager?.GetCurrentSession();
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
                IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = playback?.Controls?.IsPauseEnabled == true || playback?.Controls?.IsPlayEnabled == true,
                CanSkipNext = playback?.Controls?.IsNextEnabled == true,
                CanSkipPrevious = playback?.Controls?.IsPreviousEnabled == true,
            };

            // 时间轴以 100ns 为单位（与 TimeSpan 的 tick 一致）；部分播放器不上报 EndTime，
            // 此时 EndTime 会等于 StartTime，这里判等避免算出一个 0 长度还拿去显示进度。
            if (timeline is not null)
            {
                snapshot.Position = TimeSpan.FromTicks(Math.Max(0, timeline.Position.Ticks - timeline.StartTime.Ticks));
                if (timeline.EndTime > timeline.StartTime)
                    snapshot.Duration = TimeSpan.FromTicks(timeline.EndTime.Ticks - timeline.StartTime.Ticks);
            }

            Current = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StarLog.Error("读取媒体会话失败", ex);
        }
    }

    public async Task<bool> TogglePlayPauseAsync()
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

    public async Task<bool> NextAsync() => await TryControlAsync(s => s.TrySkipNextAsync());

    public async Task<bool> PreviousAsync() => await TryControlAsync(s => s.TrySkipPreviousAsync());

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
            if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
        catch { /* 退订失败不影响退出 */ }
    }
}
