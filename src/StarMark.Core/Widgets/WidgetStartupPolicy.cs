namespace StarMark.Core.Widgets;

/// <summary>
/// "开机要不要把组件窗建出来"这笔取舍的<b>数字与说法，只有这一颗</b>。
/// <para>
/// 界面文案、启动日志、报告里的数都从这里取：同一事实两份实现是本项目反复收口的对象（P-123/P-130 那条线），
/// 而这一颗特别容易分岔——因为它带着一个**实测出来的**数字，改动它应该是"又量了一次"，不是"顺手调一下措辞"。
/// </para>
/// </summary>
public static class WidgetStartupPolicy
{
    /// <summary>
    /// 一颗组件窗的私有内存成本（MB，本机实测）。
    /// <para>
    /// 本机有两个读数：<b>本批这一对"只差这颗开关"的 A/B</b>（档里 12 颗留着、开机一颗不建 vs 全建，
    /// 同一产物同一副本，报告 §二百一十二）稳态差 <b>82.2 MB ⇒ 6.85 MB/颗</b>；SU-0 那条"删空档 vs 12 颗"
    /// 的曲线（§二百一十）差 95 MB ⇒ 7.9 MB/颗——可它的对照组把实例配置也一起删了，<b>混着不是窗的钱</b>。
    /// 界面承诺的是"关掉能省多少"，所以取靠下那一格、整到 <b>7</b>（12 颗说 84 MB）：宁可少承诺，
    /// 也不报一个只能靠删档才量得出来的数。
    /// </para>
    /// <para>
    /// 首颗几乎免费（组合面／DWM／材质后端在 0 颗那一跑就已付掉），所以这个数对 1~2 颗仍是<b>上界</b>，界面写"约"。
    /// 同一配置跨跑差 ~14 MB＝这把尺子的噪声（记在 P-134），因此这条承诺只引<b>同一次会话里前后脚跑的那一对</b>。
    /// </para>
    /// </summary>
    public const int PerWindowPrivateMb = 7;

    /// <summary>关掉开关后这份取舍立刻生效（收起已显示的），不需要重启——"要用户重启"在本项目里按缺陷算。</summary>
    public const bool TakesEffectWithoutRestart = true;

    /// <summary>N 颗不建能省下多少私有内存（0 与负数都按"没得省"说，不返回负数）。</summary>
    public static int EstimatedPrivateMb(int instances)
        => instances > 0 ? instances * PerWindowPrivateMb : 0;

    /// <summary>启动日志里那句"为什么这次一颗都没建"——没有这句，"开机内存怎么少了"就只能靠猜。</summary>
    public static string DescribeSkipped(int instances)
        => $"开机自动加载已关：{instances} 颗组件窗一颗都不建（约 {EstimatedPrivateMb(instances)} MB 私有），"
           + "要用的组件在设置「桌面工具（组件）」或托盘里点亮，点亮时按存档的几何与外观重建";

    /// <summary>
    /// 设置页上那句取舍说明。
    /// <para><b>刻意不写"你现在有 N 颗，可省 M MB"</b>：那颗数要跟着增删组件刷新，
    /// 一旦漏刷，界面就在报一个过期的钱数——报一个说不清来源的数字，比不报更容易误导人。
    /// 颗数与总额留在<b>启动日志</b>那句里（那一刻的颗数是事实，不会过期）。</para>
    /// </summary>
    public static string TradeoffNote
        => $"开着＝开机把已添加的组件全部建出来（默认，也就是今天的行为）。"
           + $"关掉＝开机一颗都不建，省下约 {PerWindowPrivateMb} MB 私有内存每颗（本机实测，颗数越多省得越多）。"
           + "改这个开关当场生效，不用重启。";
}
