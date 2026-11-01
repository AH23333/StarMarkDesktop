#nullable enable
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// P-16 的接线闸门：WidgetStorage 四个 CRUD 封装「没写进磁盘」这件事，必须一路传到屏幕上——
/// 封装报实情（null/false）→ Manager 记下那句身份（<c>LastPersistError</c>）→ 宿主分辨两义
/// （「没有内容」照旧那句 none*，「没写进磁盘」明说未保存）→ 那几句住在表里。
/// <para>测试工程不引用 <c>StarMark.UI</c>，UI 层的结构只能这样守（<see cref="SourceGate"/> 开头的既定格局）。
/// 本批之前四条出口恒报成功：降级闩跳过写盘、写盘失败都一样——用户存完就以为已经备份，重启后才见分晓。</para>
/// </summary>
public sealed class WidgetPersistSignalGateTests
{
    // 闸门①：四个 CRUD 封装按 Save 的实情报数，⛔ 恒 true / 交回没落盘的对象冒充成功。

    [Fact]
    public void Wrappers_TransmitSaveOutcome_InsteadOfConstantSuccess()
    {
        var src = ReadRepoFile("src/StarMark.Core/Widgets/WidgetStorage.cs");

        var saveLayout = MethodBody(src, "public string? SaveLayout(WidgetLayout layout)");
        Assert.Contains("this.Save(data) ? layout.Name : null", saveLayout);
        Assert.DoesNotContain("return layout.Name;", saveLayout);            // 旧谎报：没落盘也交回名字

        var deleteLayout = MethodBody(src, "public bool DeleteLayout(string id)");
        Assert.Contains("return this.Save(data);", deleteLayout);
        Assert.DoesNotContain("return true;", deleteLayout);                 // 旧谎报：删完缓存就恒 true

        var append = MethodBody(src, "public WidgetSnapshot? AppendSnapshot(WidgetSnapshot snapshot)");
        Assert.Contains("this.Save(data) ? snapshot : null", append);
        Assert.DoesNotContain("return snapshot;", append);                   // 旧谎报：交回没落盘的快照

        var deleteSnapshot = MethodBody(src, "public bool DeleteSnapshot(string id)");
        Assert.Contains("return this.Save(data);", deleteSnapshot);
        Assert.DoesNotContain("return true;", deleteSnapshot);               // 旧谎报：删完缓存就恒 true
    }

    // 闸门②：信号一路接线到屏——Manager 五条出口各清各记，四页宿主各凭它分辨两义。

    [Fact]
    public void Manager_FiveExits_ClearAndRecord_Hosts_DistinguishFourSites()
    {
        var manager = Code(ReadRepoPartials("src/StarMark.UI/Services/WidgetManager.cs"));

        // 声明恰一颗：两颗会互相覆盖，零颗＝信号根本没交出来。
        Assert.Equal(1, Count(manager, "public LocMessage? LastPersistError { get; private set; }"));

        // 五条存删出口（Capture/Extract/DeleteSnapshot/SaveCurrent/DeleteLayout）每次先清旧值，
        // 下一次调用读到的一定是这一次的结果，⛔ 上一次的残影。
        Assert.Equal(5, Count(manager, "LastPersistError = null;"));

        // 三存两删，各记各的那句身份：存＝改动未保存，删＝多一句「重启后还会回来」。
        Assert.Equal(3, Count(manager, "LastPersistError = LocMessage.Of(\"widget.persist.notSavedBody\");"));
        Assert.Equal(2, Count(manager, "LastPersistError = LocMessage.Of(\"widget.persist.notDeletedBody\");"));

        // 四页宿主接线：组件右键两条、快照页两条、设置页删布局一条——凭 is { } why 分辨
        // 「没有内容」（照旧那句 none*）与「没写进磁盘」（明说未保存）。
        Assert.Equal(2, Count(Code(ReadRepoFile("src/StarMark.UI/Views/WidgetWindow.Menu.cs")),
            "LastPersistError is { } why"));
        Assert.Equal(2, Count(Code(ReadRepoFile("src/StarMark.UI/Views/SnapshotPage.xaml.cs")),
            "LastPersistError is { } why"));
        Assert.Equal(1, Count(Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Widgets.cs")),
            "LastPersistError is { } why"));
    }

    // 闸门③：那几句住在表里——恰一行、四列齐、en 列纯 ASCII（本族约定）、不带占位符。

    [Fact]
    public void PersistSentenceFamily_LivesInTable_OnceEach_AllColumnsFilled()
    {
        var table = ReadRepoFile("src/StarMark.Abstractions/I18n/UiStrings.cs");
        foreach (var key in new[]
                 { "widget.persist.notSavedTitle", "widget.persist.notSavedBody", "widget.persist.notDeletedBody" })
        {
            Assert.Equal(1, Count(table, $"\"{key}\""));                     // 恰一行：两行＝口径分裂
            Assert.True(StarMark.Abstractions.I18n.UiStrings.TryGet(key, out var entry), $"表里没有这颗键：{key}");
            Assert.False(string.IsNullOrWhiteSpace(entry.Zh), $"{key} zh 列空");
            Assert.False(string.IsNullOrWhiteSpace(entry.En), $"{key} en 列空");
            Assert.False(string.IsNullOrWhiteSpace(entry.Ja), $"{key} ja 列空");
            foreach (var c in entry.En)
                Assert.True(c < 128, $"{key} en 列须纯 ASCII，撞上 {(int)c}：{entry.En}");
            Assert.Equal(0, RowSlots(key));                                  // 本族是完整句子，不带 {0} 占位
        }
    }
}
