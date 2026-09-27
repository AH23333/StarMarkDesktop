#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 剪贴板采集的<b>去重闸门</b>：挡掉"同一件事被系统重复通知"与"StarMark 自己写入的复制动作"。
/// <para>
/// 两类噪声都真实存在，且后果都是历史里出现假条目：
/// ① 很多应用（Office、部分浏览器）一次复制会连发多次 <c>WM_CLIPBOARDUPDATE</c>，
///    不去抖就会被复制次数被记成 3 次；
/// ② 用户在 StarMark 的剪贴板页里点"复制"，等于我们自己的写入又被自己采集一遍——
///    表现是"我只是翻了翻历史，列表自己重排了"。这一类必须靠"写入前登记"来认，
///    而不是靠猜内容。
/// </para>
/// <para>刻意做成与 Win32 无关的纯状态机（时间戳由调用方传进来），这样两条挡回路径都能单测。</para>
/// </summary>
public sealed class ClipboardDedupe
{
    /// <summary>突发去抖窗口：同一内容在该窗口内的重复通知视为一次复制。</summary>
    public const int DefaultWindowMs = 700;

    /// <summary>自己写入的登记有效期。超过它就不再认——见 <see cref="NoteOwnWrite"/>。</summary>
    public const int OwnWriteTtlMs = 5_000;

    /// <summary>自己写入的登记容量上限（有界，防止一路增长）。</summary>
    public const int OwnWriteCapacity = 16;

    private readonly object _gate = new();
    private readonly int _windowMs;
    private readonly int _ownWriteTtlMs;
    private readonly List<(string Id, long AtMs)> _ownWrites = new();

    private string? _lastId;
    private long _lastMs;

    public ClipboardDedupe(int windowMs = DefaultWindowMs, int ownWriteTtlMs = OwnWriteTtlMs)
    {
        _windowMs = Math.Max(0, windowMs);
        _ownWriteTtlMs = Math.Max(0, ownWriteTtlMs);
    }

    /// <summary>
    /// 登记"我们即将把这段文本写进系统剪贴板"（点复制/重新复制前调用）。
    /// 之后的同内容通知会被当作自己的回声挡掉一次。
    /// <para>
    /// <b>登记必须会过期</b>：调用顺序是"先登记再写剪贴板"（反了就可能已被采集读到），
    /// 于是"登记成功但写入失败"（应用被挂起、剪贴板被别家占住、抛异常）与"登记时正好在暂停"
    /// 都会留下一个没人消费的令牌。若令牌只按容量淘汰，用户几小时后<b>真的</b>从别处复制同一段
    /// 文字时会被当成回声挡掉一次＝这条复制静默丢失。5 s 足够覆盖"写入→系统发通知"的真实间隔。
    /// </para>
    /// </summary>
    public void NoteOwnWrite(string? rawText, long nowMs) => NoteId(IdOfText(rawText), nowMs);

    /// <summary>
    /// 图片版的登记：<b>身份是归一后的像素（BGRA）哈希，不是容器字节</b>。
    /// <para>这是"字节身份"在这里唯一成立的取法：写回时我们交出去的是 PNG 字节，而系统再发通知时
    /// 给采集侧的是它自己重排出来的 DIB（长度、位深、行填充都可能不同）。拿容器字节算哈希，
    /// 自家那一次写入永远对不上登记 ⇒ 用户每点一次"复制图片"，历史就多一条自回声。</para>
    /// </summary>
    public void NoteOwnWrite(byte[]? bgra, long nowMs) => NoteId(IdOfPixels(bgra), nowMs);

    /// <summary>
    /// 这条通知要不要挡掉。<b>有副作用</b>：放行时会把"上一条"记成本次内容，
    /// 命中回声时会消费掉一个登记（所以同一次自己写入只挡一条，不影响用户随后手动复制别的东西）。
    /// </summary>
    public bool ShouldSkip(string? rawText, long nowMs) => ShouldSkipId(IdOfText(rawText), nowMs);

    /// <summary>图片版：<see cref="ShouldSkip(string,long)"/> 的同一条闸门，只是身份换成像素哈希。</summary>
    public bool ShouldSkip(byte[]? bgra, long nowMs) => ShouldSkipId(IdOfPixels(bgra), nowMs);

    private void NoteId(string? id, long nowMs)
    {
        if (id is null) return;
        lock (_gate)
        {
            _ownWrites.Add((id, nowMs));
            // 双上限：时间过期由 ShouldSkipId 负责，这里只保证"哪怕时钟疯跳也不会无界增长"。
            while (_ownWrites.Count > OwnWriteCapacity) _ownWrites.RemoveAt(0);
        }
    }

    private bool ShouldSkipId(string? id, long nowMs)
    {
        if (id is null) return true;   // 归一后为空 ⇒ 没内容可记

        lock (_gate)
        {
            // 先剥过期登记。用 <b>绝对值</b> 而不是 <c>nowMs - AtMs</c>：用户调时钟（或笔记本唤醒后
            // 系统对时）会让带符号差值变负，那样"未来登记"永不过期——与自动备份那条 ShouldRun
            // 判定同一形状的错（时钟漂移不能让护栏变成永久停摆）。
            _ownWrites.RemoveAll(t => Math.Abs(nowMs - t.AtMs) > _ownWriteTtlMs);

            // ① 自己的回声：按值消费**一个**登记（不是只挡队头——多次交替复制时队头可能已不是本条）
            var hit = _ownWrites.FindIndex(t => t.Id == id);
            if (hit >= 0)
            {
                _ownWrites.RemoveAt(hit);
                return true;
            }

            // ② 突发重复：同内容且未出窗口（同样取绝对值，时钟倒退时不该把真复制误判成"刚复制过"）
            if (_lastId == id && Math.Abs(nowMs - _lastMs) <= _windowMs) return true;

            _lastId = id;
            _lastMs = nowMs;
            return false;
        }
    }

    /// <summary>用现成的幂等键做身份：与落库键同一口径，避免"两套归一"造成的漏挡/误挡。</summary>
    private static string? IdOfText(string? rawText)
    {
        var normalized = ClipboardPolicy.NormalizeText(rawText);
        return normalized.Length == 0 ? null : ClipboardPolicy.BuildSourceId(normalized);
    }

    /// <summary>图片身份与图片条目键同一口径（<see cref="ClipboardPolicy.BuildImageSourceId"/>）：一套哈希，两处共用。</summary>
    private static string? IdOfPixels(byte[]? bgra)
        => bgra is { Length: > 0 } ? ClipboardPolicy.BuildImageSourceId(bgra) : null;
}
