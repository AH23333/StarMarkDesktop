#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="StarMark.Abstractions.ExtraJsonGuard"/> 的形状闸门（批次 SK，P-17）。
/// <para>要守的后果：<b>一行读不出的 <c>extra_json</c> 让整页查询失败</b>。SQLite 的
/// <c>json_extract(坏串, …)</c> 抛的是语句级错误，不是"这一行给 NULL"，所以只要<b>任何一个</b>
/// 实参位是裸列，那一行坏数据就够让语言下拉、按语言筛选、按"最近 Star"排序、剪贴板轮转报"加载失败"。
/// 一条禁项把这件事钉死：<b>凡把 <c>extra_json</c> 喂给 <c>json_extract</c> 的地方都必须问判据</b>。</para>
/// <para>刻意<b>不</b>管 <c>json_set</c>/<c>json_remove</c> 的实参：那两句出现在 <c>UPDATE … SET</c> 里，
/// SQLite 只对<b>已通过 WHERE 的行</b>求值，所以那一处靠的是行过滤
/// （<c>AND json_valid(extra_json)</c>，由 <see cref="TheRowLevelFilterStaysWithTheWrite"/> 单独钉）。
/// 这条区别写在判据那节的注释里，也在下面这条用例里钉住——否则下一次"顺手统一"会把行过滤删掉，
/// 而那件事的症状是"改一个布尔顺手抹掉用户整列元数据"，比崩更难被发现。</para>
/// </summary>
public static class ExtraJsonGuardGateTests
{
    private const string Judge = "src/StarMark.Abstractions/ExtraJsonGuard.cs";

    private static readonly Regex ExtractCall = new(@"json_extract\s*\(", RegexOptions.Compiled);

    [Fact]
    public static void EveryExtraJsonArgumentAsksTheJudge()
    {
        var offenders = new List<string>();
        var seen = 0;
        foreach (var (path, text) in SourceGate.ReadRepoUnder("src"))
        {
            var code = SourceGate.Code(text);                     // 注释里举的反例不算违规（#195/#200）
            foreach (Match m in ExtractCall.Matches(code))
            {
                seen++;
                var firstArg = FirstArgument(code, m.Index + m.Length);
                if (firstArg.Contains("extra_json", StringComparison.Ordinal)
                    && !firstArg.Contains("ExtraJsonGuard.Safe(", StringComparison.Ordinal))
                    offenders.Add($"{path}: json_extract({firstArg.Trim()}…");
            }
        }

        // 规模地板：扫不到任何调用点＝扫描器自己坏了（对面是"零违规"这种看不见的答案，#199）。
        Assert.True(seen >= 7,
            $"只认出 {seen} 个 json_extract 调用点，低于今天的面积（7）——是扫描器失效，不是没人违规");
        Assert.True(offenders.Count == 0,
            "把 extra_json 裸着喂进 json_extract，会让一行坏数据把整页查询崩成『加载失败』："
            + string.Join(" | ", offenders));
    }

    [Fact]
    public static void GuardWordingExistsNowhereButTheJudge()
    {
        // 并的是措辞：守卫那一句全仓只许有一颗（手抄第二份＝下次改一处漏一处，正是本批消除的那件事）。
        var copies = SourceGate.ReadRepoUnder("src")
            .Where(f => SourceGate.Code(f.Text).Contains("CASE WHEN json_valid(", StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        Assert.Equal(new[] { Judge }, copies);
    }

    [Fact]
    public static void TheFallbackObjectIsSqlQuotedOnlyInsideTheJudge()
    {
        // 守卫的兜底必须是 SQL 字符串字面量 '{}'；裸 {} 是语法错（本批第一遍全量红三十多条就是它）。
        // 这一条把"这个带引号的字面量只写在判据一处"钉住：别处再手抄一遍守卫就会在这里红。
        var writers = SourceGate.ReadRepoUnder("src")
            .Where(f => SourceGate.Code(f.Text).Contains("'{}'", StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        Assert.Equal(new[] { Judge }, writers);
    }

    [Fact]
    public static void TheBackupThroatSanitizesInsideItsOwnMethod()
    {
        // 接线断言要钉在方法体里：整文件 Contains 会被同文件另一处合法用法顶住（#196）。
        // 还要读**抹掉注释后的代码**：台架 SK4 把那一行整条注释掉，读原文的版本照样绿——
        // 那是"注释里也写着这句话"顶住了断言（#195 同族，这次是我的接线断言犯）。
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Core/Backup/BackupService.cs"),
            "private static string? ValidatePayload");

        Assert.Contains("ExtraJsonGuard.SanitizeForStore(it.ExtraJson)", SourceGate.Code(body));
    }

    [Fact]
    public static void TheRowLevelFilterStaysWithTheWrite()
    {
        // SET 侧那一处靠的是行过滤，不是表达式守卫——两件事不许互相冒充。
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Data/ItemRepository.Local.cs"),
            "private async Task<int> ApplyMissingFlagAsync");

        Assert.Contains("AND json_valid(extra_json)", SourceGate.Code(body));
        Assert.Contains("json_set(extra_json, '$.clipMissing'", SourceGate.Code(body));
        Assert.Contains("json_remove(extra_json, '$.clipMissing')", SourceGate.Code(body));
    }

    [Fact]
    public static void EveryReaderFileStillAsksTheJudge()
    {
        // 面积按文件钉：任何一个读点被改回裸列都会在上面的禁项红；这里再钉"每个文件都还在问"，
        // 防止整段查询被删掉之后禁项反而因为"没调用点"而静默通过。
        Assert.True(SourceGate.Count(SourceGate.ReadRepoFile("src/StarMark.Data/ItemRepository.Search.cs"),
            "ExtraJsonGuard.Safe(") >= 3, "搜索那三条读谓词必须都问判据");
        Assert.True(SourceGate.Count(SourceGate.ReadRepoFile("src/StarMark.Data/ItemRepository.Items.cs"),
            "ExtraJsonGuard.Safe(") >= 2, "浏览模式的语言条件与 starred 排序必须问判据");
        Assert.True(SourceGate.Count(SourceGate.ReadRepoFile("src/StarMark.Data/ItemRepository.Local.cs"),
            "ExtraJsonGuard.Safe(") >= 2, "剪贴板分桶与置缺失标记必须问判据");
    }

    /// <summary>取 <c>json_extract(</c> 之后第一个顶层实参（跳过引号与嵌套括号，直到深度 0 的逗号或右括号）。</summary>
    private static string FirstArgument(string text, int start)
    {
        var depth = 0;
        var sb = new System.Text.StringBuilder();
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\'' or '"')
            {
                var closing = c;
                sb.Append(c);
                i++;
                while (i < text.Length && text[i] != closing)
                {
                    if (text[i] == '\\') { sb.Append(text[i]); i++; }
                    sb.Append(text[i]);
                    i++;
                }
                if (i < text.Length) sb.Append(text[i]);          // 收尾引号
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')')
            {
                if (depth == 0) break;                            // 调用结束
                depth--;
            }
            else if (c == ',' && depth == 0) break;               // 第一个实参到此为止
            sb.Append(c);
        }
        return sb.ToString();
    }
}
