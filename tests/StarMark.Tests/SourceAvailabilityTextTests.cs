#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.Bookmarks;
using StarMark.Integrations.Ditto;

namespace StarMark.Tests;

/// <summary>
/// 批次 KO：诊断面板"某个源现在怎么样"这句话的契约。
/// <para>
/// 起因是用户实测反馈："Chrome 和 Ditto 剪贴板显示不可用，我用的是 Edge（同一内核），不确定有没有影响"。
/// 事实是三条各自独立的：<b>Edge 读自己的用户数据目录、工作正常</b>；Chrome 与 Ditto 本机确实没有数据文件。
/// 但界面上三行都只写"不可用"——把一个"没装"、一个"没装"和一个"没配置"压成同一句话，
/// 用户唯一能做的推断就是"是不是坏了"（文案不许比已知事实更含糊 ⇒ P-54 的同族）。
/// </para>
/// </summary>
public sealed class SourceAvailabilityTextTests
{
    /// <summary>没有取到数据时的兜底：不猜原因，只说明"本机没有它的数据/依赖"。</summary>
    private sealed class BareSource : IItemSource
    {
        public string SourceId => "bare";
        public string DisplayName => "裸源";
        public bool IsAvailable => false;
        public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(System.Array.Empty<Item>());
        public Task<IReadOnlyList<Item>> SearchAsync(string q, SearchFilter f, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(System.Array.Empty<Item>());
    }

    [Fact]
    public void Available_IsTheFixedWord()
        => Assert.Equal("可用", SourceAvailabilityText.Of(true, "任何成因都不该出现"));

    [Fact]
    public void Unavailable_WithoutAReason_StillSaysSomethingActionable()
    {
        var text = SourceAvailabilityText.Of(false, null);
        Assert.StartsWith("没有这项数据：", text, System.StringComparison.Ordinal);
        Assert.Contains(SourceAvailabilityText.UnknownReason, text, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 接口默认实现要保证既有源与测试桩不改动也能编译，并落到兜底话术。
    /// （默认成员只能经接口访问 ⇒ 这一句同时钉住"DiagnosticsService 必须按 IItemSource 取"。）
    /// </summary>
    [Fact]
    public void Interface_DefaultHint_IsNullSoOldImplementersKeepWorking()
    {
        IItemSource src = new BareSource();
        Assert.Null(src.AvailabilityHint);
        Assert.Equal(SourceAvailabilityText.Of(false, null),
                     SourceAvailabilityText.Of(src.IsAvailable, src.AvailabilityHint));
    }

    [Fact]
    public void Hint_IsCarriedThrough_AndBounded()
    {
        Assert.Equal("没有这项数据：没装 Ditto", SourceAvailabilityText.Of(false, "  没装 Ditto  "));

        var long_ = new string('x', 300);
        var text = SourceAvailabilityText.Of(false, long_);
        Assert.True(text.Length < long_.Length, "超长成因必须被截断：诊断是一行一行读的");
        Assert.EndsWith("…", text, System.StringComparison.Ordinal);
    }

    // ===== 真实源的成因必须各自带自己的路径 / 配置项 =====

    [Fact]
    public void BookmarkSources_HintNamesTheFileTheyLookedFor()
    {
        var edge = new EdgeBookmarksSource(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smoke-no-edge", "Bookmarks"));
        Assert.False(edge.IsAvailable);

        var hint = ((IItemSource)edge).AvailabilityHint;
        Assert.NotNull(hint);
        Assert.Contains("smoke-no-edge", hint!, System.StringComparison.OrdinalIgnoreCase);
        // 这一句是本轮的直接动因：Chrome/Edge 各读各的目录，一条没有不代表另一条坏了
        Assert.Contains("不受影响", hint!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DittoSource_HintSaysItsOwnDatabase_AndSeparatesItFromBuiltInHistory()
    {
        var ditto = new DittoSource(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smoke-no-ditto", "DittoDB.db"));
        Assert.False(ditto.IsAvailable);

        var hint = ((IItemSource)ditto).AvailabilityHint;
        Assert.NotNull(hint);
        Assert.Contains("smoke-no-ditto", hint!, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("剪贴板历史", hint!, System.StringComparison.Ordinal);   // 内置历史不依赖 Ditto
    }
}
