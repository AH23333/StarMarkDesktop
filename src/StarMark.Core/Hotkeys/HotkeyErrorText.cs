#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace StarMark.Core.Hotkeys;

/// <summary>
/// Win32 错误码 → 给人看的原因。
/// <para>
/// 单独立一个纯函数而不是在各调用点现写文案：同一个码在"注册热键失败"与"装钩子失败"两处
/// 含义不同，且**未知码必须带上编号**——用户拿着编号能查到根因，只看到"可能失败"就只能挨个猜。
/// 已确认语义的码才写成人话，其余一律"（Win32 错误 N）"，不编造解释。
/// </para>
/// </summary>
public static class HotkeyErrorText
{
    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED：同一组合键已被（本进程外的）程序注册。</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>ERROR_ACCESS_DENIED：系统拒绝该操作（底层键盘钩子常见于安全软件或组策略拦截）。</summary>
    public const int ErrorAccessDenied = 5;

    /// <summary>ERROR_HOOK_CREATION_FAILED：钩子创建失败。</summary>
    public const int ErrorHookCreationFailed = 1401;

    /// <summary>
    /// 注册全局热键失败的原因。
    /// <para>
    /// <paramref name="siblingInstancePids"/>＝本机还活着的**本程序其它实例**的 pid。为什么要单独传这个：
    /// Win32 不回答"这条组合键被谁占了"，而 1409 最常见的一种占用方就是我们自己的另一个实例
    /// （开发期同时开几份、或留着一个提权实例）。一律写成"被其它程序占用"，用户就会去关本不相干的软件、
    /// 或者给这些键换个组合——两种都是白做工，而那条键其实一直在另一个实例那儿好好的生效着。
    /// </para>
    /// </summary>
    public static string RegisterFailure(int errorCode, IReadOnlyList<int>? siblingInstancePids = null) => errorCode switch
    {
        ErrorHotkeyAlreadyRegistered when siblingInstancePids is { Count: > 0 } =>
            $"本程序还另开着 {siblingInstancePids.Count} 个实例（pid {string.Join("、", siblingInstancePids.Select(id => id.ToString()))}），"
            + "这条键此刻由那边响应（按它动的就是那个实例）；这个实例会自己继续重试，那边退出后自动接回来",
        ErrorHotkeyAlreadyRegistered => "该组合键已被其它程序占用",
        _ => $"系统拒绝注册（Win32 错误 {errorCode}）",
    };

    /// <summary>安装底层键盘钩子失败的原因——此时收不到任何按键，录制不能用。</summary>
    public static string HookInstallFailure(int errorCode) => errorCode switch
    {
        ErrorAccessDenied => "系统拒绝了键盘钩子的安装（Win32 错误 5，通常是安全软件或组策略拦截）",
        ErrorHookCreationFailed => "键盘钩子创建失败（Win32 错误 1401）",
        _ => $"键盘钩子安装失败（Win32 错误 {errorCode}）",
    };

    /// <summary>
    /// 组合键被占用时，<b>紧跟着把出路指到同一行上</b>。
    /// <para>
    /// 为什么单独立一颗：报完"被占用"就停手，等于把活儿交给用户自己去查哪个程序占了这颗键（P-54 口径）。
    /// 而换键的入口本来就在同一行——那颗显示键位的按钮点下去就是重录；这一句只是<b>把它说出来</b>。
    /// （批次 ST 登记 P-136 时我把这条写成"用户侧没有出口"，实际入口早在批次 JM/KL 就落地了：
    /// 缺的是指路那句话，不是那颗按钮。）
    /// </para>
    /// <para>
    /// 措辞刻意<b>不提"重启"</b>（本项目里"请重启"按缺陷算），也<b>不许出现"已自动改绑"</b>：
    /// 换哪颗键由他挑，程序不许替他动他选的键（与「重试注册」同一条边界）。
    /// </para>
    /// </summary>
    public const string RebindHint = "要现在就归这个实例用：点这一行那颗显示键位的按钮换一个组合，再点「保存快捷键」。";
}
