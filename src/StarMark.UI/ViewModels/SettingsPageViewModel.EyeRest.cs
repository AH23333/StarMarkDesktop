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
using StarMark.Abstractions.I18n;
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

    [ObservableProperty] private LocMessage? _eyeRestStatus;

    /// <summary>「试一试」的结果回报（属性名含 Status＝不进自动保存，见 <c>IsDisplayOnlyProperty</c>）。</summary>
    [ObservableProperty] private LocMessage? _eyeRestPreviewStatus;

    /// <summary>
    /// 间隔那几档（批次 WI-37-B 起是<b>数据项</b>，不再是 Core 拼好的中文标签）。
    /// <para>为什么必须换成 <see cref="LocOption"/>：闭着的那格显示的是选中那一刻从 <c>Content</c> 抄走的快照，
    /// 换语言时 <c>LocText</c> 只重贴"还活在可视树里、自己带着键"的元素（坑表 #272／#275）；
    /// 而 <c>Value</c> 交的是<b>分钟数本身</b>——⛔ 拿显示文字当身份，换语言后选中的还是同一项，读出来的却是另一串字。</para>
    /// </summary>
    public IReadOnlyList<LocOption> EyeRestIntervalOptions { get; } =
        StarMark.Core.Health.EyeRestPolicy.IntervalOptions
            .Select((minutes, index) => new LocOption(StarMark.Core.Health.EyeRestPolicy.IntervalKeys[index], string.Empty, minutes))
            .ToList();

    /// <summary>提醒形式那三档，顺序＝<c>EyeRestPolicy.NoticeOptions</c>（也就是设置档里那颗序号）。</summary>
    public IReadOnlyList<LocOption> EyeRestNoticeOptions { get; } =
        StarMark.Core.Health.EyeRestPolicy.NoticeKeys
            .Select((key, index) => new LocOption(key, string.Empty, StarMark.Core.Health.EyeRestPolicy.NoticeAt(index)))
            .ToList();

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
        EyeRestPreviewStatus = null;
    }

    /// <summary>
    /// 状态行说三件事：怎么提醒、全屏时让不让路、下一次大约几点。
    /// "开着但表没挂上"必须自己承认——用户没法用眼睛验证一张定时器在不在跑。
    /// <para><b>交回的是「键＋读数」，不是拼好的句子</b>（批次 WI-37-B，WI-2k 那颗门）：VM 拼串的那一刻
    /// 语言就被定死了，换语言没人回去重算，屏幕上会留下半中半英；而这一行是设置页最显眼的那一句。</para>
    /// <para>那一档怎么说、间隔是多少，都取 <c>EyeRestPolicy</c> 交回的<b>没渲染的</b> <see cref="LocMessage"/>：
    /// 与下拉里看到的是同一颗键（⛔ 状态行自己攒第二份"这一档会发生什么"＝漏掉"点一下可提前结束"那一族），
    /// 而嵌套那句在贴上去这一刻才按当前语言取词（坑表 #328 同族）。</para>
    /// </summary>
    private LocMessage? BuildEyeRestStatus()
    {
        if (!EyeRestEnabled) return LocMessage.Of("health.status.off");

        var next = StarMark.UI.Services.EyeRestService.IsRunning
                   && StarMark.UI.Services.EyeRestService.NextDueAt is { } due
            ? LocMessage.Of("health.status.nextDue", DateTimeText.Clock(due))
            : null;
        var warning = StarMark.UI.Services.EyeRestService.IsRunning
            ? null
            : LocMessage.Of("health.status.notRunning");
        return LocMessage.Of("health.status.on",
            StarMark.Core.Health.EyeRestPolicy.IntervalMessage(EyeRestIntervalMinutes),
            StarMark.Core.Health.EyeRestPolicy.NoticeMessage(CurrentEyeRestNotice),
            LocMessage.Of(EyeRestDeferOnFullscreen ? "health.status.deferOn" : "health.status.deferOff"),
            next, warning);
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
            // 让人以为"再点一下就行"，正是这次改判要治的那种措辞）。实参交<b>没渲染的</b>那颗，
            // 幕布上与本行才真是同一句字（⛔ 在这里拼两串中文）。
            true when StarMark.UI.Services.EyeRestService.IsResting
                => LocMessage.Of("health.preview.curtain",
                    StarMark.Core.Health.EyeRestPolicy.NoticeHintMessage(CurrentEyeRestNotice)),
            true => LocMessage.Of("health.preview.card"),
            false when StarMark.UI.Services.EyeRestService.IsResting
                => LocMessage.Of("health.preview.busy"),
            false => LocMessage.Of("health.preview.off"),
        };
    }

    // ===== RSS 订阅（批次 RB）=====

    /// <summary>
    /// RSS 总开关。翻位即持久化并<b>立刻</b>控制导航栏「RSS」项——"关掉之后项还在"就是没做到位，
    /// 也不需要重启（与 <see cref="OnTrendingEnabledChanged"/> 同一口径）。
    /// </summary>
    [ObservableProperty] private bool _rssEnabled;

    [ObservableProperty] private LocMessage? _rssStatus;

    /// <summary>LoadFromStore 回灌初值期间抑制副作用（否则每次进设置页都会重设一次导航可见性）。</summary>
    private bool _suppressRssApply;

    partial void OnRssEnabledChanged(bool value)
    {
        if (_suppressRssApply) return;
        _settings.SaveRssEnabled(value);
        App.MainWindow?.ApplyRssNavVisibility(value);
        RssStatus = value
            ? LocMessage.Of("settings.extend.rssOnStatus")
            : LocMessage.Of("settings.extend.rssOffStatus");
    }

    // ===== GitHub 热榜（批次 KG）=====

    /// <summary>
    /// 热榜总开关（<b>默认关</b>）。翻位即持久化并<b>立刻</b>控制导航栏「热榜」项的可见性——
    /// 用户裁决"关着就不显示"，那么"关掉了项还在"就是没做到位；也不需要重启。
    /// </summary>
    [ObservableProperty] private bool _trendingEnabled;

    /// <summary>是否在「今日速览」显示热榜块（开启热榜时弹窗问过，这里随时可改）。</summary>
    [ObservableProperty] private bool _trendingGlanceEnabled;

    [ObservableProperty] private LocMessage? _trendingStatus;

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
    [ObservableProperty] private LocMessage? _canvasStatus;

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
