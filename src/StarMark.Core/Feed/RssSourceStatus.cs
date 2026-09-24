#nullable enable
using StarMark.Abstractions.Feed;

namespace StarMark.Core.Feed;

/// <summary>
/// 一行订阅源"这一轮到底怎么样了"的那句文案（判据只在这里说一次）。
/// <para>原来这份写法住在设置页里，于是它一条都断言不到；而"没抓到"这件事有五种截然不同的原因，
/// <b>混成一句"没有内容"用户就不知道该改地址还是该查网络</b>（批次 NF 的教训）。搬到 Core 之后
/// 每一种状态各有一条用例钉着，改坏了会红。</para>
/// </summary>
public static class RssSourceStatus
{
    /// <summary>还没抓过的行给的不是空字符串：空着看着像渲染坏了。</summary>
    public const string NeverFetched = "还没抓过";

    /// <summary>
    /// <paramref name="outcome"/> 为 null 表示这一轮它压根没参与（例如刚添加进来还没刷新过）。
    /// 五种"没有内容"必须分得开：停止 / 停用 / 未变化 / 失败 / 通了但空。
    /// </summary>
    public static string Describe(RssSourceOutcome? outcome) => outcome switch
    {
        null => "没有参与这一轮",
        var o when o.Stopped => "没抓到它（已停止或整轮到时）",
        var o when !o.Source.Enabled => "已停用，这一轮没有抓",
        { NotModified: true } => "没有新内容（源说未变化）",
        { Ok: false } o => "失败：" + o.Error,
        var o when o.Entries.Count == 0 => "通了，但没有条目",
        var o => $"抓到 {o.Entries.Count} 条",
    };
}
