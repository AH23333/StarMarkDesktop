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
/// SettingsPage 的这一段——主题那一条：外部切换与系统深浅色变化时，这一页怎么跟着改而不把用户的显式选择吃掉。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    /// <summary>主窗口右上角快捷切换主题时，把本页主题选择同步过来。
    /// 用 <see cref="_suppressSave"/> 包住，避免触发自动保存（该值已由主窗口落盘，无需重复写）。</summary>
    private void OnExternalThemeSwitch(ThemePreference pref)
    {
        _suppressSave = true;
        try
        {
            ViewModel.ThemeIndex = pref switch
            {
                ThemePreference.Light => 1,
                ThemePreference.Dark => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressSave = false;
        }
    }

    /// <summary>
    /// 主题在运行期翻转时重建这些代码构建的行：它们的前景色是在构建那一刻取定的，
    /// 不重建就会把上一种主题的画笔留在页面上（与「切主题后组件材质不跟」同一类缺陷）。
    /// 录制快捷键期间不重建——那会把用户正在录的那一行的临时态抹掉。
    /// </summary>
    private void OnPageActualThemeChanged(object? sender, object e)
    {
        if (_recordingAction is not null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            BuildWidgetRows();
            BuildHotkeyRows();
            BuildLayoutRows();
        });
    }
}
