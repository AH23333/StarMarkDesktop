#nullable enable
using System;
using System.IO;
using System.Threading;
using StarMark.Core.Backup;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 自建路径输入框（提权会话里系统文件对话框不可用时的替代入口）的校验契约。
/// 这段判定决定"备份会被写到哪儿 / 恢复会被喂进什么文件"，故必须是可单测的纯函数。
/// </summary>
public sealed class BackupPathPolicyTests : IDisposable
{
    private readonly string _dir;

    public BackupPathPolicyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"sm_bk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    // ===== 导出 =====

    [Fact]
    public void ForExport_NullIsCancel_NotError()
    {
        var (path, error) = BackupPathPolicy.ForExport(null, _dir);
        Assert.Null(path);
        Assert.Null(error);   // 取消不能被当成"用户输错了"
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ForExport_Blank_AsksForPath(string raw)
    {
        var (path, error) = BackupPathPolicy.ForExport(raw, _dir);
        Assert.Null(path);
        Assert.NotNull(error);
    }

    [Fact]
    public void ForExport_BareFileName_LandsInDefaultDirectory()
    {
        // 只给文件名就该够用——让用户从 C:\ 一路敲到目标目录是多余的一步。
        var (path, error) = BackupPathPolicy.ForExport("bk.json", _dir);
        Assert.Null(error);
        Assert.Equal(Path.Combine(_dir, "bk.json"), path);
    }

    [Fact]
    public void ForExport_MissingExtension_AppendsJson()
    {
        var (path, error) = BackupPathPolicy.ForExport(Path.Combine(_dir, "mybackup"), _dir);
        Assert.Null(error);
        Assert.EndsWith("mybackup.json", path);
    }

    [Fact]
    public void ForExport_WrongExtension_Rejected()
    {
        var (path, error) = BackupPathPolicy.ForExport(Path.Combine(_dir, "bk.txt"), _dir);
        Assert.Null(path);
        Assert.Contains(".json", error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("LPT9")]
    public void ForExport_WindowsReservedStem_Rejected(string stem)
    {
        // 保留名能通过 File.Exists/Directory.Exists 的直觉检查，却会在真正写盘时以奇怪方式失败。
        var (path, error) = BackupPathPolicy.ForExport(Path.Combine(_dir, stem + ".json"), _dir);
        Assert.Null(path);
        Assert.Contains("保留名", error);
    }

    [Fact]
    public void ForExport_MissingDirectory_RejectedRatherThanCreated()
    {
        // 不替用户新建目录：手打错一层就会凭空造出垃圾目录，且用户以为备份存到了想去的地方。
        var ghost = Path.Combine(_dir, "no-such-sub", "bk.json");
        var (path, error) = BackupPathPolicy.ForExport(ghost, _dir);
        Assert.Null(path);
        Assert.Contains("目录不存在", error);
        Assert.False(Directory.Exists(Path.GetDirectoryName(ghost)));
    }

    [Fact]
    public void ForExport_QuotedPathFromExplorer_Accepted()
    {
        // 从资源管理器地址栏粘贴常带成对引号，不该因此让用户重敲一遍。
        var (path, error) = BackupPathPolicy.ForExport($"\"{Path.Combine(_dir, "bk.json")}\"", _dir);
        Assert.Null(error);
        Assert.Equal(Path.Combine(_dir, "bk.json"), path);
    }

    [Fact]
    public void ForExport_DotSegmentsAndMixedSlashes_Normalized()
    {
        var messy = $"{_dir.Replace('\\', '/')}/sub/../bk.json";
        var (path, error) = BackupPathPolicy.ForExport(messy, _dir);
        Assert.Null(error);
        Assert.Equal(Path.Combine(_dir, "bk.json"), path);
        Assert.DoesNotContain("..", path!);   // 归一后不留相对段：交给写盘的就是最终落点
    }

    // ===== 导入 =====

    [Fact]
    public void ForImport_NullIsCancel_NotError()
    {
        var (path, error) = BackupPathPolicy.ForImport(null);
        Assert.Null(path);
        Assert.Null(error);
    }

    [Fact]
    public void ForImport_NonJson_Rejected()
    {
        var file = Path.Combine(_dir, "notes.md");
        File.WriteAllText(file, "x");
        var (path, error) = BackupPathPolicy.ForImport(file);
        Assert.Null(path);
        Assert.Contains(".json", error);
    }

    [Fact]
    public void ForImport_MissingFile_RejectedBeforeAnyRestore()
    {
        // 必须在真正读档/恢复之前拦住：恢复流程自带"先落恢复前快照"，喂错文件不是无害失败。
        var (path, error) = BackupPathPolicy.ForImport(Path.Combine(_dir, "gone.json"));
        Assert.Null(path);
        Assert.Contains("文件不存在", error);
    }

    [Fact]
    public void ForImport_ExistingJson_Accepted()
    {
        var file = Path.Combine(_dir, "bk.json");
        File.WriteAllText(file, "{}");
        var (path, error) = BackupPathPolicy.ForImport(file);
        Assert.Null(error);
        Assert.Equal(file, path);
    }
}
