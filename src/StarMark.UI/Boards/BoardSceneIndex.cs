#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Boards;
using StarMark.UI.Services;

namespace StarMark.UI.Boards;

/// <summary>
/// 一块板在收藏系统里的那<b>一行</b>（Phase C 片146＝集成矩阵「场景 → 可检索」那一行落码）。
/// <para>
/// <b>这一行是那颗档的索引件，⛔ 是那颗档本身</b>：正身永远在 <c>boards\&lt;id&gt;.excalidraw</c>，
/// 盘上有什么就补什么，删掉这一行也丢不了任何东西（这一句决定了下面每一个取捨——凡是"拿不准要不要写"的地方，
/// 答案都是"别写，说话，下次照样补得回来"）。
/// </para>
/// <para>
/// <b>为什么这一档住 UI 而⛔  Core</b>：它两头都要碰——一头是 <see cref="BoardSceneStore"/> 那颗落点（UI 侧的
/// 唯一主人），另一头是 <see cref="IItemRepository"/> 那颗库门。判据本身（那颗板讲了哪些字）在 Core 的
/// <see cref="BoardPolicy.SceneTextDigest"/>，这里只搬字节、拼一行、递进去。
/// </para>
/// <para>
/// <b>入库走 <see cref="IItemRepository.RecordItemAsync"/> 那一颗唯一口径</b>：(source, source_id) 幂等、
/// 保住用户的 hidden/pinned/notes/标签、<c>search_text</c> 由仓储按当前 CJK 规则重算。
/// ⛔ 在这里自己拼 <c>SearchText</c>——那是第二主人，而它坏起来的形状是"中文板搜不到"（同仓那颗
/// <c>CjkTokenizer</c> 注释记过的旧坑）。
/// </para>
/// </summary>
public static class BoardSceneIndex
{
    /// <summary>
    /// 那一发写盘成功之后补上索引件（<b>同步入口</b>：调用点本身就在线程池的串行闸里，
    /// 见 <c>BoardWindow.QueueSceneWrite</c>）。
    /// <para><b>⛔ 抛，也⛔ 让调用方判成败</b>：那块板已经保住，这一行没跟上只是"暂时搜不到"，
    /// 它没有资格把 <c>landed</c> 那颗字拧成 false（那颗字管的是"用户的笔迹在不在盘上"）。</para>
    /// <para>⚠ 进程正好在这一发之后被 <c>Environment.Exit</c> 收走时，这一行还没落——
    /// 那条退出路⛔ 等它（等＝把用户的退出键抵押给一次库写）。补得回来：<see cref="BackfillAsync"/>
    /// 每次开机把盘上每一颗板再过一遍，而那颗板下次一动照样走这里。</para>
    /// </summary>
    public static void RegisterAfterSave(string sceneId, byte[] bytes)
    {
        try
        {
            RegisterAsync(sceneId, bytes, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 到这里的只有"库那一头炸了"（写锁／磁盘满／坏档）。写不成就是写不成，⛔ 再拿一次：
            // 重试的那一圈归开机回填，不归这一拍——这一拍的任务是把那块板交回屏幕。
            StarLog.Warn("boards.index.write.failed id=" + sceneId + " " + ex.GetType().Name + " " + ex.Message);
        }
    }

    /// <summary>
    /// 补那一行（幂等）。交回<b>真写过</b>那颗板没有——三档都会交 false，且各留一行痕：
    /// id 形状不对／那颗档读不懂／库那一头炸了（由 <see cref="RegisterAfterSave"/> 那格接）。
    /// <para><b>那颗"读不懂"为什么⛔ 把索引清成空</b>：交空串＝让一块<em>本来有字</em>的板当场查无此板，
    /// 而"读不懂"多半只持续那一发（半截档／同步工具正在占位）。留旧那一行顶多是"搜到一块板、盘上它已经改了"，
    /// 下一拍照样被正的那一发盖掉——两档里选损失小的那一个（口径与 <c>BoardPolicy.PickReadableScene</c> 同一句：
    /// 读不懂时唯一安全的动作是⛔ 动）。</para>
    /// </summary>
    public static async Task<bool> RegisterAsync(string sceneId, byte[] bytes, CancellationToken ct)
    {
        if (!BoardPolicy.IsSceneId(sceneId))
        {
            StarLog.Warn("boards.index.bad-id id=" + sceneId);
            return false;
        }
        var digest = BoardPolicy.SceneTextDigest(bytes);
        if (digest is null)
        {
            StarLog.Warn("boards.index.scene-unreadable id=" + sceneId + " bytes=" + (bytes?.Length ?? 0));
            return false;
        }
        var repo = App.Services.GetRequiredItemRepository();
        await repo.RecordItemAsync(Draft(sceneId, digest), ct);
        return true;
    }

    /// <summary>
    /// 开机回填：把盘上<b>每一颗</b>板各过一次索引（幂等；一颗板一行，重复跑只是把同一份正文再抄一遍）。
    /// <para>为什么要有它：本批之前落盘的那几十颗板压根没进过库——只接"从现在起写的那一发"的话，
    /// 用户手上的旧板永远搜不到，而那正是这一行要交付的东西。第二颗理由住在 <see cref="RegisterAfterSave"/>
    /// 那格注释里：进程退出时没落成的那一行靠它补。</para>
    /// <para>⛔ 跟着"退出那一拍"抢：调用方把它让开首屏（与剪贴板对账同一配方——那些写会与首屏的读抢同一把库写锁）。</para>
    /// </summary>
    /// <returns>真补过几颗板（认不到的那颗⛔ 算：那一发一句也没说）。</returns>
    public static async Task<int> BackfillAsync(CancellationToken ct)
    {
        var folder = BoardSceneStore.Folder;
        if (!Directory.Exists(folder)) return 0;
        var n = 0;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (!BoardPolicy.IsSceneFileName(name)) continue;    // .bak 与 *.tmp ⛔ 算一颗板
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception ex)
            {
                StarLog.Warn("boards.index.read.failed " + name + " " + ex.GetType().Name);
                continue;      // 被人攥着／正被改名：下一趟开机再补，⛔ 因此停整批
            }
            var id = Path.GetFileNameWithoutExtension(name);
            try
            {
                if (await RegisterAsync(id, bytes, ct)) n++;
            }
            catch (Exception ex)
            {
                StarLog.Warn("boards.index.write.failed id=" + id + " " + ex.GetType().Name + " " + ex.Message);
            }
        }
        return n;
    }

    /// <summary>
    /// 那颗板 → 待入库的那一行。<b>纯函数</b>（⛔ 摸盘、⛔ 问库），所以证人能逐颗钉住每一列吃什么。
    /// <para><b>Title＝那颗 id，⛔ 是一句上屏话</b>：这一列要落库，而库里存成品句＝把某一门语言烘进用户数据
    /// （换语言那些行还顶着旧语言，本仓定性过的那族）。那颗 id 同时就是档名（<see cref="BoardPolicy.SceneFileName"/>），
    /// 用户在文件那边看到的与在这一行看到的是<b>同一个东西</b>。</para>
    /// <para><b>Uri＝那颗档的 <c>file://</c></b>：不为"打开"而写（打开那块板归
    /// <see cref="BoardsWindows.OpenSceneAsync"/> 那一路，由 <see cref="BoardPolicy.OpensAsScene"/> 认出该走它），
    /// 它为的是<b>盘上还有没有这颗板</b>这件事现成有人答得出——<c>LocalFileIdentity.TryExistingPath</c>
    /// 与预览窗那条路都只认这一形。</para>
    /// <para><see cref="Item.Description"/> 吃的就是那颗正文：仓储拼 <c>search_text</c> 的口径是
    /// title＋description＋notes＋标签，所以"板上的字能被搜到"这件事⛔ 需要新表、也⛔ 新列——
    /// 它落在今天已经在索引里的那一列上（同仓那颗 <c>ClipboardEntry</c> 注释写的是同一句话）。</para>
    /// </summary>
    internal static Item Draft(string sceneId, string digest)
    {
        var path = BoardSceneStore.PathOf(sceneId);
        long created = 0, updated = 0;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                created = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds();
                updated = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
            }
        }
        catch (Exception) { /* 时间读不到＝交给仓储填当下（那一维⛔ 是判据，⛔ 为它停一整行） */ }
        return new Item
        {
            Type = ItemType.Board,
            Source = ItemSources.Board,
            SourceId = sceneId,
            Title = sceneId,
            Subtitle = string.Empty,
            Uri = LocalFileIdentity.UriForPath(path),
            Description = digest,
            CreatedAt = created,
            UpdatedAt = updated,
        };
    }
}
