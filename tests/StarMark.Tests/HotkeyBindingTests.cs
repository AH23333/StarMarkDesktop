#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 快捷键绑定的合并 / 归并契约。
/// <para>
/// 这批规则原本住在 <c>StarMark.UI</c>（<c>SettingsStore.GetHotkeyBindings</c> 与
/// <c>HotkeyService.ApplyBindings</c>/GetConflicts 各写一遍"按手势归并"），而 UI 工程进不了
/// 单测工程 ⇒ 批次 JB 修掉的那条真因（用户清掉的快捷键被默认值复活）当时<b>没有任何可机检护栏</b>，
/// 全靠真机验收。把规则下沉到 Core 之后，这里把它逐条钉住。
/// </para>
/// </summary>
public sealed class HotkeyBindingTests
{
    private static HotkeyGesture CtrlAltSpace => new(CtrlAlt | HotkeyModifiers.NoRepeat, HotkeyBindings.VirtualKeySpace);
    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    // ===== 默认 + 已保存的合并（JB 的真因） =====

    /// <summary>
    /// 「清掉了某个快捷键」必须能压住默认值：已保存的空手势算一次覆盖，而不是"这条没设过"。
    /// 一旦有人把它改回"删键"，Ctrl+Alt+Space 会在下次读取复活 ⇒ 界面显示未设置、热键却仍注册着。
    /// </summary>
    [Fact]
    public void Merge_SavedEmptyGesture_SuppressesDefault()
    {
        var cleared = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0),
        };

        var merged = HotkeyBindings.MergeWithDefaults(cleared);

        Assert.True(merged.ContainsKey(HotkeyActions.MainToggle));   // 键还在（这就是"显式清过"的记号）
        var g = merged[HotkeyActions.MainToggle];
        Assert.True(g.IsEmpty);
        Assert.Equal(0u, g.VirtualKey);
    }

    /// <summary>从没设过这个动作 ⇒ 默认值仍然生效（与上一条合起来才叫"覆盖 vs 缺省"可分辨）。</summary>
    [Fact]
    public void Merge_MissingSavedKey_KeepsDefault()
    {
        var merged = HotkeyBindings.MergeWithDefaults(new Dictionary<string, HotkeyGesture>());

        Assert.Equal(CtrlAltSpace.Modifiers, merged[HotkeyActions.MainToggle].Modifiers);
        Assert.Equal(CtrlAltSpace.VirtualKey, merged[HotkeyActions.MainToggle].VirtualKey);
    }

    /// <summary>已保存的非空手势整条替换默认（修饰键与主键一起换，不做逐位或运算）。</summary>
    [Fact]
    public void Merge_SavedGesture_ReplacesDefaultEntirely()
    {
        var saved = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(HotkeyModifiers.Control, 0x41),   // Ctrl+A
        };

        var merged = HotkeyBindings.MergeWithDefaults(saved);

        Assert.Equal(HotkeyModifiers.Control, merged[HotkeyActions.MainToggle].Modifiers);
        Assert.Equal(0x41u, merged[HotkeyActions.MainToggle].VirtualKey);
    }

    /// <summary>用户自定的动作 id（布局方案是动态生成的）也必须原样保留下来。</summary>
    [Fact]
    public void Merge_KeepsSavedActionsThatDefaultsDoNotHave()
    {
        var saved = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.LayoutApply("plan-a")] = new HotkeyGesture(HotkeyModifiers.Shift, 0x70),
        };

        var merged = HotkeyBindings.MergeWithDefaults(saved);

        Assert.True(merged.ContainsKey(HotkeyActions.LayoutApply("plan-a")));
        Assert.True(merged.ContainsKey(HotkeyActions.MainToggle));   // 默认没被"整表替换"掉
    }

    /// <summary>
    /// 合并的写法是"取默认表 ⇒ 就地叠加"，所以默认表<b>每次都得是新的</b>：
    /// 若哪天有人把 <see cref="HotkeyBindings.Defaults"/> 改成 static readonly 缓存，
    /// 第一个用户的保存值就会永久污染后面每一次读取（重启前看不出，重启后诡异复活/消失）。
    /// </summary>
    [Fact]
    public void Merge_OverlaysOntoAFreshDefaultsCopy_EveryCall()
    {
        var saved = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(HotkeyModifiers.Windows, 0x42),   // Win+B
        };

        var first = HotkeyBindings.MergeWithDefaults(saved);
        Assert.NotSame(first, HotkeyBindings.MergeWithDefaults(saved));
        Assert.Equal(0x42u, first[HotkeyActions.MainToggle].VirtualKey);

        var second = HotkeyBindings.MergeWithDefaults(saved);
        Assert.Equal(0x42u, second[HotkeyActions.MainToggle].VirtualKey);

        // 默认表本身既没被写脏、也不共享实例
        var defaults = HotkeyBindings.Defaults();
        Assert.NotSame(defaults, HotkeyBindings.Defaults());
        Assert.Equal(CtrlAltSpace.VirtualKey, defaults[HotkeyActions.MainToggle].VirtualKey);
    }

    /// <summary>绑定 JSON 损坏 ⇒ 读取侧传 null ⇒ 整表按默认，而不是崩溃或清空。</summary>
    [Fact]
    public void Merge_NullSaved_FallsBackToDefaults()
    {
        var merged = HotkeyBindings.MergeWithDefaults(null);

        Assert.Equal(CtrlAltSpace.VirtualKey, merged[HotkeyActions.MainToggle].VirtualKey);
    }

    /// <summary>磁盘上被手改出来的显式 <c>null</c> 值：当作没设过，不能让下游按非空假设取值时崩。</summary>
    [Fact]
    public void Merge_IgnoresExplicitNullValues()
    {
        var saved = new Dictionary<string, HotkeyGesture> { [HotkeyActions.MainToggle] = null! };

        var merged = HotkeyBindings.MergeWithDefaults(saved);

        Assert.Equal(CtrlAltSpace.VirtualKey, merged[HotkeyActions.MainToggle].VirtualKey);
    }

    /// <summary>
    /// 出厂默认是用户点名要的产品事实（"主界面切换默认 Ctrl+Alt+Space"），钉住它而不是靠读代码确认。
    /// 虚拟键码写死为 Win32 VK_SPACE = 0x20。
    /// </summary>
    [Fact]
    public void Defaults_MainToggle_IsCtrlAltSpace()
    {
        var g = HotkeyBindings.Defaults()[HotkeyActions.MainToggle];

        Assert.True(g.Modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.True(g.Modifiers.HasFlag(HotkeyModifiers.Alt));
        Assert.True(g.Modifiers.HasFlag(HotkeyModifiers.NoRepeat));
        Assert.Equal(0x20u, g.VirtualKey);
        Assert.Equal(0x4003u, (uint)g.Modifiers);
    }

    /// <summary>
    /// 空手势必须能原样走完"序列化 ⇒ 落盘 ⇒ 读回"，否则上面第一条在磁盘上就不成立。
    /// <see cref="HotkeyGesture"/> 之所以是可变类而不是 record：STJ 反序列化 record 时填不回主键。
    /// </summary>
    [Fact]
    public void EmptyGesture_SurvivesJsonRoundTrip()
    {
        var bindings = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0),
            [HotkeyActions.MainShow] = CtrlAltSpace,
        };

        var back = JsonSerializer.Deserialize<Dictionary<string, HotkeyGesture>>(
            JsonSerializer.Serialize(bindings))!;

        Assert.True(back[HotkeyActions.MainToggle].IsEmpty);
        Assert.Equal(CtrlAltSpace.VirtualKey, back[HotkeyActions.MainShow].VirtualKey);
        Assert.Equal(CtrlAltSpace.Modifiers, back[HotkeyActions.MainShow].Modifiers);
    }

    // ===== 手势归并（注册表与冲突提示共用同一份规则） =====

    /// <summary>空手势一律不注册；同一手势的多个动作合成一个注册单元；组的顺序＝手势首次出现的顺序。</summary>
    [Fact]
    public void GroupByGesture_SkipsEmpty_MergesAndKeepsFirstSeenOrder()
    {
        var shiftF5 = new HotkeyGesture(HotkeyModifiers.Shift, 0x74);
        var bindings = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = CtrlAltSpace,
            [HotkeyActions.MainShow] = shiftF5,
            [HotkeyActions.MainHide] = CtrlAltSpace,
            [HotkeyActions.WidgetsShowAll] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0),
        };

        var groups = HotkeyBindings.GroupByGesture(bindings);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { HotkeyActions.MainToggle, HotkeyActions.MainHide }, groups[0].Actions);
        Assert.Equal(new[] { HotkeyActions.MainShow }, groups[1].Actions);
        Assert.True(groups[0].IsConflict);
        Assert.False(groups[1].IsConflict);
    }

    /// <summary>
    /// 归并键把 NoRepeat 算进身份（掩码 0x400F），因为 <c>RegisterHotKey</c> 的键身份就是
    /// 修饰键 + 主键。本项目录制与默认值一律带 NoRepeat ⇒ 现网不会出现"只差 NoRepeat"的两条；
    /// 这条断言是给"哪天有人不再统一加 NoRepeat"时报警用的（那时它们会各自注册成两个热键）。
    /// </summary>
    [Fact]
    public void GestureKey_TreatsNoRepeatAsPartOfTheIdentity()
    {
        const uint ctrlAlt = (uint)CtrlAlt;

        Assert.Equal("16387:32", HotkeyGesture.GestureKey(CtrlAltSpace));
        Assert.Equal($"{ctrlAlt}:32", HotkeyGesture.GestureKey(new HotkeyGesture(CtrlAlt, 0x20)));
        Assert.NotEqual(
            HotkeyGesture.GestureKey(CtrlAltSpace),
            HotkeyGesture.GestureKey(new HotkeyGesture(CtrlAlt, 0x20)));
    }

    /// <summary>冲突＝一个手势挂了多个动作；只报冲突组，且空手势不参与（否则"未设置"会被当成彼此的冲突）。</summary>
    [Fact]
    public void GetConflicts_ReportsOnlySharedNonEmptyGestures()
    {
        var bindings = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = CtrlAltSpace,
            [HotkeyActions.WidgetsHideAll] = CtrlAltSpace,
            [HotkeyActions.MainShow] = new HotkeyGesture(HotkeyModifiers.Shift, 0x43),
            [HotkeyActions.MainHide] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0),
            [HotkeyActions.WidgetsToggleAll] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0),
        };

        var conflicts = HotkeyBindings.GetConflicts(bindings);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(CtrlAltSpace.VirtualKey, conflict.Gesture.VirtualKey);
        Assert.Equal(2, conflict.Actions.Count);
        Assert.Empty(HotkeyBindings.GetConflicts(new Dictionary<string, HotkeyGesture>()));
        Assert.Empty(HotkeyBindings.GetConflicts(null));
    }

    /// <summary>
    /// 动作目录由组件注册表派生（托盘子菜单那次 P-62b 的同一形状）：新增一种组件后，
    /// 它的显示/隐藏/切换动作与分类要自动出现，别让人去补一份手写清单。
    /// </summary>
    [Fact]
    public void HotkeyActions_CoverEveryRegisteredWidgetKind()
    {
        var all = HotkeyActions.All();
        var order = HotkeyActions.CategoryOrder;

        foreach (var kind in WidgetStorage.AllKinds)
        {
            var title = WidgetStorage.KindTitle(kind);
            Assert.Contains(HotkeyActions.WidgetToggle(kind), all);
            Assert.Contains(HotkeyActions.WidgetCreate(kind), all);
            Assert.Contains(HotkeyActions.WidgetShow(kind), all);
            Assert.Contains(HotkeyActions.WidgetHide(kind), all);
            Assert.Equal(title, HotkeyActions.CategoryOf(HotkeyActions.WidgetToggle(kind)));
            Assert.Contains(title, order);
        }
    }

    /// <summary>布局被删掉后它的动作还在磁盘上：展示名要说明"已删除"，而不是回显裸 id 或空串。</summary>
    [Fact]
    public void DisplayName_ForMissingLayout_SaysItWasDeleted()
    {
        Assert.Equal("切换布局（已删除）", HotkeyActions.DisplayName(HotkeyActions.LayoutApply("gone")));
        Assert.Equal("切换布局 · 工作", HotkeyActions.DisplayName(
            HotkeyActions.LayoutApply("id-1"),
            new List<WidgetLayout> { new WidgetLayout { Id = "id-1", Name = "工作" } }));
    }

    // ===== 修饰键键码表（钩子累计、系统状态兜底、录制判定三方共用） =====

    /// <summary>
    /// Win32 对同一个修饰键给三种码（通用 + 左 + 右），而键盘钩子实际送左右码、
    /// GetAsyncKeyState 三种都应答 ⇒ 少认一个就会出现"按住左 Ctrl 录制却没记到 Ctrl 位"。
    /// </summary>
    [Theory]
    [InlineData(0x11, HotkeyModifiers.Control)]
    [InlineData(0xA2, HotkeyModifiers.Control)]
    [InlineData(0xA3, HotkeyModifiers.Control)]
    [InlineData(0x12, HotkeyModifiers.Alt)]
    [InlineData(0xA4, HotkeyModifiers.Alt)]
    [InlineData(0xA5, HotkeyModifiers.Alt)]
    [InlineData(0x10, HotkeyModifiers.Shift)]
    [InlineData(0xA0, HotkeyModifiers.Shift)]
    [InlineData(0xA1, HotkeyModifiers.Shift)]
    [InlineData(0x5B, HotkeyModifiers.Windows)]
    [InlineData(0x5C, HotkeyModifiers.Windows)]
    public void ModifierCodes_MapToTheirBit(int vk, HotkeyModifiers expected)
    {
        Assert.Equal(expected, HotkeyKeys.ModifierOf((uint)vk));
        Assert.True(HotkeyKeys.IsModifier((uint)vk));
    }

    /// <summary>主键绝不能被认成修饰键（Space 与字母是最常见的主键）。</summary>
    [Theory]
    [InlineData(0x20)]   // Space
    [InlineData(0x41)]   // A
    [InlineData(0x70)]   // F1
    public void MainKeys_AreNotModifiers(int vk)
        => Assert.False(HotkeyKeys.IsModifier((uint)vk));

    /// <summary>
    /// "哪个虚拟键码属于哪个修饰位"必须是同一张表的两个视图：任一处漏改，
    /// 兜底读系统状态与录制判定就会给出互相矛盾的答案。
    /// </summary>
    [Fact]
    public void CodesOf_And_ModifierOf_AgreeOnEveryBit()
    {
        foreach (var bit in HotkeyKeys.ModifierBits)
        {
            var codes = HotkeyKeys.CodesOf(bit);
            Assert.NotEmpty(codes);
            foreach (var vk in codes) Assert.Equal(bit, HotkeyKeys.ModifierOf(vk));
        }

        // 通用码排在前（逐码探测时先问一次通用码即可命中多数情况）；Win 键没有通用码，只有左右两个
        Assert.Equal(0x11u, HotkeyKeys.CodesOf(HotkeyModifiers.Control)[0]);
        Assert.Equal(new uint[] { 0x5B, 0x5C }, HotkeyKeys.CodesOf(HotkeyModifiers.Windows));

        // 组合入参给出并集；NoRepeat 不是按键，没有对应键码
        Assert.Equal(6, HotkeyKeys.CodesOf(HotkeyModifiers.Control | HotkeyModifiers.Alt).Count);
        Assert.Empty(HotkeyKeys.CodesOf(HotkeyModifiers.NoRepeat));
    }

    // ===== Win32 错误码 → 给人看的原因 =====

    /// <summary>1409 是"已被别的程序占用"，可直接说成事实；其它码不编造解释。</summary>
    [Fact]
    public void RegisterFailure_NamesTheOccupiedCase_AndKeepsOtherCodesRaw()
    {
        Assert.Equal("该组合键已被其它程序占用", HotkeyErrorText.RegisterFailure(1409));
        Assert.Equal("系统拒绝注册（Win32 错误 5）", HotkeyErrorText.RegisterFailure(5));
    }

    /// <summary>
    /// 无论认不认识这个码，文案都必须带编号：用户拿着编号能查到根因，只看到"可能失败"就只能挨个猜。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(1401)]
    [InlineData(9999)]
    public void HookInstallFailure_AlwaysCarriesTheCodeNumber(int code)
        => Assert.Contains(code.ToString(), HotkeyErrorText.HookInstallFailure(code));
}
