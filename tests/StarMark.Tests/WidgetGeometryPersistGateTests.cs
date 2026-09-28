#nullable enable
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 组件几何持久化的结构守门（批次 PA-7）。被测的 <c>WidgetWindow</c> / <c>WidgetManager</c> 都在
/// <c>StarMark.UI</c>，测试工程按分层红线不引用它 ⇒ 只能扫源码。
/// <para>
/// 为什么值得守：<b>"每个窗口各写一趟整档"回来时不会有任何功能异常</b>——隐藏全部组件照样成功，
/// 只是 widgets.json 被整档读写 N 遍（这文件还连带存着全部布局与快照）。
/// 更糟的是 <c>CloseInternal(persist:…)</c> 那个旗标曾经<b>压根没人读</b>：
/// 调用点写了 <c>persist:false</c> 却照样落盘，"应用快照后又被旧几何盖回去"就是这么来的。
/// </para>
/// </summary>
public sealed class WidgetGeometryPersistGateTests
{
    private const string ManagerPath = "src/StarMark.UI/Services/WidgetManager.cs";
    private const string WindowPath = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";

    /// <summary>
    /// <b>给"守门本身"作的保</b>（承 <c>CaptureOverlayGateTests.TheCodeSideGatesReadEveryPartialFile</c>）：
    /// 批次 S4-④ 把组件窗按访问面拆成 <c>WidgetWindow.*.cs</c> 若干份之后，读法一旦退回单个文件，
    /// 那些"锚点必须命中 1 处"的守门会<b>静默变成永远扫不到</b>——而 SourceGate 的规矩是扫不到就抛，
    /// 所以这里钉住"搬进分段文件的方法，读整套时必须还看得见"，覆盖每个分段各一个代表。
    /// </summary>
    [Fact]
    public void TheWidgetWindowGatesReadEveryPartialFile()
    {
        var all = ReadRepoPartials(WindowPath);

        Assert.Contains("internal bool WriteBoundsInto(WidgetStoreData data)", all);      // Geometry
        Assert.Contains("private void ApplyAppearanceCore(", all);                        // Appearance
        Assert.Contains("private void PopulateMenu(MenuFlyout menu)", all);              // Menu
        Assert.Contains("private void ApplyChromeMode(WidgetChromeMode mode)", all);      // Chrome
        Assert.Contains("private void PersistPositionsAfterDrag()", all);                // DragResize
        Assert.Contains("private async void QuickLaunch_Drop(", all);                     // QuickLaunchDrop
        Assert.Contains("public void Reveal()", all);                                     // 主文件自己
    }

    /// <summary>隐藏一批组件只能走"整批一次落盘"那一个出口。</summary>
    [Fact]
    public void HidingAlwaysGoesThroughTheBatchExit()
    {
        var manager = ReadRepoPartials(ManagerPath);
        var outside = WithoutMethod(manager, "private void HideTemporaryAll(");

        Assert.True(Count(manager, "HideTemporaryAll(") >= 6,
            $"整批隐藏的出口只被引用 {Count(manager, "HideTemporaryAll(")} 次，调用点变少了——守门要先跟上");
        Assert.Equal(0, Count(outside, ".HideTemporary()"));   // 别处一律不许逐个隐藏
    }

    /// <summary>关窗的每个调用点都必须<b>自己说清要不要留几何</b>，不许吃默认值。</summary>
    [Fact]
    public void EveryCloseSiteStatesWhetherItPersists()
    {
        var manager = ReadRepoPartials(ManagerPath);
        var outside = WithoutMethod(WithoutMethod(manager, "private void CloseInternal("), "private void CloseAll(");

        var calls = outside.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => (l.Contains("CloseInternal(") || l.Contains("CloseAll(")) && !l.TrimStart().StartsWith("///"))
            .ToList();

        Assert.True(calls.Count >= 4, $"只扫到 {calls.Count} 处关窗调用，扫描可能已失效");
        Assert.All(calls, line => Assert.Contains("persist:", line));
    }

    [Fact]
    public void ThePersistFlagIsActuallyRead()
    {
        var body = MethodBody(ReadRepoPartials(ManagerPath), "private void CloseInternal(");

        Assert.Contains("if (persist)", body);                 // 曾经这个参数收下就丢，四个调用点的意图全部落空
        Assert.Contains("window.Shutdown()", body);
    }

    /// <summary>窗口自己的隐藏/关闭动作不许再各自落盘——那是 N 趟整档读写的源头。</summary>
    [Fact]
    public void TheWindowMethodsDoNotPersistThemselves()
    {
        var window = ReadRepoPartials(WindowPath);

        Assert.Equal(0, Count(MethodBody(window, "public void HideTemporary()"), "PersistBounds("));
        Assert.Equal(0, Count(MethodBody(window, "public void Shutdown()"), "PersistBounds("));
        Assert.Contains("SW_HIDE", MethodBody(window, "public void HideTemporary()"));   // 反空转：扫到的确实是隐藏
    }

    /// <summary>Ctrl+拖动收尾：一次 <c>Mutate</c>，参与者全在里面；<b>逐个 PersistBounds 会写 N+1 趟</b>。</summary>
    [Fact]
    public void ACoordinatedDragPersistsOnceForEveryone()
    {
        var window = ReadRepoPartials(WindowPath);
        var body = MethodBody(window, "private void PersistPositionsAfterDrag()");

        Assert.Equal(1, Count(body, "_storage.Mutate("));
        Assert.Equal(0, Count(body, "PersistBounds("));
        Assert.Contains("_coordPeers", body);                  // 参与者必须在这一次里，不能被漏到循环外
    }

    /// <summary>"写进存档"与"落盘"必须分开：<b>能把整批塞进一次读档的前提就是它不碰磁盘</b>。</summary>
    [Fact]
    public void TheBoundsWriterNeverTouchesTheDisk()
    {
        var body = MethodBody(ReadRepoPartials(WindowPath), "internal bool WriteBoundsInto(WidgetStoreData data)");

        Assert.Equal(0, Count(body, "_storage.Load()"));
        Assert.Equal(0, Count(body, "_storage.Save("));
        Assert.Contains("inst.X = ", body);                    // 反空转：几何确实是在这里写的
    }

    /// <summary>
    /// <b>给"守门本身"作的保</b>（批次 S4-④）：组件管理器拆成 <c>WidgetManager.*.cs</c> 若干份后，
    /// 每个分段各钉一个代表方法，钉住"读整个 partial 类时必须还看得见"。
    /// 这一条尤其要紧：<see cref="WidgetSnapshotSymmetryGateTests"/> 那种"扫两侧字段比对"的守门，
    /// 读法退回单文件会扫到 <b>0 个字段</b>，而"零差异"与"完全对称"在断言上长得一模一样。
    /// </summary>
    [Fact]
    public void TheWidgetManagerGatesReadEveryPartialFile()
    {
        var all = ReadRepoPartials(ManagerPath);

        Assert.Contains("OnUiAsync(", all);                                                // 主文件
        Assert.Contains("private void HideTemporaryAll(IReadOnlyList<WidgetWindow> windows)", all);  // Lifecycle
        Assert.Contains("public Task ToggleAllTopmostAsync(", all);                         // Toggles
        Assert.Contains("public IReadOnlyList<WidgetLayout> GetLayouts()", all);             // Layout
        Assert.Contains("private async Task<bool> CaptureSnapshotInternalAsync(string name)", all); // Snapshot
        Assert.Contains("public async Task<int> AddLinksAsync(", all);                       // QuickLaunch
        Assert.Contains("public Task SaveCountdownsAsync(", all);                            // PerWidget
        Assert.Contains("private async Task LogActivitiesAsync(ActivityKind kind", all);      // Activity
    }
}
