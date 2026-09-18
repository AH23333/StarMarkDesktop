#nullable enable
namespace StarMark.Abstractions;

/// <summary>半透明材质（macOS 风格观感来源）。下沉到 Abstractions 以便 Core 的组件配置也能引用（避免 Core → UI 反向依赖）。</summary>
public enum WidgetBackdropKind
{
    /// <summary>桌面亚克力：毛玻璃 + 桌面色调，最有苹果味。</summary>
    Acrylic = 0,
    /// <summary>云母：更克制的高级铉光。</summary>
    Mica = 1,
    /// <summary>不透明：完全跟主题走，最省资源。</summary>
    None = 2,
}
