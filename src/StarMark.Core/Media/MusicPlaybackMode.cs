namespace StarMark.Core.Media;

/// <summary>
/// 播放模式。SMTC 只有「随机开关」和「自动重复模式」两个独立开关，
/// DeskBox 把它们折叠成一个三态循环（普通 → 随机 → 循环 → 普通），这里照搬。
/// <para>
/// 放在 Core 而不是 UI：它是<b>不碰 WinRT</b> 的纯逻辑，测试项目不引用
/// <c>StarMark.UI</c>（UI 层拉不进单测宿主），下沉到 Core 才能真正被单测覆盖。
/// </para>
/// </summary>
public enum MusicPlaybackMode
{
    Normal = 0,
    Shuffle = 1,
    Repeat = 2,
}

/// <summary>播放模式循环的纯计算：模式怎么切、按钮显示什么。</summary>
public static class MusicPlaybackModeMath
{
    /// <summary>
    /// 下一个模式：普通 →（能随机则）随机 →（能循环则）循环 → 普通。
    /// 缺哪个能力就跳过哪一态 —— 播放器不支持随机时不该给出一个永远点不动的按钮态。
    /// </summary>
    public static MusicPlaybackMode NextMode(MusicPlaybackMode current, bool canShuffle, bool canRepeat) => current switch
    {
        MusicPlaybackMode.Normal when canShuffle => MusicPlaybackMode.Shuffle,
        MusicPlaybackMode.Normal when canRepeat => MusicPlaybackMode.Repeat,
        MusicPlaybackMode.Shuffle when canRepeat => MusicPlaybackMode.Repeat,
        MusicPlaybackMode.Shuffle => MusicPlaybackMode.Normal,
        MusicPlaybackMode.Repeat => MusicPlaybackMode.Normal,
        _ => MusicPlaybackMode.Normal,
    };

    /// <summary>Segoe Fluent 字形：随机 / 循环 / 顺序播放。各 Windows 版本都带这套字形，不会渲染成豆腐块。</summary>
    public static string Glyph(MusicPlaybackMode mode) => mode switch
    {
        MusicPlaybackMode.Shuffle => "\uE8B1",
        MusicPlaybackMode.Repeat => "\uE8EE",
        _ => "\uE8CB",
    };

    /// <summary>模式按钮的悬浮提示。</summary>
    public static string Label(MusicPlaybackMode mode) => mode switch
    {
        MusicPlaybackMode.Shuffle => "随机播放（点击切换）",
        MusicPlaybackMode.Repeat => "列表循环（点击切换）",
        _ => "顺序播放（点击切换）",
    };
}
