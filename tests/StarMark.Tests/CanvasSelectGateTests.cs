#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「选择」工具的服务侧闸门（P-288）：点选／框选／拖动／落定这一段在 <c>CanvasService</c> 里的链路约束。
/// <para>
/// 同一交互语言的另一头（截图/贴图那条）住在 <c>CaptureSelectToolGateTests</c>，各有各的闸；
/// 这里只守画布服务侧：分派顺序、历史写者先收选中、挪一组只入一步历史、空框收选择的余像收尾，
/// 以及选中框与橡皮筋<b>不进持久层</b>——快照、存图、贴图都不带走它。
/// </para>
/// <para>证人全是源码形状（<c>StarMark.Tests</c> 引不到 <c>StarMark.UI</c>，坑表 #370 那条边界）；
/// 像素级的结论由 Core 那侧的单测与真机手测管，这里⛔ 假称跑过界面。</para>
/// </summary>
public sealed class CanvasSelectGateTests
{
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";
    private const string Toolbar = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";

    /// <summary>
    /// 整套 partial（<c>CanvasService.*.cs</c>，Select 那一段自动并入）抹掉注释的一份。
    /// <b>禁项类判据必须读它</b>：这颗档里正好有一条"注释点名禁项"的现场——
    /// CloseBoardHost 的注释写着"不走 DropSelection"，按原文读会把那句说明书算成违例
    /// （CanvasWiringGateTests 那条 Notice 闸门记过同一课）。
    /// </summary>
    private static string All() => SourceGate.Code(SourceGate.ReadRepoPartials(Service));

    /// <summary>
    /// <see cref="SourceGate.MethodBody"/> 返回的那份<b>带签名行</b>（切片从签名那一行起）——
    /// 「方法的第一句必须是 X」这一类判据直接 <c>TrimStart</c> 会把签名当成第一句。这里跳过
    /// 签名与那颗开括号，回到体内；块体方法必有那颗 <c>{</c>，找不到就是判据写错了对象。
    /// </summary>
    private static string InnerBody(string body)
    {
        var at = body.IndexOf('{');
        Assert.True(at >= 0, "这块方法体里没有开括号——判据写错了对象（表达式体？）");
        return body[(at + 1)..];
    }

    /// <summary>
    /// 「选择」在场吗是一盏<b>独立的灯</b>（<c>_selectTool</c>），不是工具枚举里多出来的那支笔：
    /// 选中的是一组<b>已画成的东西</b>，与"接下来落墨用哪支笔"是两件事，塞进同一枚举就会出现
    /// "选中态被当成一支笔落进历史"那一类错。三支笔与「选择」互斥的桥在 <c>SelectTool</c>：
    /// 拿笔即离选择；「选择」在场时点笔是"换到那支笔"，不判"再点取消"。
    /// </summary>
    [Fact]
    public void SelectIsItsOwnSwitchNotAToolMember()
    {
        var all = All();
        // 状态：一盏灯、一套选中集（按实例存）、一条橡皮筋——都住在主文件
        Assert.Contains("private static bool _selectTool;", all);
        Assert.Contains("private static IReadOnlyList<Annotation>? _selected;", all);
        Assert.Contains("private static PixelPoint? _bandTo;", all);
        // 按下的性质表里给选择留了两态（拖选中／拉橡皮筋），其余成员不动
        Assert.Contains("private enum Press { None, Drawing, Ephemeral, QuickPen, Shape, PolyLine, SelectMove, SelectBand }", all);

        var toggle = SourceGate.MethodBody(all, "public static void ToggleSelectTool()");
        Assert.Contains("CommitOpenStroke();", toggle);                    // 换灯之前先收手上那条
        Assert.Contains("_selectTool = !_selectTool;", toggle);
        Assert.Contains("if (!_selectTool) DropSelection();", toggle);     // 灯灭＝框跟着走
        Assert.Contains("RaiseStateChanged();", toggle);
        Assert.Contains("public static bool IsSelectTool => _selectTool;", all);

        var select = SourceGate.MethodBody(all, "public static void SelectTool(CanvasTool tool)");
        Assert.Contains("CommitOpenStroke();", select);
        Assert.Contains("_selectTool = false;", select);
        Assert.Contains("DropSelection();", select);
        var toggleTool = SourceGate.MethodBody(all, "public static void ToggleTool(CanvasTool tool)");
        Assert.Contains("if (_selectTool)", toggleTool);                   // 在场时点笔＝换笔，不判"再点取消"
    }

    /// <summary>
    /// 分派的三道门缝：①按下先过条子那道闸再问选择——落进条子矩形的按下连选择也不该接；
    /// ②「选择」在场时这一按整支交给 <c>BeginSelectPress</c>，<c>BeginPress</c> 那套落墨一个字都不许沾；
    /// ③移动与收笔各认自己的两态，收笔的键盘链收口把"松手那一点"补成上一拍（模型写的＝屏幕正显示的）。
    /// </summary>
    [Fact]
    public void PressesRouteToTheSelectArmBeforeAnyInk()
    {
        var all = All();
        var pressed = SourceGate.MethodBody(all, "private static void OnPressed(Screen screen, CanvasPointer pointer)");
        Assert.True(pressed.IndexOf("LayerDirector.IsPointOnChrome(", StringComparison.Ordinal)
                    < pressed.IndexOf("if (_selectTool)", StringComparison.Ordinal),
            "条子矩形那道闸必须排在选择那臂之前");
        Assert.True(pressed.IndexOf("if (_selectTool)", StringComparison.Ordinal)
                    < pressed.IndexOf("BeginPress(screen, pointer.At", StringComparison.Ordinal),
            "「选择」在场时这一按不许再走落墨那条路");
        Assert.Contains("BeginSelectPress(screen, pointer.At);", pressed);

        var moved = SourceGate.MethodBody(all, "private static void OnMoved(Screen screen, CanvasPointer pointer)");
        Assert.Contains("if (_press == Press.SelectMove)", moved);
        Assert.Contains("DragSelection(screen, pointer.At);", moved);
        Assert.Contains("if (_press == Press.SelectBand)", moved);
        Assert.Contains("DragBand(screen, pointer.At);", moved);
        Assert.True(moved.IndexOf("DragSelection(screen, pointer.At);", StringComparison.Ordinal)
                    < moved.IndexOf("if (_press == Press.Ephemeral)", StringComparison.Ordinal),
            "选择的两种拖动必须整支接管：节流与提交都归那一段");

        var finish = SourceGate.MethodBody(all, "private static void FinishPress(Screen? screen, PixelPoint? at = null)");
        Assert.Contains("CommitSelectionMove(screen, at ?? new PixelPoint(", finish);
        Assert.Contains("CommitBand(screen);", finish);
    }

    /// <summary>
    /// 「选择」下的那一按只问 <c>AnnotationPainter</c>：命不命中、容差多少由它说（笔迹太细，
    /// 逐像素命中几乎点不中，容差是常量），这里只按结论分派——点中＝那一组（已在组里＝保整组），
    /// 落空＝起橡皮筋。收尾必须提交：选中框画在提交缓冲上，起手不 Flush 第一帧就没有那颗框。
    /// </summary>
    [Fact]
    public void ASelectPressAsksThePainterAndFlushes()
    {
        var all = All();
        Assert.Contains("private const int SelectSlop = 8;", all);
        var begin = SourceGate.MethodBody(all, "private static void BeginSelectPress(Screen screen, PixelPoint at)");
        Assert.Contains("AnnotationPainter.HitTest(marks, at, SelectSlop)", begin);
        Assert.Contains("SetSelection(screen, new[] { mark });", begin);
        Assert.Contains("_press = Press.SelectMove;", begin);
        Assert.Contains("_press = Press.SelectBand;", begin);
        Assert.Contains("Flush(screen);", begin);
    }

    /// <summary>
    /// 两种拖动开头同一道闸：<b>这一手是不是这块屏的</b>。多屏时迟到的事件可能带着别的屏的坐标，
    /// 不拦就会出现"框在 A 屏拖、墨在 B 屏走"。节流沿用拖动那一颗（<c>DragThrottleMs</c>），
    /// 前后两拍都进脏区。拖选中要重烤持久层（墨跟手走），拉橡皮筋只画提交缓冲——持久层一根线不动。
    /// </summary>
    [Fact]
    public void BothDragsGuardTheirScreenAndThrottleOnTheSameClock()
    {
        var all = All();
        foreach (var sig in new[]
                 {
                     "private static void DragSelection(Screen screen, PixelPoint at)",
                     "private static void DragBand(Screen screen, PixelPoint at)",
                 })
        {
            var body = SourceGate.MethodBody(all, sig);
            Assert.True(InnerBody(body).TrimStart().StartsWith("if (screen != _pressScreen) return;", StringComparison.Ordinal),
                $"{sig} 的第一句必须是跨屏防御");
            Assert.Contains("Environment.TickCount64 - screen.LastFlushMs < DragThrottleMs", body);
            Assert.Contains("screen.Dirty.Add(before);", body);
            Assert.Contains("screen.Dirty.Add(after);", body);
        }
        Assert.Contains("Recomposite(screen);",
            SourceGate.MethodBody(all, "private static void DragSelection(Screen screen, PixelPoint at)"));
        Assert.DoesNotContain("Recomposite",
            SourceGate.MethodBody(all, "private static void DragBand(Screen screen, PixelPoint at)"));
    }

    /// <summary>
    /// 模型被改写的每一条路都先收掉选中：选中集按<b>实例</b>存，撤销／重做换回来的是新实例，
    /// 清空连原件一起没了——框画在不存在的东西上就是"幽灵框"那一族。Esc 的第一级
    /// （<c>CloseWorkInProgress</c>）也要认选中那组："有没有半件事"的读数里必须有它，
    /// 否则 Esc 第一级永远轮不到收选中、直接把板子关了。
    /// </summary>
    [Fact]
    public void HistoryWritersDropTheSelectionBeforeTouchingTheModel()
    {
        var all = All();
        var undo = SourceGate.MethodBody(all, "public static void Undo()");
        Assert.True(undo.IndexOf("DropSelection();", StringComparison.Ordinal)
                    < undo.IndexOf("if (_polyPoints is { } open)", StringComparison.Ordinal),
            "撤销先收选中再动历史");
        Assert.Contains("DropSelection();", SourceGate.MethodBody(all, "public static void Redo()"));
        var clear = SourceGate.MethodBody(all, "public static void ClearAll()");
        Assert.True(clear.IndexOf("CancelOpenPolyLine();", StringComparison.Ordinal)
                    < clear.IndexOf("DropSelection();", StringComparison.Ordinal),
            "清空先收折线再收选中（两条都是'手上一半的东西不许落进结果'）");
        var wip = SourceGate.MethodBody(all, "public static void CloseWorkInProgress()");
        Assert.Contains("_selected is { Count: > 0 } || _bandTo is not null", wip);
        Assert.Contains("DropSelection();", wip);
        Assert.Contains("public static bool HasWorkInProgress => _polyPoints is { Count: > 0 } || _selected is { Count: > 0 };", all);
        // 换工具／换粗细／开穿透之前同样先落定（CommitOpenStroke 是它们共同的收口）
        var open = SourceGate.MethodBody(all, "private static void CommitOpenStroke()");
        Assert.Contains("if (_press is Press.SelectMove or Press.SelectBand) FinishPress(_pressScreen);", open);
    }

    /// <summary>
    /// 拖完落定只入<b>一步</b>历史（<c>InkDoc.ReplaceMany</c>）：挪一组撤一次整组回来；
    /// 逐条各入一步的话，"把这三个字挪过去"要按三次撤销才回得来。比对按<b>实例</b>不按下标
    /// （撤销／清空之后下标会失效），包围盒换成新实例的那份再缓存。原地松手不入历史：
    /// 只把拖动期间烤进持久层的偏移复原回来、选中保留——一步"什么都没做"的历史是撤销栈里的一个洞。
    /// </summary>
    [Fact]
    public void AMoveCommitsAsOneHistoryStep()
    {
        var move = SourceGate.MethodBody(All(), "private static void CommitSelectionMove(Screen screen, PixelPoint at)");
        Assert.Contains("if (dx == 0 && dy == 0)", move);
        Assert.Contains("screen.Ink.ReplaceMany(pairs);", move);
        Assert.Contains("picked.Contains(marks[i])", move);
        Assert.Contains("marks[i].MovedBy(dx, dy)", move);
        Assert.Contains("_selected = moved;", move);
        Assert.Contains("_selectionBoxes = boxes;", move);
        // 拖动期间选中集被别的动作动了（撤销、清空）：整组收掉，不能画一个空框
        Assert.Contains("if (pairs.Count == 0)", move);
        Assert.Contains("DropSelection();", move);
    }

    /// <summary>
    /// 收橡皮筋：框住的都进选中集；一条都没框住＝取消选择。<b>空拾取那一刻橡皮筋故意不收</b>——
    /// 连同它占的脏区一起交给 <c>DropSelection</c>，先清了 <c>_bandTo</c> 它会滞留一帧
    /// （那一刻没人再提交那块区域）。非空那一臂反过来：先记余像、清橡皮筋，再换组、提交。
    /// </summary>
    [Fact]
    public void AnEmptyBandDropsTheSelectionButLeavesTheBandToDrop()
    {
        var band = SourceGate.MethodBody(All(), "private static void CommitBand(Screen screen)");
        Assert.Contains("if (_bandTo is not { } to) return;", band);
        Assert.Contains("CaptureGeometry.Overlaps(band, marks[i].Bounds())", band);
        Assert.Contains("if (picked.Count == 0)", band);
        Assert.Contains("DropSelection();", band);
        Assert.True(band.IndexOf("if (picked.Count == 0)", StringComparison.Ordinal)
                    < band.IndexOf("_bandTo = null;", StringComparison.Ordinal),
            "空拾取那臂不许先清 _bandTo：余像要连同脏区一起交给 DropSelection 收");
        Assert.Contains("var bandPrint = BandFootprint();", band);
        Assert.Contains("SetSelection(screen, picked);", band);
        Assert.Contains("Flush(screen);", band);
    }

    /// <summary>
    /// 换一组选中：<b>先收旧的</b>（旧屏的框没人擦就成了幽灵框），再记新组并缓存每条的包围盒
    /// ——文字标注的包围盒走 GDI 实测，逐帧现算养不起。<b>这里不提交</b>：
    /// 提交时机由调用方定（起手要立刻 Flush，收橡皮筋要先清余像再 Flush）。
    /// </summary>
    [Fact]
    public void SetSelectionReplacesTheGroupAndCachesTheirBoxes()
    {
        var set = SourceGate.MethodBody(All(), "private static void SetSelection(Screen screen, IReadOnlyList<Annotation> marks)");
        Assert.True(InnerBody(set).TrimStart().StartsWith("DropSelection();", StringComparison.Ordinal),
            "换组之前必须先收旧的");
        Assert.Contains("_selected = marks;", set);
        Assert.Contains("_selectionBoxes = marks.Select(m => m.Bounds()).ToList();", set);
        Assert.Contains("screen.Dirty.Add(SelectionFootprint());", set);
        Assert.DoesNotContain("Flush", set);
    }

    /// <summary>
    /// 收掉选中那一套：占过的地方先<b>取</b>再清（清完再取就取不到了），<c>_press</c> 的选择两态一并复位
    /// ——拖动被 Esc／历史操作打断后，迟到的移动与松手必须无害。两块屏（旧选中在新屏、橡皮筋在旧屏）
    /// 各交各的；拖动烤进持久层的偏移只有<b>真的偏过</b>才需要重烤复原。
    /// </summary>
    [Fact]
    public void DropSelectionCleansBothScreensAndResetsThePress()
    {
        var drop = SourceGate.MethodBody(All(), "private static void DropSelection()");
        Assert.True(drop.IndexOf("var boxPrint", StringComparison.Ordinal)
                    < drop.IndexOf("_selected = null;", StringComparison.Ordinal),
            "footprint 必须先取后清");
        Assert.Contains("var hadOffset = _selectOffset.X != 0 || _selectOffset.Y != 0;", drop);
        Assert.Contains("if (_press is Press.SelectMove or Press.SelectBand) _press = Press.None;", drop);
        Assert.Contains("if (hadOffset) Recomposite(boxScreen);", drop);
        Assert.Contains("if (bandScreen is not null && bandScreen != boxScreen)", drop);
        Assert.Contains("Flush(boxScreen);", drop);
    }

    /// <summary>
    /// 选中框与橡皮筋画在<b>提交缓冲</b>上（持久层之上、光晕之前），⛔ 不进持久层——
    /// 快照从持久层合成，选中框跟着走的话"复制／存图"会带走一圈蓝框。
    /// 每次提交都补画：提交那一块先被 <c>CopyRect</c> 从持久层铺回来，不补画的话
    /// 外来脏区（光晕、荧光段）扫过框就会啃掉一块。
    /// </summary>
    [Fact]
    public void SelectionChromeLivesOnTheFlushBufferOnly()
    {
        var all = All();
        var flush = SourceGate.MethodBody(all, "private static void Flush(Screen screen)");
        Assert.True(flush.IndexOf("CanvasCompositor.CopyRect(", StringComparison.Ordinal)
                    < flush.IndexOf("PaintSelectionChrome(screen, rect);", StringComparison.Ordinal),
            "选中框必须画在重铺之后");
        Assert.True(flush.IndexOf("PaintSelectionChrome(screen, rect);", StringComparison.Ordinal)
                    < flush.IndexOf("CanvasCompositor.PaintGlow(", StringComparison.Ordinal),
            "选中框必须画在光晕之前");
        var chrome = SourceGate.MethodBody(all, "private static void PaintSelectionChrome(Screen screen, IntRect rect)");
        Assert.Contains("screen.Window.Pixels", chrome);
        Assert.DoesNotContain("Persistent", chrome);
    }

    /// <summary>
    /// 空心矩形按<b>四条边各自与裁剪区求交</b>：clip 常常只盖住框的中间——先把矩形与 clip 求交、
    /// 再对交出来的矩形画边，会在框内部画出一条假线、真正该补的那段边反而漏掉
    /// （外来脏区扫过框啃掉一块的那一族）。这里反钉"先 Intersect 再画"那种写法。
    /// </summary>
    [Fact]
    public void ChromeRectClipsEachSideOnItsOwn()
    {
        var rect = SourceGate.MethodBody(All(),
            "private static void ChromeRect(uint[] pixels, int width, int height, IntRect box, IntRect clip)");
        Assert.DoesNotContain("Intersect", rect);
        Assert.Contains("Math.Max(box.X, clip.X)", rect);
        Assert.Contains("Math.Min(box.Right, clip.Right)", rect);
        Assert.Equal(2, SourceGate.Count(rect, "for (var side = 0; side < 2; side++)"));
        Assert.Equal(2, SourceGate.Count(rect, "SelectionBlue"));
    }

    /// <summary>
    /// 退出画布那一下把「选择」的状态<b>直接归零</b>，不走 <c>DropSelection</c>：
    /// 它要提交的那几块缓冲（持久层、窗口像素）此刻正在被拆，对着正在拆的缓冲提交一次轻则白忙、重则崩。
    /// 注释里那句"不走 DropSelection"是这条禁项的说明书，所以这条读抹注释的那份。
    /// </summary>
    [Fact]
    public void CloseBoardHostZeroesTheSelectStateWithoutFlushing()
    {
        var close = SourceGate.MethodBody(All(), "public static void CloseBoardHost()");
        Assert.Contains("_selectTool = false;", close);
        Assert.Contains("_selected = null;", close);
        Assert.Contains("_bandTo = null;", close);
        Assert.DoesNotContain("DropSelection", close);
    }

    /// <summary>
    /// 条子只是投影：那颗灯亮不亮读 <c>CanvasService.IsSelectTool</c>，按下去只发 <c>ToggleSelectTool</c>；
    /// 亮着选择时笔那几颗一律让位——同一拍既亮选择又亮一支笔，就是"UI 是第二个状态机"（§3.3）的开头。
    /// 状态行先说选择（它是唯一一支不落墨的，不说它就只剩"软件坏了"一种解释），穿透与否跟着那两颗键走。
    /// </summary>
    [Fact]
    public void TheBarProjectsTheSelectState()
    {
        var bar = SourceGate.Code(SourceGate.ReadRepoFile(Toolbar));
        Assert.Contains("private void Select_Click(object sender, RoutedEventArgs e) => CanvasService.ToggleSelectTool();", bar);
        var refresh = SourceGate.MethodBody(bar, "private void Refresh()");
        Assert.Contains("var selecting = CanvasService.IsSelectTool;", refresh);
        Assert.Contains("Highlight(SelectButton, selecting);", refresh);
        Assert.Contains("Highlight(PenButton, !selecting && CanvasService.Tool == CanvasTool.Pen);", refresh);
        var status = SourceGate.MethodBody(bar, "private string StatusText()");
        Assert.Contains("Locale.Fmt(\"canvas.status.selectThrough\", Locale.T(\"canvas.btn.through\"))", status);
        Assert.Contains("Locale.Fmt(\"canvas.status.select\");", status);
    }

    /// <summary>
    /// 选中框那颗蓝与条子高亮那颗蓝是<b>同一颗</b>：用户对"选中"的认读一半靠框、一半靠按钮亮起，
    /// 两颗蓝差一档就是"框住的和我点亮的好像不是一回事"。字节序是这块板子统一的 BGRA 预乘。
    /// </summary>
    [Fact]
    public void TheBoxesWearTheSameBlueAsTheBarHighlight()
    {
        Assert.Contains("private const uint SelectionBlue = 0xFF2D6EC4;", All());
        Assert.Contains("Color.FromArgb(0xFF, 0x2D, 0x6E, 0xC4)", SourceGate.Code(SourceGate.ReadRepoFile(Toolbar)));
    }
}
