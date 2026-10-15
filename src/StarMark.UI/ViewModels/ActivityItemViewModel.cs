#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>活动事件的三色归类（#51）：增=绿 / 删=红 / 改=黄。展示与着色只认这一层，不再逐来源判断。</summary>
public enum ActivityChange
{
    Added,
    Removed,
    Modified,
}

/// <summary>
/// 活动流单条记录的展示模型。包装 <see cref="ActivityRecord"/>，提供事件名、
/// 相对时间与三色归类（<see cref="Change"/>，用于着色）。见扩展对比方案 P1-3、#51。
/// <para><b>✅WI-67 片9c 第三刀：那颗事件名交的是 <see cref="LocMessage"/>（键＋实参）而⛔ 成品串。</b>
/// 这一行住在 <c>DataTemplate</c> 里、宿主有两处（主窗「最近活动」页＋桌面的「最近活动」组件），
/// 模板上那颗 <c>i18n:LocText.Key</c> 只能带一颗<b>固定</b>键、带不了"这一行自己的那颗键"
/// ⇒ 走 ✅WI-40-3b 那条机制 (C)：行模型持键＋显示侧 getter <c>Mode=OneWay</c>＋宿主在 <c>Locale.Changed</c> 那一拍逐行 <see cref="Repaste"/>。
/// 旧写法把 11 档中文冻在"这一条记录被读出来"的那一刻，换语言后整页一个字都不动（P-188 那一族最大的一块）。</para>
/// </summary>
public sealed partial class ActivityItemViewModel : ObservableObject
{
    public ActivityItemViewModel(ActivityRecord rec)
    {
        Id = rec.Id;
        Kind = rec.Kind;
        Title = rec.Title;
        Uri = rec.Uri;
        KindMessage = KindMessageOf(rec.Kind);
        AtText = RelativeTimeHelper.Format(rec.At);
        Change = ChangeOf(rec.Kind);
    }

    /// <summary>活动表主键，供最近活动格增量对齐（避免每次数据广播都重建列表而闪白）。</summary>
    public long Id { get; }

    public ActivityKind Kind { get; }

    public string Title { get; }

    public string? Uri { get; }

    /// <summary>事件类型那颗键（<c>activity.kind.*</c>，11 档各一颗）。</summary>
    public LocMessage KindMessage { get; }

    /// <summary>屏幕上念出来的那一行：⛔ 产地取词，由 <see cref="KindMessage"/> 现算；换语言那一刻靠宿主那一拍重念。</summary>
    public string KindLabel => KindMessage.Render();

    /// <summary>相对时间文案：刚刚 / x 分钟前 / x 小时前 / x 天前 / yyyy-MM-dd。</summary>
    public string AtText { get; }

    /// <summary>三色归类：绿(增)/红(删)/黄(改)。着色只认这一层。</summary>
    public ActivityChange Change { get; }

    /// <summary>身份→那颗键：⛔ 交念出来的字（<c>Change</c> 那颗三色判据也只认身份，⛔ 认句子）。</summary>
    private static LocMessage KindMessageOf(ActivityKind k) => k switch
    {
        ActivityKind.StarAdd => LocMessage.Of("activity.kind.starAdd"),
        ActivityKind.StarRemove => LocMessage.Of("activity.kind.starRemove"),
        ActivityKind.BookmarkAdd => LocMessage.Of("activity.kind.bookmarkAdd"),
        ActivityKind.BookmarkRemove => LocMessage.Of("activity.kind.bookmarkRemove"),
        ActivityKind.FileAdd => LocMessage.Of("activity.kind.fileAdd"),
        ActivityKind.FileRemove => LocMessage.Of("activity.kind.fileRemove"),
        ActivityKind.ClipAdd => LocMessage.Of("activity.kind.clipAdd"),
        ActivityKind.ClipRemove => LocMessage.Of("activity.kind.clipRemove"),
        ActivityKind.ItemAdd => LocMessage.Of("activity.kind.itemAdd"),
        ActivityKind.ItemModify => LocMessage.Of("activity.kind.itemModify"),
        _ => LocMessage.Of("activity.kind.itemDelete"),
    };

    /// <summary>换语言那一刻由宿主逐行调用：⛔ 换那颗键（身份没变），只把"念出来的那一行"重新通知一遍。</summary>
    public void Repaste() => OnPropertyChanged(nameof(KindLabel));

    private static ActivityChange ChangeOf(ActivityKind k) => k switch
    {
        ActivityKind.ItemModify => ActivityChange.Modified,
        ActivityKind.StarRemove or ActivityKind.BookmarkRemove or ActivityKind.FileRemove
            or ActivityKind.ClipRemove or ActivityKind.ItemDelete => ActivityChange.Removed,
        _ => ActivityChange.Added,   // 各 *Add 与 ItemAdd
    };
}
