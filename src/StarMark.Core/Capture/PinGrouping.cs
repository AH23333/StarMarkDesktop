namespace StarMark.Core.Capture;

/// <summary>
/// 贴图组（成组显示 / 隐藏 / 穿透）里<b>能被机器检的那部分判据</b>：命名、上限、聚合态、
/// 菜单标签的方向措辞、以及托盘命令号的编解码。
/// <para>
/// 这里<b>没有</b>组的状态字段——一张贴图"有没有被收起、穿不穿透"只有一个真值，就是它自己那扇窗；
/// 组只是"哪几张"的一份名册。一旦在这里存一份 <c>Hidden</c>，就会出现
/// "组说已隐藏、里面那张其实还在屏幕上"（名册与窗体两份账的经典分岔，批次 WO/WD-7 同一族）。
/// </para>
/// <para>
/// 标签为什么也放这里：一个组里全部收起时那条菜单要写「显示这一组」，只要有一张没收齐就要写
/// 「隐藏这一组」——这个方向如果留在 UI 里，接线时写反是必然风险（WF-1 的教训：布尔参数的含义只写在
/// 被调方时，调用点写反既编译得过、也过得了"看起来在测它"的闸门）。所以方向判据在这里，UI 只拼字符串。
/// </para>
/// </summary>
public static class PinGrouping
{
    /// <summary>一屏最多几组。再多一组，托盘那棵子菜单就长到找不着目标组了（上限是给"看一眼就点到"用的）。</summary>
    public const int MaxGroups = 8;

    /// <summary>组名的前缀。序号只增不复用：删掉「组 1」后再新建一组叫「组 3」，不会出现两个"组 1"。</summary>
    public const string NamePrefix = "组 ";

    /// <summary>托盘里组命令的号段起点。宿主自带的那些 tag 都在 1..99，永不与这里重叠。</summary>
    public const int TagBase = 1000;

    /// <summary>每个组占用的号位数量（三个动作 + 一个余量位，余量留给以后加"这组置顶"）。</summary>
    public const int TagStride = 4;

    /// <summary>对一个组能做的三件事。数值进了托盘命令号，<b>只能追加、不得重排</b>。</summary>
    public enum Action
    {
        /// <summary>显示 / 收起这一组（方向由聚合态决定，见 <see cref="HideLabel"/>）。</summary>
        Show = 0,
        /// <summary>切换这一组的鼠标穿透。</summary>
        Through = 1,
        /// <summary>关掉这一组的所有贴图。</summary>
        Close = 2,
    }

    /// <summary>还能不能再分一组。不能时要说清"为什么不行 + 怎么办"，而不是把那颗按钮悄悄禁掉。</summary>
    public static string? LimitProblem(int currentGroups)
        => currentGroups >= MaxGroups
            ? $"最多分 {MaxGroups} 组，先关掉一组（托盘「贴图组」里那条「关闭这组」）"
            : null;

    /// <summary>第 <paramref name="serial"/> 号组的名字。</summary>
    public static string NameOf(int serial) => NamePrefix + serial;

    /// <summary>组标签：名字后面永远带成员数——"组 2"看不出是几张，用户就不敢点关闭。</summary>
    public static string GroupLabel(string name, int members) => $"{name}（{members} 张）";

    /// <summary>
    /// 聚合态：组里<b>每一张</b>都处于该状态才算这个状态成立。
    /// <para>空组一律返回 <b>false</b>：<c>All()</c> 对空序列返回 true，那会让"关闭这一组"之后残留的
    /// 空名册在托盘上显示成「显示这一组」——一个没有任何贴图可显示的假出口（三臂单测钉住这一条）。</para>
    /// </summary>
    public static bool AllIn(IReadOnlyList<bool> flags) => flags.Count > 0 && AllTrue(flags);

    private static bool AllTrue(IReadOnlyList<bool> flags)
    {
        for (var i = 0; i < flags.Count; i++)
            if (!flags[i]) return false;
        return true;
    }

    /// <summary>这一组是不是已经整组收起了 ⇒ 决定那条菜单写「显示」还是「隐藏」。</summary>
    public static string HideLabel(bool allHidden) => allHidden ? "显示这一组" : "隐藏这一组";

    /// <summary>这一组是不是已经整组穿透 ⇒ 同样只出措辞，不做决定。</summary>
    public static string ThroughLabel(bool allThrough) => allThrough ? "取消这组忽略鼠标" : "这组忽略鼠标";

    /// <summary>关闭那一条要带数量：关掉的是一张张真实的窗，不是"一个概念"。</summary>
    public static string CloseLabel(int members) => $"关闭这组（{members} 张）";

    /// <summary>
    /// 下一次点"显示 / 收起"要落到哪个状态。
    /// <para>单独摆出来是因为它和 <see cref="HideLabel"/> 必须<b>同向</b>：标签说「显示这一组」而实际执行成
    /// 再隐藏一次，就是"全绿而功能坏"（这条链上已经栽过三次）。</para>
    /// </summary>
    public static bool NextHidden(bool allHidden) => !allHidden;

    /// <summary>同上，穿透那一条的方向。</summary>
    public static bool NextThrough(bool allThrough) => !allThrough;

    /// <summary>把（组号，动作）编成一个托盘命令号。</summary>
    public static int TagOf(int serial, Action action) => TagBase + serial * TagStride + (int)action;

    /// <summary>命令号 → 组号。<b>不校验组在不在</b>：名册归 UI 管，这里只保证算术可逆。</summary>
    public static int SerialOf(int tag) => (tag - TagBase) / TagStride;

    /// <summary>命令号 → 动作。</summary>
    public static Action ActionOf(int tag) => (Action)((tag - TagBase) % TagStride);

    /// <summary>
    /// 这个 tag 是不是组命令。宿主自己那批 tag 全在 <see cref="TagBase"/> 以下，
    /// 而号段尾数只认三个动作 ⇒ 余量位与号段外的数一律不算（点错比点不动严重）。
    /// </summary>
    public static bool IsGroupTag(int tag)
        => tag >= TagBase && (tag - TagBase) % TagStride < (int)Action.Close + 1;

    /// <summary>成员空了就别再挂着这一组：空组在托盘里是一个只能"关闭"的假目标。</summary>
    public static bool ShouldDrop(int members) => members == 0;
}
