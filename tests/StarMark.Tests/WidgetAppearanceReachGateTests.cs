#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-82 的落地闸门：用户裁「当前仅组件的外观功能实现较好，但<b>并不完善</b>」，
/// 这一档钉的就是那四颗"半覆盖"从此⛔ 回来。
/// <para>
/// 四条判据都取<b>源码形状</b>（测试工程引用不到 <c>StarMark.UI</c>，坑表 #184）；
/// 纯算术那一半（点了底色之后字念黑还是白）住在 <see cref="SurfaceReadabilityTests"/>，
/// 因为判据已下沉到 <c>Core/Appearance/SurfaceReadability</c> 才量得到行为。
/// </para>
/// <para>⚠ 这些格子里凡"数一处主人"的，红的时候先问"多出来那颗是不是同一族写法"——
/// 是⇒改那颗数，⛔ 把判据放宽（名册 305 那一族的口径）。</para>
/// </summary>
public sealed class WidgetAppearanceReachGateTests
{
    private const string Editor = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";
    private const string Pane = "src/StarMark.UI/Views/ColorPickerPane.cs";
    private const string Window = "src/StarMark.UI/Views/WidgetWindow.Appearance.cs";
    private const string Helper = "src/StarMark.UI/Helpers/WidgetAppearance.cs";
    private const string Storage = "src/StarMark.Core/Widgets/WidgetStorage.cs";
    private const string Readability = "src/StarMark.Core/Appearance/SurfaceReadability.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>
    /// ①那颗「恢复全局」的按钮必须做它嘴里那句话：材质也在恢复之列。
    /// <para>从前写的是 <c>wBackdrop = current?.Backdrop;</c>（注释："材质保持打开时的值，不强制改"）——
    /// 于是按钮念着「恢复全局」，某一扇却永远钉在它自己的材质上。修法＝改实现去配那句话（VP 那条口径），
    /// ⛔ 把标签改短来迁就实现。</para>
    /// </summary>
    [Fact]
    public void ResetToGlobalAlsoDropsTheMaterialOverride()
    {
        var code = Code(ReadRepoFile(Editor));
        Assert.Equal(1, Count(code, "wBackdrop = null;"));
        // "= current?.Backdrop;" 全档只剩<b>打开时读回</b>那一颗声明；旧写法（reset 里再抄一次）不许回来。
        //  问的是抹掉注释后的代码：那条历史注释里<b>逐字抄了旧写法</b>，按 raw 文本数会把自己抓红。
        Assert.Equal(1, Count(code, "= current?.Backdrop;"));
    }

    /// <summary>
    /// ②「背景不透明度」现在逐扇可覆写：字段一个主人、编辑器四处各司其职、套用侧一种读法，
    /// 且⛔ 第二处就地各读各的（同一扇的材质/透明度⛔ 两处各算一份）。
    /// <para>编辑器那四处的分布＝<b>打开时读回 1 ＋ BuildWorking 写出 1 ＋ IsDirty 比 1 ＋ AllNull 比 1</b>；
    /// 这颗数会随"这一族又多一个要问它的地方"而 +1，那时改这颗数与这段分布，⛔ 放宽判据（名册 305）。</para>
    /// </summary>
    [Fact]
    public void PerWidgetOpacityHasOneFieldTwoWritersAndOneReadingShape()
    {
        Assert.Equal(1, Count(ReadRepoFile(Storage), "public double? OpacityOverride"));
        Assert.Equal(4, Count(ReadRepoFile(Editor), "OpacityOverride"));
        Assert.Equal(2, Count(Code(ReadRepoFile(Window)), "ov?.OpacityOverride ?? WidgetAppearance.Opacity()"));
        Assert.Equal(0, Count(Code(ReadRepoFile(Helper)), "OpacityOverride")); // 判据⛔ 住在 Helper 里再算一遍
    }

    /// <summary>
    /// ③「文本色」的射程＝整扇（含标题栏那一行：组件名与那五颗动作图标）。
    /// 从前只从 <c>ContentHost</c> 起走，于是内容改黄了、标题栏那一行还白着（真机量到）。
    /// <para>那颗图钉是<b>刻意</b>留在外面的：它的字色由 <c>ApplyTopmost</c> 按"钉没钉"管，
    /// 被文本色盖掉就等于把状态读数涂掉——所以这里钉的是"排除恰好一处"，⛔ "一处都⛔ 排除"。</para>
    /// </summary>
    [Fact]
    public void TextColorCoversTheWholeWindowExceptTheStateOwnedPin()
    {
        var code = Code(ReadRepoFile(Window));
        Assert.Equal(1, Count(code, "SetForegroundDeep(RootBorder, fg)"));
        Assert.Equal(0, Count(code, "SetForegroundDeep(panel, fg)"));
        Assert.Equal(1, Count(code, "ReferenceEquals(child, PinIcon)"));
        Assert.Equal(1, Count(code, "ApplyTextScale(panel, _textScale)"));   // 缩放仍只管内容区
    }

    [Fact]
    public void TheAutoTextDecisionLivesInCoreOnly()
    {
        Assert.Equal(1, Count(ReadRepoFile(Readability), "public static Color ReadableOn(Color surface)"));
        var helper = MethodBody(Code(ReadRepoFile(Helper)), "public static Windows.UI.Color? AutoForeground(");
        Assert.Contains("return SurfaceReadability.ReadableOn(surface);", helper, StringComparison.Ordinal);
        Assert.Contains("SurfaceReadability.BaseFor(dark)", helper, StringComparison.Ordinal);
        var swatch = MethodBody(Code(ReadRepoFile(Pane)), "private static Button MakeSwatchButton");
        Assert.Contains("SurfaceReadability.ReadableOn(b.Color)", swatch, StringComparison.Ordinal);
        Assert.Equal(1, Count(Code(ReadRepoFile(Window)), "WidgetAppearance.AutoForeground("));
    }

    /// <summary>
    /// ⑤色板那一排的描边常驻：⛔ 用 Transparent 当"未选中"，那颗「白」在浅色卡片上会连形状一起消失。
    /// <para>片144 起描边与高亮那颗性格住进面板（<c>ColorPickerPane</c> 的 <c>SwatchStroke</c>／<c>Highlight</c>），
    /// 编辑器三颗对象按钮的高亮也走同一颗——⛔ 第二处描边算法。</para>
    /// <para>顺带钉一句"⛔ 为这颗旋钮另立新词"：逐扇那颗不透明度与设置页那一行念的是同一件事，
    /// 共用 <c>settings.widgets.opacityLabel</c> 那颗键（一句字一个主人，两处在问）。</para>
    /// </summary>
    [Fact]
    public void SwatchesKeepAVisibleEdgeAndTheNewSliderReusesTheExistingWord()
    {
        var pane = Code(ReadRepoFile(Pane));
        Assert.Contains("private static Brush SwatchStroke(ElementTheme theme)", pane, StringComparison.Ordinal);
        Assert.Equal(1, Count(pane, "BorderBrush = SwatchStroke(theme)"));   // 摘高亮也走那颗描边（片144：性格只此一处）
        Assert.Equal(1, Count(pane, "\"AccentFillColorDefaultBrush\""));      // 高亮＝强调色描边，⛔ 另立新画笔
        Assert.Equal(1, Count(pane, "Fill = new SolidColorBrush(Colors.Transparent)"));   // SV 指示那颗：透明底只是让渐变透出，形状由描边给
        var code = Code(ReadRepoFile(Editor));
        Assert.Equal(1, Count(code, "ColorPickerPane.Highlight("));           // 三颗对象按钮的高亮走面板那同一颗
        // 全档只剩一处 Transparent：那是「背景色/文本色/边框色」三颗<b>对象按钮</b>——它们自带字，
        // 摘了描边仍然读得出，所以⛔ 一起改；这颗数＝1 是"⛔ 再给一颗无色板的按钮用透明边框"的射程。
        Assert.Equal(1, Count(code, "BorderBrush = new SolidColorBrush(Colors.Transparent)"));
        var quoted = "\"settings.widgets.opacityLabel\"";
        // 2 ＝ ①设置页那一行的标题 ②那颗滑杆的可访问名（片142 借同一颗键，⛔ 第二份文案）。
        // ⚠ 名册 305 那一族：多出来的那颗是同一族写法⇒ 改这颗数并写下分布，⛔ 放宽判据。
        //    逐扇那颗（组件外观编辑器里的滑杆）走下面的 code 那颗数，它自己只许问一次。
        Assert.Equal(2, Count(ReadRepoFile(Page), quoted));
        Assert.Equal(1, Count(code, quoted));
    }
}
