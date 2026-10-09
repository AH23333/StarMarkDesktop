#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Integrations.Bookmarks;
using StarMark.Integrations.Ditto;

namespace StarMark.Tests;

/// <summary>
/// 诊断面板"某个源现在怎么样"这句话的契约。
/// <para>
/// 起因是用户实测反馈："Chrome 和 Ditto 剪贴板显示不可用，我用的是 Edge（同一内核），不确定有没有影响"。
/// 事实是三条各自独立的：<b>Edge 读自己的用户数据目录、工作正常</b>；Chrome 与 Ditto 本机确实没有数据文件。
/// 但界面上三行都只写"不可用"——把一个"没装"、一个"没装"和一个"没配置"压成同一句话，
/// 用户唯一能做的推断就是"是不是坏了"（文案不许比已知事实更含糊 ⇒ P-54 的同族）。
/// </para>
/// <para><b>批次 WI-57（P-187）之后这里钉的是键，⛔ 中文措辞</b>（规则 #130）：那几句话全进了语言表，
/// 逐字钉中文的断言只会随另两列的改措辞红出一句假话。这一族能机检的两件事是
/// <b>哪颗键</b>与<b>实参里到底带没带那条路径</b>。</para>
/// </summary>
public sealed class SourceAvailabilityTextTests
{
    /// <summary>没有取到数据时的兜底：不猜原因，只说明"本机没有它的数据/依赖"。</summary>
    private sealed class BareSource : IItemSource
    {
        public string SourceId => "bare";
        public bool IsAvailable => false;
        public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
        public Task<IReadOnlyList<Item>> SearchAsync(string q, SearchFilter f, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
    }

    [Fact]
    public void Available_IsOneKey_AndTheReasonIsNeverAppended()
    {
        var text = SourceAvailabilityText.Of(true, LocMessage.Of("source.hint.ditto", "任何成因都不该出现"));
        Assert.Equal("source.avail.available", text.Key);
        Assert.Null(text.Args);   // "可用"那颗没有槽：给了成因也不许拼进去
    }

    [Fact]
    public void Unavailable_WithoutAReason_StillSaysSomethingActionable()
    {
        var text = SourceAvailabilityText.Of(false, null);
        Assert.Equal("source.avail.missing", text.Key);
        // 兜底那颗是<b>嵌进去的另一颗键</b>，⛔ 在这里拼成成品字：外层换语言时它必须跟着重贴（#361）。
        Assert.Equal("source.avail.unknownReason", Assert.IsType<LocMessage>(text.Args![0]).Key);
    }

    /// <summary>
    /// 接口默认实现要保证既有源与测试桩不改动也能编译，并落到兜底话术。
    /// （默认成员只能经接口访问 ⇒ 这一句同时钉住"DiagnosticsService 必须按 IItemSource 取"。）
    /// <para><b>逐字段比而不是整颗比</b>：<c>LocMessage</c> 是记录，但它那个 <c>object?[]</c> 实参数组
    /// 按引用相等——两颗各自 new 出来的同话记录并不相等，整颗比会红在仪器自己身上。</para>
    /// </summary>
    [Fact]
    public void Interface_DefaultHint_IsNullSoOldImplementersKeepWorking()
    {
        IItemSource src = new BareSource();
        Assert.Null(src.AvailabilityHint);

        var fromStub = SourceAvailabilityText.Of(src.IsAvailable, src.AvailabilityHint);
        var expected = SourceAvailabilityText.Of(false, null);
        Assert.Equal(expected.Key, fromStub.Key);
        Assert.Equal(((LocMessage)expected.Args![0]!).Key, ((LocMessage)fromStub.Args![0]!).Key);
    }

    [Fact]
    public void TheReason_IsNested_NotGluedIntoASentence()
    {
        var why = LocMessage.Of("source.hint.githubNoToken");
        var text = SourceAvailabilityText.Of(false, why);
        Assert.Equal("source.avail.missing", text.Key);
        Assert.Same(why, text.Args![0]);   // 原样嵌着：取词发生在贴上去的那一刻
    }

    // ===== 真实源的成因必须各自带自己的路径 / 配置项 =====

    [Fact]
    public void BookmarkSources_HintNamesTheFileTheyLookedFor()
    {
        var edge = new EdgeBookmarksSource(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smoke-no-edge", "Bookmarks"));
        Assert.False(edge.IsAvailable);

        var hint = ((IItemSource)edge).AvailabilityHint;
        Assert.NotNull(hint);
        // 这一句是本轮的直接动因：Chrome/Edge 各读各的目录，一条没有不代表另一条坏了——
        // 那句"不受影响"现在住在表里，这里能证的是"找过哪条路径"真的进了实参（机器事实，与语言无关）。
        Assert.Equal("source.hint.bookmarks", hint!.Key);
        Assert.Contains("smoke-no-edge", hint.Render(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DittoSource_HintSaysItsOwnDatabase_AndSeparatesItFromBuiltInHistory()
    {
        var ditto = new DittoSource(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smoke-no-ditto", "DittoDB.db"));
        Assert.False(ditto.IsAvailable);

        var hint = ((IItemSource)ditto).AvailabilityHint;
        Assert.NotNull(hint);
        Assert.Equal("source.hint.ditto", hint!.Key);
        Assert.Contains("smoke-no-ditto", hint.Render(), StringComparison.OrdinalIgnoreCase);
    }
}
