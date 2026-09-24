#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 组件档的解析缓存（批次 PA-5）。两件事同时成立才算对：
/// <b>一次交互内的多次询问只读一次盘</b>（省下来的东西），以及<b>陈旧有上界</b>（代价被压成常数）。
/// 另一条红线是"哪种结果不许进缓存"——缺文件、内容损坏、读盘被瞬时锁定各带一个恢复动作，
/// 缓存挡住它们就等于把恢复路径堵死。
/// </summary>
public sealed class WidgetStorageCacheTests : IDisposable
{
    /// <summary>测"只读一次盘"时用的超长有效期：这里要量的是缓存机制，不是墙钟运气。</summary>
    private static readonly TimeSpan LongWindow = TimeSpan.FromMinutes(5);

    /// <summary>测"陈旧有上界"时用的短有效期；等待取它的十倍，免得慢机器上飘。</summary>
    private static readonly TimeSpan ShortWindow = TimeSpan.FromMilliseconds(20);
    private const int ShortWindowWaitMs = 200;

    private readonly string _dir;
    private readonly string _path;

    public WidgetStorageCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "starmark_wcache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "widgets.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private WidgetStoreData Data(params string[] titles) => new()
    {
        Instances = titles.Select((title, index) => new WidgetInstanceConfig
        {
            Kind = WidgetKind.Clock,
            Title = title,
            Id = "i" + index,
        }).ToList(),
    };

    private static void WriteRaw(string path, WidgetStoreData data)
        => File.WriteAllText(path, JsonSerializer.Serialize(data));

    [Fact]
    public void RepeatedLoadsWithoutAChangeReadTheDiskOnce()
    {
        WriteRaw(_path, Data("甲", "乙"));
        var store = new WidgetStorage(_path, LongWindow);

        var first = store.Load();
        var second = store.Load();
        var third = store.Load();

        Assert.Equal(1, store.DiskReads);                       // 一次整档读
        Assert.Equal(2, store.CacheHits);                       // 后面两次连文件都没问
        Assert.Equal(first.Instances.Count, second.Instances.Count);
        Assert.Same(first, third);                              // 命中时给的是同一份解析结果（上层都当只读用）
        Assert.Equal(2, first.Instances.Count);
    }

    /// <summary>托盘菜单那种"逐类型问启用"的形状：<b>17 次询问 = 1 次读盘</b>。
    /// 这条就是本轮优化的目标本身，写成用例是为了退化时当场红。</summary>
    [Fact]
    public void AskingEveryKindCostsOneDiskRead()
    {
        WriteRaw(_path, Data("甲"));
        var store = new WidgetStorage(_path, LongWindow);

        for (var i = 0; i < WidgetStorage.AllKinds.Count; i++)
        {
            var kind = WidgetStorage.AllKinds[i];
            _ = store.Load().Instances.Any(instance => instance.Kind == kind);
        }

        Assert.Equal(1, store.DiskReads);
        Assert.Equal(WidgetStorage.AllKinds.Count - 1, store.CacheHits);
    }

    /// <summary>默认窗口必须短到"用户感觉是立即"：一次点击、一次菜单打开都在它之内完成，
    /// 于是省得彻底；而绕过 Save 的改写最坏也只晚它这么久被看到。</summary>
    [Fact]
    public void TheDefaultWindowIsShortEnoughToFeelImmediate()
    {
        Assert.True(WidgetStorage.DefaultCacheValidity <= TimeSpan.FromMilliseconds(500),
            $"默认缓存有效期被放宽到 {WidgetStorage.DefaultCacheValidity.TotalMilliseconds}ms，"
            + "外部改写的可见延迟会跟着变长，就不再是「感觉立即」了");
    }

    /// <summary>绕过 <see cref="WidgetStorage.Save"/> 直接改盘（备份还原、用户手改、
    /// 另一处 <c>new WidgetStorage()</c> 实例写入）后，窗口一过就必须看到新内容。</summary>
    [Fact]
    public void AnExternalRewriteIsPickedUpAfterTheWindow()
    {
        WriteRaw(_path, Data("旧摆位"));
        var store = new WidgetStorage(_path, ShortWindow);
        Assert.Equal("旧摆位", store.Load().Instances[0].Title);

        WriteRaw(_path, Data("新摆位"));
        Thread.Sleep(ShortWindowWaitMs);

        var after = store.Load();
        Assert.Equal("新摆位", after.Instances[0].Title);
        Assert.Equal(2, store.DiskReads);                       // 过期 ⇒ 重新读盘，而不是把旧快照递出去
    }

    /// <summary>等长改写也一样能发现——<b>这条不靠文件指纹</b>。
    /// 原以为靠"时间戳 + 长度"能挡住，实测挡住的是另一回事：NTFS 的最后写入时间粒度约 10ms，
    /// 同一 tick 内的两次等长写入指纹完全相同，于是陈旧没有上界。窗口一过就重读才是可证的判据。</summary>
    [Fact]
    public void ASameLengthRewriteIsStillDetected()
    {
        WriteRaw(_path, Data("甲"));
        var store = new WidgetStorage(_path, ShortWindow);
        var original = File.ReadAllText(_path);

        Assert.Equal("甲", store.Load().Instances[0].Title);

        WriteRaw(_path, Data("乙"));
        var rewritten = File.ReadAllText(_path);
        Assert.Equal(original.Length, rewritten.Length);        // 刻意构造等长改写，否则这条测不到东西
        Thread.Sleep(ShortWindowWaitMs);

        Assert.Equal("乙", store.Load().Instances[0].Title);
    }

    [Fact]
    public void OurOwnSaveInvalidatesTheSnapshot()
    {
        WriteRaw(_path, Data("甲"));
        var store = new WidgetStorage(_path, LongWindow);
        store.Load();

        store.Save(Data("甲", "新加的"));

        var after = store.Load();
        Assert.Equal(2, after.Instances.Count);
        Assert.Equal(2, store.DiskReads);                       // 自己写过的，下一次不许再递旧快照
    }

    [Fact]
    public void AFailedSaveKeepsTheCachedContentValid()
    {
        WriteRaw(_path, Data("甲"));
        var store = new WidgetStorage(_path, LongWindow);
        Assert.Equal("甲", store.Load().Instances[0].Title);

        // 用一个目录冒充 .tmp 让写入必然失败：磁盘仍是上一版，缓存也就仍是上一版
        Directory.CreateDirectory(_path + ".tmp");

        store.Save(Data("永远不会落盘"));

        Assert.Equal("甲", store.Load().Instances[0].Title);
        Assert.Equal(1, store.DiskReads);                       // 没白重读一次旧内容
    }

    /// <summary>损坏回退<b>不许进缓存</b>：把文件修好后必须能立刻读回真实内容。</summary>
    [Fact]
    public void ACorruptFallbackIsNotCached()
    {
        File.WriteAllText(_path, "{ 这不是 JSON");
        var store = new WidgetStorage(_path, LongWindow);

        var degraded = store.Load();
        WriteRaw(_path, Data("修好了"));

        var recovered = store.Load();

        Assert.NotEqual(degraded.Instances.Count, recovered.Instances.Count);
        Assert.Equal("修好了", recovered.Instances[0].Title);
        Assert.True(File.Exists(_path + ".bak"));               // 覆盖前留了挽回机会（既有行为不许退化）
    }

    /// <summary>读盘被瞬时锁定（OneDrive/杀软）时的空态<b>不许进缓存</b>，
    /// 否则"锁解除后下一次成功读取自动恢复"这条路径就被堵死了。
    /// 这里刻意不留等待：<b>下一次调用就得恢复</b>，靠窗口过期换来的恢复不算数。</summary>
    [Fact]
    public void ATransientReadFailureIsNotCached()
    {
        WriteRaw(_path, Data("甲"));
        var store = new WidgetStorage(_path, LongWindow);

        using (var hold = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var empty = store.Load();                           // 拿不到内容：回退空态，且 Save 会拒绝落盘
            Assert.DoesNotContain(empty.Instances, instance => instance.Title == "甲");
            Assert.True(store.DiskReads >= 1);
            hold.Flush();
        }

        var recovered = store.Load();
        Assert.Equal("甲", Assert.Single(recovered.Instances).Title);
    }

    /// <summary>首启预置必须当场落盘：实例 id 是随机 Guid，不落盘则每次读都换一个，
    /// 上层按 id 管窗口就会重复建窗（既有行为，缓存不许把它改回"每次换一套"）。</summary>
    [Fact]
    public void FirstRunPresetsArePersistedAndStableAcrossLoads()
    {
        var store = new WidgetStorage(_path, LongWindow);

        var first = store.Load();
        var second = store.Load();

        Assert.NotEmpty(first.Instances);
        Assert.True(File.Exists(_path));
        Assert.Equal(
            first.Instances.Select(instance => instance.Id),
            second.Instances.Select(instance => instance.Id));
    }
}
