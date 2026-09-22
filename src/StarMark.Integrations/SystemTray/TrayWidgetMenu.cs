#nullable enable

namespace StarMark.Integrations.SystemTray;

/// <summary>
/// 托盘「桌面组件」子菜单的命令 ID 分配。
/// <para>
/// 为什么单独成一个类：逐组件项是按 <c>Base + 序号</c> 发号的，而"全部显示/全部隐藏"两个固定命令
/// 原先紧贴在 <c>1110/1111</c> —— 也就是<b>只有 10 个槽</b>。子菜单一旦按真实的组件种类
/// （现 12 种）生成，第 11、12 项就会直接占掉这两个号 ⇒ 点"天气"实际执行的是"全部显示"。
/// 把两个 sentinel 改成<b>由块尾推导</b>，撞号就不可能再发生；槽数够不够由测试钉住。
/// </para>
/// </summary>
public static class TrayWidgetMenu
{
    /// <summary>逐组件项的起始命令 ID（与 Win32 菜单项一一对应）。</summary>
    public const int Base = 1100;

    /// <summary>逐组件项可用的命令槽数。</summary>
    public const int Capacity = 100;

    /// <summary>"全部显示"：显式落在组件块之后，而不是手抄一个字面量。</summary>
    public const int ShowAll = Base + Capacity;

    /// <summary>"全部隐藏"。</summary>
    public const int HideAll = ShowAll + 1;
}

/// <summary>
/// 子菜单里的一行。<paramref name="Kind"/> 存 <c>WidgetKind</c> 的<b>整数值</b>：本程序集不引用
/// StarMark.Core，故由宿主（主窗）从组件注册表注入，而不是在这儿另立一份手写清单。
/// </summary>
public sealed record TrayWidgetItem(int Kind, string Title);
