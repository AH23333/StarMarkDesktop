#nullable enable
using System;
using StarMark.Abstractions;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// 全程序唯一的"看得见"提醒出口（批次 RV）：右下角那张提示卡。
/// <para>
/// 这里原先接的是托盘气泡：向系统请求一条挂在托盘图标上的通知。那条链在 Windows 11 上
/// <b>返回 TRUE 而屏幕上什么都没有</b>：调用方拿到"成功"，于是主窗提示条与"只留日志"两条兜底按构造永不触发，
/// 日志写着"提醒已发出"，用户一次都没看见（发起人真机反馈："所有声称气泡效果的，未曾见到气泡效果，均无提示效果"）。
/// 所以判据换成"窗口可见 + 有实际尺寸 + 矩形落在某块屏的工作区内"，见 <see cref="NoticeCardWindow"/>。
/// </para>
/// <para>
/// <b>只有一张卡、复用一扇窗</b>：角落里叠三张的话，用户只会看见最上面那张，"到底提醒了几次"就没有凭据了。
/// 必须在 UI 线程调用（建窗要用调度队列）；不在 UI 线程时这里会抓住异常并回 false，
/// 调用方据此退到自己的兜底（日志／主窗提示条），而不是以为已经提醒过了。
/// </para>
/// </summary>
internal static class NoticeCard
{
    private static NoticeCardWindow? _window;

    /// <summary>
    /// 发一条提醒，返回它<b>是否真的贴在了屏幕上</b>。
    /// <para><paramref name="actionUrl"/>／<paramref name="actionLabel"/> 是给"看完就能做一步"的那类消息用的
    /// （批次 UE：有新版本时点整张卡就打开它自己的发布页）。传进来的一律必须是<b>本程序自己拼出来的</b>地址，
    /// 调用方不许把外部服务器给的回链直接递到这里（理由见 <c>NoticeCardWindow.SetAction</c>）。</para>
    /// </summary>
    public static bool Show(string title, string message, string? actionUrl = null, string? actionLabel = null)
    {
        try
        {
            if (_window is null)
            {
                _window = new NoticeCardWindow();
                // 窗被外部关掉（进程收尾、系统回收）后要能把引用丢掉，否则下一条提醒会一直发给一扇已死的窗，
                // 并且每次都诚实地回答"没贴上屏幕"——那条兜底链就在替一个永不重来的 bug 打掩护。
                _window.Closed += (_, _) => _window = null;
            }
            // 动作要先落到窗上再换内容：那条"点一下…"的说明行是卡片自己按有没有动作生成的，
            // 晚一步就会先贴上屏幕再改字（一帧的字面跳动）。没有动作的消息也必须走这一句，否则上一条的动作会留着。
            _window.SetAction(actionUrl, actionLabel);
            return _window.Apply(title, message);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[提示卡] 没能贴上屏幕（{title}：{message}）：{ex.Message}");
            return false;
        }
    }

    /// <summary>收卡（下一次提醒仍复用同一扇窗）。用于"这条消息已经没意义了"的场合。</summary>
    public static void Hide() => _window?.HideCard();

    /// <summary>
    /// 截图抓帧前后<b>成对</b>调用：抓屏抓的是已经合成好的屏幕，卡片那一块事后减不回来，
    /// 所以"截图含不含提示卡"只能在那一帧之前收起来决定（口径同 <c>CanvasService.SetHiddenForCapture</c>）。
    /// </summary>
    public static void SetHiddenForCapture(bool hidden)
    {
        try { _window?.SetHiddenForCapture(hidden); }
        catch (Exception ex) { StarLog.Warn($"[提示卡] 抓帧前后的收/还失败：{ex.Message}"); }
    }
}
