#nullable enable
using StarMark.Abstractions;
using StarMark.Integrations.Ditto;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// Ditto 数据库<b>位置探测</b>测试。Ditto 没有单一约定路径（安装版在 %APPDATA%\Ditto\DB、
/// 便携版在 exe 旁边、也有人把数据目录放到 %LOCALAPPDATA%），只探一条等于"在我这台机器上能跑"，
/// 所以「候选集形状」与「取第一个真实存在的」这两件事都要被钉住。
/// </summary>
public sealed class DittoDbLocationTests
{
    [Fact]
    public void Candidates_StartWithInstalledDefault_AndCoverLocalAppData()
    {
        var list = DittoSource.CandidateDbPaths();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(appData, "Ditto", "DB", "DittoDB.db"), list[0]);
        Assert.Contains(Path.Combine(appData, "Ditto", "DittoDB.db"), list);
        if (!string.Equals(appData, local, StringComparison.OrdinalIgnoreCase))
            Assert.Contains(Path.Combine(local, "Ditto", "DB", "DittoDB.db"), list);
        // 去重：两个 SpecialFolder 在被重定向的机器上会相等，重复项只会让提示变啰嗦
        Assert.Equal(list.Count, list.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FindDbPath_TakesTheFirstExistingOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smoke-ditto-locate-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var hit = Path.Combine(dir, "DittoDB.db");
            File.WriteAllText(hit, "x");
            var missing = Path.Combine(dir, "nope.db");

            Assert.Equal(hit, DittoSource.FindDbPath(new[] { missing, hit }));
            // 全无命中时回第一条：提示要能说出"找过哪里"，而不是"没找过"
            Assert.Equal(missing, DittoSource.FindDbPath(new[] { missing }));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnavailableSource_HintNamesPaths_AndSeparatesBuiltInHistory()
    {
        // 指到一个确定不存在的目录：走"显式路径"分支，提示里必须出现它（用户据此判断装在别处还是没装）
        var ghost = Path.Combine(Path.GetTempPath(), "smoke-no-ditto-" + Guid.NewGuid().ToString("N")[..8], "DittoDB.db");
        var src = (IItemSource)new DittoSource(ghost);
        Assert.False(src.IsAvailable);

        var hint = src.AvailabilityHint;
        Assert.NotNull(hint);
        Assert.Contains(ghost, hint!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("剪贴板历史", hint!, StringComparison.Ordinal);
    }
}
