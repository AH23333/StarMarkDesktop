#nullable enable
using System;
using StarMark.Abstractions.Capture;
using StarMark.Core.Health;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 护眼节拍的判据（规格 §2.7 / 实现详解 §9）。这一批钉死四件事：
/// ① 到点才弹、弹过一次必须重新起一轮（否则每 tick 轰炸）；
/// ② 前台全屏时让路，且让路是按 60 s 重试而不是每 15 s 探一次；
/// ③ 让路让久了／机器休眠回来＝这一轮人已经离开过屏幕，安静重置而不是补一发；
/// ④ 全屏判据"贴住四条边"——差一像素仍算全屏，但露着任务栏的最大化不算。
/// 时间全部注入，不起表也不碰窗口。
/// </summary>
public sealed class EyeRestPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 9, 0, 0, TimeSpan.FromHours(8));

    private static TimeSpan Min(int m) => TimeSpan.FromMinutes(m);

    // ────────── 节拍 ──────────

    [Fact]
    public void NotDue_BeforeTheInterval_Elaps()
        => Assert.Equal(EyeRestPolicy.Decision.Waiting,
            new EyeRestPolicy(T0).Decide(T0 + Min(44), 45, false, false));

    [Fact]
    public void ExactlyAtTheInterval_IsDue()
        => Assert.Equal(EyeRestPolicy.Decision.Due,
            new EyeRestPolicy(T0).Decide(T0 + Min(45), 45, false, false));

    [Fact]
    public void AfterReminding_TheCycleStartsAnew()
    {
        var policy = new EyeRestPolicy(T0);
        var now = T0 + Min(45);
        Assert.Equal(EyeRestPolicy.Decision.Due, policy.Decide(now, 45, false, false));

        policy.Reset(now);                       // 遮罩淡出／气泡已发
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(now + Min(1), 45, false, false));
        Assert.Equal(now + Min(45), policy.DueAt(45));
    }

    [Fact]
    public void ShorterInterval_IsHonored()
    {
        var policy = new EyeRestPolicy(T0);
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(T0 + Min(14), 15, false, false));
        // 第 16 分：按 15 分钟已到点（在宽限期内 ⇒ 该弹），按 45 分钟还早得多
        Assert.Equal(EyeRestPolicy.Decision.Due, policy.Decide(T0 + Min(16), 15, false, false));
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(T0 + Min(16), 45, false, false));
    }

    [Fact]
    public void ShrinkingTheIntervalMidCycle_DoesNotFireInstantly()
    {
        var policy = new EyeRestPolicy(T0);
        // 45 分改 15 分，此刻是第 20 分：新节拍的到点时刻（第 15 分）已经过去 5 分钟 ⇒
        // 走宽限期那条路安静重置，而不是"刚改完设置遮罩就砸下来"。改完立刻挨一发是打扰。
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(T0 + Min(20), 15, false, false));
        Assert.Equal(T0 + Min(20) + Min(15), policy.DueAt(15));
    }

    // ────────── 全屏让路 ──────────

    [Fact]
    public void ForegroundFullscreen_DeferredAndProbedAgainAfterTheGap()
    {
        var policy = new EyeRestPolicy(T0);
        var due = T0 + Min(45);

        Assert.Equal(EyeRestPolicy.Decision.Deferred, policy.Decide(due, 45, deferOnFullscreen: true, foregroundFullscreen: true));
        // 让路窗口内不再重复探前台（30 s 后仍在等待，且不返回 Deferred）
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(due.AddSeconds(30), 45, true, true));
        // 60 s 到点再探：全屏已结束 ⇒ 该弹了
        Assert.Equal(EyeRestPolicy.Decision.Due, policy.Decide(due.AddSeconds(60), 45, true, false));
    }

    [Fact]
    public void DeferredStillExpires_ThroughTheGraceWindow()
    {
        var policy = new EyeRestPolicy(T0);
        var due = T0 + Min(45);
        Assert.Equal(EyeRestPolicy.Decision.Deferred, policy.Decide(due, 45, true, true));
        Assert.Equal(EyeRestPolicy.Decision.Deferred, policy.Decide(due.AddSeconds(60), 45, true, true));

        // 第三次探测已在宽限期（2 分钟）之外：放映还在继续 ⇒ 这一轮取消，安静地重新计时
        var expired = due.AddSeconds(121);
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(expired, 45, true, true));
        Assert.Equal(expired + Min(45), policy.DueAt(45));
    }

    [Fact]
    public void TurnedOffDefer_RemindsRightIntoTheFullscreen()
        => Assert.Equal(EyeRestPolicy.Decision.Due,
            new EyeRestPolicy(T0).Decide(T0 + Min(45), 45, deferOnFullscreen: false, foregroundFullscreen: true));

    [Fact]
    public void AwayTooLong_IsNotRepaidWithAReminder()
    {
        var policy = new EyeRestPolicy(T0);
        var back = T0 + Min(200);                       // 休眠/离场两小时多
        Assert.Equal(EyeRestPolicy.Decision.Waiting, policy.Decide(back, 45, true, false));
        Assert.Equal(back + Min(45), policy.DueAt(45));  // 起点已挪到回来那一刻，不补弹
    }

    // ────────── 区间取值 ──────────

    [Theory]
    [InlineData(45, 45)]
    [InlineData(5, 5)]
    [InlineData(180, 180)]
    [InlineData(0, 45)]         [InlineData(-3, 45)]     [InlineData(9999, 45)]
    [InlineData(4, 45)]         [InlineData(181, 45)]
    public void IllegalInterval_FallsBackToDefault_NotToTheEdge(int raw, int want)
        => Assert.Equal(want, EyeRestPolicy.ClampInterval(raw));

    [Fact]
    public void DueAt_UsesTheFallbackWhenTheStoredIntervalIsBroken()
    {
        var policy = new EyeRestPolicy(T0);
        Assert.Equal(T0 + Min(45), policy.DueAt(0));    // 存档里 0 ⇒ 不是"每分钟弹"，是 45 分钟
    }

    // ────────── 界面档位（下拉的唯一真源也在 Core）──────────

    /// <summary>
    /// 默认值必须在档位列表里：不在的话设置页回灌时下拉<b>选不中当前值</b>，
    /// 显示成空白而实际用着 45——用户看到的就是错的。
    /// </summary>
    [Fact]
    public void DefaultInterval_IsOneOfTheOptions()
        => Assert.Contains(EyeRestPolicy.DefaultIntervalMinutes, EyeRestPolicy.IntervalOptions);

    /// <summary>
    /// 每一档都得是 <c>ClampInterval</c> 认的合法值：某档被回落成默认的话，
    /// 用户选了"90 分钟"实际攒的是 45 分钟，而且回灌时下拉还会跳到别的档位。
    /// </summary>
    [Fact]
    public void EveryOption_SurvivesClampUnchanged()
    {
        foreach (var minutes in EyeRestPolicy.IntervalOptions)
            Assert.Equal(minutes, EyeRestPolicy.ClampInterval(minutes));
    }

    [Fact]
    public void Labels_AreOnePerOption_AndCarryTheirOwnNumber()
    {
        Assert.Equal(EyeRestPolicy.IntervalOptions.Length, EyeRestPolicy.IntervalLabels.Count);
        for (var i = 0; i < EyeRestPolicy.IntervalOptions.Length; i++)
            Assert.Equal($"{EyeRestPolicy.IntervalOptions[i]} 分钟", EyeRestPolicy.IntervalLabels[i]);
    }

    [Fact]
    public void IndexOf_NearestOptionWins_AndBrokenValuesLandOnTheDefault()
    {
        for (var i = 0; i < EyeRestPolicy.IntervalOptions.Length; i++)
            Assert.Equal(i, EyeRestPolicy.IntervalIndexOf(EyeRestPolicy.IntervalOptions[i]));   // 逐档往返

        Assert.Equal(1, EyeRestPolicy.IntervalIndexOf(22));      // 22 → 20（不是 30）
        Assert.Equal(2, EyeRestPolicy.IntervalIndexOf(27));      // 27 → 30
        Assert.Equal(3, EyeRestPolicy.IntervalIndexOf(0));       // 非法值先回落 45，再定位到 45 那档
        Assert.Equal(3, EyeRestPolicy.IntervalIndexOf(-999));
        Assert.Equal(3, EyeRestPolicy.IntervalIndexOf(9999));
    }

    [Fact]
    public void IntervalAt_ClampsOutOfBoundsIndexes_InsteadOfThrowing()
    {
        Assert.Equal(EyeRestPolicy.IntervalOptions[0], EyeRestPolicy.IntervalAt(-7));
        Assert.Equal(EyeRestPolicy.IntervalOptions[^1], EyeRestPolicy.IntervalAt(999));
    }

    // ────────── 提醒形式那三档（批次 RS：暗幕与"能不能提前退出"解耦）──────────

    /// <summary>
    /// 数值钉死：这一档是按整数落进 <c>settings.json</c> 的，中间插一档或换顺序会让用户已有的设置
    /// <b>静默变成另一档</b>（同批次 EV 给 <c>ItemType</c>／<c>WidgetChromeMode</c> 立下的口径）。
    /// </summary>
    [Theory]
    [InlineData(EyeRestNotice.Bubble, 0)]
    [InlineData(EyeRestNotice.Curtain, 1)]
    [InlineData(EyeRestNotice.Forced, 2)]
    public void NoticeValues_ArePinned(EyeRestNotice notice, int raw)
        => Assert.Equal(raw, (int)notice);

    [Fact]
    public void EveryNotice_IsKnownAndSurvivesClampUnchanged()
    {
        for (var i = 0; i < EyeRestPolicy.NoticeOptions.Length; i++)
        {
            var notice = EyeRestPolicy.NoticeOptions[i];
            Assert.True(EyeRestPolicy.IsKnownNotice((int)notice));
            Assert.Equal(notice, EyeRestPolicy.ClampNotice((int)notice));     // 某档被回落成默认的话，
            Assert.Equal(notice, EyeRestPolicy.ClampNotice(notice));          // 用户选"强制"实际跑的是别的
            Assert.Equal(i, EyeRestPolicy.NoticeIndexOf(notice));             // 下拉的选中项跟着档位走
        }
        Assert.Equal(EyeRestPolicy.NoticeOptions.Length, EyeRestPolicy.NoticeLabels.Count);
    }

    /// <summary>
    /// 认不得的数值<b>回默认档，不夹到最近的一端</b>：3 紧挨着"强制"、-1 紧挨着"气泡"，
    /// 夹过去就等于让一处存档损坏替用户挑一档——而挑到的那档可能是"把所有退出出口关掉"。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(9999)]
    public void BrokenNotice_LandsOnTheDefault_NotOnTheNearestEdge(int? raw)
        => Assert.Equal(EyeRestPolicy.DefaultNotice, EyeRestPolicy.ClampNotice(raw));

    [Fact]
    public void AnIllegalEnumValue_LandsOnTheDefaultToo()
        => Assert.Equal(EyeRestPolicy.DefaultNotice, EyeRestPolicy.ClampNotice((EyeRestNotice)7));

    [Fact]
    public void NoticeAt_ClampsOutOfBoundsIndexes_InsteadOfThrowing()
    {
        Assert.Equal(EyeRestPolicy.NoticeOptions[0], EyeRestPolicy.NoticeAt(-7));
        Assert.Equal(EyeRestPolicy.NoticeOptions[^1], EyeRestPolicy.NoticeAt(999));
    }

    /// <summary>
    /// 这次改判的核心：三档各自回答<b>两个问题</b>，而且"盖暗幕"与"扣住用户"必须能分开成立。
    /// 旧形状把两者绑成一颗布尔，于是 <c>(暗幕，可提前结束)</c> 这一格是空的——用户原话
    /// "要么无法使用暗幕，要么使用暗幕时无法提前跳过"。这一枚测钉的就是那一格必须存在。
    /// </summary>
    [Theory]
    [InlineData(EyeRestNotice.Bubble, false, true)]    // 什么都不盖
    [InlineData(EyeRestNotice.Curtain, true, true)]    // 新增的那一格：盖幕布，但点得开
    [InlineData(EyeRestNotice.Forced, true, false)]    // 盖幕布且扣住（原规格那条"防形同虚设"仍在）
    public void TheCurtainAndTheExitAreTwoSeparateQuestions(EyeRestNotice notice, bool usesCurtain, bool skippable)
    {
        Assert.Equal(usesCurtain, EyeRestPolicy.UsesCurtain(notice));
        Assert.Equal(skippable, EyeRestPolicy.IsSkippable(notice));
    }

    /// <summary>
    /// 默认档必须是中间那一档：默认成"强制"＝一打开护眼就吃一次锁屏（惊吓），
    /// 默认成"气泡"＝这次改判要治的原始形状又回来了（想看看暗幕必须先接受不能退出）。
    /// </summary>
    [Fact]
    public void DefaultNotice_UsesTheCurtain_AndIsStillSkippable()
    {
        Assert.True(EyeRestPolicy.UsesCurtain(EyeRestPolicy.DefaultNotice));
        Assert.True(EyeRestPolicy.IsSkippable(EyeRestPolicy.DefaultNotice));
        Assert.Equal(EyeRestNotice.Curtain, EyeRestPolicy.DefaultNotice);
    }

    /// <summary>
    /// 幕布上那句退出说明<b>不许承诺按键</b>：这扇窗不抢焦点，而按键只投递给有焦点的窗，
    /// 所以写"按 Esc 可提前结束"是把一条做不到的出口印在屏幕上。给的出口是鼠标（点击不依赖焦点）。
    /// </summary>
    [Theory]
    [InlineData(EyeRestNotice.Bubble)]
    [InlineData(EyeRestNotice.Curtain)]
    [InlineData(EyeRestNotice.Forced)]
    public void NoticeHint_NeverPromisesAKeystroke_TheCurtainCannotReceive(EyeRestNotice notice)
    {
        var hint = EyeRestPolicy.NoticeHint(notice);
        Assert.DoesNotContain("Esc", hint, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("按键", hint);
        if (EyeRestPolicy.IsSkippable(notice)) Assert.Contains("点一下", hint);
        else Assert.Contains("不能提前结束", hint);
    }

    /// <summary>状态行与下拉共用一份措辞；认不得的值也要说得出默认档那一句（不能空着）。</summary>
    [Fact]
    public void NoticeLabel_FallsBackToTheDefaultWordings_AsWell()
    {
        Assert.Equal(EyeRestPolicy.NoticeLabels[EyeRestPolicy.NoticeIndexOf(EyeRestPolicy.DefaultNotice)],
            EyeRestPolicy.NoticeLabel(EyeRestPolicy.DefaultNotice));
        Assert.Equal(EyeRestPolicy.NoticeLabel(EyeRestPolicy.DefaultNotice),
            EyeRestPolicy.NoticeLabel((EyeRestNotice)7));
    }

    /// <summary>
    /// 秒数只有一个出处：幕布上写的"20 秒走完"必须跟着 <see cref="EyeRestPolicy.RestSeconds"/> 动——
    /// 倒数到 30 而说明写着 20 是用户第一眼看得见的自相矛盾（同批次 WR 那条"档位要落到最终产物"）。
    /// </summary>
    [Fact]
    public void TheSecondsInTheWordingComeFromRestSeconds()
    {
        Assert.Contains(EyeRestPolicy.RestSeconds.ToString(),
            EyeRestPolicy.NoticeHint(EyeRestNotice.Forced));
        Assert.Contains(EyeRestPolicy.RestSeconds.ToString(),
            EyeRestPolicy.NoticeLabel(EyeRestNotice.Forced));
    }

    // ────────── 全屏几何判据 ──────────

    private static readonly IntRect Screen = new(0, 0, 1920, 1080);
    private static readonly IntRect SecondMonitor = new(-1920, 0, 1920, 1080);   // 主屏左边的副屏：原点是负的

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]          // 正好铺满
    [InlineData(-1, -1, 1922, 1082, true)]        // 越过屏幕边缘（有些全屏实现会多画一圈）
    [InlineData(0, 0, 1919, 1079, true)]          // 差一像素：DWM 边界与分数缩放取整，仍按全屏算
    [InlineData(0, 0, 1920, 1040, false)]         // 最大化窗口：底下露着 40 高的任务栏 ⇒ 不算全屏
    [InlineData(10, 10, 1920, 1080, false)]       // 右下角越界但左上角缩进去 ⇒ 不算
    [InlineData(0, 0, 100, 100, false)]
    public void CoversScreen(int x, int y, int w, int h, bool want)
        => Assert.Equal(want, EyeRestPolicy.CoversScreen(new IntRect(x, y, w, h), Screen));

    [Fact]
    public void CoversScreen_ComparesAgainstTheMonitorsOwnOrigin_NotZero()
    {
        // 负原点副屏上：贴满自己那块屏的窗＝全屏；拿主屏(0,0)的尺子量会一路判成"不覆盖"
        Assert.True(EyeRestPolicy.CoversScreen(new IntRect(-1920, 0, 1920, 1080), SecondMonitor));
        Assert.False(EyeRestPolicy.CoversScreen(new IntRect(-1920, 0, 1920, 1080), Screen));
    }
}
