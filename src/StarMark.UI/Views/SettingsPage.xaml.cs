#nullable enable
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StarMark.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }

    private readonly BackupService _backup;

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsPageViewModel>();
        _backup = App.Services.GetRequiredService<BackupService>();
        ViewModel.LoadFromStore();
        _ = ViewModel.LoadHealthAsync();
        BuildWidgetRows();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadFromStore();
        _ = ViewModel.LoadHealthAsync();
        _ = ViewModel.LoadDiagnosticsAsync();
        BuildWidgetRows();
    }

    private WidgetManager? WidgetManager()
        => App.Services.GetService(typeof(WidgetManager)) as WidgetManager;

    /// <summary>逐组件一行：开关（启用/停用）＋“显示”按钮（临时隐藏后找回）。</summary>
    private void BuildWidgetRows()
    {
        var mgr = WidgetManager();
        if (mgr is null || WidgetRows is null) return;

        WidgetRows.Children.Clear();
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var toggle = new ToggleSwitch
            {
                Header = WidgetStorage.KindTitle(kind),
                OnContent = "已添加",
                OffContent = "未添加",
                IsOn = mgr.IsEnabled(kind),
                MinWidth = 0,
            };
            var captured = kind;
            toggle.Toggled += (_, _) =>
                _ = mgr.SetEnabledAsync(captured, toggle.IsOn);

            var showBtn = new Button
            {
                Content = "显示",
                Style = (Style)Application.Current.Resources["SecondaryButton"],
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            showBtn.Click += (_, _) => _ = mgr.ShowAsync(captured);

            Grid.SetColumn(toggle, 0);
            Grid.SetColumn(showBtn, 2);
            row.Children.Add(toggle);
            row.Children.Add(showBtn);
            WidgetRows.Children.Add(row);
        }
    }

    private void WidgetShowAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.ShowAllAsync();

    private void WidgetHideAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.HideAllAsync();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveCommand.Execute(null);
        // 托盘/热键即时生效
        App.MainWindow?.ApplyTraySettings();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("tree");

    // ==================== 数据备份与恢复（P0-2） ====================

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(App.MainWindow!));
        picker.FileTypeChoices.Add("JSON 备份", new[] { ".json" });
        picker.SuggestedFileName = $"starmark-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        ViewModel.IsBackupBusy = true;
        try
        {
            await _backup.ExportToFileAsync(file.Path, CancellationToken.None);
            ViewModel.BackupStatus = $"已导出备份到：{file.Path}";
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"导出失败：{ex.Message}";
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(App.MainWindow!));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        ViewModel.IsBackupBusy = true;
        try
        {
            BackupEnvelope env;
            try
            {
                env = await BackupService.ReadAsync(file.Path, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                ViewModel.BackupStatus = ex.Message;
                return;
            }

            var summary = BackupService.Peek(file.Path);
            var detail = summary is not null
                ? $"导出时间：{DateTimeOffset.FromUnixTimeSeconds(summary.ExportedAt):yyyy-MM-dd HH:mm}\n" +
                  $"条目 {summary.ItemCount}　用户状态 {summary.UserStateCount}　标签 {summary.TagCount}" +
                  (summary.HasWidgets ? "　组件数据：有" : "")
                : "（无法读取摘要）";

            var dlg = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "导入备份",
                PrimaryButtonText = "合并导入",
                SecondaryButtonText = "覆盖导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    Text = $"{detail}\n\n合并导入：保留现有条目，仅补充/覆盖用户元数据（安全、可重复）。\n" +
                           "覆盖导入：先清空再导入，精确还原到备份时刻（会丢掉备份之后新增的条目）。",
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary && result != ContentDialogResult.Secondary) return;

            var mode = result == ContentDialogResult.Secondary ? RestoreMode.Replace : RestoreMode.Merge;
            var rr = await _backup.RestoreAsync(env, mode, null, CancellationToken.None);
            ViewModel.BackupStatus = rr.Success
                ? $"{rr.Message}（恢复前快照：{rr.SnapshotPath}）"
                : $"导入失败：{rr.Message}";
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"导入失败：{ex.Message}";
        }
        finally { ViewModel.IsBackupBusy = false; }
    }
}
