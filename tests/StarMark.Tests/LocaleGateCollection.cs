#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <b>会改进程级语言的证人类共用的一个关并行集合</b>（批次 WI-61b 附带的一处测试层修复）。
/// <para>
/// <c>Locale.Current</c> 是 <c>static</c>：换一次语言，整个测试宿主里所有"按当前语言取词"的地方一起跟着换。
/// 而本工程既没有 <c>xunit.runner.json</c>，也没有 assembly 级 <c>[CollectionBehavior]</c>
/// ——xunit v2 的两个默认值叠出来就是"每个测试类各成一个集合、集合之间并发跑"。
/// 2026-10-10 现场扫到 <b>15 个</b>类会 <c>Locale.TrySet(...)</c>（口径＝抹掉注释后正文里出现那颗调用，
/// 由 <c>I18nWiringGateTests.EveryLocaleMutatingClassIsInOneSerialCollection</c> 逐颗点名，⛔ 凭记忆列名单＝#122/#141）：
/// 一边把语言按下去，另一边正拿"这一句在该语言里该长什么样"做断言，读到的就是别人那一档的语言。
/// </para>
/// <para>
/// 那种失败不指向谁改坏了哪一行，只指向"谁先抢到那颗静态"——同一条形状在批次 WI-60b 记过
/// （⛔ 拿 <c>TrySet</c> 的返回值当"换没换到"的探针：它说的是"变没变"，相同就回 <c>false</c>，那是设计如此）。
/// 只把自己这一家串行⛔ 算修好：竞争的另一半还在外面，所以这一份名单要跟着"谁改了 <c>Locale</c>"一起长，
/// 并由那颗证人当场把没挂属性的那一档红出来。
/// </para>
/// <para>代价只有并行度：这些类读的是源文件、跑的是纯逻辑，串行下来是个位数秒级。</para>
/// </summary>
[CollectionDefinition("LocaleGlobal", DisableParallelization = true)]
public sealed class LocaleGlobalCollection
{
}
