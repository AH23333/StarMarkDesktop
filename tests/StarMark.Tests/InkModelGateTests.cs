#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 笔迹模型层的守门（整合方案 §3.4 / §7：两侧只剩一个笔迹载体，落笔序号只剩一份时钟）。
/// <para>
/// 这一层出的错从来不是"算不出来"，而是<b>同一件事有两处各写一份</b>：两条各自数的序号没有大小关系，
/// "撤最后落的那一笔"就退化成"每叠各退一条"；两个历史类型并存，画布的笔迹就永远归不进全局栈。
/// 所以这里钉的是"只有一处"，不是"这一处算得对"（算得对由 <see cref="InkDocTests"/> 与
/// <see cref="CanvasUndoOrderTests"/> 钉）。
/// </para>
/// </summary>
public sealed class InkModelGateTests
{
    private const string InkOrderFile = "src/StarMark.Core/Capture/InkOrder.cs";
    private const string AnnotationFile = "src/StarMark.Core/Capture/Annotation.cs";
    private const string CanvasInkFile = "src/StarMark.Core/Canvas/CanvasInk.cs";
    private const string InkDocFile = "src/StarMark.Core/Capture/InkDoc.cs";
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";

    /// <summary>标注链的源码目录：序号这件事只许在这两格里有一份出处。</summary>
    private static readonly string[] ChainDirs =
    {
        "src/StarMark.Core/Capture",
        "src/StarMark.Core/Canvas",
    };

    [Fact]
    public void LandingOrderIsDrawnFromOneClockOnly()
    {
        var clock = SourceGate.ReadRepoFile(InkOrderFile);
        Assert.Equal(1, SourceGate.Count(clock, "Interlocked.Increment"));

        var root = SourceGate.RepoRoot();
        var strays = ChainDirs
            .SelectMany(dir => Directory.GetFiles(Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar)), "*.cs"))
            .Where(f => !f.EndsWith("InkOrder.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Interlocked.Increment", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();
        Assert.True(strays.Count == 0,
            $"落笔序号自造时钟的文件（只许 InkOrder 一处）：{string.Join("、", strays)}");

        // 两边都从那一处领号——"领了号"本身也要钉，否则哪天有人把 Order 写成常量 0 也没人发现
        Assert.Contains("InkOrder.Next()", SourceGate.ReadRepoFile(AnnotationFile));
        Assert.Contains("InkOrder.Next()", SourceGate.ReadRepoFile(CanvasInkFile));
    }

    [Fact]
    public void TheEmptyStackFallbackIsThatOneNumber()
    {
        // "这一叠没画过"必须用同一个兜底值。两处各写一个（一边 long.MinValue、一边 -1）看着都对，
        // 而"谁最新"的比较一旦跨叠，就会挑中一块空白屏去撤销（批次 WO 那一族的成因）。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(InkDocFile), "public long LastOrder");
        Assert.Contains("InkOrder.None", body);

        var root = SourceGate.RepoRoot();
        var strays = ChainDirs
            .SelectMany(dir => Directory.GetFiles(Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar)), "*.cs"))
            .Where(f => !f.EndsWith("InkOrder.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("long.MinValue", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();
        Assert.True(strays.Count == 0,
            $"自己写\"空叠\"兜底值的文件（只许问 InkOrder.None）：{string.Join("、", strays)}");
    }

    [Fact]
    public void TheBoardStoresItsMarksInTheSameDocAsTheSheet()
    {
        // 方案 §3.4：两侧只剩一个笔迹载体。画布那一叠必须真的换成 InkDoc（带归属），
        // 而不是"看着像同一套、其实各存一份"——那正是 R2 双引擎的原样。
        var screen = SourceGate.Between(SourceGate.ReadRepoFile(Service), "private sealed class Screen", "private static readonly List<Screen>");
        Assert.Contains("public required InkDoc Ink { get; init; }", screen);
        Assert.Contains("public CanvasStroke? Drawing { get; set; }", screen);
        Assert.Contains("new InkDoc(new InkSurface(SurfaceRole.Board, Screens.Count))",
            SourceGate.ReadRepoFile(Service));
        Assert.DoesNotContain("CanvasInk", SourceGate.ReadRepoFile(Service));
    }

    [Fact]
    public void TheOldHistoryTypeIsGoneNotAliased()
    {
        // 改名要改彻底：留一个 using 别名或旧类转发，就等于承认"两个名字都可以"，
        // 下一次加笔迹容器时两个都还有人用，S2 的"唯一载体"当场作废。
        var root = SourceGate.RepoRoot();
        var hits = Directory
            .GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("AnnotationHistory", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();
        Assert.True(hits.Count == 0, $"还在写旧类型名的文件：{string.Join("、", hits)}");

        Assert.Contains("public sealed class InkDoc", SourceGate.ReadRepoFile(InkDocFile));
    }

    [Fact]
    public void MarksAreImmutableSoOrderIsNeverReassigned()
    {
        // 序号是"这一笔是第几笔"，落笔那一刻定死。写成可读可写就会被"移动/缩放"顺手改号，
        // 于是撤销栈里的顺序与用户画的时间顺序脱钩（撤的不是最后那一笔）。
        var annotation = SourceGate.ReadRepoFile(AnnotationFile);
        Assert.Contains("public long Order { get; init; } = InkOrder.Next();", annotation);
        Assert.DoesNotContain("public long Order { get; set;", annotation);
        Assert.DoesNotContain("Order =", annotation);
    }
}
