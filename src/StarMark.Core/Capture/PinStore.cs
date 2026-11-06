#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StarMark.Abstractions;

namespace StarMark.Core.Capture;

/// <summary>
/// 一张已落盘贴图的元数据（✅P-80 七格口径：Id/X/Y/Zoom/Opacity/ClickThrough/Hidden）。
/// 像素本体住在同目录的 <c>pin-{Id}.png</c>，这里只记"窗怎么摆、什么状态"。
/// </summary>
public sealed class PinRecord
{
    /// <summary>贴图唯一 Id：<see cref="Guid"/> 的 "N" 格式（32 位 hex），同时是 PNG 档名的一部分。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>窗口左上角 X（物理像素，虚拟桌面坐标，跨屏可为负）。</summary>
    public double X { get; set; }

    /// <summary>窗口左上角 Y（物理像素）。</summary>
    public double Y { get; set; }

    /// <summary>缩放倍率。量程的主人只有 <see cref="CaptureGeometry.MinPinZoom"/> / <see cref="CaptureGeometry.MaxPinZoom"/>。</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>不透明度（0.1–1，下限与 <c>CaptureOverlayWindow.SetOpacity</c> 的夹取一致）。</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>鼠标穿透是否开着。</summary>
    public bool ClickThrough { get; set; }

    /// <summary>是否处于隐藏态（任务栏/托盘里仍可召回）。</summary>
    public bool Hidden { get; set; }
}

/// <summary>贴图存档根（pins.json 的形状）。组不持久化（✅P-80 裁决 D3）：组是运行时的摆法，不是贴图本身的事实。</summary>
public sealed class PinStoreData
{
    public int Version { get; set; } = 1;

    /// <summary>全部贴图元数据。上限由名册与恢复侧守住，读路径不替用户做决定（P-10 教训）。</summary>
    public List<PinRecord> Pins { get; set; } = new();
}

/// <summary>
/// 贴图持久化（✅P-80）：每张贴图＝一份 PNG＋一行元数据，启动时按元数据重建窗口。
/// <para>走 <c>WidgetStorage</c> 的纪律：缺档→空、坏 JSON→.bak＋空、读盘被瞬时占用→降级位（Save 拒写防蒸发）、
/// 无缓存（贴图状态变化频率远低于组件，每次 Load＋改＋Save 的 Mutate 模式足够，不值得养一份快照缓存）。</para>
/// <para>纯逻辑、无 UI 依赖；目录默认从 <see cref="UserDataPaths.Subdirectory"/> 出（沙盒取证/单测改道跟着整棵根走）。</para>
/// </summary>
public sealed class PinStore
{
    private readonly string _dir;
    private readonly object _gate = new();

    /// <summary>
    /// 上一次 <see cref="Load"/> 是否因瞬时 IO/权限错误（OneDrive 同步、杀软实时扫描短暂锁定 pins.json）
    /// 未能读到磁盘内容。为 true 时内存态不代表磁盘态，<see cref="Save"/> 拒绝落盘，避免用空数据覆盖真实记录。
    /// </summary>
    private bool _loadDegraded;

    /// <summary><paramref name="dir"/> 只给测试与"必须立刻反映外部改动"的调用方用；传 null 就是默认目录。</summary>
    public PinStore(string? dir = null)
    {
        _dir = dir ?? DefaultDirectory();
    }

    /// <summary>贴图档住在用户数据根的 <c>pins\</c> 子目录（改道规则只有 <see cref="UserDataPaths"/> 一处）。</summary>
    public static string DefaultDirectory() => UserDataPaths.Subdirectory("pins");

    /// <summary>PNG 档住的目录（prune 孤儿档时从这里列 <c>pin-*.png</c>）。</summary>
    public string PngDirectory => _dir;

    /// <summary>元数据档的完整路径。</summary>
    public string JsonPath => Path.Combine(_dir, "pins.json");

    /// <summary>某张贴图的 PNG 档完整路径（档名＝<c>pin-{Id}.png</c>，Id 即档名的一部分 ⇒ Id 定了档名就定了）。</summary>
    public string PngPathOf(string id) => Path.Combine(_dir, $"pin-{id}.png");

    /// <summary>
    /// 读档。三种结果各自带一个后续动作：
    /// 缺档→空（首启，允许落盘）；坏 JSON→留 .bak＋空（磁盘上本就无可信数据）；瞬时占用→降级位＋空（Save 拒写防蒸发）。
    /// 读到的记录逐条滤 null／空 Id／重复 Id（保留首个），并归一化坐标与量程。
    /// </summary>
    public PinStoreData Load()
    {
        lock (_gate)
        {
            if (!File.Exists(JsonPath))
            {
                // 首启/档确被删除：空态是可信的，允许后续 Save 落盘。
                _loadDegraded = false;
                return new PinStoreData();
            }
            try
            {
                var data = JsonSerializer.Deserialize<PinStoreData>(File.ReadAllText(JsonPath));
                _loadDegraded = false;
                return Normalize(data);
            }
            catch (JsonException ex)
            {
                // 内容损坏（读得到字节、只是 JSON 非法）：磁盘上本就无可信数据，回退空并允许后续 Save 覆盖掉坏档。
                // 覆盖前把损坏原文留一份 .bak，给用户最后的挽回机会。
                _loadDegraded = false;
                StarLog.Error($"贴图记录损坏，已回退为空并备份原文件 ({JsonPath})", ex);
                try { File.Copy(JsonPath, JsonPath + ".bak", overwrite: true); } catch { }
                return new PinStoreData();
            }
            catch (Exception ex)
            {
                // 未能读到内容（瞬时锁定/权限）：磁盘数据其实完好，只是这一瞬拿不到。
                // 此时若让随后的 Save 落盘，会用空数据覆盖真实记录——最坏的数据丢失。置降级位拒写，
                // 锁定解除后的下一次成功 Load 自动清除该位。
                _loadDegraded = true;
                StarLog.WarnThrottled($"pins-read:{_dir}",
                    $"读取贴图记录失败（疑似被临时占用），本次不落盘以免覆盖真实数据 ({JsonPath})：{ex.Message}");
                return new PinStoreData();
            }
        }
    }

    /// <summary>
    /// 整档写盘。<b>返回是否真的落盘了</b>：降级态（读取曾被占用）与写/搬失败都返回 false，
    /// 调用方据此才能说实话。写盘先落 .tmp 再原子替换，失败时保留旧档不动。
    /// </summary>
    public bool Save(PinStoreData data)
    {
        lock (_gate)
        {
            // 降级态：内存里是读不到磁盘时回退出的空数据，绝不能拿它覆盖磁盘上其实完好的真实记录。
            if (_loadDegraded)
            {
                StarLog.WarnThrottled($"pins-degraded:{_dir}",
                    $"贴图记录处于降级态（读取曾被临时占用），跳过本次保存以保护磁盘数据 ({JsonPath})");
                return false;
            }

            var tmp = JsonPath + ".tmp";
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(tmp, JsonSerializer.Serialize(Normalize(data), new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, JsonPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                StarLog.Error($"保存贴图记录失败，已保留上一次内容不变 ({JsonPath})", ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }
    }

    /// <summary>"一次读档 → 就地改 → 最多一次落盘"的出口（保存模式＝Mutate，✅P-80 拍板口径）。</summary>
    /// <returns>是否真的写了盘。</returns>
    public bool Mutate(Action<PinStoreData> mutate)
    {
        lock (_gate)
        {
            var data = Load();
            mutate(data);
            return Save(data);
        }
    }

    /// <summary>
    /// 规范数据：滤 null／空 Id／重复 Id（保留首个），坐标与量程归一化。
    /// <b>这里刻意不限量</b>（P-10 同一教训）：本方法同时被 Load 与 Save 调用，任何一道截断都是
    /// "读一次少几条"——上限由名册 live 判据与恢复侧的 break 保证，读路径不替用户做决定。
    /// </summary>
    public static PinStoreData Normalize(PinStoreData? data)
    {
        data ??= new PinStoreData();
        data.Version = 1;
        data.Pins ??= new List<PinRecord>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<PinRecord>(data.Pins.Count);
        foreach (var rec in data.Pins)
        {
            if (rec is null) continue;
            rec.Id ??= string.Empty;
            if (rec.Id.Length == 0) continue;
            if (!seen.Add(rec.Id)) continue;          // 重复 Id：保留首个，其余丢弃（手改档/坏写入可达）
            rec.X = SafeCoordinate(rec.X);
            rec.Y = SafeCoordinate(rec.Y);
            rec.Zoom = CaptureGeometry.ClampZoom(rec.Zoom);                       // NaN/∞→1，否则夹进 0.2–5
            rec.Opacity = double.IsNaN(rec.Opacity) || double.IsInfinity(rec.Opacity)
                ? 1
                : Math.Clamp(rec.Opacity, 0.1, 1);                                 // 与 SetOpacity 的下限一致
            kept.Add(rec);
        }
        data.Pins = kept;
        return data;
    }

    /// <summary>坐标归一：NaN/∞（上一状态被算坏/手改档）回落到 0，而不是把 NaN 传进窗口摆位。</summary>
    private static double SafeCoordinate(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
}
