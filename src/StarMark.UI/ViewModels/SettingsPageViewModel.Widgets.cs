#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——「开机自动加载组件」这一颗开关（用户裁决：这笔性能取舍由他自己选）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放这一件事的几条出口。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    /// <summary>
    /// 开机要不要把组件窗全建出来。<b>默认开</b>（＝今天的观感）；省多少由 <see cref="WidgetStartupPolicy"/> 说。
    /// <para>
    /// setter 里<b>当场生效</b>：关掉就立刻收起并释放当前窗口，打开就立刻按存档点亮——
    /// 把"重启后生效"写进界面在本项目里按缺陷算（记忆：任何"请重启"都是让用户替程序做事）。
    /// 建与收都只走 <see cref="WidgetManager"/> 已有的整批出口，这里不开第二份"怎么建窗"的知识。
    /// </para>
    /// </summary>
    public bool WidgetsLoadOnStartup
    {
        get => _settings.LoadWidgetsLoadOnStartup();
        set
        {
            if (value == _settings.LoadWidgetsLoadOnStartup()) return;
            _settings.SaveWidgetsLoadOnStartup(value);
            OnPropertyChanged();
            ApplyWidgetsLoad(value);
        }
    }

    /// <summary>
    /// 开关下面那行说明。数字只从 <see cref="WidgetStartupPolicy.TradeoffNote"/> 来（一颗出处），
    /// 而且<b>不含"你现在有 N 颗"</b>——那种数要跟着增删组件刷新，漏刷就变成报一个过期的钱数。
    /// </summary>
    public string WidgetsLoadHint => WidgetStartupPolicy.TradeoffNote;

    private static StarMark.UI.Services.WidgetManager? WidgetsManager()
        => App.Services.GetService<StarMark.UI.Services.WidgetManager>();

    private void ApplyWidgetsLoad(bool load)
    {
        if (WidgetsManager() is not { } mgr) return;
        if (load) _ = mgr.RestoreOnStartupAsync(loadOnStartup: true);
        else _ = mgr.CollapseAllAsync();
    }
}
