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

    [Fact]
    public void SetDone_MalformedJson_DoesNotThrow_AndReopens()
    {
        // 写入侧曾只有 SetDone 直接 JsonNode.Parse 无兜底：坏 extra_json 时「勾选完成」即抛 JsonException。
        // 现统一走 ParseObject——坏数据丢弃重开，done 仍能写入。
        var item = NewTodo(extra: "{ not json");
        var ex = Record.Exception(() => LocalItemState.SetDone(item, true));
        Assert.Null(ex);
        Assert.True(LocalItemState.IsDone(item));
    }

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

    // ── 周内星期映射（批次 DE：钉死「周X」的取字符映射，而非仅前缀）──

    private static DateTimeOffset LocalNoon(int y, int m, int d)
        => new(new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Local));

    /// <summary>
    /// 既有 <c>DescribeDue_WithinAWeek</c> 只断言 <c>StartsWith("周")</c>，从不校验后一个字——
    /// 即映射串 <c>"日一二三四五六"</c>（周日索引 0、周一..周六索引 1..6）整体未测：
    /// 旋转/错位一位（如误写成 "一二三四五六日"）都能通过全部现有断言。该文案是待办组件列表
    /// 每天给用户看的日期，映射错=直接可见的错误日期。本 [Theory] 用 days=3（恒落在 2..6 的
    /// 「本周内」窗口）覆盖全部 7 个绝对星期，把七字映射逐一钉死，含 <c>== Sunday ? 0</c> 那条特殊索引。
    /// 全用 <c>Kind=Local</c> 正午构造，避免 UTC→本地换日的时区脆弱性。
    /// </summary>
    [Theory]
    [InlineData(2026, 9, 17, 2026, 9, 20, "周日")]   // due 落周日→索引 0（特殊三元路径）
    [InlineData(2026, 9, 18, 2026, 9, 21, "周一")]
    [InlineData(2026, 9, 19, 2026, 9, 22, "周二")]
    [InlineData(2026, 9, 20, 2026, 9, 23, "周三")]
    [InlineData(2026, 9, 21, 2026, 9, 24, "周四")]
    [InlineData(2026, 9, 22, 2026, 9, 25, "周五")]
    [InlineData(2026, 9, 23, 2026, 9, 26, "周六")]
    public void DescribeDue_WithinAWeek_MapsExactWeekdayChar(
        int nowY, int nowM, int nowD, int dueY, int dueM, int dueD, string expected)
    {
        var now = LocalNoon(nowY, nowM, nowD);
        var due = LocalItemState.DayStartUnix(LocalNoon(dueY, dueM, dueD));
        Assert.Equal(expected, LocalItemState.DescribeDue(due, now));
    }

    // ── 越界拒绝臂（批次 DN）：SetColor 写时夹到 0、SetDue 写时删键，故 getter 的「拒绝」分支
    //     经 Set→Get 往返永远进不去；只有 corrupt/手改/前向版本 extra_json 会绕过写侧校验直达此臂。
    //     若有人把 `>= 0 and <= ColorCount` 改成裸 `raw.Value`，get 会返回 8 → 6 元调色板 IndexOutOfRange；
    //     把 due 的 `> 0` 去掉，get 返回 0 → 渲染成 1970 日期。故把拒绝臂逐一钉死。──

    /// <summary>
    /// GetColor 的越界拒绝：<c>raw is &gt;= 0 and &lt;= ColorCount</c> 的 false 分支。
    /// 边界 <c>6==ColorCount</c> 必须**接受**（证明是闭区间、非 off-by-one 的 <c>&lt; ColorCount</c>），
    /// 而 -1/7/99 必须回落 0。这些 raw 值都由 SetColor 写侧夹掉，无法经往返喂进，只能直构 extra_json。
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]    // 下界拒绝
    [InlineData(6, 6)]     // 上界=ColorCount：闭区间接受，防误改成 < ColorCount
    [InlineData(7, 0)]     // ColorCount+1 拒绝（前向版本多写一种颜色也不能越界取值）
    [InlineData(99, 0)]    // 远越界拒绝
    public void GetColor_RejectsOutOfRangeRawValue(int stored, int expected)
        => Assert.Equal(expected, LocalItemState.GetColor(NewTodo(extra: $"{{\"color\":{stored}}}")));

    /// <summary>
    /// GetDue 的非正拒绝：<c>raw is &gt; 0</c> 的 false 分支返回 null（而非 0→1970 日期）。
    /// due=0 是精确边界（>0 排除），SetDue 对 ≤0 走 Remove 键，故经往返拿不到 raw 非正，只能直构。
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-1000L)]
    public void GetDue_RejectsNonPositiveRawValue(long stored)
        => Assert.Null(LocalItemState.GetDue(NewTodo(extra: $"{{\"due\":{stored}}}")));

    /// <summary>GetDue 接受侧边界：最小正数 1 须原样返回，证明拒绝臂是「非正」而非「误伤正值」。</summary>
    [Fact]
    public void GetDue_AcceptsPositiveRawValue()
        => Assert.Equal(1L, LocalItemState.GetDue(NewTodo(extra: """{"due":1}""")));

    // ── source_id 编解码（批次 DV）：多实例隔离基元。此前只在 WidgetSnapshotServiceTests 里
    //     被间接用作 fixture（:44 Encode）/黑盒往返断言（:115 Decode=="TARGET"），本单元测试文件零直接断言。──

    /// <summary>
    /// 契约护栏（DV-1）：<see cref="LocalItemState.DecodeInstanceId"/> 的 <c>idx &lt; 0 ? null</c> 臂
    /// （<c>LocalItemState.cs:22</c>）——source_id 不含分隔符即判为「非本地编码」返回 null。
    /// 这是本地/非本地条目判别子（<c>TodoWidgetViewModel:129</c> / <c>QuickNoteWidgetViewModel:55</c>
    /// 以 <c>DecodeInstanceId(i.SourceId) == _instanceId</c> 过滤本实例待办/随记）。该臂此前**从未被任何用例进入**：
    /// 现有间接覆盖（:115）只喂已编码的 <c>"TARGET|…"</c>（走 <c>idx&gt;=0</c> 子串臂）。回归面真实且静默——
    /// 若把兜底从 <c>null</c> 改成返回整串，GitHub/书签等非本地条目的 <c>DecodeInstanceId</c> 会吐出其原始
    /// source_id，虽未必恰等某实例 id，但语义已坏（非本地被判成"有实例"）；若删掉 <c>idx&lt;0</c> 守卫直接切片，
    /// 无管道输入 <c>""[..-1]</c> 会抛 <c>ArgumentOutOfRangeException</c> 冒到 UI 线程。用空串 + 无管道真实非本地 id
    /// 各钉一次（空串专证「越界守卫防抛」这一独立承重点），二者皆须干净返回 null。纯字符串、确定性自证无需探针。
    /// </summary>
    [Fact]
    public void DecodeInstanceId_WithoutSeparator_ReturnsNullNotWholeString()
    {
        Assert.Null(LocalItemState.DecodeInstanceId(""));                    // 空串：守卫防 ""[..-1] 抛
        Assert.Null(LocalItemState.DecodeInstanceId("octocat/Hello-World")); // 真实非本地 source_id（无 '|')
    }

    /// <summary>
    /// 契约护栏（DV-2）：<see cref="LocalItemState.EncodeSourceId"/> 的字面分隔符契约
    /// （<c>LocalItemState.cs</c> 的 <c>$"{instanceId}|{localId}"</c>）。<c>'|'</c> 是**三处独立消费者**共享的
    /// 承重边界：除本类的 Decode 外，<c>ItemRepository.Snapshots.cs</c> 把 <c>@prefix</c> 硬编码成
    /// <c>instanceId + "|%"</c> 喂 <c>source_id LIKE @prefix</c> 做按实例前缀读写（那里的注释明载「'|' 作边界
    /// 避免 '12' 命中 '123|…'」）。Encode/Decode 的往返只校验 Decode 侧、对 Encode 的**字面量**完全不敏感——若有人把
    /// Encode 与 Decode 的分隔符同步改成 ':'（:115 仍全绿），SQL 前缀 <c>"id|%"</c> 会静默匹配零行，
    /// 快照 Capture/Restore 的实例隔离失效、且此回归无纯测可拦。故在单元层直接钉死 Encode 的确切输出串 +
    /// Decode 回取实例段，锁住与 SQL 前缀共享的那一根 <c>'|'</c>。**刻意不测**多管道 "a|b|c"（真实
    /// instanceId 为十六进制 GUID、localId 为数字 → source_id 恒恰含一个 <c>'|'</c> → 多管道结构不可达，测之即
    /// 为不可能输入加校验·注水），亦不测前导 <c>'|'</c> 的 <c>"|42"→""</c>（空实例 id 生产不可达 + 产品语义歧义）。
    /// </summary>
    [Fact]
    public void EncodeSourceId_FixesPipeDelimitedWireFormat_ConsumedBySqlPrefixAndDecode()
    {
        // 字面量钉死分隔符：这就是 ItemRepository 前缀 LIKE 与 DecodeInstanceId 共同依赖的那根 '|'。
        Assert.Equal("f3a9|42", LocalItemState.EncodeSourceId("f3a9", 42));
        Assert.Equal("f3a9", LocalItemState.DecodeInstanceId("f3a9|42"));
    }
}
