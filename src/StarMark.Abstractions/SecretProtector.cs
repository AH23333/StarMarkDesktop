#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace StarMark.Abstractions;

/// <summary>
/// 用户数据档里那两颗密钥（github.json 的 PAT、settings.json 的 AI Key）的落盘封装（P-30/P-89，方案 A）。
/// <para>
/// 为什么选 DPAPI（<c>CurrentUser</c>）：密钥不出机器、不进代码、不进配置——加解密由 Windows
/// 按当前登录账号代管，进程里拿不到主密钥；同一账号下的进程都能解（同步、更新器不用额外接线），
/// 跨账号／跨机器解不开——把 github.json 拷去另一台机器，Token 那一格读出来是「没配」，
/// 而不是「拿一串解不开的密文当凭据发出去」。
/// </para>
/// <para>
/// 为什么不填 entropy：能威胁到这一层的攻击者（同账号下的进程）本就能直接读档、也能从程序里
/// 抠出那串固定 entropy——填它防不住要防的人，只会多一颗「换一次就要作废全部存档」的伪密钥。
/// </para>
/// <para>
/// 口径：只在<b>落盘前那一刻</b>加密、<b>读出后立刻</b>解密——内存里它仍是明文字符串。
/// 这是本方案的边界，不是缺陷：本批要堵的是「明文躺在磁盘上、被同步盘／备份／误发带走」
/// 这条真实出口；内存防护是另一个量级的工程，不在这一刀里。
/// </para>
/// </summary>
public static class SecretProtector
{
    /// <summary>密文串的标记前缀（版本化的「这是密文」哨兵）：把新版密文档与旧版明文档分开，
    /// 明文档由此可以无损回落（读出来照常能用）。<b>换封装算法时换前缀而不是改算法</b>——
    /// 旧档自动走回落＋升级那半条路，而不是被新算法硬解成乱码。真实密钥不会以这串开头
    /// （GitHub PAT 与 OpenAI Key 都有各自的固定前缀）；万一有谁手填了这种形状，它会被当成
    /// 解不开的密文读成「没配」——重填一次就好，不会拿它当凭据外发。</summary>
    public const string CipherPrefix = "dpapi1:";

    /// <summary>加密。null／空串原样返回：「没配」不包壳——加密空值只会让档里多一串谁也读不懂的占位，
    /// 还把「没配」与「配了但解不开」这两种语义搅在一起。DPAPI 本身失败会抛出去：
    /// 落盘路径上的失败必须出声，吞掉它等于「看起来存上了、其实没有」。</summary>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return CipherPrefix + Convert.ToBase64String(blob);
    }

    /// <summary>存的那一格是不是密文形状。null 与明文都是 false。</summary>
    public static bool IsCipher(string? stored)
        => stored is not null && stored.StartsWith(CipherPrefix, StringComparison.Ordinal);

    /// <summary>
    /// 解开存的那一格：密文→明文；<b>明文原样返回</b>（旧版明文档的回落——升级前的那一刻它必须还能用）；
    /// null／空原样返回；<b>解不开的密文→null</b>（换机器／换账号／档损坏：「当没配」，
    /// 用户重填一次就好，⛔ 把密文原样交出去当凭据用）。
    /// </summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!IsCipher(stored)) return stored;
        try
        {
            var blob = Convert.FromBase64String(stored[CipherPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }
}
