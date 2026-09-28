#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Health;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——位置与尺寸的**读写**这一头：初值解析（含跨屏夹取）、落进 widgets.json、联动移动时的落点写入。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetWindow
{

    // ───────────────────────── 窗口样式/位置 ─────────────────────────

    private void ApplyInitialBounds()
    {
        var (x, y, w, h) = ResolveInitialRect();
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    /// <summary>
    /// 解析组件初始物理像素矩形（每显示器拓扑布局，Phase B）：
    /// 优先用 <see cref="WidgetInstanceConfig.MonitorId"/> + DIP 偏移按该显示器当前 DPI 重新换算；
    /// 显示器已断开（MonitorId 找不到）或旧实例无记录时，回退物理像素并做越界回收（落到主显示器工作区）。
    /// </summary>
    private (int X, int Y, int W, int H) ResolveInitialRect()
    {
        var scale = WindowInterop.GetScale(this);

        if (_config.Width > 0 && _config.Height > 0)
        {
            // ── 拓扑记忆：有显示器记录 → 按该显示器当前 DPI 还原 ──
            if (!string.IsNullOrEmpty(_config.MonitorDevice) &&
                WindowInterop.FindMonitorWorkAreaByDevice(_config.MonitorDevice) is { } work)
            {
                var s = WindowInterop.GetMonitorScaleByDevice(_config.MonitorDevice);
                var left = work.X + (int)(_config.MonitorLeft * s);
                var top = work.Y + (int)(_config.MonitorTop * s);
                var w = (int)(_config.MonitorWidth * s);
                var h = (int)(_config.MonitorHeight * s);
                return ClampToWorkArea(left, top, w, h, work);
            }

            // ── 旧实例 / 显示器已断开：物理像素 + 越界回收 ──
            var px = (int)_config.X;
            var py = (int)_config.Y;
            var pw = (int)_config.Width;
            var ph = (int)_config.Height;
            if (WindowInterop.IsOffScreen(new RectInt32(px, py, pw, ph)))
            {
                // 原显示器没了：落到主显示器工作区，避免组件消失
                var primary = WindowInterop.PrimaryWorkArea();
                px = primary.X + 24;
                py = primary.Y + 24;
            }
            var target = WindowInterop.MonitorWorkAreaContaining(px, py, pw, ph) ?? WindowInterop.PrimaryWorkArea();
            return ClampToWorkArea(px, py, pw, ph, target);
        }

        // 无尺寸记录：用类型默认尺寸（物理像素），夹到当前显示器
        var w0 = (int)(WidgetStorage.DefaultWidth(_kind) * scale);
        var h0 = (int)(WidgetStorage.DefaultHeight(_kind) * scale);
        var index = (int)_kind;
        var x0 = (int)((120 + index * 28) * scale);
        var y0 = (int)((90 + index * 28) * scale);
        return ClampToWorkArea(x0, y0, w0, h0, WindowInterop.GetWorkArea(this));
    }

    private static (int X, int Y, int W, int H) ClampToWorkArea(int x, int y, int w, int h, RectInt32 work)
    {
        if (x < work.X) x = work.X + 8;
        if (y < work.Y) y = work.Y + 8;
        if (x + w > work.X + work.Width) x = Math.Max(work.X + 8, work.X + work.Width - w - 8);
        if (y + h > work.Y + work.Height) y = Math.Max(work.Y + 8, work.Y + work.Height - h - 8);
        return (x, y, w, h);
    }

    /// <summary>把本窗口当前几何单独落盘（一次读 + 最多一次写）。</summary>
    internal void PersistBounds() => _storage.Mutate(WriteBoundsInto);

    /// <summary>
    /// 把本窗口当前几何<b>写进给定的存档</b>——不读盘、不落盘。整批窗口共用一次读档一次落盘时走这里
    /// （<see cref="WidgetManager"/> 的 PersistBoundsFor，以及本类的 <see cref="PersistPositionsAfterDrag"/>）。
    /// </summary>
    /// <returns>有没有写到东西。<b>实例已从存档里消失（正在被移除）时返回 false，一次盘都不该写</b>。</returns>
    internal bool WriteBoundsInto(WidgetStoreData data)
    {
        try
        {
            var r = WindowInterop.GetWindowRect(this);
            if (r.Width <= 0 || r.Height <= 0) return false;
            var inst = data.Instances.FirstOrDefault(i => i.Id == _instanceId);
            if (inst is null) return false;

            // 收起为胶囊时，窗口当前显示的是「胶囊」而非展开态：
            //  - 展开态位置/尺寸必须保持（用 _expandedRect，其次 _config），否则点击展开会跳到屏幕边缘；
            //  - 胶囊停靠位单独写入 CapsuleX/CapsuleY（供下次启动/悬停收回稳定回到原位）。
            if (_chromeMode == WidgetChromeMode.Compact)
            {
                var scale = WindowInterop.GetScale(this);
                var capH = (int)(36 * scale);
                var ex = _expandedRect
                         ?? new RectInt32((int)_config.X, (int)_config.Y, (int)_config.Width, (int)_config.Height);
                inst.X = ex.X;
                inst.Y = ex.Y;
                inst.Width = ex.Width;
                inst.Height = ex.Height;
                // 同步胶囊停靠位（拖动胶囊后此处即最新位置，悬停收回/重启都回到这里）
                _capsuleRect = new RectInt32(r.X, r.Y, _capsuleWidth > 0 ? _capsuleWidth : r.Width, capH);
                inst.CapsuleX = r.X;
                inst.CapsuleY = r.Y;
            }
            else
            {
                inst.X = r.X;
                inst.Y = r.Y;
                inst.Width = r.Width;
                inst.Height = r.Height;
            }
            inst.Topmost = _config.Topmost;
            inst.ChromeMode = _chromeMode;
            inst.PrivacyMode = _config.PrivacyMode;

            // 每显示器拓扑：记录所在显示器设备名 + DIP 偏移/尺寸（恢复时按当前 DPI 还原）
            try
            {
                var hwnd = WindowInterop.GetHwnd(this);
                var (device, work, s) = WindowInterop.GetMonitorForWindow(hwnd);
                if (!string.IsNullOrEmpty(device))
                {
                    inst.MonitorDevice = device;
                    inst.MonitorLeft = (r.X - work.X) / s;
                    inst.MonitorTop = (r.Y - work.Y) / s;
                    var dipW = (_chromeMode == WidgetChromeMode.Compact ? _config.Width : r.Width) / s;
                    var dipH = (_chromeMode == WidgetChromeMode.Compact ? _config.Height : r.Height) / s;
                    inst.MonitorWidth = dipW;
                    inst.MonitorHeight = dipH;
                }
            }
            catch { /* 拿不到显示器信息时不写拓扑字段，回退物理像素路径 */ }

            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error($"组件位置持久化失败 ({_kind})", ex);
            return false;
        }
    }

    /// <summary>
    /// 按给定位置 / 尺寸重新摆位（套用布局方案时用）。
    /// 注意：<see cref="WidgetStorage.Load"/> 每次反序列化都是新对象，窗口缓存的 _config
    /// 不会自动跟着变，因此必须由调用方显式下发并回写。
    /// </summary>
    public void ApplyBounds(double x, double y, double width, double height, bool topmost)
    {
        _config.X = x;
        _config.Y = y;
        _config.Width = width;
        _config.Height = height;
        _config.Topmost = topmost;

        if (width <= 0 || height <= 0) return;
        // 关键：布局方案下发的尺寸/位置必须先写进 _expandedRect，再走 ApplyChromeMode。
        // 否则 ApplyChromeMode(Standard) → ExpandToNormal() 会用构造期缓存的陈旧 _expandedRect
        // 把刚下发的尺寸/位置覆盖掉，表现为「应用布局后组件又跳回原来的大小/位置。
        _expandedRect = new RectInt32((int)x, (int)y, (int)width, (int)height);
        AppWindow.MoveAndResize(new RectInt32((int)x, (int)y, (int)width, (int)height));
        ApplyTopmost();
        // 重新套用外壳模式：保证布局方案下发的尺寸/位置与胶囊/隐藏态一致（不会引起递归）
        ApplyChromeMode(_chromeMode);
    }
}
