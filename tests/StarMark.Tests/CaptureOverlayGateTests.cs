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
    private static string ReadOverlay(bool xaml)
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "StarMark.UI")))
            dir = Path.GetDirectoryName(dir);
        var root = dir ?? throw new InvalidOperationException("未找到仓库根目录（src/StarMark.UI）");
        var name = xaml ? "CaptureOverlayWindow.xaml" : "CaptureOverlayWindow.xaml.cs";
        return File.ReadAllText(Path.Combine(root, "src", "StarMark.UI", "Views", name));
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
        Assert.DoesNotContain("Content=\"", bar);      // 条上不写字：文字只出现在 ToolTip（悬停读得到，也不占地方）
        Assert.DoesNotContain("<TextBlock", bar);
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

    [Fact]
    public void ToolStripIsASingleCompactRow()
    {
        // 用户反馈的原话是"太大"。这条闸门钉的是"别再长回两行、也别把图标换回带文字的下拉"：
        // 工具条 Border 里只允许一个横向 StackPanel，出现纵向 StackPanel 就是又加了一行。
        var bar = ActionBar(ReadOverlay(xaml: true));
        Assert.Equal(1, Count(bar, "<StackPanel"));
        Assert.Contains("Orientation=\"Horizontal\"", bar);
        Assert.DoesNotContain("Orientation=\"Vertical\"", bar);
        Assert.True(Count(bar, "<Border") == 1, "工具条里再套一个 Border 就是又开了一层");
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
        // 焦点没落进输入框时必须当场说出来：静默失效是最难自查的一类
        Assert.Contains("if (!TextEditor.Focus(FocusState.Programmatic))", cs);
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
        Assert.Contains("PlaceVertex(ToLocal(physical))", cs);                        // 每一按钉一个顶点，不走拖动那套
        Assert.Contains("FinishPolyLine(commit: false)", SourceGate.MethodBody(cs, "private void ResetAnnotations"));
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
        // 只给 MinWidth 而不给上限，输入框会一路长到 Margin 之外；MaxWidth 是"别铺满屏"的下限保障
        Assert.Contains("MaxWidth=", host);
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
        Assert.True(pressed.IndexOf("TryBeginGrab", StringComparison.Ordinal)
            < pressed.IndexOf("BeginStroke(ToLocal", StringComparison.Ordinal),
            "抓住旧的一条必须在起新的一笔之前问，否则永远拖不动已画的东西");
        var grab = SourceGate.MethodBody(cs, "private bool TryBeginGrab");
        Assert.Contains("if (_editingText || _polyLine is not null) return false;", grab);
        // 命中顺序＝把手 → 四角 → 框内：反过来先判框内，四角会被"移动"整锅吃掉（缩放的把手就点不到）
        Assert.True(grab.IndexOf("Grab.Rotate", StringComparison.Ordinal)
            < grab.IndexOf("Grab.Scale", StringComparison.Ordinal));
        Assert.True(grab.IndexOf("Grab.Scale", StringComparison.Ordinal)
            < grab.IndexOf("Grab.Move", StringComparison.Ordinal));
        // 把手只有一个算法出口：画它的那一处与判"按中了没有"的那处必须是同一个坐标
        var handle = SourceGate.MethodBody(cs, "private PixelPoint RotateHandle");
        Assert.Contains("box.Y - lift < selection.Y", handle);   // 贴到选区上沿时夹回框内，否则这颗点永远点不到
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
        Assert.Contains("UnderDragBuffer(index)", SourceGate.MethodBody(cs, "private bool TryBeginGrab"));
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
            "private void ResetAnnotations", "private void BeginStroke", "private void PlaceVertex",
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
        Assert.Contains("case VirtualKey.Delete when _selected is not null:", keys);
        Assert.Contains("case VirtualKey.Back when _selected is not null:", keys);
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
        Assert.Contains("mark.TransformedPoints()", SourceGate.MethodBody(painter, "public static void Paint"));
        // 绘制端一旦自己读 Rotation / Scale 去算，就变成"模型算一遍、画的时候再算一遍"，
        // 预览与交付会在两次换算的差里分开。（椭圆那类图元自己的三角函数与此无关。）
        Assert.DoesNotContain(".Rotation", painter);
        Assert.DoesNotContain(".Scale", painter);

        var overlay = ReadOverlay(xaml: false);
        var handles = SourceGate.MethodBody(overlay, "private void DrawSelectionHandles");
        Assert.Contains("mark.Bounds()", handles);
        Assert.Contains("mark.Corners()", handles);          // 把手摆在哪儿由模型说，界面只负责画
        Assert.Contains("mark.SupportsRotation", handles);   // 转不转得动也是模型的判断
        // 界面读原始角度/自己算三角函数＝第二处换算，预览与交付会在两次换算的差里分开
        Assert.DoesNotContain("mark.Rotation", handles);
        Assert.DoesNotContain("Math.Sin", handles);
        Assert.DoesNotContain("Math.Cos", handles);
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
}
