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

    /// <summary>
    /// 审查报告 F3 的机检版：<b>不许把 <c>ElementTheme.Default</c> 当字面量传给画笔解析</b>。
    /// <para>
    /// <c>ThemeBrush.For(Default, …)</c> 的深浅判定会退化成 <c>ThemeManager.IsSystemDark()</c>（OS 实时主题），
    /// 于是「OS 深色 + 应用强制浅色」时解析出深色桶的近白画笔 → 浅底白字。设置页代码构建的
    /// 「桌面组件类型名 / 快捷键分组名」两处长年就是这个形状（组件名在浅色下看不见）。
    /// </para>
    /// <para>
    /// 合法写法是传<b>元素自己的</b> <c>ActualTheme</c>，或像弹窗那样传"偏好折算后的" <c>EffectiveTheme()</c>
    /// （那里 Default 只作为跟随系统的显式入口，且它自己会解析）——所以本条只禁字面量。
    /// </para>
    /// </summary>
    [Fact]
    public void UiSources_MustNotPassLiteralDefaultThemeToBrushResolution()
    {
        var root = FindRepoRoot() ?? throw new InvalidOperationException("未找到仓库根目录（src/StarMark.UI）");
        var uiDir = Path.Combine(root, "src", "StarMark.UI");
        var violations = new List<string>();
        var callSites = 0;   // 正向对照：本扫描确实看到了画笔解析点，否则"零违规"只是空转

        foreach (var file in Directory.EnumerateFiles(uiDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(uiDir, file).Replace('\\', '/');
            if (rel.Contains("Helpers/ThemeBrush.cs")) continue;   // 解析器自己必须处理 Default
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
                if (!t.Contains("ThemeBrush.For(")) continue;
                callSites++;
                var rest = t[(t.IndexOf("ThemeBrush.For(", StringComparison.Ordinal) + "ThemeBrush.For(".Length)..];
                if (rest.StartsWith("ElementTheme.Default") || rest.StartsWith(" ElementTheme.Default"))
                    violations.Add($"{rel}:{i + 1} 画笔解析要按元素实际主题取桶，不能写死 Default：{t.Trim()}");
            }
        }

        Assert.True(callSites >= 10, $"画笔解析站点计数异常偏低（{callSites}），扫描可能已失效");
        Assert.True(violations.Count == 0, "发现写死 ElementTheme.Default 的画笔解析：\n" + string.Join("\n", violations));
    }

    [GeneratedRegex(@"Resources\[\s*""(?<key>[^""]+)""\s*\]")]
    private static partial Regex ResourceKeyRegex();
}
