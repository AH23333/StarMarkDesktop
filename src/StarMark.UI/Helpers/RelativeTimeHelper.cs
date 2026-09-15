#nullable enable

namespace StarMark.UI.Helpers;

/// <summary>
/// 将 Unix 时间戳转换为相对时间字符串。移植自浏览器扩展 relativeTime()。
/// </summary>
public static class RelativeTimeHelper
{
    public static string Format(long unixSeconds)
    {
        var diff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixSeconds;
        if (diff < 60) return "刚刚";
        if (diff < 3600) return $"{diff / 60} 分钟前";
        if (diff < 86400) return $"{diff / 3600} 小时前";
        if (diff < 2592000) return $"{diff / 86400} 天前";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd");
    }
}
