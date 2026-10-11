#nullable enable
using System;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 断言一句<b>还没取词</b>的话（批次 WI-62，P-193 那半句交身份之后，测试⛔ 再比渲染出来的中文）。
/// <para>为什么换尺子：从前这些断言写的是 <c>Assert.Contains("跳过 1 个", rr.Message)</c>——
/// 它把<b>表里那一行的措辞</b>抄进了测试，于是"把措辞改通顺"必然红（规则 #130：只改措辞必须绿），
/// 而它本来想钉的两件事反而没钉住：<b>哪一档在说话</b>与<b>数字进没进对那颗槽</b>。
/// 这里改钉这两件：外层键、槽位上的那颗键、槽里的读数。</para>
/// <para>⛔ 在这里 <c>Render()</c> 之后比字符串：那等于把测试退回旧尺子，也等于默认"屏幕上的字由语言定"这件事⛔ 需要证人。</para>
/// </summary>
internal static class LocAssert
{
    /// <summary>这句话出自表里哪一行（<c>null</c>＝那一档根本没说话，本来就该⛔ 多嘴）。</summary>
    public static string KeyOf(LocMessage? msg) => (msg ?? throw new InvalidOperationException("那句话是 null（这一档没话要说）")).Key;

    /// <summary>外层句子的第 <paramref name="slot"/> 颗槽上挂着的那句话（必须真的是一句话，⛔ 是裸字符串）。</summary>
    public static LocMessage Arm(LocMessage? msg, int slot)
        => Assert.IsType<LocMessage>(Arg(msg, slot));

    /// <summary>外层句子的第 <paramref name="slot"/> 颗槽上的实参（机器事实：条数／张数／异常原文）。</summary>
    public static object? Arg(LocMessage? msg, int slot)
    {
        var m = msg ?? throw new InvalidOperationException("那句话是 null");
        Assert.NotNull(m.Args);
        Assert.True(slot < m.Args!.Length, $"{m.Key} 只有 {m.Args.Length} 颗槽，等不到第 {slot} 颗");
        return m.Args[slot];
    }

    /// <summary>那一档<b>没说话</b>（槽上交的是 <c>null</c>）：这一族里"没有图片就别多嘴那半句"的写法。</summary>
    public static void Silent(LocMessage? msg, int slot) => Assert.Null(Arg(msg, slot));

    /// <summary>槽数（含可选槽）——用于钉"外层句子确实给那几档各留了一颗位置"。</summary>
    public static int Slots(LocMessage? msg) => (msg ?? throw new InvalidOperationException()).Args?.Length ?? 0;
}
