#nullable enable
namespace StarMark.Abstractions;

/// <summary>
/// 半透明材质（macOS 风格观感来源）。下沉到 Abstractions 以便 Core 的组件配置也能引用（避免 Core → UI 反向依赖）。
/// <para>
/// 六种材质与 DeskBox 的 Mica / MicaAlt / Acrylic(Thin) / AcrylicBase(Base) / Solid 一一对应，
/// 走 DesktopAcrylicController / MicaController 的原生系统背景方案（不再用实色盖住 SystemBackdrop）；
/// <see cref="Solid"/> 照搬 DeskBox：不挂任何控制器，改由内容表面铺一层「强调色实色」。
/// 新增成员一律追加新的整数值，旧配置（0/1/2）语义不变，无需迁移。
/// </para>
/// </summary>
public enum WidgetBackdropKind
{
    /// <summary>桌面亚克力（薄 / Thin）：毛玻璃 + 桌面色调，最通透、最有苹果味。</summary>
    Acrylic = 0,
    /// <summary>云母（Mica / Base）：更克制的高级铉光，适合大面积窗口。</summary>
    Mica = 1,
    /// <summary>实色（Solid）：不启用任何材质控制器，完全跟主题走，最省资源。</summary>
    None = 2,
    /// <summary>云母（Alt / BaseAlt）：比 Base 更明显地取桌面壁纸色调，适合层次偏后的窗口。</summary>
    MicaAlt = 3,
    /// <summary>桌面亚克力（厚 / Base）：比 Thin 更浓的霜化，隐私性更好。</summary>
    AcrylicBase = 4,
    /// <summary>
    /// 纯色（Solid，照搬 DeskBox）：不启用任何材质控制器，窗口背景由内容表面铺一层
    /// 「主题基色 + 强调色」的实色（WidgetMaterialVisualCalculator.BuildContentSolidSurfaceColor）。
    /// 与 <see cref="None"/> 的区别：None 是纯跟主题的黑白实色（最省资源、不吃不透明度），
    /// Solid 则是带强调色倾向的实色，且**吃「背景不透明度」**、不吃「材质浓度」（与 DeskBox 的
    /// SupportsWidgetOpacity / SupportsMaterialIntensity 判定一致）。
    /// </summary>
    Solid = 5,
}
