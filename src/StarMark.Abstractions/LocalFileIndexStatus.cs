#nullable enable
namespace StarMark.Abstractions;

/// <summary>
/// 本地磁盘搜索（全盘文件索引）生命周期阶段。纯逻辑，UI 与后台服务据此决定
/// 是否发查询、是否弹授权、是否显示"首次扫描进行中"提示。
/// </summary>
public enum LocalFileIndexPhase
{
    /// <summary>用户未开启该功能（默认）。此时绝不启动提权服务、绝不加载索引，内存占用为 0。</summary>
    Disabled,

    /// <summary>已开启但后台提权服务尚未安装/启动：需要一次 UAC 授权。此阶段不应显示"扫描等待"提示。</summary>
    NeedsSetup,

    /// <summary>服务已安装、正在拉起或连接中：尚未就绪。</summary>
    Starting,

    /// <summary>服务在线但首次全盘索引仍在建立：此时可显示"首次扫描进行中，请稍候"。</summary>
    Indexing,

    /// <summary>索引就绪：可正常发查询。</summary>
    Ready,
}

/// <summary>生命周期判定的输入事实（由 UI/服务探测填充）。</summary>
/// <param name="Enabled">用户是否开启本地磁盘搜索。</param>
/// <param name="ServiceInstalled">提权后台服务是否已安装。</param>
/// <param name="ServiceRunning">服务是否正在运行且可 IPC。</param>
/// <param name="IndexReady">全盘索引是否已建完可查。</param>
public sealed record LocalFileIndexFacts(
    bool Enabled,
    bool ServiceInstalled,
    bool ServiceRunning,
    bool IndexReady);

/// <summary>
/// 从 <see cref="LocalFileIndexFacts"/> 推导阶段与各门控决策的纯函数集合。
/// 编码需求约束：① 默认关时一切不动（0 查询、0 内存）；② 只有真正"索引进行中"才提示等待，
/// 未授权（NeedsSetup）阶段不提示扫描；③ 仅 Ready 才向数据源发查询。
/// </summary>
public static class LocalFileIndexGate
{
    public static LocalFileIndexPhase Evaluate(LocalFileIndexFacts f)
    {
        if (!f.Enabled) return LocalFileIndexPhase.Disabled;
        if (!f.ServiceInstalled) return LocalFileIndexPhase.NeedsSetup;
        if (!f.ServiceRunning) return LocalFileIndexPhase.Starting;
        if (!f.IndexReady) return LocalFileIndexPhase.Indexing;
        return LocalFileIndexPhase.Ready;
    }

    /// <summary>是否值得向本地文件数据源发一次查询。非 Ready 一律跳过（不发 IPC、不占内存）。</summary>
    public static bool ShouldQuery(LocalFileIndexPhase p) => p == LocalFileIndexPhase.Ready;

    /// <summary>是否应显示"首次全盘扫描进行中"等待提示。仅在索引建立中显示；未授权阶段不提示。</summary>
    public static bool ShouldShowScanProgress(LocalFileIndexPhase p) => p == LocalFileIndexPhase.Indexing;

    /// <summary>是否应触发一次 UAC 授权以安装/启动提权服务。仅在已开启但服务未装时。</summary>
    public static bool ShouldRequestElevation(LocalFileIndexPhase p) => p == LocalFileIndexPhase.NeedsSetup;
}
