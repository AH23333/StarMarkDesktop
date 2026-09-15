#nullable enable
using System.Text.Json.Serialization;

namespace StarMark.Integrations.GitHub;

/// <summary>
/// GitHub 同步配置。对应技术文档 §3.2 GitHubSource。
/// 配置加载优先级：环境变量 -> %APPDATA%\StarMark\github.json
/// PAT 仅需 public_repo 范围（只读 starred 列表，无写权限）。
/// </summary>
public sealed class GitHubOptions
{
    /// <summary>GitHub Personal Access Token（classic 或 fine-grained，需 public_repo 或 read:user 范围）。</summary>
    public string? Token { get; set; }

    /// <summary>同步间隔（秒）。默认 1 小时。0 表示不自动同步。</summary>
    public int SyncIntervalSeconds { get; set; } = 3600;

    /// <summary>每页拉取数（GitHub API max 100）。MVP 固定 100。</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>UIA 用户名（可选，用于 UI 显示）。</summary>
    public string? Username { get; set; }

    /// <summary>配置文件路径：&lt;APPDATA&gt;/StarMark/github.json</summary>
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarMark", "github.json");

    /// <summary>从文件加载配置。文件不存在则返回空对象。</summary>
    public static GitHubOptions Load(string? path = null)
    {
        var p = path ?? DefaultConfigPath;
        if (!File.Exists(p)) return new GitHubOptions();

        try
        {
            var json = File.ReadAllText(p);
            return System.Text.Json.JsonSerializer.Deserialize<GitHubOptions>(json) ?? new GitHubOptions();
        }
        catch
        {
            return new GitHubOptions();
        }
    }

    /// <summary>保存配置到文件。</summary>
    public void Save(string? path = null)
    {
        var p = path ?? DefaultConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(p, json);
    }

    /// <summary>从环境变量覆盖（开发期用 STARMARK_GITHUB_TOKEN 兜底）。</summary>
    public GitHubOptions WithEnvironmentOverrides()
    {
        var envToken = Environment.GetEnvironmentVariable("STARMARK_GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(envToken)) Token = envToken;
        var envUser = Environment.GetEnvironmentVariable("STARMARK_GITHUB_USERNAME");
        if (!string.IsNullOrEmpty(envUser)) Username = envUser;
        return this;
    }
}
