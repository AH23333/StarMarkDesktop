#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Core.Capture;

/// <summary>
/// 一块面上的笔迹与编辑历史：<b>画、撤销、重做、清空这四件事的唯一状态</b>（方案 §3.4 的 InkDoc）。
/// <para>
/// 它从前顶着"截图那条链的历史"这个名字。改名不是为了好听：整合之后它是<b>两侧唯一认的载体</b>——
/// 截图与贴图今天就是用它，画布那几叠笔迹在 S2 也要归到同一个类型下（§3.4）。
/// 叫 "History" 会让人以为它只管撤销，而它同时管着"这一面上现在到底有什么"，那才是别人要读的那一半。
/// </para>
/// <para>
/// 存的是<b>整份快照</b>而不是"动作 + 反向动作"。理由有两条：① 反向动作要每种编辑各写一遍
/// （加一条的反向是删、清空的反向是恢复整叠），任何一处写漏就是"撤销后残留半条"；② 标注数量级很小
/// （一次截图不会画过几百条），快照的内存代价可以忽略，而正确性是白送的 ⇒ <see cref="Clear"/> 能撤销回来
/// 这种细节不需要额外代码。
/// </para>
/// <para>
/// 快照有上限（<see cref="MaxStates"/>）：有界才敢每画一条就存一份，
/// 也才不会在"画一百条又撤销到底"之后还留着整条链的内存。
/// </para>
/// </summary>
public sealed class InkDoc
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

    /// <summary>
    /// 这一叠里<b>最后落的那一笔</b>是全机第几笔；空叠给 <see cref="InkOrder.None"/>，
    /// 于是它在任何"谁最新"的比较里都排最后（没画过的东西不该被选中当成"最后落的那一笔"）。
    /// <para>跨叠比的就是这个数：§7 要的是"Ctrl+Z 永远撤当前焦点域内最后落的那一笔"，
    /// 而那一笔可能住在板子的某块屏上、也可能住在这张截图/贴图里。</para>
    /// </summary>
    public long LastOrder => Count == 0 ? InkOrder.None : Marks[^1].Order;

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

    /// <summary>
    /// 把所有标注整体平移（改选区位置/大小之后，标注要跟着画面走，不能留在旧坐标上跑偏）。
    /// <para><b>刻意不制造一步历史</b>：改的是"取景框"，不是任何一条标注——挪一下框就该能撤销的话，
    /// 撤销栈里根本没有存框的几何，弹回去的只有标注，那会是"标注在、框不在"的半吊子状态。
    /// 快照模型下这是安全的：每条 <see cref="Annotation"/> 不可变，换掉的只是当前那一份列表里的元素引用。</para>
    /// </summary>
    public void ShiftAllBy(int dx, int dy)
    {
        if ((dx == 0 && dy == 0) || Count == 0) return;
        _states[_at] = Marks.Select(m => m.MovedBy(dx, dy)).ToList();
    }

    /// <summary>删掉第 <paramref name="index"/> 条（选中后按 Delete）。算一步，能撤销回来。</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Marks.Count) return;
        var next = Marks.ToList();
        next.RemoveAt(index);
        Push(next);
    }

    // ── 橡皮擦的合并撤销：一次拖拭（可能擦掉好几条）只算一步 ──

    private IReadOnlyList<Annotation>? _eraseBase;
    private bool _erasing;

    /// <summary>开始一次擦除拖拭：记下拖拭前的快照。</summary>
    public void BeginErase()
    {
        _erasing = true;
        _eraseBase = Marks.ToList();
    }

    /// <summary>擦除进行中：结果＝起点快照减去 <paramref name="removed"/>（引用身份），直接替换当前状态、不入栈。</summary>
    public void ApplyErase(HashSet<Annotation> removed)
    {
        if (!_erasing || _eraseBase is null) return;
        _states[_at] = removed.Count == 0
            ? _eraseBase
            : _eraseBase.Where(m => !removed.Contains(m)).ToList();
    }

    /// <summary>结束擦除：什么都没擦掉则恢复；擦掉了就在当前状态前插回旧快照（一次撤销回到擦除前）。</summary>
    public void EndErase()
    {
        if (!_erasing) return;
        var result = Marks;
        _erasing = false;
        if (_eraseBase is null || result.Count == _eraseBase.Count)
        {
            _states[_at] = _eraseBase ?? Array.Empty<Annotation>();
            _eraseBase = null;
            return;
        }
        _states.Insert(_at, _eraseBase);
        _at++;
        _eraseBase = null;
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
        _erasing = false;
        _eraseBase = null;
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
