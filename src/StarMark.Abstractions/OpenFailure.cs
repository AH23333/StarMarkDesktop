#nullable enable
namespace StarMark.Abstractions;

/// <summary>
/// 「点开这一行」到底卡在哪一步。
/// <para>为什么要有这么一颗枚举，而不是只递一句话：这条链上五种"点了什么都没发生"过去只写日志
/// （P-53/P-54 那一族：日志不是用户能看到的反馈面）。光有话也还不够——宿主需要分辨
/// <b>哪一类失败该就地给一个动作</b>：「东西已经不在这台机器上」该给「删掉这一行」，
/// 而"系统里没有能开它的程序"给同一颗按钮就是错的（那条不是坏数据，是缺一个关联程序）。</para>
/// </summary>
public enum OpenFailure
{
    /// <summary>已经交给系统，没有坏消息。</summary>
    None,

    /// <summary>这一行根本没有可打开的地址。</summary>
    NothingToOpen,

    /// <summary>协议不在白名单里（<c>javascript:</c>／<c>data:</c>／自定义协议那一类，被 <c>LaunchGuard</c> 挡下）。</summary>
    SchemeRejected,

    /// <summary>这串文字认不出是一个完整地址。</summary>
    NotAbsolute,

    /// <summary>本机文件条目，但盘上已经没有这个东西——<b>唯一该就地递出「删掉这一行」的那一类</b>。</summary>
    MissingOnDisk,

    /// <summary>系统里没有能处理这个协议的程序（多半是浏览器没注册成 http 处理程序）。</summary>
    NoHandler,

    /// <summary>启动时抛出来的异常（无权限、路径过长等）。</summary>
    Error,
}
