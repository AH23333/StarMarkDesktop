#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;

namespace StarMark.Core.Backup;

/// <summary>备份信封。对应浏览器扩展 <c>backup.ts</c> 的导出结构，补充其缺失的校验和与分层。</summary>
public sealed class BackupEnvelope
{
    public const string AppId = "starmark-desktop";
    public const int CurrentVersion = 1;

    [JsonPropertyName("app")]
    public string App { get; set; } = AppId;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("exportedAt")]
    public long ExportedAt { get; set; }

    /// <summary>
    /// SHA-256（小写十六进制），覆盖 <see cref="Payload"/> 的规范序列化结果。
    /// 用于防截断/损坏：不匹配直接拒绝导入，不等 Upsert 崩到一半。
    /// </summary>
    [JsonPropertyName("checksum")]
    public string Checksum { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public BackupPayload Payload { get; set; } = new();
}

/// <summary>
/// 备份载荷。<b>语义分层</b>：条目本体（可重新同步）与用户元数据（不可重建）分开存，
/// 这样即使只想找回元数据也能单独处理。
/// </summary>
public sealed class BackupPayload
{
    /// <summary>条目本体。可重新同步，但一并备份以保真。</summary>
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = new();

    /// <summary>不可重建的用户元数据：hidden / pinned / notes。</summary>
    [JsonPropertyName("userState")]
    public List<UserStateRecord> UserState { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<TagRecord> Tags { get; set; } = new();

    [JsonPropertyName("itemTags")]
    public List<ItemTagLink> ItemTags { get; set; } = new();

    /// <summary>
    /// <c>widgets.json</c> 原文。里面存着用户手写的待办与随记，
    /// 即使将来格式变更读不懂，原文留着也不会丢。
    /// </summary>
    [JsonPropertyName("widgetsJson")]
    public string? WidgetsJson { get; set; }
}

/// <summary>恢复结果。</summary>
public sealed class RestoreResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>恢复前自动落盘的快照路径；失败时可用于回滚。</summary>
    public string? SnapshotPath { get; init; }

    public int ItemsRestored { get; init; }
    public int UserStatesRestored { get; init; }
    public int TagsRestored { get; init; }
    public int LinksRestored { get; init; }
    public bool WidgetsRestored { get; init; }
}

/// <summary>备份文件损坏或不兼容。</summary>
public sealed class BackupFormatException : Exception
{
    public BackupFormatException(string message) : base(message) { }
}
