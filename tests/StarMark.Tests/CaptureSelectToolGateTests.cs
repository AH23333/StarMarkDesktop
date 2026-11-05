#nullable enable
using System;
using StarMark.Abstractions.I18n;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// P-288 建议一的<b>显式选择工具</b>闸门（扫源码，不跑界面）。
/// <para>
/// 结构上有三件事最容易被下一轮改坏，红测要能先看见：
/// ① "选择"是<b>界面的开关</b>，不是 <see cref="AnnotationTool"/> 的一员——那颗枚举归模型，
/// 每一项都要产标注，"选择"不产；塞进去等于给模型添一支画不出东西的笔，枚举表那张
/// "条上有哪些按钮"的清单也会跟着多出一颗假工具。
/// ② 批量选的组操作<b>只许一步历史</b>：挪一组、删一组、微调一组各算一步——逐条落定会把
/// "撤销"拆成半截组留在屏上（橡皮"擦一次只撤一次"是同一条规矩，先例在 <see cref="InkDoc.EndErase"/>）。
/// ③ 组的说明是<b>带条数的第二颗键</b>：与单选那句互斥显隐，谁也不许把"问 selectionTip 那颗键"
/// 抄到代码里再贴一遍（一颗键一个主人，PageWord 那格数着次数）。
/// </para>
/// </summary>
public sealed class CaptureSelectToolGateTests
{
    private static string OverlayCs()
        => SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");

    private static string OverlayXaml()
        => SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml");

    // ────────── ① 开关归界面，不进模型 ──────────

    /// <summary>
    /// 「选择」不许混进 <see cref="AnnotationTool"/>。枚举那张表同时是"条上有哪些笔"与
    /// "这一笔画什么"两件事的唯一真源：多出一颗不产标注的成员，按表生成的那一侧就会出现
    /// 一颗点了没反应的笔（或一轮画不出来的分派）。
    /// </summary>
    [Fact]
    public void SelectIsAUiSwitchNotAModelPen()
    {
        // 只看枚举那一块：整档里 "Select" 作为普通词出现在注释/别的成员名里是允许的
        var enumBody = SourceGate.Between(SourceGate.ReadRepoFile("src/StarMark.Core/Capture/Annotation.cs"),
            "enum AnnotationTool", "}");
        Assert.DoesNotContain("Select", enumBody);
        var cs = OverlayCs();
        Assert.Contains("private bool _selectTool;", cs);                  // 界面自己的开关
        Assert.DoesNotContain("AnnotationTool.Select", cs);               // 分派里也不许长出这颗笔
        // 与笔互斥：拿笔收它、亮它收笔，两个入口都要在场（SetTool / SetSelectTool）
        Assert.Contains("_selectTool = false;", SourceGate.MethodBody(cs, "private void SetTool("));
        Assert.Contains("private void SetSelectTool(bool armed)", cs);
        Assert.Contains("DropSelection();", SourceGate.MethodBody(cs, "private void SetSelectTool("));
    }

    /// <summary>条上第一颗就是它，且选中态走同一颗 <c>BarChecked</c>（"我在哪一态"只有一个信号源）。</summary>
    [Fact]
    public void TheBarLeadsWithTheSelectToggle()
    {
        var bar = SourceGate.MethodBody(OverlayCs(), "private void BuildToolBar");
        Assert.True(bar.IndexOf("_selectButton = IconButton(", StringComparison.Ordinal)
                    < bar.IndexOf("_shapeButton = IconButton(", StringComparison.Ordinal),
            "「选择」必须排在「图形」前面：它是改框/点选的默认态入口，不是图形笔的附庸");
        Assert.Contains("LocMessage.Of(\"capture.bar.select\")", bar);
        Assert.Contains("_selectButton.Background = _selectTool ? BarChecked : BarNormal;",
            SourceGate.MethodBody(OverlayCs(), "private void SyncTools"));
    }

    // ────────── ② 按下只做选择；拖框三档结算 ──────────

    /// <summary>
    /// 选择工具亮着的那一按<b>排在改框之后、挪框/落笔之前</b>：抓已画的（含组）与拖框圈组
    /// 都不许被"挪选区"或"起一笔"抢走；框可以越出选区（批次 PU 起标注本来就能画在外面）。
    /// </summary>
    [Fact]
    public void TheSelectPressGrabsOrRopesBeforeAnythingElse()
    {
        var press = SourceGate.MethodBody(OverlayCs(), "private void Root_PointerPressed");
        Assert.Contains("if (_mode == CaptureMode.Toolbar && _selectTool)", press);
        Assert.Contains("TryBeginGrab(ToLocal(physical), e.Pointer)) return;", press);
        Assert.Contains("BeginBandDrag(physical, e.Pointer);", press);
        var grab = SourceGate.MethodBody(OverlayCs(), "private bool TryBeginGrab");
        // 选择工具亮着＝每次按下都做整场命中（点哪条选哪条），"点住即拖"的隐性那条不动
        Assert.Contains("hadSelection && !_selectTool", grab);
        Assert.Contains("HitTest(_history.Marks, local, SlopInSource(SelectionSlop))", grab);
        // 组员那一抓＝挪整组：组拖动的底与按下缓冲都在这一刻备好，与单条同一口径
        Assert.Contains("UnderDragBufferMany(_selectedMany)", grab);
    }

    /// <summary>
    /// 选择工具亮着时，选中那条的<b>旋转/缩放把手先于整场命中</b>被接住：旋转把手悬在框外
    ///（<c>RotateHandle</c> 往上抬的那一截），整场 HitTest 只测标注本体，永远够不到它——
    /// 不先接住，按把手那一按就漏成拖框（真机反馈"有把手但没法选中旋转"）。
    /// 只认 Rotate/Scale 两档（把手是选中那条的专属区，框内 Move 仍归整场命中）；
    /// 组选没有把手，这一测只在单选时做。
    /// </summary>
    [Fact]
    public void TheHeldMarksHandlesWinThePressBeforeTheFullHitTest()
    {
        var grab = SourceGate.MethodBody(OverlayCs(), "private bool TryBeginGrab");
        Assert.Contains("_selectedMany is not { Count: > 1 }", grab);
        Assert.Contains("Grab.Rotate or Grab.Scale", grab);
        // 接住之后与隐性那条同路：把手命中也要真抓（GrabAt/捕获指针），不许只改选中
        Assert.Contains("_grab = mark.GrabAt(local, RotateHandle(mark), SlopInSource(MoveSlop));", grab);
    }

    /// <summary>
    /// 拖框松手按 <see cref="CaptureGeometry.Overlaps"/> 结算成三档：0 条丢选中、1 条塌成单选、
    /// ≥2 条成组（主选＝最上面那条）。命中判据在模型里，界面不自己写"碰到没有"。
    /// </summary>
    [Fact]
    public void TheBandSettlesIntoThreeTiersByTheModelsOverlapRule()
    {
        var cs = OverlayCs();
        var end = SourceGate.MethodBody(cs, "private void EndBandDrag");
        Assert.Contains("CaptureGeometry.Overlaps(band, _history.Marks[i].Bounds())", end);
        Assert.Contains("if (hits.Count == 0)", end);
        Assert.Contains("if (hits.Count == 1)", end);
        Assert.Contains("_selected = hits[^1];", end);
        Assert.Contains("CaptureGeometry.Normalize(_bandStart.X, _bandStart.Y, _bandNow.X, _bandNow.Y)",
            SourceGate.MethodBody(cs, "private IntRect BandRect"));
    }

    // ────────── ③ 组操作只许一步历史 ──────────

    /// <summary>
    /// 挪一组＝<see cref="InkDoc.ReplaceMany"/> 一步落定；每帧预览与单条拖动同一口径
    /// （一次整块复制＋逐条重画，60fps 节流），松手整帧烤准。
    /// </summary>
    [Fact]
    public void MovingAGroupIsOneHistoryStepAndItsPreviewCopiesOncePerFrame()
    {
        var cs = OverlayCs();
        var dragMany = SourceGate.MethodBody(cs, "private void DragManyTo");
        Assert.Contains("Buffer.BlockCopy(under, 0, canvas", dragMany);
        Assert.DoesNotContain("Render(", dragMany);            // 每帧重烤全组＝打码那次"跟不上手"的成因
        Assert.DoesNotContain("Rebake(", dragMany);
        Assert.DoesNotContain("UnderDragBuffer", dragMany);    // 底图准备只在按下那一下（TryBeginGrab）
        Assert.Contains("_history.ReplaceMany(pairs);", SourceGate.MethodBody(cs, "private void EndDragMany"));
        // 组拖动的分发不经过单条 DragTo 的缓冲：DragTo 那套钉照旧成立（BlockCopy 字面量仍在单条那里）
        Assert.Contains("Buffer.BlockCopy(under, 0, canvas", SourceGate.MethodBody(cs, "private void DragTo"));
        // 微调一组也一步：方向键那条链与拖动同一条规矩
        Assert.Contains("_history.ReplaceMany(pairs);", SourceGate.MethodBody(cs, "private void NudgeSelectedMark"));
    }

    /// <summary>
    /// 删一组＝橡皮的口径（BeginErase/ApplyErase/EndErase 一步）；单条删除仍走
    /// <see cref="InkDoc.RemoveAt"/>，那条"只有撤销没有删除"的老路不许被组分支吃掉。
    /// </summary>
    [Fact]
    public void DeletingAGroupIsOneStepAndSingleDeleteKeepsItsOwnPath()
    {
        var del = SourceGate.MethodBody(OverlayCs(), "private void DeleteSelected");
        Assert.Contains("_selectedMany is { Count: > 1 }", del);
        Assert.Contains("_history.BeginErase();", del);
        Assert.Contains("_history.ApplyErase(removed);", del);
        Assert.Contains("_history.EndErase();", del);
        Assert.Contains("_history.RemoveAt(index);", del);
    }

    // ────────── ④ 组选中丢得干净、说明各归各的键 ──────────

    /// <summary>历史一变（撤销/清空/收工具）整组一起丢：组下标随历史移动，留着就指着别人。</summary>
    [Fact]
    public void DroppingTheSelectionTakesTheWholeGroupAndTheBand()
    {
        var drop = SourceGate.MethodBody(OverlayCs(), "private void DropSelection");
        Assert.Contains("_selectedMany = null;", drop);
        Assert.Contains("_bandDragging = false;", drop);
        Assert.Contains("_dragManyOriginal = null;", drop);
    }

    /// <summary>
    /// 带条数的那句是<b>第二颗键</b>（capture.selectionTipMany），两颗 TextBlock 互斥显隐；
    /// 单选那句的静态键仍在标记上（WI-67 片9c 的口径），代码里⛔ 再问同一颗键——
    /// 一颗键一个主人，PageWord 那格按整串带引号数着次数。
    /// </summary>
    [Fact]
    public void TheGroupTipIsItsOwnKeyAndTheSingleTipStaysTheOnlyOwnerOfItsKey()
    {
        var xaml = OverlayXaml();
        var tip = SourceGate.Between(xaml, "x:Name=\"SelectionTip\"", "</Border>");
        Assert.Contains("x:Name=\"SelectionText\"", tip);
        Assert.Contains("x:Name=\"SelectionTextMany\"", tip);
        Assert.Contains("i18n:LocText.Key=\"capture.selectionTip\"", tip);
        var cs = OverlayCs();
        // 代码里只问带条数那颗；单选那颗的正文仍由标记上的静态键负责
        Assert.DoesNotContain("\"capture.selectionTip\"", cs);
        Assert.Contains("LocMessage.Of(\"capture.selectionTipMany\", marks.Count)",
            SourceGate.MethodBody(cs, "private void DrawManySelectionBoxes"));
        Assert.Contains("LocText.SetMessage(SelectionTextMany, TextBlock.TextProperty",
            SourceGate.MethodBody(cs, "private void DrawManySelectionBoxes"));
        // 单选回来时那颗多选的收起、静态键那颗现身：两颗互斥，别让两句叠在一行
        var handles = SourceGate.MethodBody(cs, "private void DrawSelectionHandles");
        Assert.Contains("SelectionTextMany.Visibility = Visibility.Collapsed;", handles);
        Assert.Contains("SelectionText.Visibility = Visibility.Visible;", handles);
        Assert.True(UiStrings.TryGet("capture.selectionTipMany", out var row));
        Assert.NotEqual(row.Zh, row.En);
        Assert.NotEqual(row.Zh, row.Ja);
    }

    // ────────── ⑤ 收口与光标 ──────────

    /// <summary>Esc 一次误按只收一层：先丢选中，再收选择工具，最后才轮到关张/取消整场。</summary>
    [Fact]
    public void EscPeelsOneLayerAtATime()
    {
        var key = SourceGate.MethodBody(OverlayCs(), "private void Root_KeyDown");
        Assert.Contains("else if (_selectTool) SetSelectTool(false);", key);
    }

    /// <summary>选择工具的光标是普通箭头：这一按是"指"，不是画（准线）也不是挪框（十字箭头）——
    /// 光标形状是"我在哪一态"的唯一可见信号。</summary>
    [Fact]
    public void TheSelectToolShowsAnArrowWhereNothingIsPicked()
    {
        var cs = OverlayCs();
        Assert.Contains("_selectTool ? InputSystemCursorShape.Arrow", cs);
    }

    // ────────── 建议二：工具条观感（P-288 的第二条） ──────────

    /// <summary>
    /// 静止态那层白洗底撤掉、悬停交回按钮模板自带的 PointerOver；分隔线收成定高竖杠、
    /// 按钮圆角与条内留白微调。<b>不新增 PointerEntered 处理器</b>——那条禁令由
    /// CaptureOverlayGateTests 的说明行禁令代理钉守着，这里只钉改过的那几颗数。
    /// </summary>
    [Fact]
    public void TheBarRestStateIsQuietAndItsSeparatorsAreSized()
    {
        var cs = OverlayCs();
        Assert.Contains("Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)", SourceGate.Code(cs));
        Assert.DoesNotContain("Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)", SourceGate.Code(cs));
        Assert.Contains("CornerRadius = new CornerRadius(4)", SourceGate.MethodBody(cs, "private Button IconButton"));
        var sep = SourceGate.MethodBody(cs, "private static Border Separator");
        Assert.Contains("Height = 15,", sep);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Center,", sep);
        var xaml = OverlayXaml();
        Assert.Contains("Padding=\"5,3,5,3\"", xaml);
        Assert.Contains("Spacing=\"3\"", xaml);
    }
}
