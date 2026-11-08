#nullable enable
using System;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// P-90「整批撤销这次整理」的接线闸门：行为测（<see cref="AiUndoTests"/>）量的是真 SQLite 的
/// 两颗批量基元；这里量的是<b>记录一路的接线</b>——应用攒记录、撤销清记录、即时分类刻意不记、
/// 按钮只在有记录时现身。任何一环脱线（应用忘攒、撤销忘清、即时分类顺手也记、按钮常驻或常不见），
/// 撤销还在，但用户看到的要么是"点了没反应"，要么是"把我手打的也摘了"——都在这里红。
/// <para>服务与存档侧（<c>AiClassifyService</c>／<c>SettingsStore.Ai.cs</c>）住在 UI 工程，
/// 测试工程⛔ 引用 UI（WPF 难以 headless），所以这一档用 <see cref="SourceGate"/> 钉形状；
/// 词句三语读数由 <see cref="AiWordGateTests"/> 的 roster 一并接管（本片补入五颗）。</para>
/// </summary>
public sealed class AiUndoWiringGateTests
{
    private const string Service = "src/StarMark.UI/Services/AiClassifyService.cs";
    private const string StoreAi = "src/StarMark.UI/Helpers/SettingsStore.Ai.cs";
    private const string StoreMain = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.Ai.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));
    private static string BodyOf(string rel, string signature) => SourceGate.MethodBody(CodeOf(rel), signature);

    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>①应用攒记录：ApplyAsync 走留档出口，差集并进撤销记录——记录只在"真的新挂上"时才诞生。</summary>
    [Fact]
    public void ApplyingRecordsOnlyWhatWasActuallyAdded()
    {
        var apply = BodyOf(Service, "public async Task<int> ApplyAsync(ClassifyPlan plan, CancellationToken ct)");
        Assert.Contains("TagItemsRecordedAsync(", apply, StringComparison.Ordinal);   // ⛔ 退回 TagItemsAsync：那就拿不到差集，撤销只能整条摘
        Assert.Contains("if (outcome.Additions.Count > 0) SaveUndoAdditions(outcome.Additions);", apply, StringComparison.Ordinal);

        var merge = BodyOf(Service, "private void SaveUndoAdditions(IReadOnlyList<ItemTagAssignment> additions)");
        Assert.Contains("LoadAiUndoRecord()", merge, StringComparison.Ordinal);       // 按组应用会点好几次：并进同一份记录，不是各自另起一份
        Assert.Contains("SaveAiUndoRecord(", merge, StringComparison.Ordinal);
        // 同一条目再出现时标签取并集：撤销要摘的是 AI 应用路径加过的全部，少并一次就有一条摘不掉
        Assert.Contains("merged.TryGetValue(addition.ItemId, out var names)", merge, StringComparison.Ordinal);
    }

    /// <summary>②即时分类刻意不记：它是收藏动作的顺风车，用户没对它点过「应用」——
    /// 混进撤销记录会把「这次整理」的边界搅浑，撤销一次连收藏时挂的也摘了。</summary>
    [Fact]
    public void InstantClassificationStaysOutOfTheUndoRecord()
    {
        var instant = BodyOf(Service, "public async Task TryInstantClassifyAsync(Item item, CancellationToken ct = default)");
        Assert.Equal(0, Times(instant, "SaveUndoAdditions"));
        Assert.Equal(0, Times(instant, "SaveAiUndoRecord"));

        // 而整个服务档里"攒记录"只有一个主人：ApplyAsync 那一处调用。出现第二处就是有人把别的路也接了进来
        var service = CodeOf(Service);
        Assert.Equal(1, Times(service, "SaveUndoAdditions(outcome.Additions)"));
    }

    /// <summary>③撤销清记录：一次事务摘掉记录在案的挂接，撤完记录回空——撤销是单程票，
    /// 再应用会重新记录；条目已删/挂接已手删的跳过不抛（撤销面对的是"应用之后库又动过"的常态）。</summary>
    [Fact]
    public void UndoingRemovesTheRecordedLinksAndClearsTheRecord()
    {
        var undo = BodyOf(Service, "public async Task<int> UndoLastOrganiseAsync(CancellationToken ct)");
        Assert.Contains("LoadAiUndoRecord()", undo, StringComparison.Ordinal);
        Assert.Contains("UntagItemsAsync(", undo, StringComparison.Ordinal);
        Assert.Contains("ClearAiUndoRecord()", undo, StringComparison.Ordinal);

        // 按钮可现性只问这一个数：0＝没有可撤销的整理。界面上⛔ 再开一条直读存档的口子（第二事实源）
        Assert.Contains("public int UndoRecordCount() => _store.LoadAiUndoRecord().ItemCount", CodeOf(Service), StringComparison.Ordinal);
    }

    /// <summary>④存档三出口＋独立一格：撤销记录与未应用方案分格存放——前者应用完才诞生、撤完清空，
    /// 后者应用完清空；共用一格会在"应用一组"那条路上互相覆盖（AI 域 key 同名事故的同一课）。</summary>
    [Fact]
    public void TheRecordHasItsOwnSlotAndThreeExits()
    {
        var store = CodeOf(StoreAi);
        Assert.Contains("public ClassifyPlan LoadAiUndoRecord()", store, StringComparison.Ordinal);
        Assert.Contains("public void SaveAiUndoRecord(ClassifyPlan record)", store, StringComparison.Ordinal);
        Assert.Contains("public void ClearAiUndoRecord()", store, StringComparison.Ordinal);
        // 坏档按"没有记录"处理不抛：这一格只是撤销的路标，不是数据本身
        Assert.Contains("整批撤销的记录读不出，按没有记录处理", store, StringComparison.Ordinal);

        var main = CodeOf(StoreMain);
        Assert.Contains("public string? AiUndoRecordJson { get; set; }", main, StringComparison.Ordinal);
        Assert.Contains("public string? AiPendingPlanJson { get; set; }", main, StringComparison.Ordinal);
        Assert.Equal(1, Times(main, "public string? AiUndoRecordJson { get; set; }"));
    }

    /// <summary>⑤按钮接线：只在记录非空时现身（装载、应用、撤销三处刷新）；确认框默认「取消」、
    /// 走收身份的那颗工厂——一次摘几百条身上的标签，顺手回车不该等于撤了。</summary>
    [Fact]
    public void TheButtonShowsOnlyWhenThereIsSomethingToUndoAndConfirmsFirst()
    {
        var page = CodeOf(Page);
        var click = BodyOf(Page, "private async void AiUndo_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("CenteredDialog.ConfirmAsync(", click, StringComparison.Ordinal);
        Assert.Contains("dedupeKey: \"aiundoconfirm\"", click, StringComparison.Ordinal);
        Assert.Contains("UndoLastOrganiseAsync(CancellationToken.None)", click, StringComparison.Ordinal);
        Assert.Contains("settings.ai.undoDone", click, StringComparison.Ordinal);
        Assert.Contains("settings.ai.undoNone", click, StringComparison.Ordinal);

        var refresh = BodyOf(Page, "private void RefreshAiUndoButton()");
        Assert.Contains("UndoRecordCount() > 0", refresh, StringComparison.Ordinal);
        Assert.Contains("Visibility.Visible", refresh, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", refresh, StringComparison.Ordinal);

        // 三个刷新点一个不缺：装载（跨会话还在）、应用后（当场现身）、撤销后（当场收起）。
        // ⚠ SourceGate.Code 会剥注释：断言⛔ 带行尾注释一起找（带了永远找不到，白红一场）
        Assert.Contains("RefreshAiUndoButton();", BodyOf(Page, "private void InitAiOrganiseSection()"), StringComparison.Ordinal);
        Assert.Contains("RefreshAiUndoButton();", BodyOf(Page, "private async Task ApplyAsync(ClassifyPlan plan, LocMessage label)"), StringComparison.Ordinal);
        Assert.Contains("RefreshAiUndoButton();", click, StringComparison.Ordinal);
        Assert.Equal(3, Times(page, "RefreshAiUndoButton();"));   // 三个刷新点一个不缺（装载/应用/撤销），多一处就是有人在乱刷
    }

    /// <summary>⑥标记侧：按钮挂的是静态键（与「开始整理」同族），Click 接到撤销处理器上；
    /// 页面上那五颗句键只在 code-behind 点名，标记里一次都不出现（本栏句子全是代码当场算的口径）。</summary>
    [Fact]
    public void TheButtonCarriesAStaticKeyAndTheSentencesStayInCode()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        Assert.Contains("x:Name=\"AiUndoButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("i18n:LocText.Key=\"settings.ai.undo\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"AiUndo_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);    // 出厂不现身：记录说了才算

        foreach (var key in new[]
        {
            "settings.ai.undoConfirm.title", "settings.ai.undoConfirm.body", "settings.ai.undoConfirm.go",
            "settings.ai.undoDone", "settings.ai.undoNone",
        })
        {
            Assert.Equal(0, Times(xaml, key));
            Assert.Single(UiStrings.All, entry => entry.Key == key);
            Assert.Equal(1, Times(CodeOf(Page), (char)34 + key + (char)34));
        }
        // 按钮那颗静态键两处对账：表里恰好一行、标记里恰好一次
        Assert.Single(UiStrings.All, entry => entry.Key == "settings.ai.undo");
        Assert.Equal(1, Times(xaml, "settings.ai.undo"));
    }
}
