using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 主题规则守门（改造提示词 Phase 1-4）：禁止在 UI 代码里用
/// Application.Current.Resources["…Brush"] 解析画笔——应用级主题在窗口创建后冻结，
/// 运行期切主题会拿到错误主题的画笔（白字白底根因）。必须走 ThemeBrush.For/Resolve。
/// 允许例外：Style 查找（键名不含 Brush，不受主题影响，须有注释）、
/// ThemeBrush.cs 自身的兜底实现、注释行。
/// </summary>
public partial class ThemeRulesGateTests
{
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

    private static IEnumerable<(string File, int Line, string Text, string Prev)> EnumerateUiSources()
    {
        var root = FindRepoRoot() ?? throw new InvalidOperationException("未找到仓库根目录（src/StarMark.UI）");
        var uiDir = Path.Combine(root, "src", "StarMark.UI");
        foreach (var file in Directory.EnumerateFiles(uiDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(uiDir, file).Replace('\\', '/');
            // ThemeBrush.cs 是唯一的合法解析实现（内部兜底），豁免
            if (rel == "Helpers/ThemeBrush.cs") continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var text = lines[i].TrimStart();
                if (text.StartsWith("//") || text.StartsWith("*") || text.StartsWith("///")) continue; // 注释行豁免
                if (lines[i].Contains("Application.Current.Resources"))
                    yield return (rel, i + 1, lines[i], i > 0 ? lines[i - 1].TrimStart() : string.Empty);
            }
        }
    }

    private static bool IsStyleAnnotated(string text, string prev) =>
        text.Contains("仅 Style") || text.Contains("非画笔") || text.Contains("Style 查找")
        || prev.Contains("仅 Style") || prev.Contains("非画笔") || prev.Contains("Style 查找");

    [Fact]
    public void UiSources_MustNotResolveThemeBrushesFromApplicationResources()
    {
        var violations = new List<string>();
        foreach (var (file, line, text, prev) in EnumerateUiSources())
        {
            var t = text.Trim();
            foreach (Match m in ResourceKeyRegex().Matches(t))
            {
                var key = m.Groups["key"].Value;
                if (key.Contains("Brush", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{file}:{line} 画笔解析必须走 ThemeBrush.For/Resolve: {t}");
                }
                else if (!IsStyleAnnotated(t, prev))
                {
                    violations.Add($"{file}:{line} 非画笔资源引用需同行/上一行注释说明（仅 Style，非画笔）: {t}");
                }
            }
        }
        Assert.True(violations.Count == 0, "发现主题画笔违规引用：\n" + string.Join("\n", violations));
    }

    [GeneratedRegex(@"Resources\[\s*""(?<key>[^""]+)""\s*\]")]
    private static partial Regex ResourceKeyRegex();
}
