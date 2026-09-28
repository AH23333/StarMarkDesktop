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

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——页内导航：进来时选中哪个标签、离开时收尾、返回与跳热榜。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadFromStoreSilently();
        _ = ViewModel.LoadHealthAsync();
        _ = ViewModel.LoadDiagnosticsAsync();
        RefreshEngineSize();
        if (WidgetManager() is { } mgr)
        {
            mgr.InstancesChanged -= OnInstancesChanged;   // 防重复订阅
            mgr.InstancesChanged += OnInstancesChanged;
            mgr.LayoutsChanged -= OnLayoutsChanged;
            mgr.LayoutsChanged += OnLayoutsChanged;
        }
        // 订阅主窗口快捷切主题：用户在设置页内用主界面右上角切主题时，把本页主题选择同步过来，
        // 避免离开设置页时兜底保存用过期的 ThemeIndex 把主题强制切回。
        MainWindow.ThemePreferenceQuickSwitched -= OnExternalThemeSwitch;
        MainWindow.ThemePreferenceQuickSwitched += OnExternalThemeSwitch;
        // 本页代码构建的行按 ActualTheme 取画笔，主题翻转时要重建（先退再订：本页可能被反复导航进入）
        ActualThemeChanged -= OnPageActualThemeChanged;
        ActualThemeChanged += OnPageActualThemeChanged;
        BuildWidgetRows();
        BuildHotkeyRows();
        BuildLayoutRows();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // 必须退订：WidgetManager 是单例服务，不退订会让死页面持续重建 UI（泄漏）
        if (WidgetManager() is { } mgr)
        {
            mgr.InstancesChanged -= OnInstancesChanged;
            mgr.LayoutsChanged -= OnLayoutsChanged;
        }
        // 退订主窗口快捷切主题（MainWindow 是单例，不退订会让死页面持续响应）
        MainWindow.ThemePreferenceQuickSwitched -= OnExternalThemeSwitch;
        ActualThemeChanged -= OnPageActualThemeChanged;
        StopRecording(resetText: true);      // 离开页面停止键盘钩子
        // 快捷键属「确认后才生效」项：离开页面时把改动落盘并注册，避免用户以为改了却没生效
        if (_hotkeysDirty)
        {
            ApplyHotkeyBindings();
            _hotkeysDirty = false;
            RaiseHotkeyStateChanged();
        }
        ViewModel.SaveCommand.Execute(null); // 离开时兜底落盘
    }

    // ==================== 底部操作 ====================

    private void Back_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("tree");

    /// <summary>「打开热榜页」：刚开启开关的人不必自己回导航栏找那一项（就地入口，P-54 口径）。</summary>
    private void OpenTrending_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("trending");

    // ==================== 数据备份与恢复（P0-2） ====================

    // WinRT 的文件选择器要走系统对话框宿主（普通权限）。本进程权限高于宿主时——过去是"开本地磁盘搜索就自我提权"
    // 造成的（P-108 已改判，程序不再自己抬权限），现在只剩"用户自己以管理员运行"这一种——
    // PickSaveFileAsync/PickSingleFileAsync 稳定抛 COMException E_FAIL（真机日志：提权 pid 内连挂 4 次），
    // 且 try 若从 picker 之后才开始，异常就冲到 UI 兜底网、用户只看到"点了没反应"。
    // 处置：picker 调用本身进 try；失败**不写死成因、也不要求用户换权限重启**，而是退回应用内路径输入框（见 RequestBackupPathAsync）。
    private const string PickerBlockedHint =
        "系统文件对话框在当前会话调不起来（本程序权限高于对话框宿主时就会这样）——改用路径输入框。";
}
