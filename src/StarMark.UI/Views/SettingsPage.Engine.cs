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
using StarMark.Abstractions.I18n;
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
        // 「约」那颗词从前坐在数据实参里（`（约 …）` 整块交进 `deleteBody` 的槽）⇒ 换了语言那一格还是中文。
        // 现在交的是没渲染的身份：那一格自己按当前语言取词，体积读数还是数据。
        LocMessage? sizeHint = bytes > 0
            ? LocMessage.Of("settings.engine.sizeApprox", FileSizeText.Human(bytes))
            : null;
        var confirm = await CenteredDialog.ConfirmAsync(
            LocMessage.Of("settings.engine.deleteTitle"),
            LocMessage.Of("settings.engine.deleteBody", sizeHint),
            LocMessage.Of("dialog.delete"), LocMessage.Of("dialog.cancel"),
            owner: App.MainWindow, dedupeKey: "deleteengine");
        if (!confirm) return;
        StarMark.Integrations.Everything.EverythingSource.DeleteEngine();
        RefreshEngineSize();
        ViewModel.LocalDiskSearchStatus = LocMessage.Of("settings.extend.engineDeleted");
    }

    private void RefreshEngineSize()
    {
        var bytes = StarMark.Integrations.Everything.EverythingSource.GetEngineOccupancyBytes();
        // 那一格原先把"约 / 未安装"两颗词直接写进 `Text`：换语言那一刻它停在上一门，
        // 而它旁边那行（"本地搜索引擎占用"）是跟着表走的 ⇒ 同一张卡半中半英。
        LocText.SetMessage(EngineSizeText, bytes > 0
            ? LocMessage.Of("settings.extend.engineSizeApprox", FileSizeText.Human(bytes))
            : LocMessage.Of("settings.extend.engineNotInstalled"));
    }

}
