#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
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
            return Path.Combine(local, "StarMark", "backups");
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

    public async Task ExportToFileAsync(string path, CancellationToken ct = default)
    {
        var env = await ExportAsync(ct);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(env, JsonOptions), ct);
    }

    // ==================== 读取与校验 ====================

    /// <summary>读取并校验备份文件。校验和不匹配或版本不兼容时抛 <see cref="BackupFormatException"/>。</summary>
    public static async Task<BackupEnvelope> ReadAsync(string path, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        var env = JsonSerializer.Deserialize<BackupEnvelope>(text, JsonOptions)
                  ?? throw new BackupFormatException("备份文件不是合法的 JSON。");

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

    /// <summary>仅看摘要（不校验），供 UI 在确认对话框里展示条数。校验失败返回 null。</summary>
    public static BackupSummary? Peek(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var env = JsonSerializer.Deserialize<BackupEnvelope>(text, JsonOptions);
            if (env is null) return null;
            return new BackupSummary(
                env.ExportedAt,
                env.Payload.Items.Count,
                env.Payload.UserState.Count,
                env.Payload.Tags.Count,
                env.Payload.WidgetsJson is not null);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"读取备份摘要失败: {ex.Message}");
            return null;
        }
    }

    // ==================== 恢复 ====================

    /// <summary>
    /// 恢复。<b>任何模式下都会先落一份当前数据的快照</b>，再动数据库。
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(
        BackupEnvelope env,
        RestoreMode mode = RestoreMode.Merge,
        string? widgetsTargetPath = null,
        CancellationToken ct = default)
    {
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

            bool widgetsRestored = false;
            if (!string.IsNullOrEmpty(p.WidgetsJson))
            {
                widgetsRestored = WriteWidgetsJson(p.WidgetsJson!, widgetsTargetPath);
            }

            return new RestoreResult
            {
                Success = true,
                Message = mode == RestoreMode.Replace
                    ? $"已覆盖恢复 {p.Items.Count} 条条目。"
                    : $"已合并恢复 {p.Items.Count} 条条目。",
                SnapshotPath = snapshotPath,
                ItemsRestored = p.Items.Count,
                UserStatesRestored = p.UserState.Count,
                TagsRestored = p.Tags.Count,
                LinksRestored = p.ItemTags.Count,
                WidgetsRestored = widgetsRestored,
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
        var name = $"pre-restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(SnapshotDirectory, name);
        await ExportToFileAsync(path, ct);
        return path;
    }

    // ==================== 内部 ====================

    /// <summary>SHA-256，覆盖载荷的规范序列化（无缩进、属性顺序固定）。</summary>
    internal static string ComputeChecksum(BackupPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalOptions);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static bool WriteWidgetsJson(string json, string? targetPath)
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
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("恢复 widgets.json 失败", ex);
            return false;
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
