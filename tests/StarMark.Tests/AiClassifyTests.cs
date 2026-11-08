#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;
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

        Assert.Contains("前端, 工具", prompt);                  // 参考标签仍原样进提示词（库里的词是数据）；连接符随英文协议走 ", "
        Assert.Contains("one item per line", prompt);           // O4 行格式；不写死格式要求，模型会加"好的，下面是结果："
        Assert.DoesNotContain("只输出一个 JSON 对象", prompt);   // 旧口径不许悄悄回潮（JSON 三态仍是解析兼容层，但提示词不再要求它）
        Assert.DoesNotContain("数据库", prompt);
        // 批次 QA-1：用户抱怨"出一堆各挂一条的标签"，"要能成类"这句必须真的写在提示词里，
        // 而不是只在应用侧偷偷砍——那样模型下一轮还会照旧造专有词。
        Assert.Contains("1-3 tags per item", prompt);
        Assert.DoesNotContain("1-4", prompt);
        Assert.Contains("at least 3 items", prompt);
        Assert.Contains("under-tagging beats one tag per item", prompt);
        // ✅P-210 他裁「始终为英文」：标签词的工作语言必须显式写在协议里，不许只靠"提示词恰好是英文"暗示。
        Assert.Contains("Write tags in English", prompt);
    }

    [Fact]
    public void EmptyCatalogSaysSoInsteadOfPresentingAnEmptyVocabulary()
        => Assert.Contains("The library has no tags yet", ClassifyPrompt.SystemPrompt(Array.Empty<string>()));

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
            Assert.True(ClassifyPrompt.UserPrompt(batch).Length - "Tag each item by number:\n".Length
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

    /// <summary>§20.3 预估：弹窗与将要真实发出的字节逐字同源（同一 Batches、同一份 UserPrompt 渲染结果）——
    /// "弹窗说 15K 实际发 40K"比不预估更伤信任，所以这里钉的是<b>相等</b>，不是"同量级"。</summary>
    [Fact]
    public void EstimateEqualsWhatWouldActuallyBeSent()
    {
        var items = Enumerable.Range(1, 170).Select(n => Item(n, "标题" + n + new string('字', 50))).ToList();
        var catalog = new[] { "前端", "工具" };

        var (batches, tokens) = ClassifyPrompt.Estimate(items, catalog);
        var real = ClassifyPrompt.Batches(items);
        Assert.Equal(real.Count, batches);
        Assert.Equal(
            real.Sum(b => ClassifyPrompt.SystemPrompt(catalog).Length + ClassifyPrompt.UserPrompt(b).Length),
            tokens);

        var (none, zero) = ClassifyPrompt.Estimate(Array.Empty<ClassifyItem>(), catalog);
        Assert.Equal(0, none);
        Assert.Equal(0, zero);
    }

    [Fact]
    public void NothingRequestedMeansNothingScheduled()
        => Assert.Empty(ClassifyPrompt.Batches(Array.Empty<ClassifyItem>()));

    // ────────── O4 行格式（现在的第一个公民） ──────────

    [Fact]
    public void LineReplyMapsNumbersAndSplitsSeparators()
    {
        var parsed = ClassifyPrompt.Parse(
            "1: 前端、工具\n2：笔记\n3: 读书",
            Batch(10, 20, 30));
        Assert.True(parsed.Readable);
        Assert.Equal(3, parsed.Proposals.Count);
        Assert.Equal(new[] { "前端", "工具" }, parsed.Proposals[0].Tags);
        Assert.Equal(30L, parsed.Proposals[2].Id);              // 编号按批次内序号对回数据库 id
        Assert.Empty(parsed.MissingIds);
    }

    [Fact]
    public void LineFormatToleratesBulletsDanglingIdsAndDeliberateSkips()
    {
        var parsed = ClassifyPrompt.Parse(
            "答：\n- 1: 工具\n  4: 越界\n2:",
            Batch(7, 8, 9));
        Assert.True(parsed.Readable);                            // 有可读行就不算"读不出"
        Assert.Single(parsed.Proposals);                         // 只有第 1 行有效
        Assert.Equal(new[] { 4L }, parsed.UnknownIds);           // 越界编号丢弃并回报（与 JSON 分支同口径）
        Assert.Contains(8L, parsed.MissingIds);                  // "2:"空标签＝这条不给标签，合法缺行
    }

    [Fact]
    public void ProseWithNoLinesAndNoJsonIsStillUnreadable()
    {
        var parsed = ClassifyPrompt.Parse("好的，下面是我为您整理的结果，希望有帮助。", Batch(1, 2));
        Assert.False(parsed.Readable);
        Assert.Contains("编号", parsed.Error!.Render());                    // 新话术要提行格式，让用户知道模型没按哪种形状回
    }

    [Fact]
    public void TruncatedJsonStillFallsIntoLineParseWhenLinesAreThere()
    {
        // 半截 JSON 起头 + 其实逐行：行分支兜住，不因 JSON 解析异常整批作废
        var parsed = ClassifyPrompt.Parse(
            "{\"items\": [{\"id\": 1, \"tags\": [\"工具\"  ←被切断\n2: 前端",
            Batch(1, 2));
        Assert.True(parsed.Readable);
        Assert.Equal(new[] { "前端" }, parsed.Proposals.Single(p => p.Id == 2).Tags);
    }

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
        Assert.True(parsed.Readable, parsed.Error?.Render());
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

    /// <summary>记下每次退避等了几秒、不真等——重试节奏是 P-297 的断言对象，测试里等真秒就本末倒置。</summary>
    private static Func<TimeSpan, CancellationToken, Task> RecordingWait(List<int> waited) =>
        (delay, _) => { waited.Add((int)delay.TotalSeconds); return Task.CompletedTask; };

    private static Task<AiReply> TimedOutReply() =>
        Task.FromResult(AiReply.Fail(AiFailureKind.TimedOut, LocMessage.Of("ai.transport.waitedOut")));

    [Fact]
    public async Task OneBrokenBatchNeitherStopsTheRunNorBlankTheGoodOnes()
    {
        var items = Enumerable.Range(1, 95).Select(n => Item(n, "T" + n)).ToList();   // 两批（O7 后 80/15）：80 + 15
        var calls = 0;
        var waited = new List<int>();

        var report = await ClassifyRunner.RunAsync(items, new[] { "前端" },
            _ =>
            {
                calls++;
                return calls <= 3                                   // 第一批三试全超时：线是真的抖，不是抖一下
                    ? TimedOutReply()
                    : Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, CancellationToken.None, wait: RecordingWait(waited));

        Assert.Equal(4, calls);                                     // 三试 + 第二批一次
        Assert.Equal(new[] { 5, 15 }, waited);                      // 固定退避档：先短后长
        Assert.Equal(2, report.Batches.Count);
        Assert.Equal(1, report.FailedBatches);
        Assert.False(report.Batches[0].Ok);
        Assert.Equal(3, report.Batches[0].Attempts);                // 「试了几次」要看得见，否则"超时"和"超时三次"读起来一样
        Assert.Contains("超时", report.Batches[0].Error!.Render());
        Assert.Single(report.Proposals);                            // 第二批整理出来的东西留着
        Assert.Equal(0, report.StoppedBatches);
        Assert.Equal(0, report.SkippedBatches);                     // 一批死透还不够成"线断了"
        Assert.Null(report.AbortReason);
    }

    [Fact]
    public async Task ATransientFailureIsHealedByOneRetry()
    {
        var items = Enumerable.Range(1, 95).Select(n => Item(n, "T" + n)).ToList();   // 两批
        var calls = 0;
        var waited = new List<int>();

        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ =>
            {
                calls++;
                return calls == 1
                    ? TimedOutReply()
                    : Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, CancellationToken.None, wait: RecordingWait(waited));

        Assert.Equal(3, calls);                                     // 抖了一下就救回来：第一批 2 次 + 第二批 1 次
        Assert.Equal(new[] { 5 }, waited);
        Assert.Equal(0, report.FailedBatches);                      // 没有批最终失败——这一下对用户不存在
        Assert.True(report.Batches[0].Ok);
        Assert.Equal(2, report.Batches[0].Attempts);
        Assert.Equal(1, report.Batches[1].Attempts);
        Assert.Equal(2, report.Proposals.Count);
    }

    [Fact]
    public async Task RetryAfterFromTheServerBeatsTheDefaultBackoff()
    {
        var waited = new List<int>();
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(new[] { Item(1, "T") }, Array.Empty<string>(),
            _ =>
            {
                calls++;
                return calls == 1
                    ? Task.FromResult(AiReply.Fail(AiFailureKind.RateLimited, retryAfterSeconds: 7))
                    : Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, CancellationToken.None, wait: RecordingWait(waited));

        Assert.Equal(new[] { 7 }, waited);                          // 服务说隔 7 秒，就别自作主张等 5 秒
        Assert.True(report.Batches[0].Ok);
        Assert.Equal(2, report.Batches[0].Attempts);
    }

    [Fact]
    public async Task ARetryAfterBeyondTheCeilingIsClamped()
    {
        var waited = new List<int>();
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(new[] { Item(1, "T") }, Array.Empty<string>(),
            _ =>
            {
                calls++;
                return calls == 1
                    ? Task.FromResult(AiReply.Fail(AiFailureKind.RateLimited, retryAfterSeconds: 3600))
                    : Reply("""{"items":[{"id":1,"tags":["工具"]}]}""");
            },
            null, CancellationToken.None, wait: RecordingWait(waited));

        Assert.Equal(new[] { 60 }, waited);                         // 「十分钟后再来」照单全收＝界面看着像死了
        Assert.True(report.Batches[0].Ok);
    }

    [Fact]
    public async Task TwoConsecutivelyDeadBatchesTripTheBreaker()
    {
        var items = Enumerable.Range(1, 170).Select(n => Item(n, "T" + n)).ToList();  // 三批（80/80/10）
        var calls = 0;
        var waited = new List<int>();

        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ => { calls++; return TimedOutReply(); },
            null, CancellationToken.None, wait: RecordingWait(waited));

        Assert.Equal(6, calls);                                     // 两批各三试；第三批一次都没问
        Assert.Equal(new[] { 5, 15, 5, 15 }, waited);
        Assert.Equal(2, report.Batches.Count);
        Assert.Equal(2, report.FailedBatches);
        Assert.Equal(1, report.SkippedBatches);                     // 断联时剩下的批不再各烧一遍超时（P-297 的核心判据）
        Assert.Equal(0, report.StoppedBatches);
        Assert.Equal(3, report.BatchesTotal);
        Assert.NotNull(report.AbortReason);
        Assert.Contains("第 3 批", report.AbortReason!.Render());   // 熔断那半句要能对回"哪一批起没问"
        Assert.Contains("超时", report.AbortReason!.Render());
        Assert.False(report.AnythingToApply);
    }

    [Fact]
    public async Task AFatalFailureSkipsTheRetriesAndTheRestOfTheRun()
    {
        var items = Enumerable.Range(1, 170).Select(n => Item(n, "T" + n)).ToList();  // 三批
        var calls = 0;
        var waited = new List<int>();

        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ =>
            {
                calls++;
                return Task.FromResult(AiReply.Fail(AiFailureKind.QuotaExhausted,
                    LocMessage.Of("common.rawValue", "余额不足")));
            },
            null, CancellationToken.None, wait: RecordingWait(waited));

        Assert.Equal(1, calls);                                     // 额度耗尽重试一百次也一样：墙就在那里
        Assert.Empty(waited);                                       // 也没有退避——退避是为"再试可能成"准备的
        Assert.Single(report.Batches);
        Assert.Equal(AiFailureKind.QuotaExhausted, report.Batches[0].Failure);
        Assert.Equal(1, report.Batches[0].Attempts);
        Assert.Equal(2, report.SkippedBatches);
        Assert.Equal(3, report.BatchesTotal);
        Assert.NotNull(report.AbortReason);
        Assert.Contains("第 2 批", report.AbortReason!.Render());
        Assert.Contains("充值", report.AbortReason!.Render());      // 那句话给的是下一步：掏钱，不是再点一次
        Assert.Contains("余额不足", report.AbortReason!.Render());
    }

    /// <summary>退避是等待，不是占着不让停：按停止的那一刻要能立刻带着已整理的东西回来。</summary>
    [Fact]
    public async Task CancellingDuringBackoffKeepsWhatWasOrganised()
    {
        using var cts = new CancellationTokenSource();
        var items = Enumerable.Range(1, 95).Select(n => Item(n, "T" + n)).ToList();   // 两批
        var calls = 0;

        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ => { calls++; return TimedOutReply(); },
            null, cts.Token,
            wait: (_, _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException();
            });

        Assert.Equal(1, calls);
        Assert.Equal(2, report.StoppedBatches);                     // 等待中的那一批 + 还没问的第二批
        Assert.Equal(0, report.FailedBatches);                      // 叫停不等于坏批
        Assert.Empty(report.Proposals);
        Assert.Equal(2, report.BatchesTotal);
        Assert.False(report.AnythingToApply);
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
            _ => throw new InvalidOperationException("通道里抛了"), null, CancellationToken.None,
            wait: (_, _) => Task.CompletedTask);     // 异常型＝Unknown（可重试）：三次都收在这里，不真等退避

        Assert.Equal(1, report.FailedBatches);
        Assert.Equal(3, report.Batches[0].Attempts); // 看不懂的东西也值得再试一次，但只试到上限
        Assert.Contains("通道里抛了", report.Batches[0].Error!.Render());
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

    // ────────── 行级去重（P-296）：同款只问一遍，标签扇出全体 ──────────

    [Fact]
    public void IdenticalLinesAreAskedOnceAndTheirTagsFanOutToEveryTwin()
    {
        var items = new[]
        {
            Item(1, "GitHub 仓库"), Item(2, "GitHub 仓库"), Item(3, "GitHub 仓库"),   // 三胞胎：同一网址收进多个目录的那一族
            Item(4, "本地文件"),                                                       // 独行侠
        };

        var dedup = ClassifyPrompt.DistinctByLine(items);

        Assert.Equal(2, dedup.Representatives.Count);                // 只问两行
        Assert.Equal(1, dedup.Representatives[0].Id);                // 代表者＝组内第一条（首现序）
        Assert.Equal(4, dedup.Representatives[1].Id);
        Assert.Equal(3, dedup.Groups[1].Count);                      // 三胞胎一组
        Assert.Single(dedup.Groups[4]);

        var fanned = ClassifyPrompt.FanOut(new[] { new TagProposal(1, new[] { "开发" }) }, dedup.Groups);
        Assert.Equal(3, fanned.Count);                               // 一行的答案，三条都拿到
        Assert.All(fanned, proposal => Assert.Equal(new[] { "开发" }, proposal.Tags));
        Assert.Equal(new long[] { 1, 2, 3 }, fanned.Select(proposal => proposal.Id));
    }

    [Fact]
    public void DedupFollowsTheSentLineNotTheRawTitle()
    {
        // 两条原始标题不同（末字各是「甲」「乙」），但发给模型的那一行都在 40 字截断后完全一样——
        // 判据钉 ItemLine：模型看得见的载荷一字不差才并组；标题差一个字的两条是两行。
        var shared = new string('前', 40);
        var nearTwins = new[]
        {
            Item(1, shared + "甲"),
            Item(2, shared + "乙"),
            Item(3, "完全不同的另一条"),
        };

        var dedup = ClassifyPrompt.DistinctByLine(nearTwins);

        Assert.Equal(2, dedup.Representatives.Count);                // 「甲」「乙」截断后同一行 ⇒ 并组；第三条独行
        Assert.Equal(2, dedup.Groups[1].Count);
        Assert.Single(dedup.Groups[3]);
    }

    [Fact]
    public void DistinctItemsPassThroughUnchanged()
    {
        // 判据 ③：零重复时行为零差——原序原样进批，扇出是恒等映射，一个字都不多不少
        var items = new[] { Item(1, "书签 A"), Item(2, "书签 B"), Item(3, "文件 C") };

        var dedup = ClassifyPrompt.DistinctByLine(items);

        Assert.Equal(3, dedup.Representatives.Count);
        Assert.Equal(items, dedup.Representatives);                  // 同序同款逐字对平
        Assert.All(dedup.Groups.Values, group => Assert.Single(group));

        var proposals = new[] { new TagProposal(1, new[] { "工具" }), new TagProposal(3, new[] { "本地" }) };
        var fanned = ClassifyPrompt.FanOut(proposals, dedup.Groups);
        Assert.Equal(proposals, fanned);                             // 恒等：一行一条的旧世界原样回来
    }

    [Fact]
    public void BatchCountAndEstimateFollowTheDedupedLines()
    {
        // 判据 ④：96 条里 95 条是同款 ⇒ 去重后 2 行 1 批——不去重就是 2 批，省的不只是行，还有整批的系统段
        var items = Enumerable.Range(1, 95).Select(n => Item(n, "完全同款")).ToList();
        items.Add(Item(96, "独一条"));

        var dedup = ClassifyPrompt.DistinctByLine(items);
        var (batches, tokens) = ClassifyPrompt.Estimate(dedup.Representatives, Array.Empty<string>());

        Assert.Equal(1, batches);
        Assert.Single(ClassifyPrompt.Batches(dedup.Representatives));
        Assert.True(tokens > 0);
    }
}
