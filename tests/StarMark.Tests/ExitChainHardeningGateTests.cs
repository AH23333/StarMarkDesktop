#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// P-302 乙（片278）：退出链/窗口销毁顺序加固的判例。
/// <para>
/// 0xc000027b 定案（片276）＝崩溃发生在主线程 dispatcher 泵处理 AppWindow 相关 CoreMessaging 消息内部
/// （E_FAIL×2・stowed fail-fast・零托管帧零 XAML 控件帧＝环境层 WinAppSDK），时序＝用户刚关完 12 组件窗后 55 秒。
/// 根因在框架层，产品码只能缓解，本刀落两处：
/// ①组件窗的两条关闭路（代码关停 <c>Shutdown</c>／系统真关闭 <c>WidgetWindow_Closed</c>）都先摘掉
/// AppWindow 事件订阅——半死窗口不再被拆毁消息找得到、匿名委托不再把 Window/AppWindow 一对钉在内存里等消息；
/// ②主窗退出链在关完组件窗后让 UI 线程的泵转一拍（<c>Task.Delay</c>）再动下一层——
/// 把还在队列里的 AppWindow 拆毁消息在 XAML 树仍完整时消化掉，而不是撕到一半才被泵进去。
/// </para>
/// <para>本门钉这两处形状：匿名订阅回潮、摘订阅被删、泵一拍被删或挪位，这里先红。</para>
/// </summary>
public sealed class ExitChainHardeningGateTests
{
    /// <summary>WidgetWindow 拆成多颗 partial——订阅/摘订阅/关闭回调分住不同文件，读全 partial 才钉得住。</summary>
    private static string WidgetWindowAll() =>
        SourceGate.Code(SourceGate.ReadRepoPartials("src/StarMark.UI/Views/WidgetWindow.xaml.cs"));

    [Fact]
    public void WidgetWindowAppWindowSubscriptionsAreNamedAndDetachedOnBothClosePaths()
    {
        var all = WidgetWindowAll();

        // 两条关闭路都必须先摘订阅：代码关停（Shutdown）与系统真关闭（WidgetWindow_Closed）。
        var shutdown = SourceGate.MethodBody(all, "public void Shutdown()");
        Assert.Contains("DetachAppWindowEvents()", shutdown);
        var closed = SourceGate.MethodBody(all, "private void WidgetWindow_Closed(");
        Assert.Contains("DetachAppWindowEvents()", closed);

        // 摘的方法必须三颗都摘：可见性（升格命名的）、尺寸夹取（OnAppWindowChanged）、Closing（升格命名的）。
        var detach = SourceGate.MethodBody(all, "private void DetachAppWindowEvents()");
        Assert.Contains("AppWindow.Changed -= OnAppWindowVisibilityChanged", detach);
        Assert.Contains("AppWindow.Changed -= OnAppWindowChanged", detach);
        Assert.Contains("AppWindow.Closing -= OnAppWindowClosing", detach);

        // 订阅侧必须已是命名方法：匿名订阅在关停路上摘不掉（-= 无从下手）。
        Assert.Contains("AppWindow.Changed += OnAppWindowVisibilityChanged", all);
        Assert.Contains("AppWindow.Changed += OnAppWindowChanged", all);
        Assert.Contains("AppWindow.Closing += OnAppWindowClosing", all);
        Assert.DoesNotContain("AppWindow.Changed += (_,", all);
        Assert.DoesNotContain("AppWindow.Closing += (_,", all);
    }

    [Fact]
    public void MainWindowExitPumpsOneBeatAfterWidgetShutdownBeforeHardExit()
    {
        var main = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/MainWindow.xaml.cs"));
        var exit = SourceGate.MethodBody(main, "private async void ExitApp()");

        var shutdownAt = exit.IndexOf("ShutdownAllAsync", StringComparison.Ordinal);
        var pumpAt = exit.IndexOf("await Task.Delay(", StringComparison.Ordinal);
        var clipboardAt = exit.IndexOf("ApplyClipboardHistory", StringComparison.Ordinal);
        var hardExitAt = exit.LastIndexOf("Environment.Exit(0)", StringComparison.Ordinal);

        Assert.True(shutdownAt >= 0, "ExitApp 里找不到组件窗关停调用——退出链被改形，先回来读这颗门");
        Assert.True(pumpAt > shutdownAt, "泵一拍必须在关停组件窗之后（拆毁消息要等窗关完才在场）");
        Assert.True(clipboardAt > pumpAt, "泵一拍必须在停剪贴板采集之前（先把组件窗的账消化完再动下一层）");
        Assert.True(hardExitAt > pumpAt, "Environment.Exit(0)（末次＝链尾那次）必须在泵一拍之后");
    }
}
