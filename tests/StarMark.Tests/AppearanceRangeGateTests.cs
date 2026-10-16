#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「外观 + 边缘磁吸」那三根滑杆的量程／默认只许有一个主人的形状闸门（批次 SD，P-123 清单 #4；
/// 批次 WI-82 把射程扩到<b>组件「外观」编辑器</b>那四根旋钮——那里当时是同一件事的第二主人，且已咬人：
/// 逐扇不透明度滑杆写的是 0–1，全局那根的下界是 0.3，于是单扇组件能被拖到完全看不见）。
/// <para>
/// 收口前同一件事最多有五个主人：仓储读档夹一次、落盘再夹一次；VM 字段初值一遍、读取兜底一遍；
/// XAML 的 <c>Minimum/Maximum/StepFrequency</c> 一遍。判据本身住在 Core，所以它的<b>行为</b>由
/// <see cref="AppearanceSettingsPolicyTests"/> 真测；这里只能钉<b>写法</b>（#184：测试工程不引用 StarMark.UI）。
/// </para>
/// <para>
/// <b>与 SA 那条闸门的一个差别（值得记住）</b>：SA 能把"量程数字"直接钉成禁项（4096 这种数在 UI 工程里没几个无关用法），
/// 这批不能——0.72、8、4、64、40 在界面里是<b>无关的量</b>（<c>Opacity="0.72"</c> 的装饰文字、<c>ColumnSpacing="8"</c>、
/// HSL 饱和度 0.72），钉数字就是天天误伤（#144）。所以这里全部改钉<b>形状</b>：谁在夹取、谁在写滑杆属性、谁的实参槽位里出现了判据名字。
/// </para>
/// <para>
/// <b>所有禁项都读"抹掉注释后的代码"</b>：这批正是要在注释里写"旧写法是 <c>Math.Clamp(px, 0, 40)</c>"的地方，
/// 用原文扫会让一句解释性注释把闸门弄红（而被人当噪声放宽规则＝闸门消失）。机制本身由
/// <see cref="CommentBlindnessIsReal_NotJustAnAssertionAboutIt"/> 钉住。
/// </para>
/// </summary>
public sealed class AppearanceRangeGateTests
{
    private const string JudgeFile = "src/StarMark.Core/Appearance/AppearanceSettingsPolicy.cs";
    private const string StoreFile = "src/StarMark.UI/Helpers/SettingsStore.Appearance.cs";
    private const string ViewModelFile = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string XamlFile = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string WidgetEditorFile = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";
    private const string WidgetWindowAppearanceFile = "src/StarMark.UI/Views/WidgetWindow.Appearance.cs";
    private const string WidgetAppearanceFile = "src/StarMark.UI/Helpers/WidgetAppearance.cs";

    /// <summary>四根滑杆（组件／主窗各一根不透明度 ＋ 磁吸两根）按 <c>Value=</c> 的绑定目标点名。</summary>
    private static readonly string[] BoundValueTargets =
        ["ViewModel.MainWindowOpacity", "ViewModel.WidgetOpacity", "ViewModel.SnapStrength", "ViewModel.SnapSpacing"];

    private static readonly string[] RangeAttributes = ["Minimum", "Maximum", "StepFrequency"];

    // ────────── ① 仓储不再自己写夹取 ──────────

    /// <summary>
    /// 读档与落盘都要走判据的 <c>Normalize*</c>。<b>为什么禁 <c>Math.Clamp</c> 而不是禁某个数字</b>：
    /// 数字在界面里到处是无关的量，而"这里有没有第二份夹取逻辑"才是这批要防的动作形状。
    /// </summary>
    [Fact]
    public void StoreClampsThroughTheJudgeNotItsOwnMath()
    {
        var code = CodeOf(StoreFile);
        Assert.DoesNotContain("Math.Clamp(", code, StringComparison.Ordinal);
        // 反空转：禁项为零必须配正面计数，否则"整段被删光"也算通过（#161）。
        // 10 个调用点＝四组设置各有"读＋写"（不透明度那组主窗与组件各一读一写）。
        Assert.Equal(10, Count(code, "AppearanceSettingsPolicy.Normalize"));
        Assert.Equal(4, Regex.Matches(code, @"AppearanceSettingsPolicy\.\w+Default\b").Count);
    }

    // ────────── ② 界面上那四根滑杆不许自带量程 ──────────

    [Fact]
    public void BoundSlidersTakeTheirRangeFromTheJudge()
    {
        var offenders = SliderElements()
            .Where(s => BoundValueTargets.Any(b => s.Contains(b, StringComparison.Ordinal)))
            .SelectMany(s => RangeAttributes.Select(a => (Slider: s, Attr: a, Value: AttrValue(s, a))))
            .Where(x => x.Value is not null && !x.Value!.StartsWith("{x:Bind", StringComparison.Ordinal))
            .Select(x => $"{x.Attr}=\"{x.Value}\"")
            .ToList();

        Assert.True(offenders.Count == 0,
            "外观／磁吸的滑杆又自己写量程了（该绑 AppearanceSettingsPolicy 转发的属性）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 抽取器的自证：正好抓到那四根，且每根的<b>三个</b>量程属性都是绑定而不是"没写所以没违规"。
    /// <para>这条存在的原因：②是"零命中即通过"的禁项闸门，绑定名写错一个字、或者整段属性被删掉，
    /// 它都会一直绿（#161）。</para>
    /// </summary>
    [Fact]
    public void SliderExtractorSeesTheFourBoundOnes()
    {
        var sliders = SliderElements();
        Assert.True(sliders.Length >= 6, $"只抽到 {sliders.Length} 个 Slider——抽取器失效了");
        Assert.Equal(4, sliders.Count(s => BoundValueTargets.Any(b => s.Contains(b, StringComparison.Ordinal))));
        foreach (var target in BoundValueTargets)
        {
            var slider = sliders.Single(s => s.Contains(target, StringComparison.Ordinal));
            Assert.Equal(3, RangeAttributes.Count(a =>
                AttrValue(slider, a)?.StartsWith("{x:Bind", StringComparison.Ordinal) == true));
        }
        Assert.Null(AttrValue("<Slider Value=\"0\"/>", "Minimum"));   // 没写这个属性就是没写，不许当成违规
    }

    // ────────── ③ VM 的初值与兜底都转发自判据 ──────────

    /// <summary>
    /// 四颗字段初值 + 四次读取兜底，实参槽位里必须是判据的名字。
    /// <para>按"包含"锚而不是行首：这些行前面挂着 <c>[ObservableProperty]</c>，
    /// 行首锚定那条教训（#189）已经红过一次了。锚点自己也须唯一，否则"其中一行合规"会掩盖另一行违规。</para>
    /// </summary>
    [Fact]
    public void ViewModelInitialValuesAndFallbacksComeFromTheJudge()
    {
        var lines = CodeOf(ViewModelFile).Split('\n').Select(l => l.Trim()).ToList();
        var anchors = new[]
        {
            "private double _widgetOpacity = ", "private double _mainWindowOpacity = ",
            "private double _snapSpacing = ", "private double _snapStrength = ",
            "Safe(_settings.LoadWidgetOpacity,", "Safe(_settings.LoadMainWindowOpacity,",
            "Safe(_settings.LoadWidgetSnapSpacing,", "Safe(_settings.LoadWidgetSnapStrength,",
        };
        foreach (var anchor in anchors)
        {
            // 锚点自己也要唯一：出现两行＝其中一行合规就能掩盖另一行违规
            var line = Assert.Single(lines, l => l.Contains(anchor, StringComparison.Ordinal));
            Assert.Contains("AppearanceSettingsPolicy.", line, StringComparison.Ordinal);
        }
    }

    // ────────── ④ 设置侧不许再借用求解器那颗物理像素默认 ──────────

    /// <summary>
    /// <c>WidgetSnapCalculator.Default*</c> 是<b>物理像素</b>，设置页那三根滑杆是<b>逻辑像素</b>；
    /// 批次 SD 之前滑杆的 DIP 默认寄在物理像素那颗上＝在赌缩放恒为 100%。
    /// <para>这条不是"那个常量不许存在"：它今天仍被 <c>WidgetWindow</c> 用作会话前的占位初值与迟滞宽度推导，
    /// 所以下面同时钉"那边还在读它"，免得这条禁项退化成没人管的空闸门（#161）。</para>
    /// </summary>
    [Fact]
    public void SettingsSideNeverBorrowsTheSolversPhysicalDefaults()
    {
        foreach (var file in new[] { StoreFile, "src/StarMark.UI/Helpers/WidgetAppearance.cs" })
            Assert.DoesNotContain("WidgetSnapCalculator.Default", CodeOf(file), StringComparison.Ordinal);

        Assert.Equal(3, Count(CodeOf("src/StarMark.UI/Views/WidgetWindow.xaml.cs"), "WidgetSnapCalculator.Default"));
        Assert.Contains("WidgetSnapCalculator.Default",
            CodeOf("src/StarMark.UI/Views/WidgetWindow.DragResize.cs"), StringComparison.Ordinal);
    }

    // ────────── ⑤ 判据不空壳 ──────────

    /// <summary>
    /// 判据里数字的颗数按<b>分布</b>钉，⛔ 只钉一个总数（批次 WI-82 把组件那三根旋钮也搬进来之后，
    /// 总数从 15 涨到 27；只写"27"的话，谁搬进一组却漏掉步进、或把某档整个删掉，这行照样绿）。
    /// <para>分布＝设置页三组各 4 颗（下限/上限/默认/步进）＋材质那组 3 颗（无步进，那根滑杆由 XAML 自己给）
    /// ＋组件编辑器三组各 4 颗＝27；夹取出口＝设置页四族＋组件三族＝7，每颗都真的用一次 <c>Math.Clamp</c>。</para>
    /// </summary>
    [Fact]
    public void TheJudgeHoldsEveryNumberAndNormalizesThem()
    {
        var code = CodeOf(JudgeFile);
        // 设置页那三组：4＋4＋4＝12；材质那组没有步进（XAML 自己给那根滑杆的节拍）＝3；两族合计正是批次 SD 的 15
        Assert.Equal(12, CountGroup(code, ["Opacity", "SnapStrength", "SnapSpacing"]));
        Assert.Equal(3, CountGroup(code, ["MaterialIntensity"]));
        // 批次 WI-82 搬进来的组件那三根：各 4 颗＝12
        Assert.Equal(12, CountGroup(code, ["TextScale", "BorderThickness", "CornerRadius"]));
        Assert.Equal(27, Regex.Matches(code, @"public const (double|int) \w+ =").Count);
        Assert.Equal(7, Regex.Matches(code, @"public static (double|int) Normalize\w+\(").Count);
        // 不许退成"只是把数字抄一遍、其实谁也没夹"的空壳：每颗 Normalize 都得真的用一次 Math.Clamp
        Assert.Equal(7, Count(code, "Math.Clamp("));
        Assert.Equal(7, Regex.Matches(code, @"Math\.Clamp\(\w+, \w+Floor, \w+Ceiling\)").Count);
    }

    /// <summary>按名头数一组里 <c>Floor/Ceiling/Default/Step</c> 那四颗常量的颗数（缺哪颗就少哪颗）。</summary>
    private static int CountGroup(string code, string[] prefixes)
        => prefixes.Sum(p => Regex.Matches(code, $@"public const (double|int) {p}(Floor|Ceiling|Default|Step) =").Count);

    // ────────── ⑥ 组件「外观」编辑器那四根旋钮也不许自带量程 ──────────

    /// <summary>
    /// 批次 WI-82 的第二刀：组件那扇编辑器的滑杆此前把量程<b>各写一遍</b>，其中一颗已经咬人——
    /// 逐扇「背景不透明度」写的是 0–1，而全局那根的下界是 0.3（判据注释：再低就等于把组件隐藏了）
    /// ⇒ 同一件事两扇窗给了两段互相认不出的范围，滑杆还能把单扇组件拖到完全看不见。
    /// <para>钉法沿用 ② 那一族的口径：⛔ 钉数字（0.6、12、48 在界面里是无关的量），改钉<b>形状</b>＝
    /// 按标签认出那四根 <c>MakeSlider</c> 调用，量程序列里必须出现判据的名字且<b>一颗数字都没有</b>。
    /// 抽取器自己也要正证（⑤ 那格之外的 #161 口径）：四根必须各命中恰好一次。</para>
    /// </summary>
    [Fact]
    public void WidgetEditorKnobsTakeTheirRangeFromTheJudge()
    {
        var code = CodeOf(WidgetEditorFile);
        var segments = KnobCallSegments(code);
        Assert.Equal(KnobRanges.Length, segments.Count);   // 抽取器瞎了／多了一根没点名的旋钮，都在这里红

        foreach (var (label, floor, ceiling, step) in KnobRanges)
        {
            var seg = Assert.Single(segments, s => s.Contains($"\"{label}\"", StringComparison.Ordinal));
            foreach (var name in new[] { floor, ceiling, step })
                Assert.Equal(1, Count(seg, $"AppearanceSettingsPolicy.{name}"));
            // 量程槽里一颗字面数字都不许有：写回 0.6／12／48 就是第二个主人回来了
            Assert.Equal(0, seg.Count(char.IsDigit));
        }

        // 「恢复全局」把滑杆摆回哪一档也必须问判据（三根默认值各两处＝挂上时 + 复位时；不透明度复位读全局值，形状不同）
        foreach (var name in new[] { "BorderThicknessDefault", "CornerRadiusDefault", "TextScaleDefault" })
            Assert.Equal(2, Count(code, $"AppearanceSettingsPolicy.{name}"));
    }

    /// <summary>
    /// 按标签把四根旋钮的调用段切出来：从 <c>MakeSlider("标签"</c> 起，到该调用第一个 <c>out var</c> 止，
    /// 中途遇到下一个 <c>MakeSlider(</c> 就判失败（防"段跨到下一根上"，那会让数字检查形同虚设）。
    /// </summary>
    private static List<string> KnobCallSegments(string code)
        => Regex.Matches(code, @"MakeSlider\(""[^""]*""(?:(?!MakeSlider\()[\s\S])*?out var")
            .Select(m => m.Value).ToList();

    private static readonly (string Label, string Floor, string Ceiling, string Step)[] KnobRanges =
    [
        ("settings.widgets.opacityLabel", "OpacityFloor", "OpacityCeiling", "OpacityStep"),
        ("widget.appearance.slider.borderThickness", "BorderThicknessFloor", "BorderThicknessCeiling", "BorderThicknessStep"),
        ("widget.appearance.slider.cornerRadius", "CornerRadiusFloor", "CornerRadiusCeiling", "CornerRadiusStep"),
        ("widget.appearance.slider.textScale", "TextScaleFloor", "TextScaleCeiling", "TextScaleStep"),
    ];

    // ────────── ⑦ 套用侧与取色侧都不许再自己夹一遍 ──────────

    /// <summary>
    /// 组件窗口读覆盖值、<c>WidgetAppearance</c> 算背衬透明度：这两个消费点此前各写了一份 <c>Math.Clamp(x, 0.0, 1.0)</c>
    /// 与 <c>Math.Clamp(v, 0, 1)</c>——正是"第二份真值"，也是上面那颗缺陷能存在的土壤。
    /// <para>现在禁的是<b>动作形状</b>（这两个文件里 <c>Math.Clamp</c> 归零）＋正面计数（确实各走了判据），
    /// ⛔ 只禁数字：编辑器里 HSV 取色那三颗 <c>Math.Clamp</c> 是<b>另一件事</b>（把鼠标点位置折进色域），
    /// 不在这两个文件的射程里，也不该被搬进判据。</para>
    /// </summary>
    [Fact]
    public void WidgetAppearanceConsumersClampThroughTheJudgeNotTheirOwnMath()
    {
        var window = CodeOf(WidgetWindowAppearanceFile);
        Assert.Equal(0, Count(window, "Math.Clamp("));
        Assert.Equal(4, Count(window, "AppearanceSettingsPolicy.Normalize"));
        // 覆盖为空时那三档回落也必须念同一颗默认（⛔ 在这里另写 1／8／1.0）：各恰好一处
        foreach (var name in new[] { "BorderThicknessDefault", "CornerRadiusDefault", "TextScaleDefault" })
            Assert.Equal(1, Count(window, $"AppearanceSettingsPolicy.{name}"));

        var helper = CodeOf(WidgetAppearanceFile);
        Assert.Equal(0, Count(helper, "Math.Clamp("));
        Assert.Equal(3, Count(helper, "AppearanceSettingsPolicy.NormalizeOpacity("));
    }

    // ────────── ⑧ 转发的量程，XAML 必须各读一次 ──────────

    /// <summary>
    /// VM 转发出去的 9 颗量程（三组 × 下限/上限/步进），XAML 里必须各有读者；反过来 VM 里也必须有这 9 颗出口。
    /// <b>为什么单独钉</b>：转发出去没人读＝这批只是把数字搬了个家，界面还在用自己写的那份。
    /// </summary>
    [Fact]
    public void EveryForwardedRangePropertyHasAnXamlReader()
    {
        var forwarders = new[]
        {
            "OpacityFloor", "OpacityCeiling", "OpacityStep",
            "SnapStrengthFloor", "SnapStrengthCeiling", "SnapStrengthStep",
            "SnapSpacingFloor", "SnapSpacingCeiling", "SnapSpacingStep",
        };
        var xaml = XamlCode();
        var unread = forwarders
            .Where(p => !xaml.Contains($"ViewModel.{p}, Mode=OneTime", StringComparison.Ordinal))
            .ToList();
        Assert.True(unread.Count == 0, "这些量程转发了却没人绑（界面仍在用自己写的那份）：\n" + string.Join("\n", unread));

        var vm = CodeOf(ViewModelFile);
        Assert.Equal(forwarders.Length, forwarders.Count(p =>
            vm.Contains($"public double {p} =>", StringComparison.Ordinal)
            || vm.Contains($"public int {p} =>", StringComparison.Ordinal)));
    }

    // ────────── ⑨ 本闸门依赖的机制，自己也要被钉住 ──────────

    /// <summary>
    /// 上面每条禁项都靠"注释被抹平成空格"才不误伤，所以这个机制不能只是我说一句。
    /// 同一段文本：<b>注释里</b>出现违规形状 ⇒ 看不见；<b>代码里</b>出现 ⇒ 看得见，且同行注释不会把后半段真违规一起带走（#190）。
    /// </summary>
    [Fact]
    public void CommentBlindnessIsReal_NotJustAnAssertionAboutIt()
    {
        Assert.DoesNotContain("Math.Clamp(", CodeOfText("// 旧写法是 Math.Clamp(px, 0, 40)\nint a = 1;\n"),
            StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(", CodeOfText("int a = Math.Clamp(px, 0, 40); // 以前也这样\n"),
            StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(", CodeOfText("/* 说明 */ var b = Math.Clamp(x, 0, 1); // 尾注\n"),
            StringComparison.Ordinal);
    }

    // ─────────────────────────  共用小工具  ─────────────────────────

    private static string[]? _sliders;

    private static string[] SliderElements()
        => _sliders ??= Regex.Matches(XamlCode(), @"<Slider\b[^>]*?/>", RegexOptions.Singleline)
            .Select(m => m.Value).ToArray();

    /// <summary>源码文件：抹掉注释后的代码（禁项规则一律用它，见类注释）。</summary>
    private static string CodeOf(string relativePath) => CodeOfText(ReadRepoFile(relativePath));

    private static string CodeOfText(string text) => FormatScanner.Scan(text).Code;

    /// <summary>XAML：注释是 <c>&lt;!-- --&gt;</c>，C# 扫描器不认，得自己抹。</summary>
    private static string XamlCode()
        => Regex.Replace(Xaml(), @"<!--.*?-->", " ", RegexOptions.Singleline);

    private static string Xaml()
        => File.ReadAllText(Path.Combine(RepoRoot(), XamlFile.Replace('/', Path.DirectorySeparatorChar)));

    private static string? AttrValue(string element, string attr)
    {
        var m = Regex.Match(element, $@"{attr}\s*=\s*""([^""]*)""");
        return m.Success ? m.Groups[1].Value : null;
    }
}
