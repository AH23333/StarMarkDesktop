#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="WidgetStorage.Mutate"/>（批次 PA-7）：<b>一次读档 → 就地改 N 处 → 最多一次落盘</b>。
/// 它存在的理由是"隐藏/关闭一批组件"这类动作——每个窗口各自 Load+Save 时，N 个组件就是 N 趟整档读写，
/// 而且 <c>Save</c> 会作废 <c>Load</c> 的缓存，所以读盘缓存救不了这条路径。
/// 用例两头都要测到：<b>省下来的次数是真的</b>，<b>正确性一处没丢</b>（连续两次改必须看得见对方、
/// 读盘被锁定时不许把回退出来的空档写回磁盘）。
/// </summary>
public sealed class WidgetStorageMutateTests : IDisposable
{
    private static readonly TimeSpan LongWindow = TimeSpan.FromMinutes(5);

    private readonly string _dir;
    private readonly string _path;

    public WidgetStorageMutateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "starmark_wmutate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "widgets.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static WidgetStoreData Data(params string[] titles) => new()
    {
        Instances = titles.Select((title, index) => new WidgetInstanceConfig
        {
            Kind = WidgetKind.Clock,
            Title = title,
            Id = "i" + index,
        }).ToList(),
    };

    private void WriteRaw(WidgetStoreData data) => File.WriteAllText(_path, JsonSerializer.Serialize(data));

    private WidgetStorage Open() => new(_path, LongWindow);

    [Fact]
    public void TenInstancesChangeInOneReadAndOneWrite()
    {
        WriteRaw(Data("甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"));
        var store = Open();

        var wrote = store.Mutate(data =>
        {
            foreach (var inst in data.Instances) inst.Title = inst.Title + "·改名";
            return true;
        });

        Assert.True(wrote);
        Assert.Equal(1, store.DiskReads);
        Assert.Equal(1, store.DiskWrites);                    // 逐个 Save 的话这里是 10 趟整档写
        var reread = new WidgetStorage(_path, LongWindow).Load();
        Assert.Equal("甲·改名", reread.Instances[0].Title);
        Assert.Equal("癸·改名", reread.Instances[9].Title);
    }

    [Fact]
    public void ARejectedMutationWritesNothing()
    {
        WriteRaw(Data("甲"));
        var before = File.ReadAllText(_path);
        var store = Open();

        Assert.False(store.Mutate(data =>
        {
            _ = data.Instances.Count;                         // 看过但没改
            return false;
        }));

        Assert.Equal(0, store.DiskWrites);
        Assert.Equal(before, File.ReadAllText(_path));        // 一次盘都不该动
    }

    /// <summary>连续两次改必须看得见对方的结果——<b>缓存不许把第一次写过的新档挡在外面</b>。
    /// 这条是"用缓存换往返次数"的全部风险所在。</summary>
    [Fact]
    public void TheSecondMutationSeesWhatTheFirstWrote()
    {
        WriteRaw(Data("甲"));
        var store = Open();

        Assert.True(store.Mutate(data => { data.Instances[0].Title = "第二次要看到的那一行"; return true; }));
        var observed = store.Mutate(data =>
        {
            data.Instances[0].Title = data.Instances[0].Title + "+1";
            return true;
        });

        Assert.True(observed);
        Assert.Equal("第二次要看到的那一行+1", new WidgetStorage(_path, LongWindow).Load().Instances[0].Title);
        Assert.Equal(2, store.DiskWrites);
    }

    [Fact]
    public void FiveNoOpMutationsReadTheDiskOnce()
    {
        WriteRaw(Data("甲"));
        var store = Open();

        for (var i = 0; i < 5; i++) store.Mutate(data => data.Instances[0].Title == "永远不会 equal 的写法");

        Assert.Equal(1, store.DiskReads);                     // 全部命中快照缓存，且一次都没写
        Assert.Equal(0, store.DiskWrites);
    }

    /// <summary>读盘被瞬时锁定（OneDrive/杀软）时 <see cref="WidgetStorage.Load"/> 回退出来的是<b>空档</b>，
    /// 而磁盘上的数据其实完好。此时 mutate 说"改好了，写吧"也绝不能落盘——
    /// 那是"一次文件占用把用户所有组件配置清空"的形状（P-16 那条老线）。</summary>
    [Fact]
    public void ALockedReadInsideMutateMustNotOverwriteTheDisk()
    {
        WriteRaw(Data("真实配置"));
        var store = Open();

        using (var hold = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(store.Mutate(data =>
            {
                Assert.Empty(data.Instances);                 // 这一次确实什么都没读到
                return true;                                  // 调用方以为改成了，要求落盘
            }));
            hold.Flush();
        }

        Assert.Equal(0, store.DiskWrites);
        Assert.Equal("真实配置", new WidgetStorage(_path, LongWindow).Load().Instances[0].Title);
    }

    /// <summary>写盘失败（这里用一个目录冒充 <c>.tmp</c> 逼出必然失败）时，
    /// <b>不许把"没写成功"报成"写好了"</b>——上层据此才能说"已保存"或"没能保存"，
    /// 而不是让用户以为改动已经留下。</summary>
    [Fact]
    public void AFailedWriteIsReportedAsNoWrite()
    {
        WriteRaw(Data("甲"));
        var before = File.ReadAllText(_path);
        var store = Open();
        store.Load();                                         // 先建立缓存，排除"没读到"这条干扰路径
        Directory.CreateDirectory(_path + ".tmp");

        Assert.False(store.Mutate(data => { data.Instances[0].Title = "写不进去"; return true; }));

        Assert.Equal(0, store.DiskWrites);                    // 计数只认"真的搬成功了"那一次
        Assert.Equal(before, File.ReadAllText(_path));        // 磁盘仍是上一版：没被截断，也没被清空
    }
}
