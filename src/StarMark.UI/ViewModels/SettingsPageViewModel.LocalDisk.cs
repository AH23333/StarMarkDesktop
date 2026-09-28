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

    // ===== 本地文件索引（P0-1b）=====
    [ObservableProperty] private string _fileIndexRootsText = string.Empty;
    [ObservableProperty] private string _maxFileIndexCountText = string.Empty;

    /// <summary>
    /// 「已配置但当前不可用」的索引目录说明（空＝全部可用，控件据此隐藏）。
    /// 过去这类目录是被 <c>Directory.Exists</c> 静默剔除的：用户看到的文本框少了一行、
    /// 下次保存就永久没了，而搜索结果少了一批文件却无任何解释（P-56）。
    /// </summary>
    [ObservableProperty] private string _fileIndexRootsStatus = string.Empty;

    /// <summary>有无"当前不可用目录"要提示（XAML 用现成的 BoolToVisibility 控制该行的显示）。</summary>
    public bool HasFileIndexRootsWarning => !string.IsNullOrEmpty(FileIndexRootsStatus);

    partial void OnFileIndexRootsStatusChanged(string value) => OnPropertyChanged(nameof(HasFileIndexRootsWarning));

    private void RefreshFileIndexRootsStatus()
    {
        var unavailable = Safe(_settings.UnavailableFileIndexRoots, Array.Empty<string>(), "索引目录可用性");
        FileIndexRootsStatus = unavailable.Count == 0
            ? string.Empty
            : $"以下索引目录当前不可用（不存在 / 盘未插 / 无权限），配置已保留、恢复后无需重填，但暂时不会索引进库：{string.Join("、", unavailable)}";
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
