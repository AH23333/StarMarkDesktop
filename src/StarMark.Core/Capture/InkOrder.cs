#nullable enable
using System.Threading;

namespace StarMark.Core.Capture;

/// <summary>
/// 「全机第几笔」这一份序号的<b>唯一出处</b>（方案 §7：历史栈全局化、按 surface 记归属）。
/// <para>
/// 为什么要有全局序号，而不是"每叠各数各的"：笔迹天生是分家的——画布每块屏一叠、截图那一叠、
/// 每张贴图又一叠，而用户按 <c>Ctrl+Z</c> 时说的是"退掉我<b>最后画</b>的那一笔"，那句里的"最后"
/// 是跨这几叠比的。没有共同时钟就只能"每块屏各退一条"，双屏上的真机症状就是一次撤销掉两笔、
/// 掉的还不是同一时刻画的那两笔。
/// </para>
/// <para>
/// 时钟只在这里走。任何一笔迹类型里再出现第二条自增，就是第二真值：两边的数不再有可比的大小关系，
/// "谁最后落"重新变成各凭猜测（有闸门钉这一条）。
/// </para>
/// </summary>
public static class InkOrder
{
    private static long _clock;

    /// <summary>领下一个序号：单调递增、进程内唯一。落笔那一刻领，之后不再变。</summary>
    public static long Next() => Interlocked.Increment(ref _clock);

    /// <summary>
    /// "这一叠里一笔都没有"。取最小值，这样空叠在任何"谁最新"的比较里都排最后——
    /// 没画过的东西不该被选中当成"最后落的那一笔"来撤销。
    /// </summary>
    public const long None = long.MinValue;
}
