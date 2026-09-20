#nullable enable
using System;
using System.IO;
using StarMark.Integrations.GitHub;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 回归 AT（F3）：含 PAT 的 github.json 的原子写与「载入-改-存」保真语义。
/// 均走 Save/Load 的 path 形参写临时目录，纯逻辑、不依赖 UI/网络。
/// </summary>
public sealed class GitHubOptionsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public GitHubOptionsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"starmark_gh_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "github.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Save_WritesValidFile_AndLeavesNoTmp()
    {
        new GitHubOptions { Token = "ghp_x", Username = "me", SyncIntervalSeconds = 7200, PageSize = 50 }.Save(_path);
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));   // 先写临时再原子替换：不应残留 .tmp
        var loaded = GitHubOptions.Load(_path);
        Assert.Equal("ghp_x", loaded.Token);
        Assert.Equal("me", loaded.Username);
        Assert.Equal(7200, loaded.SyncIntervalSeconds);
        Assert.Equal(50, loaded.PageSize);
    }

    [Fact]
    public void LoadThenModify_PreservesFieldsAbsentFromEditing()
    {
        // 旧写法在保存处 new GitHubOptions() 整档重写 → 把本面板不出现的 SyncIntervalSeconds/PageSize
        // 静默打回默认（3600/100）。Load→改 Token→Save 后其余字段必须原样保留。
        new GitHubOptions { Token = "old", SyncIntervalSeconds = 60, PageSize = 30 }.Save(_path);

        var github = GitHubOptions.Load(_path);
        github.Token = "new";
        github.Save(_path);

        var after = GitHubOptions.Load(_path);
        Assert.Equal("new", after.Token);
        Assert.Equal(60, after.SyncIntervalSeconds);
        Assert.Equal(30, after.PageSize);
    }
}
