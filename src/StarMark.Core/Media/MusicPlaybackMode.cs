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

    /// <summary>
    /// 模式按钮悬停说明那颗<b>键名</b>（✅WI-72：从前这一颗返回中文成品句，
    /// 于是组件那一行的提示在英文与日文界面下照样念「随机播放（点击切换）」——字压根没进表，
    /// 数汉字的尺与"每行都得有人问"的闸门同时判它干净＝P-197 那一族的形状）。
    /// <para>交键而⛔ 交字：三态各一颗、⛔ 一把钥匙开三态（坑表 #290 那一族：顺序⛔ 动＝数字住在设置档里对着枚举值）。</para>
    /// </summary>
    public static string ModeKey(MusicPlaybackMode mode) => mode switch
    {
        MusicPlaybackMode.Shuffle => "widget.music.mode.shuffle",
        MusicPlaybackMode.Repeat => "widget.music.mode.repeat",
        _ => "widget.music.mode.normal",
    };
}
