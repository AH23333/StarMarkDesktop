#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 字节数的界面表达。1024 进制（与资源管理器同基数），最多两位有效小数。
/// 集中一处，避免卡片 / 预览 / 设置页各写一份而舍入规则互不一致。
/// </summary>
public static class FileSizeText
{
    public static string Human(long bytes)
        => bytes switch
        {
            < 0 => string.Empty,
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
        };
}
