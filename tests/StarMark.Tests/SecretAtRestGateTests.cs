#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using StarMark.Integrations.GitHub;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 密钥落盘（P-30/P-89，片211）的证人：磁盘上的 github.json / settings.json 不许再出现明文密钥。
/// <para>两侧同刀、同一颗 <see cref="SecretProtector"/>。github.json 的 PAT（P-30）在
/// Integrations 层，测试工程引得到，走<b>行为测</b>；settings.json 的 AI Key（P-89）住在
/// UI 层的 <c>SettingsStore</c>，测试工程不引用 <c>StarMark.UI</c>（SourceGate 头注释那条口径），
/// 那侧按 <c>WiringGate</c> 族先例钉<b>源码形状</b>：封在 SaveAiSettings 体内、解在
/// LoadAiSettings 体内、SettingsStore 整个 partial 家族没有第三个出口。</para>
/// <para>机检判据（账本 P-30 的验收句）：写盘 → 磁盘字节里<b>没有</b>明文 → 读回还原原值；
/// 旧明文档 → 照常能读（回落）→ 磁盘当场变密文（透明升级），且升级重写不丢同档其它字段。</para>
/// </summary>
public sealed class SecretAtRestGateTests : IDisposable
{
    private readonly string _dir;
    private readonly string _ghPath;

    public SecretAtRestGateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"starmark_secrets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _ghPath = Path.Combine(_dir, "github.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ────────── 封装本体（两侧共用那一颗）──────────

    /// <summary>往返、明文回落、解不开当没配、「没配」不包壳——四件事一把测。
    /// DPAPI 的 CurrentUser 作用域在测试进程里同样成立（同一账号），不需要接线。</summary>
    [Fact]
    public void Protector_RoundTrips_AndFallsBackOnPlaintext()
    {
        const string secret = "ghp_unit_roundtrip_42";
        var cipher = SecretProtector.Protect(secret);
        Assert.NotEqual(secret, cipher);
        Assert.True(SecretProtector.IsCipher(cipher));
        Assert.StartsWith(SecretProtector.CipherPrefix, cipher, StringComparison.Ordinal);
        Assert.Equal(secret, SecretProtector.Unprotect(cipher));

        // 「没配」不包壳：null/空原样进出，也不会被认成密文
        Assert.Null(SecretProtector.Protect(null));
        Assert.Equal(string.Empty, SecretProtector.Protect(string.Empty));
        Assert.False(SecretProtector.IsCipher(null));
        Assert.False(SecretProtector.IsCipher(string.Empty));
        Assert.Null(SecretProtector.Unprotect(null));
        Assert.Equal(string.Empty, SecretProtector.Unprotect(string.Empty));

        // 明文回落：不带标记的串原样可用（旧档升级前的那一刻必须还能读）
        Assert.Equal("plain_old_key", SecretProtector.Unprotect("plain_old_key"));

        // 解不开的密文 → 当没配：⛔ 把密文原样交出去当凭据外发
        Assert.Null(SecretProtector.Unprotect(SecretProtector.CipherPrefix + "not_base64_!!"));
    }

    // ────────── P-30：github.json 的 PAT（行为测）──────────

    /// <summary>机检判据本体：写盘 → 磁盘无明文、有密文标记 → 读回还原；
    /// 再存一轮也不封两遍（Load 交回来的是明文，Save 重新封一次就好）。</summary>
    [Fact]
    public void GithubToken_OnDisk_IsCipher_AndLoadsBackAsPlaintext()
    {
        const string secret = "ghp_plain_on_disk_77";
        new GitHubOptions { Token = secret, Username = "u1", SyncIntervalSeconds = 120, PageSize = 40 }.Save(_ghPath);

        var raw = File.ReadAllText(_ghPath);
        Assert.Contains(SecretProtector.CipherPrefix, raw, StringComparison.Ordinal);   // 正锚：加密真的发生了
        Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);                   // 磁盘上没有明文
        Assert.False(File.Exists(_ghPath + ".tmp"));                                    // 原子写不残留

        var loaded = GitHubOptions.Load(_ghPath);
        Assert.Equal(secret, loaded.Token);
        Assert.Equal("u1", loaded.Username);
        Assert.Equal(120, loaded.SyncIntervalSeconds);
        Assert.Equal(40, loaded.PageSize);

        loaded.Save(_ghPath);
        Assert.Equal(secret, GitHubOptions.Load(_ghPath).Token);
        Assert.DoesNotContain(secret, File.ReadAllText(_ghPath), StringComparison.Ordinal);
    }

    /// <summary>旧版明文档（这批之前落的就是明文）：读出来照常能用（回落），
    /// 且那一次读取里磁盘当场变密文（透明升级）——升级重写不丢同档其它字段。</summary>
    [Fact]
    public void GithubToken_LegacyPlaintextFile_FallsBack_AndUpgradesInPlace()
    {
        // 测试自己序列化（不走 Save）——落出来的就是明文档，这是升级前的真实现场
        File.WriteAllText(_ghPath, JsonSerializer.Serialize(new GitHubOptions
        {
            Token = "ghp_legacy_plaintext_9",
            Username = "old-user",
            SyncIntervalSeconds = 60,
            PageSize = 30,
        }));

        var first = GitHubOptions.Load(_ghPath);
        Assert.Equal("ghp_legacy_plaintext_9", first.Token);   // 明文回落：升级前的那一刻照常能用
        Assert.Equal(60, first.SyncIntervalSeconds);

        var raw = File.ReadAllText(_ghPath);
        Assert.DoesNotContain("ghp_legacy_plaintext_9", raw, StringComparison.Ordinal);  // 明文当场消失
        Assert.Contains(SecretProtector.CipherPrefix, raw, StringComparison.Ordinal);

        var second = GitHubOptions.Load(_ghPath);
        Assert.Equal("ghp_legacy_plaintext_9", second.Token);  // 升级后再读仍是原值
        Assert.Equal("old-user", second.Username);             // 升级重写不丢同档其它字段
        Assert.Equal(60, second.SyncIntervalSeconds);
        Assert.Equal(30, second.PageSize);
    }

    /// <summary>已是密文档的档，读它不许再触发重写：没有升级要做就一个字节不动
    /// （DPAPI 每次加密输出都不同，多余的重写会从字节上看出来）。</summary>
    [Fact]
    public void GithubToken_CipherFile_LoadDoesNotRewriteIt()
    {
        new GitHubOptions { Token = "ghp_stable_1" }.Save(_ghPath);
        var before = File.ReadAllText(_ghPath);
        GitHubOptions.Load(_ghPath);
        Assert.Equal(before, File.ReadAllText(_ghPath));
    }

    /// <summary>「没配」不包壳的另一头：Token 为空的档落盘不带密文标记，读回仍是空。</summary>
    [Fact]
    public void GithubToken_EmptyStaysUnwrapped()
    {
        new GitHubOptions { Token = null, Username = "nobody" }.Save(_ghPath);
        Assert.DoesNotContain(SecretProtector.CipherPrefix, File.ReadAllText(_ghPath), StringComparison.Ordinal);
        Assert.Null(GitHubOptions.Load(_ghPath).Token);
    }

    // ────────── 源码形状（两颗落盘档的封装出口）──────────

    private const string OptionsFile = "src/StarMark.Integrations/GitHub/GitHubOptions.cs";
    private const string StoreMain = "src/StarMark.UI/Helpers/SettingsStore.cs";

    /// <summary>P-30 的封装只住在 Save/Load 这一对里：Save 恰一次 Protect、Load 恰一次
    /// Unprotect／IsCipher，其余地方零引用——第三处出口就是「有的地方封、有的地方不封」，
    /// 那种分岔比不封还难查（同一个档两种读数）。</summary>
    [Fact]
    public void GithubOptions_SecretExitsLiveOnlyInsideSaveAndLoad()
    {
        var code = Code(ReadRepoFile(OptionsFile));
        var saveBody = MethodBody(code, "public void Save(");
        var loadBody = MethodBody(code, "public static GitHubOptions Load(");

        Assert.Equal(1, Count(saveBody, "SecretProtector.Protect("));
        Assert.Equal(0, Count(saveBody, "SecretProtector.Unprotect("));
        Assert.Equal(1, Count(loadBody, "SecretProtector.Unprotect("));
        Assert.Equal(1, Count(loadBody, "SecretProtector.IsCipher("));
        Assert.Equal(0, Count(loadBody, "SecretProtector.Protect("));   // 升级走 Save 那颗口，不在读侧另封一次

        var rest = code.Replace(saveBody, string.Empty).Replace(loadBody, string.Empty);
        Assert.DoesNotContain("SecretProtector", rest);
    }

    /// <summary>Save 落盘走「影子副本」逐格抄写。这格钉的是<b>抄全</b>：将来 GitHubOptions
    /// 加一颗序列化字段而影子副本没跟上，Save 就会把它静默打回默认（"改了没落盘"最难自查的
    /// 那一种），所以拿声明侧的名册反着对账——多一格、少一格、改名，这里都会当场红。</summary>
    [Fact]
    public void GithubOptions_SaveShadowCopyCoversEverySerializedProperty()
    {
        var code = Code(ReadRepoFile(OptionsFile));
        var declared = Regex.Matches(code, @"public\s+[\w\?<>,\[\]]+\s+(\w+)\s*\{\s*get;\s*set;\s*\}")
            .Select(m => m.Groups[1].Value).OrderBy(n => n, StringComparer.Ordinal).ToList();
        // 锚点自证：这份档今天就是四格（表达式体的 IsConfigured 不算——它不落盘）
        Assert.Equal(new[] { "PageSize", "SyncIntervalSeconds", "Token", "Username" }, declared);

        var shadow = Between(MethodBody(code, "public void Save("), "new GitHubOptions", "};");
        var assigned = Regex.Matches(shadow, @"^\s*(\w+)\s*=", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(declared, assigned);
    }

    /// <summary>P-89（AI Key）与 P-30 同口径，但 <c>SettingsStore</c> 住在 UI 层、测试工程
    /// 引不到它——行为测落不了地，就按 <c>WiringGate</c> 先例钉形状：封在 SaveAiSettings 体内、
    /// 解在 LoadAiSettings 体内（升级判据与重封也在那一对括号里），SettingsStore 整个
    /// partial 家族没有第三个出口。其它 SaveXxx 对那一格是原样往返——密文进密文出，
    /// 既不二次封也不顺手解，所以「原样往返」这条不需要第二个事实源。</summary>
    [Fact]
    public void SettingsStoreFamily_SecretExitsLiveOnlyInsideTheAiPair()
    {
        var all = Code(ReadRepoPartials(StoreMain));
        var saveBody = MethodBody(all, "public void SaveAiSettings(");
        var loadBody = MethodBody(all, "public AiSettings LoadAiSettings()");

        Assert.Equal(1, Count(saveBody, "SecretProtector.Protect("));
        Assert.Equal(0, Count(saveBody, "SecretProtector.Unprotect("));
        Assert.Equal(1, Count(loadBody, "SecretProtector.Unprotect("));
        Assert.Equal(1, Count(loadBody, "SecretProtector.IsCipher("));
        Assert.Equal(1, Count(loadBody, "SecretProtector.Protect("));   // 明文档首读的透明升级，也只在这一对括号里

        var rest = all.Replace(saveBody, string.Empty).Replace(loadBody, string.Empty);
        Assert.DoesNotContain("SecretProtector", rest);
    }
}
