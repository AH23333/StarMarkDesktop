#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Widgets;
using StarLog = StarMark.Abstractions.StarLog;

namespace StarMark.Core.Backup;

/// <summary>
/// 备份与恢复。
/// </summary>
/// <remarks>
/// <b>为什么必须有它：</b>条目本体（书签 / Star / 文件）可重新同步，
/// 但用户手写笔记、标签体系、隐藏/置顶状态、桌面组件的待办与随记<b>全部不可重建</b>。
/// 重命名数据库、升级失败、误触清空，都会一次性带走。
///
/// <b>三条硬性规则（前两条是浏览器扩展 backup.ts 缺失的）：</b>
/// 1. <b>导入前自动快照</b>——先落盘再动数据，任何恢复都可回滚。
/// 2. <b>校验和前置</b>——不匹配直接拒绝，不等 Upsert 崩到一半留下半截状态。
/// 3. <b>语义分层</b>——条目与用户元数据分开存，可只恢复元数据。
/// </remarks>
public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IBackupRepository _repo;

    public BackupService(IBackupRepository repo)
    {
        _repo = repo;
    }

    /// <summary>自动快照目录（恢复前快照、日后可能的定时备份都放这里）。</summary>
    public static string SnapshotDirectory
    {
        get
        {
            if (SnapshotDirectoryOverride is not null) return SnapshotDirectoryOverride;
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, StarMark.Abstractions.AppConstants.AppName, "backups");
        }
    }

    /// <summary>测试用：把快照目录重定向到临时目录，避免污染真实 AppData。也可由宿主在特殊环境下覆盖。</summary>
    public static string? SnapshotDirectoryOverride { get; set; }

    // ==================== 导出 ====================

    public async Task<BackupEnvelope> ExportAsync(CancellationToken ct = default)
    {
        var env = new BackupEnvelope
        {
            ExportedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Payload = new BackupPayload
            {
                Items = new List<Item>(await _repo.ExportItemsAsync(ct)),
                UserState = new List<UserStateRecord>(await _repo.ExportUserStateAsync(ct)),
                Tags = new List<TagRecord>(await _repo.ExportTagsAsync(ct)),
                ItemTags = new List<ItemTagLink>(await _repo.ExportItemTagLinksAsync(ct)),
                WidgetsJson = ReadWidgetsJson(),
            },
        };
        env.Checksum = ComputeChecksum(env.Payload);
        return env;
    }

    /// <summary>
    /// 导出一份<b>带剪贴板图片本体</b>的备份（§3-Q2 的 a 期）。<b>刻意做成另一个方法而不是加一个布尔参数</b>：
    /// 布尔的含义只写在被调方时，接线处写反是必然风险（本仓踩过两次，症状都是"全绿而功能是反的"）。
    /// <para>只用在<b>用户主动点"导出备份"</b>那一条路上。恢复前快照与每日自动件<b>不带附件</b>
    /// （见 <see cref="WriteSnapshotAsync"/> 与 <see cref="RunAutoBackupAsync"/>）：它们是回滚点，
    /// 一份几十 MB 的库配几百 MB 的图会把"随时能撤一步"变成"每次恢复都先填一盘磁盘"。</para>
    /// <para>没有图片可带时仍写 <c>.json</c>（与不带附件的导出逐字节相同）；有图片时写同名 <c>.zip</c>，
    /// 返回值里的路径就是实际写出去的那一个——<b>扩展名跟着内容走</b>，
    /// 一个叫 <c>.json</c> 的 zip 会让下一个拿它的人先输错一次。</para>
    /// </summary>
    public async Task<BackupExport> ExportWithClipImagesAsync(string path, CancellationToken ct = default)
    {
        var env = await ExportAsync(ct);
        var (names, missing, plannedFailed) = ClipboardImageStore.PlanBackup(env.Payload.Items);
        var json = SerializeEnvelope(env);
        var target = AgreeExtensionWithContent(path, names.Count > 0);
        if (names.Count == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, json, ct);
            return new BackupExport(target, env.Payload.Items.Count, 0, 0, missing, plannedFailed);
        }
        var (carried, carriedBytes) = await BackupContainer.WriteAsync(target, json, names, ct);
        // 计划要带、结果没进包的那些（此刻正被别的程序独占、或刚被删掉）并进"没能带上"这一档：
        // 报出去的张数与字节都只算真在包里的，两个数说的是同一份东西。
        return new BackupExport(target, env.Payload.Items.Count, carried, carriedBytes, missing,
            plannedFailed + names.Count - carried);
    }

    /// <summary>一次带附件导出的事实：实际写到哪儿、装了多少条、带了几张图、多少字节、几张没带上。</summary>
    public sealed record BackupExport(
        string Path, int ItemCount, int ClipImages, long ClipImageBytes, int MissingImages, int FailedImages);

    /// <summary>
    /// 写<b>不带附件</b>的备份（恢复前快照、每日自动件、以及"开关关着导出的那一份"）。
    /// 返回实际写出去的路径：可能不是传进来的那一个，因为扩展名跟着内容走（见 <see cref="AgreeExtensionWithContent"/>）。
    /// </summary>
    public async Task<string> ExportToFileAsync(string path, CancellationToken ct = default)
    {
        var env = await ExportAsync(ct);
        var target = AgreeExtensionWithContent(path, carriesImages: false);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, SerializeEnvelope(env), ct);
        return target;
    }

    /// <summary>
    /// 扩展名跟着内容走：带图片本体的那份是 <c>.zip</c>，纯清单那份不许占着 <c>.zip</c> 这个名字。
    /// <para>两个方向都要改：<b>一个叫 <c>.zip</c> 的 JSON</b>（开关开着但本机一张图都没有、或用户在开关关着时
    /// 手输了 <c>x.zip</c>）会被下一个拿它的人按压缩包去读，而读它的人正是本程序的导入路径——
    /// 报出来的是"这个备份包打不开"，让人以为文件坏了，其实只是名字错了。</para>
    /// </summary>
    private static string AgreeExtensionWithContent(string path, bool carriesImages)
        => BackupContainer.IsContainer(path) == carriesImages
            ? path
            : BackupContainer.SwapExtension(path);

    /// <summary>清单的序列化<b>只有这一处</b>：写盘、打包、校验和三处调用方共用同一串字节。</summary>
    private static string SerializeEnvelope(BackupEnvelope env) => JsonSerializer.Serialize(env, JsonOptions);

    // ==================== 读取与校验 ====================

    /// <summary>
    /// 读取并校验备份文件——<b>两种载体都从这一个门进</b>：<c>.json</c> 直接读，
    /// 带附件的 <c>.zip</c> 读它里面那份清单（校验与版本判定完全同一套，附件不改变清单的字节）。
    /// 校验和不匹配或版本不兼容时抛 <see cref="BackupFormatException"/>。
    /// </summary>
    public static async Task<BackupEnvelope> ReadAsync(string path, CancellationToken ct = default)
    {
        var text = await BackupContainer.ReadManifestAsync(path, ct);
        BackupEnvelope env;
        try
        {
            env = JsonSerializer.Deserialize<BackupEnvelope>(text, JsonOptions)
                  ?? throw new BackupFormatException("备份文件不是合法的 JSON。");
        }
        catch (JsonException ex)
        {
            // 半份文件、被改过后缀的压缩包、手改坏一个字符的清单都落到这里。
            // 报成"读不懂"而不是把 .NET 那句英文（"The JSON value could not be converted to …"）递给用户：
            // 前者是可执行的结论（这份文件不能用，换一份），后者只会让人以为是应用坏了。
            throw new BackupFormatException(
                $"这个备份文件读不懂（坏在 {(string.IsNullOrEmpty(ex.Path) ? "开头" : ex.Path)}）：可能没写完或已损坏，已拒绝导入。");
        }

        if (!string.Equals(env.App, BackupEnvelope.AppId, StringComparison.Ordinal))
            throw new BackupFormatException($"这不是 StarMark 桌面版备份（app={env.App}）。");

        if (env.Version > BackupEnvelope.CurrentVersion)
            throw new BackupFormatException(
                $"备份版本 {env.Version} 高于当前支持的 {BackupEnvelope.CurrentVersion}，请升级应用后再导入。");

        if (string.IsNullOrEmpty(env.Checksum))
            throw new BackupFormatException("备份文件缺少校验和，无法验证完整性，已拒绝导入。");

        var actual = ComputeChecksum(env.Payload);
        if (!string.Equals(actual, env.Checksum, StringComparison.Ordinal))
            throw new BackupFormatException("校验和不匹配，备份文件已损坏或被篡改，已拒绝导入。");

        return env;
    }

    /// <summary>
    /// 从<b>已解析</b>的信封算摘要，供 UI 确认框展示条数。
    /// 旧签名是 <c>Peek(string path)</c>：唯一调用方（导入备份）在那之前已经用 <see cref="ReadAsync"/>
    /// 把整份文件读盘并反序列化过一次，再走 path 版本等于为一部可达数十 MB 的备份重复读+解析一次
    /// （且在 UI 线程上）。改为收 env 后该次冗余 I/O 归零，也不再需要吞异常的"摘要读不出"分支。
    /// </summary>
    public static BackupSummary Summarize(BackupEnvelope env)
        => new(
            env.ExportedAt,
            env.Payload.Items.Count,
            env.Payload.UserState.Count,
            env.Payload.Tags.Count,
            env.Payload.WidgetsJson is not null);

    // ==================== 恢复 ====================

    /// <summary>
    /// 恢复。<b>任何模式下都会先落一份当前数据的快照</b>，再动数据库。
    /// <para><paramref name="attachmentSourcePath"/>＝<b>那份备份文件自己的路径</b>：带图片的包要在条目落库之后
    /// 再按"这些条目认领哪些文件名"把附件解回 clip 目录。刻意传路径而不是传字节（几百张一次进内存＝几百 MB），
    /// 也刻意不在此处重新解析清单——清单来自哪份文件，附件就该从同一份文件解，调用方只要把同一个路径递进来。</para>
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(
        BackupEnvelope env,
        RestoreMode mode = RestoreMode.Merge,
        string? widgetsTargetPath = null,
        CancellationToken ct = default,
        string? attachmentSourcePath = null)
    {
        // 规则 0（先于快照与清库）：Replace 会先清空 items/item_tags 再导入，若导入途中抛异常，
        // 已提交的清空不会回滚 → 库被毁却只报「恢复失败」。校验和只保证字节完整、不保证语义合法，
        // 故在动任何数据前先拒绝缺业务键（Source/SourceId）的条目、并把可空字符串/集合归一到安全值，
        // 让「会崩到一半留半截状态」的载荷根本进不到清库那步。合法备份恒满足，行为不变。
        var validateFail = ValidatePayload(env.Payload);
        if (validateFail is not null)
            return new RestoreResult { Success = false, Message = validateFail };

        // 规则 1：导入前自动快照（扩展没有这一步，是最该补的）
        string? snapshotPath = null;
        try
        {
            snapshotPath = await WriteSnapshotAsync(ct);
        }
        catch (Exception ex)
        {
            // 快照失败不应静默继续——否则恢复出错就无从回滚
            return new RestoreResult
            {
                Success = false,
                Message = $"写入恢复前快照失败，已中止导入以免无法回滚：{ex.Message}",
            };
        }

        try
        {
            var p = env.Payload;

            if (mode == RestoreMode.Replace)
            {
                await _repo.ClearItemTagLinksAsync(ct);
                await _repo.ClearItemsAsync(ct);
            }

            await _repo.ImportItemsAsync(p.Items, ct);
            await _repo.ImportUserStateAsync(p.UserState, ct);
            await _repo.ImportTagsAsync(p.Tags, ct);
            await _repo.ImportItemTagLinksAsync(p.ItemTags, ct);

            // 所有导入落库后做一次「全表」search_text 重算 + FTS rebuild（复用迁移里的同一口径）。
            // 覆盖两类缺失：① 导出条目不带 Tags，按标签词的全文检索需要把标签名烘进 search_text；
            // ② ImportUserStateAsync 用裸 UPDATE SET notes 改正文却不重算 search_text，AFTER UPDATE
            // 触发器只按旧值重灌 → 未打标签行的还原笔记搜不到。全表重算一并修掉两者。
            await _repo.ReindexAllSearchTextAsync(ct);

            bool widgetsRestored = false;
            string? widgetsError = null;
            if (!string.IsNullOrEmpty(p.WidgetsJson))
            {
                (widgetsRestored, widgetsError) = WriteWidgetsJson(p.WidgetsJson!, widgetsTargetPath);
            }

            // 附件必须在条目落库<b>之后</b>解：白名单是"这一份备份里的条目认领哪些文件名"，
            // 而条目本身刚刚才写进库。放在清库之前解会出现"文件已落盘、行被 Replace 清掉"的孤儿。
            var clip = attachmentSourcePath is null
                ? (Written: 0, Skipped: 0, Failed: 0, PackageEntries: 0, RowsWithoutPicture: 0, Opened: true)
                : await ExtractClipImagesAsync(p.Items, attachmentSourcePath, ct);

            // 还原走的是批量 DELETE + INSERT（绕开 ItemRepository 各写方法的 Notify），

            // 而 ReindexAllSearchTextAsync 的注释约定「调用方收尾统一刷新」——
            // 但 SettingsPage 的调用方只更新了一行状态文本、并未刷新任何界面/组件。
            // 这里在成功返回前补一次广播：主界面计数/列表页与各组件的 DataChangeReloader 才会去抖重载，
            // 否则还原后满屏仍是旧数据（要重启才更新）。
            DataChangeHub.Notify();

            // 备份里带了组件数据却写盘失败时，若仍返回纯成功文案就是「假成功」——调用方
            // (SettingsPage) 只把 rr.Message 显示到状态栏、并不单独呈现 WidgetsRestored，
            // 用户会误以为组件也一并还原了。这里把失败并入文案，Success 仍为 true（条目本体确已还原）。
            // 原因必须一起给出：旧文案是"请稍后重试或检查 %APPDATA% 是否被占用"，等于让用户去猜
            // 一个日志里已经写明的东西（P-54）。
            var widgetsNote = (!string.IsNullOrEmpty(p.WidgetsJson) && !widgetsRestored)
                ? $" 但桌面组件数据未能写入（组件保持原状）：{widgetsError}"
                : string.Empty;

            // 图片本体的那句必须按<b>这一份备份到底带没带</b>来说，三种事实三种话（§3-Q2 反批：b 期文案
            // 在 a 期上线前不许撤——现在 a 上线了，就要按事实分支，留着它同样是假话）：
            // ① 包里确实有附件 → 报"包里几张 / 写回几张 / 跳过几张 / 写不进几张"；
            // ② 条目落库了但一张都没带（开关关着导的，或 a 期之前的老备份）→ 保留原来那句；
            // ③ 带了、但仍有历史行在这份包里就没有对应文件 → 单独报，不许一句"已恢复"盖过去。
            // 这里每一档说的都是"这一份文件里的事实"，不是"用户机器上现在一共有几张图"。
            var clipImageCount = p.Items.Count(i =>
                i.Source == ItemSources.Clipboard && ClipboardEntry.IsImageOf(i.ExtraJson));
            var clipImageNote = clip.PackageEntries switch
            {
                > 0 => ClipPackageSentence(clip),
                0 when clipImageCount == 0 => string.Empty,
                // 包打不开（被云盘抽成占位件、正被别的程序独占、或刚被删掉）：这时"这份备份不带图片本体"
                // 是没资格说的——我们连里面有什么都没看见。单独一句，并把原因留在日志里。
                0 when !clip.Opened
                    => " 剪贴板图片：这次没能打开那份附件包（原因见日志），所以一张图片都没解出来；"
                      + "条目已照常恢复，缺的会标成“文件缺失”，再导一次即可。",
                // 调用方没把那份文件的路径递进来（只有不经 UI 的调用会这样）：谁也没打开过那个包，
                // 因此"这份备份不带图片本体"这句没资格说——它是对文件内容的断言，得看过文件才配说。
                0 when attachmentSourcePath is null
                    => $" 其中 {clipImageCount} 条是剪贴板图片：这次没有从备份包里解出任何图片文件，"
                      + "这些条目只有在本机图片目录里还留着对应文件时才打得开，缺的会标成“文件缺失”。",
                _ => $" 其中 {clipImageCount} 条是剪贴板图片：这份备份只带条目与文件名、不带图片本体"
                      + "（导出时那颗开关是关的，或它是带附件之前的版本），所以这些条目只有在本机图片目录里"
                      + "还留着对应文件时才打得开，缺的会标成“文件缺失”。",
            };

            return new RestoreResult
            {
                Success = true,
                Message = (mode == RestoreMode.Replace
                    ? $"已覆盖恢复 {p.Items.Count} 条条目。"
                    : $"已合并恢复 {p.Items.Count} 条条目。") + widgetsNote + clipImageNote,
                SnapshotPath = snapshotPath,
                ItemsRestored = p.Items.Count,
                UserStatesRestored = p.UserState.Count,
                TagsRestored = p.Tags.Count,
                LinksRestored = p.ItemTags.Count,
                WidgetsRestored = widgetsRestored,
                ClipImagesRestored = clip.Written,
                ClipImagesSkipped = clip.Skipped,
                ClipImagesFailed = clip.Failed,
            };
        }
        catch (Exception ex)
        {
            StarLog.Error("恢复失败", ex);
            return new RestoreResult
            {
                Success = false,
                Message = $"恢复失败：{ex.Message}\n已保留恢复前快照：{snapshotPath}",
                SnapshotPath = snapshotPath,
            };
        }
    }

    /// <summary>把当前数据落一份快照到自动快照目录，返回文件路径。</summary>
    public async Task<string> WriteSnapshotAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(SnapshotDirectory);
        // 锁定 InvariantCulture：非公历区域（th-TH 佛历 / ar-SA 希吉来历）下不加锁定会得到错误年份，破坏回滚点按名排序的时间序。
        var name = $"{AutoBackupPolicy.SnapshotPrefix}{DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json";
        var path = Path.Combine(SnapshotDirectory, name);
        await ExportToFileAsync(path, ct);
        return path;
    }

    // ==================== 备份盘点（界面上的"本机已有备份"列表） ====================

    /// <summary>一份备份文件的盘点结果（路径、类别、写入时刻、体积）。</summary>
    public sealed record BackupFile(
        string Path, string FileName, DateTimeOffset ModifiedUtc, long LengthBytes,
        AutoBackupPolicy.BackupKind Kind);

    /// <summary>
    /// 列出目录里的备份件，按写入时刻<b>倒序</b>（同一秒内再按文件名倒序，避免刷新时上下抖），
    /// 最多 <paramref name="max"/> 份。
    /// <para>
    /// 存在的理由：卡片一直写着"应用会自己备份""任何恢复都可回滚"，却没有任何地方能看到
    /// "有哪些份、什么时候、多大"——要恢复指定某一份得自己敲文件名，回滚点也得靠同一份文件手动导入。
    /// 这里先把盘点做成可单测的纯扫描，界面上的列表与「恢复」按钮都从它取数。
    /// </para>
    /// <para>
    /// 容错方向刻意偏"少说不是错"：目录不存在或没有件＝空表；<b>单个文件的属性读不出来只跳过该件</b>
    /// （一份正被云盘/杀软占用的件不该让整列消失）；整个目录枚举失败才记一行日志返回空。
    /// </para>
    /// </summary>
    public static IReadOnlyList<BackupFile> EnumerateBackups(string? directory = null, int max = 50)
    {
        var dir = directory ?? SnapshotDirectory;
        var list = new List<BackupFile>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            // 两种载体都要盘点：带图片的导出是 .zip，只带条目的还是 .json。
            // 少扫一种，界面上就是"我明明导出过三份，列表只有两份"——而那份 zip 恰恰是唯一带图的。
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles()
                         .Where(f => BackupContainer.IsBackupPath(f.Name)))
            {
                try
                {
                    list.Add(new BackupFile(f.FullName, f.Name,
                        // LastWriteTimeUtc 是 Kind=Utc 的 DateTime：不显式包装会被按本地时区解释，差出一个时区。
                        new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero),
                        f.Length, AutoBackupPolicy.Classify(f.Name)));
                }
                catch (Exception ex)
                {
                    StarLog.Warn($"备份件属性读不出，跳过该件（{f.Name}）：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            StarLog.Warn($"扫描备份目录失败（{dir}）：{ex.Message}");
            return list;
        }
        return list.OrderByDescending(b => b.ModifiedUtc)
                   .ThenByDescending(b => b.FileName, StringComparer.Ordinal)
                   .Take(Math.Max(0, max))
                   .ToList();
    }

    // ==================== 自动备份（P-51） ====================

    /// <summary>
    /// 每日自动备份：距最近一份 <c>auto-</c> 件超过 <see cref="AutoBackupPolicy.MinGap"/> 才落盘，
    /// 落完后把自动件裁到最近 <see cref="AutoBackupPolicy.Keep"/> 份（手动导出与 pre-restore 快照不动）。
    /// <para>返回写出的路径；本次跳过（间隔未到 / 空库）返回 null。<b>异常上抛</b>由调用方记日志——
    /// 在这儿吞掉就会出现"以为备份了其实没有"，与本条存在的理由相反。</para>
    /// </summary>
    public async Task<string?> RunAutoBackupAsync(CancellationToken ct = default)
    {
        var dir = SnapshotDirectory;
        if (!AutoBackupPolicy.ShouldRun(DateTimeOffset.UtcNow, NewestAutoBackupUtc(dir)))
            return null;

        var env = await ExportAsync(ct);
        // 空库不落盘：首次运行的"空备份"只是占位噪声，还会把"最近一份"的时钟推后 24h。
        if (env.Payload.Items.Count == 0 && env.Payload.UserState.Count == 0 && env.Payload.Tags.Count == 0)
            return null;

        Directory.CreateDirectory(dir);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(dir, $"{AutoBackupPolicy.Prefix}{stamp}.json");
        await File.WriteAllTextAsync(path, SerializeEnvelope(env), ct);

        foreach (var stale in AutoBackupPolicy.PrunePlan(EnumerateAutoBackups(dir)))
        {
            try { File.Delete(stale); }
            catch (Exception ex)
            {
                // 清理失败不影响本次备份已落盘；被占用的过期件下次启动会再试。
                StarLog.Warn($"自动备份过期件未删掉（{stale}）：{ex.Message}");
            }
        }
        return path;
    }

    /// <summary>最近一份自动件的写入时刻；目录不存在/没有自动件/探测被拦都返回 null（＝该备份）。</summary>
    private static DateTimeOffset? NewestAutoBackupUtc(string dir)
        => EnumerateAutoBackups(dir).Max(f => (DateTimeOffset?)f.ModifiedUtc);

    private static IEnumerable<(string Path, DateTimeOffset ModifiedUtc)> EnumerateAutoBackups(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return Array.Empty<(string, DateTimeOffset)>();
            return new DirectoryInfo(dir).EnumerateFiles(AutoBackupPolicy.Prefix + "*.json")
                .Where(f => AutoBackupPolicy.IsAuto(f.Name))
                // LastWriteTimeUtc 是 Kind=Utc 的 DateTime；直接当 DateTimeOffset 用会按本地时区解释，
                // 差出一个时区的量就足以让"24 小时"判定天天提前或天天推后。
                .Select(f => (f.FullName, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
                .ToList();
        }
        catch (Exception ex)
        {
            StarLog.Warn($"扫描自动备份目录失败（{dir}）：{ex.Message}");
            return Array.Empty<(string, DateTimeOffset)>();
        }
    }

    // ==================== 内部 ====================

    /// <summary>SHA-256，覆盖载荷的规范序列化（无缩进、属性顺序固定）。</summary>
    internal static string ComputeChecksum(BackupPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalOptions);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 把附件解回图片目录。白名单<b>由这一份备份的条目现算</b>（而不是由调用方传进来）：
    /// "哪些文件名属于这次恢复"只有看着刚落库的那批条目才说得清，传字符串数组就等于让调用方可以
    /// 递进任意名字——而下一个动作是往用户的图片目录里写文件。
    /// <para>同时算出<b>在这份包里根本没有对应文件的图片行数</b>（<c>RowsWithoutPicture</c>）。
    /// 这个数<b>不能用"包里的条目数 − 写回数"之类减法得到</b>：条目与文件是两种量纲（一行占两张文件，
    /// 旧行只占一张），3b 自查时正是拿行减文件，得出"缺 0 条"而实际缺一条。唯一的算法是
    /// 按行认领的名字与包内条目名<b>取交集</b>。</para>
    /// </summary>
    private static async Task<(int Written, int Skipped, int Failed, int PackageEntries, int RowsWithoutPicture, bool Opened)>
        ExtractClipImagesAsync(List<Item> items, string sourcePath, CancellationToken ct)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<List<string>>();
        foreach (var item in items)
        {
            var names = new List<string>();
            foreach (var name in ClipboardImageStore.ClaimedNamesOf(item))
                if (!string.IsNullOrEmpty(name)) names.Add(name!);
            if (names.Count == 0) continue;
            rows.Add(names);
            foreach (var name in names) claimed.Add(name);
        }

        var (written, skipped, failed, packageNames, opened) =
            await BackupContainer.ExtractClipsAsync(sourcePath, claimed, ct);
        if (packageNames.Count == 0) return (0, skipped, failed, 0, rows.Count, opened);

        var inPackage = new HashSet<string>(packageNames, StringComparer.OrdinalIgnoreCase);
        var covered = rows.Count(names => names.Any(inPackage.Contains));
        return (written, skipped, failed, packageNames.Count, rows.Count - covered, opened);
    }

    /// <summary>
    /// "这一份包到底把图片带回来了没有"那句话。<b>四个数分四档说，逗号跟着实际出现的那几档走</b>——
    /// 用嵌套三元拼出来的句子在"没跳过也没失败，但有行没带着文件"时会写出"写回 2 个，；还有…"这种话。
    /// </summary>
    private static string ClipPackageSentence(
        (int Written, int Skipped, int Failed, int PackageEntries, int RowsWithoutPicture, bool Opened) clip)
    {
        var sb = new StringBuilder();
        sb.Append($" 剪贴板图片：这份包里带着 {clip.PackageEntries} 个图片文件，写回 {clip.Written} 个");
        if (clip.Skipped > 0)
            sb.Append($"，跳过 {clip.Skipped} 个（本机已有同名文件，或那个名字不属于任何一条历史）");
        if (clip.Failed > 0)
            sb.Append($"，另有 {clip.Failed} 个没能写入（原因见日志）");
        if (clip.RowsWithoutPicture > 0)
            sb.Append($"；还有 {clip.RowsWithoutPicture} 条图片历史在这份包里就没有对应的文件，会标成“文件缺失”");
        return sb.Append("。").ToString();
    }

    /// <summary>
    /// 动数据前的语义校验（校验和只保证字节完整，不保证载荷可用）。缺业务键的条目直接拒绝导入并原样
    /// 返回错误；null 元素与空白键行剔除；可空字符串/集合归一到安全值——把「Replace 清库后才崩、
    /// 留下半截空库」的载荷挡在清库之前。合法备份恒满足，行为不变。返回 null 表示通过。
    /// </summary>
    private static string? ValidatePayload(BackupPayload? p)
    {
        // STJ 会把 JSON 显式 null 覆盖到集合的 `= new()` 初始值之上，故 payload 或任一必需集合
        // 都可能为 null——直接 RemoveAll 会 NRE 且发生在 try 之外、以异常形式逃出本应"返回失败"的方法。
        // null（字段缺失/被置 null）语义上≠空备份 `[]`，属畸形载荷，一律在清库之前拒绝。
        if (p is null)
            return "备份载荷缺失（payload 为 null），已拒绝导入，未改动现有数据。";
        if (p.Items is null || p.UserState is null || p.Tags is null || p.ItemTags is null)
            return "备份载荷缺少必需集合（items/user_state/tags/item_tags 存在 null），已拒绝导入，未改动现有数据。";

        p.Items.RemoveAll(it => it is null);
        p.UserState.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Source) || string.IsNullOrWhiteSpace(s.SourceId));
        p.Tags.RemoveAll(t => t is null || string.IsNullOrWhiteSpace(t.Name));
        p.ItemTags.RemoveAll(l => l is null || string.IsNullOrWhiteSpace(l.Source) || string.IsNullOrWhiteSpace(l.SourceId) || string.IsNullOrWhiteSpace(l.TagName));

        foreach (var it in p.Items)
        {
            if (string.IsNullOrWhiteSpace(it.Source) || string.IsNullOrWhiteSpace(it.SourceId))
                return "备份条目缺 source/source_id（业务键），已拒绝导入，未改动现有数据。";
            it.Title ??= string.Empty;
            it.Subtitle ??= string.Empty;
            it.Uri ??= string.Empty;
            it.SearchText ??= string.Empty;
            it.Tags ??= new List<string>();
        }
        return null;
    }

    private static string? ReadWidgetsJson()
    {
        try
        {
            var path = WidgetStorage.DefaultPath();
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"读取 widgets.json 失败（备份将不含组件数据）: {ex.Message}");
            return null;
        }
    }

    /// <summary>把备份里的组件数据写回磁盘。返回成败与失败原因（原因要一路带到状态栏，见调用点）。</summary>
    private static (bool Ok, string? Error) WriteWidgetsJson(string json, string? targetPath)
    {
        try
        {
            var path = targetPath ?? WidgetStorage.DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 先备份现有文件，再覆盖
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            // 原子落盘：先写 .tmp 再 File.Move(overwrite) 覆盖，与 WidgetStorage.Save / SettingsStore.Save /
            // GitHubOptions.Save 同口径——否则写一半崩溃/磁盘满会把线上 widgets.json 截断成非法 JSON，
            // 而 .bak 无任何代码自动回滚，用户下次启动即整块组件全丢（R10-1 会把截断判为「内容损坏」留 .bak 但返回空）。
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            StarLog.Error("恢复 widgets.json 失败", ex);
            return (false, ex.Message);
        }
    }
}

/// <summary>备份文件摘要（供 UI 确认对话框展示）。</summary>
public sealed record BackupSummary(
    long ExportedAt,
    int ItemCount,
    int UserStateCount,
    int TagCount,
    bool HasWidgets);
