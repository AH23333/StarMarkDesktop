#nullable enable
using System;
using System.IO;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 P-80 的存储件单测：<see cref="PinStore"/> 是纯逻辑（无 UI 依赖），这里钉它的行为口径——
/// 缺档→空、坏 JSON→.bak＋空、瞬时占用→降级位（Save 拒写防蒸发）、归一化、读路径不截断。
/// <para>
/// 日志不必自己改道：<c>TestSandboxRedirect</c> 的模块初始化已把 <c>StarLog.DirectoryOverride</c>
/// 指进 %TEMP%——坏档/降级路径会写 ERROR/WARN，落进用户真实日志目录就是"测试污染真机台账"的老坑。
/// </para>
/// </summary>
public sealed class PinStoreTests : IDisposable
{
    private readonly string _dir;

    public PinStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"starmark_pins_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void MissingFileLoadsEmptyAndSavingIsAllowed()
    {
        var store = new PinStore(_dir);
        Assert.Empty(store.Load().Pins);                     // 首启：空态是可信的

        var data = new PinStoreData();
        data.Pins.Add(new PinRecord { Id = "first" });
        Assert.True(store.Save(data));                       // 允许落盘（目录不存在也得能建）
        Assert.True(File.Exists(store.JsonPath));
        Assert.Equal("first", Assert.Single(store.Load().Pins).Id);
    }

    [Fact]
    public void RoundTripCarriesAllSevenFields()
    {
        var store = new PinStore(_dir);
        var data = new PinStoreData();
        data.Pins.Add(new PinRecord
        {
            Id = "abc123",
            X = -200.5,
            Y = 88.25,
            Zoom = 2.5,
            Opacity = 0.7,
            ClickThrough = true,
            Hidden = true,
        });
        Assert.True(store.Save(data));

        var rec = Assert.Single(store.Load().Pins);
        Assert.Equal("abc123", rec.Id);
        Assert.Equal(-200.5, rec.X);
        Assert.Equal(88.25, rec.Y);
        Assert.Equal(2.5, rec.Zoom);
        Assert.Equal(0.7, rec.Opacity);
        Assert.True(rec.ClickThrough);
        Assert.True(rec.Hidden);
    }

    [Fact]
    public void CorruptJsonIsBackedUpAndThenRecovers()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pins.json"), "{ not json");
        var store = new PinStore(_dir);

        Assert.Empty(store.Load().Pins);                                    // 坏档回退空
        Assert.Equal("{ not json", File.ReadAllText(store.JsonPath + ".bak"));   // 原文留一份 .bak

        var fresh = new PinStoreData();
        fresh.Pins.Add(new PinRecord { Id = "fresh" });
        Assert.True(store.Save(fresh));                                     // 坏档那一次允许覆盖（磁盘上本就无可信数据）
        Assert.Equal("fresh", Assert.Single(store.Load().Pins).Id);
    }

    [Fact]
    public void TransientReadLockDegradesAndSaveRefuses()
    {
        var store = new PinStore(_dir);
        var earlier = new PinStoreData();
        earlier.Pins.Add(new PinRecord { Id = "survivor" });
        Assert.True(store.Save(earlier));

        using (new FileStream(store.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(store.Load().Pins);            // 瞬时占用→空＋降级位
            var wipe = new PinStoreData();
            wipe.Pins.Add(new PinRecord { Id = "imposter" });
            Assert.False(store.Save(wipe));             // 降级态拒写——空数据不许覆盖真实记录
        }

        Assert.Contains("survivor", File.ReadAllText(store.JsonPath));       // 锁定解除后原档还在
        Assert.Equal("survivor", Assert.Single(store.Load().Pins).Id);       // 成功的 Load 清掉降级位
    }

    [Fact]
    public void NormalizeDropsNullEmptyAndDuplicateIdsKeepingFirst()
    {
        var data = new PinStoreData();
        data.Pins.Add(new PinRecord { Id = "first", X = 10 });
        data.Pins.Add(null!);                                  // 手改档/坏写入可达
        data.Pins.Add(new PinRecord { Id = "", X = 99 });      // 空 Id：无处挂 PNG
        data.Pins.Add(new PinRecord { Id = "first", X = 77 }); // 重复 Id：保留首个
        data.Pins.Add(new PinRecord { Id = "second" });

        var kept = PinStore.Normalize(data).Pins;
        Assert.Equal(2, kept.Count);
        Assert.Equal("first", kept[0].Id);
        Assert.Equal(10, kept[0].X);
        Assert.Equal("second", kept[1].Id);
    }

    [Fact]
    public void NormalizeSanitizesCoordinatesAndRanges()
    {
        var data = new PinStoreData();
        data.Pins.Add(new PinRecord { Id = "nan", X = double.NaN, Y = double.NegativeInfinity, Zoom = double.NaN, Opacity = double.NaN });
        data.Pins.Add(new PinRecord { Id = "wild", X = 1, Y = 2, Zoom = 6, Opacity = 0.01 });
        data.Pins.Add(new PinRecord { Id = "floor", X = 1, Y = 2, Zoom = 0.1, Opacity = 3 });

        var kept = PinStore.Normalize(data).Pins;
        Assert.Equal(3, kept.Count);

        Assert.Equal(0, kept[0].X);                     // NaN/∞ 坐标回落 0，不许传进窗口摆位
        Assert.Equal(0, kept[0].Y);
        Assert.Equal(1, kept[0].Zoom);
        Assert.Equal(1, kept[0].Opacity);

        Assert.Equal(CaptureGeometry.MaxPinZoom, kept[1].Zoom);
        Assert.Equal(0.1, kept[1].Opacity);             // 与 SetOpacity 的下限一致
        Assert.Equal(CaptureGeometry.MinPinZoom, kept[2].Zoom);
        Assert.Equal(1, kept[2].Opacity);
    }

    [Fact]
    public void MutateLoadsChangesAndSavesInOneTrip()
    {
        var store = new PinStore(_dir);
        Assert.True(store.Mutate(d => d.Pins.Add(new PinRecord { Id = "m1" })));
        Assert.True(store.Mutate(d => d.Pins.Add(new PinRecord { Id = "m2" })));
        Assert.Equal(2, store.Load().Pins.Count);

        Assert.True(store.Mutate(d => d.Pins.RemoveAll(r => r.Id == "m1")));
        Assert.Equal("m2", Assert.Single(store.Load().Pins).Id);
    }

    [Fact]
    public void LoadDoesNotTruncate_TheLimitLivesInRestore()
    {
        // P-10 教训（读路径替用户做决定＝读一次少几条）：Normalize 刻意不限量，
        // 上限由名册 live 判据与恢复侧的 break 守——这里钉死"读路径永远原样奉还"。
        var store = new PinStore(_dir);
        var data = new PinStoreData();
        for (var i = 0; i <= CaptureGeometry.MaxPins; i++)
            data.Pins.Add(new PinRecord { Id = $"over{i}" });

        Assert.True(store.Save(data));
        Assert.Equal(CaptureGeometry.MaxPins + 1, store.Load().Pins.Count);
    }

    [Fact]
    public void PathsHangOffTheDirectory()
    {
        var store = new PinStore(_dir);
        Assert.Equal(_dir, store.PngDirectory);
        Assert.Equal(Path.Combine(_dir, "pins.json"), store.JsonPath);
        Assert.Equal(Path.Combine(_dir, "pin-abc.png"), store.PngPathOf("abc"));
    }
}
