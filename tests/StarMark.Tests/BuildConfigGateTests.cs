#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 构建配置本身的守门（批次 RG-1 / RG-2）。
/// <para>
/// 起因是用户报"生成失败，无法编译运行"，错误只有一句
/// <c>WindowsAppSDKSelfContained requires a supported Windows architecture.</c>——天书，
/// 而且指向 WinAppSDK 的包目录，看上去像"SDK 装坏了"。真因是包那道闸门只把 <c>$(Platform)</c> 的
/// <b>字面量</b>映射成架构，而 <c>Platform</c> 常常是<b>全局属性</b>（VS 配置管理器或命令行传进来），
/// <b>MSBuild 里项目体改不动全局属性</b>：csproj 那行"AnyCPU 就归一化成 x64"的兜底从来没生效过，
/// 注释却宣称已经处理＝一条假线索（RG-1）。
/// </para>
/// <para>
/// RG-1 只救住了 AnyCPU 一族，而实跑发现差一点也不行：<c>" X64 "</c>、<c>x64＋全角引号</c> 同样炸
/// （后者有现场证据——仓库里曾多出过一个 <c>bin/x64‘/</c> 目录）。用户第二次仍报同一句，
/// 说明"再猜一种拼法"这条路是错的 ⇒ RG-2 改成<b>把这道判断收回来</b>：
/// <c>Directory.Build.targets</c> 里覆盖同名 target，认不出的值统一落到 x64（本仓库只发 x64），
/// 不再报天书。为什么覆盖得掉：<c>Directory.Build.targets</c> 由 Microsoft.Common.targets 导入，
/// 位置在 NuGet 的 <c>nuget.g.targets</c> 之后，而 MSBuild 取<b>最后定义</b>的那个 target。
/// </para>
/// <para>下面三条各钉一头：覆盖在不在、覆盖丢没丢包里的逻辑、以及 csproj 那条 RID 兜底别被顺手删掉。</para>
/// </summary>
public sealed class BuildConfigGateTests
{
    private const string UiProject = "src/StarMark.UI/StarMark.UI.csproj";
    private const string RootTargets = "Directory.Build.targets";
    private const string Gate = "<Target Name=\"GetWindowsAppSDKNativePlatform\">";

    /// <summary>核心那条：<b>架构判断在我们手里</b>，且"认不出来的值"是落到 x64 而不是报错。</summary>
    [Fact]
    public void TheArchitectureDecisionIsOursAndUnknownSpellingsFallBackToX64()
    {
        var targets = ReadRepoFile(RootTargets);
        var body = Between(targets, Gate, "</Target>");

        Assert.Contains("<NativePlatform>x64</NativePlatform>", body);           // 默认值＝我们唯一发布的架构
        Assert.Contains("ToLowerInvariant()", body);                            // 差大小写/首尾空白的值也算认得
        Assert.DoesNotContain("<Error", body);                                   // 不再靠"报天书"收场
        // 覆盖必须无条件生效：加了 SelfContained 条件，就等于给"哪天改成 false"留下一个空架构窗口
        Assert.Contains(Gate, targets);
    }

    /// <summary>
     /// 覆盖别人家的 target 有一条红线：<b>不能顺手丢掉它原本做的事</b>。
     /// 这里去读装好的包里那个 target 的原文，要求它至今仍然只干"算 NativePlatform"这一件事
     /// （没有 Copy/Exec/ItemGroup 之类）——包升级改了形状，这条就红，逼着回来核对。
     /// </summary>
    [Fact]
    public void ThePackageTargetWeOverrideStillOnlyComputesThatOneProperty()
    {
        var cache = Path.Combine(
            Environment.GetEnvironmentVariable("USERPROFILE") ?? ".", ".nuget", "packages",
            "microsoft.windowsappsdk.base");
        var file = Directory.Exists(cache)
            ? Directory.GetFiles(cache, "Microsoft.WindowsAppSDK.SelfContained.targets", SearchOption.AllDirectories)
                  .OrderByDescending(File.GetLastWriteTimeUtc)
                  .FirstOrDefault()
            : null;
        Assert.NotNull(file);   // 找不到＝包改名/换了目录结构，我们的覆盖需要重新核对

        var text = File.ReadAllText(file!);
        var at = text.IndexOf("Name=\"GetWindowsAppSDKNativePlatform\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "包里已经没有这个 target 了：我们的覆盖要么多余、要么已经失效，必须重新看这段");
        var end = text.IndexOf("</Target>", at, StringComparison.Ordinal);
        var original = text[at..end];

        Assert.Contains("<NativePlatform>", original);
        Assert.Contains("requires a supported Windows architecture", original);
        foreach (var task in new[] { "<Exec", "<Copy", "<ItemGroup", "<MakeDir", "<Delete", "<MSBuild" })
            Assert.False(original.Contains(task, StringComparison.Ordinal), $"包这段多做了 {task}，覆盖会把它丢掉");
    }

    /// <summary>RG-1 那条 RID 兜底还在（它是包<i>自己</i>认的第二通道，与我们的覆盖互为保险）。</summary>
    [Fact]
    public void TheProjectsOwnAnyCpuRescueIsStillThere()
    {
        var proj = ReadRepoFile(UiProject);

        Assert.Contains("<RuntimeIdentifier Condition=", proj);
        Assert.Contains("'$(Platform)' == 'AnyCPU'", proj);
        Assert.Contains("win-x64", proj);
        Assert.Contains("全局属性", proj);      // 注释要说清那行 Platform 归一化为什么不够（别让它又变回假线索）
    }

    /// <summary>本仓库只发 x64：这句话必须写在声明里，而不是散在各处的 if 里。</summary>
    [Fact]
    public void TheUiProjectDeclaresX64AsItsOnlyPlatform()
        => Assert.Equal("x64", Regex.Match(ReadRepoFile(UiProject), @"<Platforms>([^<]*)</Platforms>").Groups[1].Value);
}
