using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;

namespace StarMark.UI.Services;

/// <summary>
/// 静态帮助类的服务访问收口（Phase 3）：禁止散落的 GetService 模式匹配，
/// 统一走扩展方法（GetRequired 语义：取不到直接抛，问题显性化）。
/// </summary>
public static class ServiceLookup
{
    public static IItemRepository GetRequiredItemRepository(this IServiceProvider sp)
        => sp.GetRequiredService<IItemRepository>();
}
