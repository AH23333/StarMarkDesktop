#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 活动流单条记录的展示模型。包装 <see cref="ActivityRecord"/>，提供中文标签、
/// 相对时间与「是否为移除类事件」（用于着色）。见扩展对比方案 P1-3。
/// </summary>
public sealed partial class ActivityItemViewModel : ObservableObject
{
    public ActivityItemViewModel(ActivityRecord rec)
    {
        Kind = rec.Kind;
        Title = rec.Title;
        Uri = rec.Uri;
        KindLabel = KindLabelOf(rec.Kind);
        AtText = FormatRelative(rec.At);
        IsRemoval = IsRemovalKind(rec.Kind);
    }

    public ActivityKind Kind { get; }

    public string Title { get; }

    public string? Uri { get; }

    /// <summary>事件类型中文标签，如「收藏 Star」「删除书签」。</summary>
    public string KindLabel { get; }

    /// <summary>相对时间文案：刚刚 / x 分钟前 / x 小时前 / x 天前 / yyyy-MM-dd。</summary>
    public string AtText { get; }

    /// <summary>是否为移除类事件（着色用：移除=警示色，新增=成功色）。</summary>
    public bool IsRemoval { get; }

    private static string KindLabelOf(ActivityKind k) => k switch
    {
        ActivityKind.StarAdd => "收藏 Star",
        ActivityKind.StarRemove => "取消 Star",
        ActivityKind.BookmarkAdd => "新增书签",
        ActivityKind.BookmarkRemove => "删除书签",
        ActivityKind.FileAdd => "新增文件",
        ActivityKind.FileRemove => "移除文件",
        ActivityKind.ClipAdd => "新增剪贴板",
        ActivityKind.ClipRemove => "移除剪贴板",
        _ => "删除条目",
    };

    private static bool IsRemovalKind(ActivityKind k) => k switch
    {
        ActivityKind.StarRemove or ActivityKind.BookmarkRemove or ActivityKind.FileRemove
            or ActivityKind.ClipRemove or ActivityKind.ItemDelete => true,
        _ => false,
    };

    private static string FormatRelative(long at)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(at);
        var diff = DateTimeOffset.UtcNow - dt;
        if (diff.TotalSeconds < 60) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} 天前";
        return dt.ToString("yyyy-MM-dd");
    }
}
