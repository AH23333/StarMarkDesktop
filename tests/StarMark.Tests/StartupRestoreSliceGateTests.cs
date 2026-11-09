#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 启动组件恢复分帧闸门（片243 起，扫源码不跑界面）。
/// <para>
/// 现场（starmark-20261108.log）：12 颗组件窗在同一 UI 回合里连建（布局路径 ApplyGeometryCore 一口气
/// foreach 完），UIStallWatchdog 每轮启动报 1.7-2.1s 阻塞——用户感知就是「启动那两秒点不动」。
/// 分帧＝一颗一拍 UI 回合。片243 守四半；片244 真机复盘发现<b>回合切开≠合成切开</b>
/// （12 颗首帧仍一批 1860 ms、UI 自耗 CPU 1657 ms），补两半；片253 补观测一半（零行为改动），缺一算没做：
/// ① 启动恢复逐颗经 OnUiAsync 派发（含「无布局」与「布局失败回退」两条路）；
/// ② 布局套用抽出单条目帮手（ApplyGeometryEntryCore），同步版与分帧版同源——⛔ 第二份匹配实现；
/// ③ 设置页「应用布局」同走分帧版（点按钮不再整轮秒级卡死）；
/// ④ 数据读取/落盘/收尾计时全部留在 UI 回合里（WidgetStorage 非线程安全口径不变）；
/// ⑤ 每颗 Reveal 后接真墙钟间隔（Task.Delay＋WidgetStartupPolicy.RevealIntervalMs）——
///    让每颗首帧落进各自的合成回合，两条恢复路（回退与分帧布局）同款；
/// ⑥ 间隔数字只住 WidgetStartupPolicy 一颗（同一事实一份实现，80 ms 实测档）。
/// ⑦ 每颗间隔睡完补一把「显示排空」刻度（StartupProfile.Elapsed，起表在派发侧）——
///    新装实测 QuickLaunch 窗显后 785 ms 才轮到下一颗构建、已知刻度只解释 ~80 ms，
///    那段无主空档全靠这行读数归因，删了它下一轮启动又只剩猜。
///    片254 两条恢复路都得有＋恢复路自报一行：14:16 实测用户启动走回退路（无默认布局），
///    片253 只插布局路＝一颗读数没出（坑 #1178），路都没认对，读数再多也归因不了。
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

    /// <summary>⑤ 点亮间隔（片244）：回合切开≠合成切开——12 颗首帧曾一批 1860 ms（UI 自耗 1657 ms）。
    /// 两条恢复路每颗 Reveal 后都接真墙钟间隔，让每颗首帧落进各自的合成回合。</summary>
    [Fact]
    public void RevealsLeaveRealWallClockGaps()
    {
        var fallback = SourceGate.Squash(SourceGate.MethodBody(
            Lifecycle(), "public async Task RestoreOnStartupAsync(bool loadOnStartup = true)"));
        Assert.Contains("await Task.Delay(WidgetStartupPolicy.RevealIntervalMs)", fallback);
        var sliced = SourceGate.Squash(SourceGate.MethodBody(
            Layout(), "private async Task ApplyLayoutSlicedAsync(WidgetStoreData data, WidgetLayout layout)"));
        // 每颗一间隔：间隔紧跟每拍派发（⛔ 只在循环外睡一大觉——那等于把分帧又合回一整轮）
        Assert.Contains(
            "await OnUiAsync(() => ApplyGeometryEntryCore(data, entry, mapping, used)); await Task.Delay(WidgetStartupPolicy.RevealIntervalMs);",
            sliced);
    }

    /// <summary>⑥ 间隔数字的唯一出处：WidgetStartupPolicy 那颗 const（80 ms 实测档），别处不许再抄一份。</summary>
    [Fact]
    public void RevealIntervalLivesOnceInPolicy()
    {
        var policy = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Core/Widgets/WidgetStartupPolicy.cs"));
        Assert.Contains("public const int RevealIntervalMs = 80;", policy);
    }

    /// <summary>⑦ 排空刻度与恢复路自报（片253 布局路・片254 回退路补齐）：两条恢复路都得有——
    /// 14:16 实测用户启动走回退路，片253 只插布局路＝一颗读数没出。起表在派发侧、读数在 UI 回合里。</summary>
    [Fact]
    public void EveryRevealIsFollowedByADrainReading()
    {
        var restore = SourceGate.Squash(SourceGate.MethodBody(
            Lifecycle(), "public async Task RestoreOnStartupAsync(bool loadOnStartup = true)"));
        Assert.Contains("[恢复路] 布局：启动时套用默认布局", restore);          // 路自报：哪条路走了，日志直说
        Assert.Contains("[恢复路] 回退：无默认布局或套用失败，逐个显示", restore);
        Assert.Contains(
            "await OnUiAsync(() => StarMark.Abstractions.StartupProfile.Elapsed($\"组件显示排空 {inst.Kind}\", drainFrom));",
            restore);                                                           // 回退路的排空刻度（片254）

        var sliced = SourceGate.Squash(SourceGate.MethodBody(
            Layout(), "private async Task ApplyLayoutSlicedAsync(WidgetStoreData data, WidgetLayout layout)"));
        Assert.Contains(
            "await OnUiAsync(() => StartupProfile.Elapsed($\"组件显示排空 {entry.Kind}\", drainFrom));",
            sliced);                                                            // 布局路的排空刻度（片253）
    }
}
