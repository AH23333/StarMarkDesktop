#nullable enable
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions;

/// <summary>
/// 诊断面板里"某个来源现在到底怎么样"那句话的唯一写法（纯函数）。
/// <para>
/// 旧写法是 <c>IsAvailable ? "可用" : "不可用"</c>：一个"不可用"把三种完全不同的事实压成一样——
/// 没装这个浏览器、没开那个外部程序、没配 Token。用户看到的就是"是不是坏了"，
/// 而正确反应可能是"我本来就没用 Ditto"。所以这里规定：<b>不可用时要说成因</b>，
/// 成因由各来源自己给（<see cref="IItemSource.AvailabilityHint"/>：它才知道自己找过哪条路径）。
/// </para>
/// <para><b>批次 WI-57（P-187）把交出的东西从成品字换成 <see cref="LocMessage"/>，三处一起换</b>：
/// 这一句直接上屏幕，而那一行的语言由贴上去的那一刻决定。旧写法在这里拼好"没有这项数据：…"，
/// 语言就定死在算出它的那一瞬间——换语言只剩外层重贴，屏幕上半中半英（坑表 #361 同一族）。
/// <b>成因那句也是 <see cref="LocMessage"/>，作为实参嵌进来</b>：<c>string.Format</c> 对实参调的正是
/// <c>ToString</c>，所以嵌套的那一句同样在贴上去的那一刻才取词。</para>
/// </summary>
public static class SourceAvailabilityText
{
    /// <summary>
    /// 诊断那一行里"路径清单"这类<b>机器事实</b>的长度上限（面板一行一行读，不该塞进整段清单）。
    /// <para>上限只管事实，⛔ 管句子：句子进了语言表，长度由那一列自己的措辞决定，
    /// 在这里按字符数切一句人话会把括号和句号切没（批次 WI-46b 那族"分隔符／括号形状"的教训）。</para>
    /// </summary>
    public const int MaxFactLength = 200;

    public static LocMessage Of(bool available, LocMessage? hint)
        => available
            ? LocMessage.Of("source.avail.available")
            : LocMessage.Of("source.avail.missing", hint ?? LocMessage.Of("source.avail.unknownReason"));
}
