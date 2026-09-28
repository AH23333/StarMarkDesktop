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
/// SettingsPageViewModel 的这一段——RSS 与 GitHub 热榜这两条来源的开关与联动（含「要不要问用户设为速览」）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    /// <summary>
    /// 源列表改动后重算一次"这一栏算不算开着"，并<b>立刻</b>把导航栏跟上。
    /// <para>为什么必须由源列表的改动来调它：从没表过态的用户（升级来的、或一个源都没删过的）
    /// 按「添加」之后，导航栏就该出现「RSS」了——若还等他去翻那个开关，就是凭空多出来的一步。</para>
    /// <para>用户<b>明确关过</b>时这里不会擅自替他打开：<c>LoadRssEnabled</c> 在有表态时一律以表态为准。</para>
    /// </summary>
    public void RefreshRssEnabled()
    {
        _suppressRssApply = true;
        RssEnabled = _settings.LoadRssEnabled();
        _suppressRssApply = false;
        App.MainWindow?.ApplyRssNavVisibility(RssEnabled);
    }

    /// <summary>
    /// 开启热榜时征询一次"要不要在今日速览里也显示"（用户裁决 §5.1）。
    /// <para>刻意不阻塞开关本身：开关先落地、弹窗只是追加决定第二块，弹窗若抛错也不能把开关状态卡住。</para>
    /// </summary>
    private async System.Threading.Tasks.Task AskTrendingGlanceAsync()
    {
        try
        {
            if (App.MainWindow is not { } owner) return;
            if (_settings.LoadTrendingGlanceEnabled()) return;      // 之前已答过"是"就不再问
            var yes = await Helpers.CenteredDialog.ConfirmAsync(
                "要在今日速览里显示 GitHub 热榜吗？",
                "组件里会多一块热榜（默认日榜），原「常看」排到它的下面。" +
                "选「不显示」也没关系：导航栏的「热榜」页照常可用，这里随时能改。",
                primaryText: "显示", cancelText: "不显示", owner: owner, dedupeKey: "trending-glance-ask");
            if (!yes) return;
            TrendingGlanceEnabled = true;
        }
        catch (Exception ex) { StarLog.Error("热榜「今日速览」询问弹窗失败（开关已生效，可在设置里手动勾选）", ex); }
    }
}
