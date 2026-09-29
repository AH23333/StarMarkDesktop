#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Integrations.Canvas;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// CanvasService 的这一段——把板子交出去的那三条：贴图、复制到剪贴板、存为 PNG（共用同一份叠好的帧）。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

    /// <summary>
    /// 把板子上<b>笔迹那一块</b>合成一张钉到桌面上，钉在它原来所在的位置（§16.5 的"快照为贴图"，S4-⑤ 定形）。
    /// <para><b>刻意不贴整块屏</b>：贴图在 Z 序名册里压在画布玻璃之上（穿透态 Pin=3、Board=4），
    /// 而且默认吃鼠标——整屏一张的意思就是"按一次贴图，整块桌面被一张静止的图盖住，之后每一按都落在它身上"。
    /// 裁到笔迹那一块之后，它和截图链贴出来的那一张才是同一形态：看得见、能拖、能缩放、不占地方。</para>
    /// <para>板子上那一份墨<b>原样留着</b>（2026-09-29 用户裁决）：这一条是"复制出去一份"，不是"收走"，
    /// 讲完这一段还要在同一块板上接着写。</para>
    /// </summary>
    public static void SnapshotToPin()
    {
        var shot = Compose();
        if (!shot.Ok)
        {
            Report("贴图失败", shot.Reason);
            return;
        }
        if (shot.Ink.IsEmpty)
        {
            // 一块墨都没有就没有"哪一块"可贴。这里不能退回"那就贴整屏"——那正是本批修掉的病；
            // 而"按了没反应"也不算交代，所以把另一条出口点名给用户，并说他**当前实际绑着的那颗键**
            // （写死键名就是把人支使去按一颗可能没用的键）。
            Report("贴图失败", "板上还没有笔迹可贴——「贴图」贴的就是笔迹那一块；" +
                $"要截整块屏幕请用截图（{BindingText(HotkeyActions.ScreenCapture)}），框选后再贴出");
            return;
        }
        // 边距是 DIP，必须按这块屏自己的缩放换算：写死像素数，150% 屏上的边就缩水一半（CanvasBackdropMath 同一条纪律）
        var padding = (int)Math.Round(CanvasSnapshotMath.PaddingDip * shot.Scale);
        var region = CanvasSnapshotMath.FrameOf(shot.Ink, shot.Width, shot.Height, padding);
        // 落点＝这块屏的原点 + 裁块在这幅画面里的偏移 ⇒ 贴出来那一块正好盖在眼睛看到的那一块上
        ScreenshotService.PinPixels(
            CanvasSnapshotMath.CropBgra(shot.Pixels, shot.Width, shot.Height, region),
            region.Width, region.Height,
            new IntRect(shot.Screen.X + region.X, shot.Screen.Y + region.Y, region.Width, region.Height));
    }

    /// <summary>把"屏幕 + 笔迹"合成一张交给剪贴板。<b>整幅交出去</b>（与存图同口径：留档的是一整页）。</summary>
    public static void SnapshotToClipboard()
    {
        var shot = Compose();
        if (!shot.Ok)
        {
            Report("复制失败", shot.Reason);
            return;
        }
        _ = ScreenshotService.CopyPixelsAsync(shot.Pixels, shot.Width, shot.Height, "画布");
    }

    /// <summary>把"屏幕 + 笔迹"合成一张存成 PNG。<b>整幅交出去</b>（与复制到剪贴板同口径）。</summary>
    public static void SavePng()
    {
        var shot = Compose();
        if (!shot.Ok)
        {
            Report("存图失败", shot.Reason);
            return;
        }
        _ = ScreenshotService.SavePixelsAsync(shot.Pixels, shot.Width, shot.Height, "画布");
    }

    // ────────── 快照 ──────────

    /// <summary>
    /// 一次合成的全部产出：整幅画面、这块屏在虚拟桌面里的位置与缩放，以及<b>笔迹占的那一块</b>。
    /// <para>三条落点（贴图／复制／存图）读的是同一份像素，差别只在贴图要照 <see cref="Shot.Ink"/> 裁一刀。
    /// 做成一个返回值而不是三份 <c>out</c>：从前七个 <c>out</c> 的形状里，"哪一条落点用了哪个数"只能靠读调用点，
    /// 而这一格的全部风险都在"落点之间悄悄分岔"（记忆 ⑧）。</para>
    /// </summary>
    private readonly record struct Shot(bool Ok, byte[] Pixels, int Width, int Height,
        IntRect Screen, IntRect Ink, double Scale, string Reason);

    /// <summary>合成就失败了：画面还没开始叠，reason 是给用户看得见的那一句。</summary>
    private static Shot NoShot(string reason)
        => new(false, Array.Empty<byte>(), 0, 0, new IntRect(), CanvasCompositor.Nothing, 1d, reason);

    /// <summary>
    /// 取"鼠标所在那块屏"的画面 + 笔迹。<b>只合成一块屏</b>：跨屏一张大图会把另一块屏的内容也截进来，
    /// 而钉上去之后它既不属于这块屏也不属于那块——用户要的是"我圈的那块黑板"。
    /// </summary>
    private static Shot Compose()
    {
        if (Screens.Count == 0) return NoShot("画布没开着");
        WindowInterop.GetCursorPos(out var cursor);
        var screen = Screens.FirstOrDefault(s => s.Bounds.X <= cursor.X && cursor.X < s.Bounds.Right
            && s.Bounds.Y <= cursor.Y && cursor.Y < s.Bounds.Bottom) ?? Screens[0];

        // 抓的是<b>干净桌面</b>（那一帧里这块玻璃不上屏），墨由下面自己叠：抓屏抓到的是已经合成完的屏幕，
        // 玻璃上的笔迹会在那一帧里进图一次，OverlayOntoFrame 又叠一次＝"图里有两份"（方案 §1 的 C5，
        // 也是"屏幕上一份、图里另一份"这类对不上的总根源）。收/还只在 CaptureWithoutCanvas 那一处写。
        //
        // <b>白板态走另一条底</b>：那块玻璃本身就是不透明的整屏白，用户看见的就是"一张白纸 + 墨"，
        // 抓来的桌面他根本没看见。仍按桌面当底就会出现"板上是白纸、贴出来是别人的桌面"——
        // 与批次 WK 那条同一类缺口：凡是"屏幕上有"的东西（预览槽、背景态）都必须进这张图。
        byte[] baseFrame;
        int boardWidth, boardHeight;
        if (CanvasBackdropMath.IsOpaque(AnnotationHub.Backdrop))
        {
            boardWidth = screen.Window.Width;
            boardHeight = screen.Window.Height;
            baseFrame = BoardFrame(boardWidth, boardHeight, CanvasBackdropMath.WhiteboardArgb);
        }
        else
        {
            var captured = ScreenshotService.CaptureWithoutCanvas();
            if (!captured.Ok || captured.Frame is not { } frame)
                return NoShot(captured.Error ?? "系统没有返回画面");
            if (ScreenshotService.TryCrop(frame, screen.Bounds) is not { } crop)
                return NoShot("这一块屏在截到的画面外面（显示器可能刚被拔掉）");
            boardWidth = crop.Width;
            boardHeight = crop.Height;
            baseFrame = crop.Pixels;
        }

        // 笔迹（持久层 + 还活着的荧光段）合成到这张底之上；光晕是"提示我在什么模式"，不进快照
        var ink = new uint[boardWidth * boardHeight];
        Array.Copy(screen.Persistent, ink, Math.Min(screen.Persistent.Length, ink.Length));
        foreach (var segment in screen.Trail.Segments)
            CanvasCompositor.Paint(ink, boardWidth, boardHeight, segment.Stroke, segment.AlphaScale);
        // 正在拖的那个图形／还开着的折线也算"屏幕上有"：不叠它就会出现"板上看得见一条，贴出来的图没有"。
        // 顺序与 Flush 一致（叠在荧光段之后），这样同一帧里不会跳色。
        if (screen.Trail.Preview is { } preview)
            CanvasCompositor.Paint(ink, boardWidth, boardHeight, preview);
        // <b>先量墨在哪，再叠幕布那层底</b>：底是半透明的，叠完之后每个像素的 alpha 都高过空白位，
        // 这一句就会返回"整幅"，"只裁笔迹那一块"当场失效（症状：幕布开着时贴出来的又是整屏、桌面照样被盖住），
        // 而 InkRegionOf 的单测照旧全绿——它测的是判据本身，不是顺序。所以顺序由接线闸门钉住。
        var inkRegion = CanvasSnapshotMath.InkRegionOf(ink, boardWidth, boardHeight);
        // 幕布那块半透明的底也是"屏幕上有"的东西：把它叠进这张墨缓冲（亮区里不叠），交出去的才是眼睛看到的那一层。
        // 圆的几何与那条混合式只有窗口那一份（<c>CompositeForSnapshot</c>）——在这儿重算一次圆就是两份出处。
        // 白板底不走这里：它是不透明的，直接当整张图的底（上面那条臂），两者读的都是 <c>AnnotationHub.Backdrop</c>。
        if (CanvasBackdropMath.HasFocusHole(AnnotationHub.Backdrop)) screen.Window.CompositeForSnapshot(ink);
        CanvasCompositor.OverlayOntoFrame(baseFrame, boardWidth, boardHeight,
            new IntRect(0, 0, 0, 0), ink, screen.Window.Width, screen.Window.Height);

        return new Shot(true, baseFrame, boardWidth, boardHeight, screen.Bounds, inkRegion, screen.Scale, string.Empty);
    }

    /// <summary>
    /// 一块纯底色的 BGRA 帧（白板快照的那张底）。<b>字节序与分层窗缓冲一致</b>（B、G、R、A），
    /// 所以这里的位移顺序不能照着"ARGB"那个名字念反——念反了白底看不出来，彩色底就是一张错色图。
    /// </summary>
    private static byte[] BoardFrame(int width, int height, uint argb)
    {
        var frame = new byte[width * height * 4];
        var b = (byte)(argb & 0xFF);
        var g = (byte)(argb >> 8 & 0xFF);
        var r = (byte)(argb >> 16 & 0xFF);
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = b;
            frame[i + 1] = g;
            frame[i + 2] = r;
            frame[i + 3] = 255;         // 交出去的图不能留透明洞（与 OverlayOntoFrame 那一句同一条纪律）
        }
        return frame;
    }
}
