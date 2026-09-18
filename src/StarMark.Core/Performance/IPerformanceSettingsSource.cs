#nullable enable
namespace StarMark.Core.Performance;

/// <summary>
/// 性能模式设置来源（与 UI 层 <c>SettingsStore</c> 解耦，便于单元测试注入假数据）。
/// {@link PerformanceSettingsPolicy} 通过该接口读取预算，UI 在启动时注入真实实现。
/// </summary>
public interface IPerformanceSettingsSource
{
    /// <summary>当前性能模式。</summary>
    PerformanceMode LoadPerformanceMode();

    /// <summary>自定义模式下的进程工作集预算（MB）。</summary>
    double LoadCacheBudgetMb();

    /// <summary>自定义模式下的有界缓存最大条目数。</summary>
    int LoadMaxImageCacheCount();
}
