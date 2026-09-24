#nullable enable
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 构建配置本身的守门（批次 RG）。
/// <para>
/// 起因是用户报"生成失败，无法编译运行"，错误只有一句
/// <c>WindowsAppSDKSelfContained requires a supported Windows architecture.</c>——
/// 天书，而且指向了 WinAppSDK 的包目录，看上去像"SDK 装坏了"。真因是 VS 把平台当<b>全局属性</b>传进来
/// （选到 Any CPU 时），而 <b>MSBuild 里项目体改不动全局属性</b>：csproj 里那行
/// <c>&lt;Platform&gt;x64&lt;/Platform&gt;</c> 兜底在这种传法下根本不生效，注释却写着"已归一化"＝一条假线索。
/// </para>
/// <para>
/// 这条闸门钉的不是"能编译"而是"Any CPU 那条路仍然被救回 x64"：救法走的是 WinAppSDK 那道闸门
/// 自己认的第二个入口（AnyCPU 时它改看 <c>RuntimeIdentifier</c>），所以不需要用户去切配置管理器。
/// 用 <c>dotnet build … -p:Platform=AnyCPU</c> 实跑验证过：删掉这行就复现原错，加上就 0 错误且产物能启动。
/// </para>
/// </summary>
public sealed class BuildConfigGateTests
{
    private const string UiProject = "src/StarMark.UI/StarMark.UI.csproj";

    [Fact]
    public void AnAnyCpuBuildIsSteeredBackToX64WithoutAskingTheUser()
    {
        var proj = ReadRepoFile(UiProject);

        Assert.Contains("<RuntimeIdentifier Condition=", proj);
        Assert.Contains("'$(Platform)' == 'AnyCPU'", proj);
        Assert.Contains("win-x64", proj);
        // 兜底必须自己成立（"平台已被上面那行改成 x64"这种说法在全局属性面前是假的）
        Assert.Contains("全局属性", proj);
    }
}
