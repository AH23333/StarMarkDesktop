#nullable enable
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// StarLog 节流判定的逐字契约（批次 GK 引入，用于热路径重复诊断行）。
/// 每条用例用独立 key（Guid），彼此不受静态字典状态影响。
/// </summary>
public sealed class StarLogThrottleTests
{
    private const int Window = 60_000;

    private static string Key() => "t:" + Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstOccurrence_EmitsWithoutSuffix()
    {
        var key = Key();
        var (emit, suppressed) = StarLog.TryThrottle(key, 1_000, Window);

        Assert.True(emit);
        Assert.Equal(0, suppressed);   // 首次放行不该带"此前 x N"
    }

    [Fact]
    public void RepeatsWithinWindow_AreSuppressed()
    {
        var key = Key();
        StarLog.TryThrottle(key, 1_000, Window);

        Assert.False(StarLog.TryThrottle(key, 2_000, Window).Emit);
        Assert.False(StarLog.TryThrottle(key, Window, Window).Emit);   // 仍在 60s 窗口内
    }

    [Fact]
    public void WindowBoundary_IsExclusive()
    {
        var key = Key();
        StarLog.TryThrottle(key, 0, Window);

        Assert.False(StarLog.TryThrottle(key, Window - 1, Window).Emit);   // 差 1 ms 仍在窗口内
        Assert.True(StarLog.TryThrottle(key, Window, Window).Emit);        // 到点即放行
    }

    [Fact]
    public void NextEmitAfterWindow_CarriesSuppressedCount_ThenResets()
    {
        var key = Key();
        StarLog.TryThrottle(key, 0, Window);
        StarLog.TryThrottle(key, 10, Window);
        StarLog.TryThrottle(key, 20, Window);

        var late = StarLog.TryThrottle(key, Window, Window);
        Assert.True(late.Emit);
        Assert.Equal(2, late.SuppressedBefore);   // 被压掉的两条要在这一行里露头

        // 计数随放行归零：下一次放行不该再报"此前 2 条"
        Assert.Equal(0, StarLog.TryThrottle(key, Window * 2, Window).SuppressedBefore);
    }

    [Fact]
    public void DifferentKeys_DoNotInterfere()
    {
        var a = Key();
        var b = Key();

        Assert.True(StarLog.TryThrottle(a, 0, Window).Emit);
        Assert.True(StarLog.TryThrottle(b, 0, Window).Emit);   // 换 key 仍算首次
        Assert.False(StarLog.TryThrottle(a, 1, Window).Emit);
        Assert.False(StarLog.TryThrottle(b, 1, Window).Emit);
    }

    [Fact]
    public void ZeroWindow_NeverSuppresses()
    {
        // 窗口配 0（或负数）时退化为"每条都写"——节流开关关掉的正向契约
        var key = Key();
        Assert.True(StarLog.TryThrottle(key, 5, 0).Emit);
        Assert.True(StarLog.TryThrottle(key, 5, 0).Emit);
    }
}
