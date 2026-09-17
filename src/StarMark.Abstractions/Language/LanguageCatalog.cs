#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Abstractions.Language;

/// <summary>
/// 编程语言目录（GitHub Star 主语言）。主界面「语言」下拉据此直选，并供
/// <see cref="LanguageDetector"/> 把条目里的拼写不一的主语言归一为规范展示名。
/// 存储层统一以 <c>Name</c> 落盘到 <c>extra_json.Language</c>，下拉与 SQL 过滤都比对 <c>Name</c>。
/// 注意：此目录只收录「编程语言」，不收录人类自然语言（主界面语言过滤的语义是
/// 「按条目的编程语言筛选 star」，与人类语言无关）。
/// </summary>
public sealed record LanguageEntry(string Code, string Name);

public static class LanguageCatalog
{
    // Code 直接使用 GitHub API 返回的编程语言原名（大小写敏感），便于精确归一。
    private static readonly LanguageEntry[] _entries =
    {
        new("Python", "Python"), new("JavaScript", "JavaScript"), new("TypeScript", "TypeScript"),
        new("Java", "Java"), new("C++", "C++"), new("C#", "C#"), new("C", "C"),
        new("Go", "Go"), new("Rust", "Rust"), new("Ruby", "Ruby"), new("PHP", "PHP"),
        new("Swift", "Swift"), new("Kotlin", "Kotlin"), new("Objective-C", "Objective-C"),
        new("Shell", "Shell"), new("HTML", "HTML"), new("CSS", "CSS"), new("Vue", "Vue"),
        new("R", "R"), new("Dart", "Dart"), new("Scala", "Scala"), new("Perl", "Perl"),
        new("Lua", "Lua"), new("Haskell", "Haskell"), new("Elixir", "Elixir"),
        new("MATLAB", "MATLAB"), new("SQL", "SQL"), new("PowerShell", "PowerShell"),
        new("Groovy", "Groovy"), new("Clojure", "Clojure"), new("F#", "F#"),
        new("Assembly", "Assembly"), new("Julia", "Julia"), new("Zig", "Zig"),
        new("Solidity", "Solidity"), new("VB.NET", "VB.NET"), new("Nim", "Nim"),
        new("Crystal", "Crystal"), new("OCaml", "OCaml"), new("Erlang", "Erlang"),
        new("CoffeeScript", "CoffeeScript"),
    };

    private static readonly Dictionary<string, LanguageEntry> _byCode;
    private static readonly Dictionary<string, LanguageEntry> _byName;

    static LanguageCatalog()
    {
        _byCode = new Dictionary<string, LanguageEntry>(_entries.Length);
        _byName = new Dictionary<string, LanguageEntry>(_entries.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries)
        {
            _byCode[e.Code] = e;
            if (!_byName.ContainsKey(e.Name)) _byName[e.Name] = e;
        }
    }

    /// <summary>全部可选编程语言（下拉展示用）。</summary>
    public static IReadOnlyList<LanguageEntry> All => _entries;

    /// <summary>展示名列表（主界面语言下拉的数据源，仅编程语言）。</summary>
    public static IReadOnlyList<string> AllNames => _entries.Select(e => e.Name).ToArray();

    /// <summary>
    /// 把任意来源的原始语言字符串（大小写/拼写不一）归一为目录里的规范展示名。
    /// 命中（按 Code 或 Name 不区分大小写）返回规范 <see cref="LanguageEntry.Name"/>；
    /// 未命中则原样返回（保留用户数据里出现但目录未收录的语言）。
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var trimmed = raw.Trim();
        if (_byCode.TryGetValue(trimmed, out var byCode)) return byCode.Name;
        if (_byName.TryGetValue(trimmed, out var byName)) return byName.Name;
        // 大小写不敏感按 Code 兜底（如 "python" → "Python"）
        foreach (var e in _entries)
            if (string.Equals(e.Code, trimmed, StringComparison.OrdinalIgnoreCase)) return e.Name;
        return trimmed;
    }

    /// <summary>按 Code 取规范展示名（扩展入口，未收录返回 null）。</summary>
    public static string? NameOfCode(string code)
        => _byCode.TryGetValue(code, out var e) ? e.Name : null;
}
