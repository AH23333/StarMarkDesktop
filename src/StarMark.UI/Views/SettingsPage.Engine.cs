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
/// SettingsPage 的这一段——本地磁盘搜索（Everything 引擎与索引根目录）这一头：装/换/删/重开与体积展示。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    // ===== 本地搜索引擎管理（占用 / 打开所在目录 / 删除）=====
    private void OpenEngineFolder_Click(object sender, RoutedEventArgs e)
        => StarMark.Integrations.Everything.EverythingSource.OpenEngineFolder();

    private async void DeleteEngine_Click(object sender, RoutedEventArgs e)
    {
        var bytes = StarMark.Integrations.Everything.EverythingSource.GetEngineOccupancyBytes();
        var sizeHint = bytes > 0 ? $"（约 {FileSizeText.Human(bytes)}）" : "";
        var confirm = await CenteredDialog.ConfirmAsync(
            "删除本地搜索引擎",
            $"将删除本地磁盘搜索所用的 Everything 引擎及其 SDK{sizeHint}。书签 / Star / 标签数据不受影响；" +
            "下次开启本地磁盘搜索会自动重新下载。确定删除？",
            primaryText: "删除", cancelText: "取消",
            owner: App.MainWindow, dedupeKey: "deleteengine");
        if (!confirm) return;
        StarMark.Integrations.Everything.EverythingSource.DeleteEngine();
        RefreshEngineSize();
        ViewModel.LocalDiskSearchStatus = "已删除本地搜索引擎。下次开启本地磁盘搜索（或重启应用）会自动重新下载。";
    }

    private void RefreshEngineSize()
    {
        var bytes = StarMark.Integrations.Everything.EverythingSource.GetEngineOccupancyBytes();
        EngineSizeText.Text = bytes > 0 ? $"约 {FileSizeText.Human(bytes)}" : "未安装";
    }

}
