#nullable enable
using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using StarMark.Core.Health;
using StarMark.UI.Helpers;
using Windows.Graphics;

namespace StarMark.UI.Views;

/// <summary>
/// 休息时盖在一块屏上的暗幕。一类一屏（多屏时每屏一张，坐标走物理像素，
/// 与截图遮罩同一套理由：<c>AppWindow</c> 那套按 DIP 算，混合 DPI 时每屏都会算偏）。
/// <para>
/// 这扇窗<b>不接键盘、也不留住焦点</b>：焦点留在原处才不会把用户正在填的表单/编辑器弄丢
/// （否则那几十秒的打字落进一扇空窗，倒数完还得重新点一遍）。不接键盘同时也是"强制休息不可跳过"的
/// 真正凭据——按键只投递给有焦点的窗，而它从来没有焦点，所以 Esc 与 Alt+F4 都到不了这里。
/// </para>
/// <para>
/// 鼠标则<b>按档位决定接不接</b>（批次 RS：暗幕与"能不能提前退出"从此是两件事）：可跳档点一下就由服务收幕，
/// 强制档一条都不挂，只有倒数走完或服务被关掉这两条路。
/// </para>
/// </summary>
public sealed partial class EyeRestOverlayWindow : Window
{
    /// <summary>用户点了一下幕布（只有可跳档会发；强制档不挂任何输入，所以永远不会发）。服务据此收幕。</summary>
    public event Action? SkipRequested;

    /// <param name="monitor">本窗负责的那块屏（设备名 + 物理矩形 + 本屏缩放）。</param>
    /// <param name="notice">这一档提醒形式。<b>传的是档位而不是"可不可以在这里退出"那个布尔</b>：
    /// 布尔的方向只有被调方知道，接线处写反编译得过、测试也看不出来，真机上却是"强制档一点就开／暗幕档点不开"
    /// （记忆 ⑥ 那条同一族）。这一档怎么退出、幕布上说什么，都由本窗现问 <see cref="EyeRestPolicy"/>。</param>
    public EyeRestOverlayWindow((string Device, RectInt32 Bounds, double Scale) monitor, EyeRestNotice notice)
    {
        InitializeComponent();
        WindowInterop.RemoveDefaultWindowFrame(this);

        // 与组件窗同一条被真机验证过的点亮配方：先用框架的 Show 让 XAML 岛把内容 realize 出来
        // （只 SetWindowPos 有整岛没渲染、留一块"看不见却挡住鼠标"的顶层窗的风险），
        // 再补一发原生 <c>SW_SHOWNOACTIVATE</c> 确保真正点亮。
        //
        // 但"点亮"这一步本身会把前台抢过来——幕布不接键盘，那几十秒用户的打字会落进一扇空窗里。
        // 所以先记下原来的前台窗，出现之后立刻把焦点还回去（我们能还，是因为前台此刻在自己手里，
        // <c>SetForegroundWindow</c> 只在这个前提下有效）。幕布不需要焦点：它只负责盖住屏幕。
        var previous = WindowInterop.GetForegroundWindow();
        var hwnd = WindowInterop.GetHwnd(this);
        AppWindow.Show();

        var b = monitor.Bounds;
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
        if (previous != IntPtr.Zero && previous != hwnd) WindowInterop.SetForegroundWindow(previous);

        // 挂在最后一句：上面那发 Show 自己会激活一次，先挂上就等于幕布出现的那一刻把自己收了。
        if (EyeRestPolicy.IsSkippable(notice)) EnableSkipByClicking();
    }

    /// <summary>
    /// 可跳档的鼠标出口：点一下即交回服务收幕。<b>两路一起挂</b>——第一次点击可能被系统当作
    /// "激活这扇窗"而吞掉输入（只发 <see cref="Window.Activated"/>，不发点击），只挂一路的症状就是"点了没反应，得点第二下"。
    /// 点击不依赖焦点，所以这条出口不受"幕布不抢焦点"的影响。
    /// </summary>
    private void EnableSkipByClicking()
    {
        Root.PointerPressed += (_, _) => SkipRequested?.Invoke();
        // 只认"被点了一下而激活"：点亮那一步自己带来的激活是 CodeActivated，认了它就会出现"幕布刚盖上就自己收了"。
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.PointerActivated) SkipRequested?.Invoke();
        };
    }

    /// <summary>
    /// 为什么提醒（"连续工作约 45 分钟"）＋ 怎么退出。退出那一句现问 <see cref="EyeRestPolicy.NoticeHint"/>：
    /// 幕布上不自己写"能不能提前结束"，免得设置页写着"点一下即可"、幕布上却写着别的（记忆 ⑧）。
    /// </summary>
    public void SetMessage(int intervalMinutes, EyeRestNotice notice)
        => Hint.Text = $"你已经连续工作约 {intervalMinutes.ToString(CultureInfo.InvariantCulture)} 分钟。"
            + "把视线挪到几米外的地方，让睫状肌松一松\n" + EyeRestPolicy.NoticeHint(notice);

    /// <summary>更新剩余秒数。</summary>
    public void SetSecondsLeft(int seconds) => Countdown.Text = seconds.ToString(CultureInfo.InvariantCulture);

    /// <summary>整窗不透明度（1＝全暗，0＝完全透明）。淡出用现成的 <see cref="WindowInterop.SetWindowOpacity"/>。</summary>
    public void ApplyOpacity(double opacity) => WindowInterop.SetWindowOpacity(this, opacity);

    public void CloseOverlay() => Close();
}
