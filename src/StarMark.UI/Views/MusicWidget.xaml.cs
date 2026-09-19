#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StarMark.Abstractions;
using StarMark.Core.Media;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// Phase C 第三个新内容组件：**音乐**。
/// 走 Windows 系统媒体传输控制（SMTC）读取<span lang="en">系统当前播放会话</span>并控制播放，
/// 因此不需要对接任何具体播放器——Spotify、网易云、浏览器里正在播的视频都能管。
/// <para>
/// 与 DeskBox 的差异：DeskBox 的 MusicWidgetContent 还带音量条（走 CoreAudio 的
/// <c>MusicVolumeNativeBackend</c>）。StarMark 按路线图「播放控制优先，音量 v2」先不做音量。
/// 音源选择已对齐：多个会话时提供「跟随系统 / 指定播放器」下拉（见 MediaSessionService）。
/// </para>
/// </summary>
public sealed partial class MusicWidget : UserControl
{
    // 多个音乐组件共享一个会话服务：SMTC 是系统级单例，各建各的只会重复订阅事件。
    private static readonly MediaSessionService s_media = new();
    private static bool s_initialized;

    private DispatcherQueueTimer? _timer;

    /// <summary>
    /// 拖动进度条期间不理会 SMTC 推来的新进度：否则 1 秒一跳的刷新会把用户刚拖到的位置拽回去，
    /// 表现为"拖了又弹回来"。松手提交后才恢复同步（DeskBox 用同一套 _isSeeking 闸门）。
    /// </summary>
    private bool _seeking;
    private bool _canSeek;
    private double _seekRatio;

    /// <summary>
    /// 进度推算基准（最后一次真实读到的位置 + 读到它的时刻）。
    /// <para>
    /// 早先的写法是每秒把"快照里的 Position + 1 秒"再画一遍 —— 但快照对象是<b>不会自己走</b>的，
    /// 于是每一跳都从同一个旧位置 +1s，进度条永远停在原地，只有点击按钮触发一次真刷新才动一下。
    /// 正确做法：记住基准点与时刻，按墙钟推算（SMTC 的时间轴事件大约 1 秒一次，够准了）。
    /// </para>
    /// </summary>
    private TimeSpan _tickBase = TimeSpan.Zero;
    private DateTimeOffset _tickBaseAt;
    private int _ticksSinceSync;

    /// <summary>
    /// 实际播放倍速（1.0 = 常速）。SMTC 的时间轴只给「位置」，不给倍速，
    /// 而按 1× 墙钟推算在 2× 播放时进度条会越走越慢（用户报「倍速播放时进度条与实际不符」）。
    /// 这里用相邻两次<b>真实</b>读数的位移 / 时间差反推倍速，播放器换倍速后两秒内自动跟上。
    /// </summary>
    private double _rate = 1.0;
    private TimeSpan _lastRealPosition = TimeSpan.Zero;
    private DateTimeOffset _lastRealAt;
    private bool _hasRateSample;

    public MusicWidget()
    {
        InitializeComponent();

        Unloaded += (_, _) =>
        {
            _timer?.Stop();
            s_media.Changed -= Media_Changed;
            s_media.SessionsChanged -= Media_SessionsChanged;
        };
        Loaded += async (_, _) =>
        {
            s_media.Changed -= Media_Changed;
            s_media.Changed += Media_Changed;
            s_media.SessionsChanged -= Media_SessionsChanged;
            s_media.SessionsChanged += Media_SessionsChanged;

            if (!s_initialized)
            {
                s_initialized = true;
                await s_media.InitializeAsync();
            }
            // 先备好计时器再渲染：Render 里会根据"是否在播放"决定启停，
            // 顺序反了就会出现"首次加载播放中却不走进度"。
            StartTimer();
            Render(s_media.Current, s_media.IsAvailable);
            RefreshSourcePicker();
        };
    }

    private void Media_Changed(object? sender, EventArgs e) => Render(s_media.Current, s_media.IsAvailable);

    private void Media_SessionsChanged(object? sender, EventArgs e) => RefreshSourcePicker();

    /// <summary>
    /// 重建「切换音源」菜单。只有一个会话时整块隐藏 —— 这时候让用户选反而是负担，
    /// 直接跟随系统就是他要的行为（DeskBox 的会话选择器同理，只是它常驻显示）。
    /// </summary>
    private void RefreshSourcePicker()
    {
        SourceFlyout.Items.Clear();
        var options = s_media.SessionOptions;

        SourceButton.Visibility = options.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        if (options.Count < 2) return;

        var follow = new RadioMenuFlyoutItem
        {
            Text = "跟随系统",
            IsChecked = s_media.PreferredSessionId is null,
        };
        follow.Click += async (_, _) => await PickSessionAsync(null);
        SourceFlyout.Items.Add(follow);
        SourceFlyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var option in options)
        {
            var id = option.SessionId;
            var item = new RadioMenuFlyoutItem
            {
                Text = option.IsPlaying ? $"{option.DisplayName} · 播放中" : option.DisplayName,
                IsChecked = s_media.PreferredSessionId == id,
            };
            item.Click += async (_, _) => await PickSessionAsync(id);
            SourceFlyout.Items.Add(item);
        }
    }

    private async Task PickSessionAsync(string? sessionId)
    {
        await s_media.SetPreferredSessionAsync(sessionId);
        RefreshSourcePicker();
        Render(s_media.Current, s_media.IsAvailable);
    }

    /// <summary>
    /// 1 秒一跳：① 推进进度条；② 定期回源（3 秒一次）。
    /// 回源不能省——部分播放器换下一个视频/音频时压根不发 <c>MediaPropertiesChanged</c>，
    /// 纯事件驱动就永远看不到切歌。没有曲目时由 <see cref="Render"/> 停表，避免常驻空转。
    /// </summary>
    private void StartTimer()
    {
        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            // 必须显式声明重复：DispatcherQueueTimer 默认 IsRepeating=false，
            // 只 Start() 会「滴答一次就停」——表现就是进度条跳一秒后再也不动（踩坑 #84）。
            _timer.IsRepeating = true;
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
        }
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_seeking) return;   // 拖动中：位置由指针说了算

        var snapshot = s_media.Current;
        if (snapshot is null || !snapshot.HasTrack) return;

        if (snapshot.IsPlaying && snapshot.Duration > TimeSpan.Zero)
        {
            // 按「基准位置 + 墙钟流逝 × 实际倍速」推算，而不是拿不动的快照做加法（那样进度条会永远停在原地）。
            // 乘上倍速：1× 之外（0.5× / 1.5× / 2×）时按 1× 推算会让进度条系统性偏慢/偏快。
            var elapsed = DateTimeOffset.Now - _tickBaseAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            var pos = _tickBase + TimeSpan.FromTicks((long)(elapsed.Ticks * _rate));

            if (pos >= snapshot.Duration)
            {
                // 到点了：可能是本曲放完要切下一首，推算已经不准，必须回源读一次真实状态
                SetProgress(snapshot.Duration, snapshot.Duration);
                _ = s_media.RefreshAsync();
                return;
            }

            // 不重画整块：只推进进度，避免每秒重排文本
            SetProgress(pos, snapshot.Duration);
        }

        // 定期回源校正：
        // ① 推算会累积误差；
        // ② 播放器可能被别处（键盘媒体键、系统弹窗）改了状态；
        // ③ **部分播放器（尤其浏览器里换下一个视频/音频）不广播 MediaPropertiesChanged**，
        //    不轮询就永远看不到曲目切换——这正是「切歌了组件还显示上一首」的根因；
        // ④ 倍速只能靠回源反推，回源越勤，倍速识别越准（见 _rate）。
        // ResetTickBase（每次真实快照到达）会把计数归零，所以这里实际是「距上次真实刷新 2 秒」。
        if (++_ticksSinceSync >= 2)
        {
            _ticksSinceSync = 0;
            _ = s_media.RefreshAsync();
        }
    }

    private void Render(MediaSnapshot? snapshot, bool available)
    {
        if (!available)
        {
            TitleBlock.Text = "无法访问媒体会话";
            ArtistBlock.Text = "系统未开放播放信息（部分非打包应用受限）";
            SourceBlock.Text = string.Empty;
            SetEnabled(false);
            SetProgress(TimeSpan.Zero, TimeSpan.Zero);
            _timer?.Stop();
            return;
        }

        if (snapshot is null || !snapshot.HasTrack)
        {
            TitleBlock.Text = "未在播放";
            ArtistBlock.Text = "打开任意播放器后这里会显示曲目";
            SourceBlock.Text = string.Empty;
            SetEnabled(false);
            SetProgress(TimeSpan.Zero, TimeSpan.Zero);
            _timer?.Stop();
            return;
        }

        TitleBlock.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "未知曲目" : snapshot.Title;
        ArtistBlock.Text = string.IsNullOrWhiteSpace(snapshot.Subtitle) ? "未知艺人" : snapshot.Subtitle;
        // 显示友好名而不是 SMTC 的原始 AUMID（那串带包名下划线的东西用户读不懂）
        SourceBlock.Text = string.IsNullOrWhiteSpace(snapshot.SourceName)
            ? snapshot.AppId
            : snapshot.SourceName;

        // 图标表示「点击后会发生什么」：播放中显示暂停键，暂停时显示播放键（与系统播放控件一致）。
        // 之前显示反了是因为刷新在非 UI 线程上失败（RPC_E_WRONG_THREAD），UI 拿到的是上一次的陈旧快照。
        PlayIcon.Glyph = snapshot.IsPlaying ? "\uE769" : "\uE768";   // 暂停 / 播放
        ToolTipService.SetToolTip(PlayButton, snapshot.IsPlaying ? "暂停" : "播放");
        PrevButton.IsEnabled = snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.CanSkipNext;
        PlayButton.IsEnabled = snapshot.CanPlayPause;

        // 播放模式按钮：随机/循环都不支持时直接禁用，别给一个永远点不动的按钮
        _canSeek = snapshot.CanSeek;
        var canChangeMode = snapshot.CanChangeShuffle || snapshot.CanChangeRepeat;
        ModeButton.IsEnabled = canChangeMode;
        ModeButton.Visibility = canChangeMode ? Visibility.Visible : Visibility.Collapsed;
        ModeIcon.Glyph = MusicPlaybackModeMath.Glyph(snapshot.PlaybackMode);
        ModeIcon.Opacity = snapshot.PlaybackMode == MusicPlaybackMode.Normal ? 0.55 : 1.0;
        ToolTipService.SetToolTip(ModeButton, MusicPlaybackModeMath.Label(snapshot.PlaybackMode));

        // 每次拿到真实快照就把推算基准归零，否则本地推算会一直叠在旧基准上
        ResetTickBase(snapshot.Position);
        SetProgress(snapshot.Position, snapshot.Duration);
        // 有曲目就保持轮询：暂停中也要回源，否则用系统媒体键/播放器窗口按了播放，
        // 组件要等到下一次事件才反应（部分播放器根本不发事件）。
        _timer?.Start();
    }

    /// <summary>重新设定进度推算基准（真实快照到达 / seek 提交后调用），并顺带反推倍速。</summary>
    private void ResetTickBase(TimeSpan position)
    {
        var now = DateTimeOffset.Now;

        // 用相邻两次真实读数反推倍速。仅在「播放中 + 间隔够长 + 位置正向推进」时取样：
        // 切歌 / seek / 暂停恢复都会让位置跳变，那些必须丢掉，否则会算出 20× 这种离谱值。
        if (_hasRateSample)
        {
            var gap = now - _lastRealAt;
            var delta = position - _lastRealPosition;
            if (gap >= TimeSpan.FromMilliseconds(400) && delta > TimeSpan.Zero)
            {
                var observed = delta.TotalSeconds / gap.TotalSeconds;
                // 合理倍速区间（0.25×–8×）之外一律视为跳变，保持原值
                if (observed >= 0.25 && observed <= 8.0)
                    _rate = _rate * 0.3 + observed * 0.7;   // 平滑，避免抖动
            }
            else if (delta <= TimeSpan.Zero)
            {
                _rate = 1.0;   // 位置倒退 = 切歌或跳转，倍速回到常速重估
            }
        }

        _lastRealPosition = position;
        _lastRealAt = now;
        _hasRateSample = true;

        _tickBase = position;
        _tickBaseAt = now;
        _ticksSinceSync = 0;
    }

    private void SetEnabled(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        PrevButton.IsEnabled = enabled;
        NextButton.IsEnabled = enabled;
        ModeButton.IsEnabled = enabled;
        if (!enabled) { _canSeek = false; _seeking = false; }
    }

    private void SetProgress(TimeSpan position, TimeSpan duration)
    {
        if (_seeking) return;   // 拖动中：位置由指针说了算，别被刷新拽回去

        if (duration <= TimeSpan.Zero)
        {
            Progress.Value = 0;
            PositionBlock.Text = "0:00";
            DurationBlock.Text = "0:00";
            return;
        }
        Progress.Value = Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 100.0, 0, 100);
        PositionBlock.Text = FormatTime(position);
        DurationBlock.Text = FormatTime(duration);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    // ── 进度跳转（seek）──

    private void SeekHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_canSeek) return;
        _seeking = true;
        // 捕获指针：拖出进度条那 16px 高之后（比如甩到下面的按钮上）仍能收到 move/release，
        // 否则松手事件丢失，_seeking 会一直卡在 true，进度条从此不再更新。
        SeekHost.CapturePointer(e.Pointer);
        UpdateSeekFromPointer(e);
        e.Handled = true;
    }

    private void SeekHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_seeking) return;
        UpdateSeekFromPointer(e);
        e.Handled = true;
    }

    private async void SeekHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        // 指针捕获可能已经被系统收回（比如拖出窗口），此时释放会抛；
        // 事件处理器里冒异常会直接崩进程，这里一律吞掉。
        try { SeekHost.ReleasePointerCapture(e.Pointer); } catch { }
        e.Handled = true;
        await CommitSeekAsync();
    }

    private void UpdateSeekFromPointer(PointerRoutedEventArgs e)
    {
        var width = SeekHost.ActualWidth;
        if (width <= 0) return;

        var x = e.GetCurrentPoint(SeekHost).Position.X;
        _seekRatio = Math.Clamp(x / width, 0.0, 1.0);
        Progress.Value = _seekRatio * 100.0;

        // 拖动时只改文本与条，不发请求 —— 每移动一像素就 seek 一次会把播放器打爆
        var snapshot = s_media.Current;
        if (snapshot is not null && snapshot.Duration > TimeSpan.Zero)
            PositionBlock.Text = FormatTime(TimeSpan.FromSeconds(snapshot.Duration.TotalSeconds * _seekRatio));
    }

    private async Task CommitSeekAsync()
    {
        var snapshot = s_media.Current;
        if (snapshot is null || snapshot.Duration <= TimeSpan.Zero) return;

        var target = TimeSpan.FromSeconds(snapshot.Duration.TotalSeconds * _seekRatio);
        // 先把基准挪到目标点，否则下一次 tick 会用旧基准把进度拽回跳转前
        ResetTickBase(target);
        SetProgress(target, snapshot.Duration);
        await s_media.SeekAsync(target);
        await s_media.RefreshAsync();
    }

    private async void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        await s_media.CyclePlaybackModeAsync();
        await s_media.RefreshAsync();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        await s_media.TogglePlayPauseAsync();
        await s_media.RefreshAsync();
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        await s_media.PreviousAsync();
        await s_media.RefreshAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await s_media.NextAsync();
        await s_media.RefreshAsync();
    }
}
