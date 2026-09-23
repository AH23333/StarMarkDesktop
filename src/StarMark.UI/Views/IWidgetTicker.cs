#nullable enable

namespace StarMark.UI.Views;

/// <summary>
/// 需要"随窗口可见性启停定时器"的组件契约。
/// <para>
/// 宿主 <c>WidgetWindow</c> 原来对时钟写的是 <c>if (_kind == WidgetKind.Clock) _clockWidget?.UpdateRunning(...)</c>
/// 这样的四处按类型分支；每加一种常驻刷新的组件（世界时钟/倒计时/番茄钟/系统监控）就要在多一处补一行，
/// 漏补的现象是"窗口隐藏了还在跑"或"显示了却不刷新"。改为认这个接口：内容实现了就驱动，没实现就不动。
/// </para>
/// </summary>
public interface IWidgetTicker
{
    /// <summary>宿主窗口可见性确定后调用：<paramref name="windowVisible"/> 为 false 时必须停表（常驻应用省电）。</summary>
    void UpdateRunning(bool windowVisible);

    /// <summary>永久停止（窗口关闭 / 组件被移除），之后不应再被驱动。</summary>
    void Stop();
}
