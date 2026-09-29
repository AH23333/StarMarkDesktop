#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Core.Hotkeys;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——截屏这一族的总开关（批次 RP，应发起人点名"需要将截屏功能总开关放入拓展功能设置页"）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"。</para>
/// </summary>
public partial class SettingsPageViewModel
{
    /// <summary>
    /// 截屏总开关（<b>默认开</b>）。关掉之后两件事同时发生：三条"发起框选"的全局热键<b>不再注册</b>
    /// （F1／F3 这类裸功能键交还给其它软件），托盘里那三项整条消失。
    /// <para><b>不</b>影响已经钉在桌面上的贴图，也不影响"显示／隐藏所有贴图""忽略鼠标"那两条——
    /// 它们管的是"图已经在那里"，而关掉一个功能不该顺手把用户桌上的东西变成拆不掉的。</para>
    /// </summary>
    [ObservableProperty] private bool _captureEnabled = true;

    /// <summary>开关当前含义的一句话（键位从当前绑定现取，界面不自己写"F1"）。</summary>
    [ObservableProperty] private string _captureStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制写盘（否则每次进设置页都重写一遍档，还会顺带重建托盘与重注册热键）。</summary>
    private bool _suppressCaptureApply;

    partial void OnCaptureEnabledChanged(bool value)
    {
        if (_suppressCaptureApply) return;
        _settings.SaveCaptureEnabled(value);
        // 托盘与热键注册表<b>当场</b>跟着改：不重启，也不要用户再去点一次「保存快捷键」——多一步就算缺陷。
        App.MainWindow?.ApplyTraySettings();
        CaptureStatus = BuildCaptureStatus();
    }

    /// <summary>
    /// 状态那一行要说的两件事：<b>哪三条入口没了</b>，以及<b>哪些东西不受影响</b>。
    /// <para>键位一律现取（<c>HotkeyText</c> 转发 <c>CanvasService.BindingText</c>，读的是真实绑定）——
    /// 把"F1"写死在说明里，用户改了键之后这句就成了假指引（同一族：批次 WM"界面不自己写中文"）。</para>
    /// </summary>
    private string BuildCaptureStatus()
    {
        var capture = HotkeyText(HotkeyActions.ScreenCapture);
        var pin = HotkeyText(HotkeyActions.ScreenPin);
        var ocr = HotkeyText(HotkeyActions.ScreenOcr);
        return CaptureEnabled
            ? $"已开启：截图 {capture} · 贴图 {pin} · 识字 {ocr}（这是此刻真正生效的键位；要改到「快捷键」页的「截图 / 贴图 / 识字」分组）。"
            : $"已关闭：那三条发起框选的热键不再向系统注册（{capture}、{pin}、{ocr} 交还给当前窗口），" +
              "托盘里的「截图 / 贴图 / 识字」三项一并消失。" +
              "已经钉在桌面上的贴图不受影响：显示／隐藏、忽略鼠标、关闭全部照常可用——" +
              "那几条管的是「图已经在那里」这件事，关掉一个功能不该顺手把桌上的图变成拆不掉。";
    }
}
