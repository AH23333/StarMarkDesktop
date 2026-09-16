#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace StarMark.UI.Controls;

/// <summary>
/// 标签编辑器。对齐（并改进）浏览器扩展的内联标签编辑（`sidepanel/App.tsx` 的 inline-edit）：
///   - 已选标签渲染为 chip，点击即删
///   - 历史标签建议一键追加（上限 24，与扩展一致）
///   - 中英文逗号 / 空白 / 回车均可作为分隔符
/// 与扩展的差异：扩展把输入框当唯一真源（草稿文本实时解析成 chip），
/// 本控件改为「提交式」——输入完按逗号/回车即落成 chip，空输入退格删最后一个。
/// 这样 chip 区始终干净可见，不必在一长串原始文本里找标签。
/// </summary>
public sealed partial class TagEditor : UserControl
{
    /// <summary>历史标签上限（与扩展 slice(0, 24) 对齐）。</summary>
    private const int MaxSuggestions = 24;

    private static readonly char[] Separators = { ',', '，', ';', '；', ' ', '\t', '\n', '\r' };

    public TagEditor()
    {
        InitializeComponent();
        Tags = new ObservableCollection<string>();
        Suggestions = new ObservableCollection<string>();
        Tags.CollectionChanged += (_, _) => RefreshSuggestions();
    }

    /// <summary>当前已提交的标签（chip 区数据源）。</summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>建议标签（排除已在 chip 区的）。</summary>
    public ObservableCollection<string> Suggestions { get; }

    public static readonly DependencyProperty AllTagsProperty =
        DependencyProperty.Register(nameof(AllTags), typeof(IEnumerable<string>), typeof(TagEditor),
            new PropertyMetadata(null, (d, _) => ((TagEditor)d).RefreshSuggestions()));

    /// <summary>全库已有标签，作为建议来源。</summary>
    public IEnumerable<string>? AllTags
    {
        get => (IEnumerable<string>?)GetValue(AllTagsProperty);
        set => SetValue(AllTagsProperty, value);
    }

    public static readonly DependencyProperty HasSuggestionsProperty =
        DependencyProperty.Register(nameof(HasSuggestions), typeof(bool), typeof(TagEditor),
            new PropertyMetadata(false));

    public bool HasSuggestions
    {
        get => (bool)GetValue(HasSuggestionsProperty);
        set => SetValue(HasSuggestionsProperty, value);
    }

    /// <summary>初始化已有标签。</summary>
    public void SetTags(IEnumerable<string> tags)
    {
        Tags.Clear();
        foreach (var t in tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            Tags.Add(t.Trim());
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var hit = Tags.FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) Tags.Remove(hit);
    }

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        AddTag(tag);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        // 以分隔符结尾 → 提交内容为一个标签
        if (Separators.Contains(text[^1]))
            CommitInput();
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                CommitInput();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Back:
                // 空输入退格 → 删掉最后一个 chip
                if (InputBox.Text.Length == 0 && Tags.Count > 0)
                {
                    Tags.RemoveAt(Tags.Count - 1);
                    e.Handled = true;
                }
                break;
        }
    }

    private void CommitInput()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text)) { InputBox.Text = string.Empty; return; }

        foreach (var part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            AddTag(part);

        InputBox.Text = string.Empty;
    }

    private void AddTag(string raw)
    {
        var tag = raw.Trim();
        if (tag.Length == 0) return;
        // 大小写不敏感去重，避免 ai / AI 两份
        if (Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))) return;
        Tags.Add(tag);
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        if (AllTags is null)
        {
            HasSuggestions = false;
            return;
        }

        foreach (var t in AllTags
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Where(t => !Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(MaxSuggestions))
        {
            Suggestions.Add(t);
        }

        HasSuggestions = Suggestions.Count > 0;
    }
}
