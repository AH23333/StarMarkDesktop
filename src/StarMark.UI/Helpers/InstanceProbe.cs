#nullable enable
using System.Diagnostics;
using StarMark.Abstractions;
using StarMark.Core.Startup;

namespace StarMark.UI.Helpers;

/// <summary>
/// 「除了自己还有没有别的 StarMark 在跑、它是不是只剩一具没窗口的壳」的现场取证与执行。
/// <para>
/// 判定规则不在这里（见 <see cref="InstanceHandoff"/>），这里只做三件事：列进程、数窗口、
/// 以及在规则说"可以结束"时结束它并把原因照实带回去。结束别人的进程这件事必须由判定说话，
/// 取证代码不许自己下结论。
/// </para>
/// </summary>
internal static class InstanceProbe
{
    /// <summary>
    /// 宽限期（秒）：一台同名实例起来不到这么久就没有窗口，我们按"它还在启动"处理而不动它。
    /// 真机度量里首帧约 0.4 秒、组件恢复完约 1.9 秒，取 10 秒留足低端机的余量。
    /// </summary>
    public const int GraceSeconds = 10;

    /// <summary>
    /// 本应用自己的进程名（不含扩展名）。apphost 是 <c>StarMark.UI.exe</c>，所以这个名字带 <c>.UI</c>。
    /// <para>这里曾经硬写过 <c>"StarMark"</c>：属主校验于是永远判成"不是我们的窗口"，二次启动既不唤起
    /// 已有实例也不报错，只是自我退出——用户看到的全部现象就是"双击图标没反应"。名字取自身所在 exe，
    /// 从此不再需要跟着产品名/程序集名手动改。</para>
    /// </summary>
    public static string OurProcessName { get; } =
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) is { Length: > 0 } n ? n : "StarMark.UI";

    /// <summary>除自己以外、与本应用同名的存活进程，各自是否还有顶层窗口、是否已过宽限期。</summary>
    public static IReadOnlyList<PeerInstance> ListPeers()
    {
        var withWindow = TopLevelWindowOwners();
        var now = DateTime.Now;
        var list = new List<PeerInstance>();
        foreach (var p in Process.GetProcessesByName(OurProcessName))
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                // StartTime 读不到（对面是更高权限的进程）就当"没过宽限期"：宁可一次不回收，
                // 也不能凭猜测结束一个还在正常显示窗口的实例。
                var pastGrace = TryGetStartTime(p, out var started) && (now - started).TotalSeconds >= GraceSeconds;
                list.Add(new PeerInstance(p.Id, true, withWindow.Contains(p.Id), pastGrace));
            }
            catch (Exception ex)
            {
                StarLog.Warn($"枚举同名实例时跳过 pid（{ex.Message}）");
            }
            finally
            {
                p.Dispose();
            }
        }
        return list;
    }

    /// <summary>
    /// 结束一台进程。返回 <b>Problem＝null 才算真结束</b>；PermissionDenied 单独回出来，
    /// 因为"权限不够"是用户能解决的那一类（提权一次），而"它已经没了"不需要任何人动手。
    /// </summary>
    public static (bool Ok, bool PermissionDenied, string Problem) TryStop(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            // 不等它真死就返回，下一步抢互斥体会看到"进程还持有句柄"而失败，于是白放弃一次自愈。
            if (!p.WaitForExit(3000))
                return (false, false, $"pid={pid} 收到结束请求但 3 秒内没退出");
            return (true, false, string.Empty);
        }
        catch (ArgumentException)
        {
            return (true, false, string.Empty);   // 已经不在＝目的达成，不是失败
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return (false, true, $"pid={pid} 以更高权限运行，本实例（{(Privilege.IsElevated() ? "已" : "未")}提权）结束不了它");
        }
        catch (Exception ex)
        {
            return (false, false, $"结束 pid={pid} 失败：{ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// 所有"顶层窗口"的属主进程 id。<see cref="WindowInterop.EnumWindows"/> 不含消息专用窗口
    /// （托盘宿主、剪贴板监听窗都是消息专用），所以"正常最小化到托盘"的实例仍会出现在这里——
    /// 隐藏的主窗口照样是顶层窗口。这正是我们要的区分：<b>窗口看不见 ≠ 窗口不存在</b>。
    /// </summary>
    private static HashSet<int> TopLevelWindowOwners()
    {
        var owners = new HashSet<int>();
        try
        {
            WindowInterop.EnumWindows((hwnd, _) =>
            {
                WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != 0) owners.Add(unchecked((int)pid));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            // 数不出窗口就退化成"谁都别结束"：判定拿不到 HasTopLevelWindow 证据时会一律 GiveUp。
            StarLog.Error("枚举顶层窗口失败（放弃残留判定）", ex);
        }
        return owners;
    }

    private static bool TryGetStartTime(Process p, out DateTime started)
    {
        try
        {
            started = p.StartTime;
            return true;
        }
        catch
        {
            started = default;
            return false;
        }
    }
}
