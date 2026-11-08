#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 启动组件恢复分帧闸门（片243，扫源码不跑界面）。
/// <para>
/// 现场（starmark-20261108.log）：12 颗组件窗在同一 UI 回合里连建（布局路径 ApplyGeometryCore 一口气
/// foreach 完），UIStallWatchdog 每轮启动报 1.7-2.1s 阻塞——用户感知就是「启动那两秒点不动」。
/// 分帧＝一颗一拍 UI 回合。这里守四半，缺一算没做：
/// ① 启动恢复逐颗经 OnUiAsync 派发（含「无布局」与「布局失败回退」两条路）；
/// ② 布局套用抽出单条目帮手（ApplyGeometryEntryCore），同步版与分帧版同源——⛔ 第二份匹配实现；
/// ③ 设置页「应用布局」同走分帧版（点按钮不再整轮秒级卡死）；
/// ④ 数据读取/落盘/收尾计时全部留在 UI 回合里（WidgetStorage 非线程安全口径不变）。
/// </para>
/// </summary>
public sealed class StartupRestoreSliceGateTests
{
    private static string Lifecycle() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Services/WidgetManager.Lifecycle.cs"));
    private static string Layout() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Services/WidgetManager.Layout.cs"));

    /// <summary>启动恢复逐颗一拍：回退路径的 ShowInternal 必须包在 OnUiAsync 派发里（⛔ 裸同步循环复活）。</summary>
    [Fact]
    public void StartupRestoreSchedulesOneUiTurnPerWindow()
    {
        var body = SourceGate.MethodBody(Lifecycle(), "public async Task RestoreOnStartupAsync(bool loadOnStartup = true)");
        var squashed = SourceGate.Squash(body);
        // 无布局/回退路径：每颗 ShowInternal 都在独立的 UI 回合里
        Assert.Contains("await OnUiAsync(() => { try { ShowInternal(inst.Id); }", squashed);
        // 布局路径走分帧版
        Assert.Contains("await ApplyLayoutSlicedAsync(data, layout);", squashed);
        Assert.True(body.Contains("foreach (var inst in data.Instances)"), "逐颗循环还在（分帧改的是回合，不是数量）");
    }

    /// <summary>单条目帮手是唯一匹配实现：同步版与分帧版都走它——快照与布局已经漂过一次，不许漂第三次。</summary>
    [Fact]
    public void LayoutSlicingSharesTheSingleEntryEngine()
    {
        var layout = Layout();
        var engine = SourceGate.MethodBody(layout, "private List<(T entry, WidgetInstanceConfig inst)> ApplyGeometryCore<T>");
        Assert.Contains("ApplyGeometryEntryCore(data, entry, mapping, used)", engine);   // 同步版逐条目走同一帮手
        var sliced = SourceGate.MethodBody(layout, "private async Task ApplyLayoutSlicedAsync(WidgetStoreData data, WidgetLayout layout)");
        var squashed = SourceGate.Squash(sliced);
        Assert.Contains("await OnUiAsync(() => ApplyGeometryEntryCore(data, entry, mapping, used));", squashed);   // 分帧版不另写匹配实现
        Assert.Contains("HideTemporaryAll", sliced);          // 收尾（隐藏布局外实例）与同步版一致
        Assert.Contains("InstancesChanged?.Invoke()", sliced);
    }

    /// <summary>设置页「应用布局」同走分帧版：点按钮不再整轮秒级卡死。</summary>
    [Fact]
    public void ApplyLayoutFromSettingsIsSlicedToo()
    {
        var body = SourceGate.MethodBody(Layout(), "public async Task<bool> ApplyLayoutAsync(string layoutId)");
        var squashed = SourceGate.Squash(body);
        Assert.Contains("await ApplyLayoutSlicedAsync(data, layout);", squashed);
        Assert.DoesNotContain("ApplyLayoutCore(data, layout);", squashed);   // ⛔ 同步版整轮复活
    }

    /// <summary>数据读取/落盘/收尾计时全在 UI 回合里：方法体只编排回合，不直碰存储（非线程安全口径不变）。</summary>
    [Fact]
    public void StorageAndMarkStayOnUiTurns()
    {
        var body = SourceGate.MethodBody(Lifecycle(), "public async Task RestoreOnStartupAsync(bool loadOnStartup = true)");
        var squashed = SourceGate.Squash(body);
        Assert.Contains("await OnUiAsync(() => _storage.Load())", squashed);
        Assert.Contains("await OnUiAsync(() => _storage.FindLayout(", squashed);
        Assert.Contains("await OnUiAsync(() => _storage.Save(data))", squashed);
        Assert.Contains("StartupProfile.Mark(", squashed);      // 收尾计时仍在（改完有没有变快的对照读数）
    }
}
