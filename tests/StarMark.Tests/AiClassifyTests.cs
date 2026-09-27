#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;
using StarMark.Core.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批量整理标签里<b>不需要模型</b>的那一半：标签文本闸门、提示词预算、答复解读、分批与批次隔离。
/// <para>这一半才是这条链真正会出错的地方——模型答什么没法保证，但"答成什么样我们都读得对"是可以断言的。</para>
/// </summary>
public sealed class AiClassifyTests
{
    private static ClassifyItem Item(long id, string title, string? description = null, params string[] tags)
        => new(id, title, null, description, "test", tags);

    private static IReadOnlyList<ClassifyItem> Batch(params long[] ids)
        => ids.Select(id => Item(id, "条目标题 " + id)).ToList();

    // ────────── 标签文本闸门 ──────────

    [Theory]
    [InlineData("前端", "前端")]
    [InlineData("  前端  ", "前端")]
    [InlineData("前端\n开发", "前端开发")]                       // 换行直接去掉，不留成一个怪字
    [InlineData("前端  开发", "前端 开发")]                      // 连续空白折成一个空格
    [InlineData("全　角", "全 角")]                              // 全角空格也算空白
    [InlineData("a\r\nb", "ab")]
    public void TagTextFoldsWhitespaceWithoutInventingContent(string raw, string expected)
        => Assert.Equal(expected, TagText.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("——")]
    [InlineData("「")]
    public void TagsThatCarryNoInformationAreDropped(string? raw)
        => Assert.Null(TagText.Normalize(raw));

    /// <summary>char 31 是 SQL 侧拼标签列的分隔符。<b>名字里带它就会把一个标签劈成两个</b>，
    /// 而那要等到下次读出来才看得见。</summary>
    [Fact]
    public void SeparatorCharacterCanNeverSurviveInATag()
    {
        var dirty = "前端" + TagText.ListSeparator + "开发";
        Assert.Equal("前端开发", TagText.Normalize(dirty));
        Assert.DoesNotContain(TagText.ListSeparator, TagText.Normalize(dirty)!);
    }

    [Fact]
    public void OverlongTagsAreCutInsteadOfRejected()
    {
        var longOne = TagText.Normalize(new string('字', 60))!;
        Assert.Equal(TagText.MaxLength, longOne.Length);
    }

    /// <summary>上限 3（批次 QA-1，与扩展侧实测同口径）：一条给四个已经够分类用，
    /// 而多出来的那一个往往就是"给这一条单独造的专有词"——正是整理要消灭的东西。</summary>
    [Fact]
    public void SanitizeDedupesCaseInsensitivelyAndCapsCount()
    {
        var tags = TagText.Sanitize(new[] { "前端", "前端 ", "FrontEnd", "工具", "读书", "多余" });
        Assert.Equal(new[] { "前端", "FrontEnd", "工具" }, tags);
        Assert.Equal(new[] { "AI" }, TagText.Sanitize(new[] { "AI", "ai ", "Ai" }));   // 大小写不同算同一个词
        Assert.Equal(4, TagText.Sanitize(new[] { "一", "二", "三", "四", "五" }, max: 4).Count);
    }

    // ────────── 提示词与分批 ──────────

    [Fact]
    public void SystemPromptAnchorsOnExistingTagsAndDemandsBareJson()
    {
        var prompt = ClassifyPrompt.SystemPrompt(new[] { "前端", "工具" });

        Assert.Contains("前端、工具", prompt);
        Assert.Contains("只输出一个 JSON 对象", prompt);           // 不写这句，模型会加"好的，下面是结果："
        Assert.DoesNotContain("数据库", prompt);
        // 批次 QA-1：用户抱怨"出一堆各挂一条的标签"，"要能成类"这句必须真的写在提示词里，
        // 而不是只在应用侧偷偷砍——那样模型下一轮还会照旧造专有词。
        Assert.Contains("1-3 个标签", prompt);
        Assert.DoesNotContain("1-4", prompt);
        Assert.Contains("至少", prompt);
        Assert.Contains("宁可少分类", prompt);
    }

    [Fact]
    public void EmptyCatalogSaysSoInsteadOfPresentingAnEmptyVocabulary()
        => Assert.Contains("库里还没有标签", ClassifyPrompt.SystemPrompt(Array.Empty<string>()));

    [Fact]
    public void ItemLinesStayOnePerItemEvenWithMultilineDescriptions()
    {
        var prompt = ClassifyPrompt.UserPrompt(new[]
        {
            Item(7, "标题", "第一行\n第二行\t第三行"),
        });

        Assert.Equal(2, prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);   // 一行说明 + 一行条目
        Assert.DoesNotContain("\t", prompt);
    }

    [Fact]
    public void BatchingRespectsBothItemCountAndCharBudget()
    {
        var many = Enumerable.Range(1, 120).Select(n => Item(n, "T" + n)).ToList();
        var byCount = ClassifyPrompt.Batches(many);
        Assert.Equal(2, byCount.Count);                           // O7 后 80/80 上限：120 → 80 / 40
        Assert.All(byCount, batch => Assert.InRange(batch.Count, 1, ClassifyPrompt.MaxItemsPerBatch));

        var fat = Enumerable.Range(1, 400).Select(n => Item(n, new string('肥', 500))).ToList();
        var byBudget = ClassifyPrompt.Batches(fat);
        Assert.True(byBudget.Count > 1, "只看条数上限时，一批全是长摘要会整批发不出去");
        Assert.All(byBudget, batch =>
            Assert.True(ClassifyPrompt.UserPrompt(batch).Length <= ClassifyPrompt.UserBudgetChars + 200,
                "单批提示词要留在预算内（+200 给编号与固定表头）"));

        // 双口径零漂移（§19.1 落地注）：分批用的 CostOf 永远 ≥ 实际渲染进提示词的字节
        Assert.All(byCount.Concat(byBudget), batch =>
            Assert.True(ClassifyPrompt.UserPrompt(batch).Length - "按编号逐条给标签：\n".Length
                        <= batch.Sum(ClassifyPrompt.CostOf),
                "预算口径与实际载荷分岔——改 ItemLine/CostOf 只改了一处"));
    }

    /// <summary>O1 载荷瘦身的形状：<b>标题是主证据；副标题与摘要各归各的去留规则，
    /// 来源与已有标签整段不发</b>（来源由类型前缀可推断，已有标签在发出前就该被 Plan 过滤——
    /// 让模型"别重复已有标签"是靠提示词里的一句话，而不是每多带一份清单）。</summary>
    [Fact]
    public void TrimmingKeepsTitleDropsTheRestOfTheOldPayload()
    {
        var rich = new ClassifyItem(1, new string('标', 60), "SENDME-副标题", "SENDME-描述", "test", new[] { "已有标签" });
        var line = ClassifyPrompt.ItemLine(rich);
        Assert.Equal(ClassifyPrompt.TitleChars + 1, line.Length);          // 40 字 + 省略号
        Assert.DoesNotContain("SENDME-副标题", line);
        Assert.DoesNotContain("SENDME-描述", line);                          // 标题够长，描述不顶
        Assert.DoesNotContain("已有标签", line);
        Assert.DoesNotContain("来源", ClassifyPrompt.UserPrompt(new[] { rich }));

        var thin = new ClassifyItem(2, "报告", null, new string('描', 60), "test", Array.Empty<string>());
        var thinLine = ClassifyPrompt.ItemLine(thin);
        Assert.Contains("｜", thinLine);                                    // 标题薄 → Desc 兜底上屏
        Assert.Contains(new string('描', ClassifyPrompt.DescFallbackChars), thinLine);
    }

    [Fact]
    public void NothingRequestedMeansNothingScheduled()
        => Assert.Empty(ClassifyPrompt.Batches(Array.Empty<ClassifyItem>()));

    // ────────── 答复解读：三种形状 + 编号错位 ──────────

    [Fact]
    public void ItemsShapeMapsOrdinalsBackToRealIds()
    {
        var batch = Batch(41, 42, 43);
        var parsed = ClassifyPrompt.Parse("""{"items":[{"id":1,"tags":["工具"]},{"id":3,"tags":["读书","  "]}]}""", batch);

        Assert.True(parsed.Readable);
        Assert.Equal(new long[] { 41, 43 }, parsed.Proposals.Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "工具" }, parsed.Proposals[0].Tags);
        Assert.Equal(new[] { "读书" }, parsed.Proposals[1].Tags);   // 空白项被丢掉
        Assert.Equal(new long[] { 42 }, parsed.MissingIds);         // 第二条没答：要数得出来
    }

    [Fact]
    public void CategoriesShapeIsUnpivoted()
    {
        var parsed = ClassifyPrompt.Parse("""{"categories":{"前端":[1,2],"工具":[2]}}""", Batch(7, 9));

        Assert.Equal(new[] { "前端" }, parsed.Proposals.Single(p => p.Id == 7).Tags);
        Assert.Equal(new[] { "前端", "工具" }, parsed.Proposals.Single(p => p.Id == 9).Tags);
        Assert.Empty(parsed.MissingIds);
    }

    [Fact]
    public void BareArrayMapsByPositionAndIgnoresExtras()
    {
        var parsed = ClassifyPrompt.Parse("""[["a"],["b"],["c"]]""", Batch(5, 6));

        // 多出来的第三条不看：位置一错位，标签就会挂到隔壁那条上
        Assert.Equal(new long[] { 5, 6 }, parsed.Proposals.Select(p => p.Id).ToArray());
        Assert.Equal("b", parsed.Proposals[1].Tags[0]);
    }

    [Theory]
    [InlineData("""```json\n{"items":[{"id":1,"tags":["工具"]}]}\n```""")]
    [InlineData("""好的，下面是结果：{"items":[{"id":1,"tags":["工具"]}]} 希望有帮助""")]
    [InlineData("""{\n"items": [\n{"id": 1, "tags": ["工具"]}\n]\n}""")]
    public void FencesAndProseAroundTheJsonAreStripped(string wrapped)
    {
        var parsed = ClassifyPrompt.Parse(wrapped.Replace("\\n", "\n"), Batch(3));
        Assert.True(parsed.Readable, parsed.Error);
        Assert.Equal("工具", parsed.Proposals.Single().Tags.Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("我不知道该给你什么标签")]
    [InlineData("""{"items":[{"id":1,"tags":["工具"]}""")]           // 被 max_tokens 切断的半截
    [InlineData("""{"result":{"whatever":1}}""")]
    public void UnreadableRepliesSayWhyAndAskForTheWholeBatchAgain(string? reply)
    {
        var parsed = ClassifyPrompt.Parse(reply, Batch(11, 12));
        Assert.False(parsed.Readable);
        Assert.NotNull(parsed.Error);
        Assert.Empty(parsed.Proposals);
        Assert.Equal(new long[] { 11, 12 }, parsed.MissingIds);
    }

    /// <summary>模型自己编出来的编号必须丢掉：<b>库里那些 id 是真实存在的别的条目</b>，
    /// 照着打标签就是改到用户没选的东西。</summary>
    [Fact]
    public void InventedOrdinalsAreNeverApplied()
    {
        var parsed = ClassifyPrompt.Parse(
            """{"items":[{"id":2,"tags":["对"]},{"id":99,"tags":["幻觉"]},{"id":0,"tags":["从零开始"]}]}""",
            Batch(21, 22));

        Assert.Equal(new long[] { 22 }, parsed.Proposals.Select(p => p.Id).ToArray());
        Assert.Equal(new long[] { 99 }, parsed.UnknownIds);
        Assert.Equal(new long[] { 21 }, parsed.MissingIds);        // 编号 0 不指任何条目，那一条算没答
    }

    [Fact]
    public void StringIdsAndLooseTagRunsStillParse()
    {
        var parsed = ClassifyPrompt.Parse("""{"items":[{"id":"1","tags":"前端, 工具、读书"}]}""", Batch(8));

        Assert.Equal("前端", parsed.Proposals.Single().Tags[0]);
        Assert.Contains("工具", parsed.Proposals.Single().Tags);
    }

    [Fact]
    public void NewTagsAreReportedOnlyAgainstANonEmptyCatalog()
    {
        var reply = """{"items":[{"id":1,"tags":["前端","没见过的词"]}]}""";

        Assert.Single(Batch(8));
        var withCatalog = ClassifyPrompt.Parse(reply, Batch(8), new[] { "前端", "工具" });
        Assert.Equal(new[] { "没见过的词" }, withCatalog.NewTags);

        var cold = ClassifyPrompt.Parse(reply, Batch(8), Array.Empty<string>());
        Assert.Empty(cold.NewTags);     // 库里本来没标签时，"全是新词"不是信息，只是噪声
    }

    // ────────── 批次编排 ──────────

    private static Task<AiReply> Reply(string text) => Task.FromResult(AiReply.Success(text));

    [Fact]
    public async Task OneBrokenBatchNeitherStopsTheRunNorBlankTheGoodOnes()
    {
        var items = Enumerable.Range(1, 95).Select(n => Item(n, "T" + n)).ToList();   // 两批（O7 后 80/15）：80 + 15
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(items, new[] { "前端" },
            _ =>
            {
                calls++;
                return calls == 1
                    ? Task.FromResult(AiReply.Fail(AiFailureKind.TimedOut, "等到时限"))
                    : Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(2, report.Batches.Count);
        Assert.Equal(1, report.FailedBatches);
        Assert.False(report.Batches[0].Ok);
        Assert.Contains("超时", report.Batches[0].Error);
        Assert.Single(report.Proposals);                          // 第二批整理出来的东西留着
        Assert.Equal(0, report.StoppedBatches);
    }

    /// <summary>真机反馈的另一半：<b>停止落在"正在问的那一批"里面</b>——那时在飞的 HTTP 已被掐断，
    /// await 直接抛出 OperationCanceledException。旧写法让这一抛冒到界面那一层，
    /// 于是"按了停止"表现为"已经整理好的前五批一起没了"。修法是在循环里就地收成"停下并回报"。</summary>
    [Fact]
    public async Task CancellingMidFlightReturnsTheRunInsteadOfThrowingItAway()
    {
        using var cts = new CancellationTokenSource();
        var items = Enumerable.Range(1, 170).Select(n => Item(n, "T" + n)).ToList();      // 三批（O7 后 80/80/10）
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ =>
            {
                calls++;
                if (calls == 2)
                {
                    cts.Cancel();
                    return Task.FromException<AiReply>(new OperationCanceledException());
                }
                return Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, cts.Token);

        Assert.Equal(2, calls);                                   // 第三批一个字节都没问 ⇒ 不再继续烧 token
        Assert.Single(report.Proposals);                          // 停之前那一批的结果留着
        Assert.Equal(0, report.FailedBatches);                    // 被叫停不等于"这一批坏了"（那会让人去查通道）
        Assert.Equal(2, report.StoppedBatches);                   // 正在问的那一批 + 还没问的第三批
        Assert.Equal(3, report.BatchesTotal);
        Assert.True(report.AnythingToApply);
    }

    [Fact]
    public async Task CheckpointCallbackRunsOnEveryBatchBoundary()
    {
        var seen = new List<int>();
        var items = Enumerable.Range(1, 95).Select(n => Item(n, "T" + n)).ToList();   // 两批（O7 后 80/15）

        await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ => Reply("""{"items":[{"id":1,"tags":["工具"]}]}"""),
            report => { seen.Add(report.Index); return Task.CompletedTask; },
            CancellationToken.None);

        // 每批边界各落一次，而不是整轮跑完再一次性补：进程中途被杀时界面显示的进度不许比真实进度新
        Assert.Equal(new[] { 0, 1 }, seen);
    }

    [Fact]
    public async Task StoppingKeepsWhatWasAlreadyOrganisedAndCountsWhatWasNotAsked()
    {
        using var cts = new CancellationTokenSource();
        var items = Enumerable.Range(1, 170).Select(n => Item(n, "T" + n)).ToList();  // 三批（O7 后 80/80/10）
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ =>
            {
                calls++;
                if (calls == 2) cts.Cancel();
                return Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, cts.Token);

        Assert.Equal(2, calls);
        Assert.Equal(1, report.StoppedBatches);                   // 第三批根本没问
        Assert.Equal(2, report.Proposals.Count);                    // 前两批各答了一条，都不丢（每批的编号各自对回本批）
        Assert.Equal(3, report.BatchesTotal);
        Assert.Equal(0, report.FailedBatches);                    // "我叫的停"不是"通道坏了"
    }

    [Fact]
    public async Task AlreadyStoppedBeforeTheFirstBatchAsksNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(new[] { Item(1, "T") }, Array.Empty<string>(),
            _ => { calls++; return Reply("{}"); }, null, cts.Token);

        Assert.Equal(0, calls);
        Assert.Equal(1, report.StoppedBatches);
        Assert.False(report.AnythingToApply);
    }

    [Fact]
    public async Task AnExceptionFromTheChannelIsolatedToItsBatch()
    {
        var report = await ClassifyRunner.RunAsync(new[] { Item(1, "T") }, Array.Empty<string>(),
            _ => throw new InvalidOperationException("通道里抛了"), null, CancellationToken.None);

        Assert.Equal(1, report.FailedBatches);
        Assert.Contains("通道里抛了", report.Batches[0].Error);
    }

    [Fact]
    public async Task APartialAnswerIsCountedNotSilentlyAccepted()
    {
        // 一批问 2 条，只答 1 条：MissingCount 要看得见，否则"整理完了"这句话是假的
        var report = await ClassifyRunner.RunAsync(new[] { Item(1, "A"), Item(2, "B") }, Array.Empty<string>(),
            _ => Reply("""{"items":[{"id":1,"tags":["工具"]}]}"""), null, CancellationToken.None);

        Assert.True(report.Batches[0].Ok);
        Assert.Equal(1, report.Batches[0].MissingCount);
        Assert.Equal(2, report.Batches[0].ItemCount);
    }
}
