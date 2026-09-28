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
/// SettingsStore 的这一段——备份那组：附件是否进备份、自动备份开关与间隔。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>
    /// 导出备份时是否带上剪贴板图片本体（<b>默认开</b>，决议 §4）。
    /// <para>只影响<b>用户主动点「导出备份」</b>那一条路：恢复前快照与每日自动件不带附件，
    /// 所以这里存的不是"备份里有没有图"，而是"下一次手动导出带不带"。</para>
    /// </summary>
    public bool LoadBackupClipboardImagesEnabled() => Load()?.BackupClipboardImagesEnabled ?? true;

    public void SaveBackupClipboardImagesEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.BackupClipboardImagesEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 自动备份总开关（<b>默认开＝加这个开关之前的行为</b>）。
    /// 关掉之后启动那条后台任务连目录都不扫——"看着关了其实还在写文件"是最难发现的不诚实。
    /// </summary>
    public bool LoadAutoBackupEnabled() => Load()?.AutoBackupEnabled ?? true;

    /// <summary>自动备份间隔（小时）。缺省与脏值都走 <c>AutoBackupPolicy</c> 的回落，
    /// 界面上看到的档位与真拿去判定的小时数必须是同一个数。</summary>
    public int LoadAutoBackupIntervalHours()
        => StarMark.Core.Backup.AutoBackupPolicy.ClampInterval(
            Load()?.AutoBackupIntervalHours ?? StarMark.Core.Backup.AutoBackupPolicy.DefaultIntervalHours);

    /// <summary>
    /// 开关与间隔一次落盘：这两条说的是同一件事（自动备份怎么排程），
    /// 分开写就是"改了间隔但没改开关"这类半套状态的来源（护眼那四项同一条口径，P-43 也省写盘次数）。
    /// </summary>
    public void SaveAutoBackup(bool enabled, int intervalHours)
    {
        var d = Load() ?? new SettingsData();
        d.AutoBackupEnabled = enabled;
        d.AutoBackupIntervalHours = StarMark.Core.Backup.AutoBackupPolicy.ClampInterval(intervalHours);
        Save(d);
    }
}
