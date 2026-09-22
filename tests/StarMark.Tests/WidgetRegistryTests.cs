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

    // ───────── DL：注册表的「排除过滤器」——三条合取消除臂 + 新建入口臂从未被进入（Default 全通过）─────────
    // GetWindowDescriptors = Where(CanCreateWindow && HasImplementedContent && IsAvailable)，
    // GetCreateEntryDescriptors = Where(ShowInCreateEntry)。CreateDefaults 的描述符全部为默认（三条合取皆真、
    // ShowInCreateEntry 真），故各「消除臂」的假分支一次都未走到；既有 AllKinds 测只把 GetWindowDescriptors()
    // 与由它派生的 WidgetStorage.AllKinds 对拍=重言、不钉谓词。这里构造非默认描述符逐条钉门。

    private static WidgetDescriptor Creatable(WidgetKind kind) => new(kind, "T", "G", 100, 100);

    [Fact]
    public void GetWindowDescriptors_ExcludesEachGateIndependently_ButKeepsRegistered()
    {
        var registry = new WidgetRegistry(new[]
        {
            Creatable(WidgetKind.Todo),
            // 三条合取：每个变体只让恰好一条为假（其余两条仍真）→ 单独钉该门确在排除集中起作用
            new(WidgetKind.QuickNote, "N", "G", 100, 100, CanCreateWindow: false),                       // 关①
            new(WidgetKind.Clock, "C", "G", 100, 100, Stage: WidgetContentStage.Placeholder),            // 关②
            new(WidgetKind.Search, "S", "G", 100, 100, Availability: WidgetContentAvailability.Planned), // 关③
            // 无关旗标不在此谓词内 → 仍可建窗（不可缩放≠不可建），钉「谓词恰为三条、不多不少」
            new(WidgetKind.TagGrid, "Tag", "G", 100, 100, IsResizable: false),
        });

        var windowed = registry.GetWindowDescriptors().Select(d => d.Kind).ToArray();

        Assert.Equal(new[] { WidgetKind.Todo, WidgetKind.TagGrid }, windowed);

        // 被排除者仍在册（用户态配置可持久化），Get/IsKnown 不因排除而崩溃
        Assert.True(registry.IsKnown(WidgetKind.Clock));
        Assert.Equal(WidgetContentStage.Placeholder, registry.Get(WidgetKind.Clock).Stage);
        Assert.False(registry.CanCreateWindow(WidgetKind.QuickNote)); // 关①经 CanCreateWindow 亦为 false
    }

    [Fact]
    public void GetCreateEntryDescriptors_ExcludesShowInCreateEntryFalse()
    {
        var registry = new WidgetRegistry(new[]
        {
            Creatable(WidgetKind.Todo),
            new(WidgetKind.Clock, "C", "G", 100, 100, ShowInCreateEntry: false),
        });

        Assert.Equal(
            new[] { WidgetKind.Todo },
            registry.GetCreateEntryDescriptors().Select(d => d.Kind).ToArray());
    }
}
