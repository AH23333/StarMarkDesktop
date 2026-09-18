#nullable enable
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// Phase B-9：每实例外观覆盖（WidgetAppearanceOverride）序列化行为测试。
/// 验证「全 null = 清除覆盖（不落盘）」「填充值正确往返」「空对象反序列化为全 null」。
/// </summary>
public sealed class WidgetAppearanceTests
{
    [Fact]
    public void Appearance_Null_NotSerialized()
    {
        var cfg = new WidgetInstanceConfig { Kind = WidgetKind.QuickLaunch, Appearance = null };
        var json = JsonSerializer.Serialize(cfg);
        Assert.DoesNotContain("Appearance", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Appearance_Populated_RoundTrips()
    {
        var cfg = new WidgetInstanceConfig
        {
            Kind = WidgetKind.Todo,
            Appearance = new WidgetAppearanceOverride
            {
                Backdrop = WidgetBackdropKind.Mica,
                BackgroundColor = "#FF3B82F6",
                ForegroundColor = "#FFFFFFFF",
                BorderColor = "#FF000000",
                BorderThickness = 2,
                CornerRadius = 12,
                TextScale = 1.2,
            },
        };
        var json = JsonSerializer.Serialize(cfg);
        var back = JsonSerializer.Deserialize<WidgetInstanceConfig>(json);
        Assert.NotNull(back);
        Assert.NotNull(back!.Appearance);
        Assert.Equal(WidgetBackdropKind.Mica, back.Appearance!.Backdrop);
        Assert.Equal("#FF3B82F6", back.Appearance.BackgroundColor);
        Assert.Equal("#FFFFFFFF", back.Appearance.ForegroundColor);
        Assert.Equal(2, back.Appearance.BorderThickness);
        Assert.Equal(12, back.Appearance.CornerRadius);
        Assert.Equal(1.2, back.Appearance.TextScale);
    }

    [Fact]
    public void Appearance_EmptyObject_RoundTripsToAllNull()
    {
        var cfg = new WidgetInstanceConfig { Kind = WidgetKind.Clock, Appearance = new WidgetAppearanceOverride() };
        var json = JsonSerializer.Serialize(cfg);
        var back = JsonSerializer.Deserialize<WidgetInstanceConfig>(json);
        Assert.NotNull(back);
        Assert.NotNull(back!.Appearance);
        Assert.Null(back.Appearance!.Backdrop);
        Assert.Null(back.Appearance.BackgroundColor);
        Assert.Null(back.Appearance.BorderColor);
        Assert.Null(back.Appearance.BorderThickness);
        Assert.Null(back.Appearance.CornerRadius);
        Assert.Null(back.Appearance.TextScale);
    }
}
