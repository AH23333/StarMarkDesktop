#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>
/// 检查更新要留下的那几格状态（宿主是设置页那份档；接口放 Core 好让服务能被逐字测试）。
/// <para>
/// <b>读一次、算完、整批写一次</b>（<see cref="Write"/>）：这几格说的是同一件事——"上次查的结果"。
/// 一格一次写盘就是"改了 A 没改 B"这类半套状态的来源（同一族：自动备份的开关与间隔一次落盘、
/// 设置页整档保存收敛 P-43）。
/// </para>
/// <para>
/// <b>结局存的是名字而不是序号</b>（见 <see cref="UpdateVerdict"/> 的宿主）：枚举顺序哪天动了，
/// 旧档里的数字会被解释成另一个结局，而"上次检查说的是什么"一旦串味，用户看到的是一句没发生过的事
/// （ItemType 那批立过的规矩）。
/// </para>
/// </summary>
public interface IUpdateStateStore
{
    /// <summary>当前状态（从没检查过时各格都是 null）。</summary>
    UpdateState Read();

    /// <summary>整批写回。</summary>
    void Write(UpdateState state);
}

/// <param name="AutoCheckEnabled">自动检查的总开关（关掉之后程序永不自己上网问）。</param>
/// <param name="LastProbeUtc">上一次"真的拿到对方答复"的时刻；null＝从没问过。
/// 离线／超时<b>不更新它</b>，所以一联网就会自己补问，不需要人动手。</param>
/// <param name="LastVerdict">上一次的结局（null＝从没检查过）。界面打开时照着它说话，不为此发请求。</param>
/// <param name="LastRemoteTag">上一次问到的远端标签（配合上一格拼出那句话）。</param>
/// <param name="AnnouncedTag">已经为哪一版出过声。<b>只记标签、不记时间</b>：记住的是"这一版通知过了"。</param>
public sealed record UpdateState(
    bool AutoCheckEnabled,
    DateTimeOffset? LastProbeUtc = null,
    UpdateVerdict? LastVerdict = null,
    string? LastRemoteTag = null,
    string? AnnouncedTag = null);

/// <summary>
/// 一次检查的完整答复。<see cref="Text"/> 是给界面与日志用的那一句（出自 <see cref="UpdatePolicy.Describe"/>），
/// <see cref="PageUrl"/> 是界面那颗「打开下载页」要用的地址——<b>由 Core 按配置里的仓库拼出来</b>，
/// 不是远端回的那条 <c>html_url</c>（理由见 <see cref="UpdatePolicy.ReleasePageUrl"/>）。
/// </summary>
public sealed record UpdateReport(
    UpdateVerdict Verdict, string? RemoteTag, string PageUrl,
    DateTimeOffset CheckedUtc, bool Announced, string Text);

/// <summary>
/// 检查更新的编排：<b>问 → 判 → 记 → （只在必要时）出声</b>（批次 UE）。
/// <para>
/// 三条设计上的取舍都写在这儿，免得下次被"顺手优化"掉：
/// ① <b>只读，不下载不替换</b>。这条线唯一往磁盘上写的东西是上面那几格状态；
///    "就地装新版"要覆盖正在运行的程序目录，前置条件（发布管线＋资产哈希）都还没落地，
///    已登记在 <c>docs/待决策事项.md</c>。
/// ② <b>同一时刻只允许一发在飞</b>（<see cref="CheckAsync"/> 复用未完成的那次）。
///    没有这条，连点"现在检查"会并发好几个请求——匿名配额每小时 60 次，
///    自己就能把自己打到 <see cref="UpdateVerdict.RateLimited"/>，然后用户看到的是"这功能老失败"。
/// ③ <b>失败不静默也不吵</b>：结局一律写进状态（设置页那张卡看得见、说得清原因），
///    但只有"确实有新版且这一版还没提醒过"才弹右下角的卡。
/// </para>
/// </summary>
public sealed class UpdateService
{
    private readonly IReleaseSource _source;
    private readonly IUpdateStateStore _state;
    private readonly Func<string?> _localVersion;
    private readonly Func<string?> _environment;
    private readonly Clock _now;

    private Task<UpdateReport>? _inFlight;
    private readonly object _gate = new();

    /// <summary>取当前时刻的缝（测试里给固定值；默认走 UTC 系统时钟）。</summary>
    public delegate DateTimeOffset Clock();

    public UpdateService(IReleaseSource source, IUpdateStateStore state,
        Func<string?>? localVersion = null, Func<string?>? environment = null, Clock? now = null)
    {
        _source = source;
        _state = state;
        _localVersion = localVersion ?? (() =>
            AppVersion.TryReadLocal(out var v) ? AppVersion.Describe(v) : null);
        _environment = environment ?? (() => Environment.GetEnvironmentVariable(UpdatePolicy.RepositoryEnvironmentVariable));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 自动检查开关。设置页按一下就当场生效：开着＝下一次到点就查（不必重启），
    /// 关掉＝从此一次网也不上（"看着关了其实还在问"是最难发现的不诚实）。
    /// </summary>
    public bool AutoCheckEnabled
    {
        get => _state.Read().AutoCheckEnabled;
        set
        {
            var current = _state.Read();
            _state.Write(current with { AutoCheckEnabled = value });
        }
    }

    /// <summary>上次留下的那条答复（只为界面显示；<b>不联网</b>——打开设置页不该顺手发一个请求）。</summary>
    public UpdateReport? LastReport()
    {
        var stored = _state.Read();
        if (stored.LastVerdict is not { } verdict) return null;
        var release = stored.LastRemoteTag is { Length: > 0 } tag
            ? new RemoteRelease(tag, null, false, null) : null;
        return new UpdateReport(verdict, stored.LastRemoteTag, PageUrl(stored.LastRemoteTag),
            stored.LastProbeUtc ?? DateTimeOffset.MinValue, Announced: false,
            UpdatePolicy.Describe(verdict, release, LocalText()));
    }

    /// <summary>发布页地址：只认配置里那个仓库，远端回的连接串一个字节都不参与（见 <see cref="UpdatePolicy.ReleasePageUrl"/>）。</summary>
    public string PageUrl(string? remoteTag)
        => UpdatePolicy.ReleasePageUrl(UpdatePolicy.RepositoryOf(_environment()), remoteTag);

    /// <summary>到点且开关开着才问一次；回来是 null 表示"这次不该问"（不是失败）。</summary>
    public async Task<UpdateReport?> TryAutoProbeAsync(CancellationToken ct = default)
    {
        var stored = _state.Read();
        if (!UpdatePolicy.ShouldAutoProbe(stored.AutoCheckEnabled, stored.LastProbeUtc, _now())) return null;
        return await CheckAsync(manual: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 问一次并把结果记下。<paramref name="manual"/>＝他点了"现在检查"：
    /// 手动检查<b>绕过节奏</b>（他要立刻知道答案），但仍照样只在"有新版"时弹卡——
    /// 手动那一发他自己已经在看了，再弹一张卡是叠噪。
    /// </summary>
    public Task<UpdateReport> CheckAsync(bool manual, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_inFlight is { } running && !running.IsCompleted) return running;
            _inFlight = RunAsync(manual, ct);
            return _inFlight;
        }
    }

    private async Task<UpdateReport> RunAsync(bool manual, CancellationToken ct)
    {
        var local = LocalText();
        var repository = UpdatePolicy.RepositoryOf(_environment());
        ReleaseProbeResult probe;
        try
        {
            probe = await _source.ProbeAsync(repository, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消不是"检查失败"：不写状态、不出声，原样交回（P-55 那条"取消不许被兜底吞掉"同一口径）
            throw;
        }
        catch (Exception ex)
        {
            // 抓取器本该自己分类；真漏出来的一种按"连不上"处理并写下类型——
            // 静默当成失败会让下一次又是同一句话，而这里要的是"说得出为什么没查到"。
            StarLog.Warn($"[更新] 检查异常（按连不上处理）：{ex.GetType().Name} {ex.Message}");
            probe = new ReleaseProbeResult(ReleaseProbeStatus.NotReachable, null, ex.GetType().Name);
        }

        var verdict = UpdatePolicy.Classify(probe, local);
        var now = _now();
        var release = probe.Release;
        var stored = _state.Read();

        // 这一版值不值得"告诉他"（第一次见到这个标签的新版）。
        var tells = UpdatePolicy.ShouldAnnounce(verdict, release?.Tag, stored.AnnouncedTag);
        // 弹卡只给自动那一发：手动那一发他正盯着屏幕，结果已经在他点的那下里了，再弹一张是叠噪。
        var showCard = tells && !manual;
        // 但两种都算"已经告诉他了"，一起把标签记下来——否则手动查完 1.1.0，第二天开机还会为同一版弹一次卡。
        var recordAnnounced = tells && release is not null;

        // 「问过」这件事只在真的拿到对方答复时才记（见 UpdatePolicy.CountsAsProbed 那段理由）
        var updated = stored with
        {
            LastProbeUtc = UpdatePolicy.CountsAsProbed(verdict) ? now : stored.LastProbeUtc,
            LastVerdict = UpdatePolicy.CountsAsProbed(verdict) ? verdict : stored.LastVerdict,
            LastRemoteTag = UpdatePolicy.CountsAsProbed(verdict) ? release?.Tag : stored.LastRemoteTag,
            AnnouncedTag = recordAnnounced ? release!.Tag : stored.AnnouncedTag,
        };
        _state.Write(updated);

        var text = UpdatePolicy.Describe(verdict, release, local);
        StarLog.Info($"[更新] {verdict}：{text}（问的仓库 {repository}，本机 {local ?? AppVersion.Unknown}"
            + (release is null ? "" : $"，远端 {release.Tag}")
            + (probe.Detail is null ? "" : $"，对方补话：{probe.Detail}") + "）");
        return new UpdateReport(verdict, release?.Tag, UpdatePolicy.ReleasePageUrl(repository, release?.Tag),
            now, showCard, text);
    }

    private string? LocalText()
        => AppVersion.TryParse(_localVersion(), out var v) ? AppVersion.Describe(v) : null;
}
