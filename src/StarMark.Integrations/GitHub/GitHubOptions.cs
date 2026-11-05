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

    /// <summary>
    /// 有没有配 Token——<b>这条判据只写在这里一次</b>：客户端的 <c>IsConfigured</c> 与源的 <c>IsAvailable</c> 都读它，
    /// 于是"没配 Token 就不该建客户端"与"界面说没说已配置"这两件事不会分岔（P-123 那条线）。
    /// 语义逐字沿用旧写法（<c>!IsNullOrEmpty</c>）：<b>只填空格</b>也算"配了"，那是既有行为，不在这批顺手改。
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrEmpty(Token);

    /// <summary>同步间隔（秒）。默认 1 小时。0 表示不自动同步。</summary>
    public int SyncIntervalSeconds { get; set; } = 3600;

    /// <summary>每页拉取数（GitHub API max 100）。MVP 固定 100。</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>UIA 用户名（可选，用于 UI 显示）。</summary>
    public string? Username { get; set; }

    /// <summary>
    /// 配置文件路径：与 settings.json / widgets.json 同一套优先规则（环境变量 ＞ %APPDATA%\StarMark）。
    /// <para>这里原先是硬拼 <c>%APPDATA%\StarMark\github.json</c>，是五份用户数据档里唯一漏掉环境变量那一档的一份。
    /// 后果不是"路径不好看"：这一份存的是 <b>GitHub Token</b>，漏掉改道就意味着单测与沙盒取证都在读写
    /// 用户真目录里的那一份密钥档（见 <see cref="StarMark.Abstractions.UserDataPaths"/>）。</para>
    /// </summary>
    public static string DefaultConfigPath => StarMark.Abstractions.UserDataPaths.Sibling("github.json");

    /// <summary>从文件加载配置。文件不存在则返回空对象。
    /// <para>Token 那一格（P-30）：密文→解密；<b>明文→原样可用</b>（旧版明文档的回落），且明文档
    /// 在这一次读取里<b>当场升级改写成密文档</b>——用户不需要知道有这一步，下次打开也不是明文。</para></summary>
    public static GitHubOptions Load(string? path = null)
    {
        var p = path ?? DefaultConfigPath;
        if (!File.Exists(p)) return new GitHubOptions();

        try
        {
            var json = File.ReadAllText(p);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<GitHubOptions>(json) ?? new GitHubOptions();
            var stored = loaded.Token;
            loaded.Token = StarMark.Abstractions.SecretProtector.Unprotect(stored);
            if (!string.IsNullOrEmpty(stored) && !StarMark.Abstractions.SecretProtector.IsCipher(stored))
            {
                // 首读透明升级：明文档当场改写成密文档（loaded.Token 此刻已是明文，Save 会重新封好）。
                // 升级是尽力而为——失败不拦读取（下次再试），但要出一声：用户的 PAT 一直躺在明文里
                // 而日志一句不提，就是「已修复」没有证人的那半截（#231 那一族）。
                try
                {
                    loaded.Save(p);
                }
                catch (Exception ex)
                {
                    StarMark.Abstractions.StarLog.Warn($"github.json 明文档透明升级失败（下次读取再试）：{ex.Message}");
                }
            }
            return loaded;
        }
        catch
        {
            return new GitHubOptions();
        }
    }

    /// <summary>保存配置到文件（先写临时文件再原子替换，避免崩溃/占用把含 PAT 的配置截断）。
    /// <para>落盘的 Token 一律走 <see cref="StarMark.Abstractions.SecretProtector"/> 密文（P-30）：
    /// 内存里这份仍是明文，调用方读到的与从前完全一样。序列化用「影子副本」而不是先把
    /// <c>this.Token</c> 换成密文再改回来——后者中途抛异常会把密文留在调用方手里那份配置上，
    /// 保存这个动作顺手改了别人读数，两边从此分岔。</para></summary>
    public void Save(string? path = null)
    {
        var p = path ?? DefaultConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var onDisk = new GitHubOptions
        {
            Token = StarMark.Abstractions.SecretProtector.Protect(Token),
            SyncIntervalSeconds = SyncIntervalSeconds,
            PageSize = PageSize,
            Username = Username,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(onDisk, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, p, overwrite: true);
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
