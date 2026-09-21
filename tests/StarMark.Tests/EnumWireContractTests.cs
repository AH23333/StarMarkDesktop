#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Xunit;
using StarMark.Abstractions;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 回归 EV：ItemType 与 WidgetChromeMode 皆经 System.Text.Json 按底层整数持久化
/// （全仓无 JsonStringEnumConverter）——ItemType 落备份 JSON 的 "type"，WidgetChromeMode 落
/// widgets.json，且备份校验和只重算已解析载荷、查不出序号漂移。二者此前用隐式序号，与已显式
/// 钉值的兄弟枚举（WidgetKind 0–11、WidgetBackdropKind 0–5）不一致：任何中途插入/重排成员都会
/// 让既有备份/配置的整数静默重映射到别的成员。此处把"持久化序号不可变"钉成机检契约——
/// 若有人改值或插入无显式值的成员使序号偏移，本测即失败。
/// </summary>
public class EnumWireContractTests
{
    [Theory]
    [InlineData(ItemType.File, 0)]
    [InlineData(ItemType.Bookmark, 1)]
    [InlineData(ItemType.GitHubStar, 2)]
    [InlineData(ItemType.Clipboard, 3)]
    [InlineData(ItemType.Todo, 4)]
    [InlineData(ItemType.Note, 5)]
    public void ItemType_PersistedByNumberOrdinal_IsFrozen(ItemType type, int expected)
        => Assert.Equal(expected, (int)type);

    [Fact]
    public void ItemType_HasNoGap_AndIsFullyEnumerated()
    {
        var ordinals = Enum.GetValues<ItemType>().Select(t => (int)t).OrderBy(x => x);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ordinals);
    }

    /// <summary>直接锁真实线格式：备份序列化器（无枚举转换器）必须把 Type 写成整数。</summary>
    [Theory]
    [InlineData(ItemType.GitHubStar, 2)]
    [InlineData(ItemType.Note, 5)]
    public void Item_SerializesType_AsOrdinalInteger(ItemType type, int expected)
    {
        var element = JsonSerializer.SerializeToElement(new Item { Type = type });
        Assert.Equal(JsonValueKind.Number, element.GetProperty("type").ValueKind);
        Assert.Equal(expected, element.GetProperty("type").GetInt32());
    }

    [Theory]
    [InlineData(WidgetChromeMode.Standard, 0)]
    [InlineData(WidgetChromeMode.Compact, 1)]
    [InlineData(WidgetChromeMode.Hidden, 2)]
    public void WidgetChromeMode_PersistedByNumberOrdinal_IsFrozen(WidgetChromeMode mode, int expected)
        => Assert.Equal(expected, (int)mode);
}
