using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StarMark.UI.Boards;

/// <summary>
/// 开着的那几扇画板窗的<b>唯一名册</b>（P-220 那位"主人"）。
/// <para><b>为什么要有这一颗</b>：退出那一途的最后一步是 <c>Environment.Exit(0)</c>（批次 PL 为了
/// "托盘图标没了而进程还锁着 dll" 加的），它⛔ 经过任何一扇窗的 <c>AppWindow.Closing</c>——
/// 片30 真机两臂量到：同一份码、同一起点，关画板窗那一臂盘上多出那份档（<c>…-be4b.excalidraw</c> 241 字节），
/// 而从主窗退出那一臂<b>那颗板压根没落盘</b>（<c>…-26bf</c> 只有 <c>bridge.ready</c> 与 <c>arm.post</c> 三行，
/// 收尾日志之后进程 1 秒内退了，档名册一颗没多）。名册住在这里，退出那一拍才有东西可以问。</para>
/// <para><b>登记的对子住在 <see cref="BoardWindow"/> 自己身上</b>：构造里 <see cref="Add"/>、
/// <c>OnClosed</c> 里 <see cref="Remove"/>——与那颗 <c>Locale.Changed</c> 的订阅同一副规矩（手臂成对，⛔ 静态事件上留尸体）。
/// 起窗那几处（托盘两颗入口／开发期那颗环境变量）⛔ 各自再登记一遍＝第二个主人。</para>
/// </summary>
public static class BoardsWindows
{
    private static readonly List<BoardWindow> _open = new();

    public static void Add(BoardWindow window)
    {
        lock (_open) _open.Add(window);
    }

    public static void Remove(BoardWindow window)
    {
        lock (_open) _open.Remove(window);
    }

    /// <summary>此刻开着几扇（只给证人与读数用；这颗数⛔ 参与任何决定）。</summary>
    public static int OpenCount
    {
        get { lock (_open) return _open.Count; }
    }

    /// <summary>
    /// 退出那一拍：请每一扇画板窗把"关窗前那一份"交回来，<b>并发</b>等它们各自的预算用完。
    /// <para>并发而⛔ 逐扇串起来＝最坏那一扇决定总时长：每颗的额度都是 <see cref="BoardPolicy.CloseFlushBudgetMs"/>，
    /// N 扇也还是那 2 秒，而收尾链上挂着一颗<b>5 秒的硬退出看门狗</b>（<c>MainWindow.ExitApp</c>）——
    /// 串起来三扇就撞过它了，被砍掉的那几扇比这条腿上原本的症状更坏。</para>
    /// <para>返回＝<b>落地了几扇</b>（写成功、盘上本来就是它，都算落地；桥没通、到点、抛了⛔ 算）。
    /// 这一趟⛔ 往屏幕上贴任何话：整条链正在往下走，那张卡既⛔ 来得及被看见，也可能把退出拖住——
    /// 所以它只往日志里说话（读数在 <c>boards.close.flush.exit</c>）。</para>
    /// </summary>
    public static async Task<int> FlushAllForExitAsync()
    {
        BoardWindow[] windows;
        lock (_open) windows = _open.ToArray();
        if (windows.Length == 0) return 0;
        var waits = windows.Select(w => w.FlushForExitAsync()).ToArray();   // 每颗自己兜异常，⛔ 一颗抛了带走整批
        var landed = await Task.WhenAll(waits);
        lock (_open) _open.Clear();
        return landed.Count(x => x);
    }
}
