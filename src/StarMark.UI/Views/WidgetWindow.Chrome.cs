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
/// WidgetWindow 的这一段——外壳（无边框 chrome）这一头：胶囊态/展开态、悬停偷看、上面那排按钮与置顶，全部围绕"框本身怎么变"。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetWindow
{

    /// <summary>
    /// 该窗口当前是否收起为胶囊。<see cref="WidgetChromeMode.Compact"/> 时窗口实际显示的是胶囊矩形，
    /// 读到的 <c>GetWindowRect</c> 是胶囊尺寸而非展开尺寸——布局/快照取几何须避开它、改用已持久化的展开位（inst.X/Y/W/H）。
    /// </summary>
    public bool IsCollapsed => _chromeMode == WidgetChromeMode.Compact;

    // ───────────────────────── 标题栏交互 ─────────────────────────

    private void WireChrome()
    {
        DragBar.PointerPressed += DragBar_PointerPressed;
        DragBar.PointerMoved += DragBar_PointerMoved;
        DragBar.PointerReleased += DragBar_PointerReleased;
        DragBar.PointerCanceled += DragBar_PointerReleased;
        DragBar.DoubleTapped += (_, _) => TogglePin();
        // 右键菜单挂到标题栏与根边框：Hidden 态标题栏不可见，
        // 此时右键内容区仍能唤起同一份菜单切换回标准/胶囊。
        // 注意：菜单必须在每次 Opening 时重建（见 OnContextMenuOpening），因为「应用布局」子菜单
        // 依赖当前已保存的布局列表 —— 用户新增 / 删除布局后若仍用旧菜单，子项不会更新。
        _contextMenu = new MenuFlyout();
        DragBar.ContextFlyout = _contextMenu;
        RootBorder.ContextFlyout = _contextMenu;

        // 右键胶囊时：先进入悬停预览 → 右键唤起菜单的过程中，鼠标离开胶囊会触发收起，
        // 导致菜单随胶囊消失。修复：菜单打开即收回预览（回到胶囊位），且菜单打开期间禁止
        // 任何收起/重新预览，菜单关闭后才允许（见 RootBorder_PointerEntered/Exited 的 _contextMenu.IsOpen 守卫）。
        _contextMenu.Opening += OnContextMenuOpening;
        _contextMenu.Closed += (_, _) => { if (_peeking) CollapsePeek(); };
        PopulateMenu(_contextMenu);

        // 胶囊三段式热区 + 悬停预览（B-10）：仅在 Compact 态生效，标准态走原有标题栏按钮
        DragBar.Tapped += DragBar_Tapped;
        RootBorder.PointerEntered += RootBorder_PointerEntered;
        RootBorder.PointerExited += RootBorder_PointerExited;

        // 键盘操作（对齐 DeskBox 的 WindowInteraction）：Esc 收起悬停预览、Enter/Space 展开胶囊、F2 重命名。
        // 挂 RootGrid 而不是 Window —— WinUI 3 的 Window 没有 KeyDown，路由事件从焦点元素冒泡到内容根。
        RootGrid.KeyDown += RootGrid_KeyDown;

        if (WidgetStorage.IsResizable(_kind))
        {
            AddResizeGrips();
        }

        AppWindow.Closing += (_, e) =>
        {
            if (_shuttingDown) return;
            // 非代码主动关闭（标题栏已移除，正常不会触发）按“移除组件”处理；
            // 延迟到回调返回后执行，避免在 Closing 事件内重入 Close
            e.Cancel = true;
            DispatcherQueue.TryEnqueue(() => { var task = _manager.RemoveAsync(_instanceId); });
        };
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (DragBar.ContextFlyout is MenuFlyout menu)
            menu.ShowAt(AddButton, new Point(0, AddButton.ActualHeight));
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => _ = _manager.HideTemporaryAsync(_instanceId);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => _ = _manager.RemoveAsync(_instanceId);

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => ToggleCompact();

    /// <summary>
    /// 应用外壳呈现模式（Phase B 胶囊模式）：
    /// Standard=标准标题栏+内容；Compact=收起为胶囊（仅标题栏、内容隐藏、窗口缩到标题高度、缩放柄隐）；
    /// Hidden=隐藏外壳（标题栏/按钮/缩放柄均隐、内容铺满，作叠加浮层）。结果持久化到实例配置。
    /// </summary>
    private void ApplyChromeMode(WidgetChromeMode mode)
    {
        try
        {
            // 进入任何外壳模式都取消悬停预览（_peeking 仅在 Compact 临时展开期间为真）
            _peeking = false;

            // 该类型不允许收起外壳时，Hidden 回退为 Standard（Compact 仍允许，因为仅缩标题高度）
            if (mode == WidgetChromeMode.Hidden &&
                !(WidgetRegistry.Default.TryGet(_kind, out var desc) && desc.CanHideChrome))
            {
                mode = WidgetChromeMode.Standard;
            }

            // 仅「从非收起态切到胶囊态」时记录当前位置为展开态原位置，供点击展开恢复；
            // 重复切到胶囊（如已在胶囊态）不覆盖，避免把胶囊停靠位误记为展开位。
            if (mode == WidgetChromeMode.Compact && _chromeMode != WidgetChromeMode.Compact)
            {
                var cur = WindowInterop.GetWindowRect(this);
                if (cur.Width > 0 && cur.Height > 0) _expandedRect = cur;
            }

            _chromeMode = mode;
            _config.ChromeMode = mode;

            var compact = mode == WidgetChromeMode.Compact;
            var hidden = mode == WidgetChromeMode.Hidden;

            RootGrid.RowDefinitions[0].Height = hidden ? new GridLength(0) : new GridLength(36);
            // 胶囊模式下内容行高夹 0：即便窗口被系统强制拉高，也绝不露出「无内容页」
            RootGrid.RowDefinitions[1].Height = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            DragBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            ChromeButtons.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            ContentScroll.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            SetGripsVisible(!compact);

            // 隐私模式：Compact 态隐藏标题（正文已隐含隐藏），避免胶囊泄露组件身份/内容；
            // 标准态不隐藏（正文可见，隐私无意义），仅收起态生效。
            var hideTitle = compact && _config.PrivacyMode;
            WidgetTitle.Visibility = hideTitle ? Visibility.Collapsed : Visibility.Visible;

            if (compact) MoveToCapsule();      // 回到稳定胶囊停靠位（不重算堆叠）
            else ExpandToNormal();             // 点击展开 / 隐藏外壳 → 恢复到收起前的展开态位置

            UpdateChromeControls();
            PersistBounds();
        }
        catch (Exception ex)
        {
            // 外壳模式切换绝不冒泡到点击处理（否则「全部显示」等入口可能演变成未处理异常致应用崩溃）
            StarLog.Error($"应用组件外壳模式失败 ({_kind},{mode})", ex);
        }
    }

    /// <summary>
    /// 收起为胶囊：移到稳定停靠位 <see cref="_capsuleRect"/>（已分配则直接用，不重算堆叠）。
    /// 优先用持久化的 <see cref="WidgetInstanceConfig.CapsuleX/Y"/>（重启后同列胶囊不再错位），
    /// 否则首次进入胶囊时调用 <see cref="AssignCapsuleSlot"/> 计算一次。
    /// 锁定宽度到胶囊宽、高度缩到标题栏高度（36 DIP 物理像素）。
    /// </summary>
    private void MoveToCapsule()
    {
        if (_capsuleRect is null)
        {
            if (_config.CapsuleX is { } cx && _config.CapsuleY is { } cy)
            {
                // 持久化停靠位可能来自旧版本 / 不同分辨率 / 历史「胶囊拉伸」bug，坐标为越界或 (0,0) 角点；
                // 落在工作区外则视为失效，改走 AssignCapsuleSlot 重新吸附最近垂直边缘。
                var wa = WindowInterop.GetWorkArea(this);
                var sane = cx >= wa.X && cx <= wa.X + wa.Width && cy >= wa.Y && cy <= wa.Y + wa.Height;
                if (sane)
                {
                    var scale = WindowInterop.GetScale(this);
                    // 胶囊统一宽度（不随组件原始宽度变化），保证角落胶囊整齐一致
                    _capsuleWidth = UnifiedCapsuleWidth;
                    _capsuleRect = new RectInt32(cx, cy, _capsuleWidth, (int)(36 * scale));
                }
                else
                {
                    AssignCapsuleSlot();
                }
            }
            else
            {
                AssignCapsuleSlot();
            }
        }
        if (_capsuleRect is { } r)
        {
            _capsuleWidth = r.Width;
            AppWindow.MoveAndResize(r);
        }
    }

    /// <summary>
    /// 分配胶囊停靠位（仅首次 / 强制重排时调用一次）：吸附到最近的屏幕垂直边缘（左/右），
    /// 并与同边缘已停靠（高度≈胶囊）的其它胶囊向下堆叠，形成一列停靠栏。
    /// 结果写入 <see cref="_capsuleRect"/> 并随实例持久化（CapsuleX/CapsuleY），后续悬停预览收回时
    /// 直接回到该位，绝不再重算——这正是修复「悬停后同列胶囊依次下移、最终移出屏幕」的关键。
    /// </summary>
    private void AssignCapsuleSlot()
    {
        var scale = WindowInterop.GetScale(this);
        var r = WindowInterop.GetWindowRect(this);
        if (r.Width <= 0 || r.Height <= 0) return;
        // 统一胶囊宽度：所有胶囊一致（不随各组件原始宽度变化），避免宽窄不一、也杜绝历史「胶囊拉伸」残留
        _capsuleWidth = UnifiedCapsuleWidth;
        var capH = (int)(36 * scale);

        int dockX, y;
        try
        {
            var wa = WindowInterop.GetWorkArea(this);
            var toLeft = (r.X + r.Width / 2) < (wa.X + wa.Width / 2);
            dockX = toLeft ? wa.X + 8 : wa.X + wa.Width - _capsuleWidth - 8;
            y = wa.Y + 8;
            // 与同边缘（X 对齐到 dockX）已停靠（高度≈胶囊）的其它组件堆叠，避免重叠
            foreach (var o in _manager.GetOtherBounds(_instanceId))
            {
                if (Math.Abs(o.X - dockX) <= 8 && o.Height <= capH + 6)
                {
                    var bottom = o.Y + o.Height + 8;
                    if (bottom > y) y = bottom;
                }
            }
        }
        catch
        {
            // 拿不到工作区/其它实例边界时回退到当前位置
            dockX = r.X;
            y = r.Y;
        }

        _capsuleRect = new RectInt32(dockX, y, _capsuleWidth, capH);
    }

    /// <summary>展开为正常：恢复到收起前记录的展开态位置/尺寸（<see cref="_expandedRect"/>）。</summary>
    private void ExpandToNormal()
    {
        if (_expandedRect is { } er && er.Width > 0 && er.Height > 0)
        {
            AppWindow.MoveAndResize(new RectInt32(er.X, er.Y, er.Width, er.Height));
            return;
        }
        var r = WindowInterop.GetWindowRect(this);
        if (r.Width <= 0 || r.Height <= 0) return;
        var w = (int)_config.Width > 0 ? (int)_config.Width : r.Width;
        var h = (int)_config.Height > 0 ? (int)_config.Height : r.Height;
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, w, h));
    }

    private void SetGripsVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var g in _grips) g.Visibility = v;
    }

    private void ToggleCompact() =>
        ApplyChromeMode(_chromeMode == WidgetChromeMode.Compact ? WidgetChromeMode.Standard : WidgetChromeMode.Compact);

    private void ToggleHidden() =>
        ApplyChromeMode(_chromeMode == WidgetChromeMode.Hidden ? WidgetChromeMode.Standard : WidgetChromeMode.Hidden);

    // ── 胶囊三段式热区 + 悬停预览（B-10）──

    /// <summary>
    /// 胶囊态标题栏的点击分区（仅在 Compact 生效）：左 1/3 = 主操作、中 1/3 = 展开、右 1/3 = 弹出菜单。
    /// 拖动用 PointerPressed/Moved/Released 处理，Tapped 只在「未拖动」时触发，两者不冲突。
    /// </summary>
    private void DragBar_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_chromeMode != WidgetChromeMode.Compact) return;
        var w = DragBar.ActualWidth;
        if (w <= 0) return;
        var x = e.GetPosition(DragBar).X;
        if (x < w / 3.0) ActivatePrimary();
        else if (x < 2.0 * w / 3.0) ToggleCompact();
        else _contextMenu?.ShowAt(DragBar, e.GetPosition(DragBar));
    }

    /// <summary>主操作（胶囊左区）：搜索/快捷启动/各条目格 → 唤起主窗口；剪贴板格直接落到「剪贴板」页；时钟/待办/随记 → 就地展开交互。</summary>
    private void ActivatePrimary()
    {
        if (_kind is WidgetKind.Search or WidgetKind.QuickLaunch
            or WidgetKind.Clipboard or WidgetKind.TagGrid
            or WidgetKind.Activity or WidgetKind.Pinned)
        {
            // 剪贴板格点名要去的就是那一页：停在「全部」列表上等于把用户带到医院却不挂号。
            // NavigateTo 不可用（主窗尚未就绪）时退回"唤起主窗"，至少窗口来到前台。
            if (_kind == WidgetKind.Clipboard && App.MainWindow is { } mw) { mw.NavigateTo("clipboard"); return; }
            _manager.OpenMainWindow();
        }
        else if (_chromeMode == WidgetChromeMode.Compact)
        {
            ToggleCompact();   // 时钟/待办/随记：展开到正常态以便直接操作
        }
    }

    /// <summary>鼠标进入组件窗口（Compact 态且非拖动/预览中、且右键菜单未打开）→ 临时展开预览内容，离开即收回。</summary>
    private void RootBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // 右键菜单打开期间禁止预览/收起，否则菜单会随胶囊收起而消失（见 _contextMenu.Opening/Closed 守卫）
        if (_contextMenu?.IsOpen == true) return;
        if (_chromeMode == WidgetChromeMode.Compact && !_dragging && !_peeking) PeekExpand();
    }

    /// <summary>
    /// 组件窗口键盘操作（对齐 DeskBox <c>Views/ContentWidgetWindow.WindowInteraction.cs</c>）。
    /// <para>
    /// 只处理「窗口级」语义，且<b>必须先排除输入类控件</b>：待办/随记的输入框里按 Esc/Space
    /// 属于编辑语境（取消输入、打空格），被窗口抢走会直接破坏输入体验。
    /// </para>
    /// </summary>
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsEditingSource(e.OriginalSource)) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                // 悬停预览时 Esc = 收起预览回到胶囊（DeskBox 同名语义）。
                // 未预览时 Esc 不做任何事——避免用户按 Esc 意外把展开的组件收成胶囊。
                if (_peeking)
                {
                    CollapsePeek();
                    e.Handled = true;
                }
                break;

            case Windows.System.VirtualKey.Enter:
            case Windows.System.VirtualKey.Space:
                // 胶囊态 Enter/Space = 展开到正常态（等同点击胶囊中区）
                if (_chromeMode == WidgetChromeMode.Compact)
                {
                    ApplyChromeMode(WidgetChromeMode.Standard);
                    e.Handled = true;
                }
                break;

            case Windows.System.VirtualKey.F2:
                // 双击已被「切换置顶」占用（README 已写明该手势），故重命名走 Windows 惯例的 F2
                _ = RenameAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 按键来源是否属于「正在编辑文本」——这类按键窗口一律不拦截。
    /// </summary>
    private static bool IsEditingSource(object? source) => source is
        TextBox or RichEditBox or PasswordBox or AutoSuggestBox or NumberBox or ComboBox or RichTextBlock;

    private void RootBorder_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 菜单仍打开时（鼠标移到菜单上会离开胶囊）不收起，避免菜单被吞掉
        if (_contextMenu?.IsOpen == true) return;
        if (_peeking) CollapsePeek();
    }

    /// <summary>悬停预览：临时把胶囊展开到正常尺寸并显示内容（外壳模式仍为 Compact，故尺寸夹取放行）。
    /// 预览锚定在胶囊当前停靠位（不恢复展开态位置），离开即收回到同一胶囊位 —— 因此预览不会挪动胶囊、
    /// 也不会触发堆叠重算（修复同列胶囊被推离原位/移出屏幕）。</summary>
    private void PeekExpand()
    {
        if (_chromeMode != WidgetChromeMode.Compact || _peeking || _dragging) return;
        _peeking = true;
        try
        {
            var scale = WindowInterop.GetScale(this);
            // 悬停预览也用「统一胶囊宽度」：宽与收起态一致，仅向下展开内容高度，外观上「原地预览」；
            // 只有「点击展开」(ToggleCompact→Standard) 才经 ExpandToNormal 恢复到组件原本的位置与尺寸。
            var w = _capsuleWidth > 0 ? _capsuleWidth : UnifiedCapsuleWidth;
            var h = (int)_config.Height > 0 ? (int)_config.Height : (int)(WidgetStorage.DefaultHeight(_kind) * scale);
            // 以胶囊停靠位为锚点展开，保持 X/Y 不变（仅向下放大内容），外观上「原地预览」
            var anchor = _capsuleRect ?? WindowInterop.GetWindowRect(this);
            if (RootGrid.RowDefinitions.Count > 1)
                RootGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            ContentScroll.Visibility = Visibility.Visible;
            AppWindow.MoveAndResize(new RectInt32(anchor.X, anchor.Y, w, h));
        }
        catch (Exception ex)
        {
            // 预览期间内容首次参与布局；内容自身渲染失败（如 x:Bind 资源缺失抛在 Measure 阶段）
            // 会把异常甩回这里。必须兜住并立刻收回：否则 _peeking 卡在 true，
            // 胶囊再也收不回去，下一次悬停还会再炸一次（踩坑 #83）。
            StarLog.Error($"展开胶囊悬停预览失败（{_kind}）", ex);
            SafeCollapsePeek();
        }
    }

    /// <summary>收回悬停预览：恢复胶囊尺寸与隐藏内容，并回到稳定停靠位（不再重算堆叠）。</summary>
    private void CollapsePeek()
    {
        if (!_peeking) return;
        SafeCollapsePeek();
    }

    /// <summary>无条件收回预览（内部实现；预览态本身出问题时也要能把它收回去）。</summary>
    private void SafeCollapsePeek()
    {
        _peeking = false;
        try
        {
            if (RootGrid.RowDefinitions.Count > 1)
                RootGrid.RowDefinitions[1].Height = new GridLength(0);
            ContentScroll.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StarLog.Error($"隐藏胶囊预览内容失败（{_kind}）", ex);
        }

        try { MoveToCapsule(); }   // 直接回到 _capsuleRect，不重算堆叠 → 不会把同列胶囊推走
        catch (Exception ex) { StarLog.Error($"收回胶囊失败（{_kind}）", ex); }
    }

    /// <summary>同步折叠按钮图标与右键菜单文案到当前外壳模式。</summary>
    private void UpdateChromeControls()
    {
        if (CollapseIcon is not null)
            CollapseIcon.Glyph = _chromeMode == WidgetChromeMode.Compact ? "\uE70D" : "\uE70E"; // 展开/收起
        if (_collapseMenuItem is not null)
            _collapseMenuItem.Text = _chromeMode == WidgetChromeMode.Compact ? "展开组件" : "收起为胶囊";
        if (_hideChromeMenuItem is not null)
            _hideChromeMenuItem.Text = _chromeMode == WidgetChromeMode.Hidden ? "显示外壳（标题栏）" : "隐藏外壳（仅内容）";
    }

    private void TogglePin()
    {
        _config.Topmost = !_config.Topmost;
        ApplyTopmost();
        PersistBounds();
    }

    /// <summary>本窗口当前是否置顶（供「组件总控」判断这一批的整体方向）。</summary>
    public bool IsTopmost => _config.Topmost;

    /// <summary>
    /// 由「切换所有组件置顶」快捷键统一下发：<b>只改层序、不落盘</b>。
    /// 落盘交给 WidgetManager 一次写完 —— 逐窗口各自 Load/Save 会把一次按键变成 N 次整档读写。
    /// </summary>
    public void ApplyGlobalTopmost(bool topmost)
    {
        _config.Topmost = topmost;
        ApplyTopmost();
    }

    private void WidgetWindow_Closed(object sender, WindowEventArgs args)
    {
        _ticker?.Stop();
        // 必须先脱离桌面层，否则会残留指向 SHELLDLL_DefView 的悬挂所有者
        WidgetLayerService.DetachFromDesktopLayer(WindowInterop.GetHwnd(this));
        _layerAttached = false;
        // 释放本窗口的亚克力/云母控制器：它们持有原生合成资源与 DWM 句柄，
        // 只靠 ConditionalWeakTable 不会主动 Dispose（复用策略下控制器是长期存活的）。
        WidgetAppearance.ReleaseBackdrop(this);
    }
}
