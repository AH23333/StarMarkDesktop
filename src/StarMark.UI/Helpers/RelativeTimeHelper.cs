#nullable enable
using System;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 卡片上"什么时候更新"的那一句。移植自浏览器扩展 <c>relativeTime()</c>。
/// <para><b>这里只负责"用本机此刻、按本机时区"</b>这两件宿主才知道的事；四档措辞、档位与
/// 超过 30 天落回哪个日期全在 <see cref="DateTimeText.Relative"/>（批次 SJ 把活动列表那第二份 if 链并掉，
/// 顺带修掉两处日期差一天的分岔）。活动列表与卡片现在共问这一颗 ⇒ 同一个时刻在两处必然同一句。</para>
/// </summary>
public static class RelativeTimeHelper
{
    public static string Format(long unixSeconds)
        => DateTimeText.Relative(unixSeconds, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
}
