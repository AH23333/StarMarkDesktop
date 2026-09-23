#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Core.Hotkeys;

/// <summary>
/// 同一手势上归并出来的一个注册单元：<see cref="Actions"/> 是绑到它的全部动作（一次触发全部执行）。
/// </summary>
/// <param name="Gesture">归并组内首个动作对应的手势（同组手势在修饰键与主键上必然相同）。</param>
/// <param name="Actions">按动作声明顺序排列的动作 id。</param>
public sealed record HotkeyGestureGroup(HotkeyGesture Gesture, IReadOnlyList<string> Actions)
{
    /// <summary>注册表 / 冲突标注共用的归并键。</summary>
    public string Key => HotkeyGesture.GestureKey(Gesture);

    /// <summary>一个手势被多个动作占用（允许，仅在设置页行内提示）。</summary>
    public bool IsConflict => Actions.Count > 1;
}

/// <summary>
/// 快捷键绑定的合并与归并规则（纯函数，无 WinUI / Win32 依赖）。
/// <para>
/// 放这里的唯一理由是它是本项目出过真问题的地方：「默认 + 已保存」的读法必须能把
/// "用户显式清掉了某动作"与"这个动作从没设过"分辨开——两者若在数据上同形，清掉的
/// Ctrl+Alt+Space 下次读取必被默认值复活，还会造成"界面显示未设置、注册却仍活着"的错位。
/// 判据本身可机检，就不该留在只能真机验收的 UI 工程里。
/// </para>
/// </summary>
public static class HotkeyBindings
{
    /// <summary>Win32 虚拟键码 VK_SPACE（Core 层不引 WinRT 的 VirtualKey 枚举）。</summary>
    public const uint VirtualKeySpace = 0x20;

    /// <summary>
    /// 出厂默认：主界面呼出/关闭 = Ctrl+Alt+Space。
    /// 每次调用给出**新的可写字典**，调用方可以就地叠加而不污染默认表。
    /// </summary>
    public static Dictionary<string, HotkeyGesture> Defaults() => new()
    {
        [HotkeyActions.MainToggle] = new HotkeyGesture(
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, VirtualKeySpace),
    };

    /// <summary>
    /// 默认 + 已保存的合并结果：<b>已保存的每一项都覆盖默认，空手势也算覆盖</b>
    /// （<see cref="HotkeyGesture.IsEmpty"/> 是"用户显式清过这一条"的记号，写成删键就会被默认值复活）。
    /// <para><paramref name="saved"/> 为 null ＝设置里的绑定 JSON 损坏/无法解析 ⇒ 整表按默认处理。</para>
    /// </summary>
    public static Dictionary<string, HotkeyGesture> MergeWithDefaults(IReadOnlyDictionary<string, HotkeyGesture>? saved)
    {
        var merged = Defaults();
        if (saved is null) return merged;
        foreach (var kv in saved)
        {
            // 磁盘 JSON 里手改出来的显式 null：当作没设过，不能让下游按非空假设取值时崩
            if (kv.Value is null) continue;
            merged[kv.Key] = kv.Value;
        }
        return merged;
    }

    /// <summary>
    /// 按手势归并（同一手势的多个动作合成一个注册单元）；<b>空手势一律剔除</b>（未绑定／已被清除）。
    /// 返回顺序＝各手势首次出现的顺序，注册 id 由此递增。
    /// </summary>
    public static IReadOnlyList<HotkeyGestureGroup> GroupByGesture(IReadOnlyDictionary<string, HotkeyGesture>? bindings)
    {
        var byKey = new Dictionary<string, (HotkeyGesture Gesture, List<string> Actions)>();
        var order = new List<string>();
        if (bindings is not null)
            foreach (var (action, gesture) in bindings)
            {
                if (gesture.IsEmpty) continue;
                var key = HotkeyGesture.GestureKey(gesture);
                if (!byKey.TryGetValue(key, out var entry))
                {
                    entry = (gesture, new List<string>());
                    byKey[key] = entry;
                    order.Add(key);
                }
                entry.Actions.Add(action);
            }

        return order.Select(k => new HotkeyGestureGroup(byKey[k].Gesture, byKey[k].Actions)).ToList();
    }

    /// <summary>冲突＝一个手势上挂了多个动作。仅用于设置页提示，不阻止保存。</summary>
    public static IReadOnlyList<HotkeyGestureGroup> GetConflicts(IReadOnlyDictionary<string, HotkeyGesture>? bindings)
        => GroupByGesture(bindings).Where(g => g.IsConflict).ToList();
}
