#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// <summary><b>窗口标题</b>那张名单（批次 WI-36）：与 <see cref="Live"/> 同一时刻、同一个订阅里重贴。
    /// <para>为什么单开一张而不是加第七颗附带属性：<b>WinUI 3 的 <c>Window</c> 不是
    /// <c>DependencyObject</c></b>（它只是 HWND 的托管包装），附带属性贴不上去——写
    /// <c>SetTitleKey(this, …)</c> 的编译错误是「无法从 WidgetWindow 转换为 DependencyObject」，
    /// 我先前认定"标题也是依赖属性，所以进得了那棵树"是错的（坑表 #338）。</para>
    /// <para>它仍是"重贴"而不是"重建"：订阅点还是只有 <see cref="ReapplyAll"/> 那唯一一处。</para></summary>
    private static readonly List<WindowTitle> WindowTitles = new();

    private static readonly HashSet<string> ReportedUnwritable = new(StringComparer.Ordinal);

    /// <summary><see cref="KeyProperty"/> 那句正文的实参（配 <c>Locale.Fmt</c> 的位置式占位符）。
    /// <para>为什么要有这一颗：状态行那一族的话是"半句固定的话＋一个当场算出来的读数"
    /// （抓了多少条、哪几个源到期、还剩多少 MB）。没有这一颗时只有两条坏路：①把整句留在 code-behind
    /// 里拼（换语言那一刻没人重贴＝那句永远中文，正是坑表 #276 那一族）；②把读数抄进句子（写死的数，
    /// P-171 那一族）。</para>
    /// <para><b>⛔ 这一颗不能做成依赖属性</b>（批次 WI-2l：它原本是第六颗附带属性
    /// <c>RegisterAttached("Args", typeof(object[]))</c>）。WinUI 3 的依赖属性类型必须投得到 WinRT，
    /// 而托管数组（SZARRAY）没有可调用包装——症状不是托管异常，是<b>启动即原生访问违例</b>
    /// （coreclr.dll+0x2E5102，三档语言全崩，5474 格测试全绿；坑表 #316）。
    /// 弱表按元素回收，语义与附带属性一致，且只此一处写、只此一处读。</para>
    /// <para>只配 <see cref="KeyProperty"/> 用：<b>提示词、占位符与开关那两侧今天都不带读数</b>，
    /// 给它们各配一份是五份可以各自漂移的实现，⛔ 等哪一条真带读数了再加。</para>
    /// </summary>
    private static readonly ConditionalWeakTable<DependencyObject, ArgsBox> ArgStore = new();

    /// <summary><see cref="PlaceholderKeyProperty"/> 那句提示的实参（批次 WI-61d·P-185）。
    /// <para>为什么现在才加：<see cref="PlaceholderKeyProperty"/> 的 doc-comment 早写着
    /// "⛔ 等哪一条真带读数了再加"——本批把 `CenteredDialog` 的形参换成交身份之后，
    /// 备份路径那句「完整路径，例：{0}」就是**第一颗带读数的占位符**。</para>
    /// <para>与 <see cref="ToolTipArgStore"/> 分开而不是共用一格信箱：同一颗输入框可以
    /// 既有正文键又有提示键，共用就等于"贴提示时拿到正文的实参"（坑表 #276 同族——一处信箱只服务一条路）。</para></summary>
    private static readonly ConditionalWeakTable<DependencyObject, ArgsBox> PlaceholderArgStore = new();

    /// <summary><see cref="ToolTipKeyProperty"/> 那句悬停说明的实参（批次 WI-51）。
    /// <para>为什么不是顺路用 <see cref="ArgStore"/>：画布那条工具条上<b>同一颗按钮两句话都有</b>——
    /// 正文（图标／短名）一句、悬停说明一句，而说明里嵌着别的按钮的名字。共用一格信箱就等于
    /// "贴正文时拿到说明的实参"，屏幕上是一句填空填错了的话（坑表 #276 同族：一处信箱只服务一条路）。
    /// ⛔ 第二颗附带属性（WinRT 投不出去的那条边界，✅P-173／坑表 #316）⇒ 与 ArgStore 同一形状的弱表。</para></summary>
    private static readonly ConditionalWeakTable<DependencyObject, ArgsBox> ToolTipArgStore = new();

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

    /// <summary><see cref="ToggleSwitch"/> 那一族<b>亮着时</b>旁边那两个字。
    /// <para>为什么不给 <see cref="KeyProperty"/> 顺路处理：① 一颗键只能出一句字，而开关有<b>两句</b>；
    /// ② <c>ToggleSwitch</c> 同时有 <c>Header</c> 与 <c>Content</c>，按 <see cref="ResolveTextProperty"/>
    /// 那条"有 Header 就不碰 Content"的规矩，正文键会去写 <c>Header</c>——屏幕上开关上方多出一行字，
    /// 而真正该换的 <c>OnContent</c> 一个字都不动（坑表 #274 同一族，只是这次是反向的）。</para></summary>
    public static readonly DependencyProperty OnContentKeyProperty = DependencyProperty.RegisterAttached(
        "OnContentKey", typeof(string), typeof(LocText), new PropertyMetadata(null, OnOnContentKeyChanged));

    /// <summary><see cref="ToggleSwitch"/> <b>关掉时</b>那两个字，与 <see cref="OnContentKeyProperty"/> 成对。</summary>
    public static readonly DependencyProperty OffContentKeyProperty = DependencyProperty.RegisterAttached(
        "OffContentKey", typeof(string), typeof(LocText), new PropertyMetadata(null, OnOffContentKeyChanged));

    public static void SetKey(DependencyObject element, string value)
    {
        // 不带读数的那一路也要过一次信箱：留着上一句的实参＝下一次换语言按旧读数填空。
        RememberArgs(element, null);
        element.SetValue(KeyProperty, value);
    }

    /// <summary>贴一句<b>带读数</b>的话（批次 WI-2k）：实参先进信箱，随后那颗键的回调才知道要填空。</summary>
    public static void SetKey(DependencyObject element, string value, params object?[] args)
    {
        // 顺序是有原因的：实参先落，随后那颗键的回调才知道自己要以"带读数"的方式贴；
        // 反过来先设键再设实参，第一次贴上去的就是没填空的句子（界面上是 {0}）。
        RememberArgs(element, args);
        element.SetValue(KeyProperty, value);
    }

    /// <summary>
    /// 把 ViewModel 交回的那句<b>「键＋实参」</b>钉到元素上（批次 WI-2k）。
    /// <para>为什么要有这一颗而不是让每个页面自己拆：<b>没有话</b>与<b>有话但不带读数</b>这两种情况
    /// 在调用点必须走同一条路——漏掉前者就留下一条注册项，换语言那一刻把<b>上一次那句已经不该出现的</b>
    /// 句子又贴回来（坑表 #276 那一族的反向）。拆在十几个页面里就会漂移成十几种漏法。</para>
    /// </summary>
    public static void SetMessage(DependencyObject element, LocMessage? message)
    {
        if (message is null)
        {
            SetKey(element, string.Empty);
            return;
        }
        SetKey(element, message.Key, message.Args ?? Array.Empty<object?>());
    }

    /// <summary>
    /// 把一句「键＋实参」钉到<b>点名要的那一颗</b>依赖属性上（批次 WI-61d·片2）。
    /// <para>为什么⛔ 让 <see cref="SetMessage(DependencyObject,LocMessage)"/> 顺路处理常驻那张提示条：
    /// <see cref="ResolveTextProperty"/> 的次序是 Text→Content→Header→Title→Message→Description，
    /// 而 <c>InfoBar</c> 没有 Text 也没有 Header ⇒ 第一颗命中的是 <b><c>Content</c></b>——那正是本批往里挂
    /// 那颗就地按钮的位置。WI-2 那次的症状会原样重来：标题写进正文容器，屏幕上看不见那句字、只剩空白。</para>
    /// <para>交的是 <c>InfoBar.TitleProperty</c> 那颗<b>标识符</b>而不是属性名 ⇒ 这颗 DP 存不存在由
    /// <b>编译器</b>回答，⛔ 留到运行期反射去猜（猜错的症状是屏幕上那张卡空白，只有真机看得见）。</para>
    /// <para>实参随 <see cref="LocMessage"/> 一起进注册项（<see cref="Paste"/> 那唯一一条取词路），
    /// 所以这里⛔ 第四颗信箱——常驻元素同一刻有两句（抬头与正文），共用一格信箱就等于贴抬头时拿到正文的实参
    /// （坑表 #276 同族）。</para>
    /// </summary>
    public static void SetMessage(DependencyObject element, DependencyProperty target, LocMessage? message)
    {
        if (message is null)
        {
            Drop(element, target);        // 没话了必须把注册项一起摘掉，否则下一次换语言把旧句子贴回来
            return;
        }
        Paste(element, target, message.Key, message.Args ?? Array.Empty<object?>());
    }

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetToolTipKey(DependencyObject element, string value)
    {
        // 不带实参的那一路也要过一次信箱：留着上一句的实参＝下一次换语言按旧读数填空（与 SetKey 同一条理由）。
        ToolTipArgStore.Remove(element);
        element.SetValue(ToolTipKeyProperty, value);
    }

    /// <summary>
    /// 把一句<b>带实参</b>的悬停说明钉进重贴名单（批次 WI-51）。
    /// <para>画布那条工具条上有四句说明嵌着别的按钮的名字（「白板开着不能穿透」这类）。
    /// 名字⛔ 抄在句子里：改了那颗按钮的说法，这四句就留在旧名字上教人点一颗不存在的东西
    /// ——与 ✅P-178 那颗"卡片正文抄了全局热键"同一形状。实参交出去的是<b>没渲染的</b>
    /// <see cref="LocMessage"/>，嵌套那句也在贴上去这一刻才取词（✅WI-36 那颗窗口标题同一条口径）。</para>
    /// </summary>
    public static void SetToolTipKey(DependencyObject element, string key, params object?[] args)
    {
        // 与正文那一路同序：实参先落，随后那颗键的回调才知道自己要以"带读数"的方式贴。
        if (args is { Length: > 0 }) ToolTipArgStore.AddOrUpdate(element, new ArgsBox(args));
        else ToolTipArgStore.Remove(element);
        element.SetValue(ToolTipKeyProperty, key);
    }

    public static string GetToolTipKey(DependencyObject element) => (string)element.GetValue(ToolTipKeyProperty);

    public static void SetPlaceholderKey(DependencyObject element, string value)
    {
        // 不带读数的那一路也要过一次信箱：留着上一句的实参＝下一次换语言按旧读数填空（与 SetKey 同一条理由）。
        PlaceholderArgStore.Remove(element);
        element.SetValue(PlaceholderKeyProperty, value);
    }

    /// <summary>贴一句<b>带读数</b>的提示（批次 WI-61d）：实参先落，随后那颗键的回调才知道要填空（与 SetKey 同序）。</summary>
    public static void SetPlaceholderKey(DependencyObject element, string key, params object?[] args)
    {
        if (args is { Length: > 0 }) PlaceholderArgStore.AddOrUpdate(element, new ArgsBox(args));
        else PlaceholderArgStore.Remove(element);
        element.SetValue(PlaceholderKeyProperty, key);
    }

    public static string GetPlaceholderKey(DependencyObject element) => (string)element.GetValue(PlaceholderKeyProperty);

    public static void SetOnContentKey(DependencyObject element, string value) => element.SetValue(OnContentKeyProperty, value);

    public static string GetOnContentKey(DependencyObject element) => (string)element.GetValue(OnContentKeyProperty);

    public static void SetOffContentKey(DependencyObject element, string value) => element.SetValue(OffContentKeyProperty, value);

    public static string GetOffContentKey(DependencyObject element) => (string)element.GetValue(OffContentKeyProperty);

    /// <summary>
    /// 把<b>窗口标题</b>（Alt+Tab 与任务栏读的那一颗）交进重贴名单（批次 WI-36）。
    /// <para>带实参：组件窗那句是"前缀 ＋ 名字"，而名字本身又是一颗键——实参交出去的是<b>没渲染的</b>
    /// <see cref="LocMessage"/>，嵌套那句也在贴上去这一刻才取词（坑表 #328 同族）。</para>
    /// </summary>
    public static void SetWindowTitle(Window window, string key, params object?[]? args)
    {
        lock (Gate)
        {
            // 同一个窗只留最新那句：重命名、换类型后重设，留着旧的会让换语言把上一版名字贴回来。
            WindowTitles.RemoveAll(t => !t.TryGetTarget(out var live) || ReferenceEquals(live, window));
            WindowTitles.Add(new WindowTitle(new WeakReference<Window>(window), key, args));
        }
        window.Title = Text(key, args);
    }

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var key = e.NewValue as string;
        if (string.IsNullOrEmpty(key))
        {
            // 键被清空＝这一处不再由表管（状态行回到"没有话要说"）。⚠ 必须把注册项一起摘掉：
            // 只把界面上的字清掉而留着注册项，下一次换语言会把**上一次那句**又贴回来。
            RememberArgs(d, null);
            Drop(d, ResolveTextProperty(d));
            return;
        }
        var target = ResolveTextProperty(d);
        if (target is null)
        {
            WarnUnwritableOnce(d, key, "找不到可写文字的属性");
            return;
        }
        Paste(d, target, key!, TakeArgs(d));
    }

    /// <summary>摘掉一处注册并清回空：状态行"没话了"与"换语言别把旧句子贴回来"必须是同一条路。</summary>
    private static void Drop(DependencyObject target, DependencyProperty? property)
    {
        if (property is null) return;
        lock (Gate)
        {
            Live.RemoveAll(r => r.TryGetTarget(out var t) && ReferenceEquals(t, target) && r.Property == property);
        }
        target.SetValue(property, string.Empty);
    }

    /// <summary>把那句正文的实参记到信箱里（<c>null</c>／空数组＝这一句不带读数，把旧的抹掉）。</summary>
    private static void RememberArgs(DependencyObject element, object?[]? args)
    {
        if (args is { Length: > 0 }) ArgStore.AddOrUpdate(element, new ArgsBox(args));
        else ArgStore.Remove(element);
    }

    private static object?[]? TakeArgs(DependencyObject element)
        => ArgStore.TryGetValue(element, out var box) ? box.Value : null;

    /// <summary>取词＋填空的唯一一处：贴的时候与换语言重贴的时候走的是同一条路，⛔ 不许两份实现。</summary>
    private static void Paste(DependencyObject d, DependencyProperty target, string key, object?[]? args)
    {
        Register(d, target, key, args);
        d.SetValue(target, Text(key, args));
    }

    private static string Text(string key, object?[]? args)
        => args is { Length: > 0 } ? Locale.Fmt(key, args) : Locale.T(key);

    private static void OnToolTipKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var key = e.NewValue as string;
        if (string.IsNullOrEmpty(key)) return;
        // 取词＋填空仍走 Text() 那唯一一处（⛔ 在这里再写一份 string.Format）：换语言重贴的是同一条路。
        var args = ToolTipArgStore.TryGetValue(d, out var box) ? box.Value : null;
        Register(d, ToolTipService.ToolTipProperty, key!, args);
        ToolTipService.SetToolTip(d, Text(key!, args));
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
        // 取词＋填空仍走 Text() 那唯一一处（与 ToolTip/OnContent 同一条路，⛔ 再写一份 string.Format）。
        var args = PlaceholderArgStore.TryGetValue(d, out var box) ? box.Value : null;
        Register(d, target, key!, args);
        d.SetValue(target, Text(key!, args));
    }

    private static void OnOnContentKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => PasteOntoNamedProperty(d, e.NewValue as string, "OnContent");

    private static void OnOffContentKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => PasteOntoNamedProperty(d, e.NewValue as string, "OffContent");

    /// <summary>把一句键贴到<b>点名要的那一颗</b>属性上（不像 <see cref="ResolveTextProperty"/> 那样去挑）。</summary>
    private static void PasteOntoNamedProperty(DependencyObject d, string? key, string propertyName)
    {
        if (string.IsNullOrEmpty(key)) return;
        var target = FindProperty(d, propertyName);
        if (target is null)
        {
            WarnUnwritableOnce(d, key!, $"没有 {propertyName}");
            return;
        }
        Register(d, target, key!, null);
        d.SetValue(target, Locale.T(key!));
    }

    private static void Register(DependencyObject target, DependencyProperty property, string key, object?[]? args)
    {
        lock (Gate)
        {
            // 同一元素同一颗属性只留一条：XAML 里改过键、或同一实例被二次设置时，
            // 留着旧的那条会让换语言把旧文案又贴回去。
            Live.RemoveAll(r => r.TryGetTarget(out var t) && ReferenceEquals(t, target) && r.Property == property);
            Live.Add(new Registration(new WeakReference<DependencyObject>(target), property, key, args));
        }
    }

    /// <summary>换语言的那一刻：把所有还活着的注册项（元素上的字与窗口标题）按新的语言重取一次词。</summary>
    private static void ReapplyAll()
    {
        Registration[] snapshot;
        WindowTitle[] titles;
        lock (Gate)
        {
            Live.RemoveAll(r => !r.TryGetTarget(out _));
            snapshot = Live.ToArray();
            WindowTitles.RemoveAll(t => !t.TryGetTarget(out _));
            titles = WindowTitles.ToArray();
        }
        foreach (var registration in snapshot)
        {
            if (!registration.TryGetTarget(out var target)) continue;
            target.SetValue(registration.Property, Text(registration.Key, registration.Args));
        }
        foreach (var title in titles)
        {
            // Title 是普通 CLR 属性（Window 不是 DependencyObject），所以这里只能整句重设，不能 SetValue。
            if (title.TryGetTarget(out var window)) window.Title = Text(title.Key, title.Args);
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

    /// <summary>信箱的装法：<see cref="ConditionalWeakTable"/> 的值必须是引用类型，而数组本身当不了那颗"值壳"。</summary>
    private sealed class ArgsBox
    {
        public ArgsBox(object?[]? value) => Value = value;

        public object?[]? Value { get; }
    }

    private sealed class Registration
    {
        public Registration(WeakReference<DependencyObject> target, DependencyProperty property, string key,
                           object?[]? args)
        {
            Target = target;
            Property = property;
            Key = key;
            Args = args;
        }

        public WeakReference<DependencyObject> Target { get; }

        public DependencyProperty Property { get; }

        public string Key { get; }

        /// <summary>那句正文的实参（没有＝<c>null</c>，走整句取词）。换语言重贴时要按它重新填空。</summary>
        public object?[]? Args { get; }

        public bool TryGetTarget(out DependencyObject target) => Target.TryGetTarget(out target!);
    }

    /// <summary>一条<b>窗口标题</b>的注册项：与 <see cref="Registration"/> 同一条取词路，只是宿主是
    /// <c>Window</c>（它不是 <c>DependencyObject</c>，进不了附带属性那棵树，只能自己记一句）。</summary>
    private sealed class WindowTitle
    {
        public WindowTitle(WeakReference<Window> target, string key, object?[]? args)
        {
            Target = target;
            Key = key;
            Args = args;
        }

        public WeakReference<Window> Target { get; }

        public string Key { get; }

        public object?[]? Args { get; }

        public bool TryGetTarget(out Window window) => Target.TryGetTarget(out window!);
    }
}
