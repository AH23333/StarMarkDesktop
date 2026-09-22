#nullable enable
using System;
using System.IO;

namespace StarMark.Core.Backup;

/// <summary>
/// 备份文件路径的校验与归一——用于<b>系统文件对话框调不起来</b>的会话（提权运行的 StarMark 跨不到
/// 中 IL 的对话框宿主，<c>FileSavePicker/FileOpenPicker</c> 稳定 E_FAIL），此时改由应用内输入框
/// 收集路径。放 Core 而不是 UI 的原因：这段是纯字符串判定，且它决定"备份会被写到哪儿 / 恢复会
/// 被喂进什么文件"，必须可单测。
/// </summary>
public static class BackupPathPolicy
{
    /// <summary>Windows 保留设备名（不区分大小写，且作为主干名时后面带任何扩展名都非法）。</summary>
    private static readonly string[] ReservedStems =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 归一用户手输的导出目标：去引号/空白、补 <c>.json</c>、展开为绝对路径。
    /// 返回 (可用路径, 错误)：错误非 null 时前者为 null，调用方把错误原样回显给用户再问一次。
    /// </summary>
    /// <param name="raw">用户输入（可为 null＝取消）。</param>
    /// <param name="defaultDirectory">默认目录；输入只给文件名时落到这里。</param>
    public static (string? Path, string? Error) ForExport(string? raw, string defaultDirectory)
    {
        var text = Clean(raw);
        if (text is null) return (null, null);                                   // 取消
        if (text.Length == 0) return (null, "请填写保存位置（只填文件名会存到备份目录）。");

        // 只给文件名 → 落到默认目录，省掉"整盘路径"这种多余输入。
        if (!HasDirectory(text))
            text = Path.Combine(defaultDirectory, text);

        var full = TryFull(text, out var err);
        if (full is null) return (null, err);

        var stem = Path.GetFileNameWithoutExtension(full);
        if (stem.Length == 0) return (null, "文件名不能为空。");
        if (Array.Exists(ReservedStems, s => string.Equals(s, stem, StringComparison.OrdinalIgnoreCase)))
            return (null, $"「{stem}」是 Windows 保留名，换一个文件名。");

        var ext = Path.GetExtension(full);
        if (ext.Length == 0) full += ".json";
        else if (!string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            return (null, "备份文件需用 .json 扩展名（直接删掉结尾的扩展名即可自动补上）。");

        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dir)) return (null, "请填写包含目录的完整路径。");
        if (!Directory.Exists(dir)) return (null, $"目录不存在：{dir}");

        return (full, null);
    }

    /// <summary>归一用户手输的导入来源：必须是存在的 .json 文件。</summary>
    public static (string? Path, string? Error) ForImport(string? raw)
    {
        var text = Clean(raw);
        if (text is null) return (null, null);
        if (text.Length == 0) return (null, "请填写要导入的备份文件完整路径。");

        var full = TryFull(text, out var err);
        if (full is null) return (null, err);

        if (!string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase))
            return (null, "请选择 .json 备份文件。");
        if (!File.Exists(full))
            return (null, $"文件不存在：{full}");

        return (full, null);
    }

    /// <summary>去掉首尾空白与从资源管理器粘贴时带上的成对引号；null 原样返回 null（＝取消）。</summary>
    private static string? Clean(string? raw)
    {
        if (raw is null) return null;
        var text = raw.Trim();
        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            text = text[1..^1].Trim();
        return text;
    }

    /// <summary>是否自带目录部分（相对路径交给默认目录兜）。判定本身不得抛——非法字符由 TryFull 统一报。</summary>
    private static bool HasDirectory(string text)
    {
        if (text.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        try { return Path.GetDirectoryName(text)?.Length > 0; }
        catch (ArgumentException) { return false; }
    }

    private static string? TryFull(string text, out string? error)
    {
        error = null;
        try
        {
            return Path.GetFullPath(text);       // 顺带把 .. / . / 混合斜杠规范化，并拒绝非法字符
        }
        catch (Exception ex)
        {
            error = $"路径无法解析：{ex.Message}";
            return null;
        }
    }
}
