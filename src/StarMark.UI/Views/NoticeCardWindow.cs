#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.UI.Helpers;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace StarMark.UI.Views;

/// <summary>
/// 右下角那张提示卡（批次 RV）：<b>本程序自己画的一扇置顶小窗</b>，取代"托盘气泡"那条链。
/// <para>
/// 为什么要换掉：托盘气泡是向系统请求一条挂在托盘图标上的通知，而它在 Windows 11 上
/// <b>API 返回 TRUE、屏幕上却什么都没有</b>（发起人原话："所有声称气泡效果的，未曾见到气泡效果，均无提示效果"）。
/// 那句返回值被当成"已经提醒过了"一路传上去，于是所有兜底分支按构造永远不会走——
/// 日志写着"提醒已发出"，用户一次都没看见。判据只能落在"屏幕上真的有这么一块"这件事上，
/// 所以下面的 <see cref="IsOnScreen"/> 问的是窗口的可见性与矩形，不是任何一发的返回值。
/// </para>
/// <para>
/// 四条硬约束都是这条链上别的窗踩过的：① <b>不抢前台</b>（点亮那一刻记下前台、亮完还回去，并走
/// <c>SW_SHOWNOACTIVATE</c>——抢了会把用户正在填的表单的打字吞进一扇空窗）；② 但<b>绝不能把窗设成
/// "不可激活"那一模</b>（真机上那样整扇窗收不到 XAML 的点击：卡看得见、点不动，而这张卡唯一的退出出口就是点它）；
/// ③ <b>topmost 两步都要</b>
/// ——先进带（<c>HWND_TOPMOST</c>）再带内重排（<c>HWND_TOP</c>），只传 TOP 的窗留在普通层，
/// 任何应用一激活就把卡盖住；④ 卡片<b>不接键盘</b>（它没有焦点），所以退出出口给鼠标：点一下即收。
/// </para>
/// </summary>
public sealed class NoticeCardWindow : Window
{
    /// <summary>卡片宽度（DIP）。<b>定宽</b>：跟着文案走的话一条长消息会把卡撑到半屏宽、下一条又缩回去，
    /// 用户看到的是角落里一块东西在跳。</summary>
    public const double CardWidthDip = 320;

    /// <summary>卡片停留多久后自己消失（秒）。界面与日志里那句"N 秒后自动消失"也从这里来，不另写一份。</summary>
    public const int KeepSeconds = 10;

    /// <summary>正文最多占多高（DIP），再长省略号收尾——完整内容始终进日志，卡只负责"让你知道有事发生"。</summary>
    private const double BodyMaxHeightDip = 104;

    /// <summary>卡片离工作区右缘与下缘的距离（DIP）。</summary>
    private const int EdgeMarginDip = 16;

    /// <summary>窗口比卡片多出的那一圈（防 DPI 取整把描边裁掉一半），与贴图条同款。</summary>
    private const int Slack = 2;

    /// <summary>卡片本体与窗底同色：WinUI 3 的客户区不能整块透明，留透明只会露出框架默认色（浅色主题下一片白）。</summary>
    private static readonly Color CardColor = Color.FromArgb(0xF2, 0x22, 0x22, 0x26);

    private readonly Border _card;
    private readonly TextBlock _title;
    private readonly TextBlock _body;
    private readonly DispatcherQueueTimer? _hide;

    private bool _shown;
    private bool _clickMounted;
    private bool _destroyed;
    private int _captureDepth;                      // 抓帧那几句套了几层，见 SetHiddenForCapture
    private bool _wasOnScreenBeforeCapture;         // 最外层收起来之前，这张卡是不是真的贴着
    private RectInt32 _rect;                        // 最近一次摆上去的实际矩形（物理像素），只用于日志

    public NoticeCardWindow()
    {
        Title = "StarMark 提示";
        _title = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _body = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = BodyMaxHeightDip,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _card = new Border
        {
            Width = CardWidthDip,
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(CardColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _title,
                    _body,
                    new TextBlock
                    {
                        Text = $"点一下关闭 · {KeepSeconds} 秒后自动消失",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    },
                },
            },
        };
        // 主题钉成暗色、不跟系统走：白字是按暗底画的，浅色主题下会糊成看不见；而提示卡随时可能出现，
        // 来不及跟着主题重铺（贴图条批次 WI 立下的同一条口径）。
        Content = new Grid
        {
            Background = new SolidColorBrush(CardColor),
            RequestedTheme = ElementTheme.Dark,
            Children = { _card },
        };
        WindowInterop.RemoveDefaultWindowFrame(this);       // 无框 + 不进任务栏/Alt+Tab
        Closed += (_, _) => _destroyed = true;
        _hide = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_hide is null)
            StarLog.Warn("[提示卡] 拿不到 DispatcherQueue，卡片不会自动消失（点一下仍然能收）");
        else
        {
            _hide.Interval = TimeSpan.FromSeconds(KeepSeconds);
            _hide.Tick += (_, _) => { _hide!.Stop(); HideCard(); };
        }
    }

    /// <summary>卡片此刻是否真的贴在屏幕上。判据是 <see cref="IsPlaced"/>（可见 + 有实际尺寸 + 落在某块屏的工作区内）。</summary>
    public bool IsOnScreen { get; private set; }

    /// <summary>
    /// 换一条内容并摆到<b>光标所在屏</b>的右下角，重新开始计时。返回卡片是否真的贴上了屏幕。
    /// <para>已经在屏幕上时收到新消息是<b>就地替换</b>而不是叠一张：角落里堆三张卡，用户只会看见最上面那张，
    /// "到底提醒了几次"这件事在界面上就没了凭据。</para>
    /// </summary>
    public bool Apply(string title, string message)
    {
        if (_destroyed) return false;
        _title.Text = title ?? string.Empty;
        _body.Text = message ?? string.Empty;

        var monitor = WindowInterop.MonitorWorkAreaAtCursor();
        if (monitor is null)
        {
            // 连光标在哪块屏都问不出来：老实报告"没能贴上屏幕"，让调用方去写日志或退到主窗提示条，
            // 而不是留一扇停在框架默认位置的窗冒充提醒。
            HideCard();
            return false;
        }
        var (work, scale) = monitor.Value;
        var dpi = scale <= 0 ? 1.0 : scale;

        var hwnd = WindowInterop.GetHwnd(this);
        if (!_shown)
        {
            // 先把窗按"卡片该有的大致尺寸"摆进那块屏的角落再点亮：Show 之前窗还住在框架的默认位置与尺寸，
            // 中间只要有一次呈现就会被看见——同一条消息不该先在屏幕中央闪一大块再跑到角落。
            // 这一轮的后续几句（量、摆、提层）都在同一个 UI 线程回合里跑完，所以这里给的是估计值，最终以实测为准。
            var estimatedHeight = (int)Math.Round(120 * dpi) + Slack * 2;
            var estimatedWidth = (int)Math.Round(CardWidthDip * dpi) + Slack * 2;
            AppWindow.MoveAndResize(new RectInt32(
                work.X + work.Width - estimatedWidth,
                work.Y + work.Height - estimatedHeight - (int)Math.Round(EdgeMarginDip * dpi),
                estimatedWidth,
                estimatedHeight));

            // 亮出来再量：窗没亮过时模板尚未应用，那一次 Measure 量到的是没排过版的尺寸（贴图条同一条坑）。
            // 点亮这一下会把前台抢过来——先记下前台是谁，亮完立刻还回去（卡片不需要焦点，它只负责被看见）。
            var previous = WindowInterop.GetForegroundWindow();
            AppWindow.Show();
            _shown = true;
            if (previous != IntPtr.Zero && previous != hwnd) WindowInterop.SetForegroundWindow(previous);
        }

        // 摆完一定要再问一次"屏幕上真的有这一块吗"：算出来的坐标对不对，只有拿窗口的实际矩形去对才作数
        // （托盘气泡那一版就是拿"API 返回 true"当凭据，结果什么都没有）。
        IsOnScreen = Place(hwnd, work, dpi) && IsPlaced(hwnd);
        MountClickToClose();       // 挂在摆放之后：上面那发 Show 自己会激活一次，先挂上就等于卡片出现那一刻把自己收了
        if (IsOnScreen)
        {
            // 每条提醒都留下几何：这一批的缺陷正是"日志说发了、屏幕上没有"，而事后唯一能分清两者的就是这一句。
            StarLog.Info($"[提示卡] 已贴上屏幕：{title}（{_rect.X},{_rect.Y} {_rect.Width}x{_rect.Height}，"
                + $"工作区 {work.X},{work.Y} {work.Width}x{work.Height}，缩放 {(int)Math.Round(dpi * 100)}%，"
                + $"{KeepSeconds} 秒后自动消失）");
            RestartHoldTimer();
        }
        else HideCard();
        return IsOnScreen;
    }

    /// <summary>
    /// 摆位：右下角<b>贴工作区</b>（不是整屏——压在任务栏上会盖住开始菜单，也读不全）。
    /// <b>右缘与下缘钉住</b> ⇒ 文案长短只让卡片往左、往上扩，不会出现"每来一条跳一次"。
    /// </summary>
    private bool Place(IntPtr hwnd, RectInt32 work, double dpi)
    {
        // 卡片定宽、高度跟正文行数走，所以每写完一次文案都要重新量、再按实测尺寸摆。
        _card.Measure(new Size(CardWidthDip, double.PositiveInfinity));
        var wanted = _card.DesiredSize;
        var width = (int)Math.Round(wanted.Width * dpi) + Slack * 2;
        var height = (int)Math.Round(wanted.Height * dpi) + Slack * 2;
        if (width < 8 || height < 8 || work.Width < 8 || work.Height < 8) return false;
        width = Math.Min(width, Math.Max(1, work.Width));
        height = Math.Min(height, Math.Max(1, work.Height));
        var margin = (int)Math.Round(EdgeMarginDip * dpi);
        // 卡片比工作区还宽/还高时取那一端的原点（Math.Clamp 在 hi<lo 时直接抛）。
        var x = Math.Max(work.X, work.X + work.Width - width - margin);
        var y = Math.Max(work.Y, work.Y + work.Height - height - margin);

        // 两步提层：先进 topmost 带，再在带内重排（只传 TOP 会留在普通层，别的窗一激活就盖住它）。
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, x, y, width, height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        _rect = new RectInt32(x, y, width, height);
        return true;
    }

    /// <summary>
    /// 点一下即收，<b>只挂一次</b>（这条窗是复用的，每次 Apply 再挂就会累积成"点一下收掉又立刻被下一颗处理器叫回来"）。
    /// 两路一起挂：第一次点击可能被系统当作"激活这扇窗"而吞掉输入（只发 <see cref="Window.Activated"/>、不发点击），
    /// 只挂一路的症状就是"点了没反应，得点第二下"。
    /// </summary>
    private void MountClickToClose()
    {
        if (_clickMounted) return;
        _clickMounted = true;
        _card.PointerPressed += (_, _) => HideCard();
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.PointerActivated) HideCard();
        };
    }

    /// <summary>
    /// "真的贴上屏幕"的凭据：<b>窗口可见 + 有实际尺寸 + 矩形落在某块屏的工作区内</b>。
    /// <para>刻意不是"某个 API 返回了 true"——上一版就是那么判的，于是屏幕上什么都没有而日志写着已发出。</para>
    /// </para>
    /// </summary>
    private static bool IsPlaced(IntPtr hwnd)
    {
        try
        {
            if (!WindowInterop.IsWindowVisible(hwnd)) return false;
            if (!WindowInterop.GetWindowRect(hwnd, out var r)) return false;
            var rect = new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return rect.Width >= 8 && rect.Height >= 8 && !WindowInterop.IsOffScreen(rect);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[提示卡] 验屏失败，按\"没能贴上屏幕\"处理：{ex.Message}");
            return false;
        }
    }

    private void RestartHoldTimer()
    {
        if (_hide is null) return;
        _hide.Stop();
        _hide.Start();
    }

    /// <summary>收卡（不关窗：这扇窗复用，下一条消息直接换内容）。</summary>
    public void HideCard()
    {
        _hide?.Stop();
        IsOnScreen = false;
        if (_destroyed) return;
        try { WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE); }
        catch (Exception ex) { StarLog.Warn($"[提示卡] 收起失败：{ex.Message}"); }
    }

    /// <summary>
    /// 截图抓那一帧前后<b>成对</b>调用（口径同 <c>CanvasService.SetHiddenForCapture</c>）：
    /// 抓屏抓的是已经合成好的屏幕，事后减不回来，所以"这张卡进不进图"只能在那一帧之前收起来决定。
    /// <para><b>按层数记，不按布尔</b>：截图那条链上"抓帧"与"不带画布的那一帧"两处都要收，套起来时
    /// 里层那句会把外层的"本来贴着"覆盖成 false，出来时没人还——症状正是"截过一次图之后提示卡再也不出现"。
    /// 收起时定时器一起停，所以这几毫秒不会因为过期而丢掉那一次还原。</para>
    /// </summary>
    public void SetHiddenForCapture(bool hidden)
    {
        if (_destroyed) return;
        if (hidden)
        {
            if (_captureDepth == 0) _wasOnScreenBeforeCapture = IsOnScreen;
            _captureDepth++;
            if (_captureDepth == 1 && IsOnScreen) HideCard();
            return;
        }
        if (_captureDepth == 0) return;
        if (--_captureDepth > 0) return;
        if (!_wasOnScreenBeforeCapture) return;
        _wasOnScreenBeforeCapture = false;
        var monitor = WindowInterop.MonitorWorkAreaAtCursor();
        if (monitor is null) return;
        var hwnd = WindowInterop.GetHwnd(this);
        IsOnScreen = Place(hwnd, monitor.Value.Work, monitor.Value.Scale <= 0 ? 1.0 : monitor.Value.Scale)
            && IsPlaced(hwnd);
        if (IsOnScreen) RestartHoldTimer();
    }
}
