#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Health;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——手式这一头：拖动、多窗联动移动、边缘吸附，以及各方向缩放手柄（含 ResizeGrip 那个私有控件）。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetWindow
{

    // ───────────────────────── 拖动 + 吸附（DeskBox CoordinatedMove 同款物理像素方案）─────────────────────────

    private void DragBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(DragBar).Properties.IsRightButtonPressed) return;
        if (FindAncestorButton(e.OriginalSource as DependencyObject)) return;
        if (_peeking) CollapsePeek();   // 悬停预览期间按下即先收回胶囊，再按胶囊拖动

        WindowInterop.GetCursorPos(out _gestureStart);
        _gestureStartRect = WindowInterop.GetWindowRect(this);

        // Ctrl+拖动 → 同屏所有可见组件一起移动（DeskBox CoordinatedMove）。
        // 修饰键状态只在**按下瞬间**采样一次：拖动过程中松/按 Ctrl 都不改变参与者，
        // 否则会出现「跟到一半不跟了」的半截跟随。
        _coordinated = IsCtrlDown();
        BeginCoordinatedMove();

        BeginSnapSession();
        RaiseTransient();
        _dragging = true;
        DragBar.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DragBar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        WindowInterop.GetCursorPos(out var pt);
        var proposed = new RectInt32(
            _gestureStartRect.X + pt.X - _gestureStart.X,
            _gestureStartRect.Y + pt.Y - _gestureStart.Y,
            _gestureStartRect.Width, _gestureStartRect.Height);

        var result = WidgetSnapCalculator.ResolveMove(
            proposed,
            _snapTargets,
            _snapWorkArea,
            _snapSpacing,
            _snapEngage,
            _snapRelease,
            _stickyHorizontal,
            _stickyVertical);
        _stickyHorizontal = result.HorizontalMatch;
        _stickyVertical = result.VerticalMatch;
        AppWindow.MoveAndResize(result.Bounds);

        ApplyCoordinatedMove(pt.X - _gestureStart.X, pt.Y - _gestureStart.Y);
        e.Handled = true;
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// 招募协同移动参与者：同一显示器上的其它可见组件。
    /// 用「显示器设备名」判定同屏（比比较矩形相交更准），
    /// 眼睛坐标为 MONITOR_DEFAULTTONEAREST 的 hwnd 判定与本组件一致。
    /// </summary>
    private void BeginCoordinatedMove()
    {
        ClearCoordinatedMove();
        if (!_coordinated) return;

        try
        {
            var selfMonitor = WindowInterop.GetMonitorForWindow(WindowInterop.GetHwnd(this)).Device;
            foreach (var w in _manager.VisibleWindowsExcept(_instanceId))
            {
                var device = WindowInterop.GetMonitorForWindow(WindowInterop.GetHwnd(w)).Device;
                if (device == selfMonitor) _coordPeers.Add((w, w.CurrentRect));
            }
        }
        catch (Exception ex)
        {
            // 招募失败不该拖累主手势：退化成普通单窗口拖动
            StarLog.Error("Ctrl+拖动协同移动招募参与者失败", ex);
            ClearCoordinatedMove();
        }
    }

    private void ApplyCoordinatedMove(int dx, int dy)
    {
        if (!_coordinated || _coordPeers.Count == 0) return;
        foreach (var (w, start) in _coordPeers)
        {
            try { w.MoveByCoordinated(dx, dy, start); }
            catch (Exception ex) { StarLog.Error("协同移动跟随失败", ex); }
        }
    }

    /// <summary>
    /// 拖动收尾的持久化：自己 + 协同移动的每个参与者，<b>一次读档、最多一次落盘</b>。
    /// 逐窗 PersistBounds 的话，一次 Ctrl+拖动要写 N+1 趟整档（N＝同屏参与组件数），
    /// 而这正是"多个组件一起摆位"的常用手势。单个窗口写失败不该拖住其它窗口，所以逐个兜住。
    /// </summary>
    private void PersistPositionsAfterDrag()
    {
        var targets = new List<WidgetWindow> { this };
        targets.AddRange(_coordPeers.Select(p => p.Window));
        _storage.Mutate(data =>
        {
            var wrote = false;
            foreach (var w in targets)
            {
                try { wrote |= w.WriteBoundsInto(data); }
                catch (Exception ex) { StarLog.Error("拖动后持久化组件位置失败", ex); }
            }
            return wrote;
        });
        ClearCoordinatedMove();
    }

    private void ClearCoordinatedMove()
    {
        _coordPeers.Clear();
        _coordinated = false;
    }

    private void DragBar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        EndSnapSession();
        try { DragBar.ReleasePointerCapture(e.Pointer); } catch { }
        PersistPositionsAfterDrag();
        e.Handled = true;
    }

    /// <summary>
    /// 开始一次吸附会话：一次性采集候选目标并按 DPI 换算阈值。
    /// 对应 DeskBox ResizeGuideOverlayService.BeginDrag 的会话准备。
    /// </summary>
    private void BeginSnapSession()
    {
        _stickyHorizontal = null;
        _stickyVertical = null;

        // 磁吸总开关（设置页「组件 → 边缘磁吸」）：关闭后用户在桌面自由摆位，不做任何自动贴合。
        if (!WidgetAppearance.SnapEnabled())
        {
            _snapTargets = Array.Empty<WidgetSnapTarget>();
            _snapWorkArea = null;
            return;
        }

        var scale = WindowInterop.GetScale(this);
        var others = _manager.GetOtherBounds(_instanceId);
        var targets = new WidgetSnapTarget[others.Count];
        for (int i = 0; i < others.Count; i++) targets[i] = new WidgetSnapTarget(others[i]);

        _snapTargets = targets;
        _snapWorkArea = WidgetSnapCalculator.InsetWorkArea(
            WindowInterop.GetWorkArea(this),
            (int)Math.Round(WidgetSnapCalculator.DefaultScreenMargin * scale));
        _snapSpacing = (int)Math.Round(WidgetAppearance.SnapSpacing() * scale);
        _snapEngage = Math.Max(1, (int)Math.Round(WidgetAppearance.SnapEngageThreshold() * scale));
        // 迟滞：release 恒比 engage 宽一档（沿用默认 24→32 的 +8px 关系），避免拖动时抖动。
        _snapRelease = Math.Max(
            _snapEngage,
            (int)Math.Round((WidgetAppearance.SnapEngageThreshold()
                + (WidgetSnapCalculator.DefaultReleaseThreshold - WidgetSnapCalculator.DefaultEngageThreshold)) * scale));
        _stickyHorizontal = null;
        _stickyVertical = null;
    }

    private void EndSnapSession()
    {
        _snapTargets = Array.Empty<WidgetSnapTarget>();
        _snapWorkArea = null;
        _stickyHorizontal = null;
        _stickyVertical = null;
    }

    // ───────────────────────── 右下角缩放 ─────────────────────────

    // ───────────────────── 边缘/四角缩放（8 向，替代原右下角手柄）─────────────────────────

    /// <summary>沿窗口四边 + 四角布置透明缩放 grip，方向以 n/s/e/w 组合标记在 Tag 上。</summary>
    private void AddResizeGrips()
    {
        if (RootBorder.Child is not Grid rootGrid) return;

        // 层级策略（避免「右上角关闭按钮被 grip 盖住」）：
        // - 四边四角 grip 统一 ZIndex=100，压在标题栏(DragBar, 默认 0)与内容区之上 → 四边四角均可拉伸，
        //   上边/左上角 grip 也因此在标题栏之上仍可拉伸。
        // - 仅 ChromeButtons（关闭/隐藏/添加/置顶按钮区，XAML 中 ZIndex=200）置于 grip 之上，
        //   让出右上角按钮区，保证按钮始终可点；代价是右上角那一点不再触发 ne 拉伸（按需求自行处理）。

        void AddGrip(string dir, double width, double height,
            HorizontalAlignment hAlign, VerticalAlignment vAlign, InputSystemCursorShape shape)
        {
            var grip = new ResizeGrip(dir, InputSystemCursor.Create(shape))
            {
                Width = width,
                Height = height,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
            };
            Grid.SetRowSpan(grip, 2);
            // 显式提到最上层：确保 8 个 grip 始终压在标题栏/内容区之上，
            // 避免被 DragBar / ScrollViewer 的命中测试吞掉（否则只有右下角 se 能命中）。
            Canvas.SetZIndex(grip, 100);
            grip.PointerPressed += ResizeGrip_PointerPressed;
            grip.PointerMoved += ResizeGrip_PointerMoved;
            grip.PointerReleased += ResizeGrip_PointerReleased;
            grip.PointerCanceled += ResizeGrip_PointerReleased;
            rootGrid.Children.Add(grip);
        }

        // 命中带加宽（边 8px、角 18px）以便精准命中；全部 RowSpan=2 + z=100（见 AddGrip），
        // 四边四角均可从窗口边缘直接拉伸，不再只有右下角 se 生效。
        AddGrip("n", double.NaN, 8, HorizontalAlignment.Stretch, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthSouth);
        AddGrip("s", double.NaN, 8, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthSouth);
        AddGrip("w", 8, double.NaN, HorizontalAlignment.Left, VerticalAlignment.Stretch, InputSystemCursorShape.SizeWestEast);
        AddGrip("e", 8, double.NaN, HorizontalAlignment.Right, VerticalAlignment.Stretch, InputSystemCursorShape.SizeWestEast);
        AddGrip("nw", 18, 18, HorizontalAlignment.Left, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthwestSoutheast);
        AddGrip("ne", 18, 18, HorizontalAlignment.Right, VerticalAlignment.Top, InputSystemCursorShape.SizeNortheastSouthwest);
        AddGrip("sw", 18, 18, HorizontalAlignment.Left, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNortheastSouthwest);
        AddGrip("se", 18, 18, HorizontalAlignment.Right, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthwestSoutheast);
    }

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 胶囊模式禁止缩放：即便 grip 意外可见也不响应
        if (_chromeMode == WidgetChromeMode.Compact) return;
        if (sender is not FrameworkElement { Tag: string dir }) return;
        _resizeDir = dir;
        WindowInterop.GetCursorPos(out _gestureStart);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
        BeginSnapSession();     // 缩放同样需要候选目标：用于边缘对齐与宽高对齐
        _resizing = true;
        RaiseTransient();
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        // 胶囊模式禁止缩放（防御：grip 被隐藏后不应触发，但保险拦截）
        if (_chromeMode == WidgetChromeMode.Compact) return;
        WindowInterop.GetCursorPos(out var pt);
        var scale = WindowInterop.GetScale(this);
        var minW = (int)(200 * scale);
        var minH = (int)(120 * scale);
        var dx = pt.X - _gestureStart.X;
        var dy = pt.Y - _gestureStart.Y;
        var x = _gestureStartRect.X;
        var y = _gestureStartRect.Y;
        var w = _gestureStartRect.Width;
        var h = _gestureStartRect.Height;

        // 拖左/上边缘时对侧边固定：先算新尺寸，再反推新位置（夹到最小尺寸时不跟手）
        if (_resizeDir.Contains('w')) { var nw = Math.Max(minW, w - dx); x += w - nw; w = nw; }
        if (_resizeDir.Contains('e')) { w = Math.Max(minW, w + dx); }
        if (_resizeDir.Contains('n')) { var nh = Math.Max(minH, h - dy); y += h - nh; h = nh; }
        if (_resizeDir.Contains('s')) { h = Math.Max(minH, h + dy); }

        var proposed = new RectInt32(x, y, w, h);

        // 用会话快照判定，不再每次指针移动重读设置：BeginSnapSession（缩放起点）已经把
        // "磁吸是否开启"折进 _snapWorkArea——关着时它是 null，开着时非 null。
        // 旧的 WidgetAppearance.SnapEnabled() 在这里按 60–120 Hz 触发一次 settings.json 读+反序列化。
        if (_snapWorkArea is not null)
        {
            // 1) 被拖动的边对齐邻居/屏幕边缘（保证边线与其他组件齐平）
            proposed = SnapResizedEdges(proposed, minW, minH);
            // 2) 尺寸对齐邻居的宽 / 高（用户很难手动把宽高调得完全一致）
            proposed = AlignSizeToNeighbors(proposed);
        }

        AppWindow.MoveAndResize(proposed);
        e.Handled = true;
    }

    /// <summary>
    /// 缩放时被拖动的那些边参与吸附：把边线贴到邻居的同向边（相邻时保留间距）或屏幕边界，
    /// 让多个组件的边缘能整体对齐。
    /// </summary>
    private RectInt32 SnapResizedEdges(RectInt32 proposed, int minW, int minH)
    {
        int left = proposed.X;
        int top = proposed.Y;
        int right = proposed.X + proposed.Width;
        int bottom = proposed.Y + proposed.Height;

        if (_resizeDir.Contains('w') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Left, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } lm)
            left = lm.Coordinate;

        if (_resizeDir.Contains('e') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Right, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } rm)
            right = rm.Coordinate;

        if (_resizeDir.Contains('n') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Top, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } tm)
            top = tm.Coordinate;

        if (_resizeDir.Contains('s') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Bottom, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } bm)
            bottom = bm.Coordinate;

        // 对侧边固定：改了宽/高就不能改同源坐标；比最小尺寸还小则放弃该轴的吸附
        return new RectInt32(
            left,
            top,
            Math.Max(minW, right - left),
            Math.Max(minH, bottom - top));
    }

    /// <summary>
    /// 把正在改变的宽 / 高对齐到邻居组件的尺寸：用户想让几个组件"一样宽 / 一样高"时，
    /// 手动拖边缘几乎不可能精确到像素，这里在阈值内直接拉齐。
    /// </summary>
    private RectInt32 AlignSizeToNeighbors(RectInt32 proposed)
    {
        const int sizeThreshold = 16;

        var result = proposed;
        var horizontal = _resizeDir.Contains('w') || _resizeDir.Contains('e');
        var vertical = _resizeDir.Contains('n') || _resizeDir.Contains('s');

        if (horizontal)
        {
            var bestDelta = sizeThreshold;
            foreach (var t in _snapTargets)
            {
                var delta = Math.Abs(t.Bounds.Width - proposed.Width);
                if (delta <= bestDelta) { bestDelta = delta; result.Width = t.Bounds.Width; }
            }
            // 拖左/北边时保持对侧位置不变
            if (_resizeDir.Contains('w')) result.X = proposed.X + proposed.Width - result.Width;
        }

        if (vertical)
        {
            var bestDelta = sizeThreshold;
            foreach (var t in _snapTargets)
            {
                var delta = Math.Abs(t.Bounds.Height - proposed.Height);
                if (delta <= bestDelta) { bestDelta = delta; result.Height = t.Bounds.Height; }
            }
            if (_resizeDir.Contains('n')) result.Y = proposed.Y + proposed.Height - result.Height;
        }

        return result;
    }

    private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        EndSnapSession();
        try { ((UIElement)sender).ReleasePointerCapture(e.Pointer); } catch { }
        PersistBounds();
        e.Handled = true;
    }

    /// <summary>
    /// 8 向缩放 grip：四边 + 四角透明命中区，进入时切换为对应方向尺寸光标、离开恢复。
    /// 基类选用 Panel（非 sealed，可承载 Background 命中；ProtectedCursor 为受保护成员，
    /// 只能在派生类的实例方法中访问，故在构造函数内订阅自身 PointerEntered/Exited 事件来设置）。
    /// Border/Grid/Canvas/StackPanel 在 WinUI 3 均 sealed，无法派生；UIElement 亦无 OnPointerEntered 虚方法。
    /// </summary>
    private sealed class ResizeGrip : Panel
    {
        private readonly InputSystemCursor _cursor;

        public ResizeGrip(string direction, InputSystemCursor cursor)
        {
            Tag = direction;
            _cursor = cursor;
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            PointerEntered += ResizeGrip_PointerEntered;
            PointerExited += ResizeGrip_PointerExited;
        }

        private void ResizeGrip_PointerEntered(object sender, PointerRoutedEventArgs e)
            => ProtectedCursor = _cursor;

        private void ResizeGrip_PointerExited(object sender, PointerRoutedEventArgs e)
            => ProtectedCursor = null;
    }

    private static bool FindAncestorButton(DependencyObject? start)
    {
        while (start is not null)
        {
            if (start is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return true;
            start = VisualTreeHelper.GetParent(start);
        }
        return false;
    }
}
