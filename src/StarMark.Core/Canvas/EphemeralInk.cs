#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Canvas;

/// <summary>
/// 荧光笔那一层的生命周期：<b>按住拖动＝画一段，松手后按 TTL 自动淡掉</b>。
/// <para>
/// 为什么不进 <see cref="Capture.InkDoc"/>：讲解时指着屏幕说一句"看这里"的那一道光，
/// 说完就该消失——留在板上会越画越糊，而"每画一条都要记得去擦"就不是随手比划了。
/// 规格 §16.3 的分层正是按<b>生命周期</b>分的，不是按长相分的。
/// </para>
/// <para>
/// 时间由调用方注入（<c>nowMs</c> 用 <see cref="Environment.TickCount64"/>，单调且不受改系统时钟影响）。
/// <b>不用 <c>DateTimeOffset.Now</c></b>：那会让"改时钟/时区"把淡出算成瞬间或永不到期。
/// </para>
/// </summary>
public sealed class EphemeralInk
{
    private readonly List<Segment> _segments = new();

    /// <summary>一段荧光笔迹 + 它最后一次被添点的那一刻。</summary>
    public sealed class Segment
    {
        public required CanvasStroke Stroke { get; init; }

        /// <summary>最后一个采样点到达的时刻（<c>TickCount64</c> 毫秒）。淡出从这一刻起算。</summary>
        public long LastPointTick { get; set; }

        /// <summary>本帧该用多淡（1＝完全不淡，0＝该消失了）。渲染层把它当作 alpha 的乘数。</summary>
        public double AlphaScale { get; set; } = 1d;

        /// <summary>
        /// 屏幕上此刻贴着的那一层有多淡。<b>与 <see cref="AlphaScale"/> 不等就意味着这一整段要重算</b>——
        /// 而相等时（正按住拖的那一段每帧都被刷新，恒为 1）要重算的只有新走过的那一小条。
        /// 这条判据就是"荧光笔拖动卡"与"不卡"的分界：以前每帧无条件把整段从几何重画一遍。
        /// </summary>
        public double PaintScale { get; set; } = 1d;
    }

    /// <summary>淡出时长。默认 1.5 秒——够指着讲一句，又不至于让痕迹攒成一团。</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>光标光晕（荧光笔态下跟着鼠标的那圈颜色）。关掉它就等于把"我在荧光笔模式"这件事
    /// 只留给工具条上的高亮——穿透态收不到鼠标事件时，光晕是唯一还能告诉用户"模式还开着"的东西。</summary>
    public bool CursorHaloEnabled { get; set; } = true;

    /// <summary>光晕颜色（默认红：深浅背景都看得见，且与荧光笔本身区分）。
    /// 位序走 <see cref="Capture.Annotation.Opaque"/>——全仓库只有那一处「字节→BGRA」的拼法。</summary>
    public int HaloColorBgra { get; set; } = Capture.Annotation.Opaque(0x30, 0x30, 0xFF);

    public IReadOnlyList<Segment> Segments => _segments;

    /// <summary>
    /// 正在拖的那个<b>图形预览</b>（没有则为 null）。它借同一层的原因是同一句话：
    /// <b>不属于"留下来的东西"，就不许写进持久层</b>——预览一旦落进 <see cref="Capture.InkDoc"/>，
    /// 拖到一半取消、拖过头再拉回来，都会留下一条撤不掉的笔迹。
    /// <para>它与荧光段的区别只有一条：<b>不按 TTL 淡出</b>（<see cref="Tick"/> 不看它），
    /// 每帧被整份替换（<see cref="SetPreview"/>）。</para>
    /// </summary>
    public CanvasStroke? Preview => _preview?.Stroke;

    private Segment? _preview;

    /// <summary>
    /// 换掉预览（拖拽期间每一帧一次）。
    /// </summary>
    /// <returns>
    /// <b>旧的那份 + 新的这份合起来占过的地方</b>。两份都要并进脏区：旧的从这一帧起再没人画它，
    /// 不交回去就是"拖一个矩形，屏幕上留一条更宽的矩形影子"；新的不交回去就是"预览不跟手"。
    /// </returns>
    public IntRect SetPreview(CanvasStroke stroke)
    {
        var was = _preview?.Stroke.Bounds ?? default;
        _preview = new Segment { Stroke = stroke, LastPointTick = 0 };
        return Union(was, stroke.Bounds);
    }

    /// <summary>丢掉预览（定形、取消、清屏都走这里）。返回它占过的地方，调用方负责交回脏区。</summary>
    public IntRect DropPreview()
    {
        if (_preview is null) return default;
        var was = _preview.Stroke.Bounds;
        _preview = null;
        return was;
    }

    public bool IsEmpty => _segments.Count == 0 && _preview is null;

    /// <summary>
    /// 现在还活着的这些段（含预览）一共占过多大一片。<b>清空之前必须先拿它去弄脏屏幕</b>——
    /// 段一旦被 list 丢掉就再没人画它，那块光却会永远留在分层窗的缓冲里。
    /// </summary>
    public IntRect LiveBounds
    {
        get
        {
            IntRect all = default;
            foreach (var segment in _segments) all = Union(all, segment.Stroke.Bounds);
            if (_preview is { } preview) all = Union(all, preview.Stroke.Bounds);
            return all;
        }
    }

    /// <summary>丢掉所有段与预览（调用方负责把 <see cref="LiveBounds"/> 交回脏区）。</summary>
    public void Clear()
    {
        _segments.Clear();
        _preview = null;
    }

    /// <summary>开始一段荧光笔迹（按下那一下）。</summary>
    public Segment Begin(PixelPoint first, int colorBgra, int width, long nowMs)
    {
        var segment = new Segment
        {
            Stroke = new CanvasStroke(CanvasTool.Highlighter, colorBgra, width, first),
            LastPointTick = nowMs,
        };
        _segments.Add(segment);
        return segment;
    }

    /// <summary>
    /// 给<b>最后一段</b>添点。
    /// <para>
    /// <b>返回值说的是"这一段真的变长了吗"</b>（太近被 <c>CanvasStroke.MinPointDistance</c> 丢掉的不算）：
    /// 调用方拿它决定要不要把新走的那一小条并进脏区。若这里"只要收到移动就算成功"，
    /// 鼠标在原地抖一下也会触发一次整块重算——那正是这批要消掉的那类开销。
    /// </para>
    /// <para>
    /// <b>没有段可添时静默返回 false 而不是抛</b>：荧光笔在穿透态收不到事件，
    /// 松手与移动的到达顺序在不同 DPI 缩放下不保证，追到这里抛异常只会变成"画布一动就崩"。
    /// </para>
    /// </summary>
    public bool Extend(PixelPoint p, long nowMs)
    {
        if (_segments.Count == 0) return false;
        var last = _segments[^1];
        last.LastPointTick = nowMs;                          // 即使这一点太近被丢，也重新起算 TTL（还在比划＝没停）
        return last.Stroke.AddPoint(p);
    }

    /// <summary>
    /// 每帧调用：按 TTL 淘汰到期段并刷新存活段的浓度。
    /// <para>从后往前删——<see cref="List{T}"/> 从前删会让索引整体错位，
    /// 而"漏掉一段没淡"会表现成屏幕上赖着不走的一道光。</para>
    /// </summary>
    /// <returns>
    /// <b>被删掉的那些段占过的地方</b>（没删则为空）。渲染层必须把这块交回脏区：
    /// 段一消失就没人再画它，而它贴过的那一层像素还留在屏幕上＝一道擦不掉的光。
    /// </returns>
    public IntRect Tick(long nowMs)
    {
        var ttlMs = Math.Max(1, Ttl.TotalMilliseconds);
        IntRect expired = default;
        for (var i = _segments.Count - 1; i >= 0; i--)
        {
            var segment = _segments[i];
            var age = nowMs - segment.LastPointTick;
            // age 为负＝TickCount64 回绕（几十天不关机才会遇到一次）：当作刚添过点，不因此把笔迹吞掉
            segment.AlphaScale = age <= 0 ? 1d : Math.Clamp(1d - age / ttlMs, 0d, 1d);
            if (segment.AlphaScale > 0d) continue;
            expired = Union(expired, segment.Stroke.Bounds);
            _segments.RemoveAt(i);
        }
        return expired;
    }

    private static IntRect Union(IntRect a, IntRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new IntRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }
}
