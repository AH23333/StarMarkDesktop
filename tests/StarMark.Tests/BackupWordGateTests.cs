#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-62（P-193 那半句 ＋ 「备份」那一行的 UI 那半句）的闸门：<b>备份与恢复那一路的话只许住在表里，
/// 而且两层同批交（⛔ 半搬）</b>。
/// <para>
/// 射程＝<c>Core/Backup</c> 那五档（服务／路径闸／自动备份策略／压缩包／信封）加上 <c>SettingsPage.Backup.cs</c>
/// 那一档 code-behind，连同 <c>settings.data.backup.</c> 那 62 颗与 <c>settings.data.autoBackupInterval.</c> 那 4 颗，
/// <b>共 66 颗</b>。从前 <c>BackupService</c> 在<b>判决那一刻</b>就把整句拼成 <c>string</c>（
/// <c>RestoreResult.Message</c>／<c>BackupFormatException.Message</c>），换语言那一刻没人回去重算 ⇒
/// 屏幕上那句停在"上一次算话那一刻的语言"（P-188 确证②在这一族的形状）。
/// </para>
/// <para>
/// <b>为什么必须两层一起翻</b>：<c>BackupStatus</c> 那句话的一半由 Core 供（拒绝导入、删除拒绝、恢复回执）。
/// 只搬 UI 那一半就会出现同一行里半中半英——那种"看着像做完了"的状态比原缺陷更难发现（✅WI-61c 把这一族
/// 划出射程时登记的理由，本批按同一口径把它整条翻完）。<c>UpdateStatus</c> 仍归 P-194，本批⛔ 碰。
/// </para>
/// <para>
/// 机制上本批⛔ 新东西：VM 交「键＋实参」＋宿主 <c>LocText.SetMessage</c> 是 ✅WI-61b／WI-61c 走通的那条门，
/// 设置页也<b>不是</b>新的 <c>Locale.Changed</c> 主人（重贴由 <c>LocText</c> 那唯一一处全局订阅负责）。
/// 组合句靠 <see cref="LocMessage.ToString"/> 在<b>贴上去那一刻</b>才取词；换行这种布局交实参
/// （<c>settings.data.backup.lineJoin</c> 的 <c>{2}</c>），⛔ 进表（规则 #100）。
/// </para>
/// <para>颗数一律现读（<c>bin/WI62/pins_62.txt</c>／<c>bin/WI62/callsites_62.py</c>，2026-10-11 现场），⛔ 凭记忆（#159/#160）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class BackupWordGateTests : IDisposable
{
    private const string Svc = "src/StarMark.Core/Backup/BackupService.cs";
    private const string PathPolicy = "src/StarMark.Core/Backup/BackupPathPolicy.cs";
    private const string AutoPolicy = "src/StarMark.Core/Backup/AutoBackupPolicy.cs";
    private const string Container = "src/StarMark.Core/Backup/BackupContainer.cs";
    private const string Envelope = "src/StarMark.Core/Backup/BackupEnvelope.cs";
    private const string Ui = "src/StarMark.UI/Views/SettingsPage.Backup.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string Host = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>对照档：⛔ 本批射程（那半句住在 <c>ClipAssets.DescribeUsage</c>，归下一片）。
    /// 它存在的理由是<b>给归零那颗尺一个分母</b>——一把静默读成 0 的尺比红更危险（台架规则 #188）。</summary>
    private const string Control = "src/StarMark.Abstractions/Clipboard/ClipAssets.cs";

    private static readonly string[] CoreFiles = [Svc, PathPolicy, AutoPolicy, Container, Envelope];
    private static readonly string[] Silent = [.. CoreFiles, Ui];
    private static readonly string[] All = [.. Silent, Vm];

    /// <summary>本批交进表里的 66 颗，逐颗点名（计数型判据抓不到"换名"＝#164）。
    /// <para>口径连着名单一起写：这一族里 <c>settings.data.backupTitle</c>／<c>backupNote</c>／
    /// <c>autoBackupInterval</c>（标签那颗）／<c>autoBackupIntervalTip</c>／<c>backupsHeader*</c> 是<b>本批之前</b>就有的行，
    /// ⛔ 算进本名单（它们各归 ✅WI-2f／WI-61c 的证人）。</para></summary>
    private static readonly string[] Keys =
    [
        "backup.badJson", "backup.unreadable", "backup.pathStart", "backup.notStarMark", "backup.tooNew",
        "backup.noChecksum", "backup.checksumMismatch", "backup.manifestMissing", "backup.packageUnopenable",
        "backup.payloadMissing", "backup.payloadSetsMissing", "backup.itemKeyMissing",
        "backup.snapshotFailed", "backup.restoredMerge", "backup.restoredReplace", "backup.widgetsNotWritten",
        "backup.clipCarried", "backup.clipSkipped", "backup.clipFailed", "backup.clipRowsMissing",
        "backup.clipNotOpened", "backup.clipNoSource", "backup.clipManifestOnly", "backup.restoreFailed",
        "backup.deleted", "backup.deleteFailed", "backup.deleteNoPath", "backup.deleteOutside", "backup.deleteNotBackup",
        "backup.needSavePath", "backup.emptyFileName", "backup.reservedName", "backup.badExtension",
        "backup.needDir", "backup.dirMissing", "backup.needFilePath", "backup.importBadExtension",
        "backup.fileMissing", "backup.pathUnparsable",
        "backup.carrierZip", "backup.carrierManifest", "backup.exportedWith", "backup.exportedImages",
        "backup.exportedNoImages", "backup.exportedMissing", "backup.exportedFailed", "backup.exportedPlain",
        "backup.importFailed", "backup.restoreThrew", "backup.openFolderFailed", "backup.listFailed",
        "backup.fileLine", "backup.exportedLine", "backup.countsLine", "backup.widgetsYes",
        "backup.mergeLine", "backup.replaceLine", "backup.snapshotLine", "backup.carriesImages", "backup.noImages",
        "backup.lineJoin", "backup.withSnapshot",
        "autoBackupInterval.6", "autoBackupInterval.24", "autoBackupInterval.72", "autoBackupInterval.168",
    ];

    /// <summary>那颗胶水键＝三列同一串 <c>{0}{2}{1}</c>，"三语读数必须互异"那一族⛔ 收它（判据另有
    /// <see cref="TheGlueRowTakesTheGapAsAnArgument"/>）。</summary>
    private const string GlueKey = "settings.data.backup.lineJoin";

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    private static string Of(string suffix) => "LocMessage.Of(\"settings.data." + suffix + "\"";

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>出现次数口径：整颗 needle 一起数（⛔ 按行或按字符类数＝#149）。</summary>
    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// ①<b>归零＋分母</b>：这六档抹掉注释之后一颗屏幕字都不剩；同一把尺在对照档必须量得出字，
    /// 否则"0"只是<b>尺没扫到东西</b>（台架规则 #188：普查型仪器的 0 要自带分母＋assert）。
    /// </summary>
    [Fact]
    public void ThoseSixFilesNoLongerSayAnythingThemselves()
    {
        Assert.Equal(6, Silent.Length);
        foreach (var rel in Silent)
            Assert.Empty(SourceGate.ScreenWords(CodeOf(rel)));

        var control = SourceGate.ScreenWords(CodeOf(Control));
        Assert.True(control.Count >= 10,
            $"对照档 {Control} 只量到 {control.Count} 片段——这把尺没扫到东西，上面那些 0 不算证据");
    }

    /// <summary>
    /// ②<b>形状禁项</b>：供话者交出的是身份，⛔ 在那里取词，也⛔ 悄悄把回型退成 <c>string</c>。
    /// <para>与①那格<b>成对</b>（规则 #124）：只钉字面量的话，有人把取词搬回 Core 那一层、字照样对；
    /// 只钉形状的话，有人把整句写死成中文字面量，形状尺看不见字面量。</para>
    /// <para><c>.Render()</c> 在 Core 只许出现在<b>日志那一行</b>（日志按既定口径⛔ 交表），在 UI 只许出现在
    /// 两个"当下就要字符串"的一次性消费者上：文件对话框的 <c>FileTypeChoices</c> 与 <c>InvalidOperationException</c> 的载荷。</para>
    /// </summary>
    [Fact]
    public void TheSuppliersHandIdentitiesAndNeverTakeWords()
    {
        foreach (var rel in Silent)
        {
            var code = CodeOf(rel);
            foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(" })
                Assert.Equal(0, Times(code, banned));
        }

        // Core：唯一的取词点是拒绝删除那句的<b>日志</b>实参，⛔ 变成第 N 处屏幕出口。
        var svc = CodeOf(Svc);
        Assert.Equal(1, Times(svc, ".Render()"));
        Assert.Contains("StarLog.Warn($\"拒绝删除备份：{refusal.Render()}\");", svc, StringComparison.Ordinal);
        foreach (var rel in new[] { PathPolicy, AutoPolicy, Container, Envelope })
            Assert.Equal(0, Times(CodeOf(rel), ".Render()"));

        // 回型⛔ 退成 string（那是"半搬"最先长回来的地方）。
        var env = CodeOf(Envelope);
        Assert.Contains("public LocMessage? Message { get; init; }", env, StringComparison.Ordinal);
        Assert.Equal(0, Times(env, "public string Message"));
        Assert.Contains("public LocMessage Reason { get; }", env, StringComparison.Ordinal);
        Assert.Contains("public BackupFormatException(LocMessage reason) : base(reason.Key)", env, StringComparison.Ordinal);
        Assert.Equal(0, Times(env, "public BackupFormatException(string"));

        Assert.Contains("public static LocMessage DeleteBackup(string? path)", svc, StringComparison.Ordinal);
        Assert.Equal(0, Times(svc, "public static string DeleteBackup"));
        Assert.Contains("public static LocMessage? DeleteRefusal(string? path, string directory)", CodeOf(AutoPolicy), StringComparison.Ordinal);
        Assert.Contains("public static LocMessage IntervalMessage(int hours)", CodeOf(AutoPolicy), StringComparison.Ordinal);

        var path = CodeOf(PathPolicy);
        foreach (var sig in new[]
                 {
                     "public static (string? Path, LocMessage? Error) ForExport(string? raw, string defaultDirectory)",
                     "public static (string? Path, LocMessage? Error) ForImport(string? raw)",
                     "private static string? TryFull(string text, out LocMessage? error)",
                 })
            Assert.Contains(sig, path, StringComparison.Ordinal);
        Assert.Equal(0, Times(path, "out string? error"));

        // UI：四颗 .Render() 各有其名，⛔ 多出一颗＝那里又在取词。
        var ui = CodeOf(Ui);
        Assert.Equal(4, Times(ui, ".Render()"));
        Assert.Equal(2, Times(ui, "PickerBlockedHint.Render(), ex"));
        Assert.Equal(2, Times(ui, ".Render(), new[] {"));
        Assert.Equal(0, Times(CodeOf(Vm), ".Render()"));
        Assert.Contains("LocMessage? error", ui, StringComparison.Ordinal);
        Assert.Contains("ViewModel.BackupStatus = ex.Reason;", ui, StringComparison.Ordinal);
    }

    /// <summary>③<b>键名单双向对账</b>：66 颗每颗在表里恰好一行、在被改的那七档里恰好点名一次。</summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedByItsOwner()
    {
        Assert.Equal(66, Keys.Length);
        Assert.Equal(62, Keys.Count(k => k.StartsWith("backup.", StringComparison.Ordinal)));
        Assert.Equal(4, Keys.Count(k => k.StartsWith("autoBackupInterval.", StringComparison.Ordinal)));
        Assert.Equal(Keys.Length, Keys.Distinct(StringComparer.Ordinal).Count());

        var codes = string.Join("\n", All.Select(CodeOf));
        var host = CodeOf(Host);
        var namedTwice = new List<string>();
        foreach (var suffix in Keys)
        {
            var full = "settings.data." + suffix;
            Assert.Single(UiStrings.All, e => e.Key == full);
            var hits = All.Sum(rel => Times(CodeOf(rel), Quoted(full)));
            Assert.True(hits is 1 or 2, $"{full} 在那七档里被点名 {hits} 次（预期 1，⛔ 0＝死键，⛔ >2＝两处主人）");
            if (hits == 2) namedTwice.Add(full);
            Assert.Equal(0, Times(host, Quoted(full)));        // 句子⛔ 由宿主递：那一侧只该拿身份去取词
        }
        // 现场只有那颗"导入失败"有两位主人（状态行与详情卡各回一次）；多一颗就是有人另起了一处措辞出处。
        Assert.Equal(new[] { "settings.data.backup.importFailed" }, namedTwice);
    }

    /// <summary>
    /// ④<b>每一处调用点递的实参数＝那一行声明的槽数</b>（按括号深度扫，共用件 <see cref="SourceGate.KeyedCallSites"/>）。
    /// <para>少递一颗时表闸门全绿、编译也过，屏幕上那一格<b>整空白</b>（坑表 #393）。这一格的分母必须连着
    /// 尺的口径写：本批 66 颗里有 <b>8 颗不归这把尺</b>——四颗住在 <c>AutoBackupPolicy.IntervalKeys</c> 的裸字面量里，
    /// 四颗以"三元选键"的形状出现；它们由 <see cref="TheEightSitesTheDepthRulerCannotSeeAreNamedByHand"/> 逐颗钉。</para>
    /// </summary>
    [Fact]
    public void EveryCallSiteHandsAsManyArgsAsItsRowDeclares()
    {
        var family = Keys.Select(k => "settings.data." + k).ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in All)
        {
            foreach (var (key, args, line) in SourceGate.KeyedCallSites(CodeOf(rel)))
            {
                if (!family.Contains(key)) continue;
                watched++;
                var slots = SourceGate.RowSlots(key);
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        }
        Assert.Empty(offenders);
        Assert.Equal(59, watched);        // 2026-10-11 现场（bin/WI62/callsites_62.py 同一形状复算）
    }

    /// <summary>
    /// ⑤那八颗"按深度扫的尺看不见"的键：点名方式逐颗钉，⛔ 让"读成 0"冒充"没有调用点"（规则 #188／#155）。
    /// <para>四颗间隔键＝<c>IntervalKeys</c> 那排<b>裸字面量</b>（下拉的数据项＋模板按位置取词）；
    /// 四颗＝两处"三元选键"（合并／覆盖、带图／只带条目）加导出回执那一颗跨行写的调用。</para>
    /// </summary>
    [Fact]
    public void TheEightSitesTheDepthRulerCannotSeeAreNamedByHand()
    {
        var auto = CodeOf(AutoPolicy);
        Assert.Equal(4, Keys.Count(k => k.StartsWith("autoBackupInterval.", StringComparison.Ordinal)));
        foreach (var suffix in new[] { "autoBackupInterval.6", "autoBackupInterval.24", "autoBackupInterval.72", "autoBackupInterval.168" })
            Assert.Equal(1, Times(auto, Quoted("settings.data." + suffix)));
        Assert.Contains("public static readonly string[] IntervalKeys =", auto, StringComparison.Ordinal);
        Assert.Contains("new LocOption(AutoBackupPolicy.IntervalKeys[index], string.Empty, hours)", CodeOf(Vm), StringComparison.Ordinal);

        var svc = CodeOf(Svc);
        Assert.Contains("mode == RestoreMode.Replace ? \"settings.data.backup.restoredReplace\" : \"settings.data.backup.restoredMerge\",",
            svc, StringComparison.Ordinal);
        var ui = CodeOf(Ui);
        Assert.Contains("? \"settings.data.backup.carrierZip\" : \"settings.data.backup.carrierManifest\");",
            ui, StringComparison.Ordinal);
    }

    /// <summary>⑥<b>语言读数</b>：每一颗都三语答话、三语互异（胶水那颗除外），槽全填满且⛔ 在屏幕上露身份。</summary>
    [Fact]
    public void EveryRowAnswersInThreeLanguagesAndFillsItsSlots()
    {
        var withSlots = 0;
        foreach (var suffix in Keys)
        {
            var key = "settings.data." + suffix;
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var texts = new[] { entry.Zh, entry.En, entry.Ja };
            foreach (var text in texts) Assert.False(string.IsNullOrWhiteSpace(text), key + " 有一列是空的");
            var slots = SourceGate.RowSlots(key);
            if (slots > 0) withSlots++;

            if (key == GlueKey)
            {
                foreach (var text in texts) Assert.Equal("{0}{2}{1}", text);
                continue;
            }
            Assert.NotEqual(texts[0], texts[1]);
            Assert.NotEqual(texts[1], texts[2]);
            Assert.NotEqual(texts[0], texts[2]);

            var args = Enumerable.Range(0, slots).Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                // ⛔ 拿 TrySet 的返回值当"换没换到"的探针：它说的是"变没变"（#112）。
                Assert.Equal(language, Locale.Current);
                var text = slots == 0 ? Locale.T(key) : Locale.Fmt(key, args);
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);      // 槽没填满＝屏幕上露 {0}
                Assert.DoesNotContain("}", text, StringComparison.Ordinal);
                Assert.DoesNotContain("settings.data.", text, StringComparison.Ordinal);   // ⛔ 把身份念给用户听
                if (slots > 0) Assert.Contains("M0", text, StringComparison.Ordinal);       // 读数进槽，⛔ 被句子吞掉
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }
        Assert.Equal(38, withSlots);      // 2026-10-11 现场（bin/WI62/pins_62.txt ②）
    }

    /// <summary>
    /// ⑦<b>两半同语言</b>：回执是套娃的（外层「已合并恢复 N 条」＋内层「剪贴板图片：带着 X 个…」＋快照路径）。
    /// <para>判据＝同一棵树在<b>两种语言下各建一遍</b>，最后都在同一拍贴出去，两句必须逐字相等。
    /// 谁把内层改成先 <c>.Render()</c> 再递字符串，"在中文里算、到英文里贴"那一格就与"在英文里算、在英文里贴"那一格分岔
    /// ——屏幕上剩下半中半英，那正是 P-188 原始缺陷在这一族的形状。</para>
    /// <para>另外单独钉一条：整棵英文的树贴出来⛔ 夹一个本族文字（坑表 #403 那条口径，⛔ 只比字符串相等）。</para>
    /// </summary>
    [Fact]
    public void TheNestedReceiptStandsInTheSameLanguage()
    {
        static LocMessage Tree() => LocMessage.Of(
            "settings.data.backup.withSnapshot",
            LocMessage.Of(
                "settings.data.backup.restoredMerge", 12,
                LocMessage.Of("settings.data.backup.clipCarried", 5, 3,
                    LocMessage.Of("settings.data.backup.clipSkipped", 2), null,
                    LocMessage.Of("settings.data.backup.clipRowsMissing", 1)),
                null),
            "C:\\snap.zip");

        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        foreach (var paste in languages)
        {
            Locale.TrySet(paste);
            Assert.Equal(paste, Locale.Current);
            var said = Tree().Render();          // 就在贴出去这一拍才建、才取词

            foreach (var build in languages)
            {
                Locale.TrySet(build);
                var froze = Tree();              // 建在别的语言那一拍
                Locale.TrySet(paste);
                Assert.Equal(said, froze.Render());   // 贴在现在这一拍 ⇒ 整句仍同一种语言
            }

            Assert.DoesNotContain("{", said, StringComparison.Ordinal);
            Assert.DoesNotContain("}", said, StringComparison.Ordinal);
            Assert.DoesNotContain("settings.data.", said, StringComparison.Ordinal);
            Assert.Contains("12", said, StringComparison.Ordinal);
            Assert.Contains("C:\\snap.zip", said, StringComparison.Ordinal);
            Assert.Equal(paste == AppLanguage.English, !SourceGate.Cjk.IsMatch(said));
        }
    }


    /// <summary>
    /// ⑧那颗胶水键只干一件事：把两半连起来，<b>而换行作为实参递进来</b>（规则 #100：表里⛔ 控制字符，那是布局）。
    /// <para>⛔ 在 C# 里拼 <c>head + "\n" + tail</c>：语序与连接符一旦写进代码，那一行就只在中文里成立
    /// （✅WI-60b 那颗 <c>widget.mon.note.join</c>／✅WI-61c 那颗 <c>autoBackupSentence</c> 同一口径）。</para>
    /// </summary>
    [Fact]
    public void TheGlueRowTakesTheGapAsAnArgument()
    {
        var ui = CodeOf(Ui);
        Assert.Equal(1, Times(ui, Of("backup.lineJoin")));
        Assert.Contains("private static LocMessage Stack(LocMessage head, LocMessage? tail, string gap = \"\\n\")",
            ui, StringComparison.Ordinal);
        Assert.Equal(7, Times(ui, "Stack("));      // 声明那颗＋现场六处调用（bin/WI62/pins_62.txt 现读）

        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var joined = LocMessage.Of(GlueKey, "AAA", "BBB", "|").Render();
            Assert.Equal("AAA|BBB", joined);
            Assert.Equal("AAABBB", LocMessage.Of(GlueKey, "AAA", "BBB", string.Empty).Render());
            // tail 交 null 的那一路：Stack 自己就不产 lineJoin；这里钉的是"那一格确实变空"，
            // ⛔ 让空槽在屏幕上留一颗孤立的分隔符。
            Assert.Equal("AAA|", LocMessage.Of(GlueKey, "AAA", null, "|").Render());
        }

        // 表里那 66 行⛔ 藏控制字符（换行由实参递）。
        foreach (var suffix in Keys)
        {
            Assert.True(UiStrings.TryGet("settings.data." + suffix, out var entry));
            foreach (var text in new[] { entry.Zh, entry.En, entry.Ja })
            {
                Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
                Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// ⑨那颗<b>状态行</b>的接线：XAML ⛔ 再绑它（绑上去就是一句停在旧语言的字），改由宿主贴，且两个时机缺一不可。
    /// <para>这一格与 <c>SettingsDataWordGateTests</c> 里那几格同一口径，只把本批翻过来的 <c>BackupStatus</c>
    /// 那一行<b>逐颗点名</b>（#164）：进页面那一次回灌＋<c>PropertyChanged</c> 里那一颗 <c>nameof</c>。</para>
    /// </summary>
    [Fact]
    public void TheHostPastesTheBackupRowFromTwoTimings()
    {
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        var host = CodeOf(Host);
        Assert.DoesNotContain("{x:Bind ViewModel.BackupStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BackupStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, Times(host, "LocText.SetMessage(BackupStatusText, ViewModel.BackupStatus);"));
        Assert.Equal(1, Times(host, "nameof(SettingsPageViewModel.BackupStatus)"));
        Assert.Contains("[ObservableProperty] private LocMessage? _backupStatus;", CodeOf(Vm), StringComparison.Ordinal);
        Assert.Equal(0, Times(CodeOf(Vm), "private string _backupStatus"));
        Assert.Equal(0, Times(CodeOf(Vm) + host, "BackupStatus = \""));
        Assert.Equal(0, Times(CodeOf(Vm) + host, "BackupStatus = $\""));

        // 自动备份那颗下拉同批改成"数据项＋模板"（闭合显示⛔ 停在旧语言＝WI-2b 登记过的那颗不彻底）。
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.AutoBackupIntervalOptions, Mode=OneTime}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource LocOptionTemplate}\"", xaml, StringComparison.Ordinal);
    }
}
