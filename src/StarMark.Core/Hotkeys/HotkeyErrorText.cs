#nullable enable

using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;

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
    /// 注册全局热键失败的原因（WI-2r 起字进语言表；键逐颗写字面＝坑表 #331，<c>{0}</c>/<c>{1}</c> 是 pid 数字＝非语言内容）。
    /// <para>
    /// <paramref name="siblingInstancePids"/>＝本机还活着的**本程序其它实例**的 pid。为什么要单独传这个：
    /// Win32 不回答"这条组合键被谁占了"，而 1409 最常见的一种占用方就是我们自己的另一个实例
    /// （开发期同时开几份、或留着一个提权实例）。一律写成"被其它程序占用"，用户就会去关本不相干的软件、
    /// 或者给这些键换个组合——两种都是白做工，而那条键其实一直在另一个实例那儿好好的生效着。
    /// </para>
    /// <para><b>批次 WI-40-3b 起交回的是「键＋实参」而不是成品串</b>：这一句要进设置页那一行的模板，
    /// 而模板行的取词时刻归界面（换语言那一刻重念）。词早就进了表，<b>取词时机</b>才是那一格缺陷（坑表 #362）——
    /// 返回 <c>string</c> 等于在注册失败的那一刻把语言钉死，而注册是自动重试的，那句话可能在几分钟前算好。</para>
    /// </summary>
    public static LocMessage RegisterFailure(int errorCode, IReadOnlyList<int>? siblingInstancePids = null) => errorCode switch
    {
        ErrorHotkeyAlreadyRegistered when siblingInstancePids is { Count: > 0 } =>
            LocMessage.Of("hotkey.err.occupiedBySiblings", siblingInstancePids.Count.ToString(),
                new LocJoin(siblingInstancePids.Select(id => (object?)id.ToString()).ToList(), "hotkey.err.pidJoin")),
        ErrorHotkeyAlreadyRegistered => LocMessage.Of("hotkey.err.occupied"),
        _ => LocMessage.Of("hotkey.err.registerDenied", errorCode.ToString()),
    };

    /// <summary>安装底层键盘钩子失败的原因——此时收不到任何按键，录制不能用。取词时刻同 <see cref="RegisterFailure"/>。</summary>
    public static LocMessage HookInstallFailure(int errorCode) => errorCode switch
    {
        ErrorAccessDenied => LocMessage.Of("hotkey.err.hookDenied"),
        ErrorHookCreationFailed => LocMessage.Of("hotkey.err.hookCreate"),
        _ => LocMessage.Of("hotkey.err.hookFailed", errorCode.ToString()),
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
    public const string RebindHintKey = "hotkey.err.rebindHint";

    /// <summary>
    /// WI-2r 起提示句进语言表，批次 WI-40-3b 起<b>交回的是键</b>而不是取好词的那一句：
    /// 它现在坐在设置页那一行的模板里，换语言那一刻要跟着重念。
    /// <para>名字⛔ 顺手改成 <c>…Message</c>：那颗闸门按 <c>"HotkeyErrorText.RebindHint"</c> 这个串钉
    /// "指路的话只有一个出处"，改名字会让它读起来像没人引用了（而同名加一颗才是真风险——两颗都在，措辞就会只改到一半）。</para>
    /// </summary>
    public static LocMessage RebindHint => LocMessage.Of(RebindHintKey);
}
