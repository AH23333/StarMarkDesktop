#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StarMark.Abstractions.Updates;

/// <summary>
/// 替换那一步的<b>共同语言</b>：主程序怎么把"该换什么"交给更新器，更新器怎么把"换成了没有"说回去。
/// <para>放在 Abstractions 的理由与 <see cref="UpdateAssets"/> 同一颗：这件事有<b>两边</b>要用
/// ——编参数在 Core 的启动侧，解参数在更新器那一层。两侧各写一份参数格式，漂开的表现是
/// "更新器说不懂参数、界面上什么都没说、日志里一句拒绝都没有"。</para>
/// <para>这一颗只讲"话"（参数、结局、时限）。<b>动手</b>的那两件在 Core：
/// <c>UpdaterEngine</c>（换）与 <c>UpdaterLauncher</c>（把它请出去跑）——判据都要能被单测跑到。</para>
/// </summary>
public static class UpdaterContract
{
    /// <summary>等主进程退出的上限。太短会撞上新版本"退出要收尾"的那几秒（批次 PL 那条链），
    /// 太长只是让人多等——它不是正确性参数，是舒适参数。</summary>
    public static readonly TimeSpan ParentExitCeiling = TimeSpan.FromSeconds(60);

    /// <summary>轮询间隔。</summary>
    public static readonly TimeSpan ParentExitPoll = TimeSpan.FromMilliseconds(200);

    /// <summary>"这颗文件此刻不许动"时自己重试的次数与间隔（P-54：不许把重试推给用户）。</summary>
    public const int MoveAttempts = 8;

    public static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>参数名。编解码都从这里取，两侧不许各写一份字面串。</summary>
    public const string PidFlag = "--pid";
    public const string NewFlag = "--new";
    public const string TargetFlag = "--target";

    /// <summary>把指令编成命令行参数。<b>数字两侧都走不变文化</b>（P-122 那一族的口径）。</summary>
    public static IReadOnlyList<string> Encode(UpdaterRequest request)
        => new[]
        {
            PidFlag, request.ParentPid.ToString(CultureInfo.InvariantCulture),
            NewFlag, request.StagedTree, TargetFlag, request.InstallDir,
        };

    /// <summary>
    /// 解命令行参数并<b>当场验一遍形</b>——<see cref="Validate"/> 说的那些坏在这里先说一次。
    /// 更新器最坏的一种坏是"参数没解出来就开始挪盘上的东西"。
    /// </summary>
    public static bool TryDecode(IReadOnlyList<string> argv, out UpdaterRequest? request, out string? error)
    {
        request = null;
        error = null;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < argv.Count; i++)
        {
            if (!argv[i].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"认不出的参数形状：{argv[i]}";
                return false;
            }
            if (i + 1 >= argv.Count)
            {
                error = $"{argv[i]} 后面没有值";
                return false;
            }
            if (!seen.TryAdd(argv[i], argv[i + 1]))
            {
                error = $"{argv[i]} 出现了两次";
                return false;
            }
            i++;
        }
        if (seen.Count != 3 || !seen.ContainsKey(PidFlag) || !seen.ContainsKey(NewFlag) || !seen.ContainsKey(TargetFlag))
        {
            error = $"要且只不要 {PidFlag}/{NewFlag}/{TargetFlag} 三对，实际给了 {seen.Count} 对";
            return false;
        }
        if (!int.TryParse(seen[PidFlag], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            error = $"{PidFlag} 不是一个进程号";
            return false;
        }

        request = new UpdaterRequest(pid, seen[NewFlag], seen[TargetFlag]);
        if (Validate(request, out error)) return true;
        request = null;
        return false;
    }

    /// <summary>
    /// 一条指令要成立，得同时满足这几条——每一条都对应一种"更新器自己会把自己坑到"的形状：
    /// 不在一块盘上就不是改名而是复制（<b>那就不再是原子的</b>）；新树在旧树里面，挪完旧树会把它一起埋掉；
    /// 不像摊好的树，就是有人把随便一个目录指到了安装目录上。
    /// </summary>
    public static bool Validate(UpdaterRequest request, out string? error)
    {
        error = null;
        if (request.ParentPid <= 0) { error = "没有要等的那个进程"; return false; }
        if (!IsOwnPath(request.StagedTree) || !IsOwnPath(request.InstallDir))
        {
            error = "两棵树的路径必须是绝对、且已经归一好的那种";
            return false;
        }
        if (!request.StagedTree.EndsWith(UpdateAssets.NewTreeSuffix, StringComparison.Ordinal))
        {
            error = $"给来的新树不像摊好的那棵（该以 {UpdateAssets.NewTreeSuffix} 结尾）";
            return false;
        }
        var rootNew = Path.GetPathRoot(request.StagedTree);
        var rootOld = Path.GetPathRoot(request.InstallDir);
        if (string.IsNullOrEmpty(rootNew) || !rootNew.Equals(rootOld, StringComparison.OrdinalIgnoreCase))
        {
            error = "新树与安装目录不在同一块盘上——跨盘只能复制，而这一层谈得上一句\"换好了\"的走法只有改名";
            return false;
        }
        if (Contains(request.InstallDir, request.StagedTree))
        {
            error = "新树在旧树里面：挪完旧树会把它一起埋掉";
            return false;
        }
        if (Contains(request.StagedTree, request.InstallDir))
        {
            error = "旧树在新树里面";
            return false;
        }
        if (string.Equals(request.StagedTree, request.InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            error = "新树与旧树是同一颗";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 结局对应的退出码。<b>0 只给 <see cref="UpdaterOutcome.Success"/>，各格之间不重号</b>：
    /// 这一层是进程边界，读它的是日志与人（还有发布那次真机演示里的一句 <c>$LASTEXITCODE</c>）。
    /// <b>兜底那一格是 9，与 <see cref="UpdaterOutcome.Unknown"/>（8）不是同一件事</b>——
    /// 前者是"有人新增了一格而这里忘了配"，后者是"更新器真的不知道走到了哪一步"。
    /// </summary>
    public static int ExitCodeFor(UpdaterOutcome outcome) => outcome switch
    {
        UpdaterOutcome.Success => 0,
        UpdaterOutcome.InvalidRequest => 2,
        UpdaterOutcome.ParentStillRunning => 3,
        UpdaterOutcome.OldTreeNotMoved => 4,
        UpdaterOutcome.RolledBack => 5,
        UpdaterOutcome.RolledBackButNotRunning => 6,
        UpdaterOutcome.RollbackFailed => 7,
        UpdaterOutcome.Unknown => 8,
        // 9 是"新增一格而这里忘了配"的专用兜底（有闸门钉着不许被真格子占用），所以这一格往后一位。
        UpdaterOutcome.UntouchedButNotRunning => 10,
        _ => 9,
    };

    /// <summary>这条串是不是"它自己"：绝对、无尾分隔符、没有 <c>..</c> 留着让后面某一步去化简。</summary>
    private static bool IsOwnPath(string path)
    {
        if (!Path.IsPathRooted(path)) return false;
        // "字符串本身不是它自己"要在这里拒：后面每一步都拿这颗串比路径，
        // 比不上的坏形状是"挪到哪棵都不知道"。
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Length > 0 && string.Equals(full, path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string outer, string inner)
        => inner.StartsWith(
            Path.TrimEndingDirectorySeparator(outer) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 一次替换指令。<b>里面刻意没有"起哪个 exe"这一格</b>——那个名字由
/// <see cref="UpdateAssets.EntryExeName"/> 一处定死，不接命令行给的：
/// 能被参数改动的东西，就等于能被改成"换完之后再替你启动一个别的文件"。
/// </summary>
public sealed record UpdaterRequest(int ParentPid, string StagedTree, string InstallDir);

/// <summary>
/// 替换那一步的结局。<b>每一格说自己那一句</b>（措辞在 Core 的 <c>UpdatePolicy.Describe</c>，#232 那条纪律）。
/// <para>口径：<b>这些格子说的是"盘上现在是什么状态"</b>，不是"哪一步摔的"——哪一步的因由写在
/// <see cref="UpdaterResult.Detail"/> 里。把"哪一步"也做成格子，同一件现状就有两句话可说，
/// 而界面只能说一句（与 #232 那条"措辞要分拆在各自的臂上"是同一件事的另一面：该合的也要合）。</para>
/// </summary>
public enum UpdaterOutcome
{
    /// <summary>换好了，新版本已经起来了。</summary>
    Success,

    /// <summary>这条指令本身不成立（路径不对、跨了盘、不像一棵摊好的树）——一个字节都没动。</summary>
    InvalidRequest,

    /// <summary>等不到那个进程退出——一个字节都没动。</summary>
    ParentStillRunning,

    /// <summary>旧目录挪不动——一个字节都没动。
    /// 被别的进程占着、ACL 不许改名、这台机器不让这颗目录改名，引擎分不出这三样，
    /// 所以这一格与它那句话说出口时<b>只许讲"挪不动"</b>：把成因写进格子，就是把猜写进事实。</summary>
    OldTreeNotMoved,

    /// <summary>没换成，但已退回原来那一版，而且它重新起来了（盘上＝改动之前）。</summary>
    RolledBack,

    /// <summary>退回原来那一版了，但旧版也没能起来：磁盘是完好的，程序没开着。</summary>
    RolledBackButNotRunning,

    /// <summary>安装目录一个字节都没动，而<b>连原来那一版也没能重新起来</b>：程序此刻没开着。
    /// <para>与 <see cref="RolledBackButNotRunning"/> 是同一对的两半——那一格是"动过之后退回来了"，
    /// 这一格是"压根没动过"。分两格是因为用户这一刻要做的决定不同：前者盘上刚被我们碰过（要留意日志），
    /// 后者盘上还是他原来那一版（直接再开一次就完事）。而两格都不许被折回"什么都没动"那一族，
    /// 因为那一族的每一句都默认程序还在屏幕上（#234：把"还在跑"演成"没影响"）。</para></summary>
    UntouchedButNotRunning,

    /// <summary>回滚也失败。<b>安装目录此刻不完整</b>——这一族里唯一一句必须让人去重装的话。</summary>
    RollbackFailed,

    /// <summary>更新器自己也没说清走到了哪一步（引擎之外冒出来的异常）。
    /// <b>这一格不许被折进"已退回原样"</b>：那句是安慰话，而这里我们真的不知道——
    /// 所以 <see cref="UpdaterResult.Detail"/> 要带上"旧树还在不在"这一条看得见的事实。</summary>
    Unknown,
}

/// <summary>替换引擎给启动它那一侧的交代（更新器也把它原样写进日志）。</summary>
public sealed record UpdaterResult(UpdaterOutcome Status, string? Detail = null)
{
    public bool IsSuccess => Status == UpdaterOutcome.Success;
}

/// <summary>
/// 启动侧的格子——问的是另一件事：<b>活有没有交出去</b>。
/// <para>不并入 <see cref="UpdaterOutcome"/> 是因为那一族说的是"盘上现在是什么状态"，
/// 而这里最顺利那一格（<see cref="Started"/>）说的是"我撒手了，接下来怎么样我还不知道"。
/// 两种量纲并成一族，界面上就多出一句谎：把"交出去了"演成"换好了"。</para>
/// </summary>
public enum LaunchStatus
{
    /// <summary>更新器已经带着这条指令跑起来了（替换成不成，接下来由它说）。</summary>
    Started,

    /// <summary>这条指令本身不成立——没去起任何东西。</summary>
    InvalidRequest,

    /// <summary>更新器没能跑起来（这一版没带那颗文件、副本没能落盘、或它不肯起）——什么都没动。</summary>
    NotStarted,
}

/// <summary>启动侧的交代。<see cref="RunnerPath"/> 只有 <see cref="LaunchStatus.Started"/> 时才非空。</summary>
public sealed record LaunchResult(LaunchStatus Status, string? Detail = null, string? RunnerPath = null)
{
    public bool IsStarted => Status == LaunchStatus.Started;
}

/// <summary>
/// 更新器用到的那几处落点。<b>唯一一条判据：更新器不许站在要被它换掉的那棵树里。</b>
/// </summary>
public static class UpdaterPaths
{
    /// <summary>更新器那颗 exe 的名字。</summary>
    public const string UpdaterExeName = "StarMark.Updater.exe";

    /// <summary>
    /// 更新器<b>在安装目录里的家</b>：只带一颗 exe 不够——它要自己的 <c>.dll</c>、
    /// <c>runtimeconfig.json</c> 和它引用的那几颗程序集。所以发布产物里它独占一个目录，
    /// 复制的时候整带复制："少带一颗就永远起不来"这种坏法在结构上不存在
    /// （列清单就会漏，而漏的那一次只在用户机器上出现——#189/#193 那一族）。
    /// </summary>
    public const string UpdaterFolderName = "Updater";

    /// <summary>本机可变数据那一块（与日志同一块地方，不是安装目录）。</summary>
    public static string LocalHome
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMark");

    /// <summary>更新器跑的那一族的根：<b>安装目录之外</b>，否则它一边在里面跑一边想把里面整个挪走。</summary>
    public static string UpdaterHome => Path.Combine(LocalHome, "Updater");

    /// <summary>安装目录里那一带头的完整路径（复制的来源）。</summary>
    public static string InInstallDir(string installDir)
        => Path.Combine(Path.TrimEndingDirectorySeparator(installDir), UpdaterFolderName);

    /// <summary>
    /// 这一轮跑的那一份副本放哪儿。<b>按主进程的号分文件夹，而不是覆盖同一颗</b>：
    /// 上一次那个更新器可能还在跑（它就是为主进程而跑的），而 Windows 不许覆盖也不许改名一个正在运行的 exe——
    /// 用固定名字的话，"两个人同时点更新"这种常见情形会变成"永远复制不过去"。
    /// </summary>
    public static string RunnerHomeFor(int parentPid)
        => Path.Combine(UpdaterHome, parentPid.ToString(CultureInfo.InvariantCulture));

    /// <summary>这一轮那颗更新器的完整路径。</summary>
    public static string RunnerExeFor(int parentPid) => Path.Combine(RunnerHomeFor(parentPid), UpdaterExeName);

    /// <summary>
    /// 摊包与替换用的暂存根：<b>安装目录的旁边</b>，不是 %TEMP%。
    /// <para>为什么不用临时目录：更新器本来就要在安装目录的父目录里改名（旧树→<c>_old</c>），
    /// 那一块不能写就只能承认"这台机器上装不了新版"；把暂存放去别处不会少要一份权限，
    /// 反而多开一种坏——跨卷的"挪过去"是复制，而复制没有原子性可言
    /// （见 <see cref="UpdaterContract.Validate"/>）。而 <c>%TEMP%</c> 在另一块盘上是常态，不是意外。</para>
    /// </summary>
    public static string StagingRootFor(string installDir)
        => Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(installDir))
            ?? installDir, ".StarMarkUpdate");
}
