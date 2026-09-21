#nullable enable
using System;
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.Everything;

namespace StarMark.Tests;

/// <summary>
/// 本地路径身份基元单测：确保 Everything 查询侧、CSV 解析侧与「拖入登记」侧对同一路径算出<b>相同</b>的
/// (source, source_id, uri)——这是 items 表 (source, source_id) 唯一索引上去重/合并的唯一依据。
/// </summary>
public sealed class LocalFileIdentityTests
{
    [Fact]
    public void UriForPath_UsesTwoSlashForwardSlashForm()
    {
        Assert.Equal("file://C:/Data/demo/readme.md", LocalFileIdentity.UriForPath(@"C:\Data\demo\readme.md"));
    }

    [Fact]
    public void SourceIdForPath_IsStable_CaseInsensitive_AndFixedLength()
    {
        var a = LocalFileIdentity.SourceIdForPath(@"C:\Data\Demo\Readme.md");
        var b = LocalFileIdentity.SourceIdForPath(@"c:\data\demo\readme.md");
        var again = LocalFileIdentity.SourceIdForPath(@"C:\Data\Demo\Readme.md");
        Assert.Equal(a, b);          // 大小写归一
        Assert.Equal(a, again);      // 稳定
        Assert.Equal(16, a.Length);  // 8 字节 → 16 位十六进制
    }

    [Fact]
    public void SourceIdForPath_DiffersForDifferentPaths()
    {
        Assert.NotEqual(
            LocalFileIdentity.SourceIdForPath(@"C:\a.txt"),
            LocalFileIdentity.SourceIdForPath(@"C:\b.txt"));
    }

    [Fact]
    public void EverythingParseCsvLine_AndDragSide_ShareIdenticalIdentity()
    {
        // Everything CSV：Name,Path → 组合成全路径 C:\Data\demo\readme.md。
        var item = EverythingInterop.ParseCsvLine(
            "readme.md,C:\\Data\\demo,1024,2024-05-01 12:00:00",
            EverythingInterop.RequestFlags.FullPath | EverythingInterop.RequestFlags.Size);
        Assert.NotNull(item);

        // 同一磁盘路径经「拖入登记」侧（LocalFileIdentity）派生的业务键必须一致 → 落库合并为一条、不分裂。
        const string fullPath = @"C:\Data\demo\readme.md";
        Assert.Equal(LocalFileIdentity.SourceIdForPath(fullPath), item!.SourceId);
        Assert.Equal(LocalFileIdentity.UriForPath(fullPath), item.Uri);
        Assert.Equal(ItemSources.FileSystem, item.Source);
    }

    [Fact]
    public void FromPath_BuildsUnpersistedFileItem()
    {
        var item = LocalFileIdentity.FromPath(@"C:\Data\demo\readme.md", "重命名");
        Assert.Equal(0, item.Id);   // 未入库
        Assert.Equal(ItemType.File, item.Type);
        Assert.Equal(ItemSources.FileSystem, item.Source);
        Assert.Equal("重命名", item.Title);
        Assert.Equal(@"C:\Data\demo", item.Subtitle);
        Assert.Equal(LocalFileIdentity.SourceIdForPath(@"C:\Data\demo\readme.md"), item.SourceId);
        Assert.Equal("file://C:/Data/demo/readme.md", item.Uri);
    }

    [Fact]
    public void TryPathFromUri_RoundTripsBothSlashForms()
    {
        // 两斜杠（Everything 侧拼法）
        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:/Data/demo/readme.md", out var p1));
        Assert.Equal(@"C:\Data\demo\readme.md", p1);

        // 三斜杠（DB / 标准 file:/// 拼法）—— 剥多余前导斜杠后须与两斜杠结果一致
        Assert.True(LocalFileIdentity.TryPathFromUri("file:///C:/Data/demo/readme.md", out var p2));
        Assert.Equal(@"C:\Data\demo\readme.md", p2);

        // UriForPath → TryPathFromUri 往返一致（拖出侧依赖此不变式）
        Assert.True(LocalFileIdentity.TryPathFromUri(LocalFileIdentity.UriForPath(@"D:\工作 报告\x.pdf"), out var p3));
        Assert.Equal(@"D:\工作 报告\x.pdf", p3);
    }

    [Fact]
    public void TryPathFromUri_RejectsNonFileOrMissingDrive()
    {
        Assert.False(LocalFileIdentity.TryPathFromUri(null, out _));
        Assert.False(LocalFileIdentity.TryPathFromUri("", out _));
        Assert.False(LocalFileIdentity.TryPathFromUri("https://example.com/x", out _));
        // file:// 但缺盘符（如 UNC 或相对）→ false，不臆造路径
        Assert.False(LocalFileIdentity.TryPathFromUri("file://server/share", out _));
    }

    [Fact]
    public void TryPathFromUri_PreservesHashAndPercent()
    {
        // 回归：Uri.LocalPath 会在 '#' 处把 URI 当片段截断（file://C:/x#y.txt → C:\x），
        // 导致含 '#' 的合法文件名打不开、且 C#1.txt 与 C#2.txt 在去重键上塌成同一。
        // TryPathFromUri 走纯字符串剥前缀，必须保留 '#' 与 '%'（不做百分号解码，维持与 UriForPath 的往返契约）。
        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:/docs/C#入门.docx", out var hash));
        Assert.Equal(@"C:\docs\C#入门.docx", hash);

        Assert.True(LocalFileIdentity.TryPathFromUri("file:///C:/tmp/issue#1.pdf", out var hash3));
        Assert.Equal(@"C:\tmp\issue#1.pdf", hash3);

        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:/a/100%.txt", out var pct));
        Assert.Equal(@"C:\a\100%.txt", pct);   // 不解码 %

        // 含 '#' 的两个不同文件必须还原成不同路径（去重键不冲突）
        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:/C#1.txt", out var u1));
        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:/C#2.txt", out var u2));
        Assert.NotEqual(u1, u2);
    }

    /// <summary>
    /// 契约护栏（DJ·承 CW 大小写不敏感归一同款）：scheme 前缀匹配刻意用
    /// <c>StringComparison.OrdinalIgnoreCase</c>（<c>LocalFileIdentity.cs:48</c>），但既有全部
    /// <c>TryPathFromUri_*</c> 测都用小写 "file://"，从未**区分性**触发不敏感那一半——
    /// 若有人把它改回默认 <c>Ordinal</c>，现有测全绿却会静默丢弃 <c>File://</c> 大小写混合的 URI
    /// （历史导入 / 外部来源可产生），令本可打开的本地文件条目被误判为非法。三例覆盖全大写 / 混合 + 三斜杠 / 交替大小写。
    /// </summary>
    [Theory]
    [InlineData("FILE://C:/x", @"C:\x")]
    [InlineData("File:///D:/y.md", @"D:\y.md")]  // 混合大小写 scheme + 三斜杠形态
    [InlineData("fIlE://E:/z", @"E:\z")]
    public void TryPathFromUri_IsCaseInsensitiveOnSchemePrefix(string uri, string expected)
    {
        Assert.True(LocalFileIdentity.TryPathFromUri(uri, out var path));
        Assert.Equal(expected, path);
    }

    /// <summary>
    /// 契约护栏（DJ·承 AT 崩溃钳制同款）：<c>:50</c> 的门 <c>rest.Length &gt;= 2 &amp;&amp; char.IsLetter(rest[0]) &amp;&amp; rest[1] == ':'</c>
    /// 依赖 <c>&amp;&amp;</c> 从左到右短路——长度检查**必须先于** <c>rest[1]</c> 求值。若被重排或改成 <c>&gt;= 1</c>，
    /// 单字符 rest（"file://C" → "C"，Length==1）访问 <c>rest[1]</c> 即抛 <see cref="IndexOutOfRangeException"/>，
    /// 冒到调用方 <c>FolderPathUtil.FileSegments</c> 与 UI 拖拽登记路径。钉死「1 字符 → 干净返 false、fullPath 留空」防重构回归。
    /// </summary>
    [Fact]
    public void TryPathFromUri_SingleCharRest_ReturnsFalseWithoutThrowing()
    {
        Assert.False(LocalFileIdentity.TryPathFromUri("file://C", out var p));
        Assert.Equal(string.Empty, p);  // :46 早置空串，早退分支不改写
    }

    /// <summary>
    /// 契约护栏（DJ）：<c>file://C:</c>（rest 恰为 "C:"，Length==2）是合法裸盘符根，须返 true 且原样保留——
    /// 与上例合起来钉死门的下界（Length==1 拒、==2 且字母+冒号 接受）。
    /// </summary>
    [Fact]
    public void TryPathFromUri_AcceptsBareDriveRoot()
    {
        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:", out var p));
        Assert.Equal("C:", p);
    }
}
