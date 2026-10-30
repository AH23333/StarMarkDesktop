#nullable enable
using System;
using System.Linq;
using Windows.UI;
using StarMark.Core.Appearance;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>自定义色只刷新背景，禁止全局强调色资源覆盖与强制重解主题，保留纯调色板算术契约。</summary>
public sealed class AccentResourceSyncGateTests
{
    private const string Sync = "src/StarMark.UI/Helpers/AccentResourceSync.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string WidgetWindow = "src/StarMark.UI/Views/WidgetWindow.Appearance.cs";
    private const string WidgetManager = "src/StarMark.UI/Services/WidgetManager.Toggles.cs";
    private const string Palette = "src/StarMark.Core/Appearance/AppPalette.cs";

    private static readonly Color Sample = Color.FromArgb(0xFF, 0x12, 0xB7, 0x6A);

    /// <summary>纯算术逐字节固定朝白 +20/+40 与朝黑 −10/−20/−30 的五档混色，不宣称有 UI 消费者。</summary>
    [Fact]
    public void RampHasFiveDeterministicMixSteps()
    {
        var (l1, l2, d1, d2, d3) = AppPalette.AccentRamp(Sample);
        Assert.Equal(Color.FromArgb(0xFF, 0x41, 0xC5, 0x88), l1);
        Assert.Equal(Color.FromArgb(0xFF, 0x71, 0xD4, 0xA6), l2);
        Assert.Equal(Color.FromArgb(0xFF, 0x10, 0xA5, 0x5F), d1);
        Assert.Equal(Color.FromArgb(0xFF, 0x0E, 0x92, 0x55), d2);
        Assert.Equal(Color.FromArgb(0xFF, 0x0D, 0x80, 0x4A), d3);

        var all = new[] { l1, l2, d1, d2, d3 };
        Assert.Equal(5, all.Select(c => (c.R << 16) | (c.G << 8) | c.B).Distinct().Count());
        Assert.All(all, c => Assert.Equal(0xFF, c.A));              // 派生出来的一律实色
        // 亮档严格越混越亮、暗档严格越混越暗（这把尺⛔ 读"差不多"）
        Assert.True(SurfaceReadability.Luminance(l1) < SurfaceReadability.Luminance(l2));
        Assert.True(SurfaceReadability.Luminance(d1) > SurfaceReadability.Luminance(d2));
        Assert.True(SurfaceReadability.Luminance(d2) > SurfaceReadability.Luminance(d3));
        Assert.True(SurfaceReadability.Luminance(l2) > SurfaceReadability.Luminance(Sample));
        Assert.True(SurfaceReadability.Luminance(d3) < SurfaceReadability.Luminance(Sample));
    }

    /// <summary>②混色那颗的两端与越界：t=0 原样、t=1 全取目标、负数夹到端点（⛔ 混出过冲或负值）。</summary>
    [Fact]
    public void MixTowardHasPinnedEnds()
    {
        Assert.Equal(Sample, AppPalette.MixToward(Sample, 255, 255, 255, 0.0));
        Assert.Equal(Color.FromArgb(0xFF, 255, 255, 255), AppPalette.MixToward(Sample, 255, 255, 255, 1.0));
        Assert.Equal(Color.FromArgb(0xFF, 0, 0, 0), AppPalette.MixToward(Sample, 0, 0, 0, 99.0));
        Assert.Equal(Sample, AppPalette.MixToward(Sample, 0, 0, 0, -3.0));   // 负数夹回 0＝原样
        Assert.Equal(0xFF, AppPalette.MixToward(Sample, 0, 0, 0, 0.5).A);   // 结果永远实色
    }

    /// <summary>纯黑与纯白的派生梯保留至少三种不同 RGB，并在不能继续变亮或变暗时停在端点。</summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void RampStaysDistinctAtTheExtremes(byte level)
    {
        var c = Color.FromArgb(0xFF, level, level, level);
        var (l1, l2, d1, d2, d3) = AppPalette.AccentRamp(c);
        var distinct = new[] { c, l1, l2, d1, d2, d3 }
            .Select(x => (x.R << 16) | (x.G << 8) | x.B).Distinct().Count();
        Assert.True(distinct >= 3, $"染料 #{level:X2} 时派生梯只剩 {distinct} 颗");
        if (level == 0xFF) Assert.Equal(c, l2);                      // 已经全白：亮档无处可去（照实相等）
        if (level == 0x00) Assert.Equal(c, d3);                      // 已经全黑：暗档同理
    }

    /// <summary>删除全局强调色写入器并普查生产源码，防止旧资源名单、写入或重解入口搬家复活。</summary>
    [Fact]
    public void NoProductionAccentResourceOverrideOrRethemePathRemains()
    {
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(RepoRoot(), Sync)));
        foreach (var (_, text) in ReadRepoUnder("src"))
        {
            var code = Code(text);
            Assert.DoesNotContain("AccentResourceSync", code, StringComparison.Ordinal);
            Assert.DoesNotContain("\"SystemAccentColor", code, StringComparison.Ordinal);
            Assert.DoesNotContain("RethemeAccent", code, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("accentChanged", Code(ReadRepoFile(ViewModel)), StringComparison.Ordinal);
        foreach (var path in new[] { App, ViewModel, WidgetWindow, WidgetManager })
            Assert.DoesNotContain("Retheme", Code(ReadRepoFile(path)), StringComparison.Ordinal);
    }

    /// <summary>启动先填背景缓存，保存先更新缓存再刷新主窗和组件，不靠覆盖资源或翻转主题刷新颜色。</summary>
    [Fact]
    public void StartupAndSaveKeepBackgroundUpdatesWithoutRetheming()
    {
        var app = Code(ReadRepoFile(App));
        const string startupApply = "Helpers.ThemeAccent.Apply(fileSettings.LoadThemeAccent());";
        const string migration = "MigrationRunner>().EnsureSchema()";
        Assert.Equal(1, Count(app, startupApply));
        Assert.Contains(migration, app, StringComparison.Ordinal);
        Assert.True(app.IndexOf(startupApply, StringComparison.Ordinal)
                  < app.IndexOf(migration, StringComparison.Ordinal),
            "背景缓存必须在迁移与首窗创建之前填好");

        var save = MethodBody(Code(ReadRepoFile(ViewModel)), "private void Save()");
        const string saveApply = "ThemeAccent.Apply(accentText);";
        Assert.Contains("AccentIndex == 1 ? AppPalette.ToHexText(ThemeAccentColor) : null", save, StringComparison.Ordinal);
        Assert.Equal(1, Count(save, "_settings.SaveThemeAccent(accentText);"));
        Assert.Equal(1, Count(save, saveApply));
        Assert.Contains("App.MainWindow?.DispatcherQueue.TryEnqueue(", save, StringComparison.Ordinal);
        foreach (var refresh in new[]
        {
            "App.MainWindow.RefreshThemeIcon(theme);",
            "App.MainWindow?.RefreshAppearance();",
            "mgr.RefreshAppearanceAsync();",
        })
        {
            Assert.Equal(1, Count(save, refresh));
            Assert.True(save.IndexOf(saveApply, StringComparison.Ordinal) < save.IndexOf(refresh, StringComparison.Ordinal),
                "背景缓存更新必须先于 " + refresh);
        }
        Assert.Equal(2, ReadRepoUnder("src").Sum(f => Count(Code(f.Text), "ThemeAccent.Apply(")));
    }

    /// <summary>移除强调色重解后，组件刷新仍逐窗套用背景，独立主题偏好仍逐窗传播。</summary>
    [Fact]
    public void WidgetBackgroundRefreshAndThemePropagationRemainConnected()
    {
        var manager = Code(ReadRepoFile(WidgetManager));
        var refresh = MethodBody(manager, "public Task RefreshAppearanceAsync()");
        Assert.Contains("OnUiAsync(", refresh, StringComparison.Ordinal);
        Assert.Contains("foreach (var w in _windows.Values.ToList())", refresh, StringComparison.Ordinal);
        Assert.Equal(1, Count(refresh, "w.RefreshAppearance();"));
        var window = Code(ReadRepoFile(WidgetWindow));
        var appearance = MethodBody(window, "public void RefreshAppearance()");
        Assert.Contains("if (!_styled) return;", appearance, StringComparison.Ordinal);
        Assert.Equal(1, Count(appearance, "ApplyAppearanceCore();"));
        var applyTheme = MethodBody(manager, "public Task ApplyThemeToAllAsync(ThemePreference pref)");
        Assert.Contains("OnUiAsync(", applyTheme, StringComparison.Ordinal);
        Assert.Contains("foreach (var w in _windows.Values.ToList())", applyTheme, StringComparison.Ordinal);
        Assert.Contains("w.ApplyTheme(pref);", applyTheme, StringComparison.Ordinal);
        var windowTheme = MethodBody(window, "public void ApplyTheme(StarMark.UI.Helpers.ThemePreference pref)");
        Assert.Contains("ThemeManager.Apply(this, pref);", windowTheme, StringComparison.Ordinal);
        Assert.Contains("RefreshAppearance();", windowTheme, StringComparison.Ordinal);
    }

    /// <summary>派生算术仍只住在 Core，UI 不再调用派生梯，也不复制混色算法。</summary>
    [Fact]
    public void TheRampRemainsCoreMathWithoutUiConsumers()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Palette)), "public static (Color Light1"));
        Assert.Equal(0, ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("AppPalette.cs", StringComparison.Ordinal))
            .Sum(f => Count(Code(f.Text), "AppPalette.AccentRamp(")));
        Assert.Equal(0, ReadRepoUnder("src/StarMark.UI")
            .Sum(f => Count(Code(f.Text), "MixToward(")));
    }
}
