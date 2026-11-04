using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarMark.Core.Boards;

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
    /// 起一扇画板窗——<b>起窗那几处的唯一主人</b>（托盘那两颗「续上一块／新起一块」／片146 从收藏系统
    /// 点开一块板那一路）。
    /// <para><paramref name="sceneId"/> 交 null＝新起一块。两档挤在同一颗方法里是<b>事实</b>而⛔ 巧合：
    /// 「续上一块」读盘上最近动过的那颗档，读不到就是 null，用户手上的表现本来同一副（一块空板）。</para>
    /// <para><b>同一颗板只开一扇</b>（片205）：名册里已有那颗 id 的窗就把它带到眼前——再起一扇＝
    /// 两颗引擎各自冷启（真机量到的卡顿）＋两扇各往同一颗档里写。新起那一档（<paramref name="sceneId"/>
    /// 交 null）⛔ 参加去重：「新起一块」每一拍都该是一块新板。</para>
    /// <para>这一颗⛔ 兜异常、也⛔ 配一句上屏话：坏消息由各自入口按各自的出口说（托盘那张通知卡／
    /// 卡片那条"打不开这一行"的回报）。把句子收进这里＝名册档成了第二主人。</para>
    /// </summary>
    public static async Task OpenSceneAsync(string? sceneId)
    {
        if (sceneId is { Length: > 0 } id)
        {
            BoardWindow? already;
            lock (_open) already = _open.FirstOrDefault(w => w.BoardId == id);
            if (already is not null)
            {
                StarMark.Abstractions.StarLog.Info("boards.scene.dedupe id=" + id);
                already.Activate();
                return;
            }
        }
        var win = new BoardWindow();
        if (sceneId is null) await win.OpenNewAsync();
        else await win.OpenSceneAsync(sceneId);
        win.Activate();
    }

    /// <summary>
    /// 把一张图放上<b>某一块板</b>——深析 §四 那两格（截图冻帧／剪贴板历史图）共用的唯一入口。
    /// <para><b>挑哪一块</b>：名册里有桥已经通着的那扇就用它（图落在用户眼前那块板上）；一块也没有就
    /// <b>新起一块</b>——那正是这两格的用法（"把这张图拿去标"）。⛔ 回一句"请先自己开一块画板"：
    /// 把程序该做的那一步支使给用户，本仓一贯算缺陷。</para>
    /// <para>新起那一扇要等页面诞生，等的是 <see cref="BoardPolicy.ExportAnswerBudgetMs"/> 那颗预算——
    /// 问的是同一件事（桥那侧多久该回话），⛔ 在这里另配一颗新数。<b>逐拍再问</b>而⛔ 一次快照定终身：
    /// 那扇窗是它自己构造时登记进名册的，起窗与登记之间隔着一次异步。</para>
    /// <para>交回的是<b>那一问的名字</b>（<see cref="BridgeProtocol.Insert"/>）：⛔ 到点没通／形状不对／装不下
    /// 三副脸在屏幕上要说的话不同，折成一颗 bool 就是把"这张太大"与"画板压根没起来"念成同一句。
    /// 那句坏话由<b>调用方</b>按各自的出口说，这一颗只报事实（与 <see cref="OpenSceneAsync"/> 同一副规矩）。</para>
    /// </summary>
    public static async Task<BridgeProtocol.Insert> SendImageAsync(
        byte[] imageBytes, string mimeType, int width, int height)
    {
        BoardWindow? PickReady()
        {
            lock (_open)
            {
                for (var i = _open.Count - 1; i >= 0; i--)
                    if (_open[i].BridgeReady) return _open[i];
            }
            return null;
        }

        if (PickReady() is { } existing) return existing.TryInsertImage(imageBytes, mimeType, width, height);

        await OpenSceneAsync(null);
        var deadline = System.Environment.TickCount64 + BoardPolicy.ExportAnswerBudgetMs;
        while (System.Environment.TickCount64 < deadline)
        {
            await Task.Delay(100);
            if (PickReady() is { } fresh) return fresh.TryInsertImage(imageBytes, mimeType, width, height);
        }
        StarMark.Abstractions.StarLog.Warn(
            "boards.insert.no-window ms=" + BoardPolicy.ExportAnswerBudgetMs);
        return BridgeProtocol.Insert.NoBridge;
    }

    /// <summary>
    /// 换主题那一刻把<b>每一扇</b>开着的画板窗推一次（片147＝矩阵「设置同步」那一格）。
    /// <para>为什么必须由主窗那一拍叫进来：画板是独立窗，<b>⛔ 继承主窗主题</b>——
    /// 与 <c>CenteredDialog.ApplyThemeToOpenDialogs</c> 完全同一副理由、同一副规矩（名册在这儿，
    /// 所以退出那一拍问得到，主题那一拍也叫得到）。</para>
    /// <para>逐扇⛔ 抛：一扇桥没通⛔ 配带走其余几扇的重贴（那颗 <c>ReapplyAppearance</c> 自己会先看闸门）。</para>
    /// </summary>
    public static void ReapplyAppearance()
    {
        BoardWindow[] windows;
        lock (_open) windows = _open.ToArray();
        foreach (var w in windows)
        {
            try { w.ReapplyAppearance(); }
            catch (Exception ex) { StarMark.Abstractions.StarLog.Warn("boards.appearance.failed " + ex.GetType().Name); }
        }
    }

    /// <summary>
    /// 开关 OFF 那一拍（片204＝P-286 处置①）：把<b>每一扇</b>开着的画板窗收一遍。
    /// <para>⚠ 片206＝P-286 修法：从前这里调 <see cref="BoardWindow.Close"/>，赌的是"代码关窗
    /// 也走 AppWindow.Closing 那套既有协商"——片205 真机钉死那一条<b>⛔ 成立</b>（WinUI 里代码那一记
    /// Close ⛔ 响 Closing），板子连问都没被问就没了＝静默关窗那一案的根因。现在每一扇都走
    /// <see cref="BoardWindow.CloseThroughNegotiation"/>：桥活着就与按×同一副「藏→问→真关」，
    /// 被拒那一支自己把窗亮回并贴上「另存／继续编辑」那块面板——"收前问一句要不要存"这一问
    /// 由每扇窗自己的关窗路问，这里⛔ 另起一份"直接拆"的捷径（拆＝把没落盘的那几笔连问都不问就丢掉，
    /// 正是这一格要堵的坏）。用户在那一支选「继续编辑」＝他的决定：那扇窗留在名册里照旧活着。</para>
    /// <para>快照后逐扇收（收的那一拍里名册会被 <c>OnClosed</c> 动——成行的窗自己摘自己，
    /// ⛔ 边收边遍历活名册）；逐扇兜异常（与 <see cref="ReapplyAppearance"/> 同一副规矩）：
    /// 一扇收掉⛔ 配带走其余几扇。</para>
    /// </summary>
    public static void CloseAll()
    {
        BoardWindow[] windows;
        lock (_open) windows = _open.ToArray();
        foreach (var w in windows)
        {
            try { w.CloseThroughNegotiation(); }
            catch (System.Exception ex)
            {
                StarMark.Abstractions.StarLog.Warn("boards.close-all.failed " + ex.GetType().Name);
            }
        }
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
