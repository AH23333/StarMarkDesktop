#nullable enable
using System;

namespace StarMark.Abstractions.Updates;

/// <summary>
/// 哈希的<b>字面写法</b>只在这一处（批次 UF）。
/// <para>为什么值得单独立一颗：清单里的期望值与本机算出来的实际值要<b>逐字符相等</b>才算"这堆字节就是那双"。
/// 大小写、分隔符、顺序这些口径只要有两份实现，总有一天一份写 <c>A3</c> 另一份写 <c>a3</c>，
/// 表现是"包完全正确但永远校验不过"——而那种失败在日志里长得跟"被人换包了"一模一样，
/// 会把最严重的那一格（<c>PackageHashMismatch</c>）喊成误报。
/// 它放在 Abstractions 是因为期望值在 Core 写、实际值在 Integrations 算，而依赖方向不许两边互相看见。</para>
/// </summary>
public static class UpdateHashHex
{
    /// <summary>小写十六进制、无分隔符。<b>认这个格式的判断（<c>IsLowercaseHex64</c>）与之同处一仓</b>，
    /// 两边改一边就会红。</summary>
    public static string ToHex(ReadOnlySpan<byte> hash)
    {
        var text = new char[hash.Length * 2];
        for (var i = 0; i < hash.Length; i++)
        {
            text[i * 2] = HexDigit(hash[i] >> 4);
            text[i * 2 + 1] = HexDigit(hash[i] & 0x0F);
        }
        return new string(text);
    }

    private static char HexDigit(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}
