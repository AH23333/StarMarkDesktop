#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 P-80 的结构守门：贴图持久化这条链（首建登记 → 状态/内容钩子 → PNG 落盘 → 关窗摘档 →
/// 退出保记录 → 启动恢复）每一环都得在。<c>PinStore</c> 的纯逻辑行为由 <c>PinStoreTests</c> 钉，
/// 这里只钉 UI 层的接线——tests 工程不引用 StarMark.UI，锚点扫不到就抛，绝不假绿。
/// <para>钉的是"哪一环挂在哪个正门上"而不是行号：钩子跟着方法走，方法搬了家守门照样跟得上。</para>
/// </summary>
public sealed class PinPersistenceGateTests
{
    private const string Manager = "src/StarMark.UI/Services/PinManager.cs";
    private const string PinFile = "src/StarMark.UI/Views/CaptureOverlayWindow.Pin.cs";
    private const string SelectionFile = "src/StarMark.UI/Views/CaptureOverlayWindow.Selection.cs";
    private const string PointerFile = "src/StarMark.UI/Views/CaptureOverlayWindow.Pointer.cs";
    private const string MainFile = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string StoreFile = "src/StarMark.Core/Capture/PinStore.cs";

    [Fact]
    public void AddRegistersThePinBeforeTheToast()
    {
        // MethodBody 对这里失效：Add 的 `=> OnUi(() =>` 是多行 lambda，表达式体判定会在
        // lambda 里第一个分号处提前收尾——Between 从方法头锚到下一个方法的 doc，整段拿全。
        var add = SourceGate.Between(SourceGate.ReadRepoFile(Manager),
            "public static void Add(byte[] bgra", "显示 / 收起所有贴图");
        // 登记晚于 toast 就是"看得见、没记录"的窗口期：toast 弹完用户就关，这张没落盘。
        Assert.Contains("PersistNew(pin);", add);
        Assert.True(
            add.IndexOf("PersistNew(pin);", StringComparison.Ordinal)
                < add.IndexOf("pinnedTitle", StringComparison.Ordinal),
            "首建登记（PersistNew）必须在「已钉住」toast 之前——晚一拍，那一拍里关掉的这张就没落盘");
    }

    [Fact]
    public void ClosingAPinDropsItsRecord()
    {
        var un = SourceGate.MethodBody(SourceGate.ReadRepoFile(Manager),
            "internal static void Unregister(CaptureOverlayWindow pin)");
        Assert.Contains("DropPersisted(pin);", un);
    }

    [Fact]
    public void DropPersistedKeepsRecordsDuringShutdown()
    {
        var drop = SourceGate.MethodBody(SourceGate.ReadRepoFile(Manager),
            "private static void DropPersisted(CaptureOverlayWindow pin)");
        // 没有 _shutdown 早退，退出收尾里每关一张删一张——"退出即清空"。
        Assert.Contains("if (_shutdown) return;", drop);
        Assert.Contains("data.Pins.RemoveAll(r => r.Id == id)", drop);
        Assert.Contains("File.Delete(Store.PngPathOf(id));", drop);
    }

    [Fact]
    public void StateAndContentHooksGuardAgainstRestoreLoop()
    {
        var manager = SourceGate.ReadRepoFile(Manager);
        var state = SourceGate.MethodBody(manager, "internal static void NotePinStateChanged");
        Assert.Contains("if (_restoring || _shutdown) return;", state);   // 恢复链带出来的钩子一律早退
        Assert.Contains("Records.TryGetValue(pin, out var id)", state);   // 首建之前的变化由 PersistNew 一并收进

        var content = SourceGate.MethodBody(manager, "internal static void NotePinContentChanged");
        Assert.Contains("if (_restoring || _shutdown) return;", content);
        Assert.Contains("QueuePinPng(pin, id);", content);                // 内容变了＝元数据与像素都要重写
    }

    [Fact]
    public void UpsertReadsPositionFromTheSameSourceAsResize()
    {
        var up = SourceGate.MethodBody(SourceGate.ReadRepoFile(Manager),
            "private static void Upsert(CaptureOverlayWindow pin, string id)");
        // 位置真值＝GetWindowRect（与 ResizePinAnchoringTopLeft 同源）：换别的来源＝重启后贴图漂移。
        Assert.Contains("WindowInterop.GetWindowRect(pin)", up);
        Assert.Contains("rec.Hidden = pin.IsHidden;", up);
    }

    [Fact]
    public void PinFileCarriesExactlyTheFiveStateHooks()
    {
        var pin = SourceGate.ReadRepoFile(PinFile);
        // 五个正门：ResizePinAnchoringTopLeft / ApplyClickThrough / Present / HidePin / SetOpacity。
        Assert.Equal(5, SourceGate.Count(pin, "PinManager.NotePinStateChanged(this);"));
        // 内容钩子不在这一档：Rebake 住在 Selection.cs（重烤＝像素真变了）。
        Assert.Equal(0, SourceGate.Count(pin, "PinManager.NotePinContentChanged(this);"));

        Assert.Contains("PinManager.NotePinStateChanged(this);",
            SourceGate.MethodBody(pin, "private void ResizePinAnchoringTopLeft()"));
        Assert.Contains("PinManager.NotePinStateChanged(this);",
            SourceGate.MethodBody(pin, "public bool ApplyClickThrough(bool on)"));
        Assert.Contains("PinManager.NotePinStateChanged(this);",
            SourceGate.MethodBody(pin, "public void Present()"));
        Assert.Contains("PinManager.NotePinStateChanged(this);",
            SourceGate.MethodBody(pin, "public void HidePin()"));
        Assert.Contains("PinManager.NotePinStateChanged(this);",
            SourceGate.MethodBody(pin, "private void SetOpacity(double opacity)"));
    }

    [Fact]
    public void RebakeAndDragHooksGoThroughTheGuardedExits()
    {
        var rebake = SourceGate.MethodBody(SourceGate.ReadRepoFile(SelectionFile), "private void Rebake()");
        Assert.Contains("if (_pinned) PinManager.NotePinContentChanged(this);", rebake);

        var released = SourceGate.MethodBody(SourceGate.ReadRepoFile(PointerFile), "void Root_PointerReleased(");
        Assert.Contains("if (_pinned) PinManager.NotePinStateChanged(this);", released);
    }

    [Fact]
    public void ApplyPersistedStateWalksTheFrontDoors()
    {
        var apply = SourceGate.MethodBody(SourceGate.ReadRepoFile(PinFile),
            "internal void ApplyPersistedState(PinRecord rec)");
        // 正门纪律：透明度/穿透/收起各走自己的正门（连着条子、角标与系统样式），直写等于恢复一张少一套。
        Assert.Contains("SetOpacity(", apply);
        Assert.Contains("ApplyClickThrough(true)", apply);
        Assert.Contains("HidePin()", apply);
        Assert.Contains("SyncBadge()", apply);
        Assert.DoesNotContain("IsHidden =", apply);
    }

    [Fact]
    public void PngWritingUsesTheEncoderThatThrows()
    {
        var manager = SourceGate.ReadRepoFile(Manager);
        // SavePngAsync 把一切异常吞成一句 false——"没存下来"从此说不出原因（P-80 裁决）。
        // 禁项读抹注释版：WritePinPngFileAsync 的 doc 里那句"刻意不用 SavePngAsync"是讲道理，不是又开出口。
        Assert.Equal(0, SourceGate.Count(SourceGate.Code(manager), "SavePngAsync"));
        Assert.Contains("GdiScreenCapture.EncodePngAsync", SourceGate.Code(manager));

        var write = SourceGate.MethodBody(manager, "private static async void WritePinPngFileAsync");
        Assert.Contains("while (true)", write);                        // 写完检查 PngPending，追平内容变化
        Assert.Contains("PngPending.Remove(pin)", write);
        Assert.Contains("File.Create(Store.PngPathOf(id))", write);
        Assert.Contains("CopyToAsync(file)", write);
        Assert.Contains("ReportPersistFailure(ex)", write);            // async void 的异常自己收
    }

    [Fact]
    public void FailureSpeaksUpOnceThenThrottles()
    {
        var report = SourceGate.MethodBody(SourceGate.ReadRepoFile(Manager),
            "private static void ReportPersistFailure(Exception ex)");
        Assert.Contains("pin.card.persistFailedTitle", report);
        Assert.Contains("pin.card.persistFailedBody", report);
        Assert.Contains("_persistFailNoticed", report);                 // toast 本会话一次，其后节流日志
        Assert.Contains("WarnThrottled", report);
    }

    [Fact]
    public void ExitKeepsRecordsAndRestoreComesAfterWidgets()
    {
        var main = SourceGate.ReadRepoFile(MainFile);
        var exit = SourceGate.MethodBody(main, "private async void ExitApp()");
        // 退出收尾的第一步：晚于任何关窗＝名册里那几张在 EnterShutdown 生效之前就被 DropPersisted 删了。
        Assert.Contains("PinManager.EnterShutdown();", exit);
        Assert.True(
            exit.IndexOf("PinManager.EnterShutdown();", StringComparison.Ordinal)
                < exit.IndexOf("_allowExit = true;", StringComparison.Ordinal),
            "EnterShutdown 必须是 ExitApp 的第一句");

        // 启动恢复：单独一条 enqueue，且在组件恢复那条链之后（不塞进启动度量链里搅浑读数）。
        Assert.Contains("PinManager.RestorePersisted()", main);
        Assert.True(
            main.IndexOf("RestoreOnStartupAsync", StringComparison.Ordinal)
                < main.IndexOf("PinManager.RestorePersisted()", StringComparison.Ordinal),
            "贴图恢复必须在组件恢复之后 enqueue");
    }

    [Fact]
    public void RestoreStopsAtTheLimitWithoutDeleting()
    {
        var manager = SourceGate.ReadRepoFile(Manager);
        var restore = SourceGate.MethodBody(manager, "internal static void RestorePersisted()");
        Assert.Contains("Pins.Count >= CaptureGeometry.MaxPins) break;", restore);   // 满了停在原地，多出来的不删
        Assert.Contains("PruneOrphanPngs();", restore);

        var one = SourceGate.MethodBody(manager, "private static void RestoreOne(PinRecord rec, List<string> removed)");
        Assert.Contains("FileNotFoundException", one);                               // 缺档→删记录
        Assert.Contains("ClipboardPayload.TryDecodePng(bytes, out var frame, out var decodeProblem)", one);
        Assert.Contains("Records[pin] = rec.Id;", one);                              // 重建即登记
        Assert.Contains("pin.ApplyPersistedState(rec);", one);
    }

    [Fact]
    public void StoreWriteDisciplineLivesInPinStore()
    {
        var store = SourceGate.ReadRepoFile(StoreFile);
        Assert.Contains("Subdirectory(\"pins\")", store);              // 目录默认从 UserDataPaths 出（改道只有一处）
        Assert.Contains("File.Move(tmp, JsonPath, overwrite: true)", store);   // 先落 .tmp 再原子替换
        Assert.Contains("ClampZoom", store);                           // 量程的主人只有 CaptureGeometry

        var save = SourceGate.MethodBody(store, "public bool Save(PinStoreData data)");
        Assert.Contains("if (_loadDegraded)", save);                   // 降级态拒写防蒸发
        Assert.Contains("return false;", save);                        // 返回是否真的落盘——恒报成功是一句谎话
    }
}
