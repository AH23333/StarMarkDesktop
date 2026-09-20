#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>活动事件的三色归类（#51）：增=绿 / 删=红 / 改=黄。展示与着色只认这一层，不再逐来源判断。</summary>
public enum ActivityChange
{
    Added,
    Removed,
    Modified,
}

/// <summary>
/// 活动流单条记录的展示模型。包装 <see cref="ActivityRecord"/>，提供中文标签、
/// 相对时间与三色归类（<see cref="Change"/>，用于着色）。见扩展对比方案 P1-3、#51。
/// </summary>
public sealed partial class ActivityItemViewModel : ObservableObject
{
    public ActivityItemViewModel(ActivityRecord rec)
    {
        Id = rec.Id;
        Kind = rec.Kind;
        Title = rec.Title;
        Uri = rec.Uri;
        KindLabel = KindLabelOf(rec.Kind);
        AtText = FormatRelative(rec.At);
        Change = ChangeOf(rec.Kind);
    }

    /// <summary>活动表主键，供最近活动格增量对齐（避免每次数据广播都重建列表而闪白）。</summary>
    public long Id { get; }

    public ActivityKind Kind { get; }

    public string Title { get; }

    public string? Uri { get; }

    /// <summary>事件类型中文标签，如「收藏 Star」「修改笔记」。</summary>
    public string KindLabel { get; }

    /// <summary>相对时间文案：刚刚 / x 分钟前 / x 小时前 / x 天前 / yyyy-MM-dd。</summary>
    public string AtText { get; }

    /// <summary>三色归类：绿(增)/红(删)/黄(改)。着色只认这一层。</summary>
    public ActivityChange Change { get; }

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
        ActivityKind.ItemAdd => "新增内容",
        ActivityKind.ItemModify => "修改内容",
        _ => "删除条目",
    };

    private static ActivityChange ChangeOf(ActivityKind k) => k switch
    {
        ActivityKind.ItemModify => ActivityChange.Modified,
        ActivityKind.StarRemove or ActivityKind.BookmarkRemove or ActivityKind.FileRemove
            or ActivityKind.ClipRemove or ActivityKind.ItemDelete => ActivityChange.Removed,
        _ => ActivityChange.Added,   // 各 *Add 与 ItemAdd
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
