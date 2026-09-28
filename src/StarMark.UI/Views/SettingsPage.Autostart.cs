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
/// SettingsPage 的这一段——开机自动启动：开关的同意流程与写注册表那一下。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    /// <summary>
    /// 逐类型列出：类型标题 +「添加组件」按钮（可重复添加同类型），其下为该类型的每个实例一行
    /// （显示 / 移除）。置顶实例在标签后标注（置顶）。
    /// </summary>
    private AutostartService AutostartService()
        => App.Services.GetRequiredService<AutostartService>();

    private void AutostartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try { AutostartService().SetEnabled(AutostartToggle.IsOn); }
        catch (Exception ex) { StarLog.Error("设置开机自启失败", ex); }
    }
}
