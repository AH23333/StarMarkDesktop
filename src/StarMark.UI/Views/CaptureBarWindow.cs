#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.UI.Helpers;
using Windows.Foundation;
using Windows.UI;

namespace StarMark.UI.Views;

/// <summary>
/// 贴图的那一条工具条：<b>独立的一扇置顶小窗</b>（批次 WI）。
/// <para>
/// 为什么必须是另一扇窗：批次 WD-6 把条子放在"贴图窗口内、画面下方外侧那一条"，那是按构造不成立的方案——
/// WinUI 3 的窗口客户区不能整块透明，画面只填满"图那一段"，为条子加高的那一条没有像素可画，
/// 渲染出来就是一块黑（真机反馈："菜单栏还是和贴图同框，会造成局部黑块"）。
/// 唯一正确的落点就是让条子自己住一扇窗：贴图窗的尺寸＝图的尺寸，一格不多。
/// </para>
/// <para>
/// 四条硬约束（都是这条链上踩过的坑）：① <b>不抢前台</b>——点亮那一刻记下前台是谁、亮完还回去，
/// 并用 <c>SW_SHOWNOACTIVATE</c>；Enter 复制／Ctrl+Z 撤销／Esc 关闭这些快捷键归贴图窗，
/// 点一条按钮就把贴图键盘吃掉的话，"点了按钮之后按什么都没反应"；
/// ② 但<b>绝不能加 <c>WS_EX_NOACTIVATE</c></b>：真机上那样整扇窗收不到 XAML 的按钮点击
/// （条子看得见、颗颗点不动）。不抢前台靠的是"亮完把前台还回去"，不是靠把窗设成不可激活；
/// ③ <b>topmost 两步都要</b>——先 <c>HWND_TOPMOST</c> 进带、再 <c>HWND_TOP</c> 带内重排，
/// 只传 TOP 的窗永远留在普通层，任何应用一激活就把条子盖住；
/// ④ 条子收起（<c>Visibility=Collapsed</c>）时量出来是 0，<b>那扇窗必须跟着藏起来</b>，
/// 否则留下一块透明的空窗吃鼠标；⑤ <b>这扇窗里放不下任何弹出层</b>——真机验证：WinUI 3 把
/// ToolTip 与 Flyout 钉在宿主窗边界内，33 像素高的条子窗里"图形选择栏"只剩半截，
/// 所以选择栏由条子自己排版、自己长高（<see cref="Place"/> 那条链，改内容时由 <c>ReflowBar</c> 叫上这里）。
/// <para><b>代价是贴图态悬停看不到 ToolTip</b>。曾经为此在条子里加过一行"悬停说明"，
/// 但那行字几百像素宽、而这扇窗的宽度＝内容实测宽度，悬停哪颗整条就变宽、左边缘跟着往左跑
/// （居中于画面的条子来回跳）——用户裁决<b>删掉说明行</b>：宽度稳定比看得到解释重要。</para>
/// </para>
/// </summary>
public sealed class CaptureBarWindow : Window
{
    /// <summary>条子与画面之间的缝隙（物理像素）。</summary>
    private const int Gap = 3;

    /// <summary>窗口比条子本体多出的那一圈（防 DPI 取整把边框裁掉一半）。</summary>
    private const int Slack = 2;

    private readonly FrameworkElement _content;
    private readonly double _scale;

    private bool _shown;
    private bool _destroyed;
    private int _baseHeight;                // 只有按钮那一行时的窗高（物理像素），见 Place 里的用法
    private IntRect _image;
    private IntRect _work;

    public CaptureBarWindow(FrameworkElement content, double scale)
    {
        _content = content;
        _scale = scale <= 0 ? 1 : scale;
        Title = "StarMark 贴图工具条";
        // 底色与条子本体同色（#EE202020）：WinUI 3 的 Window 没有 Background 可设，客户区就是这层 Grid；
        // 不设就会露出框架默认色（浅色系统主题下是一片白），围着暗色条子出现一圈白边——正是要消掉的观感。
        // 主题也钉成暗色：条上图标按白色画的，跟着系统主题走会在浅色模式下糊成一片。
        Content = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x20, 0x20)),
            RequestedTheme = ElementTheme.Dark,
            Children = { content },
        };
        WindowInterop.RemoveDefaultWindowFrame(this);       // 无框 + 不进任务栏/Alt+Tab（贴图链同款）
        Closed += (_, _) => _destroyed = true;
    }

    /// <summary>
    /// 按"画面现在在哪"摆放这一条：<b>优先画面下方，放不下就上方</b>（与选区阶段同一判据），
    /// 水平居中于画面并左右夹进工作区——贴图常常比一条工具条窄，夹不住就会"只看得见半条"。
    /// </summary>
    public void Place(IntRect image, IntRect work)
    {
        if (_destroyed) return;
        _image = image;
        _work = work;

        var hwnd = WindowInterop.GetHwnd(this);
        if (!_shown)
        {
            // 先亮出来再量：窗没亮过时控件模板尚未应用，那一次 Measure 量到的是一排空按钮的 MinWidth
            // 点亮这一下要先把前台记下来再还回去：贴图窗的快捷键（Enter 复制 / Ctrl+Z / Esc 关闭）
            // 归它，条子一出现就把前台抢走的话，"点完按钮之后按什么都没反应"（WA 那批同一条坑）。
            var previous = WindowInterop.GetForegroundWindow();
            AppWindow.Show();
            _shown = true;
            if (previous != IntPtr.Zero && previous != hwnd) WindowInterop.SetForegroundWindow(previous);
        }
        if (_content.Visibility != Visibility.Visible)
        {
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_HIDE);      // 收起的那一条不能留一扇吃鼠标的空窗
            return;
        }

        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wanted = _content.DesiredSize;
        if (wanted.Width < 8 || wanted.Height < 8)
        {
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_HIDE);
            return;
        }
        var width = (int)Math.Round(wanted.Width * _scale) + Slack * 2;
        var height = (int)Math.Round(wanted.Height * _scale) + Slack * 2;
        // "摆上面还是下面"只看<b>按钮那一行</b>的高度：选择栏是条子自己长出来的第二行，
        // 按整条的高度判会在用户刚点开选择栏时把整条翻到画面另一侧（手一伸过去它跑了）。
        if (_baseHeight == 0 || height < _baseHeight) _baseHeight = height;
        if (work.Width > 0) width = Math.Min(width, Math.Max(1, work.Width));

        var x = image.X + (image.Width - width) / 2;
        var below = image.Bottom + Gap + _baseHeight <= work.Bottom;
        var y = below ? image.Bottom + Gap : image.Y - height - Gap;
        if (work.Width > 0)
        {
            x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width));
            // 上下都塞不下（贴图贴着屏边且很高）：贴着画面下沿放，至少条子整条可见可点
            y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height));
        }

        // 两步提层：①进 topmost 带（新窗生下来不在带里）②带内重排到最上（对已在带里的窗再传 TOPMOST
        // 只换带不重排＝什么都没做），且必须带 NOACTIVATE/SWP_SHOWWINDOW 一并显形
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP, x, y, width, height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
    }

    /// <summary>画面被拖动／缩放／旋转之后重摆一次（沿用上一次的工作区，不必重新问系统）。</summary>
    public void Reposition()
    {
        if (!_destroyed && _image.Width > 0) Place(_image, _work);
    }

    /// <summary>随贴图一起收掉。<b>贴图窗关闭时必须叫上它</b>，否则留下一扇没有主人的条子窗。</summary>
    public void Shutdown()
    {
        if (_destroyed) return;
        try
        {
            // 先把条子从这扇窗的树里摘下来：窗一关，它承载的内容也就没了父元素，
            // 贴图窗若还活着（例如只是重开一次）不该留下一截"看不见也用不了"的控件树
            if (_content.Parent is Panel host) host.Children.Remove(_content);
            Close();
        }
        catch (Exception ex) { StarLog.Warn($"[贴图] 工具条窗没关干净：{ex.Message}"); }
        _destroyed = true;
    }
}
