#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Hotkeys;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——快捷键绑定的读写（含「能注册的那些」的过滤表）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>
    /// 快捷键绑定：默认 + 已保存的合并结果。合并规则本身在 <see cref="HotkeyBindings"/>
    /// （Core 层纯函数，可单测）：已保存的每一项都覆盖默认，而"空手势"是用户显式清掉该动作的
    /// 记号 ⇒ 清除必须写成空手势而不是删键，否则会被默认值复活。
    /// </summary>
    public IReadOnlyDictionary<string, HotkeyGesture> GetHotkeyBindings()
    {
        IReadOnlyDictionary<string, HotkeyGesture>? saved = null;
        var d = Load();
        if (d?.HotkeyBindingsJson is { } json)
        {
            try { saved = JsonSerializer.Deserialize<Dictionary<string, HotkeyGesture>>(json); }
            catch { /* 绑定 JSON 损坏：整表按默认，坏一次设置不该让程序起不来 */ }
        }
        return HotkeyBindings.MergeWithDefaults(saved);
    }

    /// <summary>保存快捷键绑定（动作 id → 手势）。</summary>
    public void SaveHotkeyBindings(IReadOnlyDictionary<string, HotkeyGesture> bindings)
    {
        var d = Load() ?? new SettingsData();
        d.HotkeyBindingsJson = JsonSerializer.Serialize(bindings);
        Save(d);
    }

    /// <summary>
    /// <b>注册给系统</b>的那份绑定＝磁盘上的绑定，过三道闸：<b>画布总开关</b>、<b>截屏总开关</b>与<b>当前会话态</b>（架构方案 §6.1）。
    /// 与 <see cref="GetHotkeyBindings"/> 分开是有原因的：设置页要照实显示用户绑了什么（包括功能关掉时的
    /// 那批），而注册表不能替一个关掉的功能、更不能在截图进行中替画布继续占着 Ctrl+Alt+字母。
    /// <para>
    /// 摘掉那批"只在画布里有用"的，<b>但 <c>canvas.toggle</c> 两条闸都留着</b>：按自己习惯那颗键的人
    /// 要听见一句"屏幕画布已在设置里关掉"或"截图进行中"，而不是一条什么也不发生的哑键。
    /// 截屏那三条反过来——<b>关掉时整条不注册</b>，因为 F1／F3 是裸功能键，替一个关掉的功能占着它们
    /// 等于让所有软件永久失去 Help／截图键（判据在 <see cref="CaptureGate.RegistersHotkey"/>，两族为何走法相反写在那儿）。
    /// </para>
    /// <para><b>这一处是投影的唯一出处</b>：启动、主窗菜单、设置页保存、设置页重试四个 ApplyBindings 入口
    /// 都调它，托盘与设置页读到的永远是"此刻真的生效的键"。判据本身在 Core 的 <see cref="HotkeyGate"/>
    /// 与 <see cref="CaptureGate"/>（纯函数、逐臂单测），这里只接线——在接线处现写布尔就是本项目定案禁止的写法（批次 WF-1）。</para>
    /// </summary>
    public IReadOnlyDictionary<string, HotkeyGesture> GetRegisterableHotkeyBindings()
    {
        var all = GetHotkeyBindings();
        var canvasEnabled = LoadCanvasEnabled();
        var captureEnabled = LoadCaptureEnabled();
        var stage = AnnotationHub.Stage;
        if (canvasEnabled && captureEnabled && !AnnotationHub.IsSheetActive) return all;
        var kept = new Dictionary<string, HotkeyGesture>();
        foreach (var (action, gesture) in all)
            if (HotkeyGate.ShouldRegister(action, canvasEnabled, stage)
                && CaptureGate.RegistersHotkey(action, captureEnabled)) kept[action] = gesture;
        return kept;
    }
}
