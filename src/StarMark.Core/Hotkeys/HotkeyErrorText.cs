#nullable enable

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

    /// <summary>注册全局热键失败的原因。</summary>
    public static string RegisterFailure(int errorCode) => errorCode switch
    {
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
}
