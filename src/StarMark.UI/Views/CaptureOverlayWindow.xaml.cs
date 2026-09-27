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
/// 截图遮罩窗：显示"按下热键那一刻"的整屏帧，让用户框选（悬停亮出候选窗口、点一下即预填），
/// <b>框选阶段就能画标注</b>——可越出选区、可随时改框、重拖一块新选区也不清笔迹（标注跟屏走）；
/// 点「贴」才把选区那块（连同标注一起）钉到桌面上，钉住后还能继续二次编辑
/// （批次 PU 的 Snipaste 两阶段口径：框选阶段＝一切可调，贴图阶段＝只有贴图内的编辑算数）。
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
/// 标注画在整帧底图的坐标系里（截图态原点＝屏幕左上角），每落一条笔就由 <see cref="AnnotationPainter"/>
/// 把整帧重烤一遍推上屏幕；提交时从合成图里<b>裁出选区</b>，越出选区的部分自然被裁掉、选区内天然不含压暗。
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
    private IntRect? _candidate;                     // 按下去那一刻所在的候选：点一下＝确认它，拖动＝自定义框选

    /// <summary>
    /// 截图态有没有"确认过选区"（批次 PU 两阶段模型的分界线）。确认之后工具条才出现，
    /// 标注跟屏走、可以越出选区；识别（悬停建议）阶段<b>不出现菜单栏</b>，重拖选区也不清标注。
    /// 贴图态建窗即已进入编辑，恒为 true。
    /// </summary>
    private bool _annotating;

    /// <summary>悬停光标的形状缓存：PointerMoved 每帧都会走到这里，形状没变就不重建 InputSystemCursor。</summary>
    private InputSystemCursorShape? _lastCursor;
    /// <summary>
    /// 「帧＋标注、<b>无压暗</b>」的显示位图（截图态专用）：改框/重拖期间把内容源切到它、
    /// 压暗交给 XAML 四块实时跟随——拖动全程零整帧重烤（真机反馈"拖框明显卡顿"的成因
    /// 是每 16ms 一次的 4K 整帧烤＋上传阻塞了 UI 线程，连 SelRect 一起卡），松手再整帧烤准切回。
    /// </summary>
    private WriteableBitmap? _flatPreview;

    /// <summary>这一按是不是"第一次选中脚下那条"（之前没有选中）。
    /// 文字的"点一下＝选中、再点一下＝进编辑"靠它区分：首次选中的那一按松手不许直接弹输入框。</summary>
    private bool _grabFresh;

    /// <summary>拖动进行中（改框或确认后的重拖）：压暗此时由 XAML 实时跟随，合成图切到无压暗的平面缓冲。</summary>
    private bool IsFrameDragging =>
        _adjust != CaptureGeometry.SelectionEdge.None || (_awaitingRelease && _annotating);
    /// <summary>拖动已有标注时上一次上传预览的时刻（毫秒）。整帧缓冲每帧上传太重，节流到 ~60fps。</summary>
    private long _lastDragPaint;

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
    /// 未选工具时这一按是用来改框的（十字箭头，拖动/改大小在批次 PK-2 接上）。
    /// 每支工具<b>点一次即选中并弹出颜色/粗细浮层</b>（批次 PV：换颜色永远一次点击就到），
    /// "收笔回到改框那一态"统一走 Esc。</para>
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
        // 登记＋置顶＋归位一次做完（LayerDirector 是全机唯一的层序写入点）：这张遮罩是 Sheet，
        // 名册里有了它，玻璃才知道自己要退到它下面去。
        LayerDirector.ShowAt(Role, WindowInterop.GetHwnd(this), _monitor);

        if (BuildMonitorBitmap() is not { } shot)
        {
            ShowError("这一帧没能铺到屏幕上（内存不足或像素缓冲尺寸不符）——按 Esc 结束");
            return;
        }
        Shot.Source = shot.Bitmap;
        // 底图＝整帧、内容尺寸＝整屏（批次 PU 两阶段模型）：标注画在屏幕坐标系里、可以越出选区，
        // 提交时才从"帧＋标注"的合成图里裁出选区那块。选区只是"裁到哪里"的记号，不再是渲染的边界。
        _base = shot.Pixels;
        _contentWidth = _monitor.Width;
        _contentHeight = _monitor.Height;

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
        // 贴图是名册里的 Pin 角色：绘制态玻璃要压到它之上（能在贴图上圈注），
        // 穿透态又得让到它之下（贴图可点）——这两臂都由 LayerDirector 按会话态翻，这里只管登记。
        LayerDirector.ShowAt(SurfaceRole.Pin, hwnd, _monitor);

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
        // 文字标注的 ✕（用户口径："编辑框右上角为X号，可以点击删除该文字编辑框"）。
        // Handled 必须吃掉：否则这一按会冒到 Root 再落一笔/挪框，删除就变成了"删完又画一条"。
        TextDeleteButton.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            DeleteSelected();
        };
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
        Closed += (_, _) =>
        {
            // 名册里忘掉它：留一个已销毁的句柄当锚点，玻璃每次定序都会"锚点不可用"而什么都不做。
            LayerDirector.Unregister(WindowInterop.GetHwnd(this));
            Settle(null);
        };
    }

    /// <summary>这扇窗在 Z 序名册里的角色：截图遮罩＝Sheet（盖住一切自家他窗），贴图＝Pin。</summary>
    private SurfaceRole Role => _pinned ? SurfaceRole.Pin : SurfaceRole.Sheet;

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

    private (WriteableBitmap Bitmap, byte[] Pixels)? BuildMonitorBitmap()
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
            return (bitmap, pixels);
        }
        catch (Exception ex)
        {
            StarLog.Error($"[CaptureOverlay] 铺帧失败（{DeviceName}）", ex);
            return null;
        }
    }

}
