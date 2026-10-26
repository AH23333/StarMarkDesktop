#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarMark.Abstractions.I18n;

/// <summary>
/// 一句"<b>还没取词</b>"的界面话：键＋可选实参（批次 WI-2k）。
/// <para>为什么要有这一颗而不是让 VM 直接返回字符串：VM 拼出句子的那一刻就把语言定死了，
/// 换语言那一刻<b>没人会回去重算</b>——那正是坑表 #276 那一族（"挂键≠那句字只有一处出处"的反面：
/// 句子压根没进表）。交回键与实参，界面侧用 <c>LocText.SetKey</c> 钉在元素上，
/// 换语言由那一条唯一的订阅当场按新语言重算。</para>
/// <para><see cref="Render"/> 只留给<b>不上屏幕</b>的消费者（日志、通知文本）：那条路一旦走到界面上，
/// 就是"界面显示的是上一次算出来的那句"。</para>
/// </summary>
public sealed record LocMessage(string Key, object?[]? Args = null)
{
    public static LocMessage Of(string key) => new(key);

    public static LocMessage Of(string key, params object?[] args) => new(key, args);

    /// <summary>
    /// 把<b>一串话按一颗两槽骨架折成一颗</b>（批次 片116／P-196 把这一族收成这里）。
    /// <para>为什么要有这一颗：表里那颗两槽骨架（<c>{0}；{1}</c> 那一族）只说"两件怎么连"，
    /// 而"三件、四件怎么连"从前<b>每族自己写一遍</b>——<c>Aggregate((head, next) =&gt; Of(key, head, next))</c>
    /// 与 <c>acc is null ? part : Of(key, acc, part)</c> 是同一件事的两种写法（P-196 数的正是这个）。
    /// 折叠这件事的<b>语序</b>（左折：((a＋b)＋c)）与<b>缺的那几颗怎么办</b>（跳过）都属于机制而不属于任何一族句子，
    /// 所以它们只许住在这里；哪一颗骨架、兜底交回什么，才由各族自己说。</para>
    /// <para>交回的是<b>还没取词</b>的那颗 <see cref="LocMessage"/>：折的过程只搭形状，
    /// 取词仍然发生在 <see cref="ToString"/> 被调的那一刻（＝贴上去那一刻），换语言整条一起改口。</para>
    /// <para><b>空的那一型</b>：一颗都没有 ⇒ 交回 <see langword="null"/>（"没有话要说"是各宿主自己的事，
    /// 由它自己兜底成 headline／空串／不显示）。主句一定在场的那一型看下面那颗同名重载。</para>
    /// </summary>
    public static LocMessage? Fold(string skeletonKey, IEnumerable<LocMessage?> parts)
    {
        LocMessage? acc = null;
        foreach (var part in parts)
            if (part is not null)
                acc = acc is null ? part : Of(skeletonKey, acc, part);
        return acc;
    }

    /// <summary>
    /// <b>主句一定在场</b>的那一型：把后面那几颗（每颗都可能缺）依次接到 <paramref name="head"/> 之后。
    /// <para>与上面那颗 <c>Fold(skeletonKey, parts)</c> 分两处的理由不是两种机制，是<b>兜底不归机制管</b>：
    /// 这一型的调用点本来就有一句一定在（表头那句、主句那句），让它们各自再写一遍 <c>head is null ? … </c>
    /// 或 <c>?? head</c> ＝把同一条规则抄第三、第四份，正是 P-196 那一族。这里签名保证交回非 null，
    /// 所以那一型在这一头<b>没有</b>取不到的分支。</para>
    /// </summary>
    public static LocMessage Fold(string skeletonKey, LocMessage head, params LocMessage?[] rest)
    {
        var acc = head;
        foreach (var part in rest)
            if (part is not null)
                acc = Of(skeletonKey, acc, part);
        return acc;
    }

    /// <summary>按<b>当前</b>语言取一次词（不是缓存：每次调用都重取）。</summary>
    public string Render() => Args is { Length: > 0 } ? Locale.Fmt(Key, Args) : Locale.T(Key);

    /// <summary>
    /// <b>为什么这一颗必须是 <c>ToString</c> 而不是让调用点先 <see cref="Render"/> 再交出字符串</b>：
    /// 状态行那句是<b>套娃</b>的（"来源…·N 分钟前已缓存"＝外层句子里嵌着读数句子）。
    /// 实参交出去的那一刻若已拼成字符串，语言就被<b>定死在算出它的那一瞬间</b>，换语言只会重贴外层，
    /// 屏幕上剩下半中半英——正是这一族要消灭的东西。<c>string.Format</c> 对实参调的正是 <c>ToString</c>，
    /// 于是嵌套的那一句<b>在贴上去的那一刻</b>（含换语言重贴的那一次）才按当前语言取词。
    /// </summary>
    public override string ToString() => Render();
}

/// <summary>
/// 「一串话＋用哪颗键把它们连起来」：连接符也住在表里，所以这一颗要等<b>被贴上去的那一刻</b>才取词（批次 WI-40-3b）。
/// <para>为什么⛔ 在算实参时就 <c>string.Join(Locale.T(连接键), …)</c> 再把字符串交出去：那样连接符被钉在
/// <b>算出它的那一瞬间</b>，换语言重贴只重算了外层句子，屏幕上剩下「A、B, C」这种半中半英。
/// 句子进了表而<b>取词时机</b>没跟着进——坑表 #362 那一族的第三个表面（前两个＝<see cref="LocMessage.ToString"/>
/// 与 <c>PlaceholderArgStore</c> 那颗信箱）。</para>
/// <para>实参按 <c>object?</c> 存：数字（pid）与 <see cref="LocMessage"/>（动作名）都能进来，
/// <c>string.Join</c> 对每个元素调 <c>ToString</c>，于是嵌套的那一句同样在连起来这一刻才按当下语言取词。</para>
/// </summary>
public sealed record LocJoin(IReadOnlyList<object?> Items, string JoinKey)
{
    public override string ToString() => string.Join(Locale.T(JoinKey), Items);
}

/// <summary>
/// 当前界面语言＋取词的唯一入口（批次 WI-1）。
/// <para>进程里<b>只有这一颗可变状态</b>：谁要按语言取词都从这里走，于是"换语言"这一件事
/// 只有一个要重画的时刻，而不是每个控件自己记得去读一遍。</para>
/// <para>它住在 Abstractions 是为了让 Core 的措辞（更新的结局那句话）将来能进同一张表，
/// 而不必反过来让 Abstractions 看见 WinUI。UI 侧怎么"当场重画"见 <c>StarMark.UI/Helpers/LocText.cs</c>。</para>
/// </summary>
public static class Locale
{
    private static readonly object Gate = new();

    /// <summary>没登记的键只报一次：这一类问题的表现是"某个按钮点了什么也没变"，
    /// 刷屏不会更容易发现，只会把日志里真正那条挤掉（#236 那条节流口径）。</summary>
    private static readonly HashSet<string> ReportedUnknown = new(StringComparer.Ordinal);

    private static AppLanguage _current = AppLanguages.Default;

    /// <summary>界面当前用哪一版文案。启动时由 <c>App</c> 按设置档与系统语言各算一次。</summary>
    public static AppLanguage Current
    {
        get { lock (Gate) return _current; }
    }

    /// <summary>语言换成的那一刻触发一次。<b>订阅方只许做"把已注册的元素重贴一遍"这一件事</b>：
    /// 重建页面、重新查库都不属于这里。</summary>
    public static event Action? Changed;

    /// <summary>
    /// 该用哪一版：<b>用户表过态优先</b>，没表过态才看系统，两样都读不到才落默认档。
    /// <para>顺序反过来就是"这台机器的语言设置比用户的选择大"——那是用户改不动的一次覆盖。</para>
    /// </summary>
    public static AppLanguage Resolve(string? savedCode, IEnumerable<string?> windowsLanguageTags)
        => AppLanguages.FromCode(savedCode)
           ?? AppLanguages.PickFrom(windowsLanguageTags)
           ?? AppLanguages.Default;

    /// <summary>换语言。<b>相同就不触发</b>：打开设置页看完又原样点回去，不该让整窗重贴一遍。</summary>
    public static bool TrySet(AppLanguage next)
    {
        lock (Gate)
        {
            if (_current == next) return false;
            _current = next;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>取一句界面文案。<b>键没登记</b>不抛也不装没看见：屏幕上出现 <c>⟨nav.tree⟩</c> 这种形状，
    /// 用户会截图来问，日志里也留着那句——比"那一颗按钮显示成上一个语言的词"更容易查。</summary>
    public static string T(string key)
    {
        if (!UiStrings.TryGet(key, out var entry))
        {
            WarnUnknownOnce(key);
            return "⟨" + key + "⟩";
        }
        return UiStrings.TextOf(entry, Current);
    }

    /// <summary>带读数的句子。占位符是位置式，各语言那一行必须给同一组编号——这条由闸门逐行比对钉住。
    /// <b>刻意不 catch 格式异常</b>：表是编译期数据，配错号的行应该在同一条闸门上红，不该到运行期兜。</summary>
    public static string Fmt(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, T(key), args);

    private static void WarnUnknownOnce(string key)
    {
        lock (Gate)
        {
            if (!ReportedUnknown.Add(key)) return;
        }
        StarLog.Warn($"界面文案没登记这颗键：{key}（界面上会显示成 ⟨{key}⟩，不是漏翻，是没写进表）");
    }
}
