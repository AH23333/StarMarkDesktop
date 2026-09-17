#nullable enable
using System;
using System.Linq;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 组件类型注册表测试（对应 DeskBox 的 WidgetRegistryTests / WidgetKindCoverageTests）。
/// 目的：新增组件类型而未登记描述符时，测试立即失败，防止 switch 分支再次扩散。
/// </summary>
public sealed class WidgetRegistryTests
{
    [Fact]
    public void EveryEnumValue_HasDescriptor()
    {
        var missing = Enum.GetValues<WidgetKind>()
            .Where(kind => !WidgetRegistry.Default.IsKnown(kind))
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void Get_ThrowsForUnregisteredKind()
    {
        Assert.Throws<NotSupportedException>(() => WidgetRegistry.Default.Get((WidgetKind)99));
        Assert.False(WidgetRegistry.Default.TryGet((WidgetKind)99, out _));
    }

    [Fact]
    public void Descriptors_CarryDefaultSizeAndResizePolicy()
    {
        // 时钟已开放缩放（字号随窗口自适应），默认尺寸也放大到 240×170
        Assert.Equal(240, WidgetRegistry.Default.Get(WidgetKind.Clock).DefaultWidth);
        Assert.Equal(170, WidgetRegistry.Default.Get(WidgetKind.Clock).DefaultHeight);
        Assert.True(WidgetRegistry.Default.Get(WidgetKind.Clock).IsResizable);
        Assert.True(WidgetRegistry.Default.Get(WidgetKind.Todo).IsResizable);
        Assert.True(WidgetRegistry.Default.CanCreateWindow(WidgetKind.QuickLaunch));
    }

    [Fact]
    public void StorageHelpers_ReadFromRegistry()
    {
        // 标题与图标来自同一份描述符，不再有第二处 switch
        Assert.Equal("★ 快捷启动", WidgetStorage.KindTitle(WidgetKind.QuickLaunch));
        Assert.Equal(240, WidgetStorage.DefaultWidth(WidgetKind.Clock));
        Assert.Equal(170, WidgetStorage.DefaultHeight(WidgetKind.Clock));
        Assert.True(WidgetStorage.IsResizable(WidgetKind.Clock));
        Assert.True(WidgetStorage.IsResizable(WidgetKind.Search));
    }

    [Fact]
    public void AllKinds_CoversEveryCreatableDescriptor()
    {
        var expected = WidgetRegistry.Default.GetWindowDescriptors()
            .Select(descriptor => descriptor.Kind)
            .ToArray();
        Assert.Equal(expected, WidgetStorage.AllKinds);
    }

    [Fact]
    public void UnknownKind_FallsBackWithoutThrowing()
    {
        // 存储层读取配置时不应因未知类型崩溃
        Assert.Equal("99", WidgetStorage.KindTitle((WidgetKind)99));
        Assert.True(WidgetStorage.IsResizable((WidgetKind)99));
    }
}
