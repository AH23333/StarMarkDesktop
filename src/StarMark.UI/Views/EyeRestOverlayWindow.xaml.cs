#nullable enable
using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using StarMark.UI.Helpers;
using Windows.Graphics;

namespace StarMark.UI.Views;

/// <summary>
/// 强制休息时盖在一块屏上的暗幕。一类一屏（多屏时每屏一张，坐标走物理像素，
/// 与截图遮罩同一套理由：<c>AppWindow</c> 那套按 DIP 算，混合 DPI 时每屏都会算偏）。
/// <para>
/// 这扇窗<b>不注册按键、不注册鼠标，也不留住焦点</b>：焦点留在原处才不会把用户正在填的表单/编辑器
/// 弄丢（否则那 20 秒的打字落进一扇空窗，倒数完还得重新点一遍）；不注册按键是规格点名的
/// "Esc 不跳过，防形同虚设"。
/// 收起来只有两条路——20 秒走完由服务淡出关闭，或用户直接关掉护眼开关（服务会立刻收幕）。
/// </para>
/// </summary>
public sealed partial class EyeRestOverlayWindow : Window
{
    /// <param name="monitor">本窗负责的那块屏（设备名 + 物理矩形 + 本屏缩放）。</param>
    public EyeRestOverlayWindow((string Device, RectInt32 Bounds, double Scale) monitor)
    {
        InitializeComponent();
        WindowInterop.RemoveDefaultWindowFrame(this);

        // 与组件窗同一条被真机验证过的点亮配方：先用框架的 Show 让 XAML 岛把内容 realize 出来
        // （只 SetWindowPos 有整岛没渲染、留一块"看不见却挡住鼠标"的顶层窗的风险），
        // 再补一发原生 <c>SW_SHOWNOACTIVATE</c> 确保真正点亮。
        //
        // 但"点亮"这一步本身会把前台抢过来——幕布不接键盘，那 20 秒用户的打字会落进一扇空窗里。
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
    }

    /// <summary>为什么提醒（"连续工作约 45 分钟"）＋ 该做什么。摆在倒数下面，一次写好就不再动。</summary>
    public void SetMessage(int intervalMinutes)
        => Hint.Text = $"你已经连续工作约 {intervalMinutes.ToString(CultureInfo.InvariantCulture)} 分钟。"
            + "把视线挪到几米外的地方，让睫状肌松一松——时间到了会自动恢复。";

    /// <summary>更新剩余秒数。</summary>
    public void SetSecondsLeft(int seconds) => Countdown.Text = seconds.ToString(CultureInfo.InvariantCulture);

    /// <summary>整窗不透明度（1＝全暗，0＝完全透明）。淡出用现成的 <see cref="WindowInterop.SetWindowOpacity"/>。</summary>
    public void ApplyOpacity(double opacity) => WindowInterop.SetWindowOpacity(this, opacity);

    public void CloseOverlay() => Close();
}
