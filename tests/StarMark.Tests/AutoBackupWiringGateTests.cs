#nullable enable
using System;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 自动备份"开关 + 可调间隔 + 每行删除"的<b>接线</b>闸门（批次 BK，扫源码不跑界面）。
/// <para>
/// 判据本身（<c>AutoBackupPolicy</c> 的间隔回落、到点判定、删除闸）已经在
/// <see cref="AutoBackupPolicyTests"/> 里用注入时刻钉死了。这里守的是只有源码结构才能发现的四类错：
/// ① <b>设置写了没人读</b>（WR 那次"序号粗细无效果"的老路：全绿而功能坏着）；
/// ② <b>改了频率要等下次启动才认</b>（＝一句隐藏的重启指令，本仓库已反复定性为缺陷）；
/// ③ <b>卡片上的说明与设置各说各话</b>（写死"24 小时"，用户改成每 6 小时后那句话就成了假话）；
/// ④ <b>删除越过那道闸</b>（界面上自己 <c>File.Delete</c>，一条过期列表路径就变成"程序删用户任意文件"）。
/// </para>
/// <para>断言文案的闸门一律先剥掉 <c>&lt;!-- --&gt;</c>：注释里就是要写出那些被禁的字面（"24 小时"），
/// 不剥注释的守门会因为解释自己为什么存在而红（坑表 #149 同一族）。</para>
/// </summary>
public sealed class AutoBackupWiringGateTests
{
    private const string Policy = "src/StarMark.Core/Backup/AutoBackupPolicy.cs";
    private const string Service = "src/StarMark.Core/Backup/BackupService.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string PageCode = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string Scheduler = "src/StarMark.UI/Helpers/AutoBackupScheduler.cs";

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>备份那张卡的正文（从卡片标题到下一张卡「剪贴板历史」之前）。</summary>
    private static string BackupCard()
        => Between(Markup(ReadRepoFile(PageXaml)), "数据备份与恢复", "剪贴板历史");

    // ────────── ① 设置真的有人读 ──────────

    [Fact]
    public void TheIntervalTheUiShowsIsTheIntervalTheJudgeUses()
    {
        // 档位表只能有一份：XAML 里再写一份 ComboBoxItem，就会长成"下拉里有 120 小时但判据不认"。
        var card = BackupCard();
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.AutoBackupIntervalOptions}\"", card);
        Assert.DoesNotContain("ComboBoxItem", card);
        Assert.Contains("AutoBackupPolicy.IntervalLabels", ReadRepoPartials(Vm));
    }

    [Fact]
    public void SavedIntervalIsClampedByCoreNotByTheStore()
    {
        var save = MethodBody(ReadRepoFile(Store), "public void SaveAutoBackup(bool enabled, int intervalHours)");
        Assert.Contains("AutoBackupPolicy.ClampInterval(intervalHours)", save);
        Assert.Contains("d.AutoBackupEnabled = enabled;", save);
        // 一次写盘：这两格说的是同一件事，分开 Save 就是"改了间隔没改开关"的半套状态来源（P-43 同理）。
        Assert.Equal(1, Count(save, "Save(d)"));
    }

    [Fact]
    public void LoadedIntervalGoesThroughTheSameFallback()
    {
        var load = MethodBody(ReadRepoFile(Store), "public int LoadAutoBackupIntervalHours()");
        Assert.Contains("AutoBackupPolicy.ClampInterval(", load);
        // 存储层不许自己再造一份"什么算合法"的表（那才是分岔的源头）。
        Assert.DoesNotContain("168", load);
        Assert.DoesNotContain("72", load);
    }

    [Fact]
    public void AutoBackupIsOffWhenTheSettingIsMissingButOnByDefault()
    {
        // 默认值必须是"加这颗开关之前的行为"：?? true，而不是 && 那种"缺省＝关"。
        var load = MethodBody(ReadRepoFile(Store), "public bool LoadAutoBackupEnabled()");
        Assert.Contains("?? true", load);
    }

    // ────────── ② 改设置当场生效，不需要重启 ──────────

    [Fact]
    public void BackfillingThePageNeitherWritesNorReprobes()
    {
        var vm = ReadRepoPartials(Vm);
        var backfill = Between(vm, "_suppressAutoBackupApply = true;", "_suppressAutoBackupApply = false;");
        Assert.Contains("AutoBackupEnabled = Safe(_settings.LoadAutoBackupEnabled", backfill);
        Assert.Contains("AutoBackupIntervalIndex = AutoBackupPolicy.IntervalIndexOf(", backfill);
        // 进一趟设置页就把定时器收掉再起＝把已经排好的下一次巡查悄悄推后（护眼那条同理）。
        Assert.DoesNotContain("SaveAutoBackup", backfill);
        Assert.DoesNotContain("AutoBackupScheduler", backfill);
    }

    [Fact]
    public void BothEditorsShareOneApplyPath()
    {
        var vm = ReadRepoPartials(Vm);
        Assert.Equal(2, Count(vm, "ApplyAutoBackup();"));                 // 开关与档位两颗，各自指向同一条
        Assert.Equal(1, Count(vm, "_settings.SaveAutoBackup("));          // 写盘出口只有一处
    }

    [Fact]
    public void ApplyWritesThenRestartsTheProbeThenRestates()
    {
        var apply = MethodBody(ReadRepoPartials(Vm), "private void ApplyAutoBackup()");
        var save = apply.IndexOf("_settings.SaveAutoBackup(", StringComparison.Ordinal);
        var reprobe = apply.IndexOf("AutoBackupScheduler.Start(", StringComparison.Ordinal);
        var status = apply.IndexOf("BuildAutoBackupStatus()", StringComparison.Ordinal);
        Assert.True(save >= 0 && reprobe > save && status > reprobe,
            "先落盘 → 按新设置重排巡查 → 重算状态行；顺序反了状态就会念旧值");
        Assert.DoesNotContain("Thread.Sleep", apply);
    }

    [Fact]
    public void TheStartupPathAsksTheSchedulerInsteadOfSchedulingItself()
    {
        var app = ReadRepoFile(App);
        // App 直接调 RunAutoBackupAsync 的话，"改设置立刻重排"就没有唯一的落点（两处各排各的）。
        Assert.Equal(0, Count(app, "RunAutoBackupAsync"));
        Assert.Contains("AutoBackupScheduler.Start(", app);
        Assert.Contains("AutoBackupScheduler.ProbeAsync(", app);
        // 首屏后延 20 s 那一次仍在：它避的是与迁移/组件创建抢同一批磁盘 I/O。
        Assert.Contains("TimeSpan.FromSeconds(20)", app);
    }

    [Fact]
    public void TheSchedulerHasOneTimerAndReadsTheSettingsEveryTick()
    {
        var code = ReadRepoFile(Scheduler);
        Assert.Equal(1, Count(code, "new Timer("));
        var probe = MethodBody(code, "public static async Task ProbeAsync(");
        // 每 tick 重读设置：用户中途改档位时不必靠"改的时候正好记得重启表"。
        Assert.Contains("LoadAutoBackupEnabled()", probe);
        Assert.Contains("LoadAutoBackupIntervalHours()", probe);
        Assert.Contains("Interlocked.CompareExchange(ref _running", probe);   // 跨 tick 重入的闩
        var start = MethodBody(code, "public static void Start(SettingsStore settings, BackupService service)");
        Assert.Contains("Stop();", start);                                     // 重复调用不许攒出第二块表
        Assert.True(start.IndexOf("LoadAutoBackupEnabled()", StringComparison.Ordinal)
            < start.IndexOf("new Timer(", StringComparison.Ordinal),
            "关着就一块表都不许建——留着表就是「看着关了其实还在扫盘」");
    }

    // ────────── ③ 卡片说明不写死节拍 ──────────

    [Fact]
    public void DisabledShortCircuitsBeforeTheDirectoryScan()
    {
        // ShouldRun 里也有 enabled 臂，看着重复；这一句要钉的是"关掉之后连目录都不扫"——
        // 只留判据那份的话，每次巡查仍会为了算"距上次多久"去 EnumerateFiles 一趟，
        // 于是"已经关了"在磁盘上看不出来（而磁盘动静恰恰是用户唯一能自己验的事）。
        var body = MethodBody(ReadRepoFile(Service), "public async Task<string?> RunAutoBackupAsync");
        var early = body.IndexOf("if (!enabled) return null;", StringComparison.Ordinal);
        var scan = body.IndexOf("NewestAutoBackupUtc(", StringComparison.Ordinal);
        Assert.True(early >= 0 && scan > early, "先按开关短路，再碰目录");
    }

    [Fact]
    public void TheBackupCardStopsNamingAFixedCadence()
    {
        var card = BackupCard();
        foreach (var stale in new[] { "24 小时", "每天", "每日", "按天", "7 份" })
            Assert.DoesNotContain(stale, card);
        // 间隔/份数/回溯多久这些数只能在状态行里出现，而状态行是算出来的（下一条）。
        Assert.Contains("ViewModel.AutoBackupStatus", card);
    }

    [Fact]
    public void TheStatusLineDoesTheLookbackMathItself()
    {
        var status = MethodBody(ReadRepoPartials(Vm), "private string BuildAutoBackupStatus()");
        Assert.Contains("AutoBackupPolicy.MaxLookbackDays(", status);
        Assert.Contains("AutoBackupPolicy.Keep", status);
        Assert.Contains("AutoBackupIntervalHours", status);
        // 关着的那一臂必须说清代价（"关了之后丢了别怪我"那句理由就写在这里，不是靠弹窗）。
        Assert.Contains("已关闭", status);
    }

    // ────────── ④ 删除那颗按钮 ──────────

    [Fact]
    public void EveryBackupRowHasARestoreAndADelete()
    {
        var card = BackupCard();
        Assert.Equal(1, Count(card, "Click=\"RestoreBackup_Click\""));
        Assert.Equal(1, Count(card, "Click=\"DeleteBackup_Click\""));
        // 两颗按钮都拿行对象里的原始记录当 Tag：恢复/删除都不许"按名字回查目录"。
        Assert.Equal(2, Count(card, "Tag=\"{x:Bind File}\""));
    }

    [Fact]
    public void DeleteConfirmsThenDelegatesThenReportsAndRescans()
    {
        var body = MethodBody(ReadRepoFile(PageCode), "private async void DeleteBackup_Click(");
        var confirm = body.IndexOf("CenteredDialog.ConfirmAsync(", StringComparison.Ordinal);
        var guard = body.IndexOf("if (!confirm) return;", StringComparison.Ordinal);
        var del = body.IndexOf("BackupService.DeleteBackup(", StringComparison.Ordinal);
        var rescan = body.IndexOf("ViewModel.RefreshBackups()", StringComparison.Ordinal);
        Assert.True(confirm >= 0 && guard > confirm && del > guard,
            "确认 → 未确认就当场返回 → 才交给删除出口；少了中间那一步就是「弹了确认也照删」");
        // 回执与删除同句：返回值必须被显示出来（静默删／静默不删都不许），重扫让那一行真的消失。
        Assert.Contains("ViewModel.BackupStatus = BackupService.DeleteBackup(", body);
        Assert.True(rescan > del, "删完必须重扫列表，否则界面还留着那一行，看着像没删掉");
        // 界面绝不自己删文件：判据与删除动作同一个出处，才不会有"这条路径看着像备份件"的分支。
        Assert.DoesNotContain("File.Delete", body);
    }

    [Fact]
    public void TheDeleteGateRunsBeforeTheDeleteInCore()
    {
        var body = MethodBody(ReadRepoFile(Service), "public static string DeleteBackup(string? path)");
        var refusal = body.IndexOf("AutoBackupPolicy.DeleteRefusal(", StringComparison.Ordinal);
        var del = body.IndexOf("File.Delete(", StringComparison.Ordinal);
        Assert.True(refusal >= 0 && del > refusal, "闸门排在删除之前，否则只是装饰");
        Assert.Contains("return refusal;", body);                       // 拒绝要有下文（回执），不是静默不删
        Assert.Contains("StarLog.Warn", body);                          // 拒绝与失败都进日志
    }

    [Fact]
    public void DeleteRefusalComparesTheDirectoryCharacterByCharacter()
    {
        var policy = ReadRepoFile(Policy);
        var body = MethodBody(policy, "public static string? DeleteRefusal(string? path, string directory)");
        Assert.Contains("string.Equals(parent, wanted, StringComparison.OrdinalIgnoreCase)", body);
        // StartsWith 判目录＝backups-evil 被当成 backups 里面，"程序代删"于是有了越界入口。
        Assert.DoesNotContain("StartsWith", body);
        Assert.Contains("BackupContainer.IsBackupPath", body);
    }

    [Fact]
    public void DeletingOneAutoFileDoesNotPretendTheOthersWereDeleted()
    {
        // 删除后立刻重扫列表，并且不碰保留策略：PrunePlan 的候选只有 auto- 前缀，
        // 而手动"删这一份"走的是另一条路（DeleteBackup），两者不许共用一个入口。
        var service = ReadRepoFile(Service);
        Assert.DoesNotContain("PrunePlan", MethodBody(service, "public static string DeleteBackup(string? path)"));
        Assert.Contains("AutoBackupPolicy.PrunePlan", MethodBody(service, "public async Task<string?> RunAutoBackupAsync"));
    }
}
