#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>快照列表单行模型（重建式刷新，从不就地改，故无需 INPC）。</summary>
public sealed class SnapshotRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Meta { get; set; } = string.Empty;
}

/// <summary>
/// 「快照」页：列出不可变的布局+数据快照点，提供 查看 / 提取布局 / 应用 / 删除。
/// 保存入口在组件窗口右键（与「布局方案」一致——设置/导航侧只管理，不在这里新建）。
/// 本页自充当绑定源（<see cref="INotifyPropertyChanged"/> 仅为推送 <see cref="HasNoSnapshots"/>）。
/// </summary>
public sealed partial class SnapshotPage : Page, INotifyPropertyChanged
{
    public ObservableCollection<SnapshotRow> RowsItems { get; } = new();
    public bool HasNoSnapshots => RowsItems.Count == 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public SnapshotPage()
    {
        InitializeComponent();
        BuildRows();
    }

    private static WidgetManager? Manager() => App.Services.GetService<WidgetManager>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (Manager() is { } mgr)
        {
            mgr.SnapshotsChanged -= OnSnapshotsChanged;   // 防重复订阅
            mgr.SnapshotsChanged += OnSnapshotsChanged;
        }
        BuildRows();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (Manager() is { } mgr) mgr.SnapshotsChanged -= OnSnapshotsChanged;   // 单例服务，不退订会泄漏死页面
    }

    private void OnSnapshotsChanged()
        => DispatcherQueue.TryEnqueue(BuildRows);

    private void BuildRows()
    {
        RowsItems.Clear();
        if (Manager() is { } mgr)
        {
            var points = mgr.GetSnapshots();
            // 名字那一列＝库里那颗中性形状念出来的词（空白＝「未命名快照」＋同组序号，✅P-200 (A)）；
            // 行模型持 string ⇒ 当场渲染，换语言要<b>重进</b>这一页才改口（与 Meta 同一口径）。
            var names = WidgetDisplayName.ForSnapshots(points);
            foreach (var s in points)
                RowsItems.Add(new SnapshotRow
                {
                    Id = s.Id,
                    Name = names.Word(s.Id, s.Name, WidgetDisplayName.SnapshotFallbackKey).Render(),
                    Meta = $"{s.Summary.Render()} · {FormatTime(s.CreatedAt)}",
                });
        }
        RaisePropertyChanged(nameof(HasNoSnapshots));
    }

    private static string FormatTime(long unixSeconds)
    {
        if (unixSeconds <= 0) return string.Empty;
        try { return DateTimeText.Minute(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime()); }
        catch { return string.Empty; }
    }

    private async void View_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Manager() is not { } mgr) return;
        var snapshot = mgr.GetSnapshots().FirstOrDefault(s => s.Id == id);
        if (snapshot is null) return;

        var content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = BuildDetails(snapshot),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        // 两颗按钮字点名要（批次 WI-52）：CenteredDialog 工厂里那份「确定／取消」默认值已交表，
        // 而 ShowContentAsync 的 null 说的是"这颗按钮不出现"——⛔ 拿 null 兼作"用默认字"。
        await CenteredDialog.ShowContentAsync(
            LocMessage.Of("snapshot.previewTitle",
                WidgetDisplayName.ForSnapshots(mgr.GetSnapshots()).Word(snapshot.Id, snapshot.Name, WidgetDisplayName.SnapshotFallbackKey)),
            content, owner: App.MainWindow,
            primaryText: LocMessage.Of("dialog.ok"), cancelText: LocMessage.Of("dialog.cancel"));
    }

    private static string BuildDetails(WidgetSnapshot snapshot)
    {
        if (snapshot.Entries.Count == 0) return "（空快照）";
        var lines = snapshot.Entries.Select(entry =>
        {
            var bits = new System.Collections.Generic.List<string>();
            if (entry.Links.Count > 0) bits.Add($"入口 {entry.Links.Count}");
            var local = entry.LocalItems.Count;
            if (local > 0) bits.Add($"待办/随记 {local}");
            if (!string.IsNullOrWhiteSpace(entry.GridTag)) bits.Add($"标签「{entry.GridTag}」");
            var data = bits.Count > 0 ? string.Join("，", bits) : "无数据";
            var where = $"{(int)entry.X},{(int)entry.Y} {Math.Max(0, (int)entry.Width)}×{Math.Max(0, (int)entry.Height)}";
            return $"· {WidgetStorage.KindTitle(entry.Kind)} — {data}（位置 {where}）";
        });
        return string.Join(Environment.NewLine, lines);
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Manager() is not { } mgr) return;
        var snapshot = mgr.GetSnapshots().FirstOrDefault(s => s.Id == id);
        if (snapshot is null) return;

        // 提取出来的布局该预填什么：交<b>库里那颗中性形状</b>，⛔ 交屏幕上那一句——后者是 WidgetDisplayName 现算的
        // <b>词</b>，他一字不改点确定就等于把「未命名快照」按<b>当下这门语言</b>冻进 widgets.json＝✅P-200 (A) 的侧门。
        // 没起名的快照这里就是空串（输入框靠 placeholder 提示），点确定后写侧照旧把它存回空串。
        var neutralName = WidgetDisplayName.Neutralize(snapshot.Name);
        var name = await CenteredDialog.PromptAsync(
            title: LocMessage.Of("snapshot.extractTitle"),
            message: LocMessage.Of("snapshot.extractBody"),
            placeholder: LocMessage.Of("snapshot.extractHint"),
            // defaultText 是输入框里那个**用户会改的值**（数据）⇒ 当场渲染一次就交出去，⛔ 进重贴名单
            //（进去就会在换语言那一刻盖掉他正在打的字，LocText 类注释第②条）。
            defaultText: neutralName.Length == 0
                ? string.Empty
                : LocMessage.Of("snapshot.layoutNameDefault", neutralName).Render(),
            primaryText: LocMessage.Of("snapshot.extractGo"),
            cancelText: LocMessage.Of("dialog.cancel"),
            owner: App.MainWindow);
        if (name is null) return;

        var layout = await mgr.ExtractLayoutFromSnapshotAsync(id, name);
        if (layout is null)
            await CenteredDialog.MessageAsync(LocMessage.Of("snapshot.noneToExtractTitle"),
                LocMessage.Of("snapshot.noneToExtractBody"), owner: App.MainWindow);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Manager() is not { } mgr) return;
        var row = RowsItems.FirstOrDefault(r => r.Id == id);
        var snapshot = mgr.GetSnapshots().FirstOrDefault(s => s.Id == id);
        if (snapshot is null) return;

        var confirm = await CenteredDialog.ConfirmAsync(
            LocMessage.Of("widget.menu.applySnapshot"),
            LocMessage.Of("snapshot.applyBody", row?.Name ?? id),
            LocMessage.Of("widget.grid.apply"),
            LocMessage.Of("dialog.cancel"),
            owner: App.MainWindow,
            dedupeKey: $"applysnapshot:{id}");
        if (!confirm) return;

        // Apply 是 Replace（会覆盖当前数据），应用前强制先留回滚点；回滚点没存成时 Apply 会中止并返回 false，
        // 必须明确告知用户"未应用"，否则会误以为已还原。
        var ok = await mgr.ApplySnapshotAsync(id);
        if (!ok)
            await CenteredDialog.MessageAsync(LocMessage.Of("snapshot.applyFailedTitle"),
                LocMessage.Of("snapshot.applyFailedBody",
                    mgr.LastApplyError ?? LocMessage.Of("widget.menu.rollbackFailed")),
                owner: App.MainWindow);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Manager() is not { } mgr) return;
        var row = RowsItems.FirstOrDefault(r => r.Id == id);

        var confirm = await CenteredDialog.ConfirmAsync(
            LocMessage.Of("snapshot.deleteTitle"),
            LocMessage.Of("snapshot.deleteBody", row?.Name ?? id),
            LocMessage.Of("dialog.delete"),
            LocMessage.Of("dialog.cancel"),
            owner: App.MainWindow,
            dedupeKey: $"deletesnapshot:{id}");
        if (!confirm) return;

        await mgr.DeleteSnapshotAsync(id);
    }
}
