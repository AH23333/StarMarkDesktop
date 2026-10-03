#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Insights;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using Windows.UI;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——本地磁盘搜索这一头：索引根目录的告警、Everything 的准备/重试/状态轮询。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    // ===== 库里留着、盘上已经没有的本机文件条目（批次 VQ；「索引进库」那三行输入格的位置）=====

    /// <summary>扫描结果那句话（含"读不到库"与"扫描失败"两种坏消息）；<b>空＝还没扫过</b>。非空就一定显示。</summary>
    [ObservableProperty] private string _missingFileRowsStatus = string.Empty;

    /// <summary>待清理的 Id 清单（只读扫描的结果，清理命令按它删；<b>不</b>是"每次点按钮重新数一遍"的两份真相）。</summary>
    private List<long> _missingIds = new();

    /// <summary>有没有可清理的行（只管那颗「清掉这些行」按钮）。</summary>
    public bool HasMissingFileRows => _missingIds.Count > 0;

    /// <summary>
    /// 那句话该不该显示。<b>与上一条刻意分开</b>：把说明的可见性绑在"有没有可删的行"上，
    /// "扫描失败"与"读不到库"这两句就跟着一起被藏起来——点了按钮既没反应也没人说话，
    /// 那正是本批要消掉的症状（与批次 IJ 那颗 <c>HasItems</c> 未复位导致失败提示看不见同族）。
    /// </summary>
    public bool HasMissingFileRowsNotice => !string.IsNullOrEmpty(MissingFileRowsStatus);

    partial void OnMissingFileRowsStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasMissingFileRows));
        OnPropertyChanged(nameof(HasMissingFileRowsNotice));
    }

    /// <summary>扫描一次要读多宽：与文件夹树同一页窗口（2000）。撞到窗口就照实说，不把"没扫完"报成"都扫过了"。</summary>
    private const int MissingScanWindow = 2000;

    /// <summary>
    /// 数一遍库里的本机文件条目，把"盘上已经指不到东西"的那几条挑出来。
    /// <para>为什么放在设置页、要人点一下，而不是启动时自动删：<b>"文件不在"与"该删"不是同一件事</b>——
    /// 移动硬盘没插、U 盘拔了、网络盘暂不可达、OneDrive 还没同步，都会让存在性判定假阴性，
    /// 而那些行身上挂着用户的标签与笔记。程序能自愈的是"把它挑出来、说清楚、给一颗按钮"，
    /// 悄悄删掉用户数据不在自愈的范围内（P-54 说的是别把动作写成用户作业，不是别让他确认）。</para>
    /// <para>读 <see cref="IItemRepository.GetAllAsync"/> 的 file 档而不是新加一条 SQL：这条链要的就是"所有文件行"，
    /// 现成的浏览路径正是这个语义，多一条 SQL 就多一个会与浏览口径分岔的地方。</para>
    /// </summary>
    [RelayCommand]
    private async Task RescanMissingFilesAsync()
    {
        _missingIds = new List<long>();
        if (_repository is not { } repo)
        {
            MissingFileRowsStatus = "现在还读不到库里的条目（稍后再点一次『重新扫描』）。";
            return;
        }
        try
        {
            var rows = await repo.GetAllAsync(new BrowseFilter
            {
                TypeFilter = "file",
                IncludeHidden = true,          // 隐藏的行也算：它只是不显示，没说不存在
                Limit = MissingScanWindow,
            }, CancellationToken.None);

            var gone = rows.Where(static i => StarMark.Abstractions.ItemCardPolicy.IsLocalFileRow(i.Source, i.Type)
                                              && !StarMark.Abstractions.LocalFileIdentity.TryExistingPath(
                                                  i.Uri, StarMark.UI.Helpers.LauncherEx.ExistsOnDisk, out _))
                           .ToList();
            _missingIds = gone.Select(static i => i.Id).ToList();
            // 扫过之后"一条没有"也要说一声：这一句与"坏消息"共用同一个可见性判据——
            // 点了按钮既不消失也不出声，就是本批要消掉的那个症状本身。
            // 撞到窗口时照实写"前 N 条"，不把"没扫完"报成"都扫过了"。
            var scanned = rows.Count >= MissingScanWindow
                ? $"扫过前 {MissingScanWindow} 条（窗口之外可能还有）"
                : $"扫过 {rows.Count} 条";
            MissingFileRowsStatus = gone.Count == 0
                ? $"本机文件条目都还在（{scanned}）。"
                : $"库里有 {gone.Count} 条本机文件路径在这台机器上已经找不到（文件被移动或删除，或所在的盘现在没插）——{scanned}。";
        }
        catch (Exception ex)
        {
            StarLog.Error("扫描失联的本机文件条目失败", ex);
            MissingFileRowsStatus = "扫描失败：" + ex.Message;
        }
        OnPropertyChanged(nameof(HasMissingFileRows));
    }

    /// <summary>
    /// 清掉刚才数出来的那几行。<b>只删库里的记录，绝不动磁盘上的文件</b>（仓储那条 SQL 的 WHERE 也限定了
    /// type+source，见 <see cref="IItemRepository.DeleteFileEntriesAsync"/>）。
    /// <para>先报条数再确认：一次点掉几十行而没有回看的机会，等于把"清理"做成第二个缺陷。
    /// 删完再扫一次，让那句说明与屏幕上剩下的行同步——留着上一轮的计数就是新的一份假话。</para>
    /// </summary>
    [RelayCommand]
    private async Task CleanMissingFilesAsync()
    {
        var ids = _missingIds.ToList();
        if (ids.Count == 0 || _repository is not { } repo) return;
        if (!await CenteredDialog.ConfirmAsync("清掉这些行？",
                $"将从库里删掉 {ids.Count} 条已经找不到文件的本机路径。只删记录，磁盘上的文件不会被改动；"
                + "这些行上挂的标签与笔记也会一并删掉。",
                "清掉", "先留着", dedupeKey: "clean-missing-file-rows")) return;
        try
        {
            var removed = await repo.DeleteFileEntriesAsync(ids, CancellationToken.None);
            App.MainWindow?.ShowNotice(removed > 0 ? "已清掉" : "已经没有这些行了",
                removed > 0 ? $"删掉 {removed} 条记录；磁盘上的文件没有被改动。"
                            : "它们在你点这一下之前就已经不在库里了（可能刚清过一遍）。");
        }
        catch (Exception ex)
        {
            StarLog.Error("清理失联的本机文件条目失败", ex);
            App.MainWindow?.ShowError("清理失败", ex.Message);
        }
        await RescanMissingFilesAsync();
    }

    /// <summary>
    /// 本地磁盘搜索总开关。开启即把 <see cref="StarMark.Integrations.Everything.FileIndexOptions.Enabled"/>
    /// 单例实时翻位——<c>EverythingSource.IsAvailable</c> 每次查询都实读该属性，故统一搜索立刻纳入/剔除
    /// 本地文件源，无需重启（收束待决策 P-2 方案 B）。开启时后台懒起 <c>EnsureReadyAsync</c> 准备 Everything。
    /// </summary>
    [ObservableProperty] private bool _localDiskSearchEnabled;

    /// <summary>开关下方的一行状态提示文本。</summary>
    [ObservableProperty] private string _localDiskSearchStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制 <see cref="OnLocalDiskSearchEnabledChanged"/> 副作用，
    /// 免得每次进入设置页就把默认值再存一遍、甚至误触发 Everything 拉起。</summary>
    private bool _suppressLocalDiskApply;

    partial void OnLocalDiskSearchEnabledChanged(bool value)
    {
        if (_suppressLocalDiskApply) return;
        var options = App.Services.GetRequiredService<StarMark.Integrations.Everything.FileIndexOptions>();

        // 关闭：即时翻位单例（IsAvailable 实读），统一搜索立刻剔除本地文件源；已装的 Everything / 服务不动。
        if (!value)
        {
            options.Enabled = false;
            _settings.SaveLocalDiskSearchEnabled(false);
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已关闭：不再搜索本地文件（已安装的 Everything 不受影响）。";
            return;
        }

        // 开启：持久化 + 实时翻位单例（IsAvailable 实读）。
        options.Enabled = true;
        _settings.SaveLocalDiskSearchEnabled(true);
        PrepareLocalDiskSearch();
    }

    /// <summary>
    /// 「开关已开但本地搜索实际不通」时才出现的重试按钮（P-54）。
    /// </summary>
    [ObservableProperty] private bool _canRetryLocalDiskPrepare;

    /// <summary>按钮只干一件事：就地重跑准备流程。以前这里按会话权限换文字（"重试提权重启"），
    /// 因为开关会先把 StarMark 提上去；那条自我提权已经删掉（P-108 改判），所以措辞也只有一份。</summary>
    public string LocalDiskSearchRetryLabel => "重试准备 Everything";

    /// <summary>
    /// 开启本地磁盘搜索的准备流程：<b>就地</b>起 SDK / 拉起 Everything 客户端，不碰进程权限。
    /// <para>
    /// 之所以从开关回调里抽出来：失败分支原先的文案是「请确认 Everything 正在运行后稍候再试」
    /// 「请用开始菜单『以管理员身份运行』重启 StarMark」——把程序自己能做的动作写成给用户布置的作业。
    /// 抽成方法后「重试」按钮点的就是这同一段代码，不留第二条需要人照做的路（P-54）。
    /// </para>
    /// <para>这里刻意不再请求提权：提权对连上引擎没有增益（引擎跑在普通 IL），却会顺带掐死
    /// 资源管理器拖进／拖出并让系统文件对话框调不起来。见 <c>App.xaml.cs</c> 同一条改判注释。</para>
    /// </summary>
    private void PrepareLocalDiskSearch()
    {
        CanRetryLocalDiskPrepare = false;
        OnPropertyChanged(nameof(LocalDiskSearchRetryLabel));

        LocalDiskSearchStatus = "已开启：正在准备 Everything（起 SDK / 拉起客户端）…";
        _ = Task.Run(async () =>
        {
            string status;
            bool needRetry;
            try
            {
                var src = App.Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>();
                var outcome = await src.EnableAsync(CancellationToken.None);
                needRetry = outcome != StarMark.Integrations.Everything.EverythingSource.LocalDiskSearchEnableOutcome.Ready;
                status = needRetry
                    ? "已开启，但 Everything 尚未就绪（客户端没起来，或它正以另一种权限运行——那种情况下本程序不会替你去改它的权限）。"
                    : "已开启：本地文件将参与全盘搜索（快捷搜索 / 主窗即时生效）。";
            }
            catch (Exception ex)
            {
                StarLog.Error("本地磁盘搜索：准备 Everything 失败", ex);
                needRetry = true;
                status = $"已开启，但准备 Everything 失败：{ex.Message}";
            }
            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
            {
                LocalDiskSearchStatus = status;
                CanRetryLocalDiskPrepare = needRetry;
            });
        });
    }

    [RelayCommand]
    private void RetryLocalDiskPrepare() => PrepareLocalDiskSearch();

    /// <summary>
    /// 进设置页时的初始状态文本。<b>只探测、不拉起</b>——拉起是开关与启动流程的职责，
    /// 不能因为用户打开设置页就在后台起一个 Everything 进程。
    /// <para>但"开关开着"≠"本地文件真的能搜"：App 启动那一次准备失败时用户是完全看不见的，
    /// 旧文案却一律写"已开启：本地文件会出现在搜索结果中"，属假安全感。探到不通就照实说，
    /// 并把重试按钮摆出来（P-54）。</para>
    /// </summary>
    private void RefreshLocalDiskSearchState()
    {
        if (!LocalDiskSearchEnabled)
        {
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已关闭：开启后可像 Everything 一样全盘秒搜本地文件（首次会自动准备 Everything）。";
            return;
        }

        bool ready;
        try
        {
            ready = App.Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>().IsAvailable;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"探测 Everything 可用性失败（状态按未知展示）：{ex.Message}");
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已开启：本地文件会出现在快捷搜索 / 主窗搜索结果中。";
            return;
        }

        if (ready)
        {
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已开启：本地文件会出现在快捷搜索 / 主窗搜索结果中。";
            return;
        }

        CanRetryLocalDiskPrepare = true;
        // 两种成因都照实说，但不把用户支使去改权限：本程序不会为了连引擎而抬自己的权限，
        // 抬上去的代价是拖进／拖出与系统对话框一起失灵（P-108 改判）。
        LocalDiskSearchStatus = "已开启，但 Everything 没连上：它可能没在运行，也可能正以比本程序更高的权限运行。";
    }
}
