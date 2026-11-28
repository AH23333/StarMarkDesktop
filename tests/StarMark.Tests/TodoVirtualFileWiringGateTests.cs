#nullable enable
using System;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 虚拟文件物化链（片290 / P-307）的<b>接线</b>闸门。与 <see cref="TodoAttachmentWiringGateTests"/>
/// 同族：测试工程不引用 StarMark.UI，UI 层的结构只能扫源码守。
/// <para>
/// 纯逻辑（预算边界／魔数签名／MOTW 正文）归 <see cref="TodoVirtualFilePolicyTests"/>；
/// 这里钉的是「空路径分流 → 物化 → MOTW 落托管副本 → 暂存清尾」这条腿上每一跳的接线——
/// 少一跳就回到 P-305 时代的原样：浏览器拖图静默没反应（收到 N 项但都没有可用路径）。
/// </para>
/// </summary>
public sealed class TodoVirtualFileWiringGateTests
{
    private const string ViewPath = "src/StarMark.UI/Views/TodoWidget.xaml.cs";
    private const string VmPath = "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs";
    private const string StoragePath = "src/StarMark.UI/Services/TodoAttachmentStorage.cs";
    private const string MaterializerPath = "src/StarMark.UI/Services/TodoVirtualFileMaterializer.cs";

    private static string Behind() => SourceGate.Code(SourceGate.ReadRepoFile(ViewPath));
    private static string Vm() => SourceGate.Code(SourceGate.ReadRepoFile(VmPath));
    private static string Storage() => SourceGate.Code(SourceGate.ReadRepoFile(StoragePath));
    private static string Materializer() => SourceGate.Code(SourceGate.ReadRepoFile(MaterializerPath));

    [Fact]
    public void RowDropSplitsEmptyPathFilesAndFoldsMaterializationFailuresIntoTheOutcome()
    {
        var drop = SourceGate.MethodBody(Behind(), "Row_Drop");
        // 空路径认得出：真路径直进、空路径 StorageFile 分流去物化（P-305 时代是静默丢弃——真缺口本身）
        Assert.Contains("string.IsNullOrWhiteSpace(storageItem.Path)", drop);
        Assert.Contains("is StorageFile f", drop);
        Assert.Contains("TodoVirtualFileMaterializer.MaterializeAsync(", drop);
        // 物化出来的真路径带着「自己人」记号进 VM——MOTW＋暂存清尾的依据
        Assert.Contains("materializedStaging", drop);
        Assert.Contains("new HashSet<string>(staged.Paths)", drop);
        // 物化失败的份数折进结局：沉默失败是「拖了没反应」的近亲
        Assert.Contains("outcome with {", drop);
        Assert.Contains("staged.Failed", drop);
    }

    [Fact]
    public void MaterializerRunsOnBudgetCapsEachFileAndCleansUpItsShells()
    {
        var batch = SourceGate.MethodBody(Materializer(), "Task<TodoVirtualFileMaterialization> MaterializeAsync");
        // 预算（Core 判据）先于任何落盘；逐文件隔离：一份失败不连坐整批
        Assert.Contains("ResolveStagingBudgetBytes", batch);
        Assert.Contains("failed++", batch);
        // 一批全灭不留空目录（空壳治理）；全成/部分成时暂存件还得留着给 CopyIn 用
        Assert.Contains("TryDeleteEmptyDirQuietly(dir)", batch);

        var stage = SourceGate.MethodBody(Materializer(), "Task<(string Path, long Written)?> TryStageAsync");
        // 声明大小放不下就不落盘；流抄写钳着余量；缺扩展名按魔数补（Core 判据）、补名失败不弃件
        Assert.Contains("winrtSource.Size", stage);
        Assert.Contains("capBytes", stage);
        Assert.Contains("TryInferExtension", stage);
        Assert.Contains("File.Move(target, renamed)", stage);
        // 惰性建目录：一份真要落才建；空壳治理：半截不留盘
        Assert.Contains("Directory.CreateDirectory(dir)", stage);
        Assert.Contains("TryDeleteQuietly(target)", stage);

        // 暂存必须躺在 %TEMP% 里：NeedsForcedCopy 自动接管＝Copy 入托管树的既有链一行不改（承重选择）
        Assert.Contains("Path.GetTempPath()", Materializer());
    }

    [Fact]
    public void VmMarksMotwOnTheManagedCopyAndDiscardsTheStagedSource()
    {
        var body = SourceGate.MethodBody(Vm(), "AddDroppedFilesAsync");
        Assert.Contains("materializedStaging", body);
        // ADS 不随 File.Copy 走——MOTW 必须标在托管副本上（标在 %TEMP% 原件上＝Copy 完就白标）
        Assert.Contains("TodoAttachmentStorage.TryMarkInternetZone(stored)", body);
        // 暂存原件用完即清——不然每次虚拟拖放都在 %TEMP% 留孤儿直到系统清盘
        Assert.Contains("TodoVirtualFileMaterializer.DiscardStaged(path)", body);
        // 收尾必须长在 CopyIn 之后：还没有 stored（托管副本）就没有可标的对象
        var copyAt = body.IndexOf("CopyIn(id, path)", StringComparison.Ordinal);
        var motwAt = body.IndexOf("TryMarkInternetZone(stored)", StringComparison.Ordinal);
        Assert.True(copyAt >= 0 && motwAt > copyAt, "MOTW 必须发生在 CopyIn 产出托管副本之后");
    }

    [Fact]
    public void MotwContentIsDecidedInCoreAndWritingNeverBreaksTheImport()
    {
        var zone = SourceGate.MethodBody(Storage(), "TryMarkInternetZone");
        // 正文拼法（ZoneId=3＋HostUrl＋剥 CR/LF）在 Core 纯函数——这里不再长第二份真相
        Assert.Contains("BuildZoneIdentifierContent", zone);
        Assert.Contains(":Zone.Identifier", zone);
        // 失败只记日志不抛：可选的安全注解不许把导入本身打断
        Assert.Contains("return false", zone);

        // 预算参数钉死（v6 参数表・行为规格参照——漂一格就是写穿宿主盘或该拒不拒）
        Assert.Equal(512L * 1024 * 1024, TodoVirtualFilePolicy.SafetyReserveBytes);
        Assert.Equal(64L * 1024 * 1024, TodoVirtualFilePolicy.CriticalReserveBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, TodoVirtualFilePolicy.FallbackBudgetBytes);
        Assert.Equal(3, TodoVirtualFilePolicy.InternetZone);
    }

    [Fact]
    public void DiscardStagedCleansTheFileAndOnlyEverEmptyDirectories()
    {
        var body = SourceGate.MethodBody(Materializer(), "DiscardStaged");
        Assert.Contains("TryDeleteQuietly(stagedPath)", body);
        // 只收真空壳（先验空再删）：递归删会把手伸进还没清完的暂存目录
        Assert.Contains("Directory.GetFileSystemEntries(dir).Length == 0", body);
        Assert.DoesNotContain("recursive: true", body);
    }
}
