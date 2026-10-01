#nullable enable
using System;
using System.Reflection;
using StarMark.Abstractions;

namespace StarMark.Core.Updates;

/// <summary>
/// 一个版本号的<b>解析与比较</b>（批次 UE）。纯函数，不读环境——"本机是哪一版"在 <see cref="Local"/>。
/// <para>
/// 为什么要自己写而不复用 <c>System.Version</c>：<c>Version</c> 只认 <c>1.2.3.4</c>，
/// 而 GitHub 的标签惯例是 <c>v1.2.3</c>，预发布是 <c>1.2.4-beta.2</c>；
/// 更要紧的是<b>它把"看不懂"和"是 0.0.0"混成一件事</b>——一个解析失败被当成"很旧的版本"，
/// 结果每次检查都报"有新版本"，那比不检查更坏（本仓 #224 那一族：看起来像量到了）。
/// </para>
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string? PreRelease)
{
    /// <summary>
    /// 认这些形状：<c>1.2.3</c> ／ <c>v1.2.3</c> ／ <c>V1.2</c>（缺的段按 0）／ <c>1.2.3-beta.2</c> ／
    /// <c>1.2.3+build.7</c>（构建元数据丢掉）。<b>认不回来返回 false，不返回 0.0.0</b>。
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = text.Trim();
        if (raw.Length > 0 && (raw[0] == 'v' || raw[0] == 'V')) raw = raw[1..];

        // 构建元数据（+ 之后）不参与"谁更新"的判断：同一个 1.2.3 打两次包不该互相判成新版
        var plus = raw.IndexOf('+');
        if (plus >= 0) raw = raw[..plus];

        string? pre = null;
        var dash = raw.IndexOf('-');
        if (dash >= 0)
        {
            pre = raw[(dash + 1)..];
            raw = raw[..dash];
            if (pre.Length == 0) pre = null;
        }

        var parts = raw.Split('.');
        if (parts.Length is 0 or > 3) return false;
        int major = 0, minor = 0, patch = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var segment = parts[i];
            if (segment.Length == 0 || segment.Length > 9) return false;      // 空段（1..3）与荒谬长度都算看不懂
            foreach (var c in segment)
                if (c < '0' || c > '9') return false;                          // 非数字段（1.2.x）不猜
            var value = int.Parse(segment);
            if (i == 0) major = value;
            else if (i == 1) minor = value;
            else patch = value;
        }
        version = new AppVersion(major, minor, patch, pre);
        return true;
    }

    /// <summary>
    /// 负＝本版本更旧（⇒ 有新版本），0＝同版，正＝本地比远端新（装了预发布／自己编的版）。
    /// <para>预发布排在同号正式版<b>之前</b>（<c>1.2.3-beta.2 &lt; 1.2.3</c>，SemVer 的口径）：
    /// 反过来的话，一个还挂着 beta 的构建会一直被告知"你已经是最新"，而正式版当天就发出来了。</para>
    /// </summary>
    public static int Compare(in AppVersion local, in AppVersion remote)
    {
        var byNumber = local.Major != remote.Major ? local.Major.CompareTo(remote.Major)
            : local.Minor != remote.Minor ? local.Minor.CompareTo(remote.Minor)
            : local.Patch != remote.Patch ? local.Patch.CompareTo(remote.Patch)
            : 0;
        if (byNumber != 0) return byNumber;
        return ComparePreRelease(local.PreRelease, remote.PreRelease);
    }

    private static int ComparePreRelease(string? local, string? remote)
    {
        if (local is null && remote is null) return 0;
        if (local is null) return 1;            // 无预发布＝正式版，更新
        if (remote is null) return -1;

        // 同号时比预发布标识符：全数字段按数值（beta.10 > beta.9），否则按字面（Ordinal，不随文化变）
        var a = local.Split('.');
        var b = remote.Split('.');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var hasA = i < a.Length;
            var hasB = i < b.Length;
            if (!hasA) return -1;               // 前缀相同而这边短＝更早（1.0-beta.1 < 1.0-beta.1.1）
            if (!hasB) return 1;
            var numA = long.TryParse(a[i], out var va);
            var numB = long.TryParse(b[i], out var vb);
            if (numA && numB)
            {
                if (va != vb) return va.CompareTo(vb);
                continue;
            }
            if (numA != numB) return numA ? -1 : 1;      // 数字段先于字母段（SemVer 规则 11）
            var byText = string.CompareOrdinal(a[i], b[i]);
            if (byText != 0) return byText;
        }
        return 0;
    }

    /// <summary>界面上那一句"当前版本"。<b>读不到时说"未知"，不写 0.0.0</b>。</summary>
    public static string Describe(in AppVersion version)
        => version.PreRelease is null
            ? $"{version.Major}.{version.Minor}.{version.Patch}"
            : $"{version.Major}.{version.Minor}.{version.Patch}-{version.PreRelease}";

    /// <summary>界面上那句"当前版本"：读到就念版本号，读不到就照实说 <see cref="Unknown"/>（不写 0.0.0 冒充）。</summary>
    public static string LocalDisplay => TryReadLocal(out var v) ? Describe(v) : Unknown;

    /// <summary>
    /// 本机版本号。<b>真源只有一个</b>：<c>Directory.Build.props</c> 里那一颗 <c>&lt;Version&gt;</c>
    /// （SDK 会把它同时写进 FileVersion 与 InformationalVersion，所以产物与源码不会各说各话）。
    /// <para>读的是<b>入口程序集</b>而不是"这个类所在的程序集"：StarMark.UI 才是那份产物，
    /// 而 Core 会被测试宿主加载——读错程序集会拿测试主机的版本号去和 Release 比。</para>
    /// </summary>
    public static bool TryReadLocal(out AppVersion version)
    {
        version = default;
        var text = InformationalVersionReader();
        return !string.IsNullOrWhiteSpace(text) && TryParse(text, out version);
    }

    /// <summary>测试缝：本机的版本字符串从哪来（默认走真装配属性；真实值不许进断言，同 #212 那一族）。</summary>
    internal static Func<string?> InformationalVersionReader = ReadEntryAssemblyVersion;

    private static string? ReadEntryAssemblyVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info;
            return assembly.GetName().Version?.ToString(3);
        }
        catch (Exception ex)
        {
            // 读自己的版本号都不该抛：抛出来只是把"检查更新"变成崩溃来源（尺子不许弄崩被量的东西）
            StarLog.Warn($"[更新] 读本机版本号失败：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    /// <summary>取不到时的占位；<see cref="TryReadLocal"/> 返回 false 时由调用方决定怎么说。</summary>
    public const string Unknown = "未知";
}
