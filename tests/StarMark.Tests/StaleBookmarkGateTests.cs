#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 失联书签这一链（片215·P-5B）的接线闸门：P-5 账本拍板 (B)「标灰＋一键清」落地之后，
/// 把链上每一环钉在源码里——迁移双轨、位的两个写入者、七处 SELECT、对账的两道门与次序、
/// 卡片标灰、设置卡那张嘴、以及每一句新文案的键。
/// <para>
/// 为什么用源码扫而不是行为测：行为面（标位/清位/作用域/幂等）已由 <c>StaleReconcileTests</c>、
/// <c>SyncCoordinatorTests</c>、<c>MigrationRunnerTests</c> 三家在真库上钉死；这一档管的是
/// 「接线有没有被改回裸奔」——哪天有人把 <c>!fetch.Truncated</c> 那道闸顺手抹掉，这里先红。
/// </para>
/// </summary>
public sealed class StaleBookmarkGateTests
{
    private const string Schema = "src/StarMark.Data/Schema.sql";
    private const string Runner = "src/StarMark.Data/MigrationRunner.cs";
    private const string RepoMain = "src/StarMark.Data/ItemRepository.cs";
    private const string RepoItems = "src/StarMark.Data/ItemRepository.Items.cs";
    private const string RepoSearch = "src/StarMark.Data/ItemRepository.Search.cs";
    private const string RepoActivity = "src/StarMark.Data/ItemRepository.Activity.cs";
    private const string RepoLocal = "src/StarMark.Data/ItemRepository.Local.cs";
    private const string RepoSnapshots = "src/StarMark.Data/ItemRepository.Snapshots.cs";
    private const string Interface = "src/StarMark.Abstractions/IItemSource.cs";
    private const string UiStrings = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Browser = "src/StarMark.Integrations/Bookmarks/BrowserBookmarksSource.cs";
    private const string Coordinator = "src/StarMark.Core/Sync/SyncCoordinator.cs";
    private const string AppXaml = "src/StarMark.UI/App.xaml";
    private const string CardXaml = "src/StarMark.UI/Controls/ItemCard.xaml";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string StaleVm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.StaleBookmarks.cs";
    private const string SettingsVm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string SettingsCode = "src/StarMark.UI/Views/SettingsPage.xaml.cs";

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>全仓（<c>src/</c> 下每一颗手写 .cs，抹注释）合成的一份文本——普查"全仓只有一处"用它。</summary>
    private static string AllSrcCode()
        => string.Join("\n", ReadRepoUnder("src").Select(f => Code(f.Text)));

    // ────────── ① 迁移双轨：Schema.sql 出生自带，旧库靠 MigrateV7 补，且 ALTER 有守卫 ──────────

    [Fact]
    public void MigrationV7ShipsOnBothTracksBehindAColumnExistsGuard()
    {
        var runner = Code(ReadRepoFile(Runner));
        Assert.Contains("public const int CurrentVersion = 7", runner);
        Assert.Contains("if (version < 7) MigrateV7(conn)", runner);
        var body = MethodBody(runner,
            "private static void MigrateV7(Microsoft.Data.Sqlite.SqliteConnection conn)");
        Assert.Contains("ColumnExists(conn, \"items\", \"stale\")", body);   // 幂等守卫：没它第二次启动直接炸库
        Assert.Contains("ALTER TABLE items ADD COLUMN stale INTEGER NOT NULL DEFAULT 0", body);

        // 另一轨：新库的列随 Schema.sql 出生（口径同 v2 的 pinned——两轨写同一句 DDL）。
        // 空白归一后再匹配：列对齐的空格数不是这条链要钉的东西。
        var schemaFlat = Regex.Replace(ReadRepoFile(Schema), @"\s+", " ");
        Assert.Contains("stale INTEGER NOT NULL DEFAULT 0", schemaFlat);
    }

    // ────────── ② 位的写入者普查：置/清只有对账，删行只有一键清，插入路径一律不写 ──────────

    [Fact]
    public void TheStaleFlagHasExactlyItsAgreedWriters()
    {
        var all = AllSrcCode();
        // 置/清位的 SQL 全仓只此一份（对账的 CASE）；数与删的 SQL 也各只此一份（作用域 type='bookmark' 写进 SQL 自证）
        Assert.Equal(1, Count(all, "SET stale = CASE"));
        Assert.Equal(1, Count(all, "SELECT COUNT(*) FROM items WHERE stale = 1 AND type = 'bookmark'"));
        Assert.Equal(1, Count(all, "DELETE FROM items WHERE stale = 1 AND type = 'bookmark'"));
        // 插入路径不带这列（新行默认 0）：UpsertOne / 本机 upsert / 快照恢复，谁的 INSERT 列单里都没有 stale
        Assert.Equal(0, Count(Code(ReadRepoFile(RepoMain)), "stale"));
        Assert.Equal(0, Count(Code(ReadRepoFile(RepoLocal)), "stale"));
        Assert.Equal(0, Count(Code(ReadRepoFile(RepoSnapshots)), "stale"));
    }

    // ────────── ③ 读路径：渲染用的七处 SELECT 带列，MapItem 18 号位防御式读 ──────────

    [Fact]
    public void EveryRenderingSelectCarriesTheFlagAndMapItemReadsItDefensively()
    {
        // Items 六处（GetById/GetAll/GetUntagged/GetHidden/GetRecent/GetPinned）＋搜索一处
        Assert.Equal(6, Count(Code(ReadRepoFile(RepoItems)), "i.stale"));
        Assert.Equal(1, Count(Code(ReadRepoFile(RepoSearch)), "i.stale"));
        // 18 号位防御式读：没带这列的 SELECT（备份/快照/其余）自动落 false，加列不漂移
        Assert.Contains("Stale = reader.FieldCount > 18 && !reader.IsDBNull(18) && reader.GetInt64(18) != 0",
            Code(ReadRepoFile(RepoActivity)));
    }

    // ────────── ④ 协调器：两道门，且对账排在检查点提交之后 ──────────

    [Fact]
    public void ReconciliationRunsAfterTheCheckpointBehindBothGates()
    {
        // 对账块住在 SyncAllCoreAsync（SyncAllAsync 只是那颗闸的壳：拿闸→try→放闸）
        var body = MethodBody(Code(ReadRepoFile(Coordinator)),
            "private async Task<SyncSummary> SyncAllCoreAsync(CancellationToken ct)");
        // 双门合一颗 if：源声明全量（SupportsStaleReconciliation）×这一轮没被掐断（!Truncated）。
        // 抹掉副门＝拿截断清单当全量、首页之外的历史批量误标（账本护栏③原文）。
        Assert.Contains("source.SupportsStaleReconciliation && !fetch.Truncated", body);
        // 次序：载荷→检查点→对账。对账不是这一轮的既定产出，崩在它身上不该连坐已落库的载荷与检查点。
        var commitIx = body.IndexOf("await source.CommitCheckpointAsync", StringComparison.Ordinal);
        var reconcileIx = body.IndexOf("ReconcileStaleAsync", StringComparison.Ordinal);
        Assert.True(commitIx >= 0 && reconcileIx > commitIx, "对账排到了检查点提交之前");
        // 作用域自卫：local 条目不进清单；按类分组逐族对账（组内的 source_id 清单才是该族的全量）
        Assert.Contains("i.Source != ItemSources.Local", body);
        Assert.Contains("GroupBy(i => i.Type)", body);
    }

    /// <summary>「这份清单足以对账」是每颗源自己声明的事，默认关门（GitHub 5000 顶那类截断源声明了就是误标）。</summary>
    [Fact]
    public void FullListSupportIsOptInPerSource()
    {
        Assert.Contains("bool SupportsStaleReconciliation => false", Code(ReadRepoFile(Interface)));
        Assert.Contains("bool SupportsStaleReconciliation => true", Code(ReadRepoFile(Browser)));
        // 全仓恰好三处：接口默认、浏览器书签覆写、协调器那道门——多一处就是有人想绕门
        Assert.Equal(3, Count(AllSrcCode(), "SupportsStaleReconciliation"));
    }

    // ────────── ⑤ 卡片：标灰但照常显示，同一张卡换灰度要重渲染 ──────────

    [Fact]
    public void TheGreyedCardWiringIsComplete()
    {
        var card = Markup(ReadRepoFile(CardXaml));
        Assert.Equal(1, Count(card, "ViewModel.IsStale, Mode=OneWay, Converter={StaticResource BoolToOpacity}"));
        Assert.Equal(1, Count(card, "ViewModel.StaleTip, Mode=OneWay"));   // 悬停那句话也跟着位走
        Assert.Contains("x:Key=\"BoolToOpacity\"", ReadRepoFile(AppXaml));

        var vm = Code(ReadRepoFile(CardVm));
        Assert.Contains("public bool IsStale => _item.Stale", vm);
        Assert.Contains("Locale.T(\"card.tip.stale\")", vm);
        // 换灰度＝换展示：SameDisplayContent 的判等清单必须带 Stale，否则位变了卡片不重渲染
        Assert.Contains("_item.Stale == other.Stale", vm);
    }

    // ────────── ⑥ 设置卡：那句话的可见性绑「有没有话要说」，不绑「有没有可删的行」 ──────────

    [Fact]
    public void TheSettingsCardSpeaksEvenWhenCountingFails()
    {
        var xaml = Markup(ReadRepoFile(SettingsXaml));
        Assert.Equal(1, Count(xaml, "ViewModel.HasStaleBookmarksNotice, Mode=OneWay"));  // 那句话的可见性
        Assert.Equal(1, Count(xaml, "ViewModel.HasStaleBookmarks, Mode=OneWay"));        // 按钮的可见性
        Assert.Equal(1, Count(xaml, "CleanStaleBookmarksCommand"));                      // 一键清恰好一颗入口

        var vm = Code(ReadRepoFile(StaleVm));
        // ⛔ 绑「有没有可删的行」＝数失败那句跟着按钮一起被藏（LocalDisk 那卡的同款教训，坑表同族）
        Assert.DoesNotContain("HasStaleBookmarksNotice => HasStaleBookmarks", vm);
        Assert.Contains("HasStaleBookmarksNotice => StaleBookmarksStatus is not null", vm);

        // 计数那一趟：三臂各交一份身份（没有/有 N 条/数失败），不许哪一臂留上一轮的沉默
        var refresh = MethodBody(vm, "public async Task RefreshStaleBookmarksAsync()");
        Assert.Equal(3, Count(refresh, "LocMessage.Of("));
        // 这一格只读数：⛔ 替对账做决定（改位/删行都不是数数的事）
        Assert.DoesNotContain("DeleteStaleAsync", refresh);
    }

    /// <summary>一键清的动作次序（VQ 那条「先报条数再删、删完重数」的全形）：先数→确认→删→重数。</summary>
    [Fact]
    public void CleaningCountsFirstConfirmsThenDeletesThenRecounts()
    {
        var vm = Code(ReadRepoFile(StaleVm));
        var body = MethodBody(vm, "private async Task CleanStaleBookmarksAsync()");
        Assert.Contains("CountStaleAsync", body);                               // 先报条数
        Assert.Contains("dedupeKey: \"clean-stale-bookmarks\"", body);          // 确认框去重：连点不叠框
        Assert.Contains("settings.extend.staleCleanBody", body);                // 正文各自立键（磁盘那句对书签是谎话）
        Assert.Contains("DeleteStaleAsync", body);                              // 真删只在这一键之后
        Assert.Contains("settings.extend.staleClearedBody", body);
        Assert.Contains("await RefreshStaleBookmarksAsync()", body);            // 删完重数：留着旧计数＝新一份假话
    }

    // ────────── ⑦ 页接线：状态进 or-链、进 ApplyExtensionStatuses、进页现数 ──────────

    [Fact]
    public void ThePagePlumbsTheStatusThroughAndRefreshesOnEntry()
    {
        var code = Code(ReadRepoFile(SettingsCode));
        Assert.Contains("nameof(SettingsPageViewModel.StaleBookmarksStatus)", code);   // or-链：位变即重挂
        Assert.Contains("LocText.SetMessage(StaleBookmarksStatusText, ViewModel.StaleBookmarksStatus)", code);

        // 进设置页现数一次（同步对账每轮自己刷位，这里只负责把数读给用户看）
        Assert.Equal(1, Count(Code(ReadRepoFile(SettingsVm)), "RefreshStaleBookmarksAsync()"));
    }

    // ────────── ⑧ 每句新文案都有键（三语齐不齐归 I18nContractTests，这里只管键存在且唯一） ──────────

    [Fact]
    public void EveryNewSentenceHasExactlyOneKey()
    {
        var ui = ReadRepoFile(UiStrings);
        foreach (var key in new[]
                 {
                     "settings.extend.staleTitle",
                     "settings.extend.staleCount", "settings.extend.staleNone",
                     "settings.extend.staleCleanBody", "settings.extend.staleClearedBody",
                     "settings.extend.staleCountFailed", "card.tip.stale",
                 })
        {
            Assert.Equal(1, Count(ui, $"\"{key}\""));
        }
        // 说明行刻意没有（设置页说明族的中文预算不收新行，该说的两件事各有出处）——
        // 谁把它加回来，SettingsNoteTrimGateTests 那一关先拦长度，这里补拦「键不该在」。
        Assert.Equal(0, Count(ui, "\"settings.extend.staleNote\""));
    }

    /// <summary>守门自己要有证人（坑表 #228/#236 那一族）：喂"退回旧写法"的样串必须能被上面几格认出。</summary>
    [Fact]
    public void TheScannersActuallyBitOnSamples()
    {
        // 拿掉副门的老写法：④那格的完整双门锚必须认不出它
        var singleGate = "if (source.SupportsStaleReconciliation)";
        Assert.DoesNotContain("source.SupportsStaleReconciliation && !fetch.Truncated",
            singleGate, StringComparison.Ordinal);
        // 绑错判据的可见性：⑥那格的禁项必须抓得到
        var wrongBinding = "public bool HasStaleBookmarksNotice => HasStaleBookmarks;";
        Assert.Contains("HasStaleBookmarksNotice => HasStaleBookmarks",
            wrongBinding, StringComparison.Ordinal);
    }
}
