#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把一句界面文案钉在元素上，并在换语言的那一刻<b>当场重贴</b>（批次 WI-1）。
/// <para>用法：<c>i18n:LocText.Key="nav.tree"</c>（元素自己的文字）或
/// <c>i18n:LocText.ToolTipKey="top.guideTip"</c>（悬停提示）。写了这两个之一，就不该再在同一元素上
/// 留字面量文案——那份字面量会在第一次重贴之后变成没人读的副本。</para>
/// <para><b>为什么不是 <c>x:Uid</c></b>：<c>x:Uid</c> 在 XAML <b>解析期</b>取值，换语言只能整页重建；
/// 而"改完语言要重启"在这个产品里算缺陷（P-54）。这里换成"注册＋重贴"，换语言就是
/// 一次遍历：每个元素按自己那颗键重新取一次词。</para>
/// <para><b>写哪一颗属性是查出来的，不是猜的</b>：同一颗键在 <c>TextBlock</c> 上是 <c>Text</c>、
/// 在 <c>TabViewItem</c> 上是 <c>Header</c>、在 <c>TextBox</c> 上是 <c>PlaceholderText</c>。
/// 查不到可写的那一颗会出声（日志＋那颗元素保持原样），不会悄悄留下半中半英。</para>
/// </summary>
public static class LocText
{
    /// <summary>按顺序找元素上<b>确实存在</b>的那一颗"正文"属性。
    /// <para>同一颗键在 <c>TextBlock</c> 上是 <c>Text</c>、在 <c>Button</c> 上是 <c>Content</c>、
    /// 在 <c>TabViewItem</c>／<c>Expander</c> 上是 <c>Header</c>。顺序不是随手排的，两条排除规则是踩过之后加的：</para>
    /// <para>① <b><c>Content</c> 只在元素没有 <c>Header</c> 时才当标签用</b>。<c>TabViewItem</c> 与 <c>Expander</c>
    /// 这一族里 <c>Content</c> 装的是<b>整页正文</b>，把标题写进去等于把那一栏换成一句字。
    /// 症状尤其骗人：XAML 里属性先于子元素解析，所以正文随后又被子元素盖回来，屏幕上看不见那句字——
    /// 只剩下<b>标题一颗都不出现</b>（批次 WI-2 第一次给设置页八栏挂键时真机照出来的就是这个，
    /// 而 5435 格测试全绿）。</para>
    /// <para>② <b><c>Text</c> 不写给输入框</b>（认 <c>PlaceholderText</c> 这颗属性）。那是用户正在打的字，
    /// 换语言那一刻覆盖它等于吃掉输入；这类元素真正的标签是 <c>Header</c>。</para>
    /// <para><b>这里刻意没有 <c>PlaceholderText</c></b>：提示词与输入的内容混在同一颗键下，
    /// 最坏的情况就是上面第②条。所以占位符走 <see cref="PlaceholderKeyProperty"/>。</para></summary>
    private static readonly string[] TextProperties =
    {
        "Text", "Content", "Header", "Title", "Message", "Description",
    };

    private static readonly object Gate = new();
    private static readonly List<Registration> Live = new();
    private static readonly HashSet<string> ReportedUnwritable = new(StringComparer.Ordinal);

    static LocText()
    {
        // 只订阅一次，且回调里只做"重贴"这一件事：重建页面、重查数据都不属于这里。
        Locale.Changed += ReapplyAll;
    }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(LocText), new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty ToolTipKeyProperty = DependencyProperty.RegisterAttached(
        "ToolTipKey", typeof(string), typeof(LocText), new PropertyMetadata(null, OnToolTipKeyChanged));

    /// <summary>输入框/下拉框里那行"还没输入时的提示"。
    /// <para>单独一颗属性而不是让 <see cref="KeyProperty"/> 顺路处理，是为了让"贴正文"与"贴提示"
    /// 在代码里永远是两条路：<c>TextBox</c> 同时有 <c>Text</c> 与 <c>PlaceholderText</c>，
    /// 一条路走到黑的最坏结果是把用户正在输入的那行字换成一句提示。</para></summary>
    public static readonly DependencyProperty PlaceholderKeyProperty = DependencyProperty.RegisterAttached(
        "PlaceholderKey", typeof(string), typeof(LocText), new PropertyMetadata(null, OnPlaceholderKeyChanged));

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetToolTipKey(DependencyObject element, string value) => element.SetValue(ToolTipKeyProperty, value);

    public static string GetToolTipKey(DependencyObject element) => (string)element.GetValue(ToolTipKeyProperty);

    public static void SetPlaceholderKey(DependencyObject element, string value) => element.SetValue(PlaceholderKeyProperty, value);

    public static string GetPlaceholderKey(DependencyObject element) => (string)element.GetValue(PlaceholderKeyProperty);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var key = e.NewValue as string;
        if (string.IsNullOrEmpty(key)) return;
        var target = ResolveTextProperty(d);
        if (target is null)
        {
            WarnUnwritableOnce(d, key, "找不到可写文字的属性");
            return;
        }
        Register(d, target, key!);
        d.SetValue(target, Locale.T(key!));
    }

    private static void OnToolTipKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var key = e.NewValue as string;
        if (string.IsNullOrEmpty(key)) return;
        Register(d, ToolTipService.ToolTipProperty, key!);
        ToolTipService.SetToolTip(d, Locale.T(key!));
    }

    private static void OnPlaceholderKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var key = e.NewValue as string;
        if (string.IsNullOrEmpty(key)) return;
        var target = FindProperty(d, "PlaceholderText");
        if (target is null)
        {
            WarnUnwritableOnce(d, key!, "没有 PlaceholderText");
            return;
        }
        Register(d, target, key!);
        d.SetValue(target, Locale.T(key!));
    }

    private static void Register(DependencyObject target, DependencyProperty property, string key)
    {
        lock (Gate)
        {
            // 同一元素同一颗属性只留一条：XAML 里改过键、或同一实例被二次设置时，
            // 留着旧的那条会让换语言把旧文案又贴回去。
            Live.RemoveAll(r => r.TryGetTarget(out var t) && ReferenceEquals(t, target) && r.Property == property);
            Live.Add(new Registration(new WeakReference<DependencyObject>(target), property, key));
        }
    }

    /// <summary>换语言的那一刻：把所有还活着的注册项按新的语言重取一次词。</summary>
    private static void ReapplyAll()
    {
        Registration[] snapshot;
        lock (Gate)
        {
            Live.RemoveAll(r => !r.TryGetTarget(out _));
            snapshot = Live.ToArray();
        }
        foreach (var registration in snapshot)
        {
            if (!registration.TryGetTarget(out var target)) continue;
            target.SetValue(registration.Property, Locale.T(registration.Key));
        }
    }

    private static DependencyProperty? ResolveTextProperty(DependencyObject target)
    {
        // 有 Header 的元素：Content 是正文容器，不是标签（见上面第①条）。
        var hasHeader = FindProperty(target, "Header") is not null;
        // 有 PlaceholderText 的元素是输入框：Text 里是用户正在打的字（见上面第②条）。
        var isInputBox = FindProperty(target, "PlaceholderText") is not null;
        foreach (var name in TextProperties)
        {
            if (name == "Content" && hasHeader) continue;
            if (name == "Text" && isInputBox) continue;
            if (FindProperty(target, name) is { } property) return property;
        }
        return null;
    }

    /// <summary>
    /// 按 CLR 属性名找到那颗依赖属性标识符。
    /// <para><b>字段与属性两条路都必须查</b>：托管辅助类（<c>ToolTipService.ToolTipProperty</c>）是
    /// 公开静态字段，而 <b>WinUI 3 的控件是 CsWinRT 投影出来的，<c>TextBlock.TextProperty</c> 是静态属性</b>。
    /// 只查字段的那一版在真机上的表现不是崩，是<b>整窗文案空白</b>（33 条"贴不上"）——
    /// 而 <c>StarMark.Tests</c> 引用不到 UI，这一族只有真机能看见。</para>
    /// </summary>
    private static DependencyProperty? FindProperty(DependencyObject target, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var type = target.GetType();
        if (type.GetField(name + "Property", Flags)?.GetValue(null) is DependencyProperty field) return field;
        if (type.GetProperty(name + "Property", Flags)?.GetValue(null) is DependencyProperty property) return property;
        return null;
    }

    private static void WarnUnwritableOnce(DependencyObject target, string key, string why)
    {
        lock (Gate)
        {
            if (!ReportedUnwritable.Add(key + "|" + why)) return;
        }
        StarLog.Warn($"界面文案 {key} 贴不上：{target.GetType().Name} {why}（那一处会保持原样，不是漏翻）");
    }

    private sealed class Registration
    {
        public Registration(WeakReference<DependencyObject> target, DependencyProperty property, string key)
        {
            Target = target;
            Property = property;
            Key = key;
        }

        public WeakReference<DependencyObject> Target { get; }

        public DependencyProperty Property { get; }

        public string Key { get; }

        public bool TryGetTarget(out DependencyObject target) => Target.TryGetTarget(out target!);
    }
}
