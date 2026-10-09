#nullable enable
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions;

/// <summary>
/// 来源名字的<b>身份 ↔ 念法</b>那一层（批次 WI-57，P-187）。
/// <para>要解决的问题：诊断面板那一行念的是「源：Chrome 书签」，而屏幕上这句话自本批起只许住在语言表里。
/// 从前这句话由 <c>IItemSource.DisplayName</c> 各源写死一份中文常量——那是<b>同一句字的第二个主人</b>
/// （表里改英文／日文，代码里那份中文不会跟着动；反之也一样，P-171 那一族）。所以 <c>DisplayName</c>
/// 整个删掉，这里只留<b>身份到键的一次映射</b>。</para>
/// <para><b>映射按 <see cref="IItemSource.SourceId"/>（ASCII 身份）走，认不得的落"其它"那颗</b>：
/// 与 ✅WI-40-2 的 <c>HotkeyActions.CategoryTitleKey</c> 同一条路子——键全部是<b>字面量</b>，
/// ⛔ 在这里拼 <c>"source.name." + id</c>。拼出来的键名让"表里这行永远走不到"变得看不见（坑表 #354 那一族），
/// 而逐颗写字面键的那批行有双向对账证人守着（<c>DiagnosticsWordGateTests</c>）。</para>
/// </summary>
public static class SourceNames
{
    /// <summary>认不得的身份：露出"其它来源"那颗并把身份本身递给它，⛔ 在这里替它编一句。</summary>
    public const string OtherKey = "source.name.other";

    /// <summary>来源标识 → 表里那颗行的键（逐颗字面量，⛔ 拼接）。</summary>
    public static string NameKeyOf(string sourceId) => sourceId switch
    {
        ItemSources.GitHub => "source.name.github",
        ItemSources.Chrome => "source.name.chrome",
        ItemSources.Edge => "source.name.edge",
        ItemSources.Ditto => "source.name.ditto",
        ItemSources.FileSystem => "source.name.filesystem",
        _ => OtherKey,
    };

    /// <summary>
    /// 某个来源在屏幕上该念的那句（<b>没取词</b>的那颗）。
    /// <para>认得的身份不带实参；"其它"那颗带上身份本身——不然屏幕上只会剩一句"其它来源"，
    /// 而报障时最需要知道的正是那个没登记过的标识是什么。</para>
    /// </summary>
    public static LocMessage NameOf(string sourceId)
    {
        var key = NameKeyOf(sourceId);
        return key == OtherKey ? LocMessage.Of(OtherKey, sourceId) : LocMessage.Of(key);
    }
}
