#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.UI.Services;

namespace StarMark.UI.Helpers;

/// <summary>
/// 收藏完成后的即时分类挂钩（§19 O5）。<b>唯一职责是把"动作派生、异步执行"这条链接上，
/// 且对收藏零可见</b>——拿不到服务、服务抛了，都只写日志。
/// <para>fire-and-forget 安全的前提：被调方法（TryInstantClassifyAsync）内部把所有异常都吃掉，
/// 冒不出未观察任务异常；闸门四关（开关/配置/类型/无标签）全在对方内部判，这里不做二次判断——
/// 判断写两处就是将来分岔开始的地方。</para>
/// </summary>
public static class AiInstantClassifyHook
{
    public static void AfterCollect(Item saved)
    {
        try
        {
            if (saved.Id <= 0) return;   // 没拿到真 id（仓储没回）：本就没得可分
            var service = App.Services?.GetService<AiClassifyService>();
            if (service is null) return;
            _ = service.TryInstantClassifyAsync(saved);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI 即时分类] 挂钩没接上（收藏不受影响）：{ex.Message}");
        }
    }
}
