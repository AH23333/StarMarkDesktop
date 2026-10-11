#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using StarMark.Abstractions.I18n;

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——备份与恢复这一头：选文件（原生对话框与兜底两条路）、导出导入、还原、删除与打开目录。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    /// <summary>调起 WinRT 选择器；返回 null 表示用户取消。（提权下抛 InvalidOperationException＝对话框宿主不可用。）</summary>
    private static async Task<Windows.Storage.IStorageItem?> PickAsync(
        Windows.Storage.Pickers.FileOpenPicker picker)
    {
        try { return await picker.PickSingleFileAsync(); }
        catch (Exception ex)
        {
            StarLog.Error("文件选择器调起失败（多为提权进程跨完整性级别访问系统对话框宿主被拦）", ex);
            throw new InvalidOperationException(PickerBlockedHint.Render(), ex);
        }
    }

    private static async Task<Windows.Storage.IStorageItem?> PickAsync(
        Windows.Storage.Pickers.FileSavePicker picker)
    {
        try { return await picker.PickSaveFileAsync(); }
        catch (Exception ex)
        {
            StarLog.Error("文件保存对话框调起失败（多为提权进程跨完整性级别访问系统对话框宿主被拦）", ex);
            throw new InvalidOperationException(PickerBlockedHint.Render(), ex);
        }
    }

    /// <summary>
    /// 系统文件对话框那一条路（普通权限会话）。**类型表必须与"实际会写出去/读进来的载体"一致**：
    /// 只列 <c>.json</c> 时，导入框里"刚导出的那一份 .zip"根本看不见（等于这个功能在普通权限下用不了），
    /// 导出框里挑到的名字也必然与实际落盘的那个差一个扩展名。
    /// 建议名的扩展名已经跟着那颗开关走（见 <c>ExportBackup_Click</c>），这里就按它来定类型表。
    /// </summary>
    private static async Task<string?> PickNativeAsync(bool save, string fileName)
    {
        var hwnd = WindowInterop.GetHwnd(App.MainWindow!);
        if (save)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, hwnd);
            var carriesContainer = string.Equals(Path.GetExtension(fileName),
                BackupContainer.ContainerExtension, StringComparison.OrdinalIgnoreCase);
            var primary = carriesContainer ? BackupContainer.ContainerExtension : BackupContainer.ManifestExtension;
            var secondary = carriesContainer ? BackupContainer.ManifestExtension : BackupContainer.ContainerExtension;
            // 两种载体都列进下拉（默认那一项跟着开关走）：只给一种时，用户想反过来挑的那个名字会被对话框拦下，
            // 而落盘的扩展名由内容决定——挑不到、写了另一个名字，两边就永远对不上。
            picker.FileTypeChoices.Add(CarrierLabel(primary).Render(), new[] { primary });
            picker.FileTypeChoices.Add(CarrierLabel(secondary).Render(), new[] { secondary });
            picker.DefaultFileExtension = primary;
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(fileName);
            return (await PickAsync(picker))?.Path;
        }
        var open = new FileOpenPicker();
        InitializeWithWindow.Initialize(open, hwnd);
        // 两种都列：只列 .json 的导入框里，唯一带图片的那一份是不存在的文件（这个功能在普通权限下等于没做）。
        open.FileTypeFilter.Add(BackupContainer.ManifestExtension);
        open.FileTypeFilter.Add(BackupContainer.ContainerExtension);
        return (await PickAsync(open))?.Path;
    }

    /// <summary>
    /// 对话框里那两项的标签（说的是"带不带图片本体"，不是"哪种压缩格式"——store 不压缩，别提"压缩"）。
    /// <para>交的是身份，<b>取词点在调起对话框那一行</b>：那张下拉是即时消费的一次性 surface（不像状态行那样长命），
    /// 递给 <c>FileTypeChoices</c> 的必须是字符串，所以这里 <c>.Render()</c> 是合规出口（✅<see cref="PickerBlockedHint"/> 同一形状）。</para>
    /// </summary>
    private static LocMessage CarrierLabel(string extension)
        => LocMessage.Of(extension == BackupContainer.ContainerExtension
            ? "settings.data.backup.carrierZip" : "settings.data.backup.carrierManifest");

    /// <summary>
    /// 取得备份文件路径。普通权限会话仍用系统选择器（用户熟悉、能浏览）；提权会话里宿主调不通，
    /// 直接退回应用内输入框——**不让用户为了备份去关开关、降权限、重启**。
    /// 输入非法时带着原因再问一次（就地改正），而不是失败退出后再点一遍。
    /// </summary>
    private static async Task<string?> RequestBackupPathAsync(bool save, string suggestedPath)
    {
        if (!Privilege.IsElevated())
        {
            try { return await PickNativeAsync(save, Path.GetFileName(suggestedPath)); }
            catch (InvalidOperationException) { /* 宿主不可用（不止提权一种成因）：落到输入框 */ }
        }

        LocMessage? error = null;
        var text = suggestedPath;
        while (true)
        {
            var input = await CenteredDialog.PromptAsync(
                save ? LocMessage.Of("settings.data.exportTo") : LocMessage.Of("settings.data.importFrom"),
                // 那句"原因话"从今天起也住在表里（批次 WI-62）：Core 的 `BackupPathPolicy` 交回的是「键＋实参」，
                // 所以这里⛔ 再走 `common.rawValue` 那道门——那道门只装<b>数据</b>，装句子就是第二个措辞出处（✅WI-61d 片1 定口径）。
                message: error ?? PickerBlockedHint,
                placeholder: LocMessage.Of("settings.data.pathExample", suggestedPath),
                defaultText: text,
                primaryText: save ? LocMessage.Of("settings.data.export") : LocMessage.Of("settings.data.import"),
                owner: App.MainWindow);
            if (input is null) return null;   // 取消

            var (path, validation) = save
                ? BackupPathPolicy.ForExport(input, BackupService.SnapshotDirectory)
                : BackupPathPolicy.ForImport(input);
            if (validation is null) return path;

            // 校验不过就带着原因重问，且保留用户刚敲的内容——他要改的只是一个字符，不该从头再来。
            error = validation;
            text = input;
        }
    }

    /// <summary>备份目录里最近改动过的一份 .json（目录不存在/被占用时返回 null，不打断导入流程）。</summary>
    private static string? NewestBackupPath()
    {
        try
        {
            var dir = BackupService.SnapshotDirectory;
            if (!Directory.Exists(dir)) return null;
            // 两种载体一起认：带图片的那份导出是 .zip，只认 .json 会让"最近一份"回退到旧的那份，
            // 用户在导入框里看到的默认值就成了一个几周前的备份。
            return new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => BackupContainer.IsBackupPath(f.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            // 只是"默认值取不到"，不是失败：用户照样能手输路径，故只记一行不弹提示。
            StarLog.Warn($"扫描备份目录失败，导入对话框将退回目录本身作默认值：{ex.Message}");
            return null;
        }
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        ViewModel.IsBackupBusy = true;      // 必须覆盖"弹对话框"阶段：否则等待期间可重复点，多个 picker 并存在提权下更是必挂
        try
        {
            // 先读开关：它同时决定建议名的扩展名与走哪条导出路。
            // 两条路是两个具名方法，不是一颗布尔参数——含义只写在被调方时，接线处写反是必然风险。
            var carry = ViewModel.BackupClipboardImagesEnabled;

            // 默认落在备份目录（那里已有"恢复前快照"），用户回车即接受，不必从 C:\ 一路敲过来。
            // 建议名里的扩展名跟着开关走：开着却提示 .json，用户会以为导出的是纯清单。
            var suggested = Path.Combine(BackupService.SnapshotDirectory,
                $"starmark-backup-{DateTimeText.BackupStamp(DateTimeOffset.UtcNow)}"
                + (carry ? BackupContainer.ContainerExtension
                         : BackupContainer.ManifestExtension));
            var target = await RequestBackupPathAsync(save: true, suggested);
            if (target is null) return;

            var written = carry
                ? await Task.Run(() => _backup.ExportWithClipImagesAsync(target, CancellationToken.None), CancellationToken.None)
                : null;
            if (written is not null)
            {
                // 那一行的四个读数各是一句独立的话（外层句子留三颗可选槽，没出现的那一档交 null＝格式化时那一格自己变空）；
                // 字节数与路径都是<b>数据</b> ⇒ 走实参（✅P-178 口径，⛔ 抄进句子）。
                ViewModel.BackupStatus = LocMessage.Of(
                    "settings.data.backup.exportedWith", written.Path, written.ItemCount,
                    written.ClipImages > 0
                        ? LocMessage.Of("settings.data.backup.exportedImages", written.ClipImages,
                            StarMark.Abstractions.Clipboard.ClipAssets.DescribeBytes(written.ClipImageBytes))
                        : LocMessage.Of("settings.data.backup.exportedNoImages"),
                    written.MissingImages > 0 ? LocMessage.Of("settings.data.backup.exportedMissing", written.MissingImages) : null,
                    written.FailedImages > 0 ? LocMessage.Of("settings.data.backup.exportedFailed", written.FailedImages) : null);
            }
            else
            {
                // 报实际写出去的那个名字：开关关着时若用户手输了 x.zip，那份文件仍是纯清单，
                // 名字会被改成 x.json（一个叫 .zip 的 JSON 会让下一个读它的人以为它坏了）。
                var plain = await Task.Run(() => _backup.ExportToFileAsync(target, CancellationToken.None));
                ViewModel.BackupStatus = LocMessage.Of("settings.data.backup.exportedPlain", plain);
            }
            ViewModel.RefreshBackups();     // 刚导出那份必须立刻出现在下面列表里，否则用户会以为没写成
        }
        catch (Exception ex)
        {
            // 导出这条路上异常自己那句话本来就是<b>数据</b>（.NET 的原文，⛔ 没有句子可交表）⇒ 走 `common.rawValue` 那道门，
            // 屏幕上那行与改之前逐字相同（⛔ 顺手给它加一句"导出失败："＝本批没说要改它）。
            ViewModel.BackupStatus = LocMessage.Of("common.rawValue", ex.Message);
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        ViewModel.IsBackupBusy = true;      // 同导出：选择器阶段也要占住忙碌位，避免重复点击叠出第二个 picker
        try
        {
            // 默认指向最近一份备份（导入最常就是"回到上一次"），没有就直接给备份目录当输入起点。
            var suggested = NewestBackupPath()
                ?? BackupService.SnapshotDirectory + Path.DirectorySeparatorChar;
            var source = await RequestBackupPathAsync(save: false, suggested);
            if (source is null) return;
            await ImportFromPathAsync(source);
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = LocMessage.Of("settings.data.backup.importFailed", ex.Message);
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    /// <summary>列表里某一行的「恢复这份 / 用它回滚」：跳过选择器，直接走与导入完全相同的那条链。</summary>
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: BackupService.BackupFile file }) return;
        ViewModel.IsBackupBusy = true;
        try
        {
            await ImportFromPathAsync(file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = LocMessage.Of("settings.data.backup.restoreThrew", ex.Message);
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    /// <summary>
    /// 列表里某一行的「删除」：先确认、再删，成功与失败都要有一行回执。
    /// <para>确认框里必须带上"这一份属于哪一类"的代价说明——三种类删掉之后的后果不同
    /// （自动件还会再生成，手动件与恢复前快照不会），只看文件名分不出来。</para>
    /// <para>真正的路径校验在 <see cref="BackupService.DeleteBackup"/> 里（Core 那道闸）：
    /// 这里传的 <c>Tag</c> 来自目录扫描，但列表可能已经过期，不能当成"必然是备份件"。</para>
    /// </summary>
    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: BackupService.BackupFile file }) return;
        var confirm = await CenteredDialog.ConfirmAsync(
            LocMessage.Of("settings.data.deleteBackupTitle"),
            // 句子里那两个换行⛔ 抄进表（规则 #100：表里不许有控制字符，换行是布局）⇒ 交成实参 {4}
            LocMessage.Of("settings.data.deleteBackupBody", file.FileName,
                StarMark.Abstractions.FileSizeText.Human(file.LengthBytes),
                DateTimeText.Minute(file.ModifiedUtc.ToLocalTime()),
                BackupRow.DeleteWarningFor(file.Kind), "\n"),
            LocMessage.Of("dialog.delete"), LocMessage.Of("dialog.cancel"),
            owner: App.MainWindow, dedupeKey: "deletebackup-" + file.FileName);
        if (!confirm) return;
        ViewModel.BackupStatus = BackupService.DeleteBackup(file.Path);
        ViewModel.RefreshBackups();
    }

    /// <summary>打开备份目录（备份就在本机，"去看一眼/自己拷走"不该让用户去地址栏敲路径）。</summary>
    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = BackupService.SnapshotDirectory;
            Directory.CreateDirectory(dir);     // 首次使用时目录可能还没建；开着空目录也比报错有用
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = LocMessage.Of("settings.data.backup.openFolderFailed", ex.Message);
            StarMark.Abstractions.StarLog.Warn($"打开备份目录失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 把两句已交表的话<b>按当前语言叠成一行一句</b>：连接用的换行由<b>实参</b>递进来（⛔ 抄进表＝规则 #100：
    /// 表里不许有控制字符，换行是布局），而两半的<b>顺序</b>由那颗骨架键管（✅WI-61c 那颗 <c>autoBackupSentence</c> 同一口径：
    /// 在 C# 里用 <c>+</c> 拼成品字＝语序被定死在中文那一侧）。<paramref name="tail"/> 为 null 就等于"那一档没有话要说"。
    /// </summary>
    private static LocMessage Stack(LocMessage head, LocMessage? tail, string gap = "\n")
        => tail is null ? head : LocMessage.Of("settings.data.backup.lineJoin", head, tail, gap);

    /// <summary>
    /// 从一份已知路径导入。<b>「导入备份…」与列表里的「恢复这份」共用这一条</b>——
    /// 两条路径若各写一遍，摘要/合并-覆盖确认/恢复前快照这些护栏很容易只存在于其中一条。
    /// 调用方负责 <see cref="SettingsPageViewModel.IsBackupBusy"/> 的置位与异常兜底。
    /// </summary>
    private async Task ImportFromPathAsync(string source)
    {
        // 备份文件可达数十 MB：整份读盘 + 反序列化必须在后台线程做（Microsoft.Data.Sqlite 与
        // File/Json 都是同步实现，await 并不让出），否则"点导入备份"就是先冻住整个界面几秒。
        BackupEnvelope env;
        try
        {
            env = await Task.Run(() => BackupService.ReadAsync(source, CancellationToken.None));
        }
        catch (BackupFormatException ex)
        {
            // 读 <c>Reason</c> 而不是 <c>Message</c>：后者从 WI-62 起只装键名（给日志 grep 用），
            // 屏幕上那一行必须按<b>现在</b>的语言取词（✅WI-57 那颗"设置页是长命宿主"的界）。
            ViewModel.BackupStatus = ex.Reason;
            return;
        }

        // 摘要直接从已解析的 env 算：旧代码再调 Peek(file.Path)，等于把整份备份第二次读盘+反序列化。
        var summary = BackupService.Summarize(env);
        var summaryBlock = Stack(
            LocMessage.Of("settings.data.backup.fileLine", Path.GetFileName(source)),
            Stack(LocMessage.Of("settings.data.backup.exportedLine",
                    DateTimeText.Minute(DateTimeOffset.FromUnixTimeSeconds(summary.ExportedAt).LocalDateTime)),
                LocMessage.Of("settings.data.backup.countsLine", summary.ItemCount, summary.UserStateCount, summary.TagCount,
                    summary.HasWidgets ? LocMessage.Of("settings.data.backup.widgetsYes") : null)));

        // 统一走外部居中窗口（非 ContentDialog）：按用户主题着色、可拖动、不可重复。
        // 三选一场景（合并导入 / 覆盖导入 / 取消）用 ShowContentAsync 的 primary+secondary 双按钮。
        // 附件张数：确认框上那句"这份还带 N 张图"必须来自包本身，而不是清单里的条目数——
        // 后者会说"带了"而其实一张都没进包。只读中央目录，不解压，故留在 UI 线程上是几毫秒的事。
        var clipCount = BackupContainer.CountClipEntries(source);
        var guideBlock = Stack(
            LocMessage.Of("settings.data.backup.mergeLine"),
            Stack(LocMessage.Of("settings.data.backup.replaceLine"), LocMessage.Of("settings.data.backup.snapshotLine")));
        var body = Stack(summaryBlock,
            Stack(guideBlock, clipCount > 0
                ? LocMessage.Of("settings.data.backup.carriesImages", clipCount)
                : LocMessage.Of("settings.data.backup.noImages"), "\n\n"), "\n\n");
        var detailBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
        // 交身份给 LocText 那颗门而不是先 Render 成字符串：这张窗开着的时候换语言，那一格才跟着重贴（坑表 #276 那一族）。
        LocText.SetMessage(detailBlock, body);
        var choice = await CenteredDialog.ShowContentAsync(
            LocMessage.Of("settings.data.importBackupTitle"), detailBlock, owner: App.MainWindow, dedupeKey: "importbackup",
            width: 480, height: 340,
            primaryText: LocMessage.Of("settings.data.importMerge"),
            secondaryText: LocMessage.Of("settings.data.importOverwrite"),
            cancelText: LocMessage.Of("dialog.cancel"));

        if (choice != CenteredDialog.HostedDialogResult.Committed
            && choice != CenteredDialog.HostedDialogResult.Secondary) return;

        var mode = choice == CenteredDialog.HostedDialogResult.Secondary ? RestoreMode.Replace : RestoreMode.Merge;
        // 附件从<b>这一份文件</b>里解：把路径一起递进去，服务层据此现算"条目认领哪些文件名"，
        // 调用方就没有机会把别的包里的名字写进图片目录。
        var rr = await Task.Run(() => _backup.RestoreAsync(
            env, mode, null, CancellationToken.None, source));
        // Core 交回的那半句是身份（嵌套实参，靠 LocMessage.ToString 在贴上去那一刻取词）⇒
        // 这一行的两半从此同一语言（✅WI-61c 登记"⛔ 一批只改一半"要的就是这一格）。
        ViewModel.BackupStatus = rr.Success
            ? LocMessage.Of("settings.data.backup.withSnapshot", rr.Message, rr.SnapshotPath)
            : LocMessage.Of("settings.data.backup.importFailed", rr.Message);
        ViewModel.RefreshBackups();     // 这次恢复留下的快照要立刻可见——它就是"撤销"的入口
    }
}
