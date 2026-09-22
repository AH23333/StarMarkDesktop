#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 工具栏「排序」→ Everything 原生排序（<c>Everything_SetSort</c>）的映射。
/// 取值逐字取自官方 SDK 头文件 <c>include/Everything.h</c> 的 <c>EVERYTHING_SORT_*</c>
/// （1＝NAME_ASCENDING … 14＝DATE_MODIFIED_DESCENDING），不凭记忆猜数字。
/// 返回 null 表示不下发 SetSort，交回 Everything 自己的默认排序（相关度）。
/// </summary>
public static class EverythingSort
{
    public const uint NameAscending = 1;
    public const uint DateModifiedDescending = 14;

    /// <summary>
    /// 「最近」对文件语义就是「最近改动」；「名称」按文件名升序。
    /// stars / starred / collected 是 Star 与收藏时间维度，文件没有对应字段——
    /// 与其用错序（例如把「最近收藏」当成创建时间倒排），不如保持默认序。
    /// </summary>
    public static uint? Map(string? uiSort) => uiSort switch
    {
        "recent" => DateModifiedDescending,
        "name" => NameAscending,
        _ => null,
    };
}
