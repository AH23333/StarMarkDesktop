#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Core.Appearance;

namespace StarMark.UI.Helpers;

/// <summary>
/// 组件 / 主窗口「macOS 风」外观：材质（亚克力 / 云母 / 纯色 / 实色）+ 纯色背景不透明度。
/// <para>
/// <b>批次 K 架构收敛（为何不再用 <c>DesktopAcrylicController</c> / <c>MicaController</c>）：</b>
/// 未打包 WinUI3 下把控制器挂到窗口目标时，常出现「<c>AddSystemBackdropTarget</c> 返回 true 但 DWM 实际
/// 不合成霜层」——用户真机现象即「除纯色外所有材质全黑、两滑杆全无反应、薄=厚」。既然「纯色」用的
/// <c>TransparentTintBackdrop</c>（真·合成 CompositionColorBrush 背衬）在用户机上稳定可见，本批次把
/// <b>全部材质统一改走该已验证可靠的背衬画笔机制</b>：玻璃化整窗 + 铺一层按材质性格取色的扁平背衬，
/// 内容表面保持透明让背衬透出。于是每种材质在任何显示驱动 / 远程会话下都必然可见、互不相同、绝不全黑。
/// 代价：亚克力不再产生真实的采样模糊（变成带壁纸透出的半透明染色面板）。这是为「稳定可用」对
/// DeskBox 控制器方案的有意偏离；若日后转为打包（MSIX）应用，可再评估恢复真实模糊。
/// </para>
/// <para>
/// 「背景不透明度」自本批次起<b>仅作用于纯色（Solid）</b>——其余材质用各自固定的透明度性格，不受滑杆影响
/// （用户明确要求：放弃长期无法生效的滑杆联动）。「材质浓度」滑杆已整体删除。
/// </para>
/// </summary>
public static class WidgetAppearance
{
    // 以下 getter 会在组件窗口构造函数、主窗口刷新、拖放/缩放等高频路径被调用，
    // 一律不得向外抛异常：读取失败时用默认值兜底并写日志（外观退化总好过整个窗口创建失败导致崩溃）。

    /// <summary>
    /// 走 DI 里那个 <see cref="SettingsStore"/> 单例，而不是每次 <c>new SettingsStore()</c>：
    /// 该类的读缓存是<b>实例级</b>，冷实例每次都重新 File.ReadAllText + 整档反序列化。而这些 getter 里
    /// 最勤的 <see cref="SnapEnabled"/> 在缩放手势的<b>每次 PointerMoved</b> 上被调（60–120 Hz），
    /// 一次组件显示也会连着读 6–9 次。取不到（启动早期）时才退回一次性读取，语义不变。
    /// </summary>
    private static SettingsStore Store =>
        App.Services is { } sp
            ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetService<SettingsStore>(sp) ?? new SettingsStore()
            : new SettingsStore();

    /// <summary>拖动 / 缩放时的边缘磁吸总开关（关闭 = 用户自由摆位，不做任何自动贴合）。</summary>
    public static bool SnapEnabled() => Try(() => Store.LoadWidgetSnapEnabled(), true);

    /// <summary>磁吸对齐间距（<b>逻辑像素</b>）。量程与默认都住在 <see cref="AppearanceSettingsPolicy"/>；
    /// 调用方按屏幕缩放比换成物理像素后再交给求解器（<see cref="StarMark.Core.Widgets.WidgetSnapCalculator"/> 只吃物理像素）。</summary>
    public static int SnapSpacing() => Try(
        () => Store.LoadWidgetSnapSpacing(), AppearanceSettingsPolicy.SnapSpacingDefault);

    /// <summary>磁吸吸附强度＝进入吸附阈值（<b>逻辑像素</b>）。越大越早吸附。</summary>
    public static int SnapEngageThreshold() => Try(
        () => Store.LoadWidgetSnapStrength(), AppearanceSettingsPolicy.SnapStrengthDefault);

    public static WidgetBackdropKind Backdrop()
        => Try(() => Store.LoadWidgetBackdrop(), WidgetBackdropKind.Acrylic);

    public static double Opacity()
        => Try(() => Store.LoadWidgetOpacity(), AppearanceSettingsPolicy.OpacityDefault);

    /// <summary>
    /// 整窗共用的那颗<b>染料色</b>（主题色）：自定义 &gt; 系统强调色 &gt; 兜底蓝，判据只住在
    /// <see cref="AppPalette.ResolveAccent"/>（批次 WI-81：从前只有"系统／兜底"两种，用户裁要加一颗自定义）。
    /// <para>读设置档走 <see cref="ThemeAccent"/> 那颗缓存，⛔ 在这里直接读档——材质套用是启动头号嫌疑
    /// （日志里 5 秒内出现过 38 次），而这一颗在每次套材质、每次刷画笔上都要问。</para>
    /// </summary>
    public static Windows.UI.Color AccentColor() => Try(() =>
    {
        var settings = new Windows.UI.ViewManagement.UISettings();
        var c = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        return AppPalette.ResolveAccent(ThemeAccent.Custom, Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B));
    }, ThemeAccent.Custom ?? MaterialTint.FallbackAccent);

    /// <summary>
    /// 用户给这一扇<b>点了底色</b>、却<b>⛔ 自己点文本色</b>时，那一扇上的字该念哪一颗：
    /// 把那颗底色按那一档材质的透明度压到主题基色上，取与合成结果<b>对比度更高</b>的黑/白。
    /// <para>批次 WI-82（真机量到的那一格）：深色主题 + 点了白色＝<b>整扇内容从屏幕上消失</b>
    /// （现读 tint=#FFFFFFFF 实色，字仍是主题给的白字）。判据本身住在
    /// <see cref="SurfaceReadability.ReadableOn"/>（Core，可测），这里只做"取现场那几颗色"的 plumbing。</para>
    /// <para>交 null＝<b>不插手</b>：没人点底色时那一套字色是主题配的，本来就对；而他自己点了文本色，
    /// 就以他为准（调用点那三档优先级：他的文本色 &gt; 这颗自动色 &gt; 样式默认）。</para>
    /// </summary>
    public static Windows.UI.Color? AutoForeground(
        ElementTheme theme, WidgetBackdropKind kind, double solidOpacity, Windows.UI.Color? tint)
    {
        if (tint is null) return null;
        var dark = theme == ElementTheme.Dark;
        var surface = SurfaceReadability.Over(
            MaterialTint.BackdropColor(dark, AccentColor(), kind,
                AppearanceSettingsPolicy.NormalizeOpacity(solidOpacity), tint),
            SurfaceReadability.BaseFor(dark));
        return SurfaceReadability.ReadableOn(surface);
    }

    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex)
        {
            StarLog.Error("读取组件外观设置失败，已回退默认值", ex);
            return fallback;
        }
    }

    /// <summary>
    /// 每个窗口的背衬状态。统一用一层 <see cref="WinUIEx.TransparentTintBackdrop"/> 扁平背衬着色整窗，
    /// 按材质性格取不同颜色 / 固定透明度；不再持有或挂载任何系统背衬控制器。
    /// </summary>
    private sealed class WindowBackdropState
    {
        /// <summary>整窗扁平背衬画笔。null = 未挂载（实色 None 材质）。</summary>
        public WinUIEx.TransparentTintBackdrop? Tint;

        /// <summary>
        /// 当前是否已用背衬画笔接管整窗着色（除「实色 None」外的所有材质均为 true）。
        /// 为 true 时内容表面应保持透明，让这一层背衬单独着色——若再叠一层内容实色，
        /// 就会与背衬两次 alpha 叠加导致过实 / 发灰（正是旧「纯色失效」的一半成因）。
        /// </summary>
        public bool BackdropSurfaceActive;
    }

    private static readonly ConditionalWeakTable<Window, WindowBackdropState> _states = new();

    /// <summary>
    /// 把材质真正挂到窗口上（构造期、设置变更后、DWM 主题翻转后共用）。
    /// <para>
    /// 实色（None）：什么都不挂，不玻璃化，内容表面铺主题实色（见 <see cref="SurfaceBrush"/>）。
    /// 其余材质：玻璃化整窗 + 铺一层按材质取色的扁平背衬，内容表面保持透明让背衬透出。
    /// 「背景不透明度」(<paramref name="solidOpacity"/>) 只在纯色材质参与取色；其它材质用各自固定透明度。
    /// </para>
    /// <para>
    /// <paramref name="tintOverride"/>＝那一扇窗点定的底色（组件「外观」里那颗背景色，或将来主窗的同名出口）。
    /// <b>它只换 RGB，⛔ 换 alpha</b>——那一档材质的透明度性格由 <see cref="MaterialTint.BackdropAlpha"/> 单独决定，
    /// 两边都读同一颗 ⇒「选了透明材质再改颜色」得到的仍是透明材质，只是换了色（批次 WI-81 修的那条缺陷：
    /// 从前这里把自定义底色判成"强制实色 None"，透明效果当场丢掉）。
    /// </para>
    /// </summary>
    public static void ApplyBackdrop(
        Window window, WidgetBackdropKind kind, double solidOpacity, ElementTheme theme,
        Windows.UI.Color? tintOverride = null)
    {
        try
        {
            var isDark = theme == ElementTheme.Dark;

            var state = _states.GetOrCreateValue(window);

            if (kind == WidgetBackdropKind.None)
            {
                // 实色：关玻璃、摘背衬，内容表面铺不透明主题色，最省资源。
                state.BackdropSurfaceActive = false;
                ClearTintBackdrop(state, window);
                WindowInterop.SetDwmSystemBackdropNone(window);
                WindowInterop.ClearFullWindowFrame(window);
                WindowInterop.SetImmersiveDarkMode(window, isDark);
                return;
            }

            // 其余材质统一：按材质性格取扁平背衬色（纯色吃滑杆，其余固定），玻璃化整窗让背衬透出。
            var color = MaterialTint.BackdropColor(
                isDark, AccentColor(), kind, AppearanceSettingsPolicy.NormalizeOpacity(solidOpacity), tintOverride);

            if (state.Tint is null)
            {
                state.Tint = new WinUIEx.TransparentTintBackdrop(color);
                window.SystemBackdrop = state.Tint;
            }
            else
            {
                state.Tint.TintColor = color;
                if (!ReferenceEquals(window.SystemBackdrop, state.Tint))
                    window.SystemBackdrop = state.Tint;
            }

            // 整窗玻璃化让背衬铺满客户区；DWM 侧关掉自带背景（DWMSBT_NONE），避免再叠一层默认材质。
            WindowInterop.ApplyFullWindowFrame(window);
            WindowInterop.SetImmersiveDarkMode(window, isDark);
            WindowInterop.SetDwmSystemBackdropNone(window);
            state.BackdropSurfaceActive = true;

            // 节流：每次套材质都写一行 ⇒ 组件多 + 拖滑杆即时预览时成串刷屏（同类 5 s 窗口）。
            StarLog.InfoThrottled($"backdrop:{kind}:{isDark}",
                $"[材质诊断] backdrop kind={kind} dark={isDark} solidOpacity={NumberText.Fixed2(solidOpacity)} " +
                $"tint=#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}",
                windowMs: 5000);
        }
        catch (Exception ex)
        {
            StarLog.Error($"应用材质背衬失败 ({kind})", ex);
            try { window.SystemBackdrop = null; } catch { }
        }
    }

    /// <summary>
    /// 整窗背衬画笔当前是否真正挂在窗口上。为 true 时内容表面应保持透明，让扁平背衬单独着色，
    /// 避免与背衬叠两层 alpha。适用于除「实色 None」外的所有材质（含纯色 Solid）。
    /// </summary>
    public static bool IsBackdropSurfaceActive(Window window)
        => _states.TryGetValue(window, out var s)
           && s.BackdropSurfaceActive
           && s.Tint is not null
           && ReferenceEquals(window.SystemBackdrop, s.Tint);

    /// <summary>摘掉扁平背衬（切回实色，或窗口释放时调用）。</summary>
    private static void ClearTintBackdrop(WindowBackdropState state, Window window)
    {
        if (state.Tint is null) return;
        try { if (ReferenceEquals(window.SystemBackdrop, state.Tint)) window.SystemBackdrop = null; }
        catch { }
        state.Tint = null;
    }

    /// <summary>窗口关闭时彻底释放（把背衬从窗口摘掉，避免引用悬挂）。</summary>
    public static void ReleaseBackdrop(Window window)
    {
        if (!_states.TryGetValue(window, out var state)) return;
        try { if (ReferenceEquals(window.SystemBackdrop, state.Tint)) window.SystemBackdrop = null; }
        catch { }
        finally
        {
            state.Tint = null;
            state.BackdropSurfaceActive = false;
            _states.Remove(window);
        }
    }

    /// <summary>
    /// 组件 / 主窗口的内容表面画笔（两者共用同一套，保证「设置里怎么调、两边就怎么变」）。
    /// <para>
    /// 正常路径下背衬（<see cref="IsBackdropSurfaceActive"/>）会透出，调用方会把内容置透明；这里的画笔
    /// 是<b>背衬未生效时的实色兜底</b>（理论上扁平背衬总能挂上，兜底只为防御）：
    /// </para>
    /// <list type="bullet">
    /// <item>半透明材质（亚克力 / 云母 / 纯色）→ 与背衬<b>同源</b>的 <see cref="MaterialTint.BackdropColor"/>，
    ///     保证「万一没挂上背衬也仍是该材质应有的染色面板」，绝不全黑；</item>
    /// <item><see cref="WidgetBackdropKind.None"/> → 跟随主题的黑 / 白实色。</item>
    /// </list>
    /// </summary>
    public static Brush SurfaceBrush(ElementTheme theme, WidgetBackdropKind kind)
        => SurfaceBrush(theme, kind, null);

    /// <param name="opacityOverride">显式指定「背景不透明度」（仅纯色材质读取它）。</param>
    /// <param name="tintOverride">那一扇窗点定的底色；<b>只换 RGB⛔ 换那一档材质的 alpha</b>（同 <see cref="ApplyBackdrop"/>）。</param>
    public static Brush SurfaceBrush(
        ElementTheme theme, WidgetBackdropKind kind, double? opacityOverride, Windows.UI.Color? tintOverride = null)
    {
        var dark = theme == ElementTheme.Dark;
        var opacity = AppearanceSettingsPolicy.NormalizeOpacity(opacityOverride ?? Opacity());

        // 实色且没人点底色＝跟主题的黑白；点了底色就铺那颗（实色没有透明可保留，alpha 由取色那颗给成 FF）。
        if (kind == WidgetBackdropKind.None && tintOverride is null)
        {
            // 实色：完全跟主题的黑白实色，不吃任何滑杆。
            return new SolidColorBrush(dark ? Colors.Black : Colors.White);
        }

        return new SolidColorBrush(MaterialTint.BackdropColor(dark, AccentColor(), kind, opacity, tintOverride));
    }

    /// <summary>便捷重载：按当前设置读出材质。</summary>
    public static Brush SurfaceBrush(ElementTheme theme) => SurfaceBrush(theme, Backdrop());

    /// <summary>
    /// 主窗口的表面画笔。未开启材质（实色 None）时铺主题实色；开启后与组件走同一套表面色兜底
    /// （正常路径下 RootGrid 会被置透明让整窗背衬透出，见 MainWindow.RefreshAppearance）。
    /// <paramref name="opacityOverride"/> = 主窗口自己的不透明度（独立于组件）。
    /// </summary>
    public static Brush MainWindowSurfaceBrush(ElementTheme theme, WidgetBackdropKind kind, bool translucent, double? opacityOverride = null)
    {
        if (!translucent)
            return ThemeBrush.For(theme, "ApplicationPageBackgroundThemeBrush")
                   ?? SurfaceBrush(theme, WidgetBackdropKind.None, 1.0);

        return SurfaceBrush(theme, kind, opacityOverride);
    }

    /// <summary>解析 #RRGGBB / #AARRGGBB 为实色画笔；格式非法或空返回 null（调用方据此回退主题）。</summary>
    public static SolidColorBrush? ParseColorBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex!.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return null;
        var a = (byte)((HexDig(s[0]) << 4) | HexDig(s[1]));
        var r = (byte)((HexDig(s[2]) << 4) | HexDig(s[3]));
        var g = (byte)((HexDig(s[4]) << 4) | HexDig(s[5]));
        var b = (byte)((HexDig(s[6]) << 4) | HexDig(s[7]));
        return new SolidColorBrush(ColorHelper.FromArgb(a, r, g, b));
    }

    private static int HexDig(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };
}
