#nullable enable
using StarMark.Abstractions;
using StarMark.Integrations.Ditto;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// Ditto 数据库<b>位置探测</b>测试。Ditto 没有单一约定路径（装机版 3.25 实测落 %APPDATA%\Ditto\Ditto.db、
/// 老文档形在 %APPDATA%\Ditto\DB、便携版在 exe 旁边、也有人把数据目录放到 %LOCALAPPDATA%），
/// 只探一条等于"在我这台机器上能跑"，所以「候选集形状」与「取第一个真实存在的」这两件事都要被钉住。
/// </summary>
public sealed class DittoDbLocationTests
{
    [Fact]
    public void Candidates_StartWithInstalledDefault_AndCoverLocalAppData()
    {
        var list = DittoSource.CandidateDbPaths();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // 装机版现状形（Ditto 3.25.113.0・2026-10-31 授权装机实测）排最前——它才是今天的装机默认
        Assert.Equal(Path.Combine(appData, "Ditto", "Ditto.db"), list[0]);
        // 老文档形照留：旧安装的库还落在那两处
        Assert.Contains(Path.Combine(appData, "Ditto", "DB", "DittoDB.db"), list);
        Assert.Contains(Path.Combine(appData, "Ditto", "DittoDB.db"), list);
        if (!string.Equals(appData, local, StringComparison.OrdinalIgnoreCase))
            Assert.Contains(Path.Combine(local, "Ditto", "Ditto.db"), list);
        // 去重：两个 SpecialFolder 在被重定向的机器上会相等，重复项只会让提示变啰嗦
        Assert.Equal(list.Count, list.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// P-192 结案入口①那次授权装机（winget・Ditto 3.25.113.0）量出来的事实：装机版把库落在
    /// <c>%APPDATA%\Ditto\Ditto.db</c>——没有 <c>DB\</c> 子目录、名里没有 <c>DB</c>。旧候选表整张按
    /// <c>DittoDB.db</c> 探，结果<b>装着 Ditto 也永远"不可用"</b>（那颗副标题根本不会上屏）。
    /// 钉住：候选表必须认得这张现形，且两颗根目录各自由它领头
    /// （FindDbPath 取第一个存在的——今天的装机默认优先）。
    /// </summary>
    [Fact]
    public void Candidates_LeadWithTheShapeTodaysInstallerActuallyWrites()
    {
        var list = DittoSource.CandidateDbPaths();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(appData, "Ditto", "Ditto.db"), list[0]);
        if (!string.Equals(appData, local, StringComparison.OrdinalIgnoreCase))
            Assert.Equal(Path.Combine(local, "Ditto", "Ditto.db"),
                list.First(p => p.StartsWith(local, StringComparison.OrdinalIgnoreCase)));
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
        // 那句"内置历史不依赖 Ditto"自批次 WI-57 起住在表里（钉它＝钉某一列的措辞，规则 #130）；
        // 这里能机检、且与语言无关的是：那颗键＋实参里真的带着这条路径。
        Assert.Equal("source.hint.ditto", hint!.Key);
        Assert.Contains(ghost, hint.Render(), StringComparison.OrdinalIgnoreCase);
    }
}
