#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 全局集中常量（魔法数与默认值的单一事实来源）。
/// <para>避免散落在各模块的字面量：分页上限、去抖间隔、相对时间阈值、默认最大返回数等。
/// 新增「魔法数」时优先在此声明具名常量并引用，禁止在业务逻辑里写裸数字。</para>
/// </summary>
public static class AppConstants
{
    /// <summary>应用名（用于数据库目录、设置目录、日志目录等路径拼接，不用于产品展示名）。</summary>
    public const string AppName = "StarMark";

    /// <summary>文件夹树单次加载上限（条）。</summary>
    public const int FolderTreePageSize = 2000;

    /// <summary>Everything SDK 单次查询硬上限（条），超过则被 SDK 截断。</summary>
    public const int EverythingMaxResults = 20000;

    /// <summary>IItemSource 默认最大返回条数。</summary>
    public const int DefaultMaxResults = 100;

    /// <summary>文件夹树标签变更去抖间隔（毫秒）。</summary>
    public const int TreeDebounceMs = 250;

    /// <summary>30 天对应的秒数（相对时间格式化阈值）。</summary>
    public const long ThirtyDaysInSeconds = 2592000;
}
