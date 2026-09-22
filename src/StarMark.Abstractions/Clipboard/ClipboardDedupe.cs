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

    /// <summary>自己写入的登记容量上限（有界，防止一路增长）。</summary>
    public const int OwnWriteCapacity = 16;

    private readonly object _gate = new();
    private readonly int _windowMs;
    private readonly List<string> _ownWrites = new();

    private string? _lastId;
    private long _lastMs;

    public ClipboardDedupe(int windowMs = DefaultWindowMs)
        => _windowMs = Math.Max(0, windowMs);

    /// <summary>
    /// 登记"我们即将把这段文本写进系统剪贴板"（点复制/重新复制前调用）。
    /// 之后的同内容通知会被当作自己的回声挡掉一次。
    /// </summary>
    public void NoteOwnWrite(string? rawText)
    {
        var id = IdOf(rawText);
        if (id is null) return;
        lock (_gate)
        {
            _ownWrites.Add(id);
            // 有界：只认最近若干次自己的写入。用户连抄 20 次同一内容后，
            // 旧登记自然淘汰，不会攒成无界增长，也不会永久屏蔽该文本。
            while (_ownWrites.Count > OwnWriteCapacity) _ownWrites.RemoveAt(0);
        }
    }

    /// <summary>
    /// 这条通知要不要挡掉。<b>有副作用</b>：放行时会把"上一条"记成本次内容，
    /// 命中回声时会消费掉一个登记（所以同一次自己写入只挡一条，不影响用户随后手动复制别的东西）。
    /// </summary>
    public bool ShouldSkip(string? rawText, long nowMs)
    {
        var id = IdOf(rawText);
        if (id is null) return true;   // 归一后为空 ⇒ 没内容可记

        lock (_gate)
        {
            // ① 自己的回声：按值消费**一个**登记（不是只挡队头——多次交替复制时队头可能已不是本条）
            var hit = _ownWrites.IndexOf(id);
            if (hit >= 0)
            {
                _ownWrites.RemoveAt(hit);
                return true;
            }

            // ② 突发重复：同内容且未出窗口
            if (_lastId == id && nowMs - _lastMs <= _windowMs) return true;

            _lastId = id;
            _lastMs = nowMs;
            return false;
        }
    }

    /// <summary>用现成的幂等键做身份：与落库键同一口径，避免"两套归一"造成的漏挡/误挡。</summary>
    private static string? IdOf(string? rawText)
    {
        var normalized = ClipboardPolicy.NormalizeText(rawText);
        return normalized.Length == 0 ? null : ClipboardPolicy.BuildSourceId(normalized);
    }
}
