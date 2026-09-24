#nullable enable
using System;
using System.IO;

namespace StarMark.Abstractions;

/// <summary>
/// "这一份程序是什么时候构建的"。
/// <para>为什么要有它：真机反馈的循环里出现过一整轮误判——修好的代码在树里，而跑的人启动的是
/// 之前那份产物（VS 在源码没变时按 F5 不会重编，所以产物时间戳也看不出来）。那时"问题依旧"
/// 会被当成"修复无效"，于是往判据上又叠一层猜测。这件事不该靠人记，程序自己说一次就结束。</para>
/// <para>取的是<b>正在运行那个可执行文件的写入时间</b>：它就是这份代码被编出来的时刻，
/// 不需要构建脚本配合（加不了 git 哈希就不加——时间戳已经足够回答"是不是新构建"这个问题）。</para>
/// </summary>
public static class BuildInfo
{
    /// <summary>构建时刻（本地时区）；取不到时为 null（宿主不提供可执行路径等）。</summary>
    public static DateTime? LocalTime { get; } = Read();

    /// <summary>给人看的那一句：<c>2026-09-25 00:31</c>；取不到时说实话，不假装是最新。</summary>
    public static string Display => LocalTime is { } time
        ? time.ToString("yyyy-MM-dd HH:mm")
        : "未知（读不到可执行文件的写入时间）";

    private static DateTime? Read()
    {
        try
        {
            var path = Environment.ProcessPath;
            // 目录进程（宿主）与单文件发布的临时解包路径都可能有，但写入时间仍是"这份产物何时生成"，够用。
            return string.IsNullOrEmpty(path) || !File.Exists(path)
                ? null
                : new FileInfo(path).LastWriteTime;
        }
        catch (Exception)
        {
            return null;      // 诊断信息本身不许把启动带崩：取不到就显示"未知"
        }
    }
}
