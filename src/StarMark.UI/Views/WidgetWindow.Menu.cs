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
using StarMark.Abstractions.I18n;
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
    /// 时钟组件右键菜单里的「护眼 · 休息提醒」一节（批次 WC-3 起，批次 RT 补齐提醒形式与「试一试」）。
    /// <para>
    /// 引擎<b>仍然只有一个</b> <see cref="EyeRestService"/>：这里给的是"就地开关 + 改间隔 + 换提醒形式 + 演一次"的入口，
    /// 写盘与起停都走设置页同一对出口（<c>SaveEyeRest</c> / <c>App.ApplyEyeRest</c>），
    /// 所以两处不会各自攒一份状态——第二份事实迟早对不上，而对不上的那次是用户先看见。
    /// </para>
    /// <para>
    /// 这一节为什么值得摆在这儿：发起人点的原话是"护眼设置开启后需要在时钟组件内即可直接设置，
    /// 而非需要用户每次都去主窗口的设置内进行设置"。改完当场生效（服务在到点那一刻现读），不需要"保存"按钮。
    /// </para>
    /// <para>菜单每次打开都重建（<see cref="OnContextMenuOpening"/>），勾选与"下一次几点"因此总是当前值。</para>
    /// </summary>
    private void BuildEyeRestSection(MenuFlyout menu)
    {
        var settings = App.Services.GetRequiredService<SettingsStore>();
        var enabled = settings.LoadEyeRestEnabled();
        var interval = settings.LoadEyeRestIntervalMinutes();
        var notice = settings.LoadEyeRestNotice();

        menu.Items.Add(new MenuFlyoutSeparator());
        var section = new MenuFlyoutSubItem
        {
            Text = Locale.T("widget.health.section"),
            Icon = new FontIcon { Glyph = "\uE7BA", FontSize = 14 },   // TouchPointer：示意"该歇一下"
        };

        var toggle = new ToggleMenuFlyoutItem { Text = Locale.T("widget.health.toggle"), IsChecked = enabled };
        toggle.Click += (_, _) =>
        {
            // 每一项只管自己那一件，其余三项<b>当场重新读一遍</b>带过去：拿"菜单打开那一刻"的快照写回去，
            // 就等于在本项生效的同时把别的项退回旧值（"改了间隔但开关把它重置了"这类半套状态的来源）。
            settings.SaveEyeRest(toggle.IsChecked, settings.LoadEyeRestIntervalMinutes(),
                settings.LoadEyeRestNotice(), settings.LoadEyeRestDeferOnFullscreen());
            App.ApplyEyeRest(toggle.IsChecked);
        };
        section.Items.Add(toggle);

        // 档位与说法都取 Core 的那一份（它交回的是「键＋读数」，与设置页下拉看到的是同一颗键）：
        // 这里自己拼"X 分钟"就会与设置页分岔成两处事实（批次 WI-37-B 起那句字只在表里有一份）。
        var gap = new MenuFlyoutSubItem
        {
            Text = Locale.Fmt("widget.health.gap", EyeRestPolicy.IntervalMessage(interval)),
            IsEnabled = enabled,
        };
        for (var i = 0; i < EyeRestPolicy.IntervalOptions.Length; i++)
        {
            var minutes = EyeRestPolicy.IntervalOptions[i];
            var item = new RadioMenuFlyoutItem
            {
                Text = EyeRestPolicy.IntervalMessage(minutes).Render(),
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

        // 提醒形式那一排（批次 RT）：菜单标题带上当前档怎么说，用户不用展开就知道现在配的是什么；
        // 说法一律取 Core 那颗键（NoticeMessage）——这一档"能不能点掉"在两处各写一份迟早对不上。
        var how = new MenuFlyoutSubItem
        {
            Text = Locale.Fmt("widget.health.notice", EyeRestPolicy.NoticeMessage(notice)),
            IsEnabled = enabled,
        };
        for (var i = 0; i < EyeRestPolicy.NoticeOptions.Length; i++)
        {
            var option = EyeRestPolicy.NoticeOptions[i];
            var item = new RadioMenuFlyoutItem
            {
                Text = EyeRestPolicy.NoticeMessage(option).Render(),
                IsChecked = option == notice,
            };
            item.Click += (_, _) =>
            {
                settings.SaveEyeRest(settings.LoadEyeRestEnabled(), settings.LoadEyeRestIntervalMinutes(),
                    option, settings.LoadEyeRestDeferOnFullscreen());
                App.ApplyEyeRest(settings.LoadEyeRestEnabled());
            };
            how.Items.Add(item);
        }
        section.Items.Add(how);

        // 「试一试」：这一条是给"想看看暗幕长什么样、点一下是不是真能开"的那次判断用的，
        // 不该让人先等满间隔。没起表时它是灰的，而灰的原因直接写在标题上（不让人按了才知道为什么）。
        var running = EyeRestService.IsRunning;
        var tryIt = new MenuFlyoutItem
        {
            Text = Locale.T(running ? "widget.health.tryIt" : "widget.health.tryItNeedsOn"),
            IsEnabled = running,
        };
        tryIt.Click += (_, _) =>
        {
            if (EyeRestService.Preview()) return;                 // 屏幕上已经有东西了，不需要再补一句话
            App.MainWindow?.ShowNotice(Locale.T("widget.health.previewFailed"), EyeRestService.IsResting
                ? Locale.T("widget.health.previewBlocked")
                : Locale.T("widget.health.previewNoBeat"));
        };
        section.Items.Add(tryIt);

        // 状态常驻一行：按完开关要立刻看得见"下一次是几点"，不然"按了没反应"与"生效了但没到点"分不开
        var next = EyeRestService.NextDueAt;
        section.Items.Add(new MenuFlyoutItem
        {
            Text = next is { } due
                ? Locale.Fmt("widget.health.nextDue", DateTimeText.Clock(due.ToLocalTime()))
                : enabled ? Locale.T("widget.health.notRunning") : Locale.T("widget.health.off"),
            IsEnabled = false,
        });

        var full = new MenuFlyoutItem { Text = Locale.T("widget.health.full") };
        // 直接落在「健康与诊断」页：跳进设置页却停在常规页，等于让人到了门口再自己找房间
        full.Click += (_, _) => App.MainWindow?.Present(true, SettingsTabIds.Health);
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
            Text = Locale.T("settings.widgets.addRow"),   // 与设置页那颗「添加组件」同一句话＝同一颗键（⛔ 另起一行迟早只改一处）
            Icon = new FontIcon { Glyph = "\uE710", FontSize = 14 },
        };
        foreach (var kind in WidgetStorage.AllKinds)
        {
            // 这里<b>留着</b>带图标那份（KindTitle＝"图标 空格 名字"）：菜单项没有别的元素画图标，
            // 与托盘同理。组件窗标题栏不是这种宿主——那边只许拿键（坑表 #337）。
            var add = new MenuFlyoutItem { Text = WidgetStorage.KindTitle(kind) };
            var captured = kind;
            add.Click += (_, _) => _ = _manager.AddInstanceAsync(captured);
            addSub.Items.Add(add);
        }
        menu.Items.Add(addSub);

        menu.Items.Add(new MenuFlyoutSeparator());
        var removeThis = new MenuFlyoutItem
        {
            Text = Locale.T("widget.menu.removeOne"),
            Icon = new FontIcon { Glyph = "\uE711", FontSize = 14 },   // Cancel（X），与其它图标视觉一致
        };
        removeThis.Click += (_, _) => _ = _manager.RemoveAsync(_instanceId);
        menu.Items.Add(removeThis);

        // 护眼 · 休息提醒（批次 WC-3，用户裁决"护眼建议和时钟组件结合"）：只有时钟组件挂这一节。
        if (_kind == WidgetKind.Clock) BuildEyeRestSection(menu);
        // 闹钟那一节挂在"内容自己带闹钟"的组件上（今天只有时钟），而不是再按类型判一次：
        // 类型判据与"有没有这套数据"是两件事，写成两处将来必然分岔（记忆 ⑧）。
        if (_alarms is { } alarms) BuildAlarmSection(menu, alarms);

        // 胶囊模式入口（Phase B）：受描述符 CanHideChrome 控制；Hidden 态标题栏不可见时仍可经根边框菜单切换
        menu.Items.Add(new MenuFlyoutSeparator());
        var canHide = WidgetRegistry.Default.TryGet(_kind, out var desc) && desc.CanHideChrome;
        _collapseMenuItem = new MenuFlyoutItem
        {
            Text = Locale.T("widget.chrome.collapse"),
            Icon = new FontIcon { Glyph = "\uE70E", FontSize = 14 }, // ChevronUp：收起
            IsEnabled = canHide,
        };
        _collapseMenuItem.Click += (_, _) => ToggleCompact();
        menu.Items.Add(_collapseMenuItem);

        _hideChromeMenuItem = new MenuFlyoutItem
        {
            Text = Locale.T("widget.chrome.hideFrame"),
            Icon = new FontIcon { Glyph = "\uE921", FontSize = 14 },   // Hide（有效字形，避免显示错误方框）
            IsEnabled = canHide,
        };
        _hideChromeMenuItem.Click += (_, _) => ToggleHidden();
        menu.Items.Add(_hideChromeMenuItem);

        // 隐私模式（B-10）：收起为胶囊时隐藏标题，避免胶囊泄露组件身份/内容
        var privacyItem = new ToggleMenuFlyoutItem
        {
            Text = Locale.T("widget.chrome.privacy"),
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
        var showAll = new MenuFlyoutItem { Text = Locale.T("widget.menuShowAll") };
        showAll.Click += (_, _) => _ = _manager.ShowAllAsync();
        var hideAll = new MenuFlyoutItem { Text = Locale.T("widget.menuHideAll") };
        hideAll.Click += (_, _) => _ = _manager.HideAllAsync();
        menu.Items.Add(showAll);
        menu.Items.Add(hideAll);

        menu.Items.Add(new MenuFlyoutSeparator());
        var main = new MenuFlyoutItem { Text = Locale.T("widget.menu.main") };
        main.Click += (_, _) => _manager.OpenMainWindow();
        var settings = new MenuFlyoutItem { Text = Locale.T("widget.menu.manage") };
        settings.Click += (_, _) => _manager.OpenWidgetSettings();
        menu.Items.Add(main);
        menu.Items.Add(settings);

        // 重命名本组件实例（名字显示在标题栏 / 胶囊标题上）
        var rename = new MenuFlyoutItem
        {
            Text = Locale.T("widget.menu.rename"),
            Icon = new FontIcon { Glyph = "\uE8AC", FontSize = 14 },   // Rename，与其它图标视觉一致
        };
        rename.Click += async (_, _) => await RenameAsync();
        menu.Items.Add(rename);

        // 每实例外观编辑（B-9）：材质/颜色/边框/圆角/文本缩放，可一键恢复全局
        var appearance = new MenuFlyoutItem
        {
            Text = Locale.T("widget.menu.appearance"),
            Icon = new FontIcon { Glyph = "\uE790", FontSize = 14 },
        };
        appearance.Click += async (_, _) => await EditAppearanceAsync();
        menu.Items.Add(appearance);

        // 布局方案：保存当前这一屏，或切换到已保存的布局（同一时刻只显示一套）
        menu.Items.Add(new MenuFlyoutSeparator());
        var saveLayout = new MenuFlyoutItem
        {
            Text = Locale.T("widget.menu.saveLayout"),
            Icon = new FontIcon { Glyph = "\uE78C", FontSize = 14 },
        };
        saveLayout.Click += (_, _) => _ = SaveLayoutByNameAsync();
        menu.Items.Add(saveLayout);

        var layouts = _manager.GetLayouts();
        if (layouts.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = Locale.T("widget.menu.applyLayout") };
            foreach (var l in layouts)
            {
                var id = l.Id;
                // 名字与摘要之间那个括号是<b>语言内容</b>（英文用半角），所以整句进表、两段当实参。
                var apply = new MenuFlyoutItem { Text = Locale.Fmt("widget.menu.nameWithSummary", l.Name, l.Summary) };
                apply.Click += (_, _) => _ = _manager.ApplyLayoutAsync(id);
                sub.Items.Add(apply);
            }
            menu.Items.Add(sub);
        }

        // 布局与数据快照（#53）：连组件数据一起存成不可变历史点；应用 = 回到那一刻（带自动回滚）。
        var saveSnapshot = new MenuFlyoutItem
        {
            Text = Locale.T("widget.menu.saveSnapshot"),
            Icon = new FontIcon { Glyph = "\uE787", FontSize = 14 },
        };
        saveSnapshot.Click += (_, _) => _ = SaveSnapshotByNameAsync();
        menu.Items.Add(saveSnapshot);

        var snapshots = _manager.GetSnapshots();
        if (snapshots.Count > 0)
        {
            var snapSub = new MenuFlyoutSubItem { Text = Locale.T("widget.menu.applySnapshot") };
            foreach (var s in snapshots)
            {
                var id = s.Id;
                var apply = new MenuFlyoutItem { Text = Locale.Fmt("widget.menu.nameWithSummary", s.Name, s.Summary) };
                // 快照应用刻意与「应用布局」一致：直接应用、不再二次确认，因为应用前会自动留回滚点。
                // 但回滚点没存成时 Apply 会中止（返回 false）——不静默吞掉，给一条提示说明"未应用"。
                apply.Click += async (_, _) =>
                {
                    if (!await _manager.ApplySnapshotAsync(id))
                        await ShowTipAsync(LocMessage.Of("widget.menu.applyFailed"), LocMessage.Of("widget.menu.applyFailedBody",
                            _manager.LastApplyError ?? LocMessage.Of("widget.menu.rollbackFailed")));
                };
                snapSub.Items.Add(apply);
            }
            menu.Items.Add(snapSub);
        }

        var manageSnapshots = new MenuFlyoutItem { Text = Locale.T("widget.menu.manageSnapshots") };
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
            title: LocMessage.Of("widget.menu.saveSnapshotTitle"),
            message: LocMessage.Of("widget.menu.saveSnapshotBody"),
            placeholder: LocMessage.Of("widget.menu.saveSnapshotHint"),
            primaryText: LocMessage.Of("dialog.save"),
            cancelText: LocMessage.Of("dialog.cancel"),
            owner: this);

        if (name is null) return;

        // CaptureSnapshotAsync 现任一条目取数失败即整体抛出——绝不能落一张静默缺数据的快照，
        // 故此处捕获并明确告知"保存失败"，避免用户误以为已备份。
        try
        {
            var saved = await _manager.CaptureSnapshotAsync(name);
            if (saved is null)
                await ShowTipAsync(LocMessage.Of("widget.menu.noneWidgets"), LocMessage.Of("widget.menu.noSnapshotContent"));
            else
                await ShowTipAsync(LocMessage.Of("widget.menu.snapshotSaved"),
                    LocMessage.Of("widget.menu.snapshotSavedBody", saved.Name, saved.Summary));
        }
        catch (Exception ex)
        {
            StarLog.Error("保存布局与数据快照失败", ex);
            await ShowTipAsync(LocMessage.Of("widget.menu.snapshotFailed"),
                LocMessage.Of("widget.menu.snapshotFailedBody", ex.Message));
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
            title: LocMessage.Of("widget.menu.saveLayoutTitle"),
            message: LocMessage.Of("widget.menu.saveLayoutBody"),
            placeholder: LocMessage.Of("widget.menu.saveLayoutHint"),
            primaryText: LocMessage.Of("dialog.save"),
            cancelText: LocMessage.Of("dialog.cancel"),
            owner: this);

        if (name is null) return;

        var saved = await _manager.SaveCurrentLayoutAsync(name);
        if (saved is null)
            await ShowTipAsync(LocMessage.Of("widget.menu.noneVisible"), LocMessage.Of("widget.menu.noLayoutContent"));
    }

    private async System.Threading.Tasks.Task ShowTipAsync(LocMessage title, LocMessage message)
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

    /// <summary>组件类型的默认标题那颗<b>键</b>——⛔ <b>不带图标</b>。
    /// <para>批次 db0306f（WI-2o）把这里换成 <c>WidgetStorage.KindTitle</c>（展示名＝"图标 空格 名字"），
    /// 而标题栏本来就有一颗 <c>WidgetGlyph</c> 在画图标，于是<b>同一排画出两颗图标</b>——"所有组件又出现两个图标"
    /// 就是这个形状。带图标的 <c>DisplayTitle</c> 是<b>托盘菜单</b>的需要（那里没有别的元素画图标），组件窗不是</para>
    /// <para>（坑表 #337：图标与名字各只许一个出口；同一个字符串当两处显示时，另一处的需要不许改写它。）</para></summary>
    private static string DefaultKindTitleKey(WidgetKind kind)
        => WidgetStorage.KindTitleKey(kind);

    /// <summary>
    /// 把显示名同步到标题栏文本与窗口标题（重命名后立即刷新）。
    /// <para>两条路必须分开（批次 WI-36）：<b>未重命名＝挂键</b>，换语言那一刻由 <c>LocText</c> 重贴；
    /// <b>重命名过＝写他起的名字并把那颗键摘掉</b>——挂着键，换语言就会把他的名字盖回类型名
    /// （坑表 #276 的镜像面：带值的字不许由静态键接管）。
    /// OS 那一层的 <c>Title</c>（Alt+Tab 读它）走 <c>LocText.SetWindowTitle</c>：WinUI 3 的
    /// <c>Window</c> <b>不是</b> <c>DependencyObject</c>，附带属性贴不上去（坑表 #338），所以它记在
    /// <c>LocText</c> 的第二张名单里——订阅点仍然只有那唯一一处，⛔ 不许在这里 <c>Locale.Changed +=</c>；
    /// 而名字那一层交出去的是<b>没渲染的</b> <see cref="LocMessage"/>——嵌套那句同样在贴上去那一刻才取词（#328 同族）。</para>
    /// </summary>
    private void ApplyTitle()
    {
        try
        {
            var renamed = !string.IsNullOrWhiteSpace(_config.Title);
            if (renamed)
            {
                LocText.SetKey(WidgetTitle, string.Empty);   // 摘注册项（顺带清字），再写他自己起的名字
                WidgetTitle.Text = _config.Title;
            }
            else
            {
                LocText.SetKey(WidgetTitle, DefaultKindTitleKey(_kind));
            }
            LocText.SetWindowTitle(this, "widget.win.windowTitle",
                renamed ? _config.Title! : (object)LocMessage.Of(DefaultKindTitleKey(_kind)));
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
            var def = Locale.T(DefaultKindTitleKey(_kind));   // 提示里的"默认名称"＝类型名，不带图标
            var input = await CenteredDialog.PromptAsync(
                title: LocMessage.Of("widget.menu.renameTitle"),
                message: LocMessage.Of("widget.menu.renameBody", def),
                placeholder: LocMessage.Of("common.rawValue", def),
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
