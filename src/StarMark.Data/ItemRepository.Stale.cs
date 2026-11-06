#nullable enable
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;

namespace StarMark.Data;

/// <summary>
/// 失联行对账（片215·P-5B）。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>（partial）
/// ——按 SQLite 访问面分文件，不是一个新抽象层。
/// <para>
/// 「失联」＝<b>来源清单里已经没有它、但行还留着</b>：书签在浏览器里被删了、Star 被取消了，
/// 下一轮全量同步会发现旧行没人认领。处置拍板是账本 (B)「标灰＋一键清」——行⛔ 直接删：
/// 它身上还挂着用户的标签与笔记，而「源里没了」与「该删」不是同一件事（口径同批次 VQ 的本机文件行）。
/// </para>
/// <para>
/// <b>stale 位只有两个写入者</b>：本文件的 <see cref="ReconcileStaleAsync"/>（同步对账，置位与清位）
/// 与 <see cref="DeleteStaleAsync"/>（一键清理，连位带行删掉）。<c>UpsertOne</c> 不写这列——
/// 书签回归那一轮靠对账的 CASE 清位，同一轮同步内闭环。
/// </para>
/// </summary>
public sealed partial class ItemRepository
{
    /// <summary>
    /// 按源侧现值清单对账一轮：<paramref name="presentSourceIds"/> 里没有的行置 stale=1、
    /// 重新出现的行清回 0。返回<b>这次新标</b>失联的行数（清位不计数——回归是好消息，不用报）。
    /// <para>
    /// 作用域写进 SQL（<c>source</c>＋<c>type</c> 两样一起限定，口径同
    /// <see cref="DeleteFileEntriesAsync"/> 的自卫先例）：只动这一源这一类的行，
    /// 别的源、别的类（含 source='local' 的 RSS 收藏书签）碰不到。
    /// </para>
    /// <para>
    /// <paramref name="presentSourceIds"/> 为空＝源里真的一个都没有 ⇒ 该源全部行标失联。
    /// 调用方（<c>SyncCoordinator</c>）对「零条载荷」有自己的保守闸（截断源不对账），这里只管口径。
    /// </para>
    /// <para>有变化才 <see cref="DataChangeHub.Notify(DataChangeKind.Items)"/>：界面上那张
    /// 「失联 N 条」的计数卡与列表的标灰都靠它刷新，没变化就不打扰。</para>
    /// </summary>
    public async Task<int> ReconcileStaleAsync(string source, ItemType type,
        IReadOnlyList<string> presentSourceIds, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source)) return 0;
        // json_each 吃 JSON 数组文本（Microsoft.Data.Sqlite 的 SQLite 带 JSON1，SearchAsync 的
        // json_extract 同族）。清单去重不在这做——IN 语义对重复值天然幂等，多这一趟是白付。
        var keysJson = System.Text.Json.JsonSerializer.Serialize(presentSourceIds ?? Array.Empty<string>());
        var typeStr = type.ToString().ToLowerInvariant();

        using var conn = _factory.Open();
        using var tx = conn.BeginImmediate();

        // 先算后写：两条读数（这次要新标的 / 这次要清回的）决定两件事——要不要写、要不要广播。
        // SUM 双计数一条 SELECT 拿全，省掉「写完再读一遍影响行数」的第二趟。
        int toMark, toClear;
        using (var probe = conn.CreateCommand())
        {
            probe.CommandText = @"
                SELECT
                    COALESCE(SUM(CASE WHEN stale = 0 AND source_id NOT IN (SELECT value FROM json_each(@keys)) THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN stale = 1 AND source_id IN (SELECT value FROM json_each(@keys)) THEN 1 ELSE 0 END), 0)
                FROM items
                WHERE source = @source AND type = @type;";
            probe.Parameters.AddWithValue("@keys", keysJson);
            probe.Parameters.AddWithValue("@source", source);
            probe.Parameters.AddWithValue("@type", typeStr);
            await using var reader = await probe.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            toMark = Convert.ToInt32(reader.GetInt64(0));
            toClear = Convert.ToInt32(reader.GetInt64(1));
        }

        if (toMark == 0 && toClear == 0)
        {
            await tx.CommitAsync(ct);
            return 0;   // 一行都不用动：不写、不广播，幂等重放无痕
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE items
                SET stale = CASE WHEN source_id IN (SELECT value FROM json_each(@keys)) THEN 0 ELSE 1 END
                WHERE source = @source AND type = @type;";
            cmd.Parameters.AddWithValue("@keys", keysJson);
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@type", typeStr);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        DataChangeHub.Notify(DataChangeKind.Items);   // 标灰的卡片与设置页的计数都跟着这一声换
        return toMark;
    }

    /// <summary>数一遍「失联的书签行」（设置页那张卡的读数）。</summary>
    /// <remarks>作用域限定 <c>type='bookmark'</c>：这一刀只对书签族拍板（P-5）。local 的
    /// RSS 收藏书签永远不被对账置位，故进不了这个数——限 type 是自卫，限死来源清单反而在
    /// 下一个可对账源进族时还得回来改这一句。</remarks>
    public async Task<int> CountStaleAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM items WHERE stale = 1 AND type = 'bookmark';";
        var obj = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(obj);
    }

    /// <summary>
    /// 一键清掉「失联的书签行」，返回真正删掉的行数。行删了，stale 位跟着行一起消失。
    /// <para>作用域同 <see cref="CountStaleAsync"/>（stale=1 且 type='bookmark'）——只删对账标过的
    /// 那一族，用户没失联的书签、Star、待办一根毫毛都不碰。</para>
    /// </summary>
    public async Task<int> DeleteStaleAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var tx = conn.BeginImmediate();
        int removed;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM items WHERE stale = 1 AND type = 'bookmark';";
            removed = await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (removed > 0)
            DataChangeHub.Notify(DataChangeKind.Items);   // 列表里那些灰卡这一声之后就该消失
        return removed;
    }
}
