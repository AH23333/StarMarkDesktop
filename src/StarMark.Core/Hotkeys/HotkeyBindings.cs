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

    /// <summary>VK_F1：截图默认键（D3 裁决"默认键与 Snipaste 一致"）。</summary>
    public const uint VirtualKeyF1 = 0x70;

    /// <summary>VK_F3 / VK_F4：贴图与"显示·隐藏所有贴图"的默认键，同样取自 Snipaste。</summary>
    public const uint VirtualKeyF3 = 0x72;
    public const uint VirtualKeyF4 = 0x73;

    /// <summary>
    /// VK_F5：贴图鼠标穿透的默认键。Snipaste 把这项放在贴图窗的右键菜单里，
    /// 但桌面贴图一旦穿透就收不到任何点击，"只能靠托盘关掉全部"是不可接受的出口（发起人 2026-09-24 反馈），
    /// 所以这里给一条全局键：不必知道哪张被穿透了，按一下就把它们全部交还鼠标。F5 不与 Snipaste 的键位相冲。
    /// </summary>
    public const uint VirtualKeyF5 = 0x74;

    /// <summary>VK_D：屏幕画布模式的默认键（规格 §16.5 的 Ctrl+Alt+D）。</summary>
    public const uint VirtualKeyD = 0x44;

    /// <summary>
    /// 画布内那批动作的默认键（A–Z 的虚拟键码就是其 ASCII 码，这里逐个写出来是为了让
    /// <see cref="Defaults"/> 那一段能读出"这条字母对应哪件事"）。
    /// <para>
    /// <b>这批全部带 Ctrl+Alt</b>：不带修饰键的裸键在穿透态下必须留给下层应用（用户要选文本、要翻页），
    /// 而画布吃下 Ctrl+Alt+字母几乎不与人撞——唯一常见的是 Ctrl+Alt+T（某些终端/显卡面板用它），
    /// 撞上时按批次 KL 的口径"只提示、不阻碍注册"，用户在设置页里一键改掉即可。
    /// </para>
    /// </summary>
    public const uint VirtualKeyT = 0x54, VirtualKeyP = 0x50, VirtualKeyH = 0x48,
        VirtualKeyR = 0x52, VirtualKeyU = 0x55, VirtualKeyC = 0x43, VirtualKeyS = 0x53,
        VirtualKeyK = 0x4B, VirtualKeyG = 0x47, VirtualKeyY = 0x59, VirtualKeyW = 0x57;

    /// <summary>
    /// 出厂默认：主界面呼出/关闭 = Ctrl+Alt+Space。
    /// 每次调用给出**新的可写字典**，调用方可以就地叠加而不污染默认表。
    /// </summary>
    public static Dictionary<string, HotkeyGesture> Defaults()
    {
        // 画布那批共用一档修饰键：Ctrl+Alt（NoRepeat＝按住不放不连发，画布切换类动作连发会来回抖）
        var canvasMods = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat;
        return new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, VirtualKeySpace),
            // 截图/贴图/隐藏贴图/穿透用裸 F1/F3/F4/F5（前三条与 Snipaste 默认一致，无修饰键）。
            // 与主界面呼出键不冲突：那条是 Ctrl+Alt+Space。装了 Snipaste 的机器上会撞，
            // 但批次 KL 已把冲突改成"只提示、不阻碍注册"，用户在设置里改一键即可，不必先关别的产品。
            [HotkeyActions.ScreenCapture] = new HotkeyGesture(HotkeyModifiers.NoRepeat, VirtualKeyF1),
            [HotkeyActions.ScreenPin] = new HotkeyGesture(HotkeyModifiers.NoRepeat, VirtualKeyF3),
            [HotkeyActions.ScreenPinToggleHidden] = new HotkeyGesture(HotkeyModifiers.NoRepeat, VirtualKeyF4),
            // 鼠标穿透**必须有全局键可以退出来**：穿透中的贴图收不到任何鼠标消息，
            // 那时它的右键菜单、托盘勾选项之外的操作路径全断（发起人真机反馈：只能靠托盘关掉全部）。
            // 所以给 F5——用户不需要知道是哪张被穿透了，按一下把这一组全部交还鼠标（见 VirtualKeyF5 注释）。
            [HotkeyActions.ScreenPinClickThrough] = new HotkeyGesture(HotkeyModifiers.NoRepeat, VirtualKeyF5),
            // 画布用 Ctrl+Alt+D（规格 §16.5 的默认键）。不占裸 F6：那一段留给截图那条链继续加动作，
            // 而画布是"进出一次用一下"的模式，带修饰键反而不容易与游戏/放映软件撞。
            [HotkeyActions.CanvasToggle] = new HotkeyGesture(canvasMods, VirtualKeyD),
            // 画布内那批（发起人点名"全部要，带修饰键"）：字母按"这件事叫什么"取，
            // 比让用户记住"第三个键是橡皮"可行得多——工具条上那颗「⌨」按钮随时能把这张表调出来。
            [HotkeyActions.CanvasClickThrough] = new HotkeyGesture(canvasMods, VirtualKeyT),   // Through
            [HotkeyActions.CanvasPen] = new HotkeyGesture(canvasMods, VirtualKeyP),            // Pen
            [HotkeyActions.CanvasHighlighter] = new HotkeyGesture(canvasMods, VirtualKeyH),    // Highlighter
            [HotkeyActions.CanvasEraser] = new HotkeyGesture(canvasMods, VirtualKeyR),         // eRaser
            // 白板底用 W（Whiteboard）。它与「交出鼠标」同为"这块玻璃的归属/背景"那一类，
            // 不跟在三支笔后面：按它换的是底下那块背景，不是手上拿哪支笔。
            [HotkeyActions.CanvasBoard] = new HotkeyGesture(canvasMods, VirtualKeyW),         // Whiteboard
            [HotkeyActions.CanvasUndo] = new HotkeyGesture(canvasMods, VirtualKeyU),           // Undo
            // 重做用 Y 而不是"U 加 Shift"：全局热键的修饰键组合要能被 RegisterHotKey 表达，
            // 而 Y 是"重做"在 Windows 上的第二个惯用键（Ctrl+Y），又不与画布那批里任何一颗撞。
            [HotkeyActions.CanvasRedo] = new HotkeyGesture(canvasMods, VirtualKeyY),           // Windows 上 redo 的惯用键
            [HotkeyActions.CanvasClear] = new HotkeyGesture(canvasMods, VirtualKeyC),          // Clear
            [HotkeyActions.CanvasSave] = new HotkeyGesture(canvasMods, VirtualKeyS),           // Save
            [HotkeyActions.CanvasCopy] = new HotkeyGesture(canvasMods, VirtualKeyK),           // ...K（C 被清屏占了）
            [HotkeyActions.CanvasPin] = new HotkeyGesture(canvasMods, VirtualKeyG),            // Graphic 上屏
            // 识字刻意**不占默认键**：它比截图低频，而入口已有三处（动作条「识字」、贴图工具条、托盘），
            // 少占一个全局键位就少一处"与别的软件撞键"的可能（同 P-76 的口径）。
        };
    }

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
