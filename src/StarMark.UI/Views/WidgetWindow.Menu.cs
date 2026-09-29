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
/// WidgetWindow 的这一段——右键菜单这一头：菜单怎么长出来、上面每颗点了做什么，以及标题与重命名。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetWindow
{

    /// <summary>
    /// 每次右键菜单打开时重建：动态内容（布局列表、隐私模式勾选）必须反映最新状态，
    /// 否则新增 / 删除布局后「应用布局」子菜单仍是旧列表。复用同一个 <see cref="_contextMenu"/> 实例，
    /// 仅清空并重新填充条目，避免替换 Flyout 导致正在打开的菜单失效。
    /// </summary>
    private void OnContextMenuOpening(object? sender, object e)
    {
        if (_peeking) CollapsePeek();
        if (_contextMenu is { } menu) PopulateMenu(menu);
    }

    /// <summary>
    /// 时钟组件右键菜单里的「护眼 · 休息提醒」一节（批次 WC-3）。
    /// <para>
    /// 引擎<b>仍然只有一个</b> <see cref="EyeRestService"/>：这里给的是"就地开关 + 改间隔"的入口，
    /// 写盘与起停都走设置页同一对出口（<c>SaveEyeRest</c> / <c>App.ApplyEyeRest</c>），
    /// 所以两处不会各自攒一份状态——第二份事实迟早对不上，而对不上的那次是用户先看见。
    /// </para>
    /// <para>菜单每次打开都重建（<see cref="OnContextMenuOpening"/>），勾选与"下一次几点"因此总是当前值。</para>
    /// </summary>
    private void BuildEyeRestSection(MenuFlyout menu)
    {
        var settings = App.Services.GetRequiredService<SettingsStore>();
        var enabled = settings.LoadEyeRestEnabled();
        var interval = settings.LoadEyeRestIntervalMinutes();

        menu.Items.Add(new MenuFlyoutSeparator());
        var section = new MenuFlyoutSubItem
        {
            Text = "护眼 · 休息提醒",
            Icon = new FontIcon { Glyph = "\uE7BA", FontSize = 14 },   // TouchPointer：示意"该歇一下"
        };

        var toggle = new ToggleMenuFlyoutItem { Text = "开启休息提醒", IsChecked = enabled };
        toggle.Click += (_, _) =>
        {
            // 提醒形式 / 全屏让路这两项不在这里改，但要原样带过去，否则一按开关就把它们重置回默认
            settings.SaveEyeRest(toggle.IsChecked, interval,
                settings.LoadEyeRestNotice(), settings.LoadEyeRestDeferOnFullscreen());
            App.ApplyEyeRest(toggle.IsChecked);
        };
        section.Items.Add(toggle);

        var gap = new MenuFlyoutSubItem { Text = $"间隔：{interval} 分钟", IsEnabled = enabled };
        // 档位与文案都取 Core 的那一份（IntervalLabels 与 IntervalOptions 同序）：
        // 这里自己拼"X 分钟"就会与设置页的下拉分岔成两处事实。
        for (var i = 0; i < EyeRestPolicy.IntervalOptions.Length; i++)
        {
            var minutes = EyeRestPolicy.IntervalOptions[i];
            var item = new RadioMenuFlyoutItem
            {
                Text = EyeRestPolicy.IntervalLabels[i],
                IsChecked = minutes == interval,
            };
            item.Click += (_, _) =>
            {
                settings.SaveEyeRest(settings.LoadEyeRestEnabled(), minutes,
                    settings.LoadEyeRestNotice(), settings.LoadEyeRestDeferOnFullscreen());
                App.ApplyEyeRest(settings.LoadEyeRestEnabled());
            };
            gap.Items.Add(item);
        }
        section.Items.Add(gap);

        // 状态常驻一行：按完开关要立刻看得见"下一次是几点"，不然"按了没反应"与"生效了但没到点"分不开
        var next = EyeRestService.NextDueAt;
        section.Items.Add(new MenuFlyoutItem
        {
            Text = next is { } due
                ? $"下一次大约 {due.ToLocalTime():HH:mm}（到点前还会看一眼前台是否全屏）"
                : enabled ? "开关已开，但计时器没起来（设置页「健康与诊断」有原因）" : "未开启",
            IsEnabled = false,
        });

        var full = new MenuFlyoutItem { Text = "完整设置（提醒形式 / 全屏让路）…" };
        // 直接落在「健康与诊断」页：跳进设置页却停在常规页，等于让人到了门口再自己找房间
        full.Click += (_, _) => App.MainWindow?.Present(true, "健康与诊断");
        section.Items.Add(full);

        menu.Items.Add(section);
    }

    private void PopulateMenu(MenuFlyout menu)
    {
        menu.Items.Clear();

        // 「添加」收进二级菜单：每种组件一个子项，可重复添加同类型组件（对标 DeskBox 多实例）。
        // 避免主菜单被一长串「添加 X」撑爆、与「移除本组件」混在一起难以区分。
        var addSub = new MenuFlyoutSubItem
        {
            Text = "添加组件",
            Icon = new FontIcon { Glyph = "\uE710", FontSize = 14 },
        };
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var add = new MenuFlyoutItem { Text = WidgetStorage.KindTitle(kind) };
            var captured = kind;
            add.Click += (_, _) => _ = _manager.AddInstanceAsync(captured);
            addSub.Items.Add(add);
        }
        menu.Items.Add(addSub);

        menu.Items.Add(new MenuFlyoutSeparator());
        var removeThis = new MenuFlyoutItem
        {
            Text = "移除本组件",
            Icon = new FontIcon { Glyph = "\uE711", FontSize = 14 },   // Cancel（X），与其它图标视觉一致
        };
        removeThis.Click += (_, _) => _ = _manager.RemoveAsync(_instanceId);
        menu.Items.Add(removeThis);

        // 护眼 · 休息提醒（批次 WC-3，用户裁决"护眼建议和时钟组件结合"）：只有时钟组件挂这一节。
        if (_kind == WidgetKind.Clock) BuildEyeRestSection(menu);

        // 胶囊模式入口（Phase B）：受描述符 CanHideChrome 控制；Hidden 态标题栏不可见时仍可经根边框菜单切换
        menu.Items.Add(new MenuFlyoutSeparator());
        var canHide = WidgetRegistry.Default.TryGet(_kind, out var desc) && desc.CanHideChrome;
        _collapseMenuItem = new MenuFlyoutItem
        {
            Text = "收起为胶囊",
            Icon = new FontIcon { Glyph = "\uE70E", FontSize = 14 }, // ChevronUp：收起
            IsEnabled = canHide,
        };
        _collapseMenuItem.Click += (_, _) => ToggleCompact();
        menu.Items.Add(_collapseMenuItem);

        _hideChromeMenuItem = new MenuFlyoutItem
        {
            Text = "隐藏外壳（仅内容）",
            Icon = new FontIcon { Glyph = "\uE921", FontSize = 14 },   // Hide（有效字形，避免显示错误方框）
            IsEnabled = canHide,
        };
        _hideChromeMenuItem.Click += (_, _) => ToggleHidden();
        menu.Items.Add(_hideChromeMenuItem);

        // 隐私模式（B-10）：收起为胶囊时隐藏标题，避免胶囊泄露组件身份/内容
        var privacyItem = new ToggleMenuFlyoutItem
        {
            Text = "隐私模式（胶囊态隐藏标题）",
            Icon = new FontIcon { Glyph = "\uE72E", FontSize = 14 }, // 锁
            IsChecked = _config.PrivacyMode,
        };
        privacyItem.Click += (_, _) =>
        {
            _config.PrivacyMode = privacyItem.IsChecked;
            ApplyChromeMode(_chromeMode);   // 刷新标题可见性
            PersistBounds();                // 持久化开关
        };
        menu.Items.Add(privacyItem);

        menu.Items.Add(new MenuFlyoutSeparator());
        var showAll = new MenuFlyoutItem { Text = "全部显示" };
        showAll.Click += (_, _) => _ = _manager.ShowAllAsync();
        var hideAll = new MenuFlyoutItem { Text = "全部隐藏" };
        hideAll.Click += (_, _) => _ = _manager.HideAllAsync();
        menu.Items.Add(showAll);
        menu.Items.Add(hideAll);

        menu.Items.Add(new MenuFlyoutSeparator());
        var main = new MenuFlyoutItem { Text = "打开 StarMark 主窗口" };
        main.Click += (_, _) => _manager.OpenMainWindow();
        var settings = new MenuFlyoutItem { Text = "管理组件…" };
        settings.Click += (_, _) => _manager.OpenWidgetSettings();
        menu.Items.Add(main);
        menu.Items.Add(settings);

        // 重命名本组件实例（名字显示在标题栏 / 胶囊标题上）
        var rename = new MenuFlyoutItem
        {
            Text = "重命名…",
            Icon = new FontIcon { Glyph = "\uE8AC", FontSize = 14 },   // Rename，与其它图标视觉一致
        };
        rename.Click += async (_, _) => await RenameAsync();
        menu.Items.Add(rename);

        // 每实例外观编辑（B-9）：材质/颜色/边框/圆角/文本缩放，可一键恢复全局
        var appearance = new MenuFlyoutItem
        {
            Text = "外观…",
            Icon = new FontIcon { Glyph = "\uE790", FontSize = 14 },
        };
        appearance.Click += async (_, _) => await EditAppearanceAsync();
        menu.Items.Add(appearance);

        // 布局方案：保存当前这一屏，或切换到已保存的布局（同一时刻只显示一套）
        menu.Items.Add(new MenuFlyoutSeparator());
        var saveLayout = new MenuFlyoutItem
        {
            Text = "保存当前组件布局…",
            Icon = new FontIcon { Glyph = "\uE78C", FontSize = 14 },
        };
        saveLayout.Click += (_, _) => _ = SaveLayoutByNameAsync();
        menu.Items.Add(saveLayout);

        var layouts = _manager.GetLayouts();
        if (layouts.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = "应用布局" };
            foreach (var l in layouts)
            {
                var id = l.Id;
                var apply = new MenuFlyoutItem { Text = $"{l.Name}（{l.Summary}）" };
                apply.Click += (_, _) => _ = _manager.ApplyLayoutAsync(id);
                sub.Items.Add(apply);
            }
            menu.Items.Add(sub);
        }

        // 布局与数据快照（#53）：连组件数据一起存成不可变历史点；应用 = 回到那一刻（带自动回滚）。
        var saveSnapshot = new MenuFlyoutItem
        {
            Text = "保存当前布局与数据…",
            Icon = new FontIcon { Glyph = "\uE787", FontSize = 14 },
        };
        saveSnapshot.Click += (_, _) => _ = SaveSnapshotByNameAsync();
        menu.Items.Add(saveSnapshot);

        var snapshots = _manager.GetSnapshots();
        if (snapshots.Count > 0)
        {
            var snapSub = new MenuFlyoutSubItem { Text = "应用快照" };
            foreach (var s in snapshots)
            {
                var id = s.Id;
                var apply = new MenuFlyoutItem { Text = $"{s.Name}（{s.Summary}）" };
                // 快照应用刻意与「应用布局」一致：直接应用、不再二次确认，因为应用前会自动留回滚点。
                // 但回滚点没存成时 Apply 会中止（返回 false）——不静默吞掉，给一条提示说明"未应用"。
                apply.Click += async (_, _) =>
                {
                    if (!await _manager.ApplySnapshotAsync(id))
                        await ShowTipAsync("未能应用快照",
                            $"{_manager.LastApplyError ?? "生成「应用前」回滚点失败"}——为防数据丢失已中止，当前状态未改动。");
                };
                snapSub.Items.Add(apply);
            }
            menu.Items.Add(snapSub);
        }

        var manageSnapshots = new MenuFlyoutItem { Text = "管理快照…" };
        manageSnapshots.Click += (_, _) =>
        {
            App.PresentMainWindow();
            App.MainWindow?.NavigateTo("snapshot");
        };
        menu.Items.Add(manageSnapshots);
    }

    /// <summary>
    /// 询问名称并把「当前所有组件的布局 + 各自的数据」存为一个不可变快照点。
    /// 与 <see cref="SaveLayoutByNameAsync"/> 的分工：那条纯模板不含数据，本条含数据（#52/#53）。
    /// </summary>
    private async System.Threading.Tasks.Task SaveSnapshotByNameAsync()
    {
        var name = await CenteredDialog.PromptAsync(
            title: "保存当前布局与数据",
            message: "把当前屏幕上所有组件的位置、外观，连同各自的数据（快捷入口 / 待办 / 随记 / 条目格查询）一起存成一个不可变快照点。之后「应用快照」即回到这一刻，并会先自动生成一个「应用前」回滚点。",
            placeholder: "例如：上线前",
            primaryText: "保存",
            cancelText: "取消",
            owner: this);

        if (name is null) return;

        // CaptureSnapshotAsync 现任一条目取数失败即整体抛出——绝不能落一张静默缺数据的快照，
        // 故此处捕获并明确告知"保存失败"，避免用户误以为已备份。
        try
        {
            var saved = await _manager.CaptureSnapshotAsync(name);
            if (saved is null) await ShowTipAsync("当前没有组件", "没有可保存的快照内容。");
            else await ShowTipAsync("已保存快照", $"「{saved.Name}」已记录（{saved.Summary}）。可在主窗口「快照」页或右键「应用快照」里回到这一刻。");
        }
        catch (Exception ex)
        {
            StarLog.Error("保存布局与数据快照失败", ex);
            await ShowTipAsync("保存快照失败", $"读取组件数据时出错，快照未保存：{ex.Message}");
        }
    }

    /// <summary>
    /// 询问名称并把当前可见组件保存为一整套布局方案。
    /// 弹窗走 <see cref="CenteredDialog"/>（独立居中顶层窗口）：组件窗口可能只有 200×150，
    /// 挂在组件 XamlRoot 上的 ContentDialog 会被窗口裁掉，用户根本看不见。
    /// </summary>
    private async System.Threading.Tasks.Task SaveLayoutByNameAsync()
    {
        var name = await CenteredDialog.PromptAsync(
            title: "保存当前组件布局",
            message: "记下当前屏幕上所有可见组件的位置、大小与各自的外观配置（材质/颜色等），形成一套可复用的模板。不含任何条目内容数据（待办/随记/快捷入口/笔记）。应用布局时，不属于该布局的组件会被隐藏（内容保留）。",
            placeholder: "例如：工作模式",
            primaryText: "保存",
            cancelText: "取消",
            owner: this);

        if (name is null) return;

        var saved = await _manager.SaveCurrentLayoutAsync(name);
        if (saved is null) await ShowTipAsync("当前没有可见的组件", "没有可保存的布局内容。");
    }

    private async System.Threading.Tasks.Task ShowTipAsync(string title, string message)
    {
        try { await CenteredDialog.MessageAsync(title, message, owner: this); }
        catch { /* 窗口正在关闭 */ }
    }

    /// <summary>打开每实例外观编辑浮层（B-9）：编辑中实时预览到本组件，确定后持久化，取消则还原。
    /// 同一组件若已有一个编辑器在打开，直接忽略后续点击（避免叠加多个浮层）。</summary>
    private async System.Threading.Tasks.Task EditAppearanceAsync()
    {
        if (_appearanceEditorOpen) return;   // 单例守护：防止多次右键「外观…」叠加多个编辑器
        _appearanceEditorOpen = true;
        try
        {
            var original = _config.Appearance;   // 取消时按此还原实时预览
            var result = await WidgetAppearanceEditor.ShowAsync(
                this,
                _config,
                preview =>
                {
                    // 实时预览：直接套用临时覆盖，不落盘
                    _config.Appearance = preview;
                    ApplyAppearanceCore();
                },
                textPreview =>
                {
                    // 只动字号的预览：不重挂材质 / 不重铺背景（见 ApplyTextAppearanceCore 的说明）
                    _config.Appearance = textPreview;
                    ApplyTextAppearanceCore();
                });
            if (result.Saved)
            {
                _config.Appearance = result.Override;     // 确定：写回并持久化
                ApplyAppearanceCore();
                await _manager.SaveInstanceAppearanceAsync(_instanceId, result.Override);
            }
            else
            {
                // 取消：还原为打开前的外观（编辑器内部已回退实时预览，这里兜底确保一致）
                _config.Appearance = original;
                ApplyAppearanceCore();
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("编辑组件外观失败", ex);
        }
        finally
        {
            _appearanceEditorOpen = false;
        }
    }

    /// <summary>组件类型的默认标题（未重命名时的显示名）。</summary>
    private string DefaultKindTitle()
        => WidgetRegistry.Default.TryGet(_kind, out var d) ? d.Title : _kind.ToString();

    /// <summary>当前显示名：优先用户重命名的名字，未命名/null 时回退到类型默认标题。</summary>
    private string DisplayTitle()
        => string.IsNullOrWhiteSpace(_config.Title) ? DefaultKindTitle() : _config.Title!;

    /// <summary>把显示名同步到标题栏文本与窗口标题（重命名后立即刷新）。</summary>
    private void ApplyTitle()
    {
        try
        {
            var t = DisplayTitle();
            WidgetTitle.Text = t;
            Title = $"StarMark 组件 - {t}";
        }
        catch { /* 标题不是关键路径，失败不影响窗口可用 */ }
    }

    /// <summary>
    /// 右键「重命名…」：弹出输入框修改本组件实例的名字并持久化。
    /// 提交空字符串即恢复组件类型的默认标题（存 null，不占磁盘、与旧版本配置兼容）。
    /// </summary>
    private async System.Threading.Tasks.Task RenameAsync()
    {
        try
        {
            var def = DefaultKindTitle();
            var input = await CenteredDialog.PromptAsync(
                title: "重命名组件",
                message: $"给这个组件取个名字（留空则恢复默认名称「{def}」）。",
                placeholder: def,
                defaultText: string.IsNullOrWhiteSpace(_config.Title) ? null : _config.Title,
                owner: this);
            if (input is null) return;   // 用户取消

            _config.Title = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
            ApplyTitle();
            await _manager.SaveInstanceTitleAsync(_instanceId, _config.Title);
        }
        catch (Exception ex)
        {
            StarLog.Error("重命名组件失败", ex);
        }
    }
}
