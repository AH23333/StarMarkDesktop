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
/// SettingsStore 的这一段——本地磁盘搜索那组：开关、索引根目录的取舍与上限。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>本地磁盘搜索总开关（默认关）。关闭时后台提权服务/Everything 不启动、不加载索引（0 内存），搜索也不含本地文件。</summary>
    public bool LoadLocalDiskSearchEnabled() => Load() is { } d && d.LocalDiskSearchEnabled == true;

    public void SaveLocalDiskSearchEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.LocalDiskSearchEnabled = enabled;
        Save(d);
    }

    // 「索引进库」的两颗持久化字段（FileIndexRoots / MaxFileIndexCount）连同它们的读写口已在批次 VQ 整条拆掉。
    // 老配置里残留的这两个 json 键会被反序列化直接忽略（System.Text.Json 默认行为），不需要迁移：
    // 它们今天没有任何读者，留着也不会被写到界面上。
    // 为什么不是"修好它"：那颗按钮只把配置落盘、真进库要等顶栏同步，而"引擎没跑／没配目录／目录当前不存在／
    // IPC 返回半截"四条出口全都无声，同步完还无条件弹一句「索引同步完成」——用户按那句话理解的是"已经进库"。
    // 与其给一条要同时补四处回报的批量入库路径，不如把它删到只剩"用户自己登记的那几行"（批次 VQ 的裁决）。
}
