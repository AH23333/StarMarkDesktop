#nullable enable
using System;
using System.IO;
using System.Linq;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 截图标注工具条的<b>唯一真源</b>闸门（扫源码，不跑界面）。
/// <para>
/// 为什么需要这条：工具条上的按钮"有哪些"这件事，现在只存在于 <see cref="AnnotationTool"/> 一张表里，
/// 界面按它生成。一旦有人图省事在 XAML 里手写一个按钮，就变成两份事实——
/// 之后加个工具，模型里有、条上没有（或反过来），而这种错**只有跑起来才看得见**，正是最难自查的那类。
/// </para>
/// </summary>
public sealed class CaptureOverlayGateTests
{
    /// <summary>
    /// 读截图/贴图的 XAML 或代码侧。<b>代码侧要读整个 partial 文件集</b>（批次 WF-2b 把它拆成了
    /// CaptureOverlayWindow.*.cs 若干份）：只读主文件的话，"XAML/代码里不许出现 X"这类禁项守门
    /// 会因为方法搬了家而假绿——那是比红测更糟的失败方式。
    /// </summary>
    private static string ReadOverlay(bool xaml)
        => xaml
            ? SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml")
            : SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");

    /// <summary>
    /// <b>这条是给"守门本身"作的保</b>：截图/贴图的代码侧拆成多个 partial 文件之后，读法一旦退回单个文件
    /// （<c>SourceGate</c> 里那个 <c>Foo.xaml.cs</c> 词干 bug 就是这么来的——glob 只命中主文件自己），
    /// 那些"锚点必须命中 1 处"与"不许出现 X"的守门会<b>静默变成永远通过</b>，比红测危险得多。
    /// 这里钉住：搬进分段文件的方法，读整套时必须还看得见。
    /// </summary>
    [Fact]
    public void TheCodeSideGatesReadEveryPartialFile()
    {
        var all = SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        Assert.Contains("private void DragTo", all);                       // 搬进 .Pointer.cs
        Assert.Contains("private void DropSelection", all);                // 搬进 .Selection.cs
        Assert.Contains("private void PlaceNumber", all);                  // 搬进 .Draw.cs
    }

    [Fact]
    public void ToolStripIsGeneratedNotHandWritten()
    {
        var xaml = ReadOverlay(xaml: true);
        var bar = ActionBar(xaml);
        // 整条由代码按模型生成：XAML 里出现任何按钮/下拉/文字，就变成"有哪些工具"的第二份事实，
        // 而那种分岔只有跑起来才看得见。
        Assert.Contains("x:Name=\"BarRow\"", xaml);
        Assert.DoesNotContain("<Button", bar);
        Assert.DoesNotContain("DropDownButton", bar);
        Assert.DoesNotContain("<RadioButton", bar);
        Assert.DoesNotContain("<Flyout", bar);
        Assert.DoesNotContain("Content=\"", bar);      // 条上不写字：说明只挂在每颗按钮的 ToolTip 上
        // 批次 WI 把选择栏改成条子自己的一行（贴图态那扇独立小窗里放不下弹出层）；
        // 批次 WP 按用户裁决<b>删掉了"悬停说明"那一行</b>：窗宽＝内容宽，几百像素的一行字会把
        // 条子整个推宽（悬停哪颗跳一次；批次 WQ 改右缘对齐后跳的是左边缘）。
        // 所以条子里只许有按钮行＋选择栏行，一个文字元素都不留——写了字就又变成"这句话是什么"的第二份事实，
        // 而且宽度不稳。
        Assert.Equal(0, Count(bar, "<TextBlock"));
        Assert.DoesNotContain("Text=\"", bar);
        Assert.DoesNotContain("BarHint", bar);
        Assert.Contains("x:Name=\"BarPicker\"", bar);
    }

    [Fact]
    public void EveryToolStripButtonIsBuiltFromTheModel()
    {
        var cs = ReadOverlay(xaml: false);
        // 条上的清单来自 Core 的分组表，不再自己数枚举：分组（哪些收进选择栏、哪些各占一颗）
        // 一旦在界面里重推一遍，"新工具落进哪一组"就又变成两处各说一次。
        Assert.Contains("AnnotationTools.Shapes", cs);
        Assert.Contains("AnnotationTools.Brushes", cs);
        Assert.DoesNotContain("Enum.GetValues<AnnotationTool>()", cs);
        Assert.Contains("BarRow.Children.Add", cs);
        Assert.Contains("Annotation.Palette", cs);
        Assert.Contains("Annotation.ThicknessSteps", cs);
        Assert.Contains("Annotation.ThicknessNames", cs);
        Assert.Contains("Annotation.ToolName(tool)", cs);
        Assert.Contains("Annotation.ToolHint(tool)", cs);
    }

    /// <summary>新加工具时它必须有自己看得出来的图标：漏一条臂就会静默落进兜底形状，
    /// 于是条上出现两颗一模一样的点，用户挑的不是想要的那个（图标没有文字兜底，撞车就是真的撞车）。</summary>
    [Fact]
    public void EveryToolHasItsOwnIcon()
    {
        var body = SourceGate.MethodBody(ReadOverlay(xaml: false), "private static UIElement ToolIcon");
        foreach (AnnotationTool tool in Enum.GetValues<AnnotationTool>())
            Assert.Equal(1, SourceGate.Count(body, $"AnnotationTool.{tool} =>"));
        Assert.Equal(Enum.GetValues<AnnotationTool>().Length, SourceGate.Count(body, "AnnotationTool."));
    }

    /// <summary>
    /// 条子<b>静息时仍是一行</b>（用户反馈的原话是"太大"）：按钮只许排一行，选择栏默认收起。
    /// <para>批次 WI 之后条子会随内容长高（那扇独立小窗按实测尺寸跟着长），所以"一行"这条判据
    /// 从"结构上只有一行"改成"多出来的行必须是收起的容器"——出现纵向 StackPanel 就是把按钮排成了两行，
    /// 那才是真的又长回去了。</para>
    /// </summary>
    [Fact]
    public void ToolStripRestsAsASingleCompactRow()
    {
        var bar = ActionBar(ReadOverlay(xaml: true));
        Assert.Equal(1, Count(bar, "<StackPanel"));
        Assert.Contains("Orientation=\"Horizontal\"", bar);
        Assert.DoesNotContain("Orientation=\"Vertical\"", bar);
        Assert.True(Count(bar, "<Border") == 1, "工具条里再套一个 Border 就是又开了一层");
        // 条子本体 + 选择栏：两处 Collapsed，缺一条就是"什么都没点的时候条子先胖了一行"。
        // （批次 WP 之后没有第三处——"悬停说明"那一行按用户裁决删了：窗宽＝内容宽，
        // 那行字会让贴图条在悬停时变宽、边缘跟着跑。）
        Assert.Equal(2, Count(bar, "Visibility=\"Collapsed\""));
    }

    /// <summary>工具条那一截 XAML（从 ActionBar 到下一个浮层）。</summary>
    private static string ActionBar(string xaml)
        => xaml[xaml.IndexOf("x:Name=\"ActionBar\"", StringComparison.Ordinal)..
            xaml.IndexOf("x:Name=\"HintChip\"", StringComparison.Ordinal)];

    private static int Count(string haystack, string needle)
    {
        var total = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            total++;
        return total;
    }

    [Fact]
    public void EveryToolStillHasANameAndHint()
    {
        // 与上面那条配对：新增一个枚举值时，名字与说明必须一起补上，否则这里先红
        foreach (var tool in Enum.GetValues<AnnotationTool>())
        {
            Assert.NotEqual(tool.ToString(), Annotation.ToolName(tool));
            Assert.False(string.IsNullOrEmpty(Annotation.ToolHint(tool)));
        }
    }

    /// <summary>拖动中途的采样点归谁管，只能在模型里说一次（Annotation.IsTwoPointTool）。
    /// 界面上若改回"一律追加"，绘制端就又要猜哪一点才是另一端——真机反馈"松手后图形变得非常小"就是这么来的。</summary>
    [Fact]
    public void StrokeSamplesFollowTheModelsTwoPointRule()
    {
        var body = SourceGate.MethodBody(ReadOverlay(xaml: false), "private void ExtendStroke");
        Assert.Contains("Annotation.IsTwoPointTool", body);
        Assert.Contains("points[^1] = local", body);            // 两点工具：覆盖最后一点，而不是再追加一个
    }

    [Fact]
    public void CommittedImageGoesThroughThePainterOnEveryExit()
    {
        // 四个落点必须吃同一份"合成后的像素"。漏掉一个就会出现"预览有标注、存出来没有"，
        // 而这件事只在用户把图发出去之后才发现。
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("AnnotationPainter.Render(", cs);
        foreach (var call in new[]
        {
            "ScreenshotService.CopyPixelsAsync(", "ScreenshotService.SavePixelsAsync(",
            "ScreenshotService.PinPixels(", "OcrService.CopyTextFromPixelsAsync(",
        })
        {
            Assert.Contains(call, cs);
        }
        Assert.DoesNotContain("CopySelectionAsync(_frame", cs);
        Assert.DoesNotContain("SaveSelectionAsync(_frame", cs);
        Assert.DoesNotContain("PinSelection(_frame", cs);
        Assert.DoesNotContain("CopyTextFromSelectionAsync(_frame", cs);
    }

    [Fact]
    public void AnnotationLayerSitsUnderTheSelectionChrome()
    {
        // 标注层必须在压暗层之前：摆到 SelRect 之后，刚烤好的图会盖住选区描边的内半边
        var xaml = ReadOverlay(xaml: true);
        Assert.True(xaml.IndexOf("x:Name=\"AnnotateLayer\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"DimLayer\"", StringComparison.Ordinal));
    }
    /// <summary>
    /// 就地输入那一行字，每条离开它的出口都必须先落笔。<b>真机反馈"文字编辑功能无效"就是这类出口漏了</b>：
    /// 在画布上点第二下会把刚打的字清空、没按 Enter 就点动作按钮会把字丢掉、
    /// 焦点没进输入框时 Enter 会变成"复制整张截图"并关掉遮罩——三种表现都指向同一句"我打的字呢"。
    /// </summary>
    [Fact]
    public void TypedTextIsCommittedOnEveryWayOut()
    {
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, "private void Commit(CommitAction action)"));
        Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, "private void BeginStroke"));
        Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, "private void SetTool"));
        // 点进输入框不算"在选区里起一笔"（否则第二次点击进来就把这行清空了）
        Assert.Contains("TextEditor.PointerPressed += (_, e) => e.Handled = true;", cs);
        // 点一下就要能直接打字（用户："不需要再次点击文字编辑框内区域才能输入文字"）：
        // 刚把宿主从 Collapsed 放出来的那一帧 Focus() 会当场失败，所以补一次布局再要、仍不行逐帧重试，
        // 实在拿不到才说实话。锚点钉的是这套动作，不是某一句 if。
        var focus = SourceGate.MethodBody(cs, "private void TakeEditorFocus");
        Assert.Contains("TextEditor.UpdateLayout();", focus);
        Assert.Contains("if (TextEditor.Focus(FocusState.Programmatic)) return;", focus);
        Assert.Contains("Root.DispatcherQueue.TryEnqueue", focus);
        Assert.Contains("TakeEditorFocus();", SourceGate.MethodBody(cs, "private void BeginTextEdit"));
        // Enter 换行＝TextBox 自己吃掉这个键；Enter 再也不是"落笔"的出口（提交出口见 EndTextEditing 那条）
        Assert.Contains("AcceptsReturn=\"True\"", ReadOverlay(xaml: true));
        Assert.DoesNotContain("case VirtualKey.Enter:", SourceGate.MethodBody(cs, "private void TextEditor_KeyDown"));
        // 焦点跑掉时 Enter/Esc 也不能被当成"复制整张 / 取消这一屏"
        Assert.Contains("if (_editingText && e.Key is VirtualKey.Enter or VirtualKey.Escape)",
            SourceGate.MethodBody(cs, "private void Root_KeyDown"));
        // 点撤销/重做/清空这三颗按钮同样要落笔（它们不经过 SetTool，也不经过 Commit）
        foreach (var button in new[] { "Undo_Click", "Redo_Click", "Clear_Click" })
            Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, $"private void {button}"));
    }

    /// <summary>
    /// <b>输入框不许挂在"失去焦点"上结束</b>。真机反馈："必须长按才能出现黄色矩形进行输入，
    /// 输入完文字后松开鼠标表示完成编辑"——按下那一下把焦点给了输入框，松开时焦点回到遮罩那一层，
    /// 挂在 LostFocus 上的落笔于是当场把这一行结掉，用户学到的用法就变成了"按住不放"。
    /// 落笔时机只许由看得见的动作决定（Enter / 点画布别处 / 切工具 / 点动作按钮 / Esc 丢弃）。
    /// </summary>
    [Fact]
    public void TextEditorNeverCommitsOnLostFocus()
    {
        Assert.DoesNotContain("LostFocus", ReadOverlay(xaml: true));
        Assert.DoesNotContain("TextEditor_LostFocus", ReadOverlay(xaml: false));
    }

    /// <summary>折线是"点出来的"，所以收口必须有两个出口（键盘与鼠标各一个），
    /// 而换选区时正在点的那一条要丢掉——留着它会在新框里画出一个对不上位置的圈。</summary>
    [Fact]
    public void PolyLineIsClosedOnBothEndsAndDroppedOnNewSelection()
    {
        var cs = ReadOverlay(xaml: false);
        var keys = SourceGate.MethodBody(cs, "private void Root_KeyDown");
        Assert.Contains("FinishPolyLine(commit: e.Key == VirtualKey.Enter)", keys);   // Enter 收口 / Esc 丢掉
        Assert.Contains("Root.DoubleTapped", cs);                                     // 双击也是收口（只给键盘＝找不到出口）
        Assert.Contains("PlaceVertex(local);", cs);                                  // 每一按钉一个顶点，不走拖动那套
        // 批次 PU：ResetAnnotations 删了，"重新框选时丢掉没收口的折线"由 BeginRegionDrag 承担
        Assert.Contains("FinishPolyLine(commit: false)", SourceGate.MethodBody(cs, "private void BeginRegionDrag"));
    }
    /// <summary>
    /// 就地输入那一块黄底必须<b>左上角对齐</b>。Grid 的子元素默认 Stretch：只给它 Margin 定位，
    /// 它会从点击处一路铺到屏幕右下角（真机反馈的"点击处到右下角的黄色矩形"），
    /// 输入框本身也跟着拉成一条巨大的东西——这一条只能在 XAML 里守，代码里看不出来。
    /// </summary>
    [Fact]
    public void TextEditorIsAnchoredTopLeftInsteadOfStretching()
    {
        var xaml = ReadOverlay(xaml: true);
        var host = SourceGate.Between(xaml, "x:Name=\"TextEditorHost\"", "</Border>");
        Assert.Contains("HorizontalAlignment=\"Left\"", host);
        Assert.Contains("VerticalAlignment=\"Top\"", host);
        // 上限必须存在（没有它输入框一路长到 Margin 之外），但它的值跟着屏幕算，所以住在代码里：
        // XAML 里写死一个数＝宽屏上说谎、窄屏上截字
        Assert.DoesNotContain("MaxWidth=", host);
        Assert.Contains("TextEditor.MaxWidth = Math.Max(180,",
            SourceGate.MethodBody(ReadOverlay(xaml: false), "private void BeginTextEdit"));
    }

    /// <summary>
    /// 打码的拖动预览必须是<b>真像素 + 增量</b>。
    /// <para>"真像素"这条来自真机反馈：预览画一圈近似环、松手才糊 ⇒ 用户看到的是"点出红圆点、
    /// 画的过程中什么都不显示"。"增量"这条来自同一批的第二次反馈：<b>打码速度远落后于鼠标移动速度</b>——
    /// 每帧从底图重烤整张的代价是 O(选区面积 × 已有标注数)，手一快就落在后面。</para>
    /// <para>所以这里同时钉两件事：预览只补新段（<c>AnnotationPainter.Paint(scratch</c>），
    /// 且拖动路径里<b>不许再出现全量重烤</b>（改回 <c>Rebake(...)</c> 当场红）。</para>
    /// </summary>
    [Fact]
    public void MosaicPreviewsInRealPixelsAndOnlyPaintsTheNewSegment()
    {
        var cs = ReadOverlay(xaml: false);
        var preview = SourceGate.MethodBody(cs, "private void PaintPreview");
        Assert.Contains("AnnotationPainter.Paint(scratch", preview);
        Assert.DoesNotContain("Rebake(", preview);
        Assert.DoesNotContain("AnnotationTool.Mosaic", SourceGate.MethodBody(cs, "private void DrawLive"));
        // 按下那一下就要能糊住一格：起点先存两份（MosaicBrush 逐段走，两个重合的点正好是笔尖那一格）。
        // 锚点取"存两份"这一句本身——只搜工具名会被注释或分支条件冒充成绿灯。
        Assert.Contains("new List<PixelPoint> { local, local }", SourceGate.MethodBody(cs, "private void BeginStroke"));
        Assert.Contains("StartMosaicScratch(local)", SourceGate.MethodBody(cs, "private void BeginStroke"));
        // 增量画布以"已提交的那张"为起点，而提交仍以全烤为准：两条口径不能各画各的
        Assert.Contains("_composed = composed", SourceGate.MethodBody(cs, "private void Rebake()"));
        Assert.Contains("Rebake();", SourceGate.MethodBody(cs, "private void EndStroke"));
    }

    // ────────── 批次 RE-3：画完还能挪位置 / 缩放 / 转方向 ──────────

    /// <summary>
    /// "点住已有的那一条就拖得动"这件事必须排在"起一笔"之前，但<b>不许抢走打字与钉顶点的那一下</b>。
    /// <para>反过来（先 BeginStroke 再考虑选中）就是"文字编辑永远进不去"那一类：按下被吃掉了，
    /// 用户学到的用法变成"必须先长按"。而在输入框里/折线顶点之间被抢走，则等于把刚打的一行字丢掉。</para>
    /// </summary>
    [Fact]
    public void GrabbingAnExistingMarkBeatsDrawingButNeverStealsATypingPress()
    {
        var cs = ReadOverlay(xaml: false);
        var pressed = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        // 批次 PU 起"框内/框外"落笔都收进 BeginToolStroke：抓旧的那条仍必须在落笔之前问
        Assert.True(pressed.IndexOf("TryBeginGrab", StringComparison.Ordinal)
            < pressed.IndexOf("BeginToolStroke(", StringComparison.Ordinal),
            "抓住旧的一条必须在起新的一笔之前问，否则永远拖不动已画的东西");
        var grab = SourceGate.MethodBody(cs, "private bool TryBeginGrab");
        Assert.Contains("if (_editingText || _polyLine is not null) return false;", grab);
        // 把手只有一个算法出口：画它的那一处与判"按中了没有"的那处必须是同一个坐标
        var handle = SourceGate.MethodBody(cs, "private PixelPoint RotateHandle");
        // 贴到画面上沿时夹回框内，否则这颗点永远点不到。<b>边界要在底图像素这一层比</b>：`box` 是选区内坐标，
        // 而 `_selection.Y` 是虚拟桌面坐标——副屏在主屏下方时那个不等式对每条标注都成立（把手全被塞进框里），
        // 在主屏上方时又永不成立（贴顶那颗画到窗外）。原点在 0 时两种写法同值 ⇒ 只有多屏才露出来。
        Assert.Contains("box.Y - lift < 0", handle);
        Assert.DoesNotContain("lift < selection.Y", handle);
        // 按下那一点的三条豁免与"问模型"这唯一出口
        Assert.Contains("mark.GrabAt(local, RotateHandle(mark), SlopInSource(MoveSlop))", grab);
        // 容差是手感量，只能按屏幕定：贴图缩到 0.2× 时按底图像素定的常量会小到抓不到（放大时反过来误抓）。
        // 截图那条链 _sourceScale 恒为 1 ⇒ 换算后与常量一字不差，所以这条改动不影响现行为。
        Assert.Contains("screenPixels / _sourceScale", SourceGate.MethodBody(cs, "private int SlopInSource"));
        foreach (var site in new[] { "HitTest(_history.Marks, local, SlopInSource(SelectionSlop))",
                                     "HitTest(_history.Marks, at, SlopInSource(SelectionSlop))",
                                     "Near(_dragAnchor, _dragLast, SlopInSource(SelectionSlop))" })
            Assert.Contains(site, cs);
    }

    /// <summary>
    /// "这一按是移动、缩放还是旋转"只许模型说一次。
    /// <para>真机反馈"拖动文字后字会变大"就长在这个判据上：界面里自己数了一遍角点，而角点容差跟着<b>框的尺寸</b>
    /// 收缩——一行字只有二十来像素高，四角那一圈容差正好压在用户抓字的位置上，于是"拖一下"被判成"拖角"，
    /// 字号一路涨。更糟的是这份判据在界面里<b>一条断言都造不出来</b>（测试工程引用不到 UI），
    /// 只能等用户在真机上撞见。</para>
    /// <para>所以这里钉的是结构：判据调一次 <c>GrabAt</c>，界面里不再出现容差数字、也不定义第二套抓取枚举。
    /// 判据搬回可测的那一层后，"框内每一像素都不许是缩放"才成为 <c>AnnotationTests</c> 里逐像素扫得过的一条性质。</para>
    /// </summary>
    [Fact]
    public void TheGrabDecisionLivesOnlyInTheModel()
    {
        var cs = ReadOverlay(xaml: false);
        var grab = SourceGate.MethodBody(cs, "private bool TryBeginGrab");
        Assert.Contains("mark.GrabAt(", grab);
        Assert.DoesNotContain("enum Grab", cs);                  // 抓取取值只有一套（界面顶多起个别名）
        Assert.DoesNotContain(".Corners().Any(", cs);            // 自己数角点＝把判据搬回不可测的那一层
        Assert.DoesNotContain("HandleSlop", grab);               // 容差由模型取，界面里不写死数字
        Assert.Contains("using Grab = StarMark.Core.Capture.AnnotationGrab;", cs);
        Assert.Contains("mark.WithScalePivotTowards(local)", grab);    // 缩放轴由模型挑，界面不自己算角点
        // 批次 RH-2/RH-3：缩放轴与拖动算式都不许留在界面里（现在它们住 Annotation.ScalePivot / DraggedBy，
        // 前者由 AnnotationTests 逐角断言，后者由本文件末尾那条闸门钉调用形状）
        Assert.DoesNotContain("original.ScalePivot", cs);
        Assert.DoesNotContain("Math.Atan2", cs);           // 角度换算只有一处（模型），界面不再自己算
    }

    /// <summary>
    /// 拖动过程每帧只许做"一次整块复制 + 一条重画"。<b>每帧从底图重烤全部标注</b>就是
    /// 真机反馈"打码速度远落后于鼠标移动速度"的同一个成因（代价 ∝ 选区面积 × 已有条数），
    /// 刚修好一处又在另一处复发是不可接受的。
    /// </summary>
    [Fact]
    public void DraggingRepaintsWithOneCopyInsteadOfRebakingEveryFrame()
    {
        var cs = ReadOverlay(xaml: false);
        var drag = SourceGate.MethodBody(cs, "private void DragTo");
        Assert.Contains("Buffer.BlockCopy(under, 0, canvas", drag);
        Assert.DoesNotContain("Render(", drag);
        Assert.DoesNotContain("Rebake(", drag);
        Assert.DoesNotContain("UnderDragBuffer", drag);
        // 底图整份准备只在"按下那一下"算一次；出现在每帧路径里就等于把全烤搬回拖动
        Assert.Contains("UnderDragBuffer(idx)", SourceGate.MethodBody(cs, "private bool TryBeginGrab"));
        Assert.DoesNotContain("UnderDragBuffer", SourceGate.MethodBody(cs, "private void PaintPreview"));
    }

    /// <summary>
    /// 历史一变（撤销 / 重做 / 清空 / 换选区）与起新的一笔，都必须把选中一起丢掉。
    /// <para>留着旧下标 = 框还画在原地、下一拖改的却是完全另一条标注，而且"哪条被选中"在屏幕上
    /// 看着是对的——这是最难从界面发现的一类错，所以只能在结构上钉死。</para>
    /// </summary>
    [Fact]
    public void EveryHistoryChangeDropsTheSelection()
    {
        var cs = ReadOverlay(xaml: false);
        foreach (var signature in new[]
        {
            "private void Undo()", "private void Redo()", "private void Clear_Click",
            "private void BeginRegionDrag", "private void BeginStroke", "private void PlaceVertex",
            "private void DeleteSelected",
        })
            Assert.Contains("DropSelection()", SourceGate.MethodBody(cs, signature));
        // 把手只有这一处会画：再开一处就会出现"改了框没跟着改"的第二份事实
        Assert.Equal(1, Count(cs, "private void DrawSelectionHandles("));
        Assert.Contains("LiveLayer.Children.Clear()", SourceGate.MethodBody(cs, "private void DrawSelectionHandles"));
    }

    /// <summary>
    /// "点一下没拖出形状"不再是静默丢掉，而是去选中脚下那一条；打码除外（它的"点一下"本身要糊住一格，
    /// 自动选中后下一点就变成"移动整条"，正对着批次 RE-2 才修好的那件事）。
    /// </summary>
    [Fact]
    public void ATapThatDrewNothingSelectsWhatIsUnderIt()
    {
        var cs = ReadOverlay(xaml: false);
        var end = SourceGate.MethodBody(cs, "private void EndStroke");
        Assert.Contains("SelectAtTap(points[0])", end);
        Assert.Contains("mark.Tool == AnnotationTool.Mosaic ? null", end);
        Assert.Contains("AnnotationPainter.HitTest", SourceGate.MethodBody(cs, "private void SelectAtTap"));
        // 刚打完的那一行字也要立刻可拖：写完紧接着改位置是最常见的顺序
        var text = SourceGate.MethodBody(cs, "private void EndTextEditing");
        Assert.Contains("_selected = _history.Count - 1;", text);
        Assert.Contains("DrawSelectionHandles();", text);
    }

    /// <summary>删除选中的那条要有键盘出口（Delete 与 Backspace 各一条，只给一个就等于找不到）。</summary>
    [Fact]
    public void TheSelectedMarkHasAKeyboardExit()
    {
        var cs = ReadOverlay(xaml: false);
        var keys = SourceGate.MethodBody(cs, "private void Root_KeyDown");
        // 两把键都只在"没在打字"时才删条目：闸门跟到 !_editingText 那半个条件，
        // 缺了它，退格就会在编辑中途删掉整条（真机反馈），而这条出口也就成了陷阱。
        Assert.Contains("case VirtualKey.Delete when _selected is not null && !_editingText:", keys);
        Assert.Contains("case VirtualKey.Back when _selected is not null && !_editingText:", keys);
        Assert.Contains("DeleteSelected();", keys);
        Assert.Contains("_history.RemoveAt(index)", SourceGate.MethodBody(cs, "private void DeleteSelected"));
    }

    /// <summary>
    /// 变换的算术只许住在模型里：绘制端与选择框两端都只能调 <c>TransformedPoints()</c> / <c>Bounds()</c>。
    /// 界面上自己再算一次旋转矩阵，就是"预览对、存出来偏了"那类多次换算误差的老路。
    /// </summary>
    [Fact]
    public void TransformMathLivesOnlyInTheModel()
    {
        var painter = SourceGate.ReadRepoFile("src/StarMark.Core/Capture/AnnotationPainter.cs");
        var paint = SourceGate.MethodBody(painter, "public static void Paint");
        Assert.Contains("mark.TransformedPoints()", paint);
        Assert.Contains("mark.DrawFontHeight", paint);
        // 角度在这一层只是"递出去"（给 GdiTextDrawer），不在这里被换算：绘制编排里出现 Math.
        // 就是第二次换算的入口，而"预览对、存出来偏了"那类 bug 恰恰长在多次换算上。
        Assert.Contains("mark.Rotation", paint);
        Assert.DoesNotContain("Math.", paint);

        var overlay = ReadOverlay(xaml: false);
        var handles = SourceGate.MethodBody(overlay, "private void DrawSelectionHandles");
        Assert.Contains("mark.Bounds()", handles);
        Assert.Contains("mark.Corners()", handles);          // 把手摆在哪儿由模型说，界面只负责画
        Assert.Contains("RotateHandle(mark)", handles);      // 旋转那颗点由模型的外接框推出来，界面不自己算
        // 界面读原始角度/自己算三角函数＝第二处换算，预览与交付会在两次换算的差里分开
        Assert.DoesNotContain("mark.Rotation", handles);
        Assert.DoesNotContain("Math.Sin", handles);
        Assert.DoesNotContain("Math.Cos", handles);
    }

    /// <summary>
    /// 松手算结果时，"这一拖是什么"必须是<b>传进来的参数</b>，不许是读出来的字段。
    /// <para>真机反馈"拖动文字会让文字变大"查到这一步才见底：`TryBeginGrab` 已正确判成 Move
    /// （日志 <c>[AnnoGrab] Move</c> 为证），而 <c>EndDrag</c> 先 <c>_grab = Grab.None</c> 再调
    /// <c>Preview</c>，<c>Preview</c> 里 <c>switch (_grab)</c> 拿到的就是 <c>None</c> ⇒ 落进
    /// <c>default</c>（＝缩放分支），倍数按"按下点到松手点"算，一路夹到上限 6×。
    /// 拖动过程中显示是对的（那时字段还在），只有松手才变——所以任何"看预览"的验证都抓不住它，
    /// 模型层的逐像素用例也抓不住（它判的是 <c>GrabAt</c>，不是这一步）。</para>
    /// <para>钉法用 <c>static</c>：静态方法读不到实例字段，这条不靠人自觉，编译器替我们守。</para>
    /// </summary>
    [Fact]
    public void TheDragKindIsPassedInNotReadFromAFieldThatEndDragJustCleared()
    {
        var cs = ReadOverlay(xaml: false);
        // 界面里不再有任何按可变字段分支的拖动算式
        Assert.DoesNotContain("switch (_grab", cs);
        var dragTo = SourceGate.MethodBody(cs, "private void DragTo");
        var end = SourceGate.MethodBody(cs, "private void EndDrag");
        Assert.Contains("original.DraggedBy(_dragAnchor, local, _grab)", dragTo);
        // 松手这一步用的必须是"清掉之前抄下来的那份"——原地读 _grab 就是这条缺陷的原件
        Assert.Contains("var grab = _grab;", end);
        Assert.Contains("original.DraggedBy(_dragAnchor, _dragLast, grab)", end);
        Assert.DoesNotContain("DraggedBy(_dragAnchor, _dragLast, _grab)", end);
    }

    /// <summary>
    /// 点一行<b>已经写好的</b>字＝回去改它，而不是在旁边再写一条（用户列的期望原文：
    /// "用户随时可以点击之前编辑的文字，继续在文字的编辑框内进行删减修改"）。
    /// <para>两条入口都要汇到"带着原文与下标开框"：① 那条字还没被选中 ⇒ 走命中测试；
    /// ② 它已选中、按下没移动就松手 ⇒ 走拖动那一步的"其实没拖"分支。真拖过就必须还是移动，
    /// 不能弹框——否则上一批才修好的"拖位置"又会被这次改动吃掉。</para>
    /// </summary>
    [Fact]
    public void ClickingAnExistingTextLineReopensItWithItsOwnText()
    {
        var cs = ReadOverlay(xaml: false);
        var stroke = SourceGate.MethodBody(cs, "private void BeginStroke");
        Assert.Contains("_history.Marks[index].Tool == AnnotationTool.Text", stroke);
        Assert.Contains("BeginTextEdit(local, _history.Marks[index], index)", stroke);
        var end = SourceGate.MethodBody(cs, "private void EndDrag");
        Assert.Contains("grab == Grab.Move && original.Tool == AnnotationTool.Text", end);
        Assert.Contains("BeginTextEdit(_dragAnchor, original, i)", end);
        // 落笔那一步对"改旧字"是替换而不是新增；删空了就是删掉那条（留一条没字的标注只会让人以为卡住）
        var commit = SourceGate.MethodBody(cs, "private void EndTextEditing");
        Assert.Contains("_history.Marks[existing] with { Text = text }", commit);
        Assert.Contains("{ _selected = existing; DeleteSelected(); return; }", commit);
        // Esc 是结束编辑而不是丢弃（"按一次 esc 退出文字编辑"，而"原已编辑输入的文字不会消失"）
        Assert.Contains("EndTextEditing(commit: true);", SourceGate.MethodBody(cs, "private void TextEditor_KeyDown"));
        // 编辑期间那一条不许再烤进画面：输入框压在它原来的位置上，两份一起画＝"红白两层文字"（真机反馈）
        Assert.Contains("_editingText && _editingIndex is { } hidden",
            SourceGate.MethodBody(cs, "private void Rebake"));
        // 也不许拿"当前调色板"去涂它：改旧字时用户可能已经换成别的颜色，那会让编辑中与落笔后是两个颜色
        Assert.Contains("_editingIndex is null ? ColourBgra : _editorColourBgra",
            SourceGate.MethodBody(cs, "private void ApplyEditorAccent"));
        // 输入框要盖在"真画出去那一格"上（Bounds 是旋转后的外接框，转过的字会偏到空处）
        var begin = SourceGate.MethodBody(cs, "private void BeginTextEdit");
        Assert.Contains("mark.TransformedPoints()[0] : local", begin);
        // 藏旧字这件事必须长在开框这一步里。Rebake 的排除只在下标已经写进字段之后才生效，而两条入口
        // 原先都只画把手不重烤 ⇒ 底下那份旧字一直留在画面里，与输入框叠成"红白两层"（真机反馈了两轮）。
        Assert.Contains("Rebake();", begin);
        // 改旧字之前，正在打的那一行必须先落笔：commit: false 就是真机反馈的"第二次编辑已经输入了文字，
        // 点旧字之后刚打的那一行直接没了"。这一路里不许再有丢弃式的收口。
        Assert.DoesNotContain("EndTextEditing(commit: false)", stroke);
        // 而且落笔要排在命中之前：落笔那一步可能删掉一条（把一条改成空字＝删那条），
        // 先算好的下标就会指着隔壁那条，"改这一行"静默变成"写进另一行"。
        Assert.True(stroke.IndexOf("EndTextEditing(commit: true)", StringComparison.Ordinal)
                    < stroke.IndexOf("AnnotationPainter.HitTest(", StringComparison.Ordinal),
            "命中测试排在落笔之前＝下标可能已经被落笔那一步挪位，字会写进隔壁那条");
        // 退出编辑（换选区、清历史那条路）要把藏掉的旧字烤回来，否则它永久隐身：画不出又点不着
        Assert.Contains("if (index is not null) Rebake();", commit);
    }

    /// <summary>选中说明要长在遮罩窗上（悬停到几像素的把手才看得见＝没有提示）。</summary>
    [Fact]
    public void SelectionTipLivesOnTheOverlayNotInAToolTip()
    {
        var xaml = ReadOverlay(xaml: true);
        var tip = SourceGate.Between(xaml, "x:Name=\"SelectionTip\"", "</Border>");
        Assert.Contains("VerticalAlignment=\"Bottom\"", tip);
        Assert.Contains("Visibility=\"Collapsed\"", tip);
        Assert.Contains("Delete", tip);             // 删除要有键盘出口，而出口得写在用户看得见的这一句里
        Assert.Contains("SelectionTip.Visibility", SourceGate.MethodBody(ReadOverlay(xaml: false), "private void DropSelection"));
    }

    /// <summary>
    /// 就地输入那一块底板<b>不许是实色</b>。真机反馈："点击后不应出现黄色矩形，最好是透明但描边的边框"——
    /// 截图时要看的画面正被这块板子盖住。边界改由描边负责，而描边的颜色只有当前字色说得准（深浅底都要看得出来），
    /// 所以"字色"与"边框色"必须出自同一个出口；再出现一处直接写 <c>TextEditor.Foreground</c> 就是第二份事实。
    /// </summary>
    [Fact]
    public void TextEditorIsATranslucentOutlinedBoxNotASolidSlab()
    {
        var host = SourceGate.Between(ReadOverlay(xaml: true), "x:Name=\"TextEditorHost\"", "<TextBox");
        Assert.DoesNotContain("#EEFFFF00", host);            // 那一批实色黄底（0xEE alpha）不许回来
        Assert.Contains("BorderThickness=\"2\"", host);      // 边界靠描边，不靠填充
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("TextEditorHost.BorderBrush = brush", SourceGate.MethodBody(cs, "private void ApplyEditorAccent"));
        Assert.Equal(1, SourceGate.Count(cs, "TextEditor.Foreground"));   // 只有那一个出口在说"用哪个颜色"
    }

    /// <summary>
    /// 变换轴只由模型说一次：界面既不写 <c>Pivot</c>，也不自己算"绕哪一点转/放"。
    /// <para>批次 RF-1 先做成"界面给文字打一个居中轴点"，结果同一件事在两处各表达一次——
    /// 位置四窜修好后又留下一个"只有模型自己看"的第二份事实，RF-2 把它收回到 <c>Annotation.Origin</c>。</para>
    /// </summary>
    [Fact]
    public void TheOverlayNeverInventsItsOwnTransformAxis()
    {
        var cs = ReadOverlay(xaml: false);
        // 钉的是"界面自己碰轴点"这件事：既不给它赋值、也不读它——只许调模型给的那几个出口
        // （WithScalePivotTowards / ScalePivot / Origin）。RF-2 那版写成"不许出现 Pivot 这个词"，
        // 结果 RH-2 一调模型方法就误红：锚点要钉住动作，别钉住字面。
        Assert.DoesNotContain("Pivot =", cs);
        Assert.DoesNotContain(".Pivot", cs);
        Assert.Contains("mark.Bounds()", SourceGate.MethodBody(cs, "private void DrawSelectionHandles"));
    }

    // ────────── 批次 WI：贴图工具条住独立置顶窗（贴图窗＝画面，一格不加） ──────────

    /// <summary>
    /// 贴图窗的窗口矩形必须<b>等于画面矩形</b>。<para>
    /// 批次 WD-6 的"窗口 = 图 + 下面那一条"按构造不成立：WinUI 3 的客户区不能整块透明，
    /// 画面只填满"图那一段"，为条子加高的那一条没有像素可画 ⇒ 屏幕上一块黑
    /// （真机反馈两次："菜单栏还是和贴图同框，会造成局部黑块"）。
    /// 这里反钉那两个加法：一旦回来，黑块就回来。
    /// </para>
    /// </summary>
    [Fact]
    public void PinWindowIsExactlyTheImage_TheBarNeverGrowsIt()
    {
        var rect = SourceGate.MethodBody(ReadOverlay(false), "private IntRect PinWindowRect");
        Assert.Contains("if (!_pinned) return image;", rect);
        Assert.Contains("CaptureGeometry.PinOrigin(", rect);          // PJ 那条收边判据仍在
        Assert.DoesNotContain("barHeight", rect);
        Assert.DoesNotContain("barWidth", rect);
        Assert.DoesNotContain("PinBarStrip", ReadOverlay(false));     // 那条"量一条加高的"整体作废
    }

    /// <summary>条子只搬一次，且搬完必须留下"随宿主窗一起关"的钩子（否则留下一扇没有主人的条子窗）。</summary>
    [Fact]
    public void TheBarIsReparentedOnceAndClosedWithItsPin()
    {
        var draw = SourceGate.MethodBody(ReadOverlay(false), "private void AttachBarWindow");
        Assert.Contains("if (_barWindow is not null) return;", draw);
        Assert.Contains("host.Children.Remove(ActionBar)", draw);
        Assert.Contains("new CaptureBarWindow(ActionBar, _scale)", draw);
        Assert.Contains("Closed += (_, _) => _barWindow?.Shutdown();", draw);
        Assert.Contains("HorizontalAlignment.Stretch", draw);   // 铺满那扇窗，不留一条窗底色在右下
        // 收/展只许走一个出口：WinUI 3 的 Border 没有 IsVisibleChanged 可订阅，
        // 散落着直接写 Visibility 就会留下"条子看不见但那扇窗还在吃鼠标"
        var whole = SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        Assert.Equal(1, SourceGate.Count(whole, "ActionBar.Visibility = "));
        Assert.Contains("private void SetBarVisible(bool visible)", whole);
        Assert.Contains("_barWindow?.Reposition();", whole);
    }

    /// <summary>
    /// 那一侧窗自己的四条硬约束：不抢前台（样式 + 点亮那一刻把前台还回去）、topmost 两步、收起时藏窗、
    /// 底色钉暗；外加"上下摆位只看按钮那一行"。少任一条都有具体症状
    /// （分别：贴图快捷键被吃掉／条子被应用盖住／留下一块看不见的空窗／浅色系统主题下白图标糊成一片／
    /// 一点开选择栏整条跳到画面另一侧）。
    /// </summary>
    [Fact]
    public void TheBarWindowKeepsItsFourHardRules()
    {
        var bar = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureBarWindow.cs");
        Assert.Contains("WindowInterop.RemoveDefaultWindowFrame(this)", bar);   // 无框 + 不进任务栏/Alt+Tab
        // 不抢前台靠"亮完把前台还回去"，不靠 WS_EX_NOACTIVATE：真机上给这扇窗加了不可激活样式，
        // 条子看得见、颗颗点不动（XAML 的按钮点击收不到）——那是比黑块更坏的收尾。
        Assert.DoesNotContain("WindowInterop.WS_EX_NOACTIVATE", bar);
        Assert.Contains("AppWindow.Show();", bar);                              // 先亮出来再量
        // AppWindow.Show() 必然把新窗提到前台：不记下来还回去，贴图的 Enter／Ctrl+Z／Esc 就跟着条子一起没了
        Assert.Contains("var previous = WindowInterop.GetForegroundWindow();", bar);
        Assert.Contains("WindowInterop.SetForegroundWindow(previous)", bar);
        Assert.Contains("_content.Measure(new Size(", bar);
        Assert.Contains("WindowInterop.HWND_TOPMOST", bar);
        Assert.Contains("WindowInterop.HWND_TOP,", bar);                        // 进带之后还要带内重排
        Assert.Contains("WindowInterop.SW_SHOWNOACTIVATE", bar);
        Assert.Contains("WindowInterop.SW_HIDE", bar);                           // 收起那态必须藏窗
        Assert.Contains("RequestedTheme = ElementTheme.Dark", bar);              // 图标按白色画的，跟主题走会糊
        // WinUI 3 的 Window 没有 Background：客户区就是这层根 Grid。不钉死底色会在浅色系统主题下
        // 露出一圈框架默认白，围着暗色条子——正是这条链要消掉的观感。
        Assert.Contains("Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x20, 0x20))", bar);
        // 判"上下哪一侧"只喂按钮那一行的高度（批次 WN 的口径，判据本体在模型里，见下面那条）
        Assert.Contains("CaptureGeometry.BarOrigin(image, work, width, height, _baseHeight,", bar);
        Assert.DoesNotContain("image.Width - width) / 2", bar);          // 旧的那份"水平居中"不许回来
    }

    /// <summary>
    /// 两个阶段的工具条必须走<b>同一条摆位判据</b>（批次 WQ）。
    /// <para>真机反馈："建议不要把贴图菜单栏居中，而是改成和截图的菜单栏一样"。贴图那一条曾经自己写了
    /// 一份"水平居中于画面"，于是同一个工具在框选阶段贴着选区右缘、贴完图却跑到画面正中——用户看到的是
    /// "菜单换地方了"。居中还有第二个毛病：条子一变宽就往两边扩，右半截常被夹回屏内，看着像左右跳。
    /// 现在两态都调 <c>CaptureGeometry.BarOrigin</c>：右缘对齐、优先下方、夹回屏内。</para>
    /// <para>这里钉的是"两处都只从模型要答案、都不再自己算 x"。贴图那一侧的坐标是物理像素，
    /// 所以模型给的 DIP 常量必须换算过去（不换算＝150% 屏上缝隙与边距缩一半，两态又不同了）。</para>
    /// </summary>
    [Fact]
    public void BothStagesPlaceTheBarWithTheSameRule()
    {
        var model = SourceGate.MethodBody(ReadOverlay(false), "private void PositionBar");
        Assert.Contains("CaptureGeometry.BarOrigin(", model);
        // 选区阶段：条子量到多高就按多高判侧（它长高不会外溢，两个高度是同一个数）
        Assert.Contains("ToInt(barWidth), ToInt(barHeight), ToInt(barHeight));", model);
        Assert.DoesNotContain("Math.Clamp(x + w - barWidth", model);     // 那三行自己算的摆位已经收进模型
        Assert.DoesNotContain("y + h + 6 + barHeight", model);

        var bar = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureBarWindow.cs");
        Assert.Contains("ToPixels(CaptureGeometry.BarGap), ToPixels(CaptureGeometry.BarMargin));", bar);
        Assert.DoesNotContain("private const int Gap", bar);             // 缝隙只剩模型里那一个数
    }

    /// <summary>
    /// <summary>
    /// 选择栏<b>排在条子里面</b>，不许再走 Flyout（批次 WI 的真机结论：那扇条子窗只有按钮那一行高，
    /// WinUI 3 把弹出层钉在宿主窗边界内，"图形"点开只剩半截）。
    /// <para>而"悬停说明"那一行是批次 WP 按用户裁决<b>删掉</b>的：它几百像素宽，而条子那扇窗的宽度＝
    /// 内容实测宽度，悬停哪颗整条就变宽、左边缘跟着往左跑（批次 WQ 之后条子改右缘对齐，跳的仍是左边缘）。
    /// <b>宽度稳定比看得到解释重要</b>——这里把"不许再加回来说明行"钉住，免得下一轮有人"好心"补回来。
    /// 说明的唯一出处仍是每颗按钮的 ToolTip（选区阶段那扇全屏窗里本来就显示得下）。</para>
    /// <para>收笔那条也钉在这里：Esc／再点当前工具时那一栏要跟着收，否则它会一直占着第二行。</para>
    /// </summary>
    [Fact]
    public void ThePickerIsARowOfTheBarAndThereIsNoHintRow()
    {
        var cs = ReadOverlay(false);
        Assert.DoesNotContain("new Flyout", cs);
        Assert.DoesNotContain("_pickerFlyout", cs);
        var picker = SourceGate.MethodBody(cs, "private void ShowPicker");
        Assert.Contains("BarPicker.Content = content;", picker);
        Assert.Contains("BarPicker.Visibility = Visibility.Visible;", picker);
        Assert.Contains("BarPicker.Content = null;", SourceGate.MethodBody(cs, "private void HidePicker"));
        Assert.Contains("if (tool is null) HidePicker();", SourceGate.MethodBody(cs, "private void SetTool("));
        // 说明行整套已经删净：没有 BarHint、没有 ShowHint/HideHint、按钮上也不挂悬停事件
        Assert.DoesNotContain("BarHint", cs);
        Assert.DoesNotContain("ShowHint", cs);
        Assert.DoesNotContain("PointerEntered +=", cs);
        // 文案仍然只有一份，挂在 ToolTipService 上（不是又抄一遍到别处）
        Assert.Contains("ToolTipService.SetToolTip(button, tip);", SourceGate.MethodBody(cs, "private Button IconButton"));
        var xaml = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml");
        Assert.Contains("x:Name=\"BarPicker\"", xaml);
        Assert.DoesNotContain("BarHint", xaml);
    }

    /// <summary>
    /// 批次 WL：贴图几何的<b>每一个写点</b>都必须叫上那扇条子窗。
    /// <para>真机反馈"菜单栏已和贴图分离，但无法随着贴图位置变化而改变位置"——条子与贴图是两扇窗，
    /// 移动贴图那一扇不会自动带走另一扇。原先只有"换了缩放不同的屏"才顺带重摆一次，
    /// 于是绝大多数拖动都是"图走了、条子留在原地"。</para>
    /// <para>所以这里钉三件事：平移（<c>PinDragTo</c>）自己跟上；窗口几何那个唯一写点
    /// （<c>ApplyPinWindowRect</c>，缩放与 90° 旋转都从这里过）跟上；而跟上时<b>只摆条子</b>，
    /// 不许顺手把窗内那一层整排重算（拖动每帧都进来）。</para>
    /// </summary>
    [Fact]
    public void EveryPinGeometryWritePointMovesTheBarToo()
    {
        var cs = ReadOverlay(false);
        var follow = SourceGate.MethodBody(cs, "private void FollowBarToImage()");
        Assert.Contains("if (!_pinned || _barWindow is null) return;", follow);
        Assert.Contains("var now = WindowInterop.GetWindowRect(this);", follow);   // 只问窗口实际矩形
        Assert.Contains("_barWindow.Place(new IntRect(now.X, now.Y, now.Width, now.Height), WorkArea());", follow);
        // 拖动期禁整帧重排：跟上条子不许连带强制窗内布局
        Assert.DoesNotContain("UpdateLayout", follow);
        Assert.DoesNotContain("RelayoutContent", follow);

        Assert.Contains("FollowBarToImage();", SourceGate.MethodBody(cs, "private void PinDragTo"));
        Assert.Contains("FollowBarToImage();", SourceGate.MethodBody(cs, "private void ApplyPinWindowRect"));
        // 缩放与旋转都汇到那个唯一写点：这里钉住"它仍然是唯一写点"，否则新写点又会漏掉条子
        Assert.Contains("ApplyPinWindowRect();", SourceGate.MethodBody(cs, "private void BakeQuarterTurn"));
    }

    /// <summary>
    /// 批次 WN：贴图态条子住独立置顶窗（批次 WI），而条子在那扇窗里是 <b>Stretch</b> 的——
    /// 父窗只给它窗内那一点高度，于是它<b>永远量不出"我变高了"</b>，<c>ActionBar.SizeChanged</c> 不响，
    /// 那扇窗就一直停在旧高度上把选择栏那一行截在窗外。真机反馈：
    /// "所有菜单功能的二级菜单均会被菜单的高度限制遮挡，有时会无法显示或只能部分显示"。
    /// <para>所以<b>每一处改条子内容的地方都必须叫上 <c>ReflowBar</c></b>：目前就是选择栏开／收两处
    /// （"悬停说明"那一行已在批次 WP 按用户裁决删掉——它几百像素宽，而窗宽＝内容宽，会把条子推得忽宽忽窄）。</para>
    /// 钉的是"这两个写点各有一句"，不是"某处有几句"——加第三种行而忘了叫上窗，就是这次的复发。</para>
    /// </summary>
    [Fact]
    public void EveryBarContentWritePointReflowsTheBarWindow()
    {
        var cs = ReadOverlay(false);
        var reflow = SourceGate.MethodBody(cs, "private void ReflowBar()");
        Assert.Contains("if (_pinned) FollowBarToImage();", reflow);
        // 选区阶段条子住在全屏遮罩窗里，长多少看得见，不该被这条链牵连重排
        Assert.DoesNotContain("PositionBar", reflow);

        foreach (var writer in new[] { "private void ShowPicker(", "private void HidePicker(" })
            Assert.Contains("ReflowBar();", SourceGate.MethodBody(cs, writer));

        // 内容写点只有这两颗（批次 WP 之后说明行已经删净）：多一处就得同步出现在上面那张表里
        Assert.Equal(2, SourceGate.Count(cs, "ReflowBar();"));
        // 条子在窗里是 Stretch 的：这句话是"量不出自己变高"的前提，改成 Left/Top 就要重新论证这条链
        Assert.Contains("ActionBar.VerticalAlignment = VerticalAlignment.Stretch;",
            SourceGate.MethodBody(cs, "private void AttachBarWindow()"));
    }
}
