#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Core.Capture;

/// <summary>
/// 标注的编辑历史：画、撤销、重做、清空这四件事的<b>唯一状态</b>。
/// <para>
/// 存的是<b>整份快照</b>而不是"动作 + 反向动作"。理由有两条：① 反向动作要每种编辑各写一遍
/// （加一条的反向是删、清空的反向是恢复整叠），任何一处写漏就是"撤销后残留半条"；
/// ② 标注数量级很小（一次截图不会画过几百条），快照的内存代价可以忽略，
/// 而正确性是白送的 ⇒ <see cref="Clear"/> 能撤销回来这种细节不需要额外代码。
/// </para>
/// <para>
/// 快照有上限（<see cref="MaxStates"/>）：有界才敢每画一条就存一份，
/// 也才不会在"画一百条又撤销到底"之后还留着整条链的内存。
/// </para>
/// </summary>
public sealed class AnnotationHistory
{
    /// <summary>最多记住多少步（超出就丢最早的那一步：撤销到底也够用了）。</summary>
    public const int MaxStates = 40;

    private readonly List<IReadOnlyList<Annotation>> _states = new() { Array.Empty<Annotation>() };
    private int _at;

    /// <summary>当前该画出来的那一份标注（顺序＝绘制顺序，后画的盖在先画的上面）。</summary>
    public IReadOnlyList<Annotation> Marks => _states[_at];

    public int Count => Marks.Count;
    public bool CanUndo => _at > 0;
    public bool CanRedo => _at < _states.Count - 1;

    /// <summary>画了一条新标注。<b>重做栈就此作废</b>：撤销两步再画一条，历史在这里分叉，
    /// 留着旧分支会让"前进"把用户刚画的东西换成一条他没选中的旧线。</summary>
    public void Add(Annotation mark)
    {
        var next = Marks.ToList();
        next.Add(mark);
        Push(next);
    }

    /// <summary>清空。算一步，能撤销回来（误点一下不该丢一整页标注）。已经是空的则不入栈——
    /// 否则连点三次清空要撤销三次才回到有标注的状态。</summary>
    public void Clear()
    {
        if (Marks.Count == 0) return;
        Push(Array.Empty<Annotation>());
    }

    /// <summary>
    /// 把第 <paramref name="index"/> 条换成 <paramref name="replacement"/>——移动 / 缩放 / 旋转的落定都走这里。
    /// <para>仍然只是"再存一份快照"：这就是选快照模型的红利，加一类编辑不需要再写一条反向动作
    /// （反向写漏一处，就是"撤销后残留半条"那种只有真机能看见的缺陷）。</para>
    /// <para>下标越界（撤销之后旧下标失效是常态）静默忽略：界面拿的是一份可能已经过期的选中下标，
    /// 这里抛异常等于把一次普通的按键变成崩溃。</para>
    /// </summary>
    public void ReplaceAt(int index, Annotation replacement)
    {
        if (index < 0 || index >= Marks.Count) return;
        var next = Marks.ToList();
        next[index] = replacement;
        Push(next);
    }

    /// <summary>删掉第 <paramref name="index"/> 条（选中后按 Delete）。算一步，能撤销回来。</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Marks.Count) return;
        var next = Marks.ToList();
        next.RemoveAt(index);
        Push(next);
    }

    /// <summary>退一步。已在最早的状态时返回 false（界面据此灰掉按钮，而不是点了没反应）。</summary>
    public bool Undo()
    {
        if (!CanUndo) return false;
        _at--;
        return true;
    }

    /// <summary>进一步。</summary>
    public bool Redo()
    {
        if (!CanRedo) return false;
        _at++;
        return true;
    }

    /// <summary>整条历史丢掉（换选区时调用：底图都换了，旧标注摆在新框里没有任何意义）。</summary>
    public void Reset()
    {
        _states.Clear();
        _states.Add(Array.Empty<Annotation>());
        _at = 0;
    }

    private void Push(IReadOnlyList<Annotation> state)
    {
        if (_at < _states.Count - 1) _states.RemoveRange(_at + 1, _states.Count - _at - 1);   // 丢掉作废的重做分支
        _states.Add(state);
        if (_states.Count > MaxStates) _states.RemoveAt(0);                                   // 有界：丢最早那一步
        _at = _states.Count - 1;
    }
}
