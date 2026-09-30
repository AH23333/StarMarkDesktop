#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.System;

namespace StarMark.UI.Helpers;

/// <summary>
/// 统一打开条目 URI 的助手。集中处理 file:// 与 http(s):// 的差异：
/// <list type="bullet">
///   <item>本地路径（文件 / 文件夹 / <c>.exe</c> / <c>.lnk</c>）经 <see cref="Process"/> 的
///   <c>UseShellExecute=true</c>（＝资源管理器双击），这是唯一能真正启动应用的托管途径；
///   <see cref="Launcher.LaunchFileAsync"/> 被 WinRT 拒启可执行文件、<see cref="Launcher.LaunchUriAsync"/>
///   对 <c>file://</c> 静默失效，二者都不能用来"打开应用"。</item>
///   <item>网页 URI 用 <see cref="Launcher.LaunchUriAsync"/>。</item>
/// </list>
/// 快捷启动格与搜索结果组件共用，避免重复逻辑。
/// </summary>
public static class LauncherEx
{
    public static async Task OpenAsync(string? uri) => await TryOpenAsync(uri);

    /// <summary>
    /// 打开并<b>回报到底开没开成</b>：返回 null＝已交给系统，否则是一句能直接显示给用户的原因。
    /// <para>为什么要有这个出口：这条路径上有五种"点了什么都没发生"（地址是空的、协议被闸门挡下、
    /// 认不出是完整地址、本地文件已删、系统里没有能处理这个协议的程序），原来四种只写日志。
    /// RSS 页点条目跳文章就落在这条路上——按 <see cref="OpenAsync"/> 那种"不返回话"的用法，
    /// 用户只会看到"点了一下没反应"，那正是本仓库"打不开应用"那条老缺陷的形状。</para>
    /// </summary>
    public static async Task<string?> TryOpenAsync(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return "这一行没有可打开的地址";
        // 协议白名单闸门：条目 URI 是可被导入/同步/外部数据影响的字符串，
        // 只放行 http/https/file，拒绝 javascript:/data:/ms-msdt:/自定义协议等借 Shell 协议处理器执行的记录。
        if (!StarMark.Abstractions.LaunchGuard.IsAllowedScheme(uri))
        {
            StarMark.Abstractions.StarLog.Warn($"拒绝以非法协议打开条目：{uri}");
            return "这个地址的协议不在允许范围内（只支持 http / https / 本机文件），已拒绝打开";
        }
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return "认不出这是一个完整地址";

            if (parsed.Scheme == Uri.UriSchemeFile)
            {
                // 还原规则（两类互补的生产者、原始形态优先）归 LocalFileIdentity.TryExistingPath 一颗，
                // 这里只交出本宿主自己的判定语义：文件与目录都算"在"（目录要在资源管理器里打开）。
                var opened = StarMark.Abstractions.LocalFileIdentity.TryExistingPath(
                    uri, static p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p), out var path);
                if (!opened)
                {
                    // 两种还原都不存在（文件已删）：退化为 URI 激活（可能无效果，但不抛异常），
                    // 同时把"这一台机器上已经没有这个文件了"说给用户听——只退化不回报，就是点了没反应。
                    await Launcher.LaunchUriAsync(parsed);
                    return "本机上的这个路径已经不在了（文件可能被移动或删除）：" + uri;
                }
                else
                {
                    // ShellExecute（资源管理器双击等价）：.exe/.lnk 才真正能跑，文档走关联程序，文件夹在资源管理器打开。
                    // 早先走 Launcher.LaunchFileAsync 是"打不开应用"根因——WinRT 按设计拒启可执行文件，
                    // 且其返回的 bool 被丢弃 → 点了没反应、无任何提示。
                    try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
                    catch (Exception ex)
                    {
                        StarMark.Abstractions.StarLog.Error($"打开本地路径失败（ShellExecute）：{path}", ex);
                        return "启动这个本机路径失败：" + ex.Message;
                    }
                }
                return null;
            }

            // LaunchUriAsync 返回的是"系统有没有找到能处理这个协议的程序"，不是"有没有抛异常"——
            // 不接住它，浏览器没注册 http 处理程序时就是一次彻底静默的点击。
            return await Launcher.LaunchUriAsync(parsed)
                ? null
                : "系统里没有能打开这个地址的默认程序（多半是浏览器未注册为 http 处理程序）";
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error($"打开条目失败: {uri}", ex);
            return "打开失败：" + ex.Message;
        }
    }
}
