using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>组件布局方案的纯逻辑测试（归一化、命名去重、排序）。</summary>
public sealed class WidgetLayoutTests
{
    [Fact]
    public void Normalize_TrimsAndDeduplicatesNames()
    {
        var layouts = new List<WidgetLayout>
        {
            new() { Name = " 工作模式 " },
            new() { Name = "工作模式" },
            new() { Name = "   " },
        };

        var result = WidgetLayoutCollection.Normalize(layouts);

        Assert.Equal(3, result.Count);
        Assert.Equal("工作模式", result[0].Name);
        Assert.Equal("工作模式 (2)", result[1].Name);
        Assert.Equal("未命名布局", result[2].Name);
    }

    [Fact]
    public void Normalize_Deduplication_NeverCollidesWithLiteralNumberedName()
    {
        // 旧实现只在"基名"上计数：第三条 "A" 生成 "A (2)"，会与首条本就名为 "A (2)" 的布局撞车，
        // 违反"名称保证唯一"。修后按最终名去重，必须三者互不相同。
        var layouts = new List<WidgetLayout>
        {
            new() { Name = "A (2)" },
            new() { Name = "A" },
            new() { Name = "A" },
        };

        var result = WidgetLayoutCollection.Normalize(layouts);
        var names = result.Select(l => l.Name).ToList();

        Assert.Equal(3, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("A", names[0]);                   // 首条纯 "A" 保留原名
        Assert.Equal("A (2)", names[1]);               // 原本就名为 "A (2)" 的那条也不被改名
        Assert.Equal("A (3)", names[2]);               // 第三条 "A" 顺延到 (3)，避开与 (2) 撞名
        Assert.Equal(1, names.Count(n => n == "A (2)"));
    }

    [Fact]
    public void Normalize_SortsEntriesByKindThenIndex()
    {
        var layout = new WidgetLayout
        {
            Name = "测试",
            Entries = new List<WidgetLayoutEntry>
            {
                new() { Kind = WidgetKind.Todo, Index = 1 },
                new() { Kind = WidgetKind.Clock, Index = 0 },
                new() { Kind = WidgetKind.Todo, Index = 0 },
            },
        };

        var result = WidgetLayoutCollection.Normalize(new[] { layout });

        // WidgetKind: QuickLaunch=0 < Todo=1 < QuickNote=2 < Clock=3 < Search=4
        var entries = result[0].Entries;
        Assert.Equal(WidgetKind.Todo, entries[0].Kind);
        Assert.Equal(0, entries[0].Index);
        Assert.Equal(WidgetKind.Todo, entries[1].Kind);
        Assert.Equal(1, entries[1].Index);
        Assert.Equal(WidgetKind.Clock, entries[2].Kind);
    }

    [Fact]
    public void Normalize_FillsMissingId()
    {
        var result = WidgetLayoutCollection.Normalize(new[] { new WidgetLayout() });

        Assert.Single(result);
        Assert.False(string.IsNullOrWhiteSpace(result[0].Id));
        Assert.Equal("空布局", result[0].Summary);
    }

    [Fact]
    public void MakeUniqueName_AppendsCounterWhenTaken()
    {
        var existing = new[]
        {
            new WidgetLayout { Name = "阅读模式" },
            new WidgetLayout { Name = "阅读模式 (2)" },
        };

        Assert.Equal("阅读模式 (3)", WidgetLayoutCollection.MakeUniqueName(existing, "阅读模式"));
        Assert.Equal("专注", WidgetLayoutCollection.MakeUniqueName(existing, "专注"));
    }

    // ── DR：MakeUniqueName 的输入归一与碰撞检测臂从未被覆盖 ──
    // 既有 MakeUniqueName_AppendsCounterWhenTaken 只喂「已 trim、大小写一致、非空」的 CJK 名 → :127 的
    // 空白兜底真臂、.Trim()、:128 StringComparer.OrdinalIgnoreCase 三处皆未走到必要场景。GUID 兜底(:135)
    // 需 999 条同前缀碰撞才触发，属计数/规模用例、构造即注水，故刻意不测。

    [Fact]
    public void MakeUniqueName_BlankDesired_FallsBackToDefault()
    {
        // 用户新建布局时名字留空/纯空白 → 兜底默认名，而非落回空串（空名会让列表出现无名条目）。
        Assert.Equal("未命名布局", WidgetLayoutCollection.MakeUniqueName([], ""));
        // 兜底名本身已占用时须继续加计数后缀（空白兜底臂 → 碰撞循环串联）。
        var taken = new[] { new WidgetLayout { Name = "未命名布局" } };
        Assert.Equal("未命名布局 (2)", WidgetLayoutCollection.MakeUniqueName(taken, "   "));
    }

    [Fact]
    public void MakeUniqueName_TrimsDesired()
    {
        // 首尾空白应被裁掉：否则 "  专注  " 与既有 "专注" 视作不同名、或落盘带脏空白。
        Assert.Equal("专注", WidgetLayoutCollection.MakeUniqueName([], "  专注  "));
    }

    [Fact]
    public void MakeUniqueName_Collision_IsCaseInsensitive()
    {
        // 碰撞集合用 StringComparer.OrdinalIgnoreCase（:128）——既有 CJK 测里 OrdinalIgnoreCase 与大小写敏感
        // 结果一致，从未区分。若被"简化"成大小写敏感默认比较，existing "Foo" 时 desired "foo" 会误判不撞名、
        // 返回 "foo"（与 "Foo" 实为同名）→ 本用例钉住 OrdinalIgnoreCase：应撞名并加后缀。
        var existing = new[] { new WidgetLayout { Name = "Foo" } };
        Assert.Equal("foo (2)", WidgetLayoutCollection.MakeUniqueName(existing, "foo"));
    }

    // ───────── DK：损坏输入下的 null 护栏（JSON 反序列化可真实到达；任一护栏被重构删去即设置页加载 NRE）─────────

    /// <summary>:87 入参声明为可空 <c>IEnumerable&lt;WidgetLayout&gt;?</c>——反序列化缺省 / 传 null 时须返空列表而非抛。</summary>
    [Fact]
    public void Normalize_NullCollection_ReturnsEmpty()
    {
        Assert.Empty(WidgetLayoutCollection.Normalize(null));
    }

    /// <summary>:91 <c>l is null</c> continue——corrupt JSON 数组里的 <c>null</c> 元素须被跳过、保留有效布局（删去即 <c>l.Id</c> NRE）。</summary>
    [Fact]
    public void Normalize_SkipsNullLayoutElements()
    {
        var result = WidgetLayoutCollection.Normalize(new List<WidgetLayout> { null!, new() { Name = "有效" } });

        Assert.Single(result);
        Assert.Equal("有效", result[0].Name);
    }

    /// <summary>:93 <c>l.Entries ??= new()</c>——某布局 Entries 显式为 null 时须兜底为空列表，Summary 归「空布局」，且后续 Where/OrderBy 不抛。</summary>
    [Fact]
    public void Normalize_MaterializesNullEntriesList()
    {
        var result = WidgetLayoutCollection.Normalize(new List<WidgetLayout> { new() { Name = "兜底", Entries = null! } });

        Assert.Empty(result[0].Entries);
        Assert.Equal("空布局", result[0].Summary);
    }

    /// <summary>:95 <c>Where(e =&gt; e is not null)</c>——Entries 数组含 null 元素时须在 OrderBy 前剔除（否则 <c>e.Kind</c> 排序 NRE），有效条目仍按 Kind→Index 落位。</summary>
    [Fact]
    public void Normalize_FiltersNullGeometryEntries()
    {
        var layout = new WidgetLayout
        {
            Name = "含坏条目",
            Entries = new List<WidgetLayoutEntry>
            {
                null!,
                new() { Kind = WidgetKind.Clock, Index = 0 },
                new() { Kind = WidgetKind.Todo, Index = 0 },
            },
        };

        var entries = WidgetLayoutCollection.Normalize(new[] { layout })[0].Entries;

        // QuickLaunch=0 < Todo=1 < QuickNote=2 < Clock=3 < Search=4 → Todo 先于 Clock
        Assert.Equal(2, entries.Count);
        Assert.Equal(WidgetKind.Todo, entries[0].Kind);
        Assert.Equal(WidgetKind.Clock, entries[1].Kind);
    }

    // ───────── DW：WidgetLayout 仅剩的两处未断言分支（Summary 非空插值臂 + Normalize 末尾比较器）─────────
    // DK 钉了 Normalize 的 null 护栏、DR 钉了 MakeUniqueName 的空白/Trim/OrdinalIgnoreCase 碰撞集；本文件
    // 其余分支（去重 (N) 循环、Kind→Index 排序、Id 兜底）亦各有测。唯二裸奔：① WidgetLayout.Summary(:77) 的
    // **非空插值臂**——"空布局"真臂已被 Normalize_FillsMissingId(:84)/:158 触及，$"{Count} 个组件" 假臂却零断言
    // （注意 WidgetSnapshotTests 的 "N 个组件" 测的是**另一类型** WidgetSnapshot.Summary 的更丰富串，不覆盖本属性）；
    // ② Normalize 末尾 OrderBy(:121) 用 OrdinalIgnoreCase，但既有测全喂 CJK（大小写无意义）→ 无法区分它被
    // "简化"成 Ordinal（大写字母码点 < 小写 → 顺序翻转）这一回归。二者皆纯 Core、确定性、设置页每套非空布局都走。

    [Theory]
    [InlineData(0, "空布局")]     // 三元真臂：与假臂成 0↔1 分界对照
    [InlineData(1, "1 个组件")]   // 假臂下界：防 Count-1 之类 off-by-one（1→"0 个组件"即暴露）
    [InlineData(3, "3 个组件")]   // 假臂：证明数字随 Entries.Count 插值、非塌成常量
    public void Summary_InterpolatesEntryCount_WithEmptyLayoutFallback(int entryCount, string expected)
    {
        var layout = new WidgetLayout { Name = "L", Entries = new List<WidgetLayoutEntry>() };
        for (var i = 0; i < entryCount; i++)
            layout.Entries.Add(new WidgetLayoutEntry { Kind = WidgetKind.Todo, Index = i });

        Assert.Equal(expected, layout.Summary);
    }

    [Fact]
    public void Normalize_FinalOrder_IsOrdinalIgnoreCase_NotOrdinal()
    {
        // "Banana"(B=0x42) 与 "apple"(a=0x61)：OrdinalIgnoreCase 按字母 a<b → apple 先；
        // Ordinal 大小写敏感下 'B'<'a' → Banana 先。刻意用 ASCII 让期望与具体 culture 无关
        // （en-US 下 CurrentCulture 亦 apple 先），从而只隔离「大小写敏感轴」：专门拦
        // OrdinalIgnoreCase→Ordinal 的降级回归（若误降级，本断言顺序翻转即红）。
        var result = WidgetLayoutCollection.Normalize(new[]
        {
            new WidgetLayout { Name = "Banana" },
            new WidgetLayout { Name = "apple" },
        });

        Assert.Equal(new[] { "apple", "Banana" }, result.Select(l => l.Name).ToArray());
    }
}
