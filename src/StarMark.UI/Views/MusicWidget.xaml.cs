#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
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
            Render(s_media.Current, s_media.IsAvailable);
            RefreshSourcePicker();
            StartTimer();
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
    /// 1 秒一跳只为推进进度条；曲目信息靠 SMTC 事件驱动，不靠轮询。
    /// 没有正在播放的会话时停表，避免常驻空转。
    /// </summary>
    private void StartTimer()
    {
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        var snapshot = s_media.Current;
        if (snapshot is { IsPlaying: true } && snapshot.Duration > TimeSpan.Zero)
        {
            // 不重画整块：只推进进度，避免每秒重排文本
            var pos = snapshot.Position + TimeSpan.FromSeconds(1);
            if (pos > snapshot.Duration) pos = snapshot.Duration;
            SetProgress(pos, snapshot.Duration);
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

        PlayIcon.Glyph = snapshot.IsPlaying ? "\uE769" : "\uE768";   // 暂停 / 播放
        PrevButton.IsEnabled = snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.CanSkipNext;
        PlayButton.IsEnabled = snapshot.CanPlayPause;

        SetProgress(snapshot.Position, snapshot.Duration);
        if (snapshot.IsPlaying && snapshot.Duration > TimeSpan.Zero) _timer?.Start();
        else _timer?.Stop();
    }

    private void SetEnabled(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        PrevButton.IsEnabled = enabled;
        NextButton.IsEnabled = enabled;
    }

    private void SetProgress(TimeSpan position, TimeSpan duration)
    {
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
