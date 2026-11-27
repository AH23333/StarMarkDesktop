#nullable enable
using System;
using System.IO;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 待办附件的纯策略（片284 / P-305）：<see cref="TodoAttachmentPolicy"/> 只做字符串与路径判定，
/// 不碰磁盘、不读设置——所以这里全部是纯逻辑单测，无夹具无还原。
/// <para>
/// <see cref="TodoAttachmentPolicy.IsUnder"/> 是删除护栏的唯一判据（<c>TodoAttachmentStorage.RecycleManagedCopy</c>
/// 只回收满足它的那份）与「临时文件强制复制」的唯一判据（<see cref="TodoAttachmentPolicy.NeedsForcedCopy"/>）。
/// 这两条判定错一个方向就是两种用户可见事故：把用户原件/跨条目副本送进回收站，或者 %TEMP% 链接明天变死链。
/// </para>
/// </summary>
public sealed class TodoAttachmentPolicyTests
{
    private const string Root = @"C:\Managed\todo\abc123";

    // ── IsUnder：目录归属判定 ──

    [Fact]
    public void IsUnder_FileInsideDir_True()
        => Assert.True(TodoAttachmentPolicy.IsUnder(@"C:\Managed\todo\abc123\报告.pdf", Root));

    [Fact]
    public void IsUnder_NestedFileInsideDir_True()
        => Assert.True(TodoAttachmentPolicy.IsUnder(@"C:\Managed\todo\abc123\sub\深\file.txt", Root));

    [Theory]
    [InlineData(@"C:\Managed\todo\abc123")]                      // 恰好等于根本身：算「在目录里」
    [InlineData(@"C:\Managed\todo\abc123\")]                     // 尾分隔符：Trim 后等于根
    [InlineData(@"C:\Managed\todo\abc123\f.bin")]                // 普通孩子
    [InlineData(@"c:\managed\TODO\ABC123\f.bin")]                // 大小写不敏感（Windows 文件系统语义）
    public void IsUnder_AcceptsRootItselfAndCaseInsensitive(string path)
        => Assert.True(TodoAttachmentPolicy.IsUnder(path, Root));

    [Theory]
    [InlineData(@"C:\Managed\todo\abc1234\f.bin")]               // 兄弟前缀陷阱：dirX 不是 dir 的孩子
    [InlineData(@"C:\Managed\todo\zzz\f.bin")]                   // 同级目录：别的条目的托管目录
    [InlineData(@"C:\Managed\report.pdf")]                       // 明显在外的用户文件（Linked 原件）
    [InlineData(@"D:\Managed\todo\abc123\f.bin")]                // 盘符不同
    public void IsUnder_OutsideOrSiblingPrefix_False(string path)
        => Assert.False(TodoAttachmentPolicy.IsUnder(path, Root));

    [Fact]
    public void IsUnder_RelativeEscape_ResolvesOutside()
        // .. 逃出根：GetFullPath 归一之后躺在根外——不能按字面前缀误判成「在里头」
        => Assert.False(TodoAttachmentPolicy.IsUnder(@"C:\Managed\todo\abc123\..\..\..\Windows\evil.dll", Root));

    [Fact]
    public void IsUnder_TrailingSeparatorOnRoot_StillMatches()
        // 根带尾分隔符传入（ManagedDirFor 的调用方可能这么拼）：Trim 后照常比对
        => Assert.True(TodoAttachmentPolicy.IsUnder(@"C:\Managed\todo\abc123\f.bin", Root + Path.DirectorySeparatorChar));

    [Theory]
    [InlineData("", Root)]
    [InlineData("   ", Root)]
    [InlineData(@"C:\Managed\f.bin", "")]
    [InlineData(@"C:\Managed\f.bin", "   ")]
    public void IsUnder_BlankPathOrRoot_False(string path, string root)
        => Assert.False(TodoAttachmentPolicy.IsUnder(path, root));

    // ── NeedsForcedCopy：临时文件强制复制 ──

    [Fact]
    public void NeedsForcedCopy_TempFile_True()
    {
        var tempRoot = Path.GetTempPath();   // 注入的临时根，判据不依赖本机状态活着
        var inTemp = Path.Combine(tempRoot, "StarMark", "抓帧", "frame.png");
        Assert.True(TodoAttachmentPolicy.NeedsForcedCopy(inTemp, tempRoot));
    }

    [Fact]
    public void NeedsForcedCopy_RegularDoc_False()
    {
        var tempRoot = Path.GetTempPath();
        Assert.False(TodoAttachmentPolicy.NeedsForcedCopy(@"C:\Users\me\Documents\规格书.pdf", tempRoot));
    }

    [Fact]
    public void NeedsForcedCopy_SiblingPrefixOfTemp_False()
    {
        // 临时根的同级前缀目录（C:\Users\me\AppData\Local\TempXX）不是临时目录本体
        var tempRoot = Path.GetTempPath();
        var sibling = tempRoot.TrimEnd(Path.DirectorySeparatorChar) + "X";
        Assert.False(TodoAttachmentPolicy.NeedsForcedCopy(sibling, tempRoot));
    }

    [Fact]
    public void NeedsForcedCopy_EmptyInputs_False()
        => Assert.False(TodoAttachmentPolicy.NeedsForcedCopy("", Path.GetTempPath()));
}
