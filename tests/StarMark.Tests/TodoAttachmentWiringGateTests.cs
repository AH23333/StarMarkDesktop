#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 待办附件（片284 / P-305）的<b>接线</b>闸门。与 <see cref="TodoWriterWiringGateTests"/> 同族：
/// 「写入面一直在、入口没人接」不报错也不红——那颗颜色点就这么哑了一年多。
/// 这里钉的是附件这一对入口（行级拖放／回形针菜单）与删除护栏的接线形状；
/// 纯逻辑（IsUnder／NeedsForcedCopy／extra_json 往返）归 <see cref="TodoAttachmentPolicyTests"/>
/// 与 <see cref="LocalItemStateTests"/>，这里不重复。
/// </summary>
public sealed class TodoAttachmentWiringGateTests
{
    private const string VmPath = "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs";
    private const string ViewPath = "src/StarMark.UI/Views/TodoWidget.xaml";
    private const string CodeBehindPath = "src/StarMark.UI/Views/TodoWidget.xaml.cs";
    private const string StoragePath = "src/StarMark.UI/Services/TodoAttachmentStorage.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>XAML 侧先抹注释再断言——说明注释里必然写出被禁/被钉的形状（#123／坑表 #228）。</summary>
    private static string MarkupXaml()
        => Regex.Replace(SourceGate.ReadRepoFile(ViewPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string Behind() => SourceGate.Code(SourceGate.ReadRepoFile(CodeBehindPath));
    private static string Vm() => SourceGate.Code(SourceGate.ReadRepoFile(VmPath));
    private static string Storage() => SourceGate.Code(SourceGate.ReadRepoFile(StoragePath));

    [Fact]
    public void RowRootCarriesIdAndDropTargetInTheSameOpeningTag()
    {
        var xaml = MarkupXaml();
        // 行根那颗 Grid：Drop/Tag/AllowDrop/DragOver 必须同一副开始标记——少任何一件，
        // 行级靶要么收不到文件、要么收下后认不出是哪一行（颜色点哑掉那一族）。
        var roots = Regex.Matches(xaml, @"<Grid\b[^>]*>", RegexOptions.Singleline).Cast<Match>()
            .Where(m => m.Value.Contains("Drop=\"Row_Drop\""))
            .ToList();
        var hit = Assert.Single(roots);
        Assert.Contains("Tag=\"{x:Bind Id}\"", hit.Value);
        Assert.Contains("AllowDrop=\"True\"", hit.Value);
        Assert.Contains("DragOver=\"Row_DragOver\"", hit.Value);
    }

    [Fact]
    public void RowDropTakesDeferralAndHandsPathsToTheViewModel()
    {
        var drop = SourceGate.MethodBody(Behind(), "Row_Drop");
        Assert.Contains("e.GetDeferral()", drop);                       // 异步收路径前先记延迟，否则系统提前收走这轮拖放
        Assert.Contains("deferral.Complete()", drop);                    // finally 里还那笔延迟
        Assert.Contains("ViewModel.AddDroppedFilesAsync(", drop);        // 结局（收到/挂上/重复/失败）交给回执，不吞

        var over = SourceGate.MethodBody(Behind(), "Row_DragOver");
        Assert.Contains("e.DataView.Contains(StandardDataFormats.StorageItems)", over);  // 只认文件载荷
        Assert.Contains("e.Handled = true", over);                       // 不含文件载荷时零接触＝内部重排照旧冒泡给 CanReorderItems
    }

    [Fact]
    public void ReceiptSpeaksEveryOutcomeAndFailureComesFirst()
    {
        var behind = Behind();
        var body = SourceGate.MethodBody(behind, "LocMessage AttachmentReceiptFor");
        // 失败优先：部分挂上时先说没挂上的那几份——沉默失败＝「拖了没反应」的近亲
        var failAt = body.IndexOf("widget.todo.attachFailed", StringComparison.Ordinal);
        var addedAt = body.IndexOf("widget.todo.attachAdded", StringComparison.Ordinal);
        var dupAt = body.IndexOf("widget.todo.attachDup", StringComparison.Ordinal);
        Assert.True(failAt >= 0 && addedAt > failAt && dupAt > addedAt,
            "回执次序必须是：失败 → 挂上 → 重复 → 没有（失败优先，片284 拍的口径）");

        // 四颗结局键＋回执槽：常驻槽、后一次覆盖前一次（与快捷启动回执同一口径，不引定时器）
        var xaml = MarkupXaml();
        Assert.Contains("x:Name=\"DropReceipt\"", xaml);
        Assert.Contains("Visibility=\"Collapsed\"", xaml);
        var say = SourceGate.MethodBody(behind, "void SayAttachmentReceipt");
        Assert.Contains("DropReceipt.Visibility = Visibility.Visible", say);
    }

    [Fact]
    public void AttachMenuIsBuiltInCodeBehindAndEveryItemHasOpenAndRemove()
    {
        var body = SourceGate.MethodBody(Behind(), "AttachButton_Click");
        Assert.Contains("TryRowId(sender, out var id)", body);              // 行 id 只从被点元素的 Tag 取（与颜色/截止同一道关口）
        Assert.Contains("ViewModel.AttachmentsOf(", body);                  // 明细走 VM 索引，不塞进行模型（record 值相等的纪律）
        Assert.Contains("ViewModel.RemoveAttachmentAsync(", body);          // 摘除是写入面，必须过 VM，不直接动库
        Assert.Contains("MenuFlyoutSubItem", body);                         // 每份附件一颗子菜单
        Assert.Contains("widget.todo.attachOpen", body);
        Assert.Contains("widget.todo.attachRemove", body);

        // 打开不许裸路径直进 LauncherEx（P-236）：先拼 file:// URI
        var open = SourceGate.MethodBody(Behind(), "async void OpenAttachment");
        Assert.Contains("LocalFileIdentity.UriForPath", open);
        Assert.Contains("LauncherEx.TryOpenDetailedAsync", open);
    }

    [Fact]
    public void AddDroppedFilesIsLinkedByDefaultAndCopiesOnlyOnTheTwoRulings()
    {
        var body = SourceGate.MethodBody(Vm(), "AddDroppedFilesAsync");
        // Linked 默认：只有「用户开了复制开关」或「源躺在临时目录（链接＝空头支票）」才复制
        Assert.Contains("TodoAttachmentStorage.CopyWanted()", body);
        Assert.Contains("TodoAttachmentPolicy.NeedsForcedCopy", body);
        Assert.Contains("TodoAttachmentStorage.CopyIn(", body);
        // 同一路径（OrdinalIgnoreCase）已在清单里＝跳过：同一条待办不挂两份
        Assert.Contains("StringComparison.OrdinalIgnoreCase", body);
        // 源不存在算失败，不算挂上
        Assert.Contains("File.Exists(path)", body);
        // 只有真挂上才写盘＋刷新——全重复/全失败不产生一次 Upsert
        Assert.Contains("if (added > 0)", body);
        Assert.Contains("LocalItemState.SetAttachments(", body);
        Assert.Contains("UpsertLocalItemAsync(", body);
    }

    [Fact]
    public void RemovalRecyclesOnlyTheManagedCopyAfterTheRowIsRewritten()
    {
        // Between 而不是 MethodBody：这颗方法是「=> GuardAsync(async () => { … })」的多行表达式体，
        // MethodBody 的表达式体口径会截在 lambda 体内的第一条语句上（与 UndoReattaches… 那颗同因）。
        var body = SourceGate.Between(Vm(), "public Task RemoveAttachmentAsync", "public Task DeleteAsync");
        Assert.Contains("FindIndex", body);                                 // 按路径找那一行（OrdinalIgnoreCase）
        Assert.Contains("LocalItemState.SetAttachments(", body);            // 先把记录改回去
        Assert.Contains("TodoAttachmentStorage.RecycleManagedCopy", body);  // 再回收托管副本（护栏在回收那一步自己身上）
    }

    [Fact]
    public void RecycleCarriesItsOwnGuardAndNeverTouchesLinkedOriginals()
    {
        var body = SourceGate.MethodBody(Storage(), "RecycleManagedCopy");
        // 护栏自带：路径不在这条待办自己的托管目录里＝整步空操作——
        // Linked 的用户原件、别的行的托管副本，调用方就算送错了也动不着
        var guardAt = body.IndexOf("TodoAttachmentPolicy.IsUnder(path, dir)", StringComparison.Ordinal);
        var recycleAt = body.IndexOf("TryRecycle(path)", StringComparison.Ordinal);
        Assert.True(guardAt >= 0 && recycleAt > guardAt, "回收前必须先过 IsUnder 护栏（护栏自带，片284 拍的口径）");

        var copyIn = SourceGate.MethodBody(Storage(), "CopyIn");
        Assert.Contains("File.Copy(sourcePath, target, overwrite: false)", copyIn);   // 不覆盖既有文件
        Assert.Contains("File.Exists(target)", copyIn);                              // 同名加「 (2)」式后缀
    }

    [Fact]
    public void AttachmentCountRidesTheRowAndTheConverterHidesZero()
    {
        var xaml = MarkupXaml();
        // 行模型只带颗数（int 值相等安全），有附件才亮回形针
        Assert.Contains("Visibility=\"{x:Bind AttachCount, Mode=OneTime, Converter={StaticResource CountToVisibility}}\"", xaml);
        Assert.Contains("<local:CountToVisibilityConverter x:Key=\"CountToVisibility\"/>", xaml);

        // 转换器本体：0 → Collapsed、> 0 → Visible
        var conv = SourceGate.MethodBody(Behind(), "class CountToVisibilityConverter");
        Assert.Contains("count > 0 ? Visibility.Visible : Visibility.Collapsed", conv);
    }

    [Fact]
    public void CopySwitchHasExactlyOneEditorAndTheStorageReadsTheSameSetting()
    {
        var settings = SourceGate.Code(SourceGate.ReadRepoFile(SettingsXaml));
        Assert.Equal(1, SourceGate.Count(settings, "ViewModel.TodoAttachmentCopyEnabled, Mode=TwoWay"));

        // 存储侧读的是同一颗设置（DI 里那个单例），不是第二份真相
        var wanted = SourceGate.MethodBody(Storage(), "CopyWanted");
        Assert.Contains("LoadTodoAttachmentCopyEnabled", wanted);
    }

    [Fact]
    public void TheTenNewKeysExistAndTheTwoReceiptCountsCarryOneSlotEach()
    {
        // 8 颗 widget.todo.* ＋ 2 颗 settings.widgets.*（片284 新键，三语都得在表里）
        foreach (var key in new[]
        {
            "widget.todo.attachTip", "widget.todo.attachOpen", "widget.todo.attachRemove",
            "widget.todo.attachAdded", "widget.todo.attachDup", "widget.todo.attachNone",
            "widget.todo.attachFailed", "widget.todo.attachCaption",
            "settings.widgets.todoAttachTitle", "settings.widgets.todoAttachCopyLabel",
        })
            Assert.True(StarMark.Abstractions.I18n.UiStrings.TryGet(key, out _), $"表里没有这颗键：{key}");

        // 两颗带数的回执必须恰好一个 {0}：挂上几份／没挂上几份
        Assert.Equal(1, SourceGate.RowSlots("widget.todo.attachAdded"));
        Assert.Equal(1, SourceGate.RowSlots("widget.todo.attachFailed"));
    }
}
