#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// P-3（AE 批附带项）落地的守门：未映射的 ActivityKind 不许冒充增删改三色。
/// <para>当年那颗归类 switch 的兜底臂写着「其余全算增」——将来新增一颗活动 kind（或库行解析出枚举外的值）
/// 会静默披上绿色。本批按 (B) 口径收口：不新增 ActivityKind 成员，只在归类 switch 补中性兜底
/// 「Neutral＝灰」，且既有 11 档的绿/红/黄一分不动。</para>
/// <para>11 档逐成员全显枚举是刻意的——往后有人往 ActivityKind 加第 12 档而忘了归类，
/// 这里当场红（新成员没进 switch 就落不进任何一臂，只能掉进中性兜底；若连兜底也被改回「算增」，
/// 下一例也红）。</para>
/// </summary>
public sealed class ActivityNeutralFallbackGateTests
{
    private const string KindEnum = "src/StarMark.Abstractions/Activity.cs";
    private const string ActivityVm = "src/StarMark.UI/ViewModels/ActivityItemViewModel.cs";
    private const string Converter = "src/StarMark.UI/Helpers/ActivityChangeToBrushConverter.cs";

    private static List<string> ActivityKindMembers()
    {
        var body = MethodBody(ReadRepoFile(KindEnum), "public enum ActivityKind");
        return Regex.Matches(body, @"^\s{4}(?<name>[A-Z]\w*),", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value).ToList();
    }

    /// <summary>每一档 ActivityKind 都在归类 switch 里显式出现——掉进兜底臂的只许是「枚举里还没有的」。</summary>
    [Fact]
    public void EveryActivityKindIsExplicitlyClassified()
    {
        var kinds = ActivityKindMembers();
        Assert.True(kinds.Count >= 11, $"只数到 {kinds.Count} 档 ActivityKind（枚举解析空转）");
        var body = Code(MethodBody(ReadRepoFile(ActivityVm), "private static ActivityChange ChangeOf(ActivityKind k)"));
        foreach (var k in kinds)
        {
            Assert.Contains("ActivityKind." + k, body);
        }
    }

    /// <summary>兜底臂归中性（灰），不许改回「算增」——P-3 修的就是这个误读。</summary>
    [Fact]
    public void UnmappedKindsFallToNeutralNotAdded()
    {
        var body = Code(MethodBody(ReadRepoFile(ActivityVm), "private static ActivityChange ChangeOf(ActivityKind k)"));
        Assert.Contains("_ => ActivityChange.Neutral", body);
        Assert.DoesNotContain("_ => ActivityChange.Added", body);
    }

    /// <summary>三色取色一分不动；中性档解析到中性灰，与增删改三色分开。</summary>
    [Fact]
    public void ThreeColorsStayPutAndNeutralResolvesToGray()
    {
        var conv = Code(ReadRepoFile(Converter));
        Assert.Contains("ActivityChange.Removed => \"SystemFillColorCriticalBrush\"", conv);
        Assert.Contains("ActivityChange.Modified => \"SystemFillColorCautionBrush\"", conv);
        Assert.Contains("_ => \"SystemFillColorSuccessBrush\"", conv);
        Assert.Contains("ActivityChange.Neutral => \"SystemFillColorNeutralBrush\"", conv);
    }
}
