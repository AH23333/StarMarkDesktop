#nullable enable
using System;

namespace StarMark.Core.Appearance;

/// <summary>
/// 设置页「外观 + 边缘磁吸」那三根滑杆的<b>量程、步进与默认值的唯一主人</b>（批次 SD，P-123 清单 #4）。
/// <para>
/// <b>收口前这里有 3～5 份抄本</b>：仓储读档夹一次、落盘再夹一次（同一对数字写两遍），
/// 设置页 VM 的字段初值一遍、读取兜底又一遍，XAML 那三根滑杆的 <c>Minimum/Maximum/StepFrequency</c> 第三遍；
/// 不透明度的默认 0.72 除住在 <c>WidgetAppearance</c> 外，VM 里还直接写了两次字面量。
/// </para>
/// <para>
/// <b>为什么这不只是重复</b>：这几处的读数会同时出现在屏幕上（滑杆位置 + 右侧百分比/像素文本）与真实行为里
/// （组件实际透明度、吸附手感）。任何一份抄本先动，就会出现"<b>设置页显示的数 ≠ 组件在用的数</b>"，
/// 或者"存档里合法且在生效的值在设置页<b>表达不出来</b>"——后者批次 SA 已经在性能档位上真咬过一次（#175），
/// 这批的三根条子今天逐条对过、量程两边一致，所以这条是<b>防下一次</b>，不是修已发生的错。
/// </para>
/// <para>
/// <b>单位口径</b>：这三颗默认值是<b>逻辑像素（DIP）/ 无量纲比例</b>，因为它们是"用户调的旋钮"。
/// <see cref="StarMark.Core.Widgets.WidgetSnapCalculator"/> 里的 <c>Default*</c> 同名的那几颗是<b>物理像素</b>，
/// 只作求解器省略实参时的兜底——两者今天数值相同，但换屏幕缩放比就会不同步，所以各自有主人（旧写法把 DIP 的默认
/// 寄在物理像素那颗上，是在赌 scale 恒为 1）。
/// </para>
/// </summary>
public static class AppearanceSettingsPolicy
{
    // ─────────────── 不透明度（组件与主窗口共用同一量程，批次 J 拆的是值不是范围） ───────────────

    /// <summary>最透的一档（0.3＝还能认出文字轮廓，再低就等于把组件隐藏了）。</summary>
    public const double OpacityFloor = 0.3;

    /// <summary>完全不透明。</summary>
    public const double OpacityCeiling = 1.0;

    /// <summary>没存过、或读档抛异常时的不透明度。<b>VM 初值、VM 兜底、仓储兜底三处都取这颗</b>。</summary>
    public const double OpacityDefault = 0.72;

    /// <summary>不透明度滑杆步进（2%）。</summary>
    public const double OpacityStep = 0.02;

    // ─────────────── 磁吸强度（进入吸附阈值，DIP） ───────────────

    /// <summary>磁吸强度下限：再小就基本吸不住了，所以滑杆也不许指到 4 以下。</summary>
    public const int SnapStrengthFloor = 4;

    /// <summary>磁吸强度上限（DIP）。</summary>
    public const int SnapStrengthCeiling = 64;

    /// <summary>没存过时的磁吸强度（DIP）。</summary>
    public const int SnapStrengthDefault = 24;

    /// <summary>磁吸强度滑杆步进（像素）。</summary>
    public const int SnapStrengthStep = 2;

    // ─────────────── 对齐间距（贴合后保留的缝隙，DIP） ───────────────

    /// <summary>间距下限 0＝边边紧贴，这是有意开放的一档，不许被"至少留点缝"顶掉。</summary>
    public const int SnapSpacingFloor = 0;

    /// <summary>对齐间距上限（DIP）。</summary>
    public const int SnapSpacingCeiling = 40;

    /// <summary>没存过时的对齐间距（DIP）。</summary>
    public const int SnapSpacingDefault = 8;

    /// <summary>对齐间距滑杆步进（像素）。</summary>
    public const int SnapSpacingStep = 1;

    // ─────────────── 材质浓度（没有滑杆，只有默认；仓储兜底与界面算法共用） ───────────────

    /// <summary>毛玻璃材质的默认浓度。存过一次就以存档为准，这颗只管"从未设过"与"读失败"。</summary>
    public const double MaterialIntensityDefault = 0.65;

    /// <summary>
    /// 材质浓度的合法区间。<b>它同时是 <c>WidgetMaterialVisualCalculator</c> 自己那两颗同常数的出处</b>
    /// （浓度没有滑杆，但"仓储夹一次、落盘再夹一次、算法里再夹一次"是同一对数字写三遍的病）。
    /// </summary>
    public const double MaterialIntensityFloor = 0.0;

    /// <summary>材质浓度上限（1＝完全不透色，染料的性格就没了）。</summary>
    public const double MaterialIntensityCeiling = 1.0;

    /// <summary>把不透明度夹进合法范围（<b>读档与落盘同一颗</b>；各写一次 <c>Math.Clamp</c> 就是两份真值）。</summary>
    public static double NormalizeOpacity(double opacity) => Math.Clamp(opacity, OpacityFloor, OpacityCeiling);

    /// <summary>把磁吸强度夹进合法范围（DIP）。</summary>
    public static int NormalizeSnapStrength(int px) => Math.Clamp(px, SnapStrengthFloor, SnapStrengthCeiling);

    /// <summary>把对齐间距夹进合法范围（DIP）。</summary>
    public static int NormalizeSnapSpacing(int px) => Math.Clamp(px, SnapSpacingFloor, SnapSpacingCeiling);

    /// <summary>把材质浓度夹进合法范围。</summary>
    public static double NormalizeMaterialIntensity(double intensity)
        => Math.Clamp(intensity, MaterialIntensityFloor, MaterialIntensityCeiling);
}
