#nullable enable

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——待办附件（P-305）：只这一颗「复制」开关。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放这一件事的两条出口。
/// 读档走主文件那份缓存（<c>Load()</c>），同一次拖放里读它不产生额外磁盘往返。</para>
/// <para>语义：<b>存档没这格＝false＝Linked</b>。默认不复制用户的东西（与
/// <c>LoadWidgetsLoadOnStartup</c> 的"默认开"相反是有意的：那颗收起桌面是观感事故，
/// 这颗多复制文件是磁盘与隐私的额外占用——宁可让想要的人自己点开）。</para>
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>拖进待办行的文件要不要复制成托管副本。只有存档明确写着 true 才复制。</summary>
    public bool LoadTodoAttachmentCopyEnabled()
        => Load()?.TodoAttachmentCopyEnabled is true;

    public void SaveTodoAttachmentCopyEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.TodoAttachmentCopyEnabled = enabled;
        Save(d);
    }
}
