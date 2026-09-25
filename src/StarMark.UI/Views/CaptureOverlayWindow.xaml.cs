#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

namespace StarMark.UI.Views;

/// <summary>
/// 截图遮罩窗：显示"按下热键那一刻"的整屏帧，让用户拖动框选，然后<b>就地标注</b>，
/// 最后复制或存图或贴图或识字。
/// <para>
/// 本类有两种态，共用<b>同一条标注链</b>（批次 PN 的用户口径）：
/// <b>截图态</b>铺满一整块屏、要框选；<b>贴图态</b>（<see cref="_pinned"/>）就是那张钉在桌面上的图——
/// 窗口尺寸＝画面尺寸、整块都是内容，没有压暗、没有"重新框一块"，未选笔时那一按是移动这张图，
/// 滚轮缩放、鼠标穿透与角标都搬到了这里（原来那份贴图右键菜单由这条工具条整体取代）。
/// 两态只在建窗与"这一按算不算改框"上分岔，八种笔、文字、撤销重做、合成、四个落点全是同一批代码。
/// </para>
/// <para>
/// 生命周期是<b>一次性</b>的：每次截图新建每屏一个窗、结束即关，不留常驻窗池（D3 裁决）。
/// 常驻隐藏窗池能省几十毫秒首帧，代价是永远有一批顶层窗口挂在每台显示器上。
/// 贴图态是唯一例外：它按名册（<see cref="PinManager"/>）常驻，直到用户关掉它或关掉全部。
/// </para>
/// <para>
/// <b>每条退出路径都必须把会话收干净</b>：漏一条就等于把用户摁在一层吃满屏幕的顶层窗里，
/// 只能去任务管理器杀进程。因此 Esc / 右键 / 点取消 / 窗口被外部关闭 都走同一个
/// <see cref="_finish"/> 回调（<see cref="Settle"/> 保证只回调一次），由服务统一关窗。
/// </para>
/// <para>
/// 标注这一层的设计要点：<b>预览显示的就是真的会被交出去的那份像素</b>。
/// 松手即由 <see cref="AnnotationPainter"/> 把标注合成进选区底图，再把合成结果摆回原位；
/// 只有"正在拖、还没松手"的那一条用简单图元近似显示。这样"预览看着对、存出来不对"这一整类缺陷
/// 结构上就不成立，而撤销/清空也只是拿底图重烤一次，不需要任何反向操作。
/// </para>
/// </summary>
public sealed partial class CaptureOverlayWindow : Window
{
    // 工具条兜底尺寸：真实尺寸由 UpdateLayout 之后量到的 ActualWidth/Height 决定，
    // 这两个数只在"还没量出来"的那一帧用来避免把条摆到屏外。
    private const double Bar_fallback_width = 460;
    private const double Bar_fallback_height = 34;

    private readonly ScreenFrame? _frame;       // 截图模式的整帧；就地编辑贴图时没有帧（底图由贴图交进来）
    private IntRect _monitor;                   // 本屏（截图态）／这张贴图（贴图态）在虚拟桌面里的物理矩形
    private double _scale;                  // 本屏 DPI 缩放（1.0 / 1.25 / 1.5 …）；贴图被拖到别的屏时要重量
    private readonly Action<CaptureOverlayWindow, IntRect?> _finish;
    private readonly CaptureMode _mode;         // 放开选区后做什么（F1 给条 / F3 贴 / 识字直接复制）

    /// <summary>
    /// <b>这扇窗本身就是那张钉在桌面上的贴图。</b>不铺全屏、不压暗、没有"重新框一块"，
    /// 因为整块画面都是内容；工具条、八种笔、文字、撤销重做与截图时<b>是同一条链、同一扇窗</b>，
    /// 只有"框选中的画面"与"已经钉住的画面"这一个状态差别（用户口径）。
    /// </summary>
    private readonly bool _pinned;

    /// <summary>
    /// 一个"底图像素"对应几个显示像素：截图恒为 1（选区就是屏幕上的那块），
    /// 贴图在 2.5× 时就是 2.5——不把这个除掉，放大后的贴图会"鼠标在字上、笔落在字外"。
    /// 贴图滚轮缩放改的就是它，所以可变。
    /// <para><b>初始值 1.0 是承重的</b>：截图那条链不经过贴图构造，没有这个初始值它就是 double 的默认 0，
    /// 而 <see cref="ToLocal"/> 拿它做除数、<see cref="SlopInSource"/> 与字号也按它换算 ⇒
    /// 坐标变成 Infinity/NaN 再截回整数，<b>截图态一条都画不上</b>（真机反馈"截图时无法编辑"的真因，
    /// PM/PN 两批都只盯着"贴图要不要除"，没问过"截图态它是几"）。</para>
    /// </summary>
    private double _sourceScale = 1.0;

    /// <summary>贴图态：倍率、拖动快照、穿透、角标。</summary>
    private double _zoom = 1.0;
    private bool _draggingPin;
    private bool _clickThrough;
    private WindowInterop.POINT _gestureStartCursor;
    private Windows.Graphics.RectInt32 _gestureStartRect;
    private int _lastAppliedX = int.MinValue;
    private int _lastAppliedY = int.MinValue;

    /// <summary>底图／最终图的尺寸（截图＝选区尺寸；编辑＝贴图的源尺寸，与显示尺寸无关）。</summary>
    private int _contentWidth, _contentHeight;

    private readonly AnnotationHistory _history = new();

    private PointInt32 _startPhysical;
    private IntRect? _selection;                // 虚拟桌面物理像素
    private bool _awaitingRelease;
    private bool _settled;

    // ── Snipaste 式选区辅助 ──
    private WriteableBitmap? _magnifierBitmap;
    private bool _autoDetect = true;                 // 窗口自动检测（按住 Ctrl 临时关闭）
    private IReadOnlyList<IntRect> _windowCandidates = Array.Empty<IntRect>();
    private IntRect? _detected;                      // 当前悬停命中的窗口

    // ── 橡皮擦 ──
    private bool _erasing;
    private readonly HashSet<Annotation> _eraseRemoved = new();

    // ── 贴图旋转/镜像/透明度 ──
    private double _opacity = 1d;

    private byte[]? _base;                      // 选区那块底图（物理像素，合成时的固定起点）
    private WriteableBitmap? _preview;
    private byte[]? _composed;                  // 底图 + 全部已提交标注（每次重烤后留下，给拖动做起点）
    private byte[]? _scratch;                   // 正在拖的这条打码的画布：_composed 的副本 + 已走过的段
    private PixelPoint _scratchTail;            // _scratch 已经画到哪个点（下一段从这里接）
    private List<PixelPoint>? _stroke;          // 正在拖、还没合成进去的那一条
    /// <summary>上一次把整块缓冲推给屏幕的时刻（毫秒）。见 <c>PaintPreview</c>。</summary>
    private long _lastMosaicPreview;
    // ── 选中与拖动（批次 RE-3：画完还能挪位置、改大小、转方向）──
    private int? _selected;                   // 选中的那一条（画着选择框与把手）
    private Annotation? _dragOriginal;        // 按下时的原样：拖动只改副本，松手才落进历史
    private PixelPoint _dragAnchor;           // 按下那一点（选区内物理像素）
    private PixelPoint _dragLast;             // 最近一次光标位置（松手按它落定，与预览同一套判据）
    private byte[]? _underDrag;               // 拖动期间的底：底图 + 除被拖那条之外的全部标注（一次算好）
    private byte[]? _dragCanvas;              // 每帧复用：_underDrag 的副本 + 预览那一条
    private Grab _grab;                        // Grab ＝ 别名的 AnnotationGrab（判定与取值都在模型一处）

    /// <summary>"点一下"要不要算选中脚下那条的容差（<b>屏幕像素</b>，进模型前按 <see cref="_sourceScale"/> 换算）。
    /// 比把手小：点是打在形状上，不是打在小方块上。
    /// <para>角点/把手的容差不在这里——那一条跟着形状尺寸收缩，住在模型里（<c>Annotation.HandleSlopFor</c>），
    /// 因为它能被断言。</para></summary>
    private const int SelectionSlop = 8;

    /// <summary>框内（＝移动）的容差（物理像素）：贴着形状边缘那几像素也算"点在它身上"，不然细线永远拖不动。</summary>
    private const int MoveSlop = 6;

    /// <summary>旋转把手离框顶多远（物理像素）。</summary>
    private const int RotateHandleLift = 26;
    private bool _editingText;
    private int? _editingIndex;             // 非空＝正在改历史里那一条（落笔时替换它，不再新增一条）
    private int _editorColourBgra;          // 那一条自己的颜色：改旧字时输入框不许改用现在的调色板（否则红白两层）
    private PixelPoint _textAnchor;
    /// <summary>
    /// 当前armed的绘制工具；<b>null＝一支都没选</b>。
    /// <para>框选完不再默认拿着一支矩形笔（真机反馈：那等于"自动进入编辑模式"）——
    /// 未选工具时这一按是用来改框的（十字箭头，拖动/改大小在批次 PK-2 接上），
    /// 每支工具都是"点一次选中、再点一次取消"，所以随时能退出编辑回到改框那一态。</para>
    /// </summary>
    private AnnotationTool? _tool;

    /// <summary>是否已经选了一支工具（armed）。未 armed ⇒ 这一按不改画面，只预备改框。</summary>
    private bool Armed => _tool is not null;

    /// <summary>正在进行的那一笔用的是哪支笔。<b>拖动途中换工具不许改正在画的这一笔</b>：
    /// 起点是按下那一刻定的，中途换成别的笔等于松手时按新笔重解释一遍（RH-3 那类错的同族）。</summary>
    private AnnotationTool _strokeTool = AnnotationTool.Rectangle;

    /// <summary>改框那一路：正在改哪一条边（None＝没在改）。按下时的选区与按下点都是快照，
    /// 之后每一帧从这两份快照<b>重算</b>绝对矩形，不累加增量（贴图那条抖动教训同一口径）。</summary>
    private CaptureGeometry.SelectionEdge _adjust = CaptureGeometry.SelectionEdge.None;
    private IntRect _adjustStart;
    private IntRect _adjustPending;
    private PointInt32 _adjustPress;

    private string WeightToolName => _tool is { } t ? Annotation.ToolName(t) : "未选工具";
    private int _colourIndex;
    private int _weightIndex = 1;

    /// <param name="monitorDevice">本窗负责的显示器设备名（失活时只认自己这屏的失活）。</param>
    /// <param name="mode">见 <see cref="CaptureMode"/>：<see cref="CaptureMode.Toolbar"/> 之外都放开即执行。</param>
    public CaptureOverlayWindow(
        ScreenFrame frame,
        (string Device, RectInt32 Bounds, double Scale) monitor,
        Action<CaptureOverlayWindow, IntRect?> finish,
        CaptureMode mode)
    {
        _frame = frame;
        _monitor = new IntRect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height);
        _scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;
        _finish = finish;
        _mode = mode;
        DeviceName = monitor.Device;

        InitWindow();
        HintText.Text = mode switch
        {
            CaptureMode.Pin => "按住拖动框选区域 · 放开即钉到桌面 · Enter 立即贴当前选区 · Esc 取消 · 右键不截",
            CaptureMode.Ocr => "按住拖动框选要认字的区域 · 放开即识别并把文字复制走 · Esc 取消 · 右键不截",
            _ => HintText.Text,
        };

        // 自动检测的候选窗口只枚举一次（画面已冻结，拓扑不会变）。<b>必须发生在窗口上屏之前</b>：
        // 这一步若抛异常，后面 SetWindowPos 已经把窗口摆上屏、名册里却没有它——
        // 留下一块看得见、键盘焦点也没挂上的僵尸遮罩，用户只能看着它卡死整个屏幕。
        _windowCandidates = CollectWindowCandidates();

        // 位置与尺寸走 Win32 物理像素：AppWindow 那套按 DIP 算，多屏混合 DPI 时每屏都会算偏。
        // SWP_NOACTIVATE：先就位再 Activate，避免用户看到窗口从别处滑过来。
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOPMOST,
            _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);

        if (BuildMonitorBitmap() is not { } bitmap)
        {
            ShowError("这一帧没能铺到屏幕上（内存不足或像素缓冲尺寸不符）——按 Esc 结束");
            return;
        }
        Shot.Source = bitmap;

        // 键盘（方向键改框 / Enter 复制 / Esc 取消）需要 Root 持有焦点
        Root.Loaded += (_, _) => Root.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 钉一张图到桌面上：<b>这扇窗就是那张图，同时也是它的编辑器</b>。
    /// 位置与尺寸走 Win32 物理像素（1× 时与当初屏幕那块区域等大，不该再乘一次 DPI），
    /// 倍率由滚轮改、左上角钉住并收边（PJ 口径）；没选笔时按下拖动＝移动这张图。
    /// </summary>
    /// <param name="placement">贴在虚拟桌面里的物理矩形（就是刚框选的那块区域）。</param>
    public CaptureOverlayWindow(byte[] pixels, int width, int height, IntRect placement, double zoom)
    {
        _frame = null;
        _scale = 1.0;                       // 真实缩放要等窗口就位、知道自己在哪块屏之后才量得到
        _finish = (_, _) => { };            // 贴图态没有"交回选区"这回事
        _mode = CaptureMode.Toolbar;        // 要的就是那条完整工具条
        _pinned = true;
        _zoom = CaptureGeometry.ClampZoom(zoom);
        _sourceScale = _zoom;
        DeviceName = "贴图";
        var (pw, ph) = CaptureGeometry.PinPixelSize(Math.Max(1, width), Math.Max(1, height), _zoom);
        _monitor = new IntRect(placement.X, placement.Y, pw, ph);

        InitWindow();
        // 贴图不进任务栏、不进 Alt+Tab：一屏贴十几张时那两处会被占满，而它是一次性的工具窗，
        // 用户找回它靠的是"看得见的那张图"本身。
        var hwnd = WindowInterop.GetHwnd(this);
        WindowInterop.SetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE,
            new IntPtr(WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64()
                | WindowInterop.WS_EX_TOOLWINDOW));
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST,
            _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);

        // 缩放比要按"落在哪块屏"来量：多屏混合 DPI 下拿主屏的数会把选区整体算偏。
        _scale = WindowInterop.GetScale(this);
        // 编辑态没有"选区"这件事：压暗、蓝框、尺寸提示与那条框选说明全部收起，
        // 否则用户会看到一圈根本不存在的边界，接着去拖它。
        DimLayer.Visibility = Visibility.Collapsed;
        HintChip.Visibility = Visibility.Collapsed;
        SelectionTip.IsHitTestVisible = false;

        BeginEditingExisting(pixels, width, height);
        SyncBadge();
        // 抢一次焦点：贴图要能直接按 Esc 关掉（Snipaste 同做法）。不激活的话 Esc 永远收不到，
        // 而"只能去托盘关掉全部"在贴图铺满屏幕时是最难受的那种死法。
        Activate();
        Closed += (_, _) => PinManager.Unregister(this);
    }

    /// <summary>两种模式共用的建窗步骤（XAML、工具条、事件钩子、无边框、关闭兜底）。</summary>
    private void InitWindow()
    {
        InitializeComponent();
        BuildToolBar();
        // 点进输入框（放光标、选中一段）不算"在选区里起一笔"：不拦下这一冒泡，第二次点击进来
        // 就会走 BeginStroke → BeginTextEdit → 把刚打的一行清空（真机反馈"文字编辑无效"的路径之一）。
        // 光标定位由 TextBox 自己的处理负责，我们只在它之后把事件吃掉。
        TextEditor.PointerPressed += (_, e) => e.Handled = true;
        // 条子自己的宽度要等第一次布局才有真值，而贴图态"缩多少、靠哪儿摆"全按它算：
        // 量到真值就重摆一次，否则用户得滚一下轮才把整条菜单"叫出来"。
        ActionBar.SizeChanged += (_, _) =>
        {
            if (_pinned && _selection is { } s) PositionBar(s);
        };
        Root.DoubleTapped += (_, e) => OnDoubleTapped(e);

        // 遮罩不需要主题：画面是抓来的桌面，文字全画在暗底上并用硬编码白色 —— 这里刻意不调
        // ThemeManager。套主题反而会把窗口的 ActualTheme 拉去影响按钮默认前景，出现"暗底灰字"。
        WindowInterop.RemoveDefaultWindowFrame(this);

        // 被外部关掉（Alt+F4、任务管理器"切到"、系统注销）也要按取消回报，否则会话永远挂着。
        Closed += (_, _) => Settle(null);
    }

    public string DeviceName { get; }

    /// <summary>当前选区（虚拟桌面物理像素）；还没框选时为 null。</summary>
    public IntRect? Selection => _selection;

    /// <summary>本屏的矩形（虚拟桌面物理像素）。</summary>
    public IntRect MonitorBounds => _monitor;

    // ────────── 会话收尾 ──────────

    /// <summary>把结果（null＝取消）交给服务；一个会话只交一次。</summary>
    private void Settle(IntRect? selection)
    {
        if (_settled) return;
        _settled = true;
        // 贴图态没有"交回"这回事：这扇窗显示的就是那张图，关掉即从名册里摘掉（见 Closed 钩子）。
        if (!_pinned) _finish(this, selection);
    }

    /// <summary>服务在统一收尾时调用：本窗已把结果交出去了，直接关。</summary>
    public void CloseWindow() => Close();

    /// <summary>用户按了 Esc / 右键 / 取消：服务据此撤销整场会话。</summary>
    public void CancelFromService() => Settle(null);

    private WriteableBitmap? BuildMonitorBitmap()
    {
        if (_frame is not { } frame) return null;     // 编辑态没有整帧，也不需要（底图由贴图交进来）
        try
        {
            var (ox, oy) = CaptureGeometry.CropOffset(_monitor, frame.Bounds);
            var pixels = GdiScreenCapture.Crop(new FrameCopyRequest(
                frame, ox, oy, _monitor.Width, _monitor.Height));
            var bitmap = new WriteableBitmap(_monitor.Width, _monitor.Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
                stream.Write(pixels, 0, pixels.Length);
            bitmap.Invalidate();
            return bitmap;
        }
        catch (Exception ex)
        {
            StarLog.Error($"[CaptureOverlay] 铺帧失败（{DeviceName}）", ex);
            return null;
        }
    }

    // ────────── 工具条（按钮、图标、颜色、粗细一律按 Core 的模型生成）──────────────────

    /// <summary>图标边长与按钮尺寸（DIP）。整条上每个按钮都由这几个数决定：
    /// 加一种画法就多一颗点或浮层里多一项，而不是把条撑成第二行（浮层多长一行就盖住用户正要标的东西）。</summary>
    private const double IconSide = 16;
    private const double ButtonWidth = 27;
    private const double ButtonHeight = 23;

    /// <summary>图标用的笔色。<b>写死在这里而不是交给主题</b>：条底是一条固定的暗色，
    /// 跟着系统主题走会在浅色模式下把按钮刷成白底，图标就糊在底上了。</summary>
    private static readonly Brush Ink = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush InkDim = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarNormal = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarChecked = new SolidColorBrush(Color.FromArgb(0x66, 0x4C, 0xA0, 0xFF));

    /// <summary>浮层里圆点用的实心白（<see cref="Ink"/> 是画笔，带浓度，点出来会像"没选中"）。</summary>
    private static readonly int SolidWhite = Annotation.Opaque(0xFF, 0xFF, 0xFF);

    private Button _shapeButton = null!;
    private readonly List<Button> _brushButtons = new();
    private Button _brushButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _clearButton = null!;
    private Button _copyButton = null!;
    private Flyout? _pickerFlyout;

    /// <summary>正在点的折线（顶点＝选区内物理像素）；null＝没有正在画的折线。</summary>
    private List<PixelPoint>? _polyLine;
    private PixelPoint _hoverLocal;                 // 折线的"橡皮筋"拖到哪儿

    /// <summary>
    /// 生成整条工具条：一颗「图形」（矩形/椭圆/直线/折线/箭头收在同一层里，见
    /// <see cref="AnnotationTools.Shapes"/>）+ 四颗各占一位的笔（画笔/荧光/打码/文字）
    /// +「当前这支笔」+ 撤销/重做/清空 + 四个动作。文字全部收进 ToolTip，条上只有图标。
    /// <para>真机反馈两轮把它推到了这个形状：先嫌"带文字的下拉太大"（于是全上图标），
    /// 再嫌"图形一种占一颗太铺开"（于是图形收进一个选择栏，新增折线也不再撑长条）。</para>
    /// <para>颜色与粗细收在「笔」那颗点开的浮层里（点当前工具图标也开同一层）：
    /// 藏起来的是选择，不是状态——条上那颗点始终看得出当前颜色与粗细。</para>
    /// </summary>
    private void BuildToolBar()
    {
        _shapeButton = IconButton(ToolIcon(AnnotationTool.Rectangle), ShapeButtonText());
        _shapeButton.Click += (_, _) => ShowShapePicker(_shapeButton);
        BarRow.Children.Add(_shapeButton);

        foreach (AnnotationTool tool in AnnotationTools.Brushes)
        {
            var button = IconButton(ToolIcon(tool),
                $"{Annotation.ToolName(tool)}：{Annotation.ToolHint(tool)}（再点一次换颜色和粗细）");
            button.Tag = tool;
            button.Click += BrushTool_Click;
            button.RightTapped += BrushTool_RightTapped;   // 换颜色/粗细从"再点一次"挪到这里（点按那条按用户要求让给"取消选中"）
            _brushButtons.Add(button);
            BarRow.Children.Add(button);
        }

        _brushButton = IconButton(BrushIcon(), "当前这支笔：点开换颜色和粗细");
        _brushButton.Click += (_, _) => ToggleBrushPicker(_brushButton);
        BarRow.Children.Add(_brushButton);

        BarRow.Children.Add(Separator());
        _undoButton = IconButton(ArrowIcon(left: true), "撤销上一条（Ctrl+Z）");
        _undoButton.Click += Undo_Click;
        _redoButton = IconButton(ArrowIcon(left: false), "重做上一条（Ctrl+Y）");
        _redoButton.Click += Redo_Click;
        _clearButton = IconButton(EraserIcon(), "清空全部标注（还能撤销回来）");
        _clearButton.Click += Clear_Click;
        BarRow.Children.Add(_undoButton);
        BarRow.Children.Add(_redoButton);
        BarRow.Children.Add(_clearButton);

        BarRow.Children.Add(Separator());
        _copyButton = IconButton(CopyIcon(), "把带标注的画面复制进剪贴板（Enter）");
        _copyButton.Click += Copy_Click;
        var save = IconButton(SaveIcon(), "存成 PNG（Ctrl+S）");
        save.Click += Save_Click;
        // 贴图态不再显示"再钉一张"这颗：这张本来就钉着，多一颗只会让人猜那份去哪了。
        if (!_pinned)
        {
            var pin = IconButton(PinIcon(), "钉在桌面上（同 F3），带上刚画的标注");
            pin.Click += Pin_Click;
            BarRow.Children.Add(pin);
        }
        var ocr = IconButton(OcrIcon(), "认出这块画面的文字并复制");
        ocr.Click += Ocr_Click;
        BarRow.Children.Add(_copyButton);
        BarRow.Children.Add(save);
        BarRow.Children.Add(ocr);
        if (_pinned)
        {
            // 这颗按下去动的是<b>所有</b>贴图（名册的既定口径：收不到鼠标的窗只能靠全局通道救回来），
            // 说明写错范围会让人以为只有自己手上这张被点穿。
            var through = IconButton(ThroughIcon(), "鼠标穿透：让所有贴图都不再收鼠标（再用这一颗或按 F5 恢复）");
            through.Click += Through_Click;
            BarRow.Children.Add(through);
        }
        var cancel = IconButton(CrossIcon(), _pinned ? "关闭这张（Esc）" : "结束这一屏（Esc）");
        cancel.Click += Cancel_Click;
        BarRow.Children.Add(cancel);

        _undoButton.IsEnabled = _redoButton.IsEnabled = _clearButton.IsEnabled = false;
        SyncTools();
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
        Margin = new Thickness(3, 2, 3, 2),
    };

    private static Button IconButton(UIElement icon, string tip)
    {
        var button = new Button
        {
            Content = icon,
            Width = ButtonWidth,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = BarNormal,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tip);
        return button;
    }

    private void BrushTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AnnotationTool tool }) return;
        // 用户口径：点一次选中，再点一次取消选中（回到"不动笔"＝可以改框的那一态）
        if (tool == _tool) SetTool(null);
        else SetTool(tool);
    }

    /// <summary>右键当前已选中的那支笔＝换颜色和粗细（这颗按钮本来就是"笔"，浮层留在它身上最省事）。</summary>
    private void BrushTool_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not Button { Tag: AnnotationTool tool } button || tool != _tool) return;
        ToggleBrushPicker(button);
        e.Handled = true;
    }

    /// <summary>换工具。<b>所有换工具的入口都必须走这里</b>：先落笔（正在打的那行字、正在点的折线），
    /// 否则"换了个工具，刚画的东西凭空消失"。传 null＝取消选中（回到改框那一态）。</summary>
    private void SetTool(AnnotationTool? tool)
    {
        EndTextEditing(commit: true);
        FinishPolyLine(commit: true);
        _tool = tool;
        SyncTools();
        ApplyCursor();
    }

    /// <summary>
    /// 光标跟着"这一按是画还是改框"走：<b>没选工具＝十字箭头</b>（用户要的"进入拖动模式"的可见信号），
    /// 选了工具＝十字准线（要落笔的地方得看得清）。没有这一步，用户只能靠点一下才知道自己在哪一态。
    /// </summary>
    private void ApplyCursor()
    {
        if (_mode != CaptureMode.Toolbar) return;
        // 正在改哪条边就用哪条边的双向箭头；没在改：选了笔＝十字准线，一支都没选＝十字箭头（可拖框）
        InputSystemCursorShape? shape = _adjust switch
        {
            CaptureGeometry.SelectionEdge.Left or CaptureGeometry.SelectionEdge.Right => InputSystemCursorShape.SizeWestEast,
            CaptureGeometry.SelectionEdge.Top or CaptureGeometry.SelectionEdge.Bottom => InputSystemCursorShape.SizeNorthSouth,
            CaptureGeometry.SelectionEdge.TopLeft or CaptureGeometry.SelectionEdge.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
            CaptureGeometry.SelectionEdge.TopRight or CaptureGeometry.SelectionEdge.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
            CaptureGeometry.SelectionEdge.Move => InputSystemCursorShape.SizeAll,
            _ => null,
        };
        Root.Cursor = Microsoft.UI.Input.InputSystemCursor.Create(
            shape ?? (Armed ? InputSystemCursorShape.Cross : InputSystemCursorShape.SizeAll));
    }

    private string ShapeButtonText()
        => _tool is { } tool
            ? $"图形：{Annotation.ToolName(tool)}（点开换矩形 / 椭圆 / 直线 / 折线 / 箭头；再点一次当前工具＝取消选中）"
            // 贴图态没有"框"可改：未选笔那一按是移动整张图，说明要按它真正的行为写
            : _pinned
                ? "没选工具＝移动这张图（十字箭头：按住可拖走）。点图标开始画"
                : "没选工具＝改框那一态（十字箭头：可拖动选区、可改边缘大小）。点图标开始画";

    /// <summary>把"当前用的是哪种图形、哪一支笔"画出来（图标上没有文字，只能靠底色与那颗点说）。</summary>
    private void SyncTools()
    {
        var shape = _tool is { } current && AnnotationTools.IsShapeTool(current) ? current : AnnotationTool.Rectangle;
        _shapeButton.Content = ToolIcon(shape);
        // 没选工具时图形那颗不许看起来"被选中"：底色是这条链上唯一的"我在哪一态"信号
        _shapeButton.Background = _tool is { } armed && AnnotationTools.IsShapeTool(armed) ? BarChecked : BarNormal;
        ToolTipService.SetToolTip(_shapeButton, ShapeButtonText());
        foreach (var button in _brushButtons)
            button.Background = (AnnotationTool)button.Tag! == _tool ? BarChecked : BarNormal;
        _brushButton.Content = BrushIcon();
        if (_tool is not { } tool)
        {
            ToolTipService.SetToolTip(_brushButton,
                "没选工具：这一按是改框（十字箭头）。点上面任一支笔开始画；已选中的笔右键＝换颜色和粗细");
            return;
        }
        var sizes = string.Join(" / ", Enumerable.Range(0, Annotation.ThicknessSteps.Length)
            .Select(index => Annotation.ThicknessFor(tool, index)));
        ToolTipService.SetToolTip(_brushButton,
            $"当前：{Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Name}"
            + $" · {Annotation.ThicknessNames[Math.Clamp(_weightIndex, 0, Annotation.ThicknessNames.Count - 1)]}"
            + $"（{Annotation.ToolName(tool)} 的细 / 中 / 粗 ≈ {sizes} 像素）。右键这支笔＝换颜色和粗细；再点一次＝取消选中");
    }

    // ────────── 选择浮层：图形 / 颜色 / 粗细（项同样按模型生成）──────────────────

    private void ShowShapePicker(FrameworkElement anchor)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(4) };
        foreach (AnnotationTool shape in AnnotationTools.Shapes)
        {
            var wanted = shape;
            var button = IconButton(ToolIcon(shape),
                $"{Annotation.ToolName(shape)}：{Annotation.ToolHint(shape)}");
            button.Background = shape == _tool ? BarChecked : BarNormal;
            button.Click += (_, _) =>
            {
                // 浮层里也遵守同一条口径：再点当前这个图形＝取消选中（回到改框那一态）
                SetTool(wanted == _tool ? null : wanted);
                HidePicker();
            };
            row.Children.Add(button);
        }
        ShowPicker(anchor, row);
    }

    private void ToggleBrushPicker(FrameworkElement anchor)
    {
        if (_pickerFlyout is not null) HidePicker();
        else ShowBrushPicker(anchor);
    }

    private void ShowBrushPicker(FrameworkElement anchor)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Padding = new Thickness(4) };

        var colours = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var index = 0; index < Annotation.Palette.Count; index++)
        {
            var colour = Annotation.Palette[index];
            var wanted = index;
            var dot = IconButton(DotIcon(colour.Bgra, selected: index == _colourIndex), colour.Name);
            dot.Click += (_, _) =>
            {
                _colourIndex = wanted;
                // 正在输入的那行字跟着换色：否则"选了颜色，字却没变"要用户自己去猜为什么
                if (_editingText) ApplyEditorAccent();
                HidePicker();
            };
            colours.Children.Add(dot);
        }

        var weights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var index = 0; index < Annotation.ThicknessSteps.Length; index++)
        {
            var wanted = index;
            var dot = IconButton(DotIcon(SolidWhite, selected: index == _weightIndex, diameter: 3 + index * 3),
                $"{Annotation.ThicknessNames[index]}（{WeightToolName} 上约 {Annotation.ThicknessFor(_tool ?? AnnotationTool.Pen, index)} 像素）");
            dot.Click += (_, _) =>
            {
                _weightIndex = wanted;
                HidePicker();
            };
            weights.Children.Add(dot);
        }

        rows.Children.Add(colours);
        rows.Children.Add(weights);
        ShowPicker(anchor, rows);
    }

    private void ShowPicker(FrameworkElement anchor, UIElement content)
    {
        _pickerFlyout = new Flyout { Content = content };
        _pickerFlyout.ShowAt(anchor);
    }

    private void HidePicker()
    {
        _pickerFlyout?.Hide();
        _pickerFlyout = null;
        SyncTools();
    }

    // ────────── 折线：点几下钉几个顶点 ──────────

    /// <summary>点一下加一个顶点。<b>直线只能一段，而"沿一条边界描一圈"是截图标注最常见的指示</b>
    /// （真机反馈点名缺它）。顶点是点出来的，所以它不进拖动那套 _stroke 状态。</summary>
    private void PlaceVertex(PixelPoint local)
    {
        ErrorChip.Visibility = Visibility.Collapsed;
        if (_polyLine is null)
        {
            DropSelection();                  // 开始钉新的顶点，就不该再指着上一条（把手也会被 DrawLive 抹掉）
            _polyLine = new List<PixelPoint> { local };
        }
        else if (_polyLine[^1] != local) _polyLine.Add(local);
        _hoverLocal = local;
        DrawLive();
    }

    /// <summary>收口折线。<paramref name="commit"/> 为 false 只用于 Esc 与换选区——
    /// 顶点不足两个不算一条（那只是一个点，画出来什么也指不了）。</summary>
    private void FinishPolyLine(bool commit)
    {
        var points = _polyLine;
        _polyLine = null;
        LiveLayer.Children.Clear();
        if (points is null) return;
        if (!commit || points.Count < Annotation.MinPoints(AnnotationTool.PolyLine)) return;
        var mark = new Annotation(AnnotationTool.PolyLine, points, ColourBgra, ThicknessForTool);
        if (mark.Problem() is { } problem)
        {
            ShowError(problem);
            return;
        }
        _history.Add(mark);
        _selected = _history.Count - 1;       // 与 EndStroke 同一口径：刚画完的那条立刻可挪/可缩放
        Rebake();
        DrawSelectionHandles();
    }

    // ────────── 图标：矢量图元画的，不用字体字形 ──────────
    // 为什么不用图标字体：缺字会显示成方块，而遮罩窗一按 Esc 就没了，"这九个字形到底长什么样"
    // 没人能在真机上一眼逐个确认。画出来的图元至少几何是自己算出来的，撞了车也能在断言里看出来。

    private static Canvas Icon(params UIElement[] parts)
    {
        var canvas = new Canvas { Width = IconSide, Height = IconSide };
        foreach (var part in parts) canvas.Children.Add(part);
        return canvas;
    }

    private static Line Seg(double x1, double y1, double x2, double y2, double thickness = 1.6, Brush? brush = null)
        => new()
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush ?? Ink,
            StrokeThickness = thickness,
        };

    private static Polyline Curve(Brush? brush, params (double X, double Y)[] pts)
    {
        var line = new Polyline { Stroke = brush ?? Ink, StrokeThickness = 1.5 };
        foreach (var p in pts) line.Points.Add(new Point(p.X, p.Y));
        return line;
    }

    private static T Placed<T>(T part, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(part, x);
        Canvas.SetTop(part, y);
        return part;
    }

    private static Rectangle Out(double x, double y, double w, double h, double thickness = 1.5, Brush? brush = null)
        => Placed(new Rectangle { Width = w, Height = h, Stroke = brush ?? Ink, StrokeThickness = thickness }, x, y);

    private static Rectangle Fill(double x, double y, double w, double h, Brush brush)
        => Placed(new Rectangle { Width = w, Height = h, Fill = brush }, x, y);

    private static Ellipse Ring(double x, double y, double w, double h, double thickness = 1.5)
        => Placed(new Ellipse { Width = w, Height = h, Stroke = Ink, StrokeThickness = thickness }, x, y);

    /// <summary>浮层里的一颗点：选中＝白圈加粗，未选中＝细灰圈（两种都要在暗底上看得出来）。</summary>
    private static UIElement DotIcon(int bgra, bool selected, double? diameter = null)
    {
        var side = diameter ?? 11d;
        return Icon(Placed(new Ellipse
        {
            Width = side,
            Height = side,
            Fill = new SolidColorBrush(ToColor(bgra)),
            Stroke = new SolidColorBrush(selected ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = selected ? 2 : 1,
        }, (IconSide - side) / 2, (IconSide - side) / 2));
    }

    /// <summary>某个工具长什么样。<b>每种工具的差异必须只看图形就分得开</b>：条上没有文字，
    /// 图标撞车就等于把两个功能摆成同一个按钮（直线与折线的差别刻意做成"一段 / 两段带顶点"）。</summary>
    private static UIElement ToolIcon(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => Icon(Out(2.5, 4, 11, 8)),                        // 空心方框
        AnnotationTool.Ellipse => Icon(Ring(2.5, 4, 11, 8)),                         // 空心椭圆
        AnnotationTool.Line => Icon(Seg(3, 13, 13, 3)),                              // 就是一条斜线，没头没尾
        // 两段折 + 顶点小方块：一眼看得出"这是点出来的多段线"，不是一条直线
        AnnotationTool.PolyLine => Icon(Curve(null, (2.5, 13), (7, 5.5), (13.5, 9.5)),
            Fill(5.6, 4.1, 2.8, 2.8, Ink), Fill(12.1, 8.1, 2.8, 2.8, Ink)),
        AnnotationTool.Arrow => Icon(Seg(3, 13, 12, 4),                              // 斜线 + 终点一个开口头
            Seg(12, 4, 7.6, 4.4), Seg(12, 4, 11.6, 8.4)),
        AnnotationTool.Pen => Icon(Curve(null, (2.5, 13), (5.5, 6.5), (8.5, 10.5), (13.5, 2.5))),
        AnnotationTool.Highlighter => Icon(Fill(2.5, 6.5, 11, 5.5,                   // 粗而半透明的一横
            new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))),
            Seg(2.5, 13.5, 13.5, 13.5, 1.2, InkDim)),
        AnnotationTool.Mosaic => Icon(Fill(2.5, 2.5, 5, 5, Ink), Fill(8, 2.5, 5, 5, InkDim),   // 2×2 格子
            Fill(2.5, 8, 5, 5, InkDim), Fill(8, 8, 5, 5, Ink)),
        // 用线段拼出的 "A"：文字工具的通用记号，且不依赖任何字体（字形缺了就是个方块）
        AnnotationTool.Text => Icon(Curve(null, (2.5, 13), (8, 2.5), (13.5, 13)), Seg(5, 9.5, 11, 9.5)),
        // 圆里一个 1：序号工具
        AnnotationTool.Number => Icon(Ring(3, 3, 10, 10, 1.6), Seg(8, 5.5, 8, 11), Seg(6.8, 6.6, 8, 5.5)),
        // 倾斜的橡皮块＋中间一道分界线
        AnnotationTool.Eraser => Icon(Seg(5, 5, 11, 5), Seg(11, 5, 12.5, 11), Seg(12.5, 11, 6.5, 11),
            Seg(6.5, 11, 5, 5), Seg(5.7, 8, 11.8, 8)),
        _ => Icon(Out(2.5, 4, 11, 8)),
    };

    /// <summary>「当前这支笔」：实心点的颜色＝正在用的颜色，点的直径＝正在用的粗细。</summary>
    private UIElement BrushIcon() => DotIcon(ColourBgra, selected: false,
        diameter: 3 + Math.Clamp(_weightIndex, 0, Annotation.ThicknessSteps.Length - 1) * 3);

    /// <summary>撤销 / 重做：同一支箭头只差方向（形状不一样就会被看成两个不同的动作）。</summary>
    private static UIElement ArrowIcon(bool left)
    {
        double X(double v) => left ? v : IconSide - v;
        return Icon(Seg(X(13), 8, X(3.5), 8), Seg(X(3.5), 8, X(7.5), 4.2), Seg(X(3.5), 8, X(7.5), 11.8));
    }

    private static UIElement EraserIcon()
        => Icon(Placed(new Polygon
        {
            Fill = InkDim,
            Points = new PointCollection { new(3, 12), new(9, 12), new(13.5, 4), new(7.5, 3) },
        }, 0, 0), Seg(3, 13.5, 13.5, 13.5, 1.2, InkDim));

    private static UIElement CrossIcon() => Icon(Seg(3.5, 3.5, 12.5, 12.5), Seg(12.5, 3.5, 3.5, 12.5));

    /// <summary>穿透那颗：一个方框被一支箭头穿过——"鼠标会从它身上走过去"这件事得看得出来。</summary>
    private static UIElement ThroughIcon() => Icon(
        Ring(4.5, 3, 8.5, 9, 1.4), Seg(1.5, 14, 14.5, 1.5, 1.8),
        Seg(10.5, 1.5, 14.5, 1.5, 1.8), Seg(14.5, 1.5, 14.5, 5.5, 1.8));

    private static UIElement CopyIcon()
        => Icon(Out(2, 2.5, 8, 9, 1.3, InkDim), Out(6, 5, 8, 9, 1.3));

    private static UIElement SaveIcon()
        => Icon(Seg(8, 1.5, 8, 9.5), Seg(8, 9.5, 4.8, 6.3), Seg(8, 9.5, 11.2, 6.3), Seg(2.5, 13, 13.5, 13));

    private static UIElement PinIcon()
        => Icon(Ring(4.5, 2, 7, 5.5), Seg(8, 7.5, 8, 13.5), Seg(5, 13.5, 11, 13.5));

    private static UIElement OcrIcon() => Icon(Ring(2.5, 2.5, 8, 8, 1.6), Seg(9.8, 9.8, 14, 14, 1.8));

    private int ColourBgra => Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Bgra;

    private int ThicknessForTool => Annotation.ThicknessFor(_strokeTool, _weightIndex);

    private static Color ToColor(int bgra) => Color.FromArgb(
        (byte)(bgra >>> 24), (byte)(bgra >> 16 & 0xFF), (byte)(bgra >> 8 & 0xFF), (byte)(bgra & 0xFF));

    // ────────── 鼠标：按下 → 拖动 → 放开 ──────────

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        var point = e.GetCurrentPoint(Root);
        var physical = ToPhysical(point.Position.X, point.Position.Y);

        if (!point.Properties.IsLeftButtonPressed) return;

        // 已经有选区、这次按在选区里面、而且是"给动作条"的那条链 ⇒ 这一按是**画**，不是重新框选。
        // 选区外面照旧起新框：用户想换个范围就换个范围，标注跟着作废（底图都换了，留着旧的只会对不上）。
        // <b>边框优先于落笔</b>（Snipaste 同款）：选了笔之后，按在选区边缘那一圈仍是"改框"——
        // 不放行的话，选了笔就只能靠"再点一次取消选中"才能调框，等于换范围前要先扔掉手上的笔。
        if (_mode == CaptureMode.Toolbar && !_pinned && _base is not null && TryBeginAdjust(physical, e.Pointer)) return;
        if (_mode == CaptureMode.Toolbar && _base is not null && InsideSelection(physical))
        {
            // 先问"这一按是不是在改已有的那一条"（选中框/把手就在那儿）；不是才轮到画新的
            if (TryBeginGrab(ToLocal(physical), e.Pointer)) return;
            // 一支笔都没选＝不改画面。截图态放行给"改框/重新框一块"（上面那条守卫是唯一出口——
            // 少了它，下面所有 _tool!.Value 都会是空引用）；贴图态没有框可改，这一按＝移动整张图。
            if (_tool is not { } tool)
            {
                if (_pinned) BeginPinDrag(e.Pointer);
                return;
            }
            _strokeTool = tool;                        // 这一笔从头到尾用它，与中途会不会换工具无关
            // 橡皮擦：按住拖过标注，整条擦掉
            if (tool == AnnotationTool.Eraser) { BeginEraseStroke(ToLocal(physical), e.Pointer); return; }
            // 序号：每按一下放一个编号圆点
            if (tool == AnnotationTool.Number) { PlaceNumber(ToLocal(physical)); return; }
            // 折线是"点出来的"，没有按下-拖动-放开这一说：每一按钉一个顶点，收口用 Enter 或双击
            if (tool == AnnotationTool.PolyLine) PlaceVertex(ToLocal(physical));
            else BeginStroke(ToLocal(physical), e.Pointer, tool);
            return;
        }

        // 贴图态没有"重新框一块"这回事：整块画面就是内容，落点稍偏（圆角外、边缘那一像素）
        // 也不能把底图丢掉——那等于把用户已经画好的标注一起清空。
        // 没选笔 ⇒ 这一按是"移动这张图"（与截图态"未选笔＝不动笔、可以改框"同一语义）。
        if (_pinned)
        {
            if (!Armed) BeginPinDrag(e.Pointer);
            return;
        }

        // 自动检测窗口：点在候选窗口上即选中它（按住 Ctrl 时 _detected 为 null，退回手动拖框）
        if (_detected is { } detected && ContainsPoint(detected, AsPixel(physical)))
        {
            if (_mode == CaptureMode.Toolbar) EnterAnnotationMode(detected);
            else { _selection = detected; Commit(_mode == CaptureMode.Pin ? CommitAction.Pin : CommitAction.Ocr); }
            return;
        }

        _startPhysical = physical;
        _awaitingRelease = true;
        ResetAnnotations();
        _selection = null;
        HintChip.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        ErrorChip.Visibility = Visibility.Collapsed;
        Root.CapturePointer(e.Pointer);      // 拖出窗口边界也要继续收到 Moved/Released
        DrawSelection(new IntRect(_startPhysical.X, _startPhysical.Y, 0, 0));
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        if (_draggingPin) { PinDragTo(); return; }
        var point = e.GetCurrentPoint(Root);
        var physical = ToPhysical(point.Position.X, point.Position.Y);

        if (_erasing) { EraseTo(ToLocal(physical)); return; }
        if (_dragOriginal is not null)
        {
            DragTo(ToLocal(physical));
            return;
        }
        if (_adjust != CaptureGeometry.SelectionEdge.None)
        {
            AdjustTo(physical);
            return;
        }
        if (_stroke is not null)
        {
            ExtendStroke(ToLocal(physical));
            return;
        }
        if (_polyLine is not null)
        {
            // 看不见"下一段会落在哪儿"，点出来的顶点就不是想要的形状 ⇒ 最后顶点到光标挂一段橡皮筋
            _hoverLocal = ToLocal(physical);
            DrawLive();
            return;
        }

        // 选区阶段（还没进入标注）：放大镜一直跟；未拖框时做窗口自动检测
        if (!_pinned && _base is null)
        {
            if (!_awaitingRelease)
            {
                if (_autoDetect && !IsControlDown()) UpdateDetected(AsPixel(physical));
                else ClearDetected();
            }
            UpdateMagnifier(AsPixel(physical));
        }

        if (!_awaitingRelease) return;
        var selection = CaptureGeometry.Normalize(_startPhysical.X, _startPhysical.Y, physical.X, physical.Y);
        // 夹回本屏：L1 按"每屏各截各的"处理跨屏拖拽（每屏一个遮罩窗，各拿各的选区，互不合并）
        _selection = CaptureGeometry.Intersect(selection, _monitor) ?? selection;
        if (_selection is { } box) DrawSelection(box);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        if (_draggingPin)
        {
            _draggingPin = false;
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (_erasing)
        {
            EndEraseStroke();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (_adjust != CaptureGeometry.SelectionEdge.None)
        {
            EndAdjust();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (_dragOriginal is not null)
        {
            // 拖动改的是已有的那一条：这条链与"新画一笔"的收口（EndStroke）是两件事，别混在一起
            EndDrag();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (_stroke is not null)
        {
            EndStroke();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (!_awaitingRelease) return;
        _awaitingRelease = false;
        Root.ReleasePointerCapture(e.Pointer);
        if (_selection is not { } selection) return;

        // 只点了一下没拖动：不是错误，静默清掉让用户再拖一次（"选区太小"的措辞留给真拖了但太窄的情况）
        if (selection.Width < CaptureGeometry.MinSelectionSide && selection.Height < CaptureGeometry.MinSelectionSide)
        {
            _selection = null;
            ClearSelection();
            return;
        }
        if (CaptureGeometry.SelectionProblem(selection, _monitor) is { } reason)
        {
            ShowError(reason);
            return;
        }
        // 贴图与识字：放开的这个动作本身就是答案，不必再让用户多点一次按钮（Snipaste 的 F3 同理）
        if (_mode != CaptureMode.Toolbar) Commit(_mode == CaptureMode.Pin ? CommitAction.Pin : CommitAction.Ocr);
        else EnterAnnotationMode(selection);
    }

    /// <summary>
    /// 双击的分工：<b>只在选区阶段</b>（还没进入标注）承担"确认并复制"——没框就取本屏，Snipaste 同款。
    /// <para><b>标注阶段双击一律不做全局动作</b>：那是"快速点两下"的高频手势（放两颗序号、
    /// 双击文字想接着改字——那一条会先经过抓取进编辑），这时把整场截图提交复制收走，
    /// 用户看到的就是"我还没弄完，图没了"。折线进行中的双击＝收笔（既有口径）；
    /// 贴图态双击＝快速隐藏这一张（Snipaste 同款，F4/托盘可全部找回）。</para>
    /// </summary>
    private void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        // 折线进行中：双击收笔（既有行为）
        if (_polyLine is not null) { e.Handled = true; FinishPolyLine(commit: true); return; }
        // 贴图：双击＝快速隐藏这一张（Snipaste；F4/托盘可全部找回）
        if (_pinned) { e.Handled = true; HidePin(); return; }
        if (_mode != CaptureMode.Toolbar) return;
        // 已进入标注 ⇒ 双击交给"落笔/编辑"那一层，不再承担提交
        if (_base is not null) return;
        // 选区阶段双击＝复制（没框就先取整屏），Snipaste 同款
        e.Handled = true;
        _selection ??= _monitor;
        EnterAnnotationMode(_selection.Value);
        Commit(CommitAction.Copy);
    }

    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        // 截图时右键＝"这一屏不截"（Snipaste 同款）。
        if (!_pinned) Settle(null);
        // 贴图：右键（未拖动）＝Snipaste 式贴图菜单
        else ShowPinMenu(e.GetPosition(Root));
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // 编辑文字时：Enter 提交、Esc 取消（不提交）。
        // 这一整层是"焦点没真的落进输入框"时的兜底（RD-1 那条"文字编辑无效"）：焦点进去了键会先被
        // TextBox 吃掉、冒不到这里；没进去时至少不会把整张截图复制走或整场取消。
        if (_editingText && e.Key is VirtualKey.Enter or VirtualKey.Escape)
        {
            e.Handled = true;
            EndTextEditing(commit: e.Key == VirtualKey.Enter);
            return;
        }
        // 编辑文字时 Ctrl+Z / Ctrl+Y 应作用于**编辑框内的输入文本**（TextBox 原生撤销），
        // 而不是标注历史栈——否则历史回滚后编辑中的 index 失效、编辑框与画面错乱。
        // 不设 Handled：让 TextBox 完成原生撤销/重做。
        if (_editingText && IsControlDown() && e.Key is VirtualKey.Z or VirtualKey.Y) return;
        // 编辑文字时 Ctrl+S：先提交这一行字再保存整图——否则保存结果会缺正在编辑的文字。
        if (_editingText && IsControlDown() && e.Key is VirtualKey.S)
        {
            e.Handled = true;
            EndTextEditing(commit: true);
            Commit(CommitAction.Save);
            return;
        }
        if (_polyLine is not null && e.Key is VirtualKey.Enter or VirtualKey.Escape)
        {
            // Enter＝收口这条折线，Esc＝只丢掉这一条；两者都不该动到整场截图
            e.Handled = true;
            FinishPolyLine(commit: e.Key == VirtualKey.Enter);
            return;
        }
        // 方向键：选区阶段＝移动/缩放选区（Shift＝缩放）；标注阶段＝移动选中的标注（Shift＝10px）。
        // Snipaste 同款，坐标为物理像素。
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            e.Handled = true;
            if (_pinned || _base is not null) NudgeSelectedMark(e.Key, IsShiftDown());
            else NudgeRegion(e.Key, IsShiftDown());
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Delete when _selected is not null && !_editingText:
            case VirtualKey.Back when _selected is not null && !_editingText:
                // 选中之后总得能删掉：只有"撤销"的话，删中间那条要把后面几条一起退掉。
                // <b>编辑中不放行</b>：正在改的那一条同时也是"选中的那一条"，而输入框没接到焦点时
                // 退格会冒到这一层——真机反馈"在编辑里按 Backspace，上一次编辑的文字整个没了"就是它，
                // 一次按键删掉一整条，比删不干净严重得多。
                e.Handled = true;
                DeleteSelected();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                // 贴图态 Esc 分两级：有选中的标注先丢选中（顺手改的取消不伤画面），再按才关这张——
                // 关闭会连没烤出去的标注一起丢，一次误按全没是最贵的出口。截图态 Esc 仍是取消整场（Snipaste 同款）。
                if (_pinned)
                {
                    if (_selected is not null) DropSelection();
                    else Close();
                }
                else Settle(null);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                // Enter 一直是"按当前那条链直接交出去"：截图＝复制走，贴图态是 Toolbar 模式所以同样复制走。
                Commit(_mode switch
                {
                    CaptureMode.Pin => CommitAction.Pin,
                    CaptureMode.Ocr => CommitAction.Ocr,
                    _ => CommitAction.Copy,
                });
                break;
            case VirtualKey.C when IsControlDown():
                e.Handled = true;
                Commit(CommitAction.Copy);
                break;
            case VirtualKey.S when IsControlDown():
                e.Handled = true;
                Commit(CommitAction.Save);
                break;
            case VirtualKey.Z when IsControlDown() && IsShiftDown():
                e.Handled = true;
                Redo();                       // Ctrl+Shift+Z：另一派习惯，两个都给
                break;
            case VirtualKey.Z when IsControlDown():
                e.Handled = true;
                Undo();
                break;
            case VirtualKey.Y when IsControlDown():
                e.Handled = true;
                Redo();
                break;
        }
    }

    // Windows.UI.Core 里也有 Point/Size，与上面 using Windows.Foundation 撞名 ⇒ 这里只能全限定
    private static bool IsShiftDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsControlDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ────────── 坐标换算 ──────────

    private static PixelPoint AsPixel(PointInt32 p) => new(p.X, p.Y);
    /// <summary>窗口内 DIP → 虚拟桌面物理像素（先乘本屏缩放，再加本屏原点）。</summary>
    private PointInt32 ToPhysical(double dipX, double dipY)
        => new(
            _monitor.X + (int)Math.Round(dipX * _scale, MidpointRounding.AwayFromZero),
            _monitor.Y + (int)Math.Round(dipY * _scale, MidpointRounding.AwayFromZero));

    /// <summary>虚拟桌面物理像素 → 窗口内 DIP（画选区与提示用）。</summary>
    private (double X, double Y, double W, double H) ToDip(IntRect selection)
        => ((selection.X - _monitor.X) / _scale,
            (selection.Y - _monitor.Y) / _scale,
            selection.Width / _scale,
            selection.Height / _scale);

    private bool InsideSelection(PointInt32 physical) => _selection is { } s
        && physical.X >= s.X && physical.X < s.Right && physical.Y >= s.Y && physical.Y < s.Bottom;

    /// <summary>虚拟桌面物理像素 → 选区内物理像素（标注模型用的坐标系，原点在选区左上角）。
    /// 贴图编辑时"显示像素"与"底图像素"差一个 zoom，所以这里要除掉——不除就是
    /// 2.5× 的贴图"鼠标明明在字上、笔落在字外"。</summary>
    private PixelPoint ToLocal(PointInt32 physical)
        => new(
            (int)Math.Round((physical.X - (_selection?.X ?? physical.X)) / _sourceScale, MidpointRounding.AwayFromZero),
            (int)Math.Round((physical.Y - (_selection?.Y ?? physical.Y)) / _sourceScale, MidpointRounding.AwayFromZero));

    /// <summary>选区内物理像素 → 窗口内 DIP（摆预览图元与文字输入框用）。</summary>
    private (double X, double Y) LocalToDip(PixelPoint local)
    {
        var s = _selection ?? default;
        return ((s.X + local.X * _sourceScale - _monitor.X) / _scale,
                (s.Y + local.Y * _sourceScale - _monitor.Y) / _scale);
    }

    /// <summary>
    /// 把"用屏幕像素量的容差"换算进底图像素。<b>容差是手感量，只能按屏幕定</b>：
    /// 贴图放大到 5× 时 8 个底图像素＝40 个屏幕像素（在角把手附近随手一点就误判成抓把手），
    /// 缩到 0.2× 时又只剩 1.6 个屏幕像素（那颗 6 DIP 看得见的小方块机械地抓不到）。
    /// 截图那条链 <see cref="_sourceScale"/> 恒为 1 ⇒ 换算结果与常量一字不差，现行为零变化。
    /// </summary>
    private int SlopInSource(int screenPixels)
        => Math.Max(1, (int)Math.Round(screenPixels / _sourceScale, MidpointRounding.AwayFromZero));

    // ────────── 标注：进入、拖动一条、合成 ──────────

    /// <summary>框选放开后进入标注态：把这一块底图拿在手里，之后每改一条就在它上面重烤一次。</summary>
    private void EnterAnnotationMode(IntRect selection)
    {
        if (_frame is not { } frame) return;      // 编辑态走 BeginEditingExisting，这里必须有帧
        var (ox, oy) = CaptureGeometry.CropOffset(selection, frame.Bounds);
        byte[] basePixels;
        try
        {
            basePixels = GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, selection.Width, selection.Height));
        }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 取选区底图失败", ex);
            ShowError("这一块画面的像素没能取到：" + ex.Message);
            return;
        }
        ShowBase(basePixels, selection.Width, selection.Height, selection);
    }

    /// <summary>
    /// 就地编辑贴图的第一帧：底图＝贴图像素，选区＝整个窗口（＝贴图当前显示的那块矩形），
    /// 之后所有编辑动作与截图时<b>走的是同一批代码</b>（八种笔、文字、撤销重做、马赛克增量、复制存图识字）。
    /// </summary>
    private void BeginEditingExisting(byte[] pixels, int width, int height)
    {
        _selection = _monitor;
        ShowBase(pixels, width, height, _monitor);
        HintText.Text = string.Empty;
    }

    /// <summary>
    /// 把一份底图摆上屏幕并进入可标注态。截图与贴图编辑共用这一句：两者唯一的差别是底图从哪来，
    /// 摆法、重烤、工具条定位、光标态完全一致——分成两份写迟早会有一处不同步（本项目已栽过四次）。
    /// </summary>
    private void ShowBase(byte[] basePixels, int contentWidth, int contentHeight, IntRect selection)
    {
        HideMagnifier();
        ClearDetected();
        // <b>selection 必须在这里落进 _selection</b>：手动拖框那条路在拖动时已赋过值，
        // 但候选窗口点击（EnterAnnotationMode）把框直接递进来——不落的话 _selection 保持 null，
        // 之后每一按都判不出"在选区里"，落回"重新框选"并清掉刚铺好的底图
        // （真机反馈"点击候选无反应、只能一直拖框、什么都画不了"就是它）。
        _selection = selection;
        _base = basePixels;
        _contentWidth = Math.Max(1, contentWidth);
        _contentHeight = Math.Max(1, contentHeight);
        _history.Reset();
        _preview = new WriteableBitmap(_contentWidth, _contentHeight);
        AnnotateShot.Source = _preview;
        var (x, y, w, h) = ToDip(selection);
        Canvas.SetLeft(AnnotateShot, x);
        Canvas.SetTop(AnnotateShot, y);
        AnnotateShot.Width = w;
        AnnotateShot.Height = h;
        AnnotateLayer.Visibility = Visibility.Visible;

        Rebake();
        ActionBar.Visibility = Visibility.Visible;
        PositionBar(selection);
        SyncTools();            // 条上不该有任何一颗看起来是选中的：框选完是"改框"那一态
        ApplyCursor();
        _copyButton.Focus(FocusState.Programmatic);
    }

    // ────────── 贴图态：移动、缩放、穿透、角标、悬停出条 ──────────

    /// <summary>
    /// 没选笔时按下拖动＝移动整张图。存"按下时的窗口矩形 + 光标物理坐标"两份快照，
    /// 每帧从快照重算绝对位置——用增量累加会抖（与 MZ 那条抖动教训同一口径）。
    /// </summary>
    private void BeginPinDrag(Pointer pointer)
    {
        WindowInterop.GetCursorPos(out _gestureStartCursor);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
        _draggingPin = true;
        Root.CapturePointer(pointer);
    }

    private void PinDragTo()
    {
        WindowInterop.GetCursorPos(out var cursor);
        // 全程物理像素：光标坐标与窗口矩形同一单位，不需要 DPI 因子，
        // 于是"拖到另一块缩放不同的屏上就越来越偏"这一类错误结构上不存在。
        var x = _gestureStartRect.X + cursor.X - _gestureStartCursor.X;
        var y = _gestureStartRect.Y + cursor.Y - _gestureStartCursor.Y;
        // 可以拖出屏幕去看想看的部分，但不许整块丢光（与缩放共用同一条收边判据）
        var (cx, cy) = CaptureGeometry.PinOrigin(
            x, y, _gestureStartRect.Width, _gestureStartRect.Height, WorkArea());
        if (cx == _lastAppliedX && cy == _lastAppliedY) return;
        _lastAppliedX = cx;
        _lastAppliedY = cy;
        SetMonitor(new IntRect(cx, cy, _monitor.Width, _monitor.Height));
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero, cx, cy, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
        // 换到一块缩放不同的屏：物理尺寸不用动（我们本来就按物理像素摆窗），但 DIP↔物理的除数变了，
        // 不重算就是"拖到另一台显示器上画面突然比窗口大/小一圈，笔也落在偏的地方"。
        if (RefreshScaleIfChanged()) RelayoutContent();
    }

    /// <summary>
    /// 摆这张贴图的新位置/新尺寸。<b>贴图态的"选区"恒等于本窗矩形</b>，所以改 <see cref="_monitor"/>
    /// 必须连它一起改：`ToLocal`／`LocalToDip`／`InsideSelection`／`PositionBar` 全以选区为原点，
    /// 留着旧矩形就等于把笔迹按"拖过的那段距离"整体平移一遍——拖完一次就再也画不到点上，
    /// 而且 `InsideSelection` 会一路报 false，连"这一按是画"都判不出来。
    /// </summary>
    private void SetMonitor(IntRect next)
    {
        _monitor = next;
        if (_pinned) _selection = next;
    }

    /// <summary>
    /// 重量一次本窗所在屏的缩放，变了就更新 <see cref="_scale"/> 并回报 true。
    /// <para>截图态不需要（每屏一窗，窗不会跨屏走）；贴图态会被用户拖到别的屏上，而那条链上
    /// 每一处 DIP 都除以这个数——`SetWindowPos` 之后 WinUI 会按新屏的缩放重新排布局，
    /// 除数留在旧屏的值，画面与窗口就对不上了。</para>
    /// </summary>
    private bool RefreshScaleIfChanged()
    {
        var next = WindowInterop.GetScale(this);
        if (next <= 0 || Math.Abs(next - _scale) < 0.001) return false;
        _scale = next;
        return true;
    }

    /// <summary>滚轮＝缩放这张图（贴图态专属；截图态滚轮没有意义）。</summary>
    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_pinned) return;
        // 手上有未完成的一笔时不改倍率/透明度：那些点是按旧倍率换算的，中途改等于让正在画的那一笔跑偏，
        // 而"跑偏"只在松手合成之后才看得见（那时已经退不掉）。
        if (_stroke is not null || _polyLine is not null || _dragOriginal is not null
            || _draggingPin || _editingText) return;
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        // Shift+滚轮＝整窗透明度（Snipaste 同款）
        if (IsShiftDown())
        {
            SetOpacity(Math.Clamp(_opacity + (delta > 0 ? 0.1 : -0.1), 0.1, 1d));
            e.Handled = true;
            return;
        }
        var next = CaptureGeometry.NextZoom(_zoom, delta);
        if (Math.Abs(next - _zoom) < 0.0001) { SyncBadge(); return; }   // 已在端点：窗不动，角标仍要说清现在几倍
        _zoom = next;
        _sourceScale = next;
        ResizePinAnchoringTopLeft();
        e.Handled = true;
    }

    /// <summary>
    /// 改尺寸时<b>钉住左上角</b>再按 <see cref="CaptureGeometry.PinOrigin"/> 收边（PJ 口径）：
    /// 绕中心缩放会让整块图跑出屏幕再也回不来。
    /// </summary>
    private void ResizePinAnchoringTopLeft()
    {
        var current = WindowInterop.GetWindowRect(this);
        var (w, h) = CaptureGeometry.PinPixelSize(_contentWidth, _contentHeight, _zoom);
        var (x, y) = CaptureGeometry.PinOrigin(current.X, current.Y, w, h, WorkArea());
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero, x, y, w, h,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
        SetMonitor(new IntRect(x, y, w, h));
        _lastAppliedX = x;
        _lastAppliedY = y;
        RefreshScaleIfChanged();        // 收边可能把这张图整个推到另一块屏上
        RelayoutContent();
    }

    /// <summary>窗口矩形变了（缩放）之后重摆内容与工具条：底图与预览的像素没动，动的只是显示尺寸。</summary>
    private void RelayoutContent()
    {
        if (_selection is not { } s) return;
        var (x, y, w, h) = ToDip(s);
        Canvas.SetLeft(AnnotateShot, x);
        Canvas.SetTop(AnnotateShot, y);
        AnnotateShot.Width = w;
        AnnotateShot.Height = h;
        PositionBar(s);
        DrawSelectionHandles();
        SyncBadge();
    }

    /// <summary>
    /// 贴图态的角标：倍率，以及"已穿透"这件事必须留在图上（这张窗收不到鼠标时，
    /// 用户若不知道 F5 就只剩"托盘关掉全部"这一条粗路）。复用截图时那颗尺寸提示 <c>SizeChip</c>——
    /// 它本来就是"这块画面现在是什么样"的说明位，另起一块只会多一处要维护的浮层。
    /// <para>摆在<b>右下</b>：这条工具条在贴图态只能压在画面顶部，左上一个角标会把最右那几颗挡住。</para>
    /// </summary>
    private void SyncBadge()
    {
        if (!_pinned) return;
        SizeChip.HorizontalAlignment = HorizontalAlignment.Right;
        SizeChip.VerticalAlignment = VerticalAlignment.Bottom;
        SizeChip.Margin = new Thickness(0, 0, 1, 1);
        SizeText.Text = CaptureGeometry.FormatZoom(_zoom) + (_clickThrough ? " · 已穿透，按 F5 恢复" : "");
        // 100% 且没穿透＝刚贴上的原样，不必顶一个角标挡画面
        SizeChip.Visibility = Math.Abs(_zoom - 1.0) < 0.0001 && !_clickThrough
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>当前是否鼠标穿透（名册用它决定托盘勾选项的勾选态）。</summary>
    public bool IsClickThrough => _clickThrough;

    /// <summary>
    /// 套用穿透状态并回报有没有真的套上。<b>失败不能静默</b>：那时用户看到的是"点了没反应"。
    /// <para>开成穿透时把工具条收掉：这张窗此后收不到鼠标，那条悬停才收起的条子会一直压在画面上，
    /// 而用户已经没有第二颗按钮能把它点掉（F5 恢复后再移进来就会重新出现）。</para>
    /// </summary>
    public bool ApplyClickThrough(bool on)
    {
        var ok = WindowInterop.SetClickThrough(this, on);
        _clickThrough = ok ? on : _clickThrough;
        if (_clickThrough)
        {
            ActionBar.Visibility = Visibility.Collapsed;
            PinBorder.Visibility = Visibility.Collapsed;    // 穿透中的窗收不到鼠标，悬停高亮也永远等不来退出
        }
        SyncBadge();
        return ok;
    }

    /// <summary>
    /// 放回屏幕（F4 显示全部）。显示走"原生 ShowWindow 兜一遍"：批次 D4 量过 WinUI 的显示调用
    /// 在桌面窗 owned 的那层关系上并不可靠，贴图窗同样挂在桌面上，不该指望另一条路径。
    /// </summary>
    public void Present()
    {
        try
        {
            var hwnd = WindowInterop.GetHwnd(this);
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
            Activate();
        }
        catch (Exception ex) { StarLog.Warn($"[Pin] 唤回贴图失败：{ex.Message}"); }
    }

    public void HidePin()
    {
        try { WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE); }
        catch (Exception ex) { StarLog.Warn($"[Pin] 隐藏贴图失败：{ex.Message}"); }
    }

    /// <summary>工具条平时收起（一屏十几张贴图就十几条横杠，会盖住画面），鼠标进窗即现。</summary>
    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_pinned) return;
        ActionBar.Visibility = Visibility.Visible;
        PinBorder.Visibility = Visibility.Visible;      // 高亮边框：标出"这块画面是一张贴图"（真机点名缺它）
    }

    /// <summary>
    /// 鼠标离开就收起——但<b>正在用笔的时候不许收</b>：选了笔、正在打字、折线还没收口、已选中某条要拖，
    /// 这些时候条子半路消失比看不见更烦（真机反馈里"工具条自己没了"就是这类收起时机错的形状）。
    /// </summary>
    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_pinned || Armed || _editingText || _polyLine is not null || _selected is not null) return;
        ActionBar.Visibility = Visibility.Collapsed;
        PinBorder.Visibility = Visibility.Collapsed;
    }

    /// <summary>本屏工作区（物理像素，已扣任务栏）：选区挪动与改大小都只能在它里面。</summary>
    private IntRect WorkArea()
    {
        var a = WindowInterop.GetWorkArea(this);
        return new IntRect(a.X, a.Y, a.Width, a.Height);
    }

    /// <summary>这一按是不是在改框：落在边/角或框内才是改框；框外放行给"重新框一块"。</summary>
    private bool TryBeginAdjust(PointInt32 physical, Pointer pointer)
    {
        if (_selection is not { } s) return false;
        var edge = CaptureGeometry.SelectionEdgeAt(s, new PixelPoint(physical.X, physical.Y), SelectionSlop);
        if (edge == CaptureGeometry.SelectionEdge.None) return false;
        _adjust = edge;
        _adjustStart = s;
        _adjustPending = s;
        _adjustPress = physical;
        Root.CapturePointer(pointer);
        ApplyCursor();
        return true;
    }

    private void AdjustTo(PointInt32 physical)
    {
        var work = WorkArea();
        var (dx, dy) = (physical.X - _adjustPress.X, physical.Y - _adjustPress.Y);
        _adjustPending = _adjust == CaptureGeometry.SelectionEdge.Move
            ? CaptureGeometry.MoveSelection(_adjustStart, dx, dy, work)
            : CaptureGeometry.ResizeSelection(_adjustStart, _adjust, dx, dy, work, CaptureGeometry.MinSelectionSide);
        DrawSelection(_adjustPending);
    }

    private void EndAdjust()
    {
        _adjust = CaptureGeometry.SelectionEdge.None;
        ApplySelection(_adjustPending);
        DrawSelection(_selection ?? _adjustStart);
        ApplyCursor();
    }

    /// <summary>
    /// 把选区挪到／改成 <paramref name="next"/>：<b>底图重裁一次，标注整体跟着画面平移</b>。
    /// <para>两步必须成对：只挪框不挪标注，已画的矩形/文字就会对到另一块画面上（错位比丢框更难发现）。
    /// 重裁失败时保留原框并说明原因——交出一张"框与内容不一致"的画面比不改更糟。</para>
    /// </summary>
    private void ApplySelection(IntRect next)
    {
        // 改框只在截图态存在（编辑贴图时整块都是内容，没有"另一个框"可裁）
        if (_frame is not { } frame || _selection is not { } old || old == next
            || next.Width < 1 || next.Height < 1) return;
        var (ox, oy) = CaptureGeometry.CropOffset(next, frame.Bounds);
        byte[] cropped;
        try
        {
            cropped = GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, next.Width, next.Height));
        }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 改框后重取底图失败", ex);
            ShowError("改完框之后这一块画面的像素没能取到：" + ex.Message);
            return;
        }
        _selection = next;
        _base = cropped;
        _contentWidth = Math.Max(1, next.Width);
        _contentHeight = Math.Max(1, next.Height);
        _history.ShiftAllBy(old.X - next.X, old.Y - next.Y);   // 标注原点跟着画面走（不制造一步历史）
        _preview = new WriteableBitmap(next.Width, next.Height);
        AnnotateShot.Source = _preview;
        var (x, y, w, h) = ToDip(next);
        Canvas.SetLeft(AnnotateShot, x);
        Canvas.SetTop(AnnotateShot, y);
        AnnotateShot.Width = w;
        AnnotateShot.Height = h;
        Rebake();
        DrawSelectionHandles();
        PositionBar(next);
    }

    /// <summary>换选区或取消：标注与底图一起丢掉（留着旧的会和新的选区对不上）。</summary>
    private void ResetAnnotations()
    {
        EndTextEditing(commit: false);
        FinishPolyLine(commit: false);
        _stroke = null;
        _history.Reset();
        _base = null;
        _preview = null;
        AnnotateLayer.Visibility = Visibility.Collapsed;
        DropSelection();
        _composed = null;
        _scratch = null;
        _detected = null;
        HideMagnifier();
        _undoButton.IsEnabled = _clearButton.IsEnabled = false;
    }

    private void BeginStroke(PixelPoint local, Pointer pointer, AnnotationTool tool)
    {
        ErrorChip.Visibility = Visibility.Collapsed;
        // 上一行字先落笔再动手：在画布上点第二下不该把刚打的字凭空清掉（真机反馈"文字编辑无效"的路径之一）。
        if (tool == AnnotationTool.Text)
        {
            // 落笔必须排在命中<b>之前</b>：真机反馈"第二次编辑时已经输入了文字，点旧字之后刚打的字直接没了"，
            // 原因是这里原先按"先命中、再丢弃正在打的那一行"走（commit: false＝丢），而丢弃并不等于"那条不存在"。
            // 命中也不能先算：落笔那一步可能删掉一条（把一条改成空字＝删那条），先算好的下标就会指着隔壁那条，
            // 于是"改这一行"变成"把字写进另一行"——最坏的一种静默改错。
            EndTextEditing(commit: true);
            // 点在已经写好的那行字上＝回去改它（真机期望"随时可以点击之前编辑的文字继续删减修改"），
            // 点在空白处＝新写一行。命中判据用模型里那一条（与"点一下就选中"同一个式子），不另算一套。
            var hit = AnnotationPainter.HitTest(_history.Marks, local, SlopInSource(SelectionSlop));
            if (hit is { } index && _history.Marks[index].Tool == AnnotationTool.Text)
            {
                _selected = index;
                DrawSelectionHandles();
                BeginTextEdit(local, _history.Marks[index], index);
                return;
            }
            BeginTextEdit(local);
            return;
        }
        // 非文字工具：上一行字先落笔再动手（真机反馈"文字编辑无效"的路径之一）。
        EndTextEditing(commit: true);
        // 起新的一笔就不再指着上一条了：选择框留在原地会挡住看新画的形状，下标也会变成误导
        DropSelection();
        // 打码的"一笔"从按下那一下就该看见：同一格糊掉与"还没糊"对用户是两个完全不同的结果，
        // 所以起点先按"一个点画两遍"存（MosaicBrush 走的是段，两个重合的点正好糊掉笔尖那一格）。
        _stroke = tool == AnnotationTool.Mosaic
            ? new List<PixelPoint> { local, local }
            : new List<PixelPoint> { local };
        if (tool == AnnotationTool.Mosaic) StartMosaicScratch(local);
        Root.CapturePointer(pointer);
        PaintPreview();
    }

    /// <summary>
    /// 开一条打码：以"已提交的那张合成图"为起点，之后每帧只往上补新走过的那一段。
    /// <b>不再每帧从底图重烤整张</b>——那正是真机反馈"打码速度远落后于鼠标移动速度"的成因：
    /// 一次重烤的代价 ∝ 选区面积 × 已有标注条数，手一快就落在后面。
    /// </summary>
    private void StartMosaicScratch(PixelPoint local)
    {
        _scratch = _composed is { } composed ? (byte[])composed.Clone() : null;
        _scratchTail = local;
    }

    /// <summary>把 _scratch 推到屏幕。整块缓冲上传是 memcpy，节流只为挡住每秒上百次的重复上传。</summary>
    private void FlushMosaicScratch()
    {
        if (_scratch is not { } scratch || _preview is not { } preview) return;
        try
        {
            using var stream = preview.PixelBuffer.AsStream();
            stream.Write(scratch, 0, scratch.Length);
            preview.Invalidate();
        }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 打码预览上传失败", ex);
            ShowError("打码预览没能刷到屏幕上：" + ex.Message);
        }
    }

    /// <summary>拖动中的预览：画笔/荧光/形状走近似图元，打码走真像素（增量补段）。</summary>
    private void PaintPreview()
    {
        if (_strokeTool != AnnotationTool.Mosaic)
        {
            DrawLive();
            return;
        }
        if (_stroke is not { Count: > 1 } points || _scratch is not { } scratch
            || _base is not { } || _preview is null || _selection is not { } selection) return;
        // 先补段（每帧都做，代价只与"这一帧走了多远"成正比），再按节奏上传整块缓冲：
        // 少上传一次不会丢东西——下一帧会把之前画的段一起带上去。
        var tail = points[^1];
        if (tail != _scratchTail)
        {
            AnnotationPainter.Paint(scratch, _contentWidth, _contentHeight,
                new Annotation(AnnotationTool.Mosaic, new[] { _scratchTail, tail }, ColourBgra, ThicknessForTool));
            _scratchTail = tail;
        }
        if (Environment.TickCount64 - _lastMosaicPreview < 16) return;
        _lastMosaicPreview = Environment.TickCount64;
        FlushMosaicScratch();
    }

    private void ExtendStroke(PixelPoint local)
    {
        if (_stroke is not { Count: > 0 } points) return;
        if (points[^1] == local) return;                 // 鼠标不动也会反复回调：重复点会让折线的点数爆掉
        // 两点工具（矩形/椭圆/直线/箭头）的形状只由"按下那点"与"放开那点"决定：中途的采样是走过的痕迹，
        // 覆盖掉而不是追加。留着它们，一条矩形会在历史里带着几十个点，而任何按"第二个点"取另一端的写法
        // 都会画出针尖大的框——预览取最后一点、落笔取第二点，就是"松手后图形变得非常小"的成因。
        if (points.Count > 1 && Annotation.IsTwoPointTool(_strokeTool)) points[^1] = local;
        else points.Add(local);
        PaintPreview();
    }

    private void EndStroke()
    {
        var points = _stroke;
        _stroke = null;
        LiveLayer.Children.Clear();
        if (points is null) return;
        if (points.Count < Annotation.MinPoints(_strokeTool))
        {
            // 点了一下没拖：以前是静默丢掉（真机反馈"点了没反应"），现在改成"选中脚下那一条"。
            // 打码（点一下糊一格）与文字（点一下出输入框）走不到这里——它们的那一下本来就够点数。
            SelectAtTap(points[0]);
            return;
        }
        var mark = new Annotation(_strokeTool, points, ColourBgra, ThicknessForTool);
        if (mark.Problem() is { } problem)
        {
            ShowError(problem);
            return;
        }
        _history.Add(mark);
        // 刚画完的那条就是接下来最想改的：不用再点一次就把它选中。
        // 打码**不**自动选中——它是"同一块区域反复补几笔"的面积笔刷，
        // 选中后"点住就拖"会变成移动整条，正对着 RE-2 修好的"点一下就糊一格"。
        _selected = mark.Tool == AnnotationTool.Mosaic ? null : _history.Count - 1;
        Rebake();
        DrawSelectionHandles();
    }

    // ────────── 选中：点住拖动改位置，四角拖改大小，顶上那颗转方向 ──────────

    /// <summary>当前选中的那一条（下标会因撤销/删除而失效，一律现取，不缓存引用）。</summary>
    private Annotation? Selected =>
        _selected is { } i && i < _history.Count ? _history.Marks[i] : null;

    /// <summary>
    /// 这一按是不是"抓住已有的那一条"。顺序＝<b>旋转把手 → 四角（缩放）→ 框内（移动）</b>：
    /// 把手就画在框的边角上，反过来先判框内，四角会被"移动"整锅吃掉。
    /// <para>命中框内时连选中一起改。Snipaste 那套"先点一下选中、再点一下才拖"在这里会变成
    /// 用户点了两下没反应（第一下被吃掉了看不见），"点住就拖"一次就成。</para>
    /// </summary>
    private bool TryBeginGrab(PixelPoint local, Pointer pointer)
    {
        // 正在打字、或正在钉折线顶点时，这一按有它自己的含义（落笔/钉点），不能被"抓住上一条"抢走：
        // 抢走就等于把刚打的一行字丢在半路——那是批次 RD-1 刚堵掉的那一类丢字路径。
        if (_editingText || _polyLine is not null) return false;
        if (_selected is not { } index) return false;
        if (Selected is not { } mark) { DropSelection(); return false; }

        _grab = mark.GrabAt(local, RotateHandle(mark), SlopInSource(MoveSlop));
        if (_grab == Grab.None) return false;

        // 按的是某一头的把手 ⇒ 钉住的那一点改到<b>对面</b>那头（模型算，界面不猜）：
        // 否则绕字块中心缩放会把左上角一起推出去，真机反馈就是"一缩放整行字和它的框都跑了"。
        _dragOriginal = _grab == Grab.Scale ? mark.WithScalePivotTowards(local) : mark;
        _dragAnchor = local;
        _dragLast = local;
        _underDrag = UnderDragBuffer(index);
        _dragCanvas = _underDrag is { } under ? (byte[])under.Clone() : null;
        Root.CapturePointer(pointer);
        DrawSelectionHandles();
        return true;
    }

    /// <summary>
    /// 旋转把手落在哪儿。<b>顶边贴到画面上沿时把它挪进框内</b>：画在画面外面的那一按不属于本窗的
    /// "在画面里"那条链（截图态会被当成重新框选，贴图态干脆落在窗外），等于这颗永远点不到。
    /// <para><b>比较要在底图像素这一层做</b>：`box` 是选区内坐标（原点＝画面左上角），而 `_selection.Y`
    /// 是虚拟桌面坐标——拿桌面坐标当边界，副屏在主屏下方时那个不等式对每条标注都成立（把手永远被
    /// 塞进框内压住内容），副屏在主屏上方时又永不成立（贴顶的那颗画到窗外、点不到）。
    /// 原点在 0 时两种写法恰好同值，所以这条错只有多屏才露出来。</para>
    /// </summary>
    private PixelPoint RotateHandle(Annotation mark)
    {
        var box = mark.Bounds();
        var lift = SlopInSource(RotateHandleLift);
        if (box.Y - lift < 0) lift = Math.Min(box.Height / 2, lift);
        return new PixelPoint(box.X + box.Width / 2, box.Y - lift);
    }

    /// <summary>
    /// "除了被拖这条、其余都在原位"的那张底，拖动开始时算一次；之后每帧只做
    /// <b>一次整块复制 + 一条重画</b>。每帧从底图重烤全部标注的话，标注一多就又变成"跟不上手"
    /// 那一类（正是打码那条反馈的同一个成因）。
    /// </summary>
    private byte[]? UnderDragBuffer(int excluding)
    {
        if (_base is not { } basePixels || _selection is not { }) return null;
        var rest = new List<Annotation>(_history.Marks);
        rest.RemoveAt(excluding);
        // 与 Rebake 同一口径：尺寸跟着底图走，不跟选区（贴图态那是显示尺寸）走。
        try { return AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, rest); }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 拖动底图准备失败", ex);
            return null;
        }
    }

    /// <summary>拖动过程中：算出这一帧该长什么样，画进预览缓冲，再跟着更新选择框。</summary>
    private void DragTo(PixelPoint local)
    {
        _dragLast = local;
        if (_dragOriginal is not { } original || _grab == Grab.None) return;
        var preview = original.DraggedBy(_dragAnchor, local, _grab);
        if (_dragCanvas is { } canvas && _underDrag is { } under
            && _preview is { } previewBitmap && _selection is { } selection)
        {
            Buffer.BlockCopy(under, 0, canvas, 0, under.Length);
            try
            {
                AnnotationPainter.Paint(canvas, _contentWidth, _contentHeight, preview);
            }
            catch (Exception ex)
            {
                // 预览画不出来不当场说，用户就要到松手才发现这一改是坏的
                ShowError("这一改画不出来：" + ex.Message);
                return;
            }
            using (var stream = previewBitmap.PixelBuffer.AsStream())
                stream.Write(canvas, 0, canvas.Length);
            previewBitmap.Invalidate();
        }
        DrawSelectionHandles(preview);
    }

    // 拖动的算式本身在模型里（Annotation.DraggedBy），界面不再自己 switch：
    // 那一版读的是可变字段 _grab，而松手那一步会先把它清零 ⇒ "拖位置"在松手瞬间被算成"拖倍数"
    // （真机反馈"拖动文字会变大"，日志原件：[AnnoGrab] Move 跟着 [AnnoDrag] Move scale 1.000→6.000）。

    /// <summary>松手：把这一改落进历史（算一步，撤销能回去）。没真的动过就不制造一步空历史。</summary>
    private void EndDrag()
    {
        var original = _dragOriginal;
        var index = _selected;
        var grab = _grab;
        _dragOriginal = null;
        _underDrag = null;
        _dragCanvas = null;
        _grab = Grab.None;
        if (original is null || index is not { } i || grab == Grab.None) return;
        var result = original.DraggedBy(_dragAnchor, _dragLast, grab);
        if (result == original)
        {
            DrawSelectionHandles();
            // 按住的是已经写好的那行字、按下到松手几乎没有移动 ⇒ 这是"点回去改它"（真机期望：
            // 随时可以点击之前编辑的文字，在编辑框里继续删减修改）。真拖过了就还是上一条语义＝移动位置，
            // 不该在这种时候弹框。
            if (grab == Grab.Move && original.Tool == AnnotationTool.Text &&
                Annotation.Near(_dragAnchor, _dragLast, SlopInSource(SelectionSlop))) BeginTextEdit(_dragAnchor, original, i);
            return;
        }

        _history.ReplaceAt(i, result);
        Rebake();
        DrawSelectionHandles();
    }

    /// <summary>
    /// 选择框 + 把手。传 <paramref name="mark"/> 时画的是"正在拖的那一条"的新位置。
    /// <para>名字刻意与 <see cref="DrawSelection(IntRect)"/> 分开：那个画的是"框选出来的哪一块"，
    /// 这个画的是"选中的哪一条标注"，两件事共用一个名字迟早会有人调错。</para>
    /// </summary>
    private void DrawSelectionHandles(Annotation? mark = null)
    {
        LiveLayer.Children.Clear();           // 先清再画：把手只有几颗，叠两层就会糊成一团黑方块
        mark ??= Selected;
        if (mark is null)
        {
            SelectionTip.Visibility = Visibility.Collapsed;
            return;
        }
        var box = mark.Bounds();
        var (left, top) = LocalToDip(new PixelPoint(box.X, box.Y));
        var (right, bottom) = LocalToDip(new PixelPoint(box.Right, box.Bottom));
        LiveLayer.Children.Add(Out(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
        foreach (var corner in mark.Corners())
        {
            var (x, y) = LocalToDip(corner);
            LiveLayer.Children.Add(Fill(x - 3, y - 3, 6, 6, Ink));
        }
        // 每一类标注都给旋转把手：文字的旋转走"字模覆盖率再铺回去"那条路（见 GdiTextDrawer），
        // 与几何类一样能被像素断言钉住，所以不必再对文字单独关一档。
        var (hx, hy) = LocalToDip(RotateHandle(mark));
        LiveLayer.Children.Add(new Line { X1 = hx, Y1 = hy, X2 = hx, Y2 = top, Stroke = Ink, StrokeThickness = 1 });
        LiveLayer.Children.Add(Fill(hx - 3, hy - 3, 6, 6, Ink));
        SelectionTip.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 丢掉选中（连带把手与那句说明）。
    /// <para><b>历史一变就必须调</b>：撤销/重做/清空/删除都会移动下标，留着旧下标就成了
    /// "框指着矩形、下一个拖动改的是椭圆"。拖动状态也一起收：那些缓冲是按旧下标算的。</para>
    /// </summary>
    private void DropSelection()
    {
        // 正在改的那一条被历史移动带走了：留住下标就等于把这一笔字写进"另一条"里（最坏的一种静默改错）
        if (_editingText && _editingIndex is not null) EndTextEditing(commit: false);
        _editingIndex = null;
        _selected = null;
        _grab = Grab.None;
        _dragOriginal = null;
        _dragLast = default;
        _underDrag = null;
        _dragCanvas = null;
        LiveLayer.Children.Clear();
        SelectionTip.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// "这一按没拖出形状"（点了一下就走）原来会被静默丢掉——真机反馈的"点了没反应"就是它。
    /// 现在用它去选中脚下那条：已有的标注于是有一个不需要额外模式、也不会挡住画新东西的出口。
    /// <para>只在 <see cref="Annotation.MinPoints"/> 判定为"画不成"的那条链上触发，所以打码（点一下
    /// 就该糊掉一格）与文字（点一下就该出输入框）的语义完全没动。</para>
    /// </summary>
    private void SelectAtTap(PixelPoint at)
    {
        var index = AnnotationPainter.HitTest(_history.Marks, at, SlopInSource(SelectionSlop));
        _selected = index;
        DrawSelectionHandles();               // 没点中时它也负责把手与说明一起收掉
    }

    /// <summary>删掉选中的那一条（Delete / Backspace）。算一步，撤销能拿回来。</summary>
    private void DeleteSelected()
    {
        if (_selected is not { } index) return;
        _history.RemoveAt(index);
        DropSelection();
        Rebake();
    }

    /// <summary>
    /// 拿底图把所有标注重烤一遍，并把结果摆成选区那块画面。
    /// <para>重烤而不是增量叠画：撤销、清空、改顺序这些操作就都不需要反向运算，
    /// 而"部分成功"（撤销了一条却残留半条）这类缺陷也结构上不可能出现。</para>
    /// <para>结果同时留一份在 <c>_composed</c>：下一条打码要以"已经画成的这张"为起点做增量预览
    /// （见 <see cref="StartMosaicScratch"/>），而<b>提交仍以这一次全烤为准</b>——
    /// 拖动中的增量只负责跟手，绝不会变成最终输出的第二条口径。</para>
    /// </summary>
    private void Rebake()
    {
        if (_base is not { } basePixels || _preview is not { }) return;
        // 正在改的那一条先不烤进画面：输入框就压在它原来的位置上，两份同时画出来
        // 就是真机反馈的"编辑中文字和已编辑文字重叠，红白两层"。落笔/取消后它自然回来。
        var marks = _editingText && _editingIndex is { } hidden && hidden < _history.Count
            ? _history.Marks.Where((_, i) => i != hidden).ToList()
            : _history.Marks;
        try
        {
            // 渲染尺寸取底图自己的尺寸，<b>不取选区</b>：贴图态"选区"＝窗口的显示尺寸（＝底图 × 倍率），
            // 按它渲染就是"缓冲比声明的尺寸短，画上去会越界"——真机反馈"标注没能画上去"的那条报信。
            var composed = AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, marks);
            using (var stream = _preview.PixelBuffer.AsStream())
                stream.Write(composed, 0, composed.Length);
            _preview.Invalidate();
            _composed = composed;
            _scratch = null;
        }
        catch (Exception ex)
        {
            // 画不上去必须看得见：交出一张"少了刚画的那条"的图，用户完全没有办法发现
            StarLog.Error("[CaptureOverlay] 合成标注失败", ex);
            ShowError("标注没能画上去：" + ex.Message);
            return;
        }
        ErrorChip.Visibility = Visibility.Collapsed;
        _undoButton.IsEnabled = _history.CanUndo;
        _redoButton.IsEnabled = _history.CanRedo;
        _clearButton.IsEnabled = _history.Count > 0;
    }

    /// <summary>拖动中的那一条用简单图元近似显示；松手立刻换成真像素。</summary>
    private void DrawLive()
    {
        LiveLayer.Children.Clear();
        if (_polyLine is { Count: > 0 } vertices)
        {
            DrawPolyLinePreview(vertices);
            return;
        }
        if (_stroke is not { Count: > 0 } points) return;
        var brush = new SolidColorBrush(ToColor(ColourBgra));
        var thickness = Math.Max(1.0, ThicknessForTool / _scale);
        var first = LocalToDip(points[0]);

        switch (_strokeTool)
        {
            case AnnotationTool.Rectangle:
            {
                var last = LocalToDip(points[^1]);
                AddShape(new Rectangle { Stroke = brush, StrokeThickness = thickness },
                    Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                    Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
                return;
            }

            case AnnotationTool.Ellipse:
            {
                var last = LocalToDip(points[^1]);
                AddShape(new Ellipse { Stroke = brush, StrokeThickness = thickness },
                    Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                    Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
                return;
            }

            case AnnotationTool.Arrow:
            {
                var head = Annotation.ArrowBarbs(
                    points[0], points[^1], ThicknessForTool)
                    .Select(barb => LocalToDip(barb)).ToList();
                AddLine(brush, thickness, first, LocalToDip(points[^1]));
                if (head.Count == 3)
                {
                    AddLine(brush, thickness, head[0], head[1]);
                    AddLine(brush, thickness, head[1], head[2]);
                }
                return;
            }
        }

        var polyline = new Polyline { Stroke = brush, StrokeThickness = thickness };
        foreach (var point in points)
        {
            var (x, y) = LocalToDip(point);
            polyline.Points.Add(new Point(x, y));
        }
        LiveLayer.Children.Add(polyline);
    }

    /// <summary>正在点的折线：已定的段实线、最后一顶点到光标那段半透明，顶点各摆一颗白点
    /// （不画顶点就分不清"这里断了一段"与"这里只是一笔经过"）。</summary>
    private void DrawPolyLinePreview(IReadOnlyList<PixelPoint> vertices)
    {
        var thickness = Math.Max(1.0, ThicknessForTool / _scale);
        var corners = vertices.Select(LocalToDip).ToList();
        if (corners.Count >= 2) LiveLayer.Children.Add(Band(corners, Ink, thickness));
        var trailing = new List<(double X, double Y)>(corners) { LocalToDip(_hoverLocal) };
        if (trailing.Count >= 2) LiveLayer.Children.Add(Band(trailing, InkDim, thickness));
        foreach (var corner in corners)
            LiveLayer.Children.Add(Placed(Fill(corner.X - 2, corner.Y - 2, 4, 4, Ink), corner.X - 2, corner.Y - 2));
    }

    private static Polyline Band(IReadOnlyList<(double X, double Y)> pts, Brush brush, double thickness)
    {
        var line = new Polyline { Stroke = brush, StrokeThickness = thickness };
        foreach (var p in pts) line.Points.Add(new Point(p.X, p.Y));
        return line;
    }

    private void AddShape(Shape shape, double x, double y, double w, double h)
    {
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        shape.Width = Math.Max(1, w);
        shape.Height = Math.Max(1, h);
        LiveLayer.Children.Add(shape);
    }

    private void AddLine(Brush brush, double thickness, (double X, double Y) from, (double X, double Y) to)
    {
        var line = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = brush, StrokeThickness = thickness };
        LiveLayer.Children.Add(line);
    }

    // ────────── 文字标注：就地输入 ──────────

    /// <summary>
    /// 就地开一个输入框。<b>点一下就该能直接打字</b>（用户的原话是"不需要再次点击文字编辑框内区域才能输入"），
    /// 所以这里管三件事：框摆在哪儿、字色描边、以及<b>键盘焦点真的落进去</b>。
    /// <para><paramref name="editing"/> 非空＝改已经写好的那一条：框里带上原文、光标停在末尾，
    /// 落笔时替换那一条而不是再加一条。位置用它<b>当下</b>的包围盒左上角——那条字可能已经被拖走过或放大过。</para>
    /// </summary>
    private void BeginTextEdit(PixelPoint local, Annotation? editing = null, int? index = null)
    {
        _editingText = true;
        _editingIndex = editing is null ? null : index;
        // 颜色跟着"这一条自己的颜色"，不跟着调色板：用户改旧字时可能已经换成了别的颜色，
        // 输入框显示红的、落笔仍是白的＝"编辑时和编辑完不是一份字"（真机反馈的红白两层之一）。
        _editorColourBgra = editing?.EffectiveColorBgra ?? ColourBgra;
        // "让位"必须长在开框这一步里，不能指望调用方随后重烤：Rebake 的那条排除只在下标已经写进字段之后
        // 才生效，而两条入口（命中测试、拖动没动）原先都只画把手 ⇒ 底下那份旧字一直留在画面里，
        // 与输入框叠成真机反馈了两轮的"红白两层"。
        Rebake();
        _textAnchor = local;
        // 摆到"真画出去那一格"的左上角（Bounds() 是旋转后的外接框，转过 90° 时它会跑到字外面的空处）
        var at = editing is { } mark ? mark.TransformedPoints()[0] : local;
        var (x, y) = LocalToDip(at);
        // 宽度上限跟着屏幕收：写满一行的字被 MaxWidth 截断＝用户看到的成品与框里不一样。
        // 换行只由 Enter 决定（XAML 里 AcceptsReturn），不许自动折行——编辑框折了而 GDI 不折，就是两张图。
        TextEditor.MaxWidth = Math.Max(180, _monitor.Width / _scale - 20);
        // 靠右边/下边点击时把输入框拉回屏内：越界就等于"输入框跑屏外了，打不了字"
        TextEditorHost.Margin = new Thickness(
            Math.Clamp(x, 0, Math.Max(0, _monitor.Width / _scale - 40)),
            Math.Clamp(y, 0, Math.Max(0, _monitor.Height / _scale - 40)), 0, 0);
        TextEditorHost.Visibility = Visibility.Visible;
        // 字号走的是"底图像素 → 屏幕像素 → DIP"两层：只除 `_scale` 的话，放大过的贴图里
        // 输入框中的字会比烤进去的那份小一个倍率（所见非所得），2.5× 上就是小 2.5 倍。
        TextEditor.FontSize = (editing?.DrawFontHeight ?? Annotation.DefaultFontHeight) * _sourceScale / _scale;
        ApplyEditorAccent();
        TextEditor.Text = editing?.Text ?? string.Empty;
        TextEditor.SelectionStart = TextEditor.Text.Length;   // 改字＝光标落在末尾：退格与接着打字都在手边
        TakeEditorFocus();
    }

    /// <summary>
    /// 把键盘焦点真的送进输入框。刚把宿主从 Collapsed 改成 Visible 的<em>同一帧</em>里
    /// <c>Focus()</c> 会当场返回 false（元素还没量过），键于是全落到遮罩那一层——
    /// 用户看到的就是"框出来了，但必须再点一下框里才能打字"。所以：补一次布局再要，
    /// 仍要不到就在接下来几帧里重试；真拿不到才说实话（静默失效是最难查的一类）。
    /// </summary>
    private void TakeEditorFocus(int triesLeft = 3)
    {
        TextEditor.UpdateLayout();
        if (TextEditor.Focus(FocusState.Programmatic)) return;
        if (triesLeft > 1 && Root.DispatcherQueue.TryEnqueue(() => TakeEditorFocus(triesLeft - 1))) return;
        if (!_editingText) return;        // 用户已改去点别处：这时再说"没焦点"是假警报
        ShowError("这一行字还没拿到键盘焦点：点一下那个描边的输入框再打字（Enter 换行，Esc 结束编辑）");
    }

    /// <summary>
    /// 就地输入那一框的字色与描边：<b>只有这一处</b>在说"用哪个颜色"。
    /// 底板是近乎透明的（真机反馈："点击后不应出现黄色矩形，最好是透明但描边的边框"——
    /// 实色黄底会把正要看的画面盖掉），所以边界全靠这条描边认出来，描边跟着当前字色走，
    /// 在深色截图与浅色截图上都看得出来。
    /// </summary>
    private void ApplyEditorAccent()
    {
        // 新写的一行跟着当前调色板走；改旧字时用那条字自己的颜色（换调色板不该改旧字的颜色）
        var brush = new SolidColorBrush(ToColor(_editingIndex is null ? ColourBgra : _editorColourBgra));
        TextEditor.Foreground = brush;
        TextEditorHost.BorderBrush = brush;
    }

    /// <summary>
    /// 输入框里的键：<b>Enter 换行</b>（交给 TextBox 自己插行，不再当"落笔"），Esc 结束编辑并保住已打的字。
    /// <para>Enter 以前是提交，用户想分两行就只能写完一条再点别处开第二条——真机期望是"编辑过程中可以通过
    /// enter 进行文字换行继续编辑"。提交出口现在是：点选区别处、切工具、点动作按钮、或 Esc。</para>
    /// </summary>
    private void TextEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;                 // Esc 在输入框里＝结束这一行字的编辑，不是取消整场截图
        EndTextEditing(commit: true);
    }

    /// <summary>
    /// 结束就地输入。<paramref name="commit"/> 为 false 只用于"换选区"（整屏都要作废的那条路）——
    /// <b>Esc 是落笔不是丢弃</b>：用户要的是"退出文字编辑去选别的工具"，不是"把我打的字变没"。
    /// <para>改旧字那一路只动文本、不动变换（位置/字号/角度都留着），并且删空了就是删掉那条——
    /// 留一条"没有字"的文字标注既画不出东西又占着选中位，只会让人以为程序卡了一条。</para>
    /// </summary>
    private void EndTextEditing(bool commit)
    {
        if (!_editingText) return;
        _editingText = false;
        TextEditorHost.Visibility = Visibility.Collapsed;
        var index = _editingIndex;
        _editingIndex = null;
        // 开框时被从画面里藏掉的那一条（见 Rebake）要在这一刻回来，否则它就永久隐身：一条画不出又点不着的字。
        // 只在下标非空时才需要重烤——新写一行的那一路什么都没藏，白烤一张整幅选区不值。
        if (!commit)
        {
            if (index is not null) Rebake();
            return;
        }
        var text = TextEditor.Text.TrimEnd();

        if (index is { } existing)
        {
            if (existing >= _history.Count) return;       // 编辑期间历史被动过（撤销/删除）：不猜下标，宁可不改
            if (text.Length == 0) { _selected = existing; DeleteSelected(); return; }
            _history.ReplaceAt(existing, _history.Marks[existing] with { Text = text });
            Rebake();
            DrawSelectionHandles();
            return;
        }
        if (text.Length == 0) return;
        _history.Add(new Annotation(AnnotationTool.Text, new[] { _textAnchor }, ColourBgra, ThicknessForTool)
        {
            Text = text,
            FontHeight = Annotation.DefaultFontHeight,
        });   // 轴由模型按字块中心现算（Annotation.Origin），界面不自己钉变换轴
        _selected = _history.Count - 1;   // 打完字紧接着就是"挪个位置/改个字号"：那一条直接在手边
        Rebake();
        DrawSelectionHandles();
    }

    // ────────── 编辑历史：撤销 / 重做 / 清空 ──────────
    // 三个动作都只是移动历史指针（状态快照在 AnnotationHistory 里），
    // 所以"清空了又撤销回来"和"撤销两步再重做"不需要任何额外代码，也不会残留半条。

    /// <summary>选区阶段的方向键：plain＝平移 1px，Shift＝缩放对应边 1px（物理像素）。</summary>
    private void NudgeRegion(VirtualKey key, bool resize)
    {
        if (_selection is not { } sel) return;
        IntRect next;
        if (resize)
        {
            next = key switch
            {
                VirtualKey.Left => sel with { Width = Math.Max(1, sel.Width - 1) },
                VirtualKey.Right => sel with { Width = sel.Width + 1 },
                VirtualKey.Up => sel with { Height = Math.Max(1, sel.Height - 1) },
                VirtualKey.Down => sel with { Height = sel.Height + 1 },
                _ => sel,
            };
        }
        else
        {
            next = key switch
            {
                VirtualKey.Left => sel with { X = sel.X - 1 },
                VirtualKey.Right => sel with { X = sel.X + 1 },
                VirtualKey.Up => sel with { Y = sel.Y - 1 },
                VirtualKey.Down => sel with { Y = sel.Y + 1 },
                _ => sel,
            };
            // 夹回本屏：方向键把选区推到屏外再按 Enter，裁剪护栏会拿一句错误把人挡住
            next = CaptureGeometry.Intersect(next, _monitor) ?? next;
        }
        _selection = next;
        DrawSelection(next);
    }

    /// <summary>标注阶段的方向键：移动选中的那条标注（plain 1px，Shift 10px）。</summary>
    private void NudgeSelectedMark(VirtualKey key, bool big)
    {
        if (_selected is not int idx || idx < 0 || idx >= _history.Count) return;
        var mark = _history.Marks[idx];
        // 步长按<b>屏幕</b>像素定再换算进底图：贴图缩到 0.2× 时 1 个底图像素只有 0.2 个屏幕像素，
        // 按一下方向键几乎看不见动（Snipaste 的手感是"按一下动一格"）；放大 5× 时反过来会窜格。
        var step = SlopInSource(big ? 10 : 1);
        var dx = key switch
        {
            VirtualKey.Left => -step,
            VirtualKey.Right => step,
            _ => 0,
        };
        var dy = key switch
        {
            VirtualKey.Up => -step,
            VirtualKey.Down => step,
            _ => 0,
        };
        if (dx == 0 && dy == 0) return;
        _history.ReplaceAt(idx, mark.MovedBy(dx, dy));
        Rebake();
    }

    private void Undo()
    {
        if (!_history.Undo()) return;
        DropSelection();                      // 下标随历史移动：旧框指着的是完全另一条标注
        Rebake();
    }

    private void Redo()
    {
        if (!_history.Redo()) return;
        DropSelection();
        Rebake();
    }

    /// <summary>
    /// 输入框<b>不</b>因失去焦点而结束——这一条就是真机反馈"必须按住鼠标才在输入、松手就算编辑完"的成因：
    /// 按下那一下把焦点给了输入框，松开时焦点回到遮罩那一层，原先挂在 LostFocus 上的落笔于是把这一行当场结掉。
    /// 落笔的时机改由用户看得见的那几个动作明确决定：Enter、点画布别处、切工具、点动作按钮（各自都会调
    /// <see cref="EndTextEditing"/>），Esc 只丢掉这一行。
    /// </summary>
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        Redo();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        _history.Clear();
        DropSelection();
        Rebake();
    }

    // ────────── 绘制 ──────────

    private void ClearSelection()
    {
        SelRect.Visibility = Visibility.Collapsed;
        foreach (var dim in new[] { DimTop, DimLeft, DimRight, DimBottom })
            dim.Visibility = Visibility.Collapsed;
        SizeChip.Visibility = Visibility.Collapsed;
        HintChip.Visibility = Visibility.Visible;
    }

    private void DrawSelection(IntRect selection)
    {
        if (selection.Width <= 0 || selection.Height <= 0)
        {
            ClearSelection();
            return;
        }
        var (x, y, w, h) = ToDip(selection);
        var screenWidth = _monitor.Width / _scale;
        var screenHeight = _monitor.Height / _scale;

        SelRect.Visibility = Visibility.Visible;
        PlaceOnCanvas(SelRect, x, y, w, h);

        PlaceOnCanvas(DimTop, 0, 0, screenWidth, y);
        PlaceOnCanvas(DimBottom, 0, y + h, screenWidth, Math.Max(0, screenHeight - y - h));
        PlaceOnCanvas(DimLeft, 0, y, x, h);
        PlaceOnCanvas(DimRight, x + w, y, Math.Max(0, screenWidth - x - w), h);

        SizeChip.Visibility = Visibility.Visible;
        SizeText.Text = CaptureGeometry.FormatSize(selection.Width, selection.Height);
        PlaceByMargin(SizeChip, Math.Max(0, x), Math.Max(0, y - 22));
    }

    /// <summary>
    /// 把工具条摆到选区下方（放不下就摆到上方），左右都夹进本屏。
    /// <para>尺寸按<b>量出来的</b> ActualWidth/Height 算：三行都是代码生成的，按写死的数字摆
    /// 一旦加个工具就会压住选区或掉到屏外。</para>
    /// </summary>
    private void PositionBar(IntRect selection)
    {
        ActionBar.UpdateLayout();
        var barWidth = ActionBar.ActualWidth > 0 ? ActionBar.ActualWidth : Bar_fallback_width;
        var barHeight = ActionBar.ActualHeight > 0 ? ActionBar.ActualHeight : Bar_fallback_height;
        var (x, y, w, h) = ToDip(selection);
        // 贴图态这条只能压在画面上（窗口就是那张图）。这里<b>不自己算左边界</b>：直接靠右上对齐，
        // 可用宽度量布局真值（Root.ActualWidth）而不是"物理宽 ÷ 缩放"——后者在建窗那一刻可能还没
        // 拿到本窗真正的 DPI，算出来的边界会比窗口宽，结果整条被推到画面外，只剩右上角露一点，
        // 真机反馈就是"要不停放大贴图，菜单才一点点挪出来"。
        if (_pinned)
        {
            var available = Root.ActualWidth > 0 ? Root.ActualWidth : _monitor.Width / _scale;
            // 小贴图常常没有一条工具条宽：整条缩到塞得进画面（贴着右上角往里收），
            // 而不是把右边那几颗（复制/存图/识字/穿透/关闭）裁掉——裁掉的正好是要用的。
            var fit = barWidth > 0 ? Math.Clamp((available - 8) / barWidth, 0.5, 1.0) : 1.0;
            ActionBar.HorizontalAlignment = HorizontalAlignment.Right;
            ActionBar.VerticalAlignment = VerticalAlignment.Top;
            ActionBar.Margin = new Thickness(0, 0, 4, 0);
            ActionBar.RenderTransformOrigin = new Windows.Foundation.Point(1, 0);
            ActionBar.RenderTransform = new ScaleTransform { ScaleX = fit, ScaleY = fit };
            return;
        }
        var screenWidth = _monitor.Width / _scale;
        var screenHeight = _monitor.Height / _scale;
        var left = Math.Clamp(x + w - barWidth, 4, Math.Max(4, screenWidth - barWidth - 4));
        var below = y + h + 6 + barHeight <= screenHeight;
        PlaceByMargin(ActionBar, left, below ? y + h + 6 : Math.Max(4, y - barHeight - 6));
    }

    private static void PlaceOnCanvas(FrameworkElement element, double x, double y, double w, double h)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, w);
        element.Height = Math.Max(0, h);
        element.Visibility = w <= 0 || h <= 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Grid 里的浮层元素：靠 Left/Top 对齐 + Margin 定位（Canvas 的附加属性在这里不起作用）。</summary>
    private static void PlaceByMargin(FrameworkElement element, double x, double y)
        => element.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);

    private void ShowError(string reason)
    {
        ErrorText.Text = reason;
        ErrorChip.Visibility = Visibility.Visible;
    }

    // ────────── 动作 ──────────

    private void Copy_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Copy);

    private void Save_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Save);

    private void Pin_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Pin);

    private void Ocr_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Ocr);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_pinned) Close();          // 贴图态的 ✕＝关闭这张图（与 Esc 同一件事）
        else Settle(null);
    }

    private void Through_Click(object sender, RoutedEventArgs e) => PinManager.ToggleClickThrough();

    private enum CommitAction { Copy, Save, Pin, Ocr }

    /// <summary>
    /// 提交这一屏的选区：复制、存图、钉住，或认字并复制文字。四个落点交的都是<b>带上标注的那一份画面</b>。
    /// 先算好像素再 Settle：服务收到结果就会关掉所有遮罩窗（包括本窗），
    /// 反过来先干活会让用户在裁图期间还被困在暗幕里。
    /// </summary>
    private void Commit(CommitAction action)
    {
        // 没按 Enter 就点动作按钮：刚打的那行字要跟着图一起走，而不是被丢掉（四条落点同一条出口）。
        EndTextEditing(commit: true);
        if (_selection is not { } selection)
        {
            Settle(null);
            return;
        }
        if (!_pinned && CaptureGeometry.SelectionProblem(selection, _monitor) is { } reason)
        {
            ShowError(reason);
            return;
        }
        if (FinalPixels() is not { } final) return;       // 原因已经在里面报过了，这里只负责不再往下走
        if (!_pinned) Settle(selection);
        // 贴图态交出去的也是"当前这一份"：贴图显示的就是合成预览，所见即所存这条不变。
        var category = _pinned ? "贴图" : "截图";
        switch (action)
        {
            case CommitAction.Copy:
                _ = ScreenshotService.CopyPixelsAsync(final.Pixels, final.Width, final.Height, category);
                break;
            case CommitAction.Save:
                _ = ScreenshotService.SavePixelsAsync(final.Pixels, final.Width, final.Height, category);
                break;
            case CommitAction.Ocr:
                _ = OcrService.CopyTextFromPixelsAsync(final.Pixels, final.Width, final.Height,
                    _pinned ? "贴图识字" : "截图识字");
                break;
            default: ScreenshotService.PinPixels(final.Pixels, final.Width, final.Height, selection); break;
        }
    }

    /// <summary>
    /// 要交出去的那份画面：有底图就在它上面重烤一次标注（连"一条都没画"也走这条路，结果就是原样），
    /// 拿不到底图时退回"直接从这一帧里裁"。两条路都报同样的失败原因，不静默少一张图。
    /// <para>尺寸取 <see cref="_contentWidth"/>／<see cref="_contentHeight"/>（底图自己的尺寸），
    /// <b>不取选区的显示尺寸</b>：贴图在 2.5× 时选区矩形是显示尺寸，按它渲染会得到一张放大的糊图。</para>
    /// </summary>
    private (byte[] Pixels, int Width, int Height)? FinalPixels()
    {
        if (_base is { } basePixels)
        {
            try
            {
                return (AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, _history.Marks),
                    _contentWidth, _contentHeight);
            }
            catch (Exception ex)
            {
                StarLog.Error("[CaptureOverlay] 交图前合成标注失败", ex);
                ShowError("标注没能合成：" + ex.Message);
                return null;
            }
        }
        return _selection is { } selection && _frame is { } frame
            ? ScreenshotService.TryCrop(frame, selection)
            : null;
    }

    // ────────── Snipaste 式像素放大镜 ──────────

    private const int MagnifierLens = 15;   // 放大 15×15 源像素
    private const int MagnifierZoom = 10;   // 每源像素＝10 物理像素
    private static readonly byte[] GridLineColor = { 0x00, 0x00, 0x00, 0x66 };
    private static readonly byte[] CrossLineColor = { 0x30, 0x3B, 0xFF, 0xFF };

    private void UpdateMagnifier(PixelPoint physical)
    {
        if (_frame is null) return;
        var size = MagnifierLens * MagnifierZoom;
        if (_magnifierBitmap is null)
        {
            _magnifierBitmap = new WriteableBitmap(size, size);
            MagnifierImage.Source = _magnifierBitmap;
            // 位图按物理像素画，显示尺寸换算成 DIP，保证"每源像素＝10 物理像素"
            MagnifierImage.Width = size / _scale;
            MagnifierImage.Height = size / _scale;
        }

        var cx = physical.X - _frame.Bounds.X;
        var cy = physical.Y - _frame.Bounds.Y;
        var startX = cx - MagnifierLens / 2;
        var startY = cy - MagnifierLens / 2;
        var magnified = new byte[size * size * 4];
        for (var my = 0; my < MagnifierLens; my++)
        {
            for (var mx = 0; mx < MagnifierLens; mx++)
            {
                var sx = startX + mx;
                var sy = startY + my;
                byte b = 0, g = 0, r = 0;
                if (sx >= 0 && sy >= 0 && sx < _frame.Width && sy < _frame.Height)
                {
                    var sp = (sy * _frame.Width + sx) * 4;
                    b = _frame.Bgra[sp];
                    g = _frame.Bgra[sp + 1];
                    r = _frame.Bgra[sp + 2];
                }
                for (var by = 0; by < MagnifierZoom; by++)
                {
                    var dy = my * MagnifierZoom + by;
                    for (var bx = 0; bx < MagnifierZoom; bx++)
                    {
                        var dx = mx * MagnifierZoom + bx;
                        var dp = (dy * size + dx) * 4;
                        magnified[dp] = b;
                        magnified[dp + 1] = g;
                        magnified[dp + 2] = r;
                        magnified[dp + 3] = 255;
                    }
                }
            }
        }

        // 像素网格（每格一条暗线）＋中心红色十字
        for (var i = 0; i <= size; i += MagnifierZoom)
        {
            PaintHLine(magnified, size, i, GridLineColor);
            PaintVLine(magnified, size, i, GridLineColor);
        }
        var center = size / 2;
        PaintHLine(magnified, size, center, CrossLineColor);
        PaintVLine(magnified, size, center, CrossLineColor);

        using (var stream = _magnifierBitmap.PixelBuffer.AsStream())
            stream.Write(magnified, 0, magnified.Length);
        _magnifierBitmap.Invalidate();

        if (cx >= 0 && cy >= 0 && cx < _frame.Width && cy < _frame.Height)
        {
            var cp = (cy * _frame.Width + cx) * 4;
            MagnifierRgb.Text = $"RGB: {_frame.Bgra[cp + 2]},{_frame.Bgra[cp + 1]},{_frame.Bgra[cp]}";
        }
        MagnifierPos.Text = $"X: {physical.X} Y: {physical.Y}";

        PositionMagnifier(physical);
        Magnifier.Visibility = Visibility.Visible;
    }

    private void HideMagnifier() => Magnifier.Visibility = Visibility.Collapsed;

    private void PositionMagnifier(PixelPoint physical)
    {
        var win = WindowInterop.GetWindowRect(this);
        const int lensW = 162;
        const int lensH = 196;
        const int gap = 18;
        var px = physical.X - win.X + gap;
        var py = physical.Y - win.Y + gap;
        // 靠近右/下边缘时翻到光标的左/上方
        if (px + lensW > win.Width) px = physical.X - win.X - lensW - 6;
        if (py + lensH > win.Height) py = physical.Y - win.Y - lensH - 6;
        MagnifierTransform.X = Math.Max(0, px) / _scale;
        MagnifierTransform.Y = Math.Max(0, py) / _scale;
    }

    private static void PaintHLine(byte[] bgra, int width, int y, byte[] color)
    {
        if (y < 0 || y >= width) return;
        for (var x = 0; x < width; x++)
        {
            var p = (y * width + x) * 4;
            bgra[p] = color[0];
            bgra[p + 1] = color[1];
            bgra[p + 2] = color[2];
            bgra[p + 3] = color[3];
        }
    }

    private static void PaintVLine(byte[] bgra, int width, int x, byte[] color)
    {
        if (x < 0 || x >= width) return;
        for (var y = 0; y < width; y++)
        {
            var p = (y * width + x) * 4;
            bgra[p] = color[0];
            bgra[p + 1] = color[1];
            bgra[p + 2] = color[2];
            bgra[p + 3] = color[3];
        }
    }

    // ────────── 窗口自动检测（按住 Ctrl 临时关闭） ──────────

    private void UpdateDetected(PixelPoint physical)
    {
        foreach (var cand in _windowCandidates)
        {
            if (ContainsPoint(cand, physical))
            {
                if (_detected is { } d && d == cand) return;
                _detected = cand;
                DrawSelection(cand);
                return;
            }
        }
        ClearDetected();
    }

    private void ClearDetected()
    {
        if (_detected is null) return;
        _detected = null;
        ClearSelection();
    }

    private static bool ContainsPoint(IntRect rect, PixelPoint p)
        => p.X >= rect.X && p.X < rect.Right && p.Y >= rect.Y && p.Y < rect.Bottom;

    private List<IntRect> CollectWindowCandidates()
    {
        // 窗口检测是<b>尽力而为</b>：它只服务于"点一下选窗口"这条捷径，任何失败（枚举失败、
        // DWM 不可用、原生调用抛异常）都只能降级成"没有候选、退回手动拖框"，绝不能让截图
        // 会话在构造函数里夭折——SP 那次一个写错的 P/Invoke 入口名让每次 F1 都直接"截图失败"，
        // 就是缺这层兜底。
        try
        {
            var list = new List<IntRect>();
            var currentProcess = Environment.ProcessId;
            WindowInterop.EnumWindows((hwnd, _) =>
            {
                if (!WindowInterop.IsWindowVisible(hwnd)) return true;
                WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == currentProcess) return true;     // 不把我们自己的窗口当候选
                var r = WindowInterop.GetExtendedFrameBounds(hwnd);
                var rect = new IntRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (rect.Width < 24 || rect.Height < 24) return true;
                if (CaptureGeometry.Intersect(rect, _monitor) is { } clipped && !clipped.IsEmpty)
                    list.Add(rect);
                return true;
            }, IntPtr.Zero);
            // 小窗排前：小窗叠在大窗上时，点小窗不该被后面的大窗抢先
            list.Sort((a, b) => (a.Width * a.Height).CompareTo(b.Width * b.Height));
            return list;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[CaptureOverlay] 窗口候选收集失败，退回手动拖框：{ex.Message}");
            return new List<IntRect>();
        }
    }

    // ────────── 序号标注 ──────────

    private void PlaceNumber(PixelPoint local)
    {
        // 编号取"现存最大号 + 1"而不是自增计数器：撤销/删除最大那颗之后再点，Snipaste 的手感是
        // 接着已有的号往下走（画 1,2,3 → 撤销掉 3 → 再点仍是 3），全局计数器会让它跳成 4。
        var next = 1;
        foreach (var existing in _history.Marks)
            if (existing.Tool == AnnotationTool.Number) next = Math.Max(next, existing.Number + 1);
        var mark = new Annotation(AnnotationTool.Number, new[] { local }, ColourBgra, 2)
        {
            Number = next,
        };
        if (mark.Problem() is not null) { return; }
        EndTextEditing(commit: true);
        DropSelection();
        _history.Add(mark);
        Rebake();
    }

    // ────────── 橡皮擦 ──────────

    private void BeginEraseStroke(PixelPoint local, Pointer pointer)
    {
        EndTextEditing(commit: true);
        DropSelection();
        _erasing = true;
        _eraseRemoved.Clear();
        _history.BeginErase();
        Root.CapturePointer(pointer);
        EraseTo(local);
    }

    private void EraseTo(PixelPoint local)
    {
        var changed = false;
        while (true)
        {
            var hit = AnnotationPainter.HitTest(_history.Marks, local, SlopInSource(SelectionSlop));
            if (hit is not int index) break;
            _eraseRemoved.Add(_history.Marks[index]);
            _history.ApplyErase(_eraseRemoved);
            changed = true;
        }
        if (changed) Rebake();
    }

    private void EndEraseStroke()
    {
        _erasing = false;
        _history.EndErase();
        _eraseRemoved.Clear();
        Rebake();
    }

    // ────────── 贴图旋转 / 翻转 / 透明度 / 右键菜单 ──────────

    /// <summary>
    /// 90° 离散旋转（左/右）：源尺寸换轴的<b>精确像素重排</b>——没有插值糊化、没有填黑四角、
    /// 不需要任何窗口区域裁切。自由角度旋转（右键拖动）在 WinUI 上必然带着"四角填黑 +
    /// 区域裁切"两件套，真机反馈就是大面积黑背景；且外接矩形随角度变大，
    /// 过度旋转会把贴图顶出屏幕。按用户裁决只留四种姿态：左转 90° / 右转 90° / 水平翻转 / 垂直翻转。
    /// <para>先把当前标注合成进像素再转（旋转带着标注一起走，之后它们就是像素的一部分）；
    /// 显示尺寸＝新源尺寸 × 当前倍率，位置按 <see cref="CaptureGeometry.PinOrigin"/> 收边——
    /// 转完仍要整块可见，不许跑出屏幕。</para>
    /// </summary>
    private void BakeQuarterTurn(bool clockwise)
    {
        Rebake();
        if (_composed is not { } composed) return;
        var rotated = BitmapTransform.Rotate90(composed, _contentWidth, _contentHeight, clockwise);
        var (w, h) = CaptureGeometry.PinPixelSize(rotated.Width, rotated.Height, _zoom);
        var current = WindowInterop.GetWindowRect(this);
        var (x, y) = CaptureGeometry.PinOrigin(current.X, current.Y, w, h, WorkArea());
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero, x, y, w, h,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
        SetMonitor(new IntRect(x, y, w, h));
        BeginEditingExisting(rotated.Pixels, rotated.Width, rotated.Height);
        RefreshScaleIfChanged();
        SyncBadge();
    }

    private void BakeFlip(bool horizontal)
    {
        Rebake();
        if (_composed is not { } composed) return;
        var flipped = BitmapTransform.Flip(composed, _contentWidth, _contentHeight, horizontal);
        BeginEditingExisting(flipped, _contentWidth, _contentHeight);
        SyncBadge();
    }

    private void SetOpacity(double opacity)
    {
        _opacity = opacity;
        WindowInterop.SetWindowOpacity(this, opacity);
        SizeChip.Visibility = Visibility.Visible;
        SizeText.Text = $"不透明度 {opacity * 100:0}%";
    }

    private void ShowPinMenu(Point localDip)
    {
        var menu = new MenuFlyout();
        AddMenuItem(menu, "复制（Ctrl+C）", () => Commit(CommitAction.Copy));
        AddMenuItem(menu, "保存（Ctrl+S）", () => Commit(CommitAction.Save));
        AddMenuItem(menu, "识字", () => Commit(CommitAction.Ocr));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "缩放重置为 100%", ResetZoomTo1);
        var opacityMenu = new MenuFlyoutSubItem { Text = "不透明度" };
        foreach (var v in new[] { 1.0, 0.75, 0.5, 0.25 })
        {
            var value = v;
            opacityMenu.Items.Add(NewMenuItem($"{v * 100:0}%", () => SetOpacity(value)));
        }
        menu.Items.Add(opacityMenu);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "向左旋转 90°", () => BakeQuarterTurn(clockwise: false));
        AddMenuItem(menu, "向右旋转 90°", () => BakeQuarterTurn(clockwise: true));
        AddMenuItem(menu, "水平翻转", () => BakeFlip(horizontal: true));
        AddMenuItem(menu, "垂直翻转", () => BakeFlip(horizontal: false));
        AddMenuItem(menu, _clickThrough ? "取消鼠标穿透" : "鼠标穿透",
            () => ApplyClickThrough(!_clickThrough));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "关闭（Esc）", Close);
        menu.ShowAt(Root, localDip);
    }

    private static MenuFlyoutItem NewMenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static void AddMenuItem(MenuFlyout menu, string text, Action action)
        => menu.Items.Add(NewMenuItem(text, action));

    private void ResetZoomTo1()
    {
        if (Math.Abs(_zoom - 1d) < 0.0001) { SyncBadge(); return; }
        _zoom = 1d;
        _sourceScale = 1d;
        ResizePinAnchoringTopLeft();
        SyncBadge();
    }

}
