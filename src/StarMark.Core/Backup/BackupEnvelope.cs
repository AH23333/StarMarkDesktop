#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Backup;

/// <summary>备份信封。对应浏览器扩展 <c>backup.ts</c> 的导出结构，补充其缺失的校验和与分层。</summary>
public sealed class BackupEnvelope
{
    /// <summary>
    /// 信封归属标记。<b>与扩展那边的 <c>'starmark'</c> 刻意不统一，别"顺手改名"统一它</b>（P-117）：
    /// 两边的载荷形状本就不兼容（items 层级、时间单位 ms↔s、type 字符串↔整数、扩展那份没有校验和），
    /// 而 P-68 已裁决两库之间不建数据通道。这两个字符串是互斥<b>唯一</b>的闸门——
    /// 桌面读侧 <c>BackupService.ReadAsync</c> 按 <see cref="AppId"/> 逐字拒绝，
    /// 扩展读侧 <c>backup.ts</c> 的 <c>parseBackup</c> 里那句 <c>parsed.app !== 'starmark'</c> 只认 <c>'starmark'</c>；
    /// 名字一旦统一，"读错档"就从报错变成静默改库。<b>对面那仓的行号不写在这里</b>（一次合法编辑就会漂）。
    /// </summary>
    public const string AppId = "starmark-desktop";

    /// <summary>
    /// 桌面自己的版本号。<b>不要为了与扩展同号去动它</b>（同上）：扩展导出写 <c>version: 2</c> 且读时不校验，
    /// 两边同号也不会互读（上面那道 app 名先挡）；而这里的 <see cref="CurrentVersion"/> 同时是读侧的
    /// <b>前向拒绝上限</b>（<c>env.Version &gt; CurrentVersion</c> 直接拒），抬高它＝同时放宽"未来版本的文件现在就能导入"，
    /// 压低它＝把自己以前写过的档变成"版本过高"。两库真正要错开的是<b>语义</b>，不是这个数字。
    /// </summary>
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

    /// <summary>
    /// 恢复回执那句话的<b>身份</b>（键＋实参），⛔ 一句成品字（批次 WI-62／P-193）。
    /// <para>为什么这一颗必须交身份而不是字符串：念它的是设置页「数据」那一行（<c>BackupStatus</c>），
    /// 而那一行是<b>长命宿主</b>上的读数。Core 在被调用那一刻按当时语言拼好，换语言就没人回去重算 ⇒
    /// 屏幕上停的是"上一次恢复那一刻的语言"，而同卡的「本机备份」「自动备份」那两行早已跟着语言走——
    /// 同一张卡片两行两种语言就是这一族缺陷的样子（✅WI-61c 那颗跨层证人点名的正是这一行）。</para>
    /// <para>⛔ 在这里 <c>.Render()</c>：那条出口只留给<b>不上屏幕</b>的消费者（日志、通知文本）。</para>
    /// </summary>
    public LocMessage? Message { get; init; }

    /// <summary>恢复前自动落盘的快照路径；失败时可用于回滚。</summary>
    public string? SnapshotPath { get; init; }

    public int ItemsRestored { get; init; }
    public int UserStatesRestored { get; init; }
    public int TagsRestored { get; init; }
    public int LinksRestored { get; init; }
    public bool WidgetsRestored { get; init; }

    /// <summary>从包里解回 clip 目录的图片文件个数（三个数分开报，见 <see cref="RestoreResult"/> 那三条）。</summary>
    public int ClipImagesRestored { get; init; }

    /// <summary>跳过的个数：本机已有同名文件（不覆盖）与"这个名字不属于任何一条历史"（不认领）都算在这里。</summary>
    public int ClipImagesSkipped { get; init; }

    /// <summary>写失败的个数（磁盘、权限写不进去）。与"跳过"必须分开：一句"已恢复"会把失败说成一件好事。</summary>
    public int ClipImagesFailed { get; init; }
}

/// <summary>
/// 备份文件损坏或不兼容。<b>给人看的那句话交的是身份（<see cref="Reason"/>），不是拼好的字</b>（批次 WI-62／P-193）。
/// <para>为什么⛔ 把句子写进 <see cref="Exception.Message"/>：唯一的消费者是设置页那一行状态（<c>BackupStatus</c>），
/// 而 <c>Message</c> 在抛出那一刻就定稿了——那一行换语言不会重抛，屏幕上就停在旧语言。
/// <c>Message</c> 因此只装<b>键名</b>（日志里可 grep，⛔ 是一句翻不动的字），界面侧一律读 <see cref="Reason"/>。</para>
/// </summary>
public sealed class BackupFormatException : Exception
{
    /// <summary>拒绝导入那句话的「键＋实参」；按当前语言取词由宿主在贴上屏幕那一刻做。</summary>
    public LocMessage Reason { get; }

    public BackupFormatException(LocMessage reason) : base(reason.Key) => Reason = reason;
}
