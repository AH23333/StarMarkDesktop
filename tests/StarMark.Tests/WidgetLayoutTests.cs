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
}
