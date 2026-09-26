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
/// 鼠标链：按下 → 拖动 → 放开，加上物理像素 ↔ DIP 的坐标换算。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 鼠标：按下 → 拖动 → 放开 ──────────

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        var point = e.GetCurrentPoint(Root);
        var physical = ToPhysical(point.Position.X, point.Position.Y);

        if (!point.Properties.IsLeftButtonPressed) return;

        // ── ① 改框的边与角（截图态）。<b>边框优先于落笔</b>（Snipaste 同款）：选了笔，按在选区
        // 边缘那一圈仍是改框。但<b>框内那条（Move）不在这里抢</b>——SelectionEdgeAt 对框内一律回
        // Move，之前它排在落笔前面，等于"拿着笔按在框内＝挪框"，笔永远落不上（真机反馈
        // "点击编辑工具后编辑无效，反而把识别出的框又拖了一遍"的真因）。
        if (_mode == CaptureMode.Toolbar && !_pinned && _selection is { } box)
        {
            var edge = CaptureGeometry.SelectionEdgeAt(box, AsPixel(physical), SelectionSlop);
            if (edge is not (CaptureGeometry.SelectionEdge.None or CaptureGeometry.SelectionEdge.Move))
            {
                BeginAdjust(edge, physical, e.Pointer);
                return;
            }
        }

        // ── ② 框内：没拿笔先问是不是抓已画的那条（点住就拖，一次成），拿笔就是落笔。
        if (_mode == CaptureMode.Toolbar && _base is not null && InsideSelection(physical))
        {
            if (!Armed && TryBeginGrab(ToLocal(physical), e.Pointer)) return;
            if (_tool is not { } tool)
            {
                // 贴图态没有框可改，这一按＝移动整张图；截图态未拿笔按在框内＝挪框
                //（Move 那条在 ① 里被刻意放行，就是为了让"抓标注"排在"挪框"前面）。
                if (_pinned) BeginPinDrag(e.Pointer);
                else BeginAdjust(CaptureGeometry.SelectionEdge.Move, physical, e.Pointer);
                return;
            }
            BeginToolStroke(physical, e.Pointer);
            return;
        }

        // ── ③ 贴图态没有"重新框一块"这回事：整块画面就是内容，落点稍偏（圆角外、边缘那一像素）
        // 也不能把底图丢掉——那等于把用户已经画好的标注一起清空。
        // 没选笔 ⇒ 这一按是"移动这张图"（与截图态"未选笔＝不动笔、可以改框"同一语义）。
        if (_pinned)
        {
            if (!Armed) BeginPinDrag(e.Pointer);
            return;
        }

        // ── ④ 截图态拿着笔：选区外那一按<b>也是落笔</b>（标注可以越出选区，批次 PU）。
        // 想换个范围就先"再点一次取消选中"回到改框那一态，再拖新框。
        if (_mode == CaptureMode.Toolbar && Armed)
        {
            BeginToolStroke(physical, e.Pointer);
            return;
        }

        // ── ⑤ 自动检测窗口：点在候选上＝预填选区，仍处于框选阶段（点一下确认 / 从这里拖出
        // 自定义范围），识别阶段不出现菜单栏。按住 Ctrl 时 _detected 为 null，退回手动拖框。
        if (_detected is { } detected && ContainsPoint(detected, AsPixel(physical)))
        {
            if (_mode == CaptureMode.Toolbar) BeginCandidatePress(physical, e.Pointer);
            else { _selection = detected; Commit(_mode == CaptureMode.Pin ? CommitAction.Pin : CommitAction.Ocr); }
            return;
        }

        // ── ⑥ 手动拖框。已画的标注<b>留在屏上</b>（标注跟屏走，不再随选区作废）。
        BeginRegionDrag(physical, e.Pointer);
    }

    /// <summary>
    /// 落笔的那一按（框内/框外同一处分发）。<b>笔种在按下那一刻钉进 <see cref="_strokeTool"/></b>：
    /// 中途换工具不许改正在画的这一笔。折线是"点出来的"，每一按钉一个顶点，收口用 Enter 或双击。
    /// </summary>
    private void BeginToolStroke(PointInt32 physical, Pointer pointer)
    {
        var tool = _tool!.Value;
        _strokeTool = tool;                        // 这一笔从头到尾用它，与中途会不会换工具无关
        var local = ToLocal(physical);
        // 橡皮擦：按住拖过标注，整条擦掉
        if (tool == AnnotationTool.Eraser) { BeginEraseStroke(local, pointer); return; }
        // 序号：每按一下放一个编号圆点
        if (tool == AnnotationTool.Number) { PlaceNumber(local); return; }
        if (tool == AnnotationTool.PolyLine) PlaceVertex(local);
        else BeginStroke(local, pointer, tool);
    }

    /// <summary>
    /// 按在候选窗口上的那一按：记住候选、进入"等待放开"。<b>选区当场预填成这个候选</b>——
    /// 点一下（没拖动）松手＝确认它，按住拖动＝从这里拖出自定义框选（逐帧归一化的矩形会覆盖预填值）。
    /// 预填是必须的：纯点击可能一个 Moved 事件都没有，松手时 _selection 若还是 null，
    /// "确认候选"就永远走不到。两条路都仍处于框选阶段，松手才确认并出现工具条。
    /// </summary>
    private void BeginCandidatePress(PointInt32 physical, Pointer pointer)
    {
        _candidate = _detected;
        _startPhysical = physical;
        _awaitingRelease = true;
        _selection = _candidate;
        HintChip.Visibility = Visibility.Collapsed;
        ErrorChip.Visibility = Visibility.Collapsed;
        Root.CapturePointer(pointer);      // 拖出窗口边界也要继续收到 Moved/Released
        if (_candidate is { } cand) DrawSelection(cand);   // 按住期间候选框保持可见（别被 0 尺寸清掉）
    }

    /// <summary>
    /// 手动拖一块新选区。已画的标注<b>全部留在屏上</b>（批次 PU：标注跟屏走，
    /// 不再随"重新框一块"作废——用户画了一半换个范围，笔迹还在原处等着被圈进新框）。
    /// 正在打的那行字先落笔保住；没收口的折线作废。
    /// </summary>
    private void BeginRegionDrag(PointInt32 physical, Pointer pointer)
    {
        EndTextEditing(commit: true);
        FinishPolyLine(commit: false);
        _stroke = null;
        DropSelection();
        _candidate = null;
        _selection = null;
        HintChip.Visibility = Visibility.Collapsed;
        SetBarVisible(false);
        ErrorChip.Visibility = Visibility.Collapsed;
        _startPhysical = physical;
        _awaitingRelease = true;
        Root.CapturePointer(pointer);      // 拖出窗口边界也要继续收到 Moved/Released
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

        // 选区阶段（还没确认选区）：放大镜一直跟；未拖框时做窗口自动检测
        if (!_pinned && !_annotating)
        {
            if (!_awaitingRelease)
            {
                if (_autoDetect && !IsControlDown()) UpdateDetected(AsPixel(physical));
                else ClearDetected();
            }
            UpdateMagnifier(AsPixel(physical));
        }
        else if (!_pinned)
        {
            UpdateHoverCursor(physical);            // 悬停光标：框内十字箭头 / 边缘双向箭头 / 拿笔十字准线
        }

        if (!_awaitingRelease) return;
        var selection = CaptureGeometry.Normalize(_startPhysical.X, _startPhysical.Y, physical.X, physical.Y);
        // 夹回本屏：L1 按"每屏各截各的"处理跨屏拖拽（每屏一个遮罩窗，各拿各的选区，互不合并）
        var normalized = CaptureGeometry.Intersect(selection, _monitor) ?? selection;
        // 按在候选上的那一下还没拖出形状（点击时的毫级抖动）就继续亮着候选框——
        // 拖动一旦成型就接管为自定义范围；这也是 Snipaste 的手感：建议框留到你真的拖走它。
        if (_candidate is not null && normalized.Width < CaptureGeometry.MinSelectionSide
            && normalized.Height < CaptureGeometry.MinSelectionSide) return;
        _selection = normalized;
        if (_selection is { } box)
        {
            DrawSelection(box);
            // 确认过选区之后又拖了新框：压暗交给 XAML 实时跟随（内容源切到无压暗的平面缓冲），
            // 拖动全程零整帧重烤——每 16ms 烤一次 4K 整帧会阻塞 UI 线程，真机反馈"拖框明显卡顿"。
            if (_annotating) ShowFlatWhileDragging();
        }
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
        var candidate = _candidate;
        _candidate = null;

        // 只点了一下没拖动：按在候选上＝确认这个候选（窗口检测那条捷径）；
        // 按在空白处＝静默清掉让用户再拖一次（"选区太小"的措辞留给真拖了但太窄的情况）。
        if (selection.Width < CaptureGeometry.MinSelectionSide && selection.Height < CaptureGeometry.MinSelectionSide)
        {
            if (candidate is { } cand)
            {
                _selection = cand;
                ConfirmSelection();
                return;
            }
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
        else ConfirmSelection();
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
        // 已确认选区（框选阶段起）⇒ 双击交给"落笔/编辑"那一层，不再承担提交
        if (_annotating) return;
        // 选区阶段双击＝复制（没框就先取整屏），Snipaste 同款
        e.Handled = true;
        _selection ??= _monitor;
        ConfirmSelection();
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
        // 方向键：未确认选区＝移动/缩放选区（Shift＝缩放）；确认之后＝移动选中的标注（Shift＝10px）。
        // Snipaste 同款，坐标为物理像素。
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            e.Handled = true;
            if (_pinned || _annotating) NudgeSelectedMark(e.Key, IsShiftDown());
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
                // 关闭会连没烤出去的标注一起丢，一次误按全没是最贵的出口。
                // 截图态同样两级（批次 PU）：拿着笔先收笔回到改框那一态，再按才取消整场。
                if (_pinned)
                {
                    if (_selected is not null) DropSelection();
                    else Close();
                }
                else if (Armed) SetTool(null);
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

    /// <summary>虚拟桌面物理像素 → 标注模型的坐标系。批次 PU 起两态的原点不同：
    /// <b>截图态＝整帧左上角</b>（标注跟屏走、可以越出选区，选区只是最后裁剪的记号）；
    /// <b>贴图态＝贴图左上角</b>，且"显示像素"与"底图像素"差一个 zoom，要除掉——不除就是
    /// 2.5× 的贴图"鼠标明明在字上、笔落在字外"。</summary>
    private PixelPoint ToLocal(PointInt32 physical)
    {
        var originX = _pinned ? _selection?.X ?? physical.X : _monitor.X;
        var originY = _pinned ? _selection?.Y ?? physical.Y : _monitor.Y;
        return new(
            (int)Math.Round((physical.X - originX) / _sourceScale, MidpointRounding.AwayFromZero),
            (int)Math.Round((physical.Y - originY) / _sourceScale, MidpointRounding.AwayFromZero));
    }

    /// <summary>标注坐标 → 窗口内 DIP（摆选择框、把手与文字输入框用）。截图态直接除 DPI；
    /// 贴图态按"选区原点 + 局部坐标 × 倍率"回虚拟桌面再转 DIP。</summary>
    private (double X, double Y) LocalToDip(PixelPoint local)
    {
        if (!_pinned) return (local.X / _scale, local.Y / _scale);
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

}
