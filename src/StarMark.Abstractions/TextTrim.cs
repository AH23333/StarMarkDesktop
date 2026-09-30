#nullable enable
using System;

namespace StarMark.Abstractions;

/// <summary>
/// "按展示宽度截断"的<b>唯一切法</b>（批次 SC，P-130）。
/// <para>为什么收在这里：同一件事全仓曾有 11 把（切片式 10 ＋ 累加式 1）。SB 收预览窗标题那两把时量出来的。
/// 但共享的只有<b>怎么切</b>——各处的宽度<b>各是各的语义</b>（弹窗标题 40、卡片标题 160、闹钟菜单标签 24、
/// RSS 摘要 400、Ditto 预览 140…），把宽度并成一个数字才是假抽象，所以 <paramref name="max"/> 由宿主传进来。</para>
/// <para><b>不许把一枚 emoji 切成半个</b>：一枚 emoji 在 UTF-16 里占两枚单元，那一刀落在代理对中间就会留下
/// 未配对代理，界面上渲染成方块（实测：切出的串里未配对代理＝1）。⇒ 下刀前退一格。
/// 除这一种形状之外，两种切法与"直接切片"逐字相同（中文／ASCII／全角符号／BMP 内组合字符都实测过）。</para>
/// </summary>
public static class TextTrim
{
    /// <summary>取前 <paramref name="max"/> 枚 UTF-16 单元；那一刀若落在代理对中间就退一格。不补省略号。</summary>
    public static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = max;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut];
    }

    /// <summary>截到 <paramref name="max"/> 枚单元并补一枚省略号；没超长则原样返回（不许多长出省略号）。</summary>
    public static string Ellipsize(string text, int max)
        => text.Length <= max ? text : Cut(text, max) + "…";
}
