#nullable enable
using System.IO;
using System.Text.Json.Nodes;

namespace StarMark.Abstractions.Language;

/// <summary>
/// 条目编程语言识别。主界面「语言」下拉的语义是「按条目的编程语言筛选 star」，
/// 因此本类只识别<see cref="LanguageCatalog"/> 中的编程语言，绝不推断人类自然语言。
/// <list type="bullet">
///   <item>GitHub Star 等已标注主语言（extra_json.Language，来自 GitHub API / 种子数据）→ 直接归一。</item>
///   <item>书签 / 网页等无标注 → 仅当直链到明确源码文件时，按扩展名兜底推断编程语言。</item>
///   <item>无法判定（如普通网页书签）→ 返回 null，绝不臆测「中文 / English」之类人类语言。</item>
/// </list>
/// 存储键统一为 <c>extra_json.Language</c>（PascalCase），避免大小写敏感过滤失效。
/// </summary>
public static class LanguageDetector
{
    // 源码扩展名 → 编程语言（仅限明确源码扩展名，避免把 .html 网页误标为语言）。
    private static readonly Dictionary<string, string> _extToLang = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".py", "Python" }, { ".js", "JavaScript" }, { ".jsx", "JavaScript" }, { ".ts", "TypeScript" },
        { ".tsx", "TypeScript" }, { ".java", "Java" }, { ".cpp", "C++" }, { ".cc", "C++" },
        { ".cxx", "C++" }, { ".c", "C" }, { ".h", "C" }, { ".hpp", "C++" }, { ".cs", "C#" },
        { ".go", "Go" }, { ".rs", "Rust" }, { ".rb", "Ruby" }, { ".php", "PHP" }, { ".swift", "Swift" },
        { ".kt", "Kotlin" }, { ".kts", "Kotlin" }, { ".m", "Objective-C" }, { ".mm", "Objective-C" },
        { ".sh", "Shell" }, { ".bash", "Shell" }, { ".zsh", "Shell" }, { ".ps1", "PowerShell" },
        { ".sql", "SQL" }, { ".r", "R" }, { ".dart", "Dart" }, { ".scala", "Scala" }, { ".pl", "Perl" },
        { ".lua", "Lua" }, { ".hs", "Haskell" }, { ".ex", "Elixir" }, { ".exs", "Elixir" },
        { ".jl", "Julia" }, { ".zig", "Zig" }, { ".sol", "Solidity" }, { ".fs", "F#" },
        { ".clj", "Clojure" }, { ".groovy", "Groovy" }, { ".vb", "VB.NET" }, { ".asm", "Assembly" },
        { ".s", "Assembly" }, { ".vue", "Vue" }, { ".coffee", "CoffeeScript" }, { ".erl", "Erlang" },
        { ".ml", "OCaml" },
    };

    /// <summary>
    /// 在不破坏现有 extra_json 的前提下，确保条目带有编程语言标注：
    /// 已有（任意大小写）则原样返回（小写键归一到 PascalCase）；
    /// 缺失则按扩展名兜底推断并最小写入。无法判断时返回原 extra_json。
    /// </summary>
    public static string? EnsureLanguage(string? extraJson, string? uri, string? title, string? description)
    {
        JsonObject? obj = null;
        if (!string.IsNullOrWhiteSpace(extraJson))
        {
            try
            {
                if (JsonNode.Parse(extraJson) is JsonObject o)
                {
                    obj = o;
                    // 已有 PascalCase 语言标注 → 原样返回
                    if (o["Language"] is JsonValue lv && IsNonEmptyString(lv)) return extraJson;
                    // 小写键归一到 PascalCase（避免 json_extract('$.Language') 大小写敏感漏匹配）
                    if (o["language"] is JsonValue l2 && IsNonEmptyString(l2))
                    {
                        o["Language"] = LanguageCatalog.Normalize(l2.GetValue<string>());
                        o.Remove("language");
                        return o.ToJsonString();
                    }
                }
            }
            catch { obj = null; }
        }

        var detected = Detect(extraJson, uri, title, description);
        if (detected is null) return extraJson;

        obj ??= new JsonObject();
        obj["Language"] = detected;
        return obj.ToJsonString();
    }

    /// <summary>推断编程语言展示名；无法判断返回 null。</summary>
    public static string? Detect(string? extraJson, string? uri, string? title, string? description)
    {
        // 1) extra_json 已标注（主语言来自 GitHub API / 种子数据）→ 归一为规范名
        if (!string.IsNullOrWhiteSpace(extraJson))
        {
            try
            {
                if (JsonNode.Parse(extraJson) is JsonObject o)
                {
                    if (o["Language"] is JsonValue l && IsNonEmptyString(l))
                        return LanguageCatalog.Normalize(l.GetValue<string>());
                    if (o["language"] is JsonValue l2 && IsNonEmptyString(l2))
                        return LanguageCatalog.Normalize(l2.GetValue<string>());
                }
            }
            catch { /* 忽略损坏 JSON */ }
        }

        // 2) 直链到明确源码文件：按扩展名兜底推断编程语言（不臆测人类语言）
        return InferFromUri(uri);
    }

    private static string? InferFromUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return null;
        var ext = Path.GetExtension(u.AbsolutePath);
        return _extToLang.TryGetValue(ext, out var lang) ? LanguageCatalog.Normalize(lang) : null;
    }

    private static bool IsNonEmptyString(JsonValue? e) =>
        e is not null && e.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s);
}
