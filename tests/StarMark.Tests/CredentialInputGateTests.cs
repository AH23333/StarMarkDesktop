#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页里<b>凭据输入格</b>的形状守门（批次 UI，P-146）。
/// <para>
/// 起因是一条真实存在的泄漏面：<c>TextBox</c> 的内容在 UIA 树里就是<b>可读的 Value</b>——读屏、放大镜、
/// 自动化、截图这类走 UIA 或走像素的路径都能把 GitHub Token 带走，而且不需要任何权限。
/// <c>PasswordBox</c> 不给这个 Value，代价是它的 <c>Password</c> <b>不可绑定</b>（WinUI 刻意如此），
/// 于是"档 → 界面 → 档"这一条链必须由代码里<b>那一对读写</b>撑起来。
/// </para>
/// <para>
/// 所以这一族要钉的是<b>两件事一起成立</b>，缺一不可：① 那一格确实是打码的（不是 TextBox）；
/// ② 换成 code-behind 之后"只有一个编辑入口、且只有一对读写"这条没有松动——
/// 以前它是靠数 <c>Mode=TwoWay</c> 钉住的（<see cref="SettingsTaxonomyGateTests.EachSettingHasExactlyOneEditor"/>），
/// 那套计数对 PasswordBox 根本不成立，直接把属性从那张表里删掉就等于<b>没人管这一格了</b>。
/// </para>
/// <para>
/// 判据一律<b>不钉控件名</b>：名字是从代码里 <c>.Password</c> 的用法派生出来的。
/// 改名（<c>GithubTokenBox → GithubTokenInput</c> 这种）必须原样放过——把名字钉死会让下一次合理的
/// 重命名变成一次"闸门红了"的假事故，而那正是让人去放宽判据的最快路径（#33 那条纪律）。
/// </para>
/// </summary>
public sealed class CredentialInputGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string PageCode = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";

    /// <summary>住在 ViewModel 里、以字符串形态存的凭据属性。<b>只加不减</b>：
    /// 再加一颗（比如哪天设置页能填其它 Token）就登记进来，否则那一格连"不许用明文框"这条都不成立。
    /// 这张表不是靠人记得住——<see cref="TheCredentialTableCoversEveryCredentialShapedViewModelProperty"/>
    /// 拿源码里实际存在的属性反过来对它，删表项与"加了新凭据却没登记"都会当场红。</summary>
    private static readonly string[] CredentialProperties = { "GithubToken" };

    /// <summary>形状像凭据的属性名后缀（小写比对）：新增一颗而表里没它，就是这一族要拦的那种"顺手加一格明文框"。</summary>
    private static readonly string[] CredentialNameMarks = { "token", "apikey", "api_key", "secret", "password", "credential" };

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>扫出所有<b>绑定到指定属性</b>的输入元素（跨行也算：属性可以换行写）。</summary>
    private static List<string> PlainBoxesBoundTo(string xaml, string property)
        => Regex.Matches(Markup(xaml), @"<TextBox\b[^>]*?/?>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(el => el.Contains($"ViewModel.{property}", StringComparison.Ordinal))
            .ToList();

    /// <summary>代码里被当作密码框读过的那些控件名（<c>Xxx.Password</c> 的 <c>Xxx</c>）。</summary>
    private static string[] BoxesReadThroughPassword(string code)
        => Regex.Matches(code, @"(\w+)\.Password").AsEnumerable()
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 那张凭据表<b>不许被偷偷删空</b>，也不许漏掉新长出来的凭据：拿 ViewModel 里实际存在的
    /// <c>[ObservableProperty] private string _xxx</c> 反向对账，名字形状像凭据的必须<b>逐个登记</b>。
    /// <para>为什么这一格比前面几格更要紧：前面全是"按表扫"，表 emptied 之后每一格都会因为
    /// "没有东西要查"而整齐地绿过去——那是这一族最坏的一种假绿（查不出任何东西， yet 一句红都没有）。</para>
    /// </summary>
    [Fact]
    public void TheCredentialTableCoversEveryCredentialShapedViewModelProperty()
    {
        var shaped = Regex.Matches(Code(ReadRepoPartials(ViewModel)),
                @"\[ObservableProperty\]\s+private\s+string\s+_(\w+)")
            .Select(m => m.Groups[1].Value)
            .Where(n => CredentialNameMarks.Any(mark => n.ToLowerInvariant().Contains(mark, StringComparison.Ordinal)))
            .Select(n => char.ToUpperInvariant(n[0]) + n[1..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        // 锚点自证：这一族今天确实存在（Token 那一颗），否则"表与源码相互对账"这句话是空的
        Assert.NotEmpty(shaped);
        Assert.Equal(shaped, CredentialProperties.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>禁项扫描器自己要有证人：喂一段"退回明文框"的样串必须命中，
    /// 否则这一格是装饰（#228/#236 那一族——注释替被改掉的形状答题、锚点死了守门白过）。</summary>
    [Fact]
    public void ThePlainTextBoxScannerActuallyBites()
    {
        var sample = "<StackPanel><TextBox PlaceholderText=\"ghp_...\" Text=\"{x:Bind ViewModel.GithubToken, Mode=TwoWay}\"/></StackPanel>";
        Assert.Single(PlainBoxesBoundTo(sample, "GithubToken"));
        // 同一套扫描必须放过合法形状：打码框、以及"文字里出现那个属性名但元素不是 TextBox"
        Assert.Empty(PlainBoxesBoundTo("<PasswordBox x:Name=\"GithubTokenBox\"/>", "GithubToken"));
        Assert.Empty(PlainBoxesBoundTo("<TextBlock Text=\"ViewModel.GithubToken\"/>", "GithubToken"));
        // 也放过写在注释里的旧写法：守门读的是抹掉注释之后的那一份，注释不该替被改掉的形状答题
        Assert.Empty(PlainBoxesBoundTo("<!-- TextBox Text=\"{x:Bind ViewModel.GithubToken}\" --><Grid/>", "GithubToken"));
    }

    /// <summary>每一颗凭据属性<b>都不得</b>由 <c>TextBox</c> 承载。今天的现场就是这一格被咬住过（批次 UI）。</summary>
    [Fact]
    public void NoCredentialIsTypedIntoAPlainTextBox()
    {
        var xaml = ReadRepoFile(Xaml);
        foreach (var property in CredentialProperties)
            Assert.Empty(PlainBoxesBoundTo(xaml, property));
    }

    /// <summary>代码里读过的每一颗密码框，在 XAML 里<b>必须是 PasswordBox 且只有一颗</b>，
    /// 并且<b>没有同名的 TextBox 残留</b>（换控件最容易留下的就是那一半旧标记）。
    /// 名字派生自代码，不派生自常量：合理改名不许惊动这一格。</summary>
    [Fact]
    public void EveryBoxReadThroughPasswordIsDeclaredMaskedExactlyOnce()
    {
        var xaml = Markup(ReadRepoFile(Xaml));
        var code = Code(ReadRepoPartials(PageCode));
        var boxes = BoxesReadThroughPassword(code);
        // 锚点自证：这一页确实有密码框（AI 的 Key 与 GitHub 的 Token），扫到 0 颗就是锚点死了
        Assert.True(boxes.Length >= 2, $"只从代码里派生出 {boxes.Length} 颗密码框——锚点失效，守门不许白过");
        foreach (var box in boxes)
        {
            Assert.Single(Regex.Matches(xaml, $"<PasswordBox\\b[^>]*x:Name=\"{box}\"", RegexOptions.Singleline));
            Assert.Empty(Regex.Matches(xaml, $"<TextBox\\b[^>]*x:Name=\"{box}\"", RegexOptions.Singleline));
        }
    }

    /// <summary>
    /// 换成不可绑定的控件之后，"只有一个编辑入口"这条换了形状但仍要成立：
    /// 对每一颗凭据属性，代码里<b>档→界面</b>与<b>界面→档</b>各恰好一处，<b>而且是同一颗控件</b>。
    /// <para>为什么合不成一处：分在两颗控件上就是"界面显示 A 的密码、保存的却是 B"——
    /// 编译器不管、运行时也不报错，症状是"用户明明填了 Token，同步还是说没配置"。
    /// 为什么各只许一处：两处读就是两份真相（与 <see cref="SettingsTaxonomyGateTests.EachSettingHasExactlyOneEditor"/> 同一族）。</para>
    /// </summary>
    [Fact]
    public void EachCredentialHasExactlyOnePairOfPasswordReadersOnTheSameBox()
    {
        var code = Code(ReadRepoPartials(PageCode));
        foreach (var property in CredentialProperties)
        {
            var into = Regex.Matches(code, $@"(\w+)\.Password\s*=\s*ViewModel\.{property}\b");
            var outOf = Regex.Matches(code, $@"ViewModel\.{property}\s*=\s*(\w+)\.Password\b");
            Assert.Single(into);
            Assert.Single(outOf);
            Assert.Equal(into[0].Groups[1].Value, outOf[0].Groups[1].Value);   // 两头指同一颗控件
        }
    }

    /// <summary>
    /// <b>载入那一行必须落在 <c>LoadFromStoreSilently</c> 的抑制区内</b>。
    /// <para>赋 <c>Password</c> 会当场触发 <c>PasswordChanged</c>，而那个处理器往 ViewModel 里推值——
    /// 属性变更是去抖保存的触发源（批次 GM/HC 那一族），于是"打开设置页"就变成"重写一次凭据档"。
    /// 这条不需要人发现：它今天成立，明天有人觉得"顺手放到别处更清楚"就破了，而症状只是多写几次盘。</para>
    /// </summary>
    [Fact]
    public void TheLoadDirectionHappensInsideTheSaveSuppressedRegion()
    {
        var code = Code(ReadRepoPartials(PageCode));
        var body = MethodBody(code, "private void LoadFromStoreSilently()");
        Assert.Contains("_suppressSave = true", body, StringComparison.Ordinal);
        Assert.Matches(@"(\w+)\.Password\s*=\s*ViewModel\.GithubToken", body);
        // 反向：那一行不许同时出现在抑制区之外（别处再来一次赋值就是第二次写盘的入口）
        var elsewhere = Code(ReadRepoPartials(PageCode)).Replace(body, string.Empty);
        Assert.DoesNotMatch(@"(\w+)\.Password\s*=\s*ViewModel\.GithubToken", elsewhere);
    }

    /// <summary>打码不是只把界面挡住：<b>那一条链上任何一环都不许把密码写进日志</b>。
    /// （凭据落盘的账在 P-30，本格只管这一族新加的那条路——改成 code-behind 之后，
    /// 代码里读密码的地方从"没有"变成"有"，所以这条要当场跟上。）
    /// <para>与 <see cref="NoLogLineAnywhereCarriesACredentialValue"/> 是定点与普查两份证人：同一把尺子
    /// （<see cref="LogCallsCarryingCredentialValues"/>），定点那份红的时候能直接说出是哪一页，
    /// 普查那份保证"把这页改名或搬家"也躲不掉。</para></summary>
    [Fact]
    public void NoLogLineReadsAPasswordField()
        => Assert.Empty(LogCallsCarryingCredentialValues(Code(ReadRepoPartials(PageCode))));

    // ────────── P-30 的两张"净结论"证人（批次 VP）──────────
    //
    // CC 批普查出"全仓无一处把 Token 值写进日志"与"备份载荷不含 GitHubOptions"，两句都记进了账本，
    // 但**当时没有任何一格会替它们红**——那正是坑表 #231 那一族（"已修复"没有证人就会被改掉第二次）。
    // 这两格把它们从"读过一遍"变成"改坏了会响"。

    /// <summary>凭据<b>取值</b>的读法（带点号，所以中文说明里写"Token"两个字不会被误伤）。
    /// <para>表里同时收 <c>.GithubToken</c> 与 <c>.AiApiKey</c>：属性名带前缀的那两颗是真凭据，
    /// 而 <c>\w*Token</c> 那种松口径会把 <c>.TotalToken</c>／<c>.AiTokenBudget</c>／<c>.ContinuationToken</c>
    /// 一起卷进来（那些是<b>计数</b>，不是秘密）——误伤一次的代价是天天红，最后被人整个绕过（#144）。
    /// 所以这里按<b>本仓实际存在的凭据属性名</b>逐颗登记，而不是按字根猜。</para></summary>
    private static readonly string[] CredentialValueMarks =
        [".Token", ".Password", ".ApiKey", ".Secret", ".Credential", ".GithubToken", ".AiApiKey"];

    /// <summary>字段名是不是凭据形状（与 <see cref="CredentialNameMarks"/> 同一批标记）。</summary>
    private static bool LooksLikeCredential(string name)
        => CredentialNameMarks.Any(mark => name.Contains(mark, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 从每一处 <c>StarLog.Xxx(</c> 起<b>按括号配对切出实参表</b>，看里面有没有凭据取值。
    /// <para>为什么不是"扫当前行"：日志文案折行是常见写法，而那个<b>值往往就落在续行上</b>
    /// （<c>StarLog.Warn($"…"{exn} + options.Token)</c>）。按行数会把这种形状整个放过——
    /// 守门只认一行，等于教人把违规写成两行（#54 那一族：判据要钉在<b>会出事的那一处</b>）。</para>
    /// <para>与 <c>FormatScanner.ToStringArgumentLists</c> 同一简化：括号按字面配对，因此串里出现
    /// 落单的 <c>)</c> 时会<b>少读</b>而不是多读。宁可漏一条极端写法，也不要天天假失败逼人放宽判据（#123/#144）。</para>
    /// </summary>
    private static List<string> LogCallsCarryingCredentialValues(string code)
    {
        var hits = new List<string>();
        for (var at = code.IndexOf("StarLog.", StringComparison.Ordinal); at >= 0;
             at = code.IndexOf("StarLog.", at + "StarLog.".Length, StringComparison.Ordinal))
        {
            var open = code.IndexOf('(', at);
            if (open < 0) break;
            var depth = 0;
            var end = open;
            for (; end < code.Length; end++)
            {
                if (code[end] == '(') depth++;
                else if (code[end] == ')' && --depth == 0) break;
            }
            var args = code[open..Math.Min(end + 1, code.Length)];
            if (!CredentialValueMarks.Any(m => args.Contains(m, StringComparison.Ordinal))) continue;
            hits.Add($"第 {1 + code.Take(at).Count(c => c == '\n')} 行：{args.Replace('\n', ' ').Replace('\r', ' ').Trim()}");
        }
        return hits;
    }

    /// <summary>先自证这颗尺子两头都灵：会咬住真正的取值（<b>单行与续行两种形状都算</b>），
    /// 也不会把"名字里没有凭据字样"的字段冤枉进来。</summary>
    [Fact]
    public void TheCredentialLogScannerActuallyBites()
    {
        Assert.Single(LogCallsCarryingCredentialValues("StarLog.Warn($\"Token 被拒：{options.Token}\");"));
        // 反规避：值写在续行上一样要抓到——按行数的那份尺子在这一格就会假绿
        Assert.Single(LogCallsCarryingCredentialValues("StarLog.Warn(\"Token 被拒：\"\n    + options.Token);"));
        // 属性名带前缀的那一颗也得认（ViewModel.GithubToken 才是设置页今天真正的凭据读法）
        Assert.Single(LogCallsCarryingCredentialValues("StarLog.Info($\"当前 Token = {ViewModel.GithubToken}\");"));
        // 口径不许宽到把「计数」当凭据：那几颗名字里都有 token，但打出去的是一个数
        Assert.Empty(LogCallsCarryingCredentialValues("StarLog.Info($\"月度用量 {usage.TotalToken} / 上限 {budget.AiTokenBudget}\");"));
        // 说明性文字里出现"Token"三个字不算（那是人话，不是值）
        Assert.Empty(LogCallsCarryingCredentialValues("StarLog.Warn(\"github.json 里没有配置 Token，同步跳过\");"));
        // 注释里引用一句旧写法也不算（守门读的是抹掉注释之后的那一份，与上面几格同一口径）
        Assert.Empty(LogCallsCarryingCredentialValues(Code("// StarLog.Warn(options.Token);\nStarLog.Info(\"配置已读取\");")));
        Assert.True(LooksLikeCredential("GithubToken") && !LooksLikeCredential("ExportedAt"));
    }

    /// <summary>
    /// 全仓（<c>src/</c> 下每一颗 .cs）都不许有"日志调用里读凭据取值"的形状。
    /// <para>为什么扫整仓而不是这一页：CC 那条普查说的就是"全仓零处"，只守设置页等于把其余几百个文件
    /// 当作不会有人动——而加一行日志是"顺手"级别的改动。</para>
    /// </summary>
    [Fact]
    public void NoLogLineAnywhereCarriesACredentialValue()
    {
        var scanned = 0;
        var offenders = new List<string>();
        foreach (var (path, text) in ReadRepoUnder("src"))
        {
            offenders.AddRange(LogCallsCarryingCredentialValues(Code(text)).Select(h => $"{path} {h}"));
            scanned++;
        }
        Assert.True(scanned >= 100, $"只扫了 {scanned} 个源文件——普查路径错了，守门不许白过");
        Assert.True(offenders.Count == 0, "有日志调用把凭据取值带出去了：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 备份档的<b>载荷形状</b>里不许出现凭据字段：用户会把这个 <c>.json</c> 发到群里、传网盘、
    /// 交给别人排查——"顺手把设置也备进去"那一步一旦做了，泄露的就是活的 GitHub 凭据。
    /// 今天它不含 <c>GitHubOptions</c>（CC 已核实），这一格钉的是"以后也别加"。
    /// </summary>
    [Fact]
    public void TheBackupPayloadHasNoCredentialShapedField()
    {
        var envelope = Code(ReadRepoPartials("src/StarMark.Core/Backup/BackupEnvelope.cs"));
        var fields = Regex.Matches(envelope, @"public\s+[\w\?<>,\s]+?\s(\w+)\s*\{\s*get;")
            .Select(m => m.Groups[1].Value).ToList();
        // 锚点自证：这个文件今天确实有 8 个以上的序列化字段，认不出来就是正则死了而非"干净"
        Assert.True(fields.Count >= 8, $"只从 BackupEnvelope.cs 认出 {fields.Count} 个字段——锚点失效，守门不许白过");
        Assert.True(fields.Contains("Checksum") && fields.Contains("WidgetsJson"),
            "认出来的字段里没有 Checksum/WidgetsJson ⇒ 扫描范围不对，'没有凭据字段'这句话就没证人");
        Assert.DoesNotContain(fields, LooksLikeCredential);
    }
}
