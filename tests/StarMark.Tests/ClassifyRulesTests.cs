#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Ai;
using StarMark.Core.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// O2 规则预分类的边界。<b>钉的不是"规则能不能命中"，是三个最容易长歪的方向</b>：
/// ① 空条件规则不得变成"全库命中"（那是"写了个半成品 JSON → 全库被打上同一个标签"的事故形状）；
/// ② 规则命中≠跳过用户（提案与 AI 进同一份方案、同一个预览，下游一刀不能少）；
/// ③ 用户 JSON 坏了按"只有内置"降级，而不是半份生效或炸掉整理。
/// </summary>
public sealed class ClassifyRulesTests
{
    private static ClassifyItem Item(long id, string title, string source = "test", params string[] tags)
        => new(id, title, null, null, source, tags);

    [Fact]
    public void GithubAndLocalSourcesAreRuledWithoutAskingTheModel()
    {
        var items = new[]
        {
            Item(1, "dotnet/runtime", "GitHub"),
            Item(2, "笔记", "本地文件"),
            Item(3, "一篇博客", "书签"),
        };
        var (forAi, ruled) = ClassifyRules.Split(items, ClassifyRules.Defaults);

        Assert.Equal(new[] { 3L }, forAi.Select(i => i.Id));
        Assert.Equal(2, ruled.Count);                             // GitHub→开发 与 本地文件→本地文件 都被截胡，不进 AI
        Assert.Equal(new[] { "开发" }, ruled.Single(r => r.Id == 1).Tags);
        Assert.Equal(new[] { "本地文件" }, ruled.Single(r => r.Id == 2).Tags);   // 提案形状与 AI 产物完全一致，下游无从分辨来源
    }

    [Fact]
    public void DomainNeedlesMatchTitleCaseInsensitively()
    {
        var (_, ruled) = ClassifyRules.Split(
            new[] { Item(1, "C# 入门 - DOCS.MICROSOFT.COM", "书签", "已有") }, ClassifyRules.Defaults);
        Assert.Single(ruled);
        Assert.Equal(new[] { "文档", "微软" }, ruled[0].Tags);
    }

    [Fact]
    public void HalfWrittenRuleNeverMatchesAnything()
    {
        var noCondition = new ClassifyRule(null, null, new[] { "随便" });
        Assert.False(noCondition.Matches(Item(1, "任何标题", "任何来源")));

        var noTags = new ClassifyRule("GitHub", null, Array.Empty<string>());
        Assert.False(noTags.Matches(Item(1, "x", "GitHub")));   // 没标签输出的规则不配"命中"
    }

    [Fact]
    public void BrokenUserJsonFallsBackToDefaults_Alone()
    {
        var merged = ClassifyRules.Merged("这不是 JSON{{{");
        Assert.Equal(ClassifyRules.Defaults.Count, merged.Count);   // 坏档＝没有自定义，内置照常，半份生效不存在

        // 空数组串是"用户把所有自定义删干净了"，不是坏档（[] 合法）
        Assert.Equal(ClassifyRules.Defaults.Count, ClassifyRules.Merged("[]").Count);
    }

    [Fact]
    public void UserRulesTakePrecedenceOverBuiltins()
    {
        const string user = """[{"titleContains":"runtime","tags":["基础设施"]}]""";
        var merged = ClassifyRules.Merged(user);
        var (forAi, ruled) = ClassifyRules.Split(new[] { Item(1, "dotnet/runtime", "GitHub") }, merged);

        Assert.Empty(forAi);
        Assert.Equal(new[] { "基础设施" }, ruled[0].Tags);       // 用户规则在前，先到先得——用户能盖内置，反过来不行
    }

    [Fact]
    public void RuledItemsNeverReachTheModelButKeptInOnePass()
    {
        // 两条 GitHub 条目：一条规则命中；同 id 不会既进 AI 批又进规则堆（一条只有一个归宿）
        var items = new[] { Item(1, "a", "GitHub"), Item(2, "b", "GitHub") };
        var (forAi, ruled) = ClassifyRules.Split(items, ClassifyRules.Defaults);
        Assert.Empty(forAi);
        Assert.Equal(2, ruled.Count);
        Assert.DoesNotContain(ruled.Select(r => r.Id).GroupBy(id => id).Where(g => g.Count() > 1), g => g.Any());
    }
}

/// <summary>§19 O6 分级模型的换算：只有两句话，<b>但"降级规则住在配置里、不散在调用方"这句要靠断言钉</b>——
/// Gateway 与 Runner 各判一次 "ClassifyModel 空就用 Model"，漏的那一处就是"悄悄用了贵模型"。</summary>
public sealed class ClassifyModelSelectionTests
{
    [Fact]
    public void ForClassifyReplacesModelAndTrims()
    {
        var s = new AiSettings(Enabled: true, Provider: AiProviderKind.Ollama, Model: "qwen2.5:32b", ClassifyModel: "  llama3.2:3b  ");
        var forIt = s.ForClassify();
        Assert.Equal("llama3.2:3b", forIt.Model);
        Assert.Equal("qwen2.5:32b", s.Model);               // 原配置不动：主模型是别的路的"主"
        Assert.Equal(s.ClassifyModel, forIt.ClassifyModel); // 分级格原样保留（Trim 只发生在顶上 Model 那一步）
        Assert.Equal(forIt.Model, forIt.ForClassify().Model); // 幂等：对换算结果再换算一次不许再变形
    }

    [Fact]
    public void EmptyClassifyModelFallsThroughToMain()
    {
        var s = new AiSettings(Enabled: true, Model: "big", ClassifyModel: "   ");
        Assert.Same(s, s.ForClassify());                    // 没配分级＝原样，不造"Model 变空白"的中间态
        Assert.Equal("big", s.EffectiveClassifyModel);
    }
}
