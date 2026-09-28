#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-P3-a（备份附件打包）的<b>形状</b>守门：这一批里"只有源码形状能验"的那几条裁决。
/// <para>行为面在 <c>ClipboardBackupAttachmentTests</c>（碰真文件系统），这里只管四件事：
/// ① <b>回滚点永远不带附件</b>（快照与每日自动件仍写 <c>.json</c>）；② 附件是<b>搬流</b>而不是整张读进内存；
/// ③ "哪种内容用哪个扩展名"只有一个出处；④ 界面上那颗开关真的接到了两条具名导出路上。</para>
/// <para>每条都写清"破了会怎样"，因为这类闸门最坏的失效不是红，是<b>安静地白过</b>。</para>
/// </summary>
public sealed class ClipboardBackupAttachmentGateTests
{
    private const string Service = "src/StarMark.Core/Backup/BackupService.cs";
    private const string Container = "src/StarMark.Core/Backup/BackupContainer.cs";
    private const string Policy = "src/StarMark.Core/Backup/BackupPathPolicy.cs";
    private const string Store = "src/StarMark.Integrations/Clipboard/ClipboardImageStore.cs";
    private const string Ui = "src/StarMark.UI/Views/SettingsPage.xaml.cs";

    // ==================== ① 回滚点永远不带附件 ====================

    [Fact]
    public void SnapshotsAndAutoBackupsStillWriteDataOnlyJson()
    {
        // 决议 §3-Q2 把附件只给"用户主动导出"那一条路。快照与自动件是"随时能撤一步"用的：
        // 一份几十 MB 的库配几百 MB 的图，会把恢复前快照变成每按一次就填一盘磁盘，
        // 还会让"最近 7 份自动件"变成 7 份图片仓库。
        var service = ReadRepoFile(Service);
        var snapshot = MethodBody(service, "public async Task<string> WriteSnapshotAsync");
        Assert.Contains("ExportToFileAsync", snapshot);
        Assert.DoesNotContain("ExportWithClipImagesAsync", snapshot);
        Assert.Contains(".json\"", snapshot);          // 名字里的载体也是 .json：内容不许占着别的名字
        Assert.DoesNotContain(".zip", snapshot);

        var auto = MethodBody(service, "public async Task<string?> RunAutoBackupAsync");
        Assert.DoesNotContain("ExportWithClipImagesAsync", auto);
        Assert.Contains("SerializeEnvelope", auto);         // 清单的字节与手动导出同一份序列化，不另开一路
        Assert.Contains("{AutoBackupPolicy.Prefix}{stamp}.json", auto);
    }

    [Fact]
    public void OnlyTheManualExportPathCanCarryAttachments()
    {
        // 带附件的导出方法在 Core 里<b>只许出现一次（它自己的定义）</b>：
        // 多一处调用就意味着有人在回滚点或自动件那条路上悄悄把图带上了。
        Assert.Equal(1, Count(ReadRepoFile(Service), "ExportWithClipImagesAsync"));
    }

    // ==================== ② 附件是搬流，不是整张进内存 ====================

    [Fact]
    public void AttachmentsAreStreamedNeverFullyBuffered()
    {
        // 200 张 4K 截图一次全进内存＝几百 MB 峰值，而备份那一刻正是最不该崩的时候。
        // 钉"调用形状"而不是钉词：这份文件的头注释里就写着"不 ReadAllBytes"，数整个词会让注释自己撞红。
        var container = ReadRepoFile(Container);
        Assert.DoesNotContain("File.ReadAllBytes(", container);
        Assert.DoesNotContain("File.ReadAllText(", container);
        Assert.Contains("source.CopyToAsync(es", container);            // 打包：流→流
        Assert.Contains("source.CopyToAsync(fs", ReadRepoFile(Store));   // 恢复：条目流→临时文件
    }

    [Fact]
    public void ThePackageOnlyKnowsOneAttachmentFolderAndNeverComposesPaths()
    {
        // 前缀与"能不能落盘"是分开的两件事：包这边只认 clip/ 这一层，路径一律交回
        // ClipboardImageStore.WriteBackFromBackupAsync（它内部问 ClipAssets.FullPathOf 那道名册）。
        // 这里若能自己拼路径，Zip Slip 的防线就从"名册 + 白名单"退化成"字符串拼接"。
        var extract = MethodBody(ReadRepoFile(Container), "int Written, int Skipped, int Failed, IReadOnlyList<string> PackageNames, bool Opened");
        Assert.Contains("ClipboardImageStore.WriteBackFromBackupAsync", extract);
        Assert.Contains("MaxEntryBytes", extract);
        Assert.DoesNotContain("Path.Combine", extract);
        Assert.Equal(1, Count(ReadRepoFile(Container), "ClipPrefix = \"clip/\""));
    }

    [Fact]
    public void WritingAnAttachmentBackCreatesTheClipFolderFirst()
    {
        // clip 目录是采集到第一张图时才建的，所以"新机器"上它可能不存在；写回那一步不建目录，
        // 症状就是换机恢复后"条目都在、每张图都报没能写入"。行为面由
        // RestoringOntoAMachineWithoutTheClipFolderStillWritesThePicturesBack 覆盖，
        // 这里钉的是**顺序**：建目录必须在临时件之前（放在之后的话第一张照样撞 DirectoryNotFoundException）。
        var write = MethodBody(ReadRepoFile(Store), "public static async Task<bool> WriteBackFromBackupAsync");
        Assert.Contains("Directory.CreateDirectory(Folder)", write);
        Assert.True(write.IndexOf("Directory.CreateDirectory(Folder)", StringComparison.Ordinal)
                    < write.IndexOf("File.Create(temp)", StringComparison.Ordinal),
                    "建目录那一句必须排在临时件之前");
        // 且必须排在"本机已有同名文件"那道之后之前？不——不覆盖那道在前是对的：目录都不存在时不可能有同名文件，
        // 顺序反过来只是多建一个空目录，而**建目录在写之前**才是不能翻的那一面。
        Assert.Contains("if (File.Exists(path)) return false;", write);
    }

    [Fact]
    public void AttachmentsAreStoredNotDeflatedBecauseTheDecisionSaidStore()
    {
        // 决议写的是 zip(store)。这里钉"每一处建条目都显式写了 NoCompression"：
        // 漏一处就退化成默认 Optimal——功能一样、看不出来，只是导出变成一场 CPU 活。
        var write = MethodBody(ReadRepoFile(Container), "public static async Task<(int Count, long Bytes)> WriteAsync");
        Assert.Equal(2, Count(write, "CompressionLevel.NoCompression"));   // 清单一条 + 附件各一条，两处都写死
        Assert.Equal(2, Count(write, "CreateEntry"));                       // 建条目的地方只有这两处，没有第三张清单
        Assert.DoesNotContain("CompressionLevel.Optimal", ReadRepoFile(Container));
        Assert.DoesNotContain("CompressionLevel.Fastest", ReadRepoFile(Container));
    }

    // ==================== ③ 扩展名只有一个出处 ====================

    [Fact]
    public void CarrierExtensionsHaveExactlyOneDefinitionSite()
    {
        // 认扩展、换扩展、给用户的建议名、补默认扩展名——四处各写一份 ".json"/".zip" 的话，
        // 漏的那一处恰好是唯一下过图片的那份，症状是"列表里看得见、输入框说不是备份文件"。
        var container = ReadRepoFile(Container);
        Assert.Contains("public const string ManifestExtension = \".json\";", container);
        Assert.Contains("public const string ContainerExtension = \".zip\";", container);
        // 这两个常量之外的裸字面量只许出现在给用户看的中文句子里（错误提示），不许出现在判定里。
        Assert.DoesNotContain("== \".zip\"", container);
        Assert.DoesNotContain("GetExtension(path), \".", container);
        Assert.DoesNotContain("\".zip\"", ReadRepoFile(Policy));
        Assert.DoesNotContain("\".json\"", ReadRepoFile(Policy));
        Assert.Equal(2, Count(ReadRepoFile(Policy), "if (!BackupContainer.IsBackupPath("));   // 导出与导入两个方向同一份口径
    }

    // ==================== ④ 界面上的开关接到两条路 ====================

    [Fact]
    public void TheExportButtonPicksBetweenTwoNamedMethodsNotABooleanArgument()
    {
        // 布尔参数的含义只写在被调方时，接线处写反是必然风险（本仓踩过两次，症状都是"全绿而功能是反的"）。
        // 所以两条路是两个具名方法，界面上只按开关分流；这里钉的是"分流真的发生了"。
        var ui = ReadRepoPartials(Ui);
        var export = MethodBody(ui, "private async void ExportBackup_Click");
        Assert.Contains("ViewModel.BackupClipboardImagesEnabled", export);
        Assert.Contains("ExportWithClipImagesAsync", export);
        Assert.Contains("ExportToFileAsync", export);
        // 报出去的路径必须是"实际写出去的那一个"：内容改名字时（.json↔.zip）状态栏说目标路径就是假话。
        Assert.Contains("written.Path", export);
        Assert.Contains("var plain = await Task.Run(() => _backup.ExportToFileAsync", export);

        // 建议名的扩展名跟着开关走（开着却提示 .json，用户会以为导出的是纯清单）。
        Assert.Contains("carry ? BackupContainer.ContainerExtension", export);

        // 恢复必须把"这一份文件自己的路径"递进去——附件只从这份包里解，调用方没机会指认别的包。
        var import = MethodBody(ui, "private async Task ImportFromPathAsync");
        Assert.Contains("_backup.RestoreAsync(", import);
        Assert.Contains("source)", import);
        Assert.Contains("BackupContainer.CountClipEntries(source)", import);
    }

    [Fact]
    public void TheSystemFilePickersOfferBothCarriers()
    {
        // 普通权限会话走的是系统文件对话框（提权会话才退回输入框），而这条路上出过两个"看不见"的洞：
        // ① 导入框的 FileTypeFilter 只列 .json ⇒ 刚导出的那一份 .zip 在对话框里根本不存在，
        //    这个功能在最常见的那种会话里等于没做；② 导出框只给 .json 类型 ⇒ 挑到的名字与落盘的名字必然不同。
        // 这里钉的是"两种载体都列出来"，而不是钉某段中文（中文是给用户看的标签，改文案不该撞红）。
        var pick = MethodBody(ReadRepoPartials(Ui), "private static async Task<string?> PickNativeAsync");
        Assert.Equal(2, Count(pick, "open.FileTypeFilter.Add("));
        Assert.Contains("open.FileTypeFilter.Add(BackupContainer.ContainerExtension)", pick);
        Assert.Contains("open.FileTypeFilter.Add(BackupContainer.ManifestExtension)", pick);
        Assert.Equal(2, Count(pick, "picker.FileTypeChoices.Add("));   // 两种类型都在下拉里，用户能反过来挑
        Assert.Equal(1, Count(pick, "picker.DefaultFileExtension =")); // 默认那一项只设一次，跟着开关走
        Assert.Contains("CarrierLabel(primary)", pick);
        Assert.Contains("CarrierLabel(secondary)", pick);
        Assert.Contains("BackupContainer.ContainerExtension ? \"备份包",
            MethodBody(ReadRepoPartials(Ui), "private static string CarrierLabel"));
        // 类型表按"建议名的扩展名"分流，而建议名由那颗开关决定 ⇒ 这条链不许各写一份判据。
        Assert.Contains("Path.GetExtension(fileName)", pick);
        Assert.Contains("BackupContainer.ContainerExtension, StringComparison.OrdinalIgnoreCase)", pick);
    }
}
