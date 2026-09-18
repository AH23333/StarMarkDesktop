#nullable enable
namespace StarMark.Core.Performance;

/// <summary>性能模式（常驻应用必补的内存 / 缓存预算开关）。</summary>
public enum PerformanceMode
{
    /// <summary>均衡：默认预算，毛玻璃常驻，体验优先。</summary>
    Balanced = 0,
    /// <summary>省资源：收紧预算、降低缓存，低内存机器 / 笔记本省电优先。</summary>
    ResourceSaver = 1,
    /// <summary>自定义：由 CacheBudgetMb / MaxImageCacheCount 精确控制。</summary>
    Custom = 2,
}
