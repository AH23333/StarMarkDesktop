#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 设置页 ViewModel。主题 / 托盘 / 热键 / GitHub 配置的读取与保存。
/// 主题变更即时生效（通过 <see cref="MainWindow"/> 应用），其余重启后生效。
/// </summary>
public partial class SettingsPageViewModel : ObservableObject
{
    private readonly SettingsStore _settings;

    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _enableTray = true;
    [ObservableProperty] private bool _enableGlobalHotKey = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private string _githubToken = string.Empty;
    [ObservableProperty] private string _githubUsername = string.Empty;
    [ObservableProperty] private string _saveErrorMessage = string.Empty;
    [ObservableProperty] private bool _hasSaveError;

    // 备份与恢复（P0-2）状态反馈
    [ObservableProperty] private string _backupStatus = string.Empty;
    [ObservableProperty] private bool _isBackupBusy;

    public SettingsPageViewModel()
    {
        // 与 MainWindow 保持同一实例语义：settings 文件路径由环境变量决定
        _settings = new SettingsStore();
    }

    public void LoadFromStore()
    {
        var github = StarMark.Integrations.GitHub.GitHubOptions.Load();
        ThemeIndex = _settings.LoadTheme() switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0,
        };
        EnableTray = _settings.LoadEnableTray();
        EnableGlobalHotKey = _settings.LoadEnableGlobalHotKey();
        MinimizeToTray = _settings.LoadMinimizeToTray();
        GithubToken = github.Token ?? string.Empty;
        GithubUsername = github.Username ?? string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        HasSaveError = false;
        try
        {
            var theme = ThemeIndex switch
            {
                1 => ThemePreference.Light,
                2 => ThemePreference.Dark,
                _ => ThemePreference.Default,
            };
            _settings.SaveTheme(theme);
            _settings.SaveEnableTray(EnableTray);
            _settings.SaveEnableGlobalHotKey(EnableGlobalHotKey);
            _settings.SaveMinimizeToTray(MinimizeToTray);

            var github = new StarMark.Integrations.GitHub.GitHubOptions();
            if (!string.IsNullOrWhiteSpace(GithubToken)) github.Token = GithubToken.Trim();
            if (!string.IsNullOrWhiteSpace(GithubUsername)) github.Username = GithubUsername.Trim();
            github.Save();

            StarMark.Abstractions.StarLog.Info($"设置已保存（主题={theme}, 托盘={EnableTray}）");
            SaveErrorMessage = string.Empty;

            // 主题即时应用
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.MainWindow != null)
                {
                    Helpers.ThemeManager.Apply(App.MainWindow, theme);
                    App.MainWindow.RefreshThemeIcon(theme);
                }
            });
        }
        catch (Exception ex)
        {
            HasSaveError = true;
            SaveErrorMessage = $"保存失败: {ex.Message}";
            StarMark.Abstractions.StarLog.Error($"设置保存失败: {ex}");
        }
    }
}