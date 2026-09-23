#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// XAML 绑定极性守门（批次 KJ，由用户实测发现的真回归引入）。
/// <para>
/// 起因：KE 把 <c>ItemCard</c> 里十来处"逐条写条件"收敛成判据 <c>ShowsLibraryActions</c> 时，
/// 照抄了旧写法自带的 <c>ConverterParameter=Invert</c>。旧表达式绑的是 <c>IsLauncherMode</c>
/// （"是启动器就藏起来" ⇒ 需要取反），而新属性本身已经是<strong>"该显示"</strong>的语义
/// （＝非启动器 且 非热榜候选）——再套一层 Invert 等于把整族动作的可见性**整体反了**。
/// </para>
/// <para>
/// 后果是真机才看得见的两件事，且构建与既有单测全绿：主窗「文件夹」页的条目右键与按钮
/// 只剩"打开/预览/复制/发送到快捷启动"（置顶·笔记·标签·隐藏全部消失），
/// 而热榜候选行反倒多出这四个会写主库的动作。判据的单元测试抓不到它，因为纯函数是对的、
/// 错的是**消费它的那一层写法**——与 JJ 那条"修好真因后要问：这条规则被改回去时什么会红"同形。
/// </para>
/// </summary>
public sealed class XamlBindingPolarityGateTests
{
    /// <summary>绑了这个"已经带正向语义"的判据就不许再取反；哪天要加同类判据，加到这里。</summary>
    private static readonly string[] PositiveFlags =
    [
        nameof(StarMark.Abstractions.ItemCardPolicy.ShowsLibraryActions),
        nameof(StarMark.Abstractions.ItemCardPolicy.ShowsTrendingActions),
    ];

    [Fact]
    public void PositiveSemanticFlags_MustNotBeInvertedInXaml()
    {
        var hits = 0;
        var violations = new List<string>();
        foreach (var (file, line, text) in EnumerateXamlLines())
        {
            if (!PositiveFlags.Any(f => text.Contains(f, StringComparison.Ordinal))) continue;
            hits++;
            // 只有真的写了 ConverterParameter=Invert 才算违规（注释里提到 Invert 不在扫描范围内，因为只看 XAML）
            if (text.Contains("ConverterParameter=Invert", StringComparison.Ordinal))
                violations.Add($"{file}:{line}  {text.Trim()}");
        }

        // 空扫不算通过：判据被整体删掉时这条守门会静默变绿，所以钉一句"至少还在若干处被消费"。
        Assert.True(hits >= 8,
            $"只扫到 {hits} 处判据消费点（预期 ≥8）：要么绑定被整批删了，要么路径漂了，守门本身已失效");
        Assert.Empty(violations);
    }

    private IEnumerable<(string File, int Line, string Text)> EnumerateXamlLines()
    {
        var root = FindRepoRoot() ?? throw new InvalidOperationException("未找到仓库根目录（src/StarMark.UI）");
        var uiDir = Path.Combine(root, "src", "StarMark.UI");
        foreach (var file in Directory.EnumerateFiles(uiDir, "*.xaml", SearchOption.AllDirectories))
        {
            // obj\ 下是构建期复制的副本：读它们会把"源文件已改对但产物还旧"当成违规（反之亦然）
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            var rel = Path.GetRelativePath(uiDir, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++) yield return (rel, i + 1, lines[i]);
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "StarMark.UI"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
