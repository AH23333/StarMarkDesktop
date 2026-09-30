#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// ItemCard 操作集中处理：打开条目 / 编辑笔记 / 编辑标签 / 隐藏 / 置顶 / 复制链接 / 复制图片 / 打开所在位置。
/// 供所有页面复用，避免重复实现 ContentDialog 逻辑。
/// 所有入口都有异常保护并写 StarLog——async void 中的未捕获异常会直接命中全局 UnhandledException。
/// </summary>
public static class ItemCardActions
{
    /// <summary>
    /// 条目标签被改动后触发（增/删/重设）。供搜索页/文件夹页订阅——
    /// 用户在内联编辑标签后，当前的条件搜索与浏览结果需要实时刷新（对齐扩展实时条件搜索）。
    /// 在 UI 线程触发（调用方均为 UI 事件处理器，async void 默认回到 UI 同步上下文）。
    /// </summary>
    public static event Action? ItemTagsChanged;

    public static IItemRepository GetRepo() => App.Services.GetRequiredItemRepository();

    /// <summary>
    /// 确保一条「实时源虚拟条目」（Everything 文件结果：Id=0、查询时从不入库）已登记进主库，返回其真实 Id；
    /// 已入库条目（Id&gt;0）原样返回其 Id。<b>只登记索引记录（复用同步口径的幂等 upsert），绝不移动 / 改名 / 删除磁盘上的实际文件。</b>
    /// 登记会把真实 Id 回填到 <paramref name="item"/>——其宿主 <see cref="ItemCardViewModel"/> 随即指向真实条目，
    /// 故紧随其后的置顶/隐藏/标签/笔记等按 Id 写库的操作得以生效。缺 (Source, SourceId) 业务键无法登记时返回 0。
    /// </summary>
    public static async Task<long> EnsureRecordedAsync(Item? item, CancellationToken ct = default)
    {
        if (item is null) return 0;
        if (item.Id > 0) return item.Id;
        return await GetRepo().RecordItemAsync(item, ct);
    }

    public static async void Open(XamlRoot xamlRoot, long itemId)
    {
        try
        {
            var repo = GetRepo();
            var item = await repo.GetByIdAsync(itemId, CancellationToken.None);
            if (item is null) return;
            // 剪贴板条目没有可启动的目标，对它"打开"＝把正文复制回剪贴板。各页/组件都从这一个入口进来，
            // 所以在这里收口一次即可；否则搜索页右键一条剪贴板记录点"打开"会静默无事发生。
            if (ClipboardPolicy.OpensAsCopy(item.Type, item.Uri)) { CopyUri(new ItemCardViewModel(item)); return; }
            if (!string.IsNullOrEmpty(item.Uri))
                await LauncherEx.OpenAsync(item.Uri);
        }
        catch (Exception ex)
        {
            StarLog.Error($"打开条目失败 (id={itemId})", ex);
        }
    }

    /// <summary>
    /// 按卡片视图模型打开条目。<b>未入库的实时源虚拟条目（Everything 本地文件结果 Id=0）库里必然查不到，
    /// 旧的"只按 Id 打开"对它们静默失效</b>——现象即"文件夹能开、文件点了没反应"。Id≤0 时直接按其 Uri 走
    /// <see cref="LauncherEx"/>（ShellExecute：文件交系统关联程序，无关联则由 Windows 提示选应用；文件夹在资源管理器打开）；
    /// 已入库条目仍按 Id 取最新库值，行为不变。
    /// </summary>
    public static void Open(XamlRoot xamlRoot, ItemCardViewModel? vm)
    {
        if (vm is null) return;
        if (vm.Id <= 0) { _ = LauncherEx.OpenAsync(vm.Uri); return; }
        Open(xamlRoot, vm.Id);
    }

    /// <summary>打开所在位置：本地文件 → 资源管理器定位；其余类型无位置概念。</summary>
    public static async void OpenLocation(ItemCardViewModel vm)
    {
        try
        {
            if (!vm.HasOpenLocation) return;
            // 还原规则（两类互补的 file:// 生产者）归 LocalFileIdentity 一颗，别再在这里只写一半：
            // 只试原始形态时，剪贴板图片行那类 percent 编码的 Uri 会还原成带 %XX 的假路径 ⇒ 磁盘上不存在 ⇒ 静默不定位。
            // 本宿主的判定留在本地：① 不含引号（这条启动点不经 LaunchGuard.IsAllowedScheme，路径直拼进 explorer.exe
            // 命令行、被子进程重切参数；被污染备份/快照的 uri 可携双引号越界注入额外参数 → 任意本地程序被执行）；
            // ② 磁盘上真有这个东西（合法「打开所在位置」恒满足；对已删除/伪造路径 explorer 本就无意义）。
            if (!LocalFileIdentity.TryExistingPath(vm.Uri, IsShellSelectTarget, out var localPath)) return;
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{localPath}\"");
        }
        catch (Exception ex)
        {
            StarLog.Error($"打开所在位置失败 (id={vm.Id}, uri={vm.Uri})", ex);
        }
        await Task.CompletedTask;
    }

    /// <summary>复制链接/路径到剪贴板（file:// 转本地路径）。</summary>
    public static async void CopyUri(ItemCardViewModel vm)
    {
        try
        {
            var text = vm.Uri;
            if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // 复制出去的东西要"粘进资源管理器就能用"：先取磁盘上真存在的那个候选——
                // 剪贴板图片行/快捷启动那些 percent 编码形态，只试原始形态会复制出一条带 %XX 的假路径。
                // 两个候选都不存在时照抄原始形态（保留 '#'/%，与改道前一致），不猜、不静默造路径。
                if (LocalFileIdentity.TryExistingPath(text, ExistsOnDisk, out var real)) text = real;
                else if (LocalFileIdentity.TryPathFromUri(text, out var localPath)) text = localPath;
            }
            else if (ClipboardPolicy.OpensAsCopy(vm.Type, text))
            {
                // 剪贴板历史条目没有 URI，正文才是"可复制的东西"。这里若照抄空串，
                // 菜单点下去毫无反应（还静默），等于一个看着能点其实无效的动作。
                text = vm.Description ?? string.Empty;
            }
            if (text.Length == 0) return;

            // 先登记回声再写剪贴板：不登记的话，开着剪贴板历史时"在应用里复制一次"会被自己再记一条，
            // 表现为"我只是翻了翻列表，它自己重排了"。顺序不能反——写入是同步的，登记慢了就可能已被采集读到。
            App.NoteClipboardOwnWrite(text);

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            // Flush：SetContent 默认是延迟渲染，主窗口/组件被挂起或进程退出时内容会丢。
            // 用户点了"复制"就该在剪贴板里，不靠应用还活着。
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
        catch (Exception ex)
        {
            StarLog.Error($"复制链接失败 (id={vm.Id})", ex);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 「复制图片」：把<b>位图数据</b>放进剪贴板。它与 <see cref="CopyUri"/> 是两件事，且这区别是用户看得见的——
    /// 交路径时画图/Word/浏览器粘出来的是一串文字，交位图才粘出一张图（真机坏法见 ClipIMG-2c：
    /// 把 <c>file://</c> 递给 <c>SetBitmap</c>，系统按"复制了一个文件"呈现，症状就是"只能复制路径"）。
    /// <para><b>刻意自包含、不走页面事件</b>（同 <see cref="DeleteClipboard"/>）：卡片被剪贴板页 / 文件夹树 /
    /// 搜索页 / 组件行十余处复用，靠宿主订阅的话，没订阅的那几处又是一个"看着能点其实没反应"的菜单项。</para>
    /// <para>读盘与转码整段离 UI 线程：文件夹树里挑中的可能是一张几十 MB 的 TIFF。</para>
    /// </summary>
    public static async void CopyImage(ItemCardViewModel vm)
    {
        try
        {
            // 判据只有一份：菜单上不该出现这一项时，这里也不动剪贴板（否则宿主页面自己挂的按钮会绕过规则）。
            if (!vm.CanCopyAsImage) return;

            // 历史行走 TryReadEntryImage（"是哪个文件"要过 ClipAssets 的名字名册——备份文件里回来的
            // Uri 可以是任意字符串）；文件行按路径问那一条读文件的入口。两条路共用同一个解码原语。
            var (png, frame, why) = vm.IsClipboardImageRow
                ? await Task.Run(() => ReadEntryImage(vm), CancellationToken.None)
                : await Task.Run(() => ClipboardImageStore.TryReadFileAsPngAsync(
                    LocalFileIdentity.TryExistingPath(vm.Uri, ExistsOnDisk, out var localPath) ? localPath : null,
                    CancellationToken.None), CancellationToken.None);

            if (png is null)
            {
                // 坏消息要说得出为什么、并落在用户能做的那件事上（P-54 同口径）：
                // 只回一句"复制失败"，他只能去猜是程序坏了还是这张图坏了。
                App.MainWindow?.ShowError("没能复制图片", why ?? "这张图读不出来。");
                return;
            }

            // 先登记像素回声再写剪贴板，顺序不能反：WM_CLIPBOARDUPDATE 在 SetContent 之后就到，
            // 登记晚一步就挡不住那一帧——症状是"每右键复制一次图片，剪贴板历史多出一条"。
            App.NoteClipboardOwnImageWrite(frame.Bgra);

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference
                .CreateFromStream(ClipboardImageStore.StreamOf(png)));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();   // 不 Flush，窗口一挂起内容就没了
        }
        catch (Exception ex)
        {
            StarLog.Error($"复制图片失败 (id={vm.Id}, uri={vm.Uri})", ex);
            App.MainWindow?.ShowError("没能复制图片", ex.Message);
        }
    }

    /// <summary>把一条图片历史/图片文件贴到桌面上（ClipIMG-P3：接合贴图管线，收官格）。
    /// <para>与 <see cref="CopyImage"/> 是两个动作，共用同一个读与同一套读不出的说法：那颗交像素给
    /// 剪贴板（所以要登记回声——不登记，每复制一次历史就多一条自采集），这颗<b>不经剪贴板</b>，
    /// 没有回声问题；成功也<b>不在此处另报一句</b>——"已钉住（共 N 张，上限 M 张）"由 PinManager
    /// 的通知卡统一说话，这里再报就成同一件事两种措辞。</para>
    /// <para>判据只有一份："这类行有没有这个动作"仍是 <c>CanCopyAsImage</c>（菜单两处与这里都读它），
    /// 这颗只再多问一句"这张的像素这会儿还在不在"（<c>CanPinAsImage</c>，用的是卡片构造时已经算好的那个位，
    /// 不在菜单构建里 stat 文件）。文件真不在时那条路仍由读盘那一步带着原因报告（P-54 同口径）。</para>
    /// <para>摆放＝光标屏工作区居中（判据在 <c>ItemCardPolicy.CenteredPlacement</c>，可单测）；
    /// 探测不到光标屏时老实退回 (0,0) 并写日志——比"贴不出去"更贴近用户按下的意图，
    /// 那张窗仍可拖走，而拒绝执行没有第二次机会。</para></summary>
    public static async void PinImageToDesktop(ItemCardViewModel vm)
    {
        try
        {
            if (!vm.CanPinAsImage) return;

            var (png, frame, why) = vm.IsClipboardImageRow
                ? await Task.Run(() => ReadEntryImage(vm), CancellationToken.None)
                : await Task.Run(() => ClipboardImageStore.TryReadFileAsPngAsync(
                    LocalFileIdentity.TryExistingPath(vm.Uri, ExistsOnDisk, out var localPath) ? localPath : null,
                    CancellationToken.None), CancellationToken.None);

            if (png is null || frame.Width <= 0 || frame.Height <= 0)
            {
                App.MainWindow?.ShowError("没能贴出这张图", why ?? "这张图读不出来。");
                return;
            }

            var cursorWork = WindowInterop.MonitorWorkAreaAtCursor();
            if (cursorWork is null) StarLog.Info("[ClipIMG-P3] 光标所在屏没探到，贴图落 (0,0)");
            var work = cursorWork?.Work;
            var placement = ItemCardPolicy.CenteredPlacement(
                work?.X ?? 0, work?.Y ?? 0, work?.Width ?? frame.Width, work?.Height ?? frame.Height,
                frame.Width, frame.Height);

            // 交出去即完：PinManager 自己判上限、自己广播，那扇窗本身就是编辑器（PN 口径）。
            ScreenshotService.PinPixels(frame.Bgra, frame.Width, frame.Height, placement);
        }
        catch (Exception ex)
        {
            StarLog.Error($"剪贴板图片贴到桌面失败 (id={vm.Id}, uri={vm.Uri})", ex);
            App.MainWindow?.ShowError("没能贴出这张图", ex.Message);
        }
    }

    /// <summary>把历史行的读盘入口折成与文件侧同一个形状（两条路的失败都得带得出原因，不能一个抛一个返回）。</summary>
    private static (byte[]? Png, ClipboardPayload.ImageFrame Frame, string? Reason) ReadEntryImage(ItemCardViewModel vm)
    {
        var ok = ClipboardImageStore.TryReadEntryImage(vm.GetItem(), out var bytes, out var frame, out var reason);
        return ok ? (bytes, frame, null) : (null, default, reason);
    }

    /// <summary>
    /// 永久删除一条剪贴板历史（单条正文可再复制，故不设确认框；"清空全部"那条才有确认）。
    /// <b>刻意自包含、不走页面事件</b>：卡片被搜索页 / 标签格 / 组件行等十余处复用，
    /// 若靠宿主订阅，凡是没订阅的那几处就又是一个"看着能点其实没反应"的菜单项。
    /// 成功不需要额外反馈（数据广播会让这一行自己消失）；<b>失败必须说出来</b>——
    /// "点完什么都没发生"与"这条已经没了"在界面上长得一样，是最难自证的一种歧义。
    /// </summary>
    public static async void DeleteClipboard(ItemCardViewModel vm)
    {
        try
        {
            if (vm.Id <= 0)
            {
                App.MainWindow?.ShowError("删除失败", "这条历史还没入库，没有可删除的记录。");
                return;
            }
            var removed = await GetRepo().DeleteClipboardEntryAsync(vm.Id, CancellationToken.None);
            if (!removed)
                App.MainWindow?.ShowError("删除失败", "这条记录已经不在历史里了（可能刚被清空或被新内容挤掉）。");
        }
        catch (Exception ex)
        {
            StarLog.Error($"删除剪贴板历史失败 (id={vm.Id})", ex);
            App.MainWindow?.ShowError("删除失败", ex.Message);
        }
    }

    public static async void TogglePin(ItemCardViewModel vm)
    {
        try
        {
            // 未入库的实时源虚拟条目（Everything，Id=0）没有可写 pinned 的主库行：先按路径登记拿真实 Id。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;
            var newState = !vm.IsPinned;
            var word = newState ? "置顶" : "取消置顶";
            // 库里没那一行时不许翻旗：从前它翻完就没人管，150 ms 后重载把它弹回去，
            // 用户看到的只是"点了一下没反应"（P-40）。
            if (!await GetRepo().SetPinnedAsync(vm.Id, newState, CancellationToken.None))
            {
                App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.RowGone(word));
                return;
            }
            vm.SetPinned(newState);
        }
        catch (Exception ex)
        {
            StarLog.Error($"置顶切换失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>把 XamlRoot 解析为发起窗口（用于弹窗居中显示器）。无可靠映射时回落到主窗口。</summary>
    private static Window? ResolveOwner(XamlRoot? xamlRoot) => WindowInterop.ResolveWindow(xamlRoot, App.MainWindow);

    public static async void EditNote(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var box = new TextBox
            {
                Text = vm.Notes ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 100,
                PlaceholderText = "写下笔记...",
            };
            box.Select(box.Text.Length, 0);

            // 统一走外部居中窗口（非 ContentDialog），自动按用户主题着色、可拖动、不可重复。
            var result = await CenteredDialog.ShowContentAsync(
                "编辑笔记", box, owner: ResolveOwner(xamlRoot),
                dedupeKey: $"editnote:{vm.Id}", width: 460, height: 260,
                primaryText: "保存", cancelText: "取消");

            if (result != CenteredDialog.HostedDialogResult.Committed) return;
            var text = box.Text.Trim();
            // 仅在真正改动时记一条「修改」事件——空保存/重复保存不污染活动流（#51）。
            var oldNotes = vm.Notes ?? string.Empty;
            if (string.Equals(oldNotes, text, StringComparison.Ordinal)) return;
            // 虚拟条目（Everything，Id=0）无主库行可写 notes：先按路径登记拿真实 Id，再落笔。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;
            if (!await GetRepo().SetNoteAsync(vm.Id, text, CancellationToken.None))
            {
                // 笔记没落到那一行上：既不改笔记框，也不往活动流记一笔"修改"（那是条假事件，比没改更难解释）。
                App.MainWindow?.ShowError(StateWriteNotice.Title("保存笔记"), StateWriteNotice.RowGone("保存笔记"));
                return;
            }
            vm.ApplyNotes(string.IsNullOrWhiteSpace(text) ? null : text);
            await LogModify(vm);
        }
        catch (Exception ex)
        {
            StarLog.Error($"编辑笔记失败 (id={vm.Id})", ex);
        }
    }

    public static async void EditTags(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var repo = GetRepo();
            var editor = new Controls.TagEditor
            {
                AllTags = (await repo.GetAllTagsAsync(CancellationToken.None)).Select(t => t.Name).ToList(),
            };
            editor.SetTags(vm.Tags);

            // 统一走外部居中窗口（非 ContentDialog），自动按用户主题着色、可拖动、不可重复。
            var result = await CenteredDialog.ShowContentAsync(
                "编辑标签", editor, owner: ResolveOwner(xamlRoot),
                dedupeKey: $"edittags:{vm.Id}", width: 460, height: 460,
                primaryText: "保存", cancelText: "取消");

            if (result != CenteredDialog.HostedDialogResult.Committed) return;

            // 差集、索引重建、活动流水、组件通知全部交给仓储一次做完（PA-3）。
            // 逐条 Add/RemoveTagAsync 的写法里，"加五个删三个"要开十六次库，
            // 而且中间每次重建 search_text 用的都是半成品标签集——中途来一次搜索就会少命中。
            var desired = editor.Tags.ToList();

            // 虚拟条目（Everything，Id=0）无主库行可挂标签：想打标签就得先按路径登记拿真实 Id。
            // 判据从"有净改动"变成"清单非空"是等价的：虚拟条目在库里没有行，现状必然是空集。
            if (vm.Id == 0 && desired.Count > 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;

            var edit = await repo.SetItemTagsAsync(vm.Id, desired, CancellationToken.None);
            vm.ApplyTags(edit.FinalTags.ToArray());
            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"编辑标签失败 (id={vm.Id})", ex);
        }
    }

    public static async Task<bool> ToggleHidden(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        // 兄弟方法（EditTags/RemoveTag/AddTag）均有 try/catch，此前唯 ToggleHidden 裸奔：
        // 它被多个 async void 的 Card_HideRequested 直接 await（SearchPage / ClipboardPage /
        // FolderTreePage / TagsPage），SetHiddenAsync 抛 SqliteException 会顺着 async void 冒到
        // UI 线程成未处理异常。改为兜异常并回传"未改变"的当前状态（调用方据此不再做移除/刷新）。
        try
        {
            // 虚拟条目（Everything，Id=0）无主库行可写 hidden：先按路径登记拿真实 Id，「隐藏」才落得下去。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return vm.IsHidden;
            var repo = GetRepo();
            var newState = !vm.IsHidden;
            var word = newState ? "隐藏" : "取消隐藏";
            if (!await repo.SetHiddenAsync(vm.Id, newState, CancellationToken.None))
            {
                // 回传"未改变"（调用方据此不摘行、不整表重载），并把原因当面对他说（P-40）。
                App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.RowGone(word));
                return vm.IsHidden;
            }
            vm.SetHidden(newState);
            return newState;
        }
        catch (Exception ex)
        {
            StarLog.Error($"切换隐藏状态失败 (id={vm.Id})", ex);
            return vm.IsHidden;
        }
    }

    public static async void RemoveTag(XamlRoot xamlRoot, ItemCardViewModel vm, string tag)
    {
        try
        {
            var unlinked = await GetRepo().RemoveTagAsync(vm.Id, tag, CancellationToken.None);
            // 挂接本来就不在库里时芯片照样收（那才是现状），但**不往活动流记一笔"修改"**——
            // 库里这次什么都没发生（P-40）。
            vm.ApplyTags(vm.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (unlinked) await LogModify(vm);
            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"移除标签失败 (id={vm.Id}, tag={tag})", ex);
        }
    }

    /// <summary>
    /// 快速添加标签。与 <see cref="EditTags"/> 共用 TagEditor，因此同样享有
    /// 历史标签建议（避免把 ai 打成 AI 造成同义标签分裂）。
    /// </summary>
    public static async void AddTag(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var repo = GetRepo();
            var editor = new Controls.TagEditor
            {
                AllTags = (await repo.GetAllTagsAsync(CancellationToken.None)).Select(t => t.Name).ToList(),
            };

            // 统一走外部居中窗口（非 ContentDialog），自动按用户主题着色、可拖动、不可重复。
            var result = await CenteredDialog.ShowContentAsync(
                "快速添加标签", editor, owner: ResolveOwner(xamlRoot),
                dedupeKey: $"addtag:{vm.Id}", width: 460, height: 460,
                primaryText: "添加", cancelText: "取消");

            if (result != CenteredDialog.HostedDialogResult.Committed) return;

            var desired = editor.Tags.ToList();
            if (desired.Count == 0) return;

            var current = await repo.GetTagsForItemAsync(vm.Id, CancellationToken.None);

            // 虚拟条目（Everything，Id=0）无主库行可挂标签：先按路径登记拿真实 Id。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;

            // 差集与"要不要记活动"都在 TagItemsAsync 里判（PA-1）：这里再算一遍就是第二套判据，
            // 而两处判据一旦分叉，就会出现"卡片上写着加了、库里其实没加"。
            await repo.TagItemsAsync(new[] { new ItemTagAssignment(vm.Id, desired) }, CancellationToken.None);

            vm.ApplyTags(current.Concat(desired).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"添加标签失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>
    /// 记一条用户主动「修改」事件（笔记 / 标签增删）。活动流只反映用户操作，不含同步/种子（#51）。
    /// item_key 用条目的 (source:source_id) 作为业务键，便于后续按条目聚合。
    /// </summary>
    private static async Task LogModify(ItemCardViewModel vm)
    {
        try
        {
            var it = vm.GetItem();
            var key = !string.IsNullOrEmpty(it.Source) ? $"{it.Source}:{it.SourceId}" : null;
            await GetRepo().LogActivityAsync(ActivityKind.ItemModify, key, vm.Title, vm.Uri, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error($"记录修改活动失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>「这台机器上确实放着这个东西」——打开／定位／复制三类动作共用的磁盘判定（目录也算，文件也算）。</summary>
    private static bool ExistsOnDisk(string path) => System.IO.File.Exists(path) || System.IO.Directory.Exists(path);

    /// <summary>
    /// 「打开所在位置」的宿主判定：<b>两个候选都要过这两道</b>——
    /// ① <see cref="LaunchGuard.IsSafeShellSelectTarget"/>（这条启动点不经协议白名单，路径直拼进
    ///    <c>explorer.exe</c> 命令行、被子进程重切参数 → 含引号者可越界注入额外参数）；
    /// ② 磁盘上真有这个东西（对已删除／伪造路径，explorer 本就无意义）。
    /// </summary>
    private static bool IsShellSelectTarget(string path) => LaunchGuard.IsSafeShellSelectTarget(path) && ExistsOnDisk(path);
}
