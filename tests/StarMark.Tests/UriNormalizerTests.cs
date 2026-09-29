#nullable enable
using Xunit;
using StarMark.Abstractions;

namespace StarMark.Tests;

public sealed class UriNormalizerTests
{
    [Theory]
    [InlineData("https://github.com/a/b/", "https://github.com/a/b")]
    [InlineData("https://github.com/a/b?tab=readme", "https://github.com/a/b")]
    [InlineData("https://github.com/owner/repo/issues/123", "https://github.com/owner/repo")]
    [InlineData("http://example.com:80/x", "http://example.com/x")]
    [InlineData("https://example.com:443/x", "https://example.com/x")]
    [InlineData("https://Example.COM/x", "https://example.com/x")]
    [InlineData("https://example.com/p#frag", "https://example.com/p")]
    [InlineData("https://example.com/p?utm_source=x&id=1", "https://example.com/p?id=1")]
    [InlineData("https://example.com/p?fbclid=abc&id=1", "https://example.com/p?id=1")]
    public void Normalize_Standardizes(string input, string expected)
    {
        Assert.Equal(expected, UriNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_NonHttpPassthrough()
    {
        // file:// 等非 http(s) 源不应被改动，避免破坏文件路径键
        Assert.Equal("file:///C:/x.txt", UriNormalizer.Normalize("file:///C:/x.txt"));
    }

    /// <summary>
    /// 批次 Z 不变式：本地文件的 source_id 是 <see cref="LocalFileIdentity.SourceIdForPath"/> 产出的
    /// 裸 16 位十六进制串（不是 URI），而 <c>ItemRepository.UpsertOne</c> 对<b>每个</b> upsert 都会把
    /// source_id 喂给这个「面向 URL」的 Normalize。若 Normalize 哪天尝试「修正」这类短串（补协议、
    /// 改动大小写），文件去重键会被静默破坏、同一文件裂成多条。这里钉死恒等 + 幂等。
    /// 用真实哈希而非硬编码串，确保测的是实际落库形态。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\me\文档\报告 v2#1.txt")]
    [InlineData(@"D:\Program Files\app.exe")]
    [InlineData(@"E:\")]
    public void Normalize_LeavesLocalFileSourceIdUntouched(string path)
    {
        var sourceId = LocalFileIdentity.SourceIdForPath(path);
        Assert.Equal(sourceId, UriNormalizer.Normalize(sourceId));
        Assert.Equal(sourceId, UriNormalizer.Normalize(UriNormalizer.Normalize(sourceId)));
    }

    [Fact]
    public void Normalize_Idempotent()
    {
        var a = "https://github.com/a/b?tab=readme";
        Assert.Equal(UriNormalizer.Normalize(a), UriNormalizer.Normalize(UriNormalizer.Normalize(a)));
    }

    [Theory]
    [InlineData("http://example.com//", "http://example.com/")]
    [InlineData("http://example.com///", "http://example.com/")]
    [InlineData("https://github.com//", "https://github.com/")]
    public void Normalize_MultiSlashRoot_CollapsesToSingleSlashAndIsIdempotent(string input, string expected)
    {
        // 旧实现 TrimEnd('/') 把 "//" 塌成空串 → 重建得 "http://host"（无尾斜杠）；
        // 再次归一时 AbsolutePath 变 "/"（长度 1，不再触发去尾斜杠）→ "http://host/"，两次不一致、裂成两个 source_id。
        var once = UriNormalizer.Normalize(input);
        Assert.Equal(expected, once);
        Assert.Equal(once, UriNormalizer.Normalize(once)); // 幂等
    }

    [Fact]
    public void Normalize_EmptyReturnsEmpty()
    {
        Assert.Equal(string.Empty, UriNormalizer.Normalize(string.Empty));
        Assert.Equal(string.Empty, UriNormalizer.Normalize(null!));
    }

    [Fact]
    public void Normalize_KeepsNonDefaultPort()
    {
        Assert.Equal("https://example.com:8080/x", UriNormalizer.Normalize("https://example.com:8080/x"));
    }

    [Fact]
    public void Normalize_Ipv6Literal_PreservesBracketsAndIsIdempotent()
    {
        // .NET 的 Uri.Host 对 IPv6 字面量本就返回带方括号的 "[::1]"，归一后应保持括号且幂等。
        // 这条用例是"防回归"：任何试图再包一层方括号、或改用 Authority 拼端口导致错位的改动都会打破它。
        var once = UriNormalizer.Normalize("http://[::1]:8080/x");
        Assert.Equal("http://[::1]:8080/x", once);
        Assert.Equal(once, UriNormalizer.Normalize(once));

        // 默认端口 443 去掉，方括号保留
        Assert.Equal("https://[2001:db8::1]/p", UriNormalizer.Normalize("https://[2001:db8::1]:443/p"));
    }

    /// <summary>
    /// 批次 DD：IsGitHub 的 `.github.com` 后缀子句（UriNormalizer.cs:68）此前零测——所有 github 用例都用裸
    /// github.com。该子句决定 gist.github.com 等子域是否享受「/owner/repo 收窄 + tab 清空」，
    /// 一旦回归（改成 == 比较）这些主机的 source_id 会静默不再收窄、同一仓库裂成多条。同时钉死反向：
    /// 仅以 "github.com" 结尾但**缺点号**的 notgithub.com 不是 GitHub，绝不能被误收窄（防过度匹配）。
    /// <para>批次 RW 改过本条的一条期望值：`www.github.com` 现在会先折进主域再收窄，
    /// 所以输出是 `github.com/...` 而不是 `www.github.com/...`。**这条测的仍是子域收窄**（意图未变），
    /// 变的只是那台主机的标准形——它本来就是 RW 要修的分裂成因。</para>
    /// </summary>
    [Theory]
    [InlineData("https://www.github.com/owner/repo/issues/5", "https://github.com/owner/repo")]
    [InlineData("https://gist.github.com/alice/abc?tab=example", "https://gist.github.com/alice/abc")]
    public void Normalize_GitHubSubdomain_NarrowsLikeApex(string input, string expected)
    {
        var once = UriNormalizer.Normalize(input);
        Assert.Equal(expected, once);
        Assert.Equal(once, UriNormalizer.Normalize(once));   // 幂等
    }

    [Theory]
    [InlineData("https://notgithub.com/owner/repo/issues/5")]
    [InlineData("https://mygithub.com/a/b/c")]
    public void Normalize_HostEndingInGithubWithoutDot_IsNotTreatedAsGitHub(string input)
    {
        // 无点号后缀不应命中 IsGitHub 的 EndsWith(".github.com")，路径不收窄。
        Assert.Equal(input, UriNormalizer.Normalize(input));
    }

    /// <summary>
    /// 批次 DD：StripTrackingParams 的「参数全被剥除 → 返回空串」分支（:91 kept.Count==0）+ 重建时
    /// 「query 空则不拼 '?'」的 IsNullOrEmpty 守卫（:63）此前无测——既有仅覆盖 utm+存活参数（?id=1 保留）。
    /// 纯追踪链接（分享链常见形态）若被误留一个悬空 '?'，source_id 就与去参标准形分裂、同页去重失效。
    /// </summary>
    [Theory]
    [InlineData("https://example.com/p?utm_source=a&utm_medium=b")]
    [InlineData("https://example.com/p?fbclid=1")]
    [InlineData("https://example.com/docs?gclid=x&mc_eid=y")]
    public void Normalize_AllParamsAreTracking_StrippedWithoutStrayQuestionMark(string input)
    {
        var once = UriNormalizer.Normalize(input);
        Assert.DoesNotContain("?", once);
        Assert.Equal(input.Split('?')[0], once);   // 恰等于去 query 的裸形，无悬空 '?'
        Assert.Equal(once, UriNormalizer.Normalize(once));   // 幂等
    }

    /// <summary>
    /// 批次 RW（P-118）：同一仓库的十种写法必须收敛成**一个** source_id。
    /// 这条故意不比对"字符串长什么样"，只比对"是不是同一个键"——那才是去重的真判据，
    /// 也是扩展 <c>normalize.ts:40-45</c> 那条"用户实测踩坑"注释对应的三件事：
    /// <c>www.</c> 折叠、github 强制 https、<c>/owner/repo</c> 段折小写。
    /// 三条里任何一条回归，这里都会重新裂成多行（且"疑似重复"会误报）。
    /// </summary>
    [Fact]
    public void Normalize_SameGithubRepo_EverySpellingCollapsesToOneKey()
    {
        var variants = new[]
        {
            "https://github.com/owner/repo",
            "http://github.com/owner/repo",
            "https://www.github.com/owner/repo",
            "http://www.github.com/Owner/Repo/",
            "https://WWW.GitHub.COM/OWNER/REPO/",
            "https://github.com/Owner/REPO/issues/123",
            "https://github.com/owner/repo?tab=repositories",
            "https://github.com:443/owner/repo",
            "http://github.com:80/owner/repo",
            "https://github.com/owner/repo/pulse?utm_source=share",
        };
        var keys = variants.Select(UriNormalizer.Normalize).Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "https://github.com/owner/repo" }, keys);
        foreach (var k in keys) Assert.Equal(k, UriNormalizer.Normalize(k));   // 幂等
    }

    [Theory]
    [InlineData("http://www.github.com/", "https://github.com/")]
    [InlineData("https://github.com", "https://github.com/")]
    [InlineData("https://GitHub.com/Owner", "https://github.com/owner")]
    public void Normalize_GithubApex_RootAndSingleSegmentForms(string input, string expected)
    {
        var once = UriNormalizer.Normalize(input);
        Assert.Equal(expected, once);
        Assert.Equal(once, UriNormalizer.Normalize(once));   // 幂等
    }

    /// <summary>
    /// 批次 RW：子域（gist 等）**只升协议**——主机不折进主域、路径大小写也不折。
    /// 理由排序是"宁可裂也不许错并"：gist 的 id 段不保证大小写无关，一旦折小写，
    /// 两个不同资源的标签/笔记会挂到同一条上——那比多出一行难发现得多。
    /// </summary>
    [Fact]
    public void Normalize_GithubSubdomain_UpgradesHttpsButKeepsHostAndCase()
    {
        var once = UriNormalizer.Normalize("http://gist.github.com/alice/AbC123");
        Assert.Equal("https://gist.github.com/alice/AbC123", once);
        Assert.Equal(once, UriNormalizer.Normalize(once));
    }

    /// <summary>
    /// 批次 RW 的反向闸门：三条 github 规则绝不能漏到别的站点上。
    /// http 不升级、路径大小写不折——这两条若被"顺手统一"，会把成千上万条非 github 书签的键改掉、
    /// 并把大小写敏感的服务器路径（/Docs 与 /docs 常是两个页面）错并成一条。
    /// </summary>
    [Theory]
    [InlineData("http://example.com/x")]
    [InlineData("https://example.com/Docs/FAQ")]
    [InlineData("https://notgithub.com/Owner/Repo")]
    [InlineData("https://github.com.evil.com/Owner/Repo")]
    public void Normalize_NonGithub_UnaffectedByTheGithubRules(string input)
    {
        var once = UriNormalizer.Normalize(input);
        Assert.Equal(input, once);
        Assert.Equal(once, UriNormalizer.Normalize(once));
    }
}
