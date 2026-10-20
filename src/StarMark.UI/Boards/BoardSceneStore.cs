#nullable enable
using System;
using System.IO;
using StarMark.Abstractions;
using StarMark.Core.Boards;

namespace StarMark.UI.Boards;

/// <summary>
/// 一颗板的那份档（Phase C 片21＝落地设计提案 v1 §21.3 存储适配器那一半的落码）。
/// <para>
/// 这里只管<b>三件事</b>：落点、一颗板一颗档的命名怎么落到盘上、写的时候怎么不把上一份弄丢。
/// 命名的<b>主人是 Core 的 <see cref="BoardPolicy"/></b>（档名、后缀、那颗 .bak 的尾名都在它那里），
/// 本类⛔ 拼任何一颗名字字面量；落点这一颗⛔ 是判据（§21.3 明写"路径⛔ 是判据"），所以它住在 UI 侧——
/// 但那颗<b>根</b>照旧问 <see cref="UserDataPaths"/>（片23：落点规则只许一颗主人，沙盒改道才带得动画板档）。
/// </para>
/// <para>
/// <b>写的那条顺序是有原因的</b>：先把现档滚成 <c>.bak</c>（只留最近一份，而且<b>那一发与盘上同件的写⛔ 滚</b>——
/// 滚下去只会把唯一那条「真正不同」的退路顶成同件，判据在 <see cref="BoardPolicy.BackupRollNeeded"/>，片37），再把新字节写进 <c>*.tmp</c>，
/// 最后同卷改名盖过去。进程死在任何一步，盘上留下的都是「上一份完整档 ＋ 至多一颗临时件」，
/// 而⛔ 是「主档被截成半颗」——本仓那条「读档⛔ 截零」的旧规矩在这里的同一条线。
/// </para>
/// <para><b>失败要说话</b>：两颗出口都交回布尔与故障名，⛔ 静默吞（"画板自己把我画的东西弄丢了"
/// 是这一族最坏的一种坏，见 P-53 那条口径）。</para>
/// </summary>
public static class BoardSceneStore
{
    /// <summary>板档目录（<c>boards\</c> 那一颗子目录，根由 <see cref="UserDataPaths"/> 给；
    /// 导出物另住在它下面的 <c>exports\</c>，两族不同主人）。
    /// <para>本档⛔ 自己拼 <c>%APPDATA%\StarMark</c>：那份优先规则（settings 变量 ＞ db 变量 ＞ 真目录）
    /// 只许有一颗主人，抄第二份的那一族不会报错，它只是<b>永远写到真目录去</b>（批次 SS 那颗坑的同一条线）。</para></summary>
    public static string Folder => UserDataPaths.Subdirectory("boards");

    /// <summary>那颗 id 的档在哪（只算名字，⛔ 摸盘）。</summary>
    public static string PathOf(string sceneId)
        => Path.Combine(Folder, BoardPolicy.SceneFileName(sceneId));

    /// <summary>
    /// 写这颗板的档。<b>⛔ 抛</b>：失败回 <c>false</c> ＋故障名（调用方负责上屏与日志）。
    /// <para>那颗 id 先过 <see cref="BoardPolicy.IsSceneId"/>：形状不对就连名字都不拼——
    /// 这条桥的字节来自 web 侧，而"哪颗板"这件事必须由宿主的身份判据说了算（⛔ 拿内容当身份）。</para>
    /// </summary>
    public static bool TrySave(string? sceneId, byte[] bytes, out string? fault, out bool rolledBackup)
    {
        fault = null;
        rolledBackup = false;
        if (sceneId is null || !BoardPolicy.IsSceneId(sceneId))
        {
            fault = "bad-id";
            return false;
        }
        try
        {
            System.IO.Directory.CreateDirectory(Folder);
            var main = PathOf(sceneId);
            // 滚之前先问<b>盘上那份</b>（判据住 Core，片37／片38）：宿主内存里那份「我上次写了什么」会落后于盘
            // （两扇同开同一颗板／外部同步工具改了档／上一发写失败之后重试），而一发与盘上同件的写滚下去
            // 只会把唯一那条「真正不同」的退路顶成同件——退路当场少一条，而且少的是唯一那条。
            // 片38 补的是同一条线上的另一半：盘上那份<b>读得出却⛔ 是一颗档</b>（0 字节／截在半路）时它与来件当然不同，
            // 照滚＝把还能打开的那颗 .bak 顶成垃圾——那颗「该不该留」的形状判据也住在 Core（LooksLikeSceneDoc），
            // 存储这一档⛔ 自己再判一遍「长不长得像一颗档」（那才是第二个主人）。
            if (BoardPolicy.BackupRollNeeded(ReadIfExists(main), bytes))
            {
                System.IO.File.Copy(main, BackupPathOf(sceneId), overwrite: true);
                rolledBackup = true;
            }
            var temp = main + TempSuffix;
            System.IO.File.WriteAllBytes(temp, bytes);
            System.IO.File.Move(temp, main, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            fault = ex.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 盘上那份主档（不存在＝null；读不动＝也 null：被人攥着，或那颗 <c>.tmp</c> 正在改名的那一瞬间）。⛔ 抛。
    /// <para>这一颗只服务一个问句——「滚了会不会白丢一条退路」——所以它⛔ 参与载入，也不交回给调用方当事实。</para>
    /// </summary>
    private static byte[]? ReadIfExists(string path)
    {
        try { return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 读这颗板的档：主档还认得出是一颗档就用主档，否则退到 <c>.bak</c>（退路也认不出才说"这颗板没有能打开的一份"）。
    /// <para>这颗退路是有读者的：上一份刚被滚走、新那份还没改名的那一瞬间进程没了，
    /// 用户重开时看到的应该是<b>还能打开的那一份</b>，而⛔ 是"这颗板不存在"。</para>
    /// <para><b>片43 补上的那一半（P-238）：退路的存在⛔ 等于退路读得到。</b>这一头原来只问「主档在⛔ 在」，
    /// 而盘上出事的形状恰恰是「主档在、却是 0 字节／半颗」（同步工具的占位档／磁盘满时那次改名／用户从别处拷一颗空档进来——
    /// 与 ✅P-234 登记的是同一批门路）。那种形状下这一头返回"读到了"，调用方把空串当成"这颗板不存在"而<b>另起一颗新板</b>，
    /// 紧挨着它那颗还能打开的 <c>.bak</c> 一次也没被问过（真机逐字＝<c>bin/WI91/readings_38b_174633.txt</c>：
    /// <c>via=no-scene</c>，我摆那颗档名⛔ 出现在任何一发里）。⇒ 滚那一头的「该不该留」由片38 收干净了，
    /// 载入这一头的「该拿哪一份」由本批收，整颗交回 Core 的 <see cref="BoardPolicy.PickReadableScene"/>：
    /// 本档只负责把盘上那两颗读给它看，⛔ 自己再判一遍形状／空（那才是第二个主人）。</para>
    /// <para><paramref name="fromBackup"/> 交回的是"读到的是<em>哪一颗</em>文件"这件事实：退到 <c>.bak</c> 那一刻，
    /// 盘上的<b>主档位置当⛔ 一份档</b>——调用方若把这份字节当成"盘上已经有了"（<see cref="BoardPolicy.DiskAlreadyHolds"/>），
    /// 一颗"打开看看、没动"的板就永远补不回主档，而 <see cref="LatestSceneId"/> 只认主档。
    /// 补回去那一发⛔ 会顶掉这条退路：滚那一头认得出那颗垃圾主档⛔ 配滚（✅P-234），两片正好咬合。</para>
    /// <para><paramref name="blocked"/> 交回的是"读不到"分<b>哪一族</b>（片49＝P-239）：<b>主档位置在、却一颗字节也拿不出</b>
    /// （被人攥着／自家那次改名正在半路）才为真。从前两族折成同一颗 <c>false</c>，调用方只能一起处理，
    /// 于是把"现在拿不出来"也当成了"没有这颗档"：屏幕上是一块空画布，而盘上那块板⛔ 动过一个字节，
    /// 用户⛔ 知道是自己的板没了两颗、还是别人的程序正攥着它。判据本身住 <see cref="BoardPolicy.SceneFileIsBlocked"/>，
    /// 本档只把盘上问到的两颗事实（在不在、读到些啥）递给它。</para>
    /// </summary>
    public static bool TryLoad(string? sceneId, out byte[] bytes, out bool fromBackup, out bool blocked)
    {
        bytes = Array.Empty<byte>();
        fromBackup = false;
        blocked = false;
        if (sceneId is null || !BoardPolicy.IsSceneId(sceneId)) return false;
        var main = PathOf(sceneId);
        byte[]? onMain = null;
        if (System.IO.File.Exists(main))
        {
            onMain = ReadIfExists(main);
            blocked = BoardPolicy.SceneFileIsBlocked(true, onMain);
            // 主档在、却一颗字节都读不出（被人攥着／正在改名的那一瞬间）：这一发⛔ 退到 .bak。
            // 那颗退路按定义是上一版，把它当这一版递出去、再让第一拍写回＝用旧的一份盖掉一份可能更新的主档。
            if (blocked) return false;
        }
        // 「该拿哪一份」整颗交 Core；这一头只决定<b>要不要再去读第二颗档</b>（⛔ 每拍都读两颗）。
        // ⚠ 本批第一跑正是这句写歪（只在「主档⛔ 存在」时才去读退路），被真机腿当场打回（坋格=3：宿主照旧另起一颗新板）。
        // 把判据搬进可测那一层，<b>⛔</b> 等于调用那一头自动正确（坑表 #803）。
        var onBackup = onMain is null || BoardPolicy.LooksLikeSceneDoc(onMain)
            ? null
            : ReadIfExists(BackupPathOf(sceneId));
        return BoardPolicy.PickReadableScene(onMain, onBackup, out bytes, out fromBackup);
    }

    /// <summary>
    /// 最近动过的那颗板（托盘那颗入口读它：有档就续上，没档就新起一块）。
    /// <para>只认 <see cref="BoardPolicy.IsSceneFileName"/>（<c>.bak</c> 与临时件⛔ 算一颗板），
    /// 平局时按名字取——那颗序只在"同一秒内改过两颗板"时才会被读到，而它必须可复算（⛔ 枚举顺序当判据）。</para>
    /// </summary>
    public static string? LatestSceneId()
    {
        try
        {
            if (!System.IO.Directory.Exists(Folder)) return null;
            string? best = null;
            var bestAt = DateTime.MinValue;
            foreach (var file in System.IO.Directory.EnumerateFiles(Folder))
            {
                var name = System.IO.Path.GetFileName(file);
                if (!BoardPolicy.IsSceneFileName(name)) continue;
                var at = System.IO.File.GetLastWriteTimeUtc(file);
                var id = System.IO.Path.GetFileNameWithoutExtension(name);
                if (at > bestAt || (at == bestAt && string.CompareOrdinal(id, best) < 0))
                {
                    bestAt = at;
                    best = id;
                }
            }
            return best;
        }
        catch (Exception) { return null; }
    }

    /// <summary>那颗 .tmp 的尾名（对账那一族按它认"半途而废的那一颗"；这里给主人，⛔ 各处各写一份）。</summary>
    public const string TempSuffix = ".tmp";

    private static string BackupPathOf(string sceneId)
        => Path.Combine(Folder, BoardPolicy.BackupFileName(sceneId));
}
