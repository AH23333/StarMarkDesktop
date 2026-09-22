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
            foreach (var s in mgr.GetSnapshots())
                RowsItems.Add(new SnapshotRow
                {
                    Id = s.Id,
                    Name = s.Name,
                    Meta = $"{s.Summary} · {FormatTime(s.CreatedAt)}",
                });
        }
        RaisePropertyChanged(nameof(HasNoSnapshots));
    }

    private static string FormatTime(long unixSeconds)
    {
        if (unixSeconds <= 0) return string.Empty;
        try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
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
        await CenteredDialog.ShowContentAsync($"快照「{snapshot.Name}」", content, owner: App.MainWindow);
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

        var name = await CenteredDialog.PromptAsync(
            title: "从快照提取布局",
            message: "把这个快照的「位置 + 外观」另存为一套**不含数据**的纯模板布局，之后可在布局方案里复用。给这套布局起个名字：",
            placeholder: "例如：从「上线前」提取",
            defaultText: $"{snapshot.Name} 的布局",
            primaryText: "提取",
            cancelText: "取消",
            owner: App.MainWindow);
        if (name is null) return;

        var layout = await mgr.ExtractLayoutFromSnapshotAsync(id, name);
        if (layout is null)
            await CenteredDialog.MessageAsync("无法提取", "该快照没有可提取的组件。", owner: App.MainWindow);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Manager() is not { } mgr) return;
        var row = RowsItems.FirstOrDefault(r => r.Id == id);
        var snapshot = mgr.GetSnapshots().FirstOrDefault(s => s.Id == id);
        if (snapshot is null) return;

        var confirm = await CenteredDialog.ConfirmAsync(
            "应用快照",
            $"确定回到快照「{row?.Name ?? id}」记录的时刻？当前所有组件的位置与数据将被该快照**覆盖**（不属于该快照的组件会被隐藏，内容保留）。"
            + "应用前会自动生成一个「自动备份（应用前）」快照点，万一结果不满意可再应用它退回。",
            primaryText: "应用",
            cancelText: "取消",
            owner: App.MainWindow,
            dedupeKey: $"applysnapshot:{id}");
        if (!confirm) return;

        // Apply 是 Replace（会覆盖当前数据），应用前强制先留回滚点；回滚点没存成时 Apply 会中止并返回 false，
        // 必须明确告知用户"未应用"，否则会误以为已还原。
        var ok = await mgr.ApplySnapshotAsync(id);
        if (!ok)
            await CenteredDialog.MessageAsync("未能应用",
                $"{mgr.LastApplyError ?? "生成「应用前」回滚点失败"}——为防数据丢失已中止，当前状态未改动。",
                owner: App.MainWindow);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Manager() is not { } mgr) return;
        var row = RowsItems.FirstOrDefault(r => r.Id == id);

        var confirm = await CenteredDialog.ConfirmAsync(
            "删除快照",
            $"确定删除快照「{row?.Name ?? id}」？此操作不可撤销（其它快照不受影响）。",
            primaryText: "删除",
            cancelText: "取消",
            owner: App.MainWindow,
            dedupeKey: $"deletesnapshot:{id}");
        if (!confirm) return;

        await mgr.DeleteSnapshotAsync(id);
    }
}
