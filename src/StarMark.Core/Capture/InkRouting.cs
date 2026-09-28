#nullable enable
namespace StarMark.Core.Capture;

/// <summary>
/// 全局「撤销／重做」落在<b>哪一叠笔迹</b>上的唯一判据（方案 §7：栈全局化、按 surface 归属）。
/// <para>
/// 之所以要下沉成 Core 纯函数：这条判据的方向一旦写反，症状是"我明明在贴图上画的，一按撤销
/// 画布上少了一笔"——两边都是"成功撤销了什么"，只有用户知道撤错了。而接线层写反是必然风险
/// （本仓已三起，见坑表 WF-1 那一族），所以判据留在这里，接线只准调用。
/// </para>
/// </summary>
public static class InkRouting
{
    /// <summary>
    /// 前台那扇窗是贴图 ⇒ 这一键属于那张贴图；其余（画布玻璃／工具条／别人的窗／没查到）⇒ 属于画布板。
    /// <para><b>判据是"哪扇窗在吃键盘"，不是"桌面上有没有贴图"</b>：挂着三张贴图而用户正在画布上画时，
    /// 后一种判法会把画布的撤销抢走，交给一张他根本没在看的贴图。</para>
    /// <para>Sheet（截图冻帧）不在这里出现不是遗漏：截图会话期间画布那批热键整批不注册
    /// （<c>HotkeyGate</c>），而截图窗自己有窗口内的 Ctrl+Z／Ctrl+Y，永远不会走到这条全局路由上。</para>
    /// </summary>
    public static bool UndoBelongsToFocusedPin(SurfaceRole? focusedRole) => focusedRole == SurfaceRole.Pin;
}
