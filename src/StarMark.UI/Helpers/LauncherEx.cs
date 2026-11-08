#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.System;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 统一打开条目<b>地址</b>的助手——收的是<b>绝对 URI 形态</b>的串（<c>file://…</c>／<c>http(s)://…</c>），按协议分流：
/// <list type="bullet">
///   <item><c>file://</c>（本机文件／文件夹，含 <c>.exe</c>／<c>.lnk</c>）经 <see cref="Process"/> 的
///   <c>UseShellExecute=true</c>（＝资源管理器双击），这是唯一能真正启动应用的托管途径；
///   <see cref="Launcher.LaunchFileAsync"/> 被 WinRT 拒启可执行文件、<see cref="Launcher.LaunchUriAsync"/>
///   对 <c>file://</c> 静默失效，二者都不能用来"打开应用"。</item>
///   <item>网页 URI 用 <see cref="Launcher.LaunchUriAsync"/>。</item>
/// </list>
/// <para><b>裸磁盘路径（没有 <c>file://</c> 前缀的 <c>D:\dir</c> 那一类）⛔ 在本类的射程里</b>（P-236 改口，2026-10-19）。
/// 这条注释以前把"本地路径"整个写成了自己的射程，那是说大了：协议闸门确实放裸路径过去
/// （<c>new Uri("D:\\x")</c> 在 Windows 上解析成 <c>file:</c>），可还原那一问归
/// <see cref="LocalFileIdentity.TryExistingPath"/>——它的入口条件就是 <c>file://</c> 前缀，裸路径两格候选都是空串，
/// 于是当场判成"这一台机器上已经没有这个文件了"，再退化成 <see cref="Launcher.LaunchUriAsync"/>。
/// 真机上那一发的读数＝一句与事实无关的坏话＋桌面多开一颗<b>主文件夹</b>窗
/// （<c>bin/WI91/readings_39_183824.txt</c> 逐字为证；<c>LocalPathResolutionTests</c> 里那格行为用例钉着同一件事）。</para>
/// <para>⇒ 手上只有磁盘路径的宿主，先经 <see cref="LocalFileIdentity.UriForPath"/> 拼成 URI 再递进来
/// （画板那颗「打开所在文件夹」就是这么走的，见 ✅P-219）。要不要反过来让本类就地收裸路径＝另一颗契约决定，
/// 那要连 <see cref="OpenFailure.MissingOnDisk"/> 那句的措辞一起改，⛔ 半件。</para>
/// 快捷启动格与搜索结果组件共用，避免重复逻辑。
/// </summary>
public static class LauncherEx
{
    /// <summary>
    /// "这台机器上确实放着这个东西"——文件与目录都算存在，全程序<b>只有这一颗</b>（批次 VR）。
    /// <para>它过去在 <c>LauncherEx</c>／<c>ItemCardActions</c>／<c>ItemDragHelper</c>／快捷启动的拖入与手输／
    /// 设置页的失联扫描里各写一遍（<c>p =&gt; File.Exists(p) || Directory.Exists(p)</c>）。
    /// <b>少写一半 <c>Directory.Exists</c> 的那一份会造出的成因形状是"文件夹明明还在、却被说不在了"</b>
    /// （批次 210 修过一次同款；⚠ 这是<b>读码推出来的形状，本机没有任何一行命中它</b>——批次 VS 更正过这里的措辞，坑表 #252）。
    /// 收成一颗的真正理由不需要那条症状：同一个磁盘事实分五份写，将来任何一方说不清"当时是哪一份在答话"。</para>
    /// <para>住在 UI 而不是 <c>Abstractions</c>：本地路径那颗判据刻意<b>不碰磁盘</b>（存在与否一律由调用方注入，
    /// 由 <c>LocalPathResolutionTests.TheJudgeNeverTouchesTheDiskItself</c> 钉着），所以磁盘事实归宿主这一侧。</para>
    /// </summary>
    public static bool ExistsOnDisk(string? path)
        => !string.IsNullOrEmpty(path) && (File.Exists(path!) || Directory.Exists(path!));

    /// <summary>打开并回报到底开没开成（只有话、没有种类）。与 <see cref="TryOpenDetailedAsync"/> 是同一条代码、两种问法。</summary>
    public static async Task<string?> TryOpenAsync(string? uri) => (await TryOpenDetailedAsync(uri)).Message;

    /// <summary>
    /// 打开并回报<b>坏在哪一步</b>＋能直接显示的那句话。
    /// <para><paramref name="uri"/> 收<b>绝对 URI 形态</b>（<c>file://…</c>／<c>http(s)://…</c>）；
    /// 裸磁盘路径⛔ 在射程内——它过得去协议闸门却⛔ 进不了 <see cref="LocalFileIdentity.TryExistingPath"/>，
    /// 最后落在 <see cref="OpenFailure.MissingOnDisk"/> 那一句上。只有磁盘路径就先经
    /// <see cref="LocalFileIdentity.UriForPath"/> 拼一颗 URI（本类文件头那段是完整口径）。</para>
    /// <para>为什么要有这个出口：这条路径上有五种"点了什么都没发生"（地址是空的、协议被闸门挡下、
    /// 认不出是完整地址、本地文件已删、系统里没有能处理这个协议的程序），原来四种只写日志。
    /// RSS 页点条目跳文章就落在这条路上——按"不返回话"的那种用法（本类曾有的一颗 <c>OpenAsync</c>，
    /// 批次 VR 摘掉：留着就等于允许新宿主再一次把话扔在返回值里），
    /// 用户只会看到"点了一下没反应"，那正是本仓库"打不开应用"那条老缺陷的形状。</para>
    /// </summary>
    public static async Task<(OpenFailure Kind, string? Message)> TryOpenDetailedAsync(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return (OpenFailure.NothingToOpen, "这一行没有可打开的地址");
        // 协议白名单闸门：条目 URI 是可被导入/同步/外部数据影响的字符串，
        // 只放行 http/https/file，拒绝 javascript:/data:/ms-msdt:/自定义协议等借 Shell 协议处理器执行的记录。
        if (!StarMark.Abstractions.LaunchGuard.IsAllowedScheme(uri))
        {
            StarMark.Abstractions.StarLog.Warn($"拒绝以非法协议打开条目：{uri}");
            return (OpenFailure.SchemeRejected, "这个地址的协议不在允许范围内（只支持 http / https / 本机文件），已拒绝打开");
        }
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return (OpenFailure.NotAbsolute, "认不出这是一个完整地址");

            if (parsed.Scheme == Uri.UriSchemeFile)
            {
                // 还原规则（两类互补的生产者、原始形态优先）归 LocalFileIdentity.TryExistingPath 一颗，
                // 这里只交出本宿主自己的判定语义：文件与目录都算"在"（目录要在资源管理器里打开）——那颗判据就是本类上面的 ExistsOnDisk。
                // 「在不在盘上」这一问整段进线程池（片251）：冷盘降速唤醒／网络路径上 File.Exists 可达秒级，
                // 而点标题＝打开是卡片最高频的一路，卡的就是按下后的那一帧；await 续体回 UI 线程，ShellExecute 与回报照旧。
                var path = string.Empty;
                if (!await Task.Run(() => StarMark.Abstractions.LocalFileIdentity.TryExistingPath(
                        uri, ExistsOnDisk, out path)))
                {
                    // 两种还原都不存在（文件已删）：退化为 URI 激活（可能无效果，但不抛异常），
                    // 同时把"这一台机器上已经没有这个文件了"说给用户听——只退化不回报，就是点了没反应。
                    await Launcher.LaunchUriAsync(parsed);
                    // 这句话归 ItemCardPolicy.MissingRowMessage 一颗（批次 SI 的口径：同一件事只许一个出处），
                    // 但递进去的是 DisplayPath 解过的那一串：库里存的是编码态时，别把 %E5%B7%A5… 念给用户听（批次 VR）。
                    // 种类那一格才是宿主判断"要不要就地递出删掉这一行"的依据。
                    // DisplayPath 同样要问盘（挑哪一格给人看）：跟着上一问一起离 UI 线程。
                    return (OpenFailure.MissingOnDisk, await Task.Run(() => StarMark.Abstractions.ItemCardPolicy.MissingRowMessage(
                        StarMark.Abstractions.LocalFileIdentity.DisplayPath(uri, ExistsOnDisk)).Render()));
                }
                // ShellExecute（资源管理器双击等价）：.exe/.lnk 才真正能跑，文档走关联程序，文件夹在资源管理器打开。
                // 早先走 Launcher.LaunchFileAsync 是"打不开应用"根因——WinRT 按设计拒启可执行文件，
                // 且其返回的 bool 被丢弃 → 点了没反应、无任何提示。
                try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
                catch (Exception ex)
                {
                    StarMark.Abstractions.StarLog.Error($"打开本地路径失败（ShellExecute）：{path}", ex);
                    return (OpenFailure.Error, "启动这个本机路径失败：" + ex.Message);
                }
                return (OpenFailure.None, null);
            }

            // LaunchUriAsync 返回的是"系统有没有找到能处理这个协议的程序"，不是"有没有抛异常"——
            // 不接住它，浏览器没注册 http 处理程序时就是一次彻底静默的点击。
            return await Launcher.LaunchUriAsync(parsed)
                ? (OpenFailure.None, null)
                : (OpenFailure.NoHandler, "系统里没有能打开这个地址的默认程序（多半是浏览器未注册为 http 处理程序）");
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error($"打开条目失败: {uri}", ex);
            return (OpenFailure.Error, "打开失败：" + ex.Message);
        }
    }
}
