#nullable enable
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 材质（WidgetBackdropKind）测试。
/// 重点防回归两件事：
/// <list type="number">
/// <item>枚举整数值**只能追加、不能重排**——设置下拉靠「SelectedIndex == 枚举值」映射，
/// 一旦改动旧值就会把用户已保存的材质换掉；</item>
/// <item>新增的纯色 Solid 能被 Enum.IsDefined 接受（SettingsStore.LoadWidgetBackdrop 靠它校验，
/// 不认识的值会静默回退成亚克力）。</item>
/// </list>
/// </summary>
public sealed class WidgetMaterialTests
{
    [Theory]
    [InlineData(WidgetBackdropKind.Acrylic, 0)]
    [InlineData(WidgetBackdropKind.Mica, 1)]
    [InlineData(WidgetBackdropKind.None, 2)]
    [InlineData(WidgetBackdropKind.MicaAlt, 3)]
    [InlineData(WidgetBackdropKind.AcrylicBase, 4)]
    [InlineData(WidgetBackdropKind.Solid, 5)]
    public void Backdrop_EnumValues_AreStable(WidgetBackdropKind kind, int expected)
    {
        Assert.Equal(expected, (int)kind);
    }

    [Fact]
    public void Backdrop_Solid_IsDefined()
    {
        // SettingsStore.LoadWidgetBackdrop 用 Enum.IsDefined 校验持久化值，
        // 未定义的值会被静默回退成 Acrylic —— 新增材质必须落进枚举，否则设置存了也读不回来。
        Assert.True(Enum.IsDefined(typeof(WidgetBackdropKind), WidgetBackdropKind.Solid));
        Assert.True(Enum.IsDefined(typeof(WidgetBackdropKind), (WidgetBackdropKind)5));
    }

    [Fact]
    public void Appearance_SolidBackdrop_RoundTrips()
    {
        var cfg = new WidgetInstanceConfig
        {
            Kind = WidgetKind.Todo,
            Appearance = new WidgetAppearanceOverride { Backdrop = WidgetBackdropKind.Solid },
        };
        var json = JsonSerializer.Serialize(cfg);
        var back = JsonSerializer.Deserialize<WidgetInstanceConfig>(json);

        Assert.NotNull(back);
        Assert.NotNull(back!.Appearance);
        Assert.Equal(WidgetBackdropKind.Solid, back.Appearance!.Backdrop);
    }

    [Fact]
    public void Appearance_LegacyBackdropValue_StillDeserializes()
    {
        // 模拟旧版 widgets.json：只存过整数材质值。即便旧值语义不变，
        // 反序列化也不能因为新增了 Solid 而失败或漂移。
        const string json = """{"Kind":3,"Appearance":{"Backdrop":2}}""";
        var back = JsonSerializer.Deserialize<WidgetInstanceConfig>(json);

        Assert.NotNull(back);
        Assert.NotNull(back!.Appearance);
        Assert.Equal(WidgetBackdropKind.None, back.Appearance!.Backdrop);
    }
}
