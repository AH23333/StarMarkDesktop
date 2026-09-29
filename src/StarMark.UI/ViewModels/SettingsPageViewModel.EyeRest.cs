#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Canvas;
using StarMark.Core.Hotkeys;
using StarMark.Core.Insights;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using Windows.UI;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——护眼/休息提醒这一头：档位换算、开关写盘、状态那一行的措辞、以及「试一试」。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    // ===== 护眼 / 休息提醒（批次 WA）=====

    /// <summary>
    /// 护眼总开关，<b>默认关</b>：不请自来的遮罩是最讨人嫌的一种"帮忙"。
    /// 翻位即起停那张节拍表（与剪贴板开关同一口径：回报<b>实际</b>在不在跑，而不是"用户点了开"）。
    /// </summary>
    [ObservableProperty] private bool _eyeRestEnabled;

    /// <summary>间隔档位在 <see cref="StarMark.Core.Health.EyeRestPolicy.IntervalOptions"/> 里的下标（是档位不是滑杆）。</summary>
    [ObservableProperty] private int _eyeRestIntervalIndex;

    /// <summary>
    /// 提醒形式在那三档里的下标（提示卡／暗幕可跳／强制不可跳）。
    /// <para>批次 RS 之前这是一颗"强制模式"开关，于是<b>暗幕被绑在强制上</b>：想用暗幕就必须接受不能提前结束，
    /// 「试一试」也试不到暗幕。现在它是三档里选一档，"出现什么"与"能不能退出"各有位置。</para>
    /// </summary>
    [ObservableProperty] private int _eyeRestNoticeIndex;

    /// <summary>前台是全屏应用时让路（放 PPT / 放映不被砸）。默认开。</summary>
    [ObservableProperty] private bool _eyeRestDeferOnFullscreen;

    [ObservableProperty] private string _eyeRestStatus = string.Empty;

    /// <summary>「试一试」的结果回报（属性名含 Status＝不进自动保存，见 <c>IsDisplayOnlyProperty</c>）。</summary>
    [ObservableProperty] private string _eyeRestPreviewStatus = string.Empty;

    /// <summary>下拉的档位文案，与 <see cref="EyeRestIntervalIndex"/> 同序（一处事实：档位与文案都在 Core）。</summary>
    public IReadOnlyList<string> EyeRestIntervalOptions => StarMark.Core.Health.EyeRestPolicy.IntervalLabels;

    /// <summary>提醒形式的三档文案，与 <see cref="EyeRestNoticeIndex"/> 同序。</summary>
    public IReadOnlyList<string> EyeRestNoticeOptions => StarMark.Core.Health.EyeRestPolicy.NoticeLabels;

    private int EyeRestIntervalMinutes => StarMark.Core.Health.EyeRestPolicy.IntervalAt(EyeRestIntervalIndex);

    /// <summary>下拉那一档是哪一种提醒形式（越界由 Core 夹到两端，不抛）。</summary>
    private StarMark.Core.Health.EyeRestNotice CurrentEyeRestNotice => StarMark.Core.Health.EyeRestPolicy.NoticeAt(EyeRestNoticeIndex);

    private bool _suppressEyeRestApply;

    partial void OnEyeRestEnabledChanged(bool value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    partial void OnEyeRestIntervalIndexChanged(int value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    partial void OnEyeRestNoticeIndexChanged(int value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    partial void OnEyeRestDeferOnFullscreenChanged(bool value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    private void ApplyEyeRestSwitch()
    {
        _settings.SaveEyeRest(EyeRestEnabled, EyeRestIntervalMinutes, CurrentEyeRestNotice, EyeRestDeferOnFullscreen);
        App.ApplyEyeRest(EyeRestEnabled);
        EyeRestStatus = BuildEyeRestStatus();
        // 设置一变，上一次"试一试"演出来的就已经不是当前配置了：留着那行会读成"刚验证过现在的设置"
        EyeRestPreviewStatus = string.Empty;
    }

    /// <summary>
    /// 状态行说三件事：怎么提醒、全屏时让不让路、下一次大约几点。
    /// "开着但表没挂上"必须自己承认——用户没法用眼睛验证一张定时器在不在跑。
    /// <para>那一档怎么说取 <c>EyeRestPolicy.NoticeLabel</c>：与下拉里看到的是同一句，
    /// 免得状态行自己攒第二份"这一档会发生什么"（漏掉"点一下可提前结束"就是这一族最典型的形状）。</para>
    /// </summary>
    private string BuildEyeRestStatus()
    {
        if (!EyeRestEnabled)
            return "已关闭：不建定时器、不探测前台窗口，屏幕上不会出现任何东西。";
        var how = $"连续工作约 {EyeRestIntervalMinutes} 分钟后：{StarMark.Core.Health.EyeRestPolicy.NoticeLabel(CurrentEyeRestNotice)}";
        var defer = EyeRestDeferOnFullscreen ? "；前台是全屏应用（放 PPT / 放映）时自己让路" : "；全屏应用下也照常提醒";
        var running = StarMark.UI.Services.EyeRestService.IsRunning;
        var next = running && StarMark.UI.Services.EyeRestService.NextDueAt is { } due
            ? $"下一次大约 {due:HH:mm}。"
            : string.Empty;
        var warning = running ? string.Empty : "开关是开着的，但节拍表没挂上（原因见日志）——当前不会提醒。";
        return $"{how}{defer}。{next}{warning}";
    }

    /// <summary>
    /// 「试一试」：按<b>当前那一档</b>原样演一次。没有这条出口，用户只能等满间隔才知道自己配的到底是什么
    /// 效果——而"等 15 分钟验证一个开关"等于没给验证路径。演的内容不动节拍（见 <c>EyeRestService.Preview</c>）。
    /// </summary>
    [RelayCommand]
    private void PreviewEyeRest()
    {
        var did = StarMark.UI.Services.EyeRestService.Preview();
        EyeRestPreviewStatus = did switch
        {
            // 盖上幕布时把幕布上那句退出说明同样说一遍：这一行与屏幕上那块幕布不许对不上（强制档可点不开，
            // 让人以为"再点一下就行"，正是这次改判要治的那种措辞）。
            true when StarMark.UI.Services.EyeRestService.IsResting
                => "已演一次：暗幕已经盖上。" + StarMark.Core.Health.EyeRestPolicy.NoticeHint(CurrentEyeRestNotice),
            true => "已演一次：右下角那张提示卡真的贴上了屏幕（贴不上时才会退到主窗提示条）。",
            false when StarMark.UI.Services.EyeRestService.IsResting
                => "幕布还盖着屏，等这一轮结束再按（能不能提前点掉按上面那一档的说法）。",
            false => "没演成：护眼开关没打开时不建节拍表，也就没有可演的东西（先开启本卡片顶部的开关）。",
        };
    }

    // ===== RSS 订阅（批次 RB）=====

    /// <summary>
    /// RSS 总开关。翻位即持久化并<b>立刻</b>控制导航栏「RSS」项——"关掉之后项还在"就是没做到位，
    /// 也不需要重启（与 <see cref="OnTrendingEnabledChanged"/> 同一口径）。
    /// </summary>
    [ObservableProperty] private bool _rssEnabled;

    [ObservableProperty] private string _rssStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制副作用（否则每次进设置页都会重设一次导航可见性）。</summary>
    private bool _suppressRssApply;

    partial void OnRssEnabledChanged(bool value)
    {
        if (_suppressRssApply) return;
        _settings.SaveRssEnabled(value);
        App.MainWindow?.ApplyRssNavVisibility(value);
        RssStatus = value
            ? "已开启：导航栏现在有「RSS」这一栏，每个订阅源是它自己的一个文件夹。抓取只在你按「刷新」时发生。"
            : "已关闭：导航栏的「RSS」项已移除，不再发起任何抓取。已收藏进库的条目不受影响（它们已经是普通书签了）。";
    }

    // ===== GitHub 热榜（批次 KG）=====

    /// <summary>
    /// 热榜总开关（<b>默认关</b>）。翻位即持久化并<b>立刻</b>控制导航栏「热榜」项的可见性——
    /// 用户裁决"关着就不显示"，那么"关掉了项还在"就是没做到位；也不需要重启。
    /// </summary>
    [ObservableProperty] private bool _trendingEnabled;

    /// <summary>是否在「今日速览」显示热榜块（开启热榜时弹窗问过，这里随时可改）。</summary>
    [ObservableProperty] private bool _trendingGlanceEnabled;

    [ObservableProperty] private string _trendingStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制副作用（否则每次进设置页都重设可见性、甚至弹一次询问框）。</summary>
    private bool _suppressTrendingApply;

    // ────────── 屏幕画布（批次 WD-5：总开关 + 键位只读一览）──────────

    /// <summary>
    /// 画布总开关（<b>默认开</b>）。关掉之后三件事同时发生：托盘里那一项整条消失、画布内那批快捷键
    /// 不再注册（不替一个关掉的功能继续占着系统的 Ctrl+Alt+字母）、再按键位只会听见一句原因。
    /// <para>
    /// 正在画的时候关掉会<b>立刻收掉那块玻璃</b>——"我已经关了，屏幕上还压着一层吃鼠标的东西"是这条链
    /// 最坏的收尾（与护眼 Stop 立刻收幕同一口径）。
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _canvasEnabled = true;

    /// <summary>「截图带画布」（默认开＝与这条设置出现之前的行为一致：笔迹会进截图）。</summary>
    [ObservableProperty] private bool _canvasInScreenshots = true;

    /// <summary>
    /// 光标那块圆的半径（DIP）。<b>一个数管两块圆</b>：幕布开着时它是那块亮区，关着时它是光标光晕（批次 S4-⑥ 的合并结论）。
    /// <para>改完<b>立刻生效、不用点保存也不用重启</b>：画布每帧现读这份档（与总开关同一口径），
    /// 多一道"保存"按钮就是发起人算作缺陷的那种额外步骤。</para>
    /// </summary>
    [ObservableProperty] private double _cursorCircleRadiusDip = CursorCircle.DefaultRadiusDip;

    /// <summary>
    /// 滑杆右侧的读数。单位写死成 <b>DIP</b>：这块圆按屏的缩放换算成像素，150% 屏上那个数是它的 1.5 倍，
    /// 写成"像素"会让人以为两根滑杆在同一条刻度上（批次 WR 那条"单位要进界面"的口径）。
    /// </summary>
    public string CursorCircleRadiusText => $"{(int)CursorCircle.ClampRadiusDip(CursorCircleRadiusDip)} DIP";

    /// <summary>开关当前含义的一句话（看得见"关掉会发生什么"，不用猜）。</summary>
    [ObservableProperty] private string _canvasStatus = string.Empty;

    /// <summary>
    /// 十条画布动作与它们<b>当前真实绑定</b>的键位，一行一条——生成，不写死。
    /// <para>一行是<b>两个字段</b>（名字＋键位）而不是一句拼好的字符串：拼串渲染出来是十个左对齐段落，
    /// 键位起点跟着名字长度跑、右侧一大片没人占（批次 RO 点名的"文字全部挤在一起，而右侧却有很大空间"）。</para>
    /// </summary>
    [ObservableProperty] private ObservableCollection<StarMark.UI.Views.CanvasHotkeyRow> _canvasHotkeyRows = new();

    private bool _suppressCanvasApply;

    partial void OnCanvasEnabledChanged(bool value)
    {
        if (_suppressCanvasApply) return;
        _settings.SaveCanvasEnabled(value);
        if (!value) StarMark.UI.Services.CanvasService.Stop();
        // 注册表当场跟着改：不重启、也不要用户再去点一次「保存快捷键」（多余的步骤算缺陷）
        App.MainWindow?.ApplyTraySettings();
        CanvasStatus = BuildCanvasStatus();
    }

    /// <summary>
    /// 截图带不带画布。<b>只管"抓哪一帧时玻璃上不上屏"，不当擦笔迹的橡皮擦</b>：
    /// 关掉之后画布照旧显示、笔迹照旧留着，只是别人截走的图里没有它。
    /// </summary>
    partial void OnCanvasInScreenshotsChanged(bool value)
    {
        if (_suppressCanvasApply) return;
        _settings.SaveCanvasInScreenshots(value);
    }

    /// <summary>
    /// 半径一改就落盘（<b>读数刷新不受回灌闸门影响</b>：进设置页时回灌的那一版也要显示对，
    /// 只是那一次不该顺手写一遍档）。画布侧每帧现读，所以这里不做任何"通知画布"的动作——
    /// 通知迟早漏一处，而漏掉那一处的症状是"滑杆动了、屏幕上的圆没动"。
    /// </summary>
    partial void OnCursorCircleRadiusDipChanged(double value)
    {
        OnPropertyChanged(nameof(CursorCircleRadiusText));
        if (_suppressCanvasApply) return;
        _settings.SaveCursorCircleRadiusDip(value);
    }
}
