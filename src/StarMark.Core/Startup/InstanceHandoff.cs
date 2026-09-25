#nullable enable
namespace StarMark.Core.Startup;

/// <summary>
/// 除自己以外、"看起来是本应用"的另一个进程。UI 侧只负责取证（进程名、有没有窗口、活了多久），
/// 判定全部留在这里——这样"该不该结束别人的进程"这条规则是可单测的，而不是一堆 Win32 调用。
/// </summary>
/// <param name="Pid">进程 id。</param>
/// <param name="NameMatches">进程名是否与本应用相同。任意程序都能把窗口标题设成 "StarMark"，
/// 标题像不像不算证据，进程名才是这里认"自己人"的依据。</param>
/// <param name="HasTopLevelWindow">它是否还有顶层窗口。<b>隐藏不等于没有</b>：最小化到托盘时主窗口仍在
/// （只是不可见），一个都没有才是"界面已经拆完、进程却没被收掉"的残留。</param>
/// <param name="PastGrace">它是否已活得足够久，久到"窗口可能还在建"这一解释不再成立。
/// 启动到首帧约 0.5 秒，宽限期外还没有窗口就不是"正在启动"。</param>
public readonly record struct PeerInstance(int Pid, bool NameMatches, bool HasTopLevelWindow, bool PastGrace);

/// <summary>二次启动时对本进程的安排。</summary>
public enum HandoffAction
{
    /// <summary>唤起那个实例的窗口，本进程退出——这是单实例的正常行为。</summary>
    ActivatePeer,

    /// <summary>结束那台没有任何窗口的残留，把互斥体拿回来后照常启动。</summary>
    RecoverPeer,

    /// <summary>谁都不动，本进程退出：证据不足以安全地结束一个进程。</summary>
    GiveUp,
}

/// <summary>
/// 「再开一个 StarMark 时，对面那个进程是活实例还是残留」的判定。
/// <para>
/// 为什么要有它：进程残留时二次启动会"唤起不到任何窗口 → 自我退出"，用户看到的就是
/// 双击图标没反应（热键、托盘图标全都不在，而程序自称在跑）。要自愈就得结束另一个进程，
/// 而结束别人的进程是这台机器上最不该写错的一件事，所以判据必须能与 Win32 取证分开、可逐条钉死：
/// </para>
/// <list type="number">
/// <item>只要有任何一台同名实例还留着顶层窗口，它就是活实例——唤起它，绝不结束它。</item>
/// <item>没有窗口、又过了宽限期、而且<b>只有一台</b>时，才认定是退出没收干净的残留并回收。</item>
/// <item>其余一律放弃（包括"同名实例刚起来不到宽限期""同时有两台没窗口的同名进程"）：
/// 猜错一次的代价是用户的另一个窗口凭空消失，比一次"双击没反应"严重得多。</item>
/// </list>
/// </summary>
public static class InstanceHandoff
{
    /// <summary>判定 + 结论对应的进程 id（<see cref="HandoffAction.GiveUp"/> 时为 0）+ 可直接写进日志的原因。</summary>
    public static (HandoffAction Action, int Pid, string Reason) Decide(IReadOnlyList<PeerInstance> peers)
    {
        var ours = peers.Where(p => p.NameMatches).ToList();

        var alive = ours.FirstOrDefault(p => p.HasTopLevelWindow);
        if (alive.Pid != 0)
            return (HandoffAction.ActivatePeer, alive.Pid, $"pid={alive.Pid} 还留着窗口，是活实例");

        var stale = ours.Where(p => !p.HasTopLevelWindow && p.PastGrace).ToList();
        if (stale.Count == 1)
            return (HandoffAction.RecoverPeer, stale[0].Pid,
                $"pid={stale[0].Pid} 过了宽限期还一个窗口都没有，判定为退出没收干净的残留");

        var reason = ours.Count == 0
            ? "没有同名实例，互斥体被别的程序持有"
            : stale.Count == 0
                ? "同名实例起来不到宽限期，窗口可能还在建"
                : $"有 {stale.Count} 台没有窗口的同名进程，判定不了该结束哪一台";
        return (HandoffAction.GiveUp, 0, reason);
    }
}
