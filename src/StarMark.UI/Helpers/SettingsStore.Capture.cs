#nullable enable

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——截屏这一族（截图 / 贴图 / 识字）的总开关。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>
    /// 截屏总开关（<b>默认开</b>：这是一条做完了的功能，"没表过态"不等于"关过"，同屏幕画布那条判据）。
    /// <para>这一处只回答"设置里是怎么写的"。<b>哪些入口因此消失、哪些键因此不注册</b>不在这里判——
    /// 那些判据在 <c>Core/Hotkeys/CaptureGate</c>，托盘、热键注册投影、<c>ScreenshotService.Start</c> 三个读者都问它，
    /// 谁在这里自己写一遍"关掉就藏哪几项"，三处迟早不一致（记忆 ⑧）。</para>
    /// </summary>
    public bool LoadCaptureEnabled() => Load() is not { } d || d.CaptureEnabled != false;

    public void SaveCaptureEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.CaptureEnabled = enabled;
        Save(d);
    }
}
