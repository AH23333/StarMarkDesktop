#nullable enable
using System;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 待办增强的纯逻辑部分（LocalItemState 的颜色/截止读写与中文描述）。
/// 测的是 extra_json 这条持久化通道的<｜hy_place▁holder▁no▁813｜>向后兼容**：旧数据没有这些键时必须安全回落，
/// 因为同一个 items 表里还躺着 A-3 之前创建的待办。
/// </summary>
public class LocalItemStateTests
{
    private static Item NewTodo(string? extra = null) => new()
    {
        Type = ItemType.Todo,
        Source = ItemSources.Local,
        SourceId = "inst|1",
        Title = "写测试",
        ExtraJson = extra,
    };

    // ── 向后兼容：旧数据没有任何新键 ──

    [Fact]
    public void GetColor_NoExtraJson_ReturnsZero()
        => Assert.Equal(0, LocalItemState.GetColor(NewTodo(extra: null)));

    [Fact]
    public void GetDue_NoExtraJson_ReturnsNull()
        => Assert.Null(LocalItemState.GetDue(NewTodo(extra: null)));

    [Fact]
    public void GetColor_DoneOnlyExtraJson_ReturnsZero()
    {
        var item = NewTodo();
        LocalItemState.SetDone(item, true);
        Assert.Equal(0, LocalItemState.GetColor(item));
        Assert.Null(LocalItemState.GetDue(item));
        Assert.True(LocalItemState.IsDone(item));   // 原有字段没被破坏
    }

    [Fact]
    public void GetColor_MalformedJson_DoesNotThrow()
        => Assert.Equal(0, LocalItemState.GetColor(NewTodo(extra: "{ not json")));

    // ── 颜色 ──

    [Fact]
    public void SetColor_RoundTrips()
    {
        var item = NewTodo();
        LocalItemState.SetColor(item, 3);
        Assert.Equal(3, LocalItemState.GetColor(item));
    }

    [Fact]
    public void SetColor_ZeroClearsPreviousColor()
    {
        var item = NewTodo();
        LocalItemState.SetColor(item, 5);
        LocalItemState.SetColor(item, 0);
        Assert.Equal(0, LocalItemState.GetColor(item));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void SetColor_OutOfRange_ClampsToZero(int bad)
    {
        var item = NewTodo();
        LocalItemState.SetColor(item, bad);
        Assert.Equal(0, LocalItemState.GetColor(item));
    }

    [Fact]
    public void SetColor_PreservesDoneFlag()
    {
        var item = NewTodo();
        LocalItemState.SetDone(item, true);
        LocalItemState.SetColor(item, 2);
        Assert.True(LocalItemState.IsDone(item));
        Assert.Equal(2, LocalItemState.GetColor(item));
    }

    // ── 截止日期 ──

    [Fact]
    public void SetDue_RoundTrips()
    {
        var item = NewTodo();
        var due = LocalItemState.DayStartUnix(DateTimeOffset.Now.AddDays(3));
        LocalItemState.SetDue(item, due);
        Assert.Equal(due, LocalItemState.GetDue(item));
    }

    [Fact]
    public void SetDue_NullRemovesKey()
    {
        var item = NewTodo();
        LocalItemState.SetDue(item, LocalItemState.DayStartUnix(DateTimeOffset.Now));
        LocalItemState.SetDue(item, null);
        Assert.Null(LocalItemState.GetDue(item));
    }

    /// <summary>截止时间按「天」存储：同一天的不同时刻必须得到同一个 Unix 值。</summary>
    [Fact]
    public void DayStartUnix_TruncatesToLocalMidnight()
    {
        // 必须用 Kind=Local 构造：截止日期按**本地时区**的当天 0 点比较，
        // 若按 UTC 构造，同一天的 8:30 与 23:59 转成本地（UTC+8）会跨到两天。
        var morning = new DateTimeOffset(new DateTime(2026, 9, 19, 8, 30, 0, DateTimeKind.Local));
        var night = new DateTimeOffset(new DateTime(2026, 9, 19, 23, 59, 0, DateTimeKind.Local));
        Assert.Equal(LocalItemState.DayStartUnix(morning), LocalItemState.DayStartUnix(night));
    }

    // ── 截止文案 ──

    [Fact]
    public void DescribeDue_Null_IsEmpty()
        => Assert.Equal(string.Empty, LocalItemState.DescribeDue(null, DateTimeOffset.Now));

    [Fact]
    public void DescribeDue_Today_And_Tomorrow()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("今天", LocalItemState.DescribeDue(LocalItemState.DayStartUnix(now), now));
        Assert.Equal("明天", LocalItemState.DescribeDue(LocalItemState.DayStartUnix(now.AddDays(1)), now));
        Assert.Equal("昨天", LocalItemState.DescribeDue(LocalItemState.DayStartUnix(now.AddDays(-1)), now));
    }

    [Fact]
    public void DescribeDue_Overdue_ShowsDayCount()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("逾期 3 天", LocalItemState.DescribeDue(LocalItemState.DayStartUnix(now.AddDays(-3)), now));
    }

    [Fact]
    public void DescribeDue_WithinAWeek_ShowsWeekday()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var due = LocalItemState.DayStartUnix(now.AddDays(4));
        var text = LocalItemState.DescribeDue(due, now);
        Assert.StartsWith("周", text);
        Assert.NotEqual("今天", text);
    }

    [Fact]
    public void DescribeDue_FarFuture_ShowsMonthAndDay()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var due = LocalItemState.DayStartUnix(now.AddDays(60));
        var text = LocalItemState.DescribeDue(due, now);
        Assert.Contains("月", text);
        Assert.EndsWith("日", text);
    }

    // ── 逾期判定 ──

    [Fact]
    public void IsOverdue_PastDueAndNotDone_True()
    {
        var item = NewTodo();
        LocalItemState.SetDue(item, LocalItemState.DayStartUnix(DateTimeOffset.Now.AddDays(-1)));
        Assert.True(LocalItemState.IsOverdue(item));
    }

    [Fact]
    public void IsOverdue_CompletedItem_False()
    {
        var item = NewTodo();
        LocalItemState.SetDue(item, LocalItemState.DayStartUnix(DateTimeOffset.Now.AddDays(-1)));
        LocalItemState.SetDone(item, true);
        Assert.False(LocalItemState.IsOverdue(item));   // 已完成就不算逾期
    }

    [Fact]
    public void IsOverdue_NoDue_False()
        => Assert.False(LocalItemState.IsOverdue(NewTodo()));

    // ── 手动排序序号（拖拽排序持久化）──

    [Fact]
    public void GetOrder_UnsetItem_ReturnsNull()
    {
        // 老数据没有 order 键，必须返回 null 而不是 0 —— 否则会把所有旧条目
        // 当成"排在最前面"，手动排序结果立刻被打乱。
        Assert.Null(LocalItemState.GetOrder(NewTodo()));
    }

    [Fact]
    public void SetOrder_RoundTrips()
    {
        var item = NewTodo();
        LocalItemState.SetOrder(item, 3);
        Assert.Equal(3, LocalItemState.GetOrder(item));

        LocalItemState.SetOrder(item, 0);
        Assert.Equal(0, LocalItemState.GetOrder(item));
    }

    [Fact]
    public void SetOrder_NegativeValue_Preserved()
    {
        // 新条目用「最小序号 - 1」插到顶部，序号会持续变负，不能被夹到 0
        var item = NewTodo();
        LocalItemState.SetOrder(item, -5);
        Assert.Equal(-5, LocalItemState.GetOrder(item));
    }

    [Fact]
    public void SetOrder_KeepsColorAndDue()
    {
        var item = NewTodo();
        LocalItemState.SetColor(item, 2);
        LocalItemState.SetDue(item, LocalItemState.DayStartUnix(DateTimeOffset.Now));

        LocalItemState.SetOrder(item, 7);   // 写 order 不能把同在 extra_json 的其它键冲掉

        Assert.Equal(7, LocalItemState.GetOrder(item));
        Assert.Equal(2, LocalItemState.GetColor(item));
        Assert.NotNull(LocalItemState.GetDue(item));
    }
}
