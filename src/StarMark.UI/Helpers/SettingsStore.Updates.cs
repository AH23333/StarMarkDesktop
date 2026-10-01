#nullable enable
using System;
using StarMark.Core.Updates;

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——检查更新那五格（批次 UE）。
/// <para>
/// 这里是 <see cref="IUpdateStateStore"/> 在真宿主上的<b>唯一</b>实现：Core 那边只管"读一次、算完、写一次"，
/// 怎么落到 settings.json、脏值怎么回落都只住在这几行。
/// </para>
/// <para>
/// 两处刻意的写法：① <b>结局按名字存</b>（认不出来的名字一律当"没查过"，而不是猜一个最接近的枚举值——
/// 猜错就是界面在说一件没发生过的事）；② <b>时间戳按 UTC 秒存</b>，与自动备份那条同口径，
/// 于是调本地时钟不会把"上次几点查的"挪走。
/// </para>
/// </summary>
public sealed partial class SettingsStore : IUpdateStateStore
{
    /// <summary>读当前状态（从没检查过时那几格都是 null，开关回默认＝开）。</summary>
    public UpdateState LoadUpdateState()
    {
        var d = Load() ?? new SettingsData();
        return new UpdateState(
            d.UpdateAutoCheckEnabled ?? true,
            UnixToUtc(d.UpdateLastProbeUnix),
            ParseVerdict(d.UpdateLastVerdict),
            d.UpdateLastRemoteTag,
            d.UpdateAnnouncedTag);
    }

    /// <summary>整批写回（见 <see cref="IUpdateStateStore"/>：这几格说的是同一件事，不许分开落盘）。</summary>
    public void SaveUpdateState(UpdateState state)
    {
        var d = Load() ?? new SettingsData();
        d.UpdateAutoCheckEnabled = state.AutoCheckEnabled;
        d.UpdateLastProbeUnix = state.LastProbeUtc?.ToUnixTimeSeconds();
        d.UpdateLastVerdict = state.LastVerdict?.ToString();
        d.UpdateLastRemoteTag = state.LastRemoteTag;
        d.UpdateAnnouncedTag = state.AnnouncedTag;
        Save(d);
    }

    UpdateState IUpdateStateStore.Read() => LoadUpdateState();

    void IUpdateStateStore.Write(UpdateState state) => SaveUpdateState(state);

    private static DateTimeOffset? UnixToUtc(long? seconds)
        => seconds is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null;

    /// <summary>
    /// 只认名字，不认数字：<c>Enum.TryParse</c> 会把落盘里的 <c>"3"</c> 按<b>序号</b>认成一个从没发生过的结局，
    /// 于是界面上一句"上次检查说的是什么"就在替一条不存在的事说话（ItemType／WidgetChromeMode 那批立的规矩）。
    /// 名字对不上就当"没查过"——宁可空着，也不要猜。
    /// </summary>
    private static UpdateVerdict? ParseVerdict(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || char.IsAsciiDigit(name[0])) return null;
        return Enum.TryParse<UpdateVerdict>(name, ignoreCase: false, out var parsed)
            && Enum.IsDefined(typeof(UpdateVerdict), parsed) ? parsed : null;
    }
}
