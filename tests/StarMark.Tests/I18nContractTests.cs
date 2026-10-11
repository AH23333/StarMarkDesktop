#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 界面语言那一族的<b>纯逻辑</b>契约（批次 WI-1）：认代码、挑语言、取词、换语言那一次出声。
/// <para>这一族坏掉的表现都不是崩溃，而是"话说得比实现满"或"半中半英"：</para>
/// <list type="number">
/// <item>选项里出现一门表里没有整列的语言 → 选了之后满屏仍是中文；</item>
/// <item>启动时把系统语言读成"用户没表态" → 他改的那一项永远不算数；</item>
/// <item>设置档里一个认不得的代码被就近猜成中文 → 用户找不到自己那一项；</item>
/// <item>换语言时静默不出声 → 已经建好的元素留着上一版的词，屏幕上两种语言并存。</item>
/// </list>
/// <para><b>这里只查 Abstractions 那一层</b>：XAML 有没有真的按键取词、启动接线有没有排在建窗之前，
/// 那些归 <see cref="I18nWiringGateTests"/>（<c>StarMark.Tests</c> 引用不到 UI 项目，只能按源码形状钉）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class I18nContractTests : IDisposable
{
    private readonly AppLanguage _languageBefore = Locale.Current;

    public void Dispose() => Locale.TrySet(_languageBefore);

    private static string TextOf(AppLanguage language, string key)
    {
        Assert.True(UiStrings.TryGet(key, out var entry), $"表里没这颗键：{key}");
        return UiStrings.TextOf(entry, language);
    }

    // ===== 表本身 =====

    /// <summary>
    /// <b>每一行都必须有两门语言的整句</b>，且英文那一列不许夹汉字。
    /// <para>两列都"必填"不是格式洁癖：<c>Entry</c> 的构造要求三个实参，漏一门就编不过；
    /// 但"编得过而留一串空格/复制中文"编得过——那句在英文界面上看起来像"翻译丢了"，
    /// 而它是从表里读出来的，没有任何一条运行期路径会发现。所以这一格查的是<b>内容</b>。</para>
    /// <para>禁的只是中日韩那几段码位：标点（「」）、箭头（↑↓）、星星（⭐）在两列里都合法，
    /// 把它们一起禁掉只会逼人把符号从文案里删了。</para>
    /// </summary>
    [Fact]
    public void EveryRowCarriesBothLanguagesAndTheEnglishColumnIsReallyEnglish()
    {
        Assert.True(UiStrings.All.Count >= 40,
            $"表里只有 {UiStrings.All.Count} 行（现场量过是 47，少这么多＝整段被清空，不是有意删行）");
        foreach (var entry in UiStrings.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Zh), $"{entry.Key} 的中文列是空的");
            Assert.False(string.IsNullOrWhiteSpace(entry.En), $"{entry.Key} 的英文列是空的");
            Assert.DoesNotMatch("[\\u3040-\\u30ff\\u3400-\\u4dbf\\uf900-\\ufaff\\u4e00-\\u9fff]", entry.En);
        // 中文列反过来也不许夹假名（那说明那一行被写成了日文）——同一条形状，反方向也得成立。
        Assert.DoesNotMatch("[\\u3040-\\u30ff]", entry.Zh);
        }
    }

    /// <summary>
    /// <b>表里的文本不过 XAML 解析器</b>，所以任何一列都不许出现 <c>&gt;</c> 这类转义写法。
    /// <para>从表里取出的 <c>&gt;</c> 会照原样显示成四个字符——而它写在 XAML 里才是对的。
    /// 这条判据是句式（禁转义形状），不是逐字禁某一处，所以新加一行带尖括号的文案时会正常放过原文。</para>
    /// </summary>
    [Fact]
    public void TableTextIsFinalDisplayTextNotXmlMarkup()
    {
        foreach (var entry in UiStrings.All)
        {
            foreach (var text in new[] { entry.Zh, entry.En, entry.Ja })
            {
                Assert.DoesNotContain("&gt;", text, StringComparison.Ordinal);
                Assert.DoesNotContain("&lt;", text, StringComparison.Ordinal);
                Assert.DoesNotContain("&amp;", text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 任何一列都不许混进<b>看不见的控制字符</b>（现实读数：162 行里 0 行含换行、制表或退格）。
    /// <para>这一格是批次 WI-2f 现场立起来的。表里 <c>settings.data.autoBackupNote</c> 那句要写出备份目录
    /// <c>%LOCALAPPDATA%\StarMark\backups</c>——而普通 C# 字符串里 <c>\b</c> 是一个退格符、<c>\S</c> 是非法转义：
    /// 少了 <c>@</c> 的话前一种写错<b>编得过</b>，屏幕上那行只是"怪怪的"（中间少一个字符、日志里也看不出来），
    /// 而后一种直接编不过。两种坏法里只有一种会被编译器抓住，所以这一格读的是<b>编进程序集的实际字符</b>
    /// （<see cref="UiStrings.All"/>），不是源码长相——源码里看着像 <c>\b</c> 的两个字符，运行时可能已经是一个 0x08。</para>
    /// <para>禁的是 U+0000–U+001F 整段（含换行／制表）：那是"这一版所有句子都是单行"的现实，
    /// 真要一句多行时，改这一格并把理由写在这里，而不是让一个手滑的转义悄悄上线。</para>
    /// </summary>
    [Fact]
    public void NoRowCarriesAnInvisibleControlCharacter()
    {
        Assert.NotEmpty(UiStrings.All);   // 遍历活着的前提：不是拿空表读出"零违规"
        foreach (var entry in UiStrings.All)
        {
            foreach (var (text, column) in new[] { (entry.Zh, "中文"), (entry.En, "英文"), (entry.Ja, "日文") })
            {
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    Assert.False(c < '\u0020',
                        $"{entry.Key} 的{column}列第 {i} 个字符是控制字符 U+{(int)c:X4}" +
                        "（路径里的 \\b 在普通字符串里是退格 ⇒ 那一行必须写成 verbatim）");
                }
            }
        }
    }

    /// <summary>
    /// 「快捷键」这一族的句子<b>不许点名任何组合键</b>（Ctrl／Alt／Win 那种）。
    /// <para>这一栏的键位是用户可改的，而表里的句子是死的：抄进去的那一刻起，用户改一次键，
    /// 那句话就变成假话（批次 VC 的裁决；「这几件事的按键」那一节之所以从绑定表现场渲染，同一个理由）。
    /// 原先写在 <c>ResetHotkeys_Click</c> 里的那句确认就抄了一颗 <c>Ctrl + Alt + Space</c>，
    /// 还把它说成"只保留这一颗"——出厂默认里其实还有截图／贴图／穿透／画布那一整批。</para>
    /// <para>判据按<b>键的前缀</b>划射程，不是全表：搜索框那句里的 <c>Ctrl+Enter</c> 说的是本地固定按键、
    /// 不随用户改全局热键而变化，不许被这一格误伤。录制说明里那句"按 Esc 结束／Backspace 清除"
    /// 也不在禁列——那两颗是录制器自己的控制键，代码里没给它们留改的入口。</para>
    /// </summary>
    [Fact]
    public void HotkeyFamilySentencesNeverNameAGesture()
    {
        var rows = UiStrings.All
            .Where(e => e.Key.StartsWith("settings.hotkeys.", StringComparison.Ordinal))
            .ToArray();
        Assert.True(rows.Length >= 11,
            $"这一族只剩 {rows.Length} 行（批次 WI-42 精简掉「动作 → 快捷键」那句重复说明后现场是 11）——整段被清空不是改措辞");
        foreach (var entry in rows)
        {
            foreach (var text in new[] { entry.Zh, entry.En, entry.Ja })
            {
                Assert.DoesNotMatch(@"\b(Ctrl|Alt|Win)\b", text);
                Assert.DoesNotMatch(@"\bF[1-9]\b", text);
            }
        }
    }

    /// <summary>
    /// 「屏幕画布」那一栏的说明句<b>不许再写它自己那一组有几条快捷键</b>（批次 WI-2g）。
    /// <para>搬那句进表时逐字读它，读出一句已经和屏幕对不上的话：原文写"画布内的<b>九条</b>快捷键"
    /// （WD-3 那批确实是九条），而 <c>HotkeyActions.Canvas</c> 今天是<b>十三条</b>——WK/WM/P3 各加了图形、
    /// 复制、贴出，谁都没回头看这句死在标记里的话。数接不上出处（<c>LocText</c> 只会整句重贴，
    /// 读数得走组合句那一族，坑表 #279），所以这里的口径是<b>那一行不写数</b>：
    /// 下面那张生成的表会自己把每一条列出来，不缺这一个数字。</para>
    /// <para>⚠ 射程<b>只画在这一行</b>，不是全表：同栏「截屏」那句的"那三条发起框选的全局热键"
    /// 与「截图带画布」那句的"那三条产出动作"今天<b>都对得上</b>（截图 F1／贴图 F3／识字，与工具条那三个产出）。
    /// 把禁字面扩到全表＝拿判据去改两句真话，那是另一回事——那两句欠的是"数要接出处"，
    /// 已按开放项登记在账本（P-170），不在这一格里顺手改掉。</para>
    /// </summary>
    [Fact]
    public void CanvasCopyNoLongerCountsItsOwnShortcuts()
    {
        var row = UiStrings.All.Single(e => e.Key == "settings.extend.canvasNote");
        foreach (var (column, text) in new[] { ("中文", row.Zh), ("英文", row.En), ("日文", row.Ja) })
        {
            Assert.DoesNotMatch(@"[一二三四五六七八九十两0-9]+\s*[条项个支]\s*(快捷键|热键)", text);
            Assert.DoesNotMatch(
                @"(?i)\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|\d+)\s+(canvas\s+)?(shortcut|hotkey)",
                text);
        }
    }

    /// <summary>
    /// 同一行的<b>各列</b>必须用同一组位置式占位符（<c>{0}</c>／<c>{1}</c>…）；批次 WI-3 起是中英日三列。
    /// <para>换语序就得换编号：中文"还剩 {0} 条"翻成 "{1} items left" 那种配错，
    /// 界面上是一个看起来完全合法的数字挂错了句子（#234 那一族）。这条判据今天<b>没有输入</b>
    /// （现场量过：47 行里 0 行带占位符——带读数的句子还在 WI-2），它是句式哨兵而不是"跑过了什么"，
    /// 所以这里同时钉住"遍历确实在跑"，别让空遍历读成通过。</para>
    /// </summary>
    [Fact]
    public void EveryColumnOfARowUsesTheSamePlaceholderNumbers()
    {
        Assert.NotEmpty(UiStrings.All);   // 抽取器活着的前提：遍历的对象不是空表
        foreach (var entry in UiStrings.All)
        {
            Assert.Equal(Slots(entry.Zh), Slots(entry.En));
            Assert.Equal(Slots(entry.Zh), Slots(entry.Ja));
        }
    }


    /// <summary>
    /// <b>日文那一列必须整列有字，而且不许是中文列的抄本</b>（批次 WI-3）。
    /// <para>为什么不拿"必须含假名"当尺子（我先在这把尺子上摔过一次）：现场数过，表里有<b>二十多行正确的日文是纯汉字</b>
    /// ——<c>非表示</c>／<c>設定</c>／<c>今日</c>／<c>追加</c>／<c>診断</c>，那正是日语的写法。
    /// "必须含假名"会把它们一起判死，而它想拦的那件事（照抄中文）反倒照样看不见：抄过来的中文常常也没假名。</para>
    /// <para>所以判据换成<b>逐字相等</b>：日文列与中文列一个字不差＝那行没翻。确有同形的例外
    /// （<c>RSS</c>／<c>Star</c> 这类专有名词、「最近／条件／停止」这类两语同词、以及三行纯胶水 <c>{0}·{1}</c>）
    /// 必须在这里<b>点名并写理由</b>。名单是<b>双向</b>的：不在名单里却相同＝红；在名单里但两列其实已经不同＝也红
    /// （<b>过期的名单比没有名单更坏</b>，它会让下一批以为那一行还抄着中文）。</para>
    /// <para>⛔ 不许为了变绿把整行摘进名单——那等于当场承认那一行没翻。</para>
    /// </summary>
    [Fact]
    public void TheJapaneseColumnIsFullAndNotTheChineseColumnCopied()
    {
        Assert.NotEmpty(UiStrings.All);       // 空表读出来的"零违规"不算证据
        foreach (var entry in UiStrings.All)
            Assert.False(string.IsNullOrWhiteSpace(entry.Ja), $"{entry.Key} 的日文列是空的");

        var reasons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nav.rss"] = "专有名词 RSS，两语同形",
            ["source.starTip"] = "动作名 Star（GitHub 术语），两语同形",
            ["sort.stars"] = "“Stars 数”两语同形",
            ["sort.recent"] = "最近：日语也写「最近」",
            ["kind.conditions"] = "条件：日语也写「条件」",
            ["settings.ai.stop"] = "停止：日语也写「停止」",
                        ["tray.tip"] = "产品名 StarMark，三语同形",
                        ["dialog.save"] = "「保存」日文同字（WI-2p）",
            ["glance.festival.qingming"] = "「清明」中日同字：日本也把这一天写成「清明」，同颗节日（批次 WI-56／P-186）",
            ["glance.festival.qixi"] = "「七夕」中日同字：同一颗节日在两文化里都写这两个汉字（批次 WI-56／P-186）",
            ["diag.fact"] = "直通行「{0}」：那一格交的是机器事实（数据库路径、版本号、ETag、启动刻度行），三语同形是刻意的（批次 WI-57／P-187）",
            ["health.dup.item"] = "全角括号「（）」中日同形，en 列给半角——那颗宽度就是语言内容（批次 WI-57 顺手交表）",
                        ["guide.hotkey.bound"] = "占位骨架「{0}：{1}」三语同形（WI-2q）",
                        ["guide.page.rss"] = "RSS 三语同字（WI-2q）",
                        ["upd.verdict.emptyTag"] = "「空」三语同字（WI-2v）",
                        ["hotkey.err.pidJoin"] = "pid 分隔符「、」三语同字（WI-2s）",
                        ["hotkey.err.summaryItem"] = "占位骨架「{0}：{1}」三语同形（WI-2s）",
["trending.daily"] = "今日：日语也写「今日」",
            ["widget.calc.tabQuick"] = "常用：日语也写「常用」（常用ツール）",
            ["widget.cd.save"] = "保存：日语也写「保存」",
            ["widget.pomo.stop"] = "停止：日语也写「停止」",
            ["trending.statusViaAge"] = "纯胶水行「{0}·{1}」，没有词可翻",
            ["trending.statusNotice"] = "纯胶水行「{0}｜{1}」，没有词可翻",
            ["trending.fetch.httpStatus"] = "「HTTP {0}（{1}）」：码与状态名都是机器事实，中日两档都用全角括号 ⇒ 同形（批次 WI-58／P-190）",
                        ["trending.notice.compose"] = "纯骨架「{0}（{1}）」：中文与日文都用全角括号，两语同形（WI-2z）",
                        ["trending.notice.join"] = "两颗槽的骨架「{0}；{1}」中日同形，en 列是 \", \"（WI-2z 立为裸分隔符，批次 WI-58／P-190 改成骨架：代码里拼成品字＝语言被定死在抓取那一刻）",
            ["widget.glance.ym"] = "月题骨架「{0}年{1}月」中日同形（ja 也用年月，en 列走 ISO 式不同形，WI-31）",
            ["health.join.list"] = "列表连接号「、」中日同形，en 列是 \", \"（WI-33）",
            ["ocr.how.paren"] = "纯括号骨架「（{0}）」：中日都用全角括号，en 列是半角带前空格（WI-46b）",
            ["hotkey.conflict.join"] = "冲突名单的连接号「、」中日同形，en 列是 \", \"（WI-40-1，与 health.join.list／hotkey.err.pidJoin 同形）",
                        ["rss.statusJoin"] = "纯胶水行「{0}{1}」，没有词可翻",
            ["widget.menu.nameWithSummary"] = "纯骨架「{0}（{1}）」：中文与日文都用全角括号，两语同形（WI-37，与 trending.notice.compose 同形）",
            ["alarm.stop.button"] = "纯骨架「{0}（{1}）」：那颗按钮＝名字那颗键＋条数，中英日各自都不换形状（WI-37-C）",
            ["time.weekday.stem.0"] = "月历表头第一格：星期日的「日」中日同字（那一格只装一个字，⛔ 为了不同形去改表头）（WI-37-C）",
            ["anno.color.white"] = "颜色名「白」中日同字（日文里它就是写作白，⛔ 为了不同形改成片假名反而不像界面用语）（批次 WI-49／P-181）",
            ["alarm.days.pick1"] = "只勾一天时那一行就是「{0}」本身，中日英都没有分隔符可放（WI-37-C）",
            ["pin.opacity.label"] = "「不透明度」日文也照汉字写（⛔ 为不同形改成「透過度」，那在贴图语境里是另一个意思）（批次 WI-50／P-179）",
            ["pin.bar.opacity"] = "纯胶水行「{0} {1}%」：词在 label 那颗里，这一行没有词可翻（批次 WI-50／P-179）",
            ["pin.menu.save"] = "「保存」日文同字（与 widget.cd.save 同形，⛔ 为不同形改成「ファイル保存」反而长出第二条尺）（批次 WI-50／P-179）",
            ["anno.color.purple"] = "颜色名「紫」中日同字（与 anno.color.white 同形：日文里它就写作紫，改成「パープル」反而不像这一排界面用语）（批次 WI-52／P-183）",
            ["widget.appearance.target.bg"] = "「背景色」日文同字（日语界面里就写背景色；⛔ 为不同形改成「背景」，那一档就说不清是颜色还是图）（批次 WI-52／P-183）",
            ["widget.cd.remainClock"] = "纯骨架「{0}」：那一格只装一个钟点读数（05:20），三语同形，没有词可翻（批次 WI-54／P-182a）",
            ["calc.num"] = "纯骨架「{0}」：那一格只装一个读数，三语同形，没有词可翻（批次 WI-55b／P-182b）",
            ["common.rawValue"] = "纯骨架「{0}」三列同形：这一行不是句子，是「把一样东西原样交出去」那道门（批次 WI-61d／P-185）——对话框形参改收 LocMessage 之后，文件路径、条目标题、组件默认名这些**读数**要有合法出口，⛔ 为此把它们各抄一份字面量进表（那才是第二份措辞出处）",
            ["card.title.withCategory"] = "纯骨架「{0} · {1}」三列同形：右下角提示卡抬头那句「谁在说话 · 说了什么」的分隔符（批次 WI-61d 片2／P-185）——卡片形参改收 LocMessage 之后，抬头由两份身份拼成，⛔ 分隔符留在 C# 里（那是第二份拼装出处，✅P-178 当年把「·」收成一颗主人正是为此）",
            ["settings.ai.result.full"] = "纯胶水「{0}…{6}」：一句话交代这一轮的那七段各是一句独立的话，拼接位⛔ 译（✅WI-60b 那颗 widget.mon.note.join 同一口径：代码里拼 + 就是把语序钉死在中文那一侧）（批次 WI-41／P-188 第四刀）",
            ["settings.ai.tagJoin"] = "连接位「{0}、{1}」：中日都用顿号，英文用逗号＋空格——连的是<b>用户自己起的标签名</b>，这一行没有词可翻（批次 WI-41／P-188 第四刀）",
            ["settings.ai.groupJoin"] = "连接号「、」中日同形，en 列是 \", \"：那一行没有词可翻，连的是<b>用户自己起的标签名</b>与<b>条目编号</b>（批次 WI-40-3b 片2／P-175 缺口②，与 health.join.list／hotkey.conflict.join／settings.ai.tagJoin 同形）",
            ["settings.ai.usage.featureBlock"] = "「。{0}。」：中日都用全角句号，英文换成半角点＋空格——那一格包的是一串读数，句号是标点不是词（批次 WI-41／P-188 第四刀）",
            ["settings.ai.usage.full"] = "纯胶水「{0}{1}{2}{3}」：用量面板那句由「窗口合计／估算说明／按功能分开／裁决」四段拼成，缺的段交 null（格式化时那一格自己变空），⛔ 在代码里拼字符串（批次 WI-41／P-188 第四刀）",
            ["calc.num.percent"] = "纯骨架「{0}%」：百分号是记号不是词，⛔ 为了不同形改成全角「％」＝让读数形状随语言变（批次 WI-55b／P-182b）",
            ["calc.quick.field.qtyA"] = "「数量」日文界面同字（同排的「价格／価格」只差一个汉字，⛔ 为不同形改成「量 A」反而不像界面用语）（批次 WI-55b／P-182b）",
            ["calc.quick.field.qtyB"] = "与 qtyA 同一形状：日文界面就写「数量 B」（批次 WI-55b／P-182b）",
            ["calc.quick.line.caution"] = "「注意」：日语界面里那两个汉字就是这么写，那一行没有别的词可换（批次 WI-55b／P-182b）",
            ["calc.cat.speed"] = "速度：日语界面也照汉字写「速度」（⛔ 为不同形改成「スピード」，换算表那一档不像界面用语）（批次 WI-55a／P-182b）",
            ["calc.cat.temperature"] = "温度：日语界面也照汉字写「温度」（与 calc.cat.area「面積」同一写法）（批次 WI-55a／P-182b）",
            ["calc.unit.nmi"] = "海里：日语同字（航海单位在两语里都这么写）（批次 WI-55a／P-182b）",
            ["calc.unit.s"] = "秒：日语同字（同一类的 ms 写「ミリ秒」，那一格本来就不同形）（批次 WI-55a／P-182b）",
            ["calc.unit.kb"] = "存储单位符号 KB：三语界面都写 KB（与「字节 B／比特」不同，那一颗有词）（批次 WI-55a／P-182b）",
            ["calc.unit.mb"] = "存储单位符号 MB：同 calc.unit.kb（批次 WI-55a／P-182b）",
            ["calc.unit.gb"] = "存储单位符号 GB：同 calc.unit.kb（批次 WI-55a／P-182b）",
            ["calc.unit.tb"] = "存储单位符号 TB：同 calc.unit.kb（批次 WI-55a／P-182b）",
            ["calc.unit.pb"] = "存储单位符号 PB：同 calc.unit.kb（批次 WI-55a／P-182b）",
            ["calc.convert.result"] = "纯骨架「{0} {1} = {2} {3}」：词全在那两颗单位名的键里，这一行没有词可翻（批次 WI-55a／P-182b）",
            ["widget.mon.row.cpu"] = "「CPU」三语同形：拉丁字母缩写，日文界面也这么写（⛔ 为了不同形改成「プロセッサ」，那一格就不是这一行的写法了）（批次 WI-60／P-193）",
            ["widget.mon.note.join"] = "两颗槽的骨架「{0}；{1}」中日同形，en 列是 \", \"：从前这句在代码里 string.Join 拼成品字＝语言被定死在采样那一刻（批次 WI-60／P-192，与 trending.notice.join 同形）",
            ["widget.mon.upRate"] = "纯骨架「↑{0}」：箭头是记号不是词，速率是机器事实（⛔ 把箭头搬进词里＝让读数形状随语言变）（批次 WI-60／P-192）",
            ["settings.data.autoBackupSentence"] = "纯胶水行「{0}{1}」，没有词可翻（批次 WI-61c／P-188，与 rss.statusJoin／widget.mon.note.join 同形——从前那半句在 C# 里用 + 拼成品字，语序就被定死在中文那一侧）",
            ["settings.data.backup.lineJoin"] = "纯胶水行「{0}{2}{1}」三列同形：把回执的两半连起来的那颗位置，⛔ 在 C# 里拼 +（同 autoBackupSentence／widget.mon.note.join）；{2} 交的是<b>换行</b>那颗布局，⛔ 进表（规则 #100，先例＝settings.data.deleteBackupBody）（批次 WI-62／P-193）",
        };

        var actual = UiStrings.All
            .Where(e => string.Equals(e.Zh, e.Ja, StringComparison.Ordinal))
            .Select(e => e.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var listed = reasons.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // 两份名单必须逐字相等：多一颗＝有人抄中文，少一颗＝有人删了那行而名单没跟着改。
        Assert.Equal(listed, actual);
        foreach (var key in actual)
            Assert.True(reasons.ContainsKey(key), $"{key} 两列相同却没写理由");
    }

    /// <summary>取一行里的占位符编号，去重后按数字序。<b>返回数组而不是集合</b>：
    /// <c>Assert.Equal</c> 对 <c>SortedSet</c> 走引用相等，那样的判据永远红，写的人只会去改判据。</summary>
    private static string[] Slots(string text)
        => Regex.Matches(text, @"\{(\d+)\}").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(n => int.Parse(n)).ToArray();

    /// <summary>
    /// 键的形状：<b>每段以小写字母开头</b>、点分段、至少两段，段内允许 camelCase。
    /// <para>段内允许大写不是让步而是原样：这些键名会照抄进 C# 代码（<c>Locale.T("top.syncCancel")</c>），
    /// 全小写的 <c>top.synccancel</c> 只是把同一句词拆成难以读的一坨。而被钉死的是<b>首字符必须小写</b>
    /// 与<b>至少两段</b>：单段的名字（<c>tree</c>）看不出它属于哪一屏，两三个月后就会出现第三处也叫
    /// <c>tree</c> 的东西；大写开头的段则会让同一族键在设置档与代码里出现两种写法。</para>
    /// </summary>
    [Fact]
    public void KeysAreDottedWithLowerCamelSegmentsAndAtLeastTwoOfThem()
    {
        foreach (var key in UiStrings.Keys())
        {
            // WI-2q 起"有序行"的键允许纯数字尾段（guide.page.search.1／guide.first.s2.3）——
            // 行号是它唯一的语义，编个词反而造假；其余段仍必须小驼峰开头。
            Assert.Matches(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*|\.\d+)+$", key);
            Assert.DoesNotContain("..", key, StringComparison.Ordinal);
        }
    }

    // ===== 认代码与挑语言 =====

    /// <summary>只认 <see cref="AppLanguages"/> 里那几串（zh／en／ja）；
    /// <b>认不得的一律 null，不做"像日文就先给中文"的就近猜测</b>。
    /// <para>手改设置档、或旧版本写过别的写法，被猜成一门语言的表现是"我明明写了 en 他偏给我中文"，
    /// 而那件事在界面上没有任何一条线索能看出来。</para></summary>
    [Theory]
    [InlineData("zh", AppLanguage.Chinese)]
    [InlineData("en", AppLanguage.English)]
    [InlineData("  EN ", AppLanguage.English)]
    [InlineData("zh-CN", null)]          // 带子标签的写法不是这一层的事（见 PickFrom）
    [InlineData("ja", AppLanguage.Japanese)]
    [InlineData(" JA ", AppLanguage.Japanese)]
    [InlineData("jpx", null)]            // 认不得的那串仍然是"没表态"，⛔ 不许就近猜
    [InlineData("", null)]
    [InlineData(null, null)]
    public void OnlyTheListedCodesAreRecognisedAndEverythingElseIsNoStatement(string? code, AppLanguage? expected)
        => Assert.Equal(expected, AppLanguages.FromCode(code));

    /// <summary>语言代码<b>存进设置档的必须是代码本身</b>，不能是序号：
    /// 往 <c>Pickable</c> 中间插一门语言会把所有已存的序号平移一位，而那种坏读不出来——它看起来完全合法。</summary>
    [Fact]
    public void CodesRoundTripThroughTheEnumAndAreNotOrdinals()
    {
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            var code = AppLanguages.CodeOf(language);
            Assert.Equal(language, AppLanguages.FromCode(code));
            Assert.DoesNotMatch(@"\d", code);   // 存的是代码，不是 Pickable 里的下标
        }
        // 新增一格而 CodeOf 没配 → 当场炸，而不是静默给个"最近的"
        Assert.Throws<ArgumentOutOfRangeException>(() => AppLanguages.CodeOf((AppLanguage)99));
    }

    /// <summary>系统的语言列表是<b>优先级表</b>：按顺序取第一个认得的，而不是只看第一条。
    /// <para>"德文为主、英文为二"的用户拿不到德文界面时应落到英文；只看第一条就等于替他决定了他看不懂英文。
    /// 批次 WI-3 起 <c>ja-JP</c> 本身是认得的，所以那一例仍然留着，只是它不再演示"跳过"。</para></summary>
    [Fact]
    public void SystemTagsArePickedInOrderAndUnknownTagsAreSkippedNotGuessed()
    {
        Assert.Equal(AppLanguage.Japanese, AppLanguages.PickFrom(new[] { "ja-JP", "en-GB" }));
        Assert.Equal(AppLanguage.English, AppLanguages.PickFrom(new[] { "de-DE", "en-GB" }));
        Assert.Equal(AppLanguage.Chinese, AppLanguages.PickFrom(new[] { "zh-Hans-CN" }));
        Assert.Equal(AppLanguage.Chinese, AppLanguages.PickFrom(new[] { "  ", "ZH-hant-tw" }));
        Assert.Null(AppLanguages.PickFrom(new[] { "de-DE", "fr-FR" }));
        Assert.Null(AppLanguages.PickFrom(Array.Empty<string>()));
        Assert.Null(AppLanguages.PickFrom(null!));
    }

    /// <summary>读环境那一手不许抛：受限配置下拿不到文化，应当是"没有信号"，不是启动失败。</summary>
    [Fact]
    public void ReadingTheMachineLanguagesNeverThrowsAndNeverYieldsBlanks()
    {
        var tags = AppLanguages.SystemLanguageTags();
        Assert.NotNull(tags);
        Assert.All(tags, tag => Assert.False(string.IsNullOrWhiteSpace(tag)));
    }

    /// <summary>
    /// 语言名<b>用它自己的写法，且不跟着界面语言变</b>。
    /// <para>这条看着像口味，其实是可用性：语言选择器是拿去认自己母语的。把「日本語」印成 "Japanese"，
    /// 一位只认得假名的用户在那一列里找不到自己。所以这里必须换一次界面语言再读一次名。</para>
    /// </summary>
    [Fact]
    public void LanguageNamesAreEndonymsAndDoNotFollowTheInterfaceLanguage()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.Chinese);
            var chineseUi = AppLanguages.Pickable.Select(AppLanguages.SelfName).ToArray();
            Locale.TrySet(AppLanguage.English);
            Assert.Equal(chineseUi, AppLanguages.Pickable.Select(AppLanguages.SelfName).ToArray());
            // 现场读数：选项三颗，其中「日本語」必须原样在那一列里（翻成 Japanese 就没人认得自己了）
            Assert.Contains("日本語", chineseUi);
        }
        finally
        {
            Locale.TrySet(before);
        }
    }

    // ===== 该用哪一版 =====

    /// <summary><b>用户表过态优先</b>，没表过态才看系统，两样都读不到才落默认档。</summary>
    [Fact]
    public void AStatementFromTheUserOutranksTheMachineLanguage()
        => Assert.Equal(AppLanguage.English, Locale.Resolve("en", new[] { "zh-CN" }));

    /// <summary>没表态时看系统；<b>设置档里那串认不得也算"没表态"</b>，于是仍然看系统，
    /// 而不是"读坏了就回默认档"——后者会让一次手改把用户永久钉在中文上。</summary>
    [Fact]
    public void AnUnreadableSavedCodeFallsBackToTheMachineNotToTheDefault()
    {
        Assert.Equal(AppLanguage.English, Locale.Resolve(null, new[] { "en-US" }));
        Assert.Equal(AppLanguage.English, Locale.Resolve("zz", new[] { "en-US" }));
    }

    /// <summary>两样都没有信号才落默认档（＝这份文案作者的语言）。</summary>
    [Fact]
    public void WithNoSignalAtAllTheDefaultColumnWins()
    {
        Assert.Equal(AppLanguage.Chinese, Locale.Resolve("zz", new[] { "de-DE" }));
        Assert.Equal(AppLanguages.Default, Locale.Resolve(null, Array.Empty<string>()));
    }

    // ===== 取词与换语言那一次出声 =====

    /// <summary>换语言<b>只出声一次，而且相同就不出声</b>。
    /// <para>这两半都在防同一种坏：出声太多次，订阅方（把已注册的元素重贴一遍）就白跑几轮；
    /// 该出声却不出声，屏幕上留下的就是上一版的词——半中半英。
    /// 打开设置页看完又原样点回去，是最常见的"原样点回去"，不该重贴整窗。</para></summary>
    [Fact]
    public void ChangingTheLanguageRaisesTheEventExactlyOnceAndNotWhenUnchanged()
    {
        var hits = 0;
        void OnChanged() => hits++;
        Locale.Changed += OnChanged;
        try
        {
            var other = Locale.Current == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese;
            Assert.True(Locale.TrySet(other), "换成另一门语言必须报告化");
            Assert.Equal(1, hits);
            Assert.False(Locale.TrySet(other), "原样点回去必须报告没变");
            Assert.Equal(1, hits);
            Assert.True(Locale.TrySet(Locale.Current == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese));
            Assert.Equal(2, hits);
        }
        finally
        {
            Locale.Changed -= OnChanged;
        }
    }

    /// <summary>同一句界面文案在<b>每一列</b>里都取得到，而且<b>换语言当场就换词</b>（不需要重建元素）。</summary>
    [Fact]
    public void TheSameKeyAnswersInEveryLanguageAndChangesAtOnce()
    {
        var before = Locale.Current;
        try
        {
            foreach (var language in AppLanguages.Pickable)
            {
                Locale.TrySet(language);
                // ⛔ 这里不能给 Assert.Equal 传第三颗"消息"参数：xunit 会把三参重载解析到
                // IAsyncEnumerable／IEqualityComparer 那一族上，报出来的是一句看不懂的类型错。
                Assert.Equal(TextOf(language, "nav.tree"), Locale.T("nav.tree"));
            }

            Locale.TrySet(AppLanguage.Chinese);
            var zh = Locale.T("nav.tree");
            Locale.TrySet(AppLanguage.Japanese);
            Assert.NotEqual(zh, Locale.T("nav.tree"));
            Assert.Equal(TextOf(AppLanguage.Japanese, "nav.tree"), Locale.T("nav.tree"));
        }
        finally
        {
            Locale.TrySet(before);
        }
    }

    /// <summary>
    /// 键没登记时<b>不抛、也不装没看见</b>：屏幕上出现 <c>⟨nav.tree⟩</c> 这种形状，用户会截图来问，
    /// 日志里也留着那句。相比之下"那一颗按钮显示成上一个语言的词"既看不见也查不到。
    /// <para>这里必须用一颗表里真没有的键，所以先断言它确实不在表里——否则哪天有人把它写进表，
    /// 这一格会静默变成"取到了词"，而它就不再查任何东西了。</para>
    /// </summary>
    [Fact]
    public void AnUnregisteredKeyShowsItselfOnScreenInsteadOfASilentWrongWord()
    {
        const string key = "nav.thisKeyIsDeliberatelyAbsent";
        Assert.False(UiStrings.TryGet(key, out _));
        Assert.Equal("⟨" + key + "⟩", Locale.T(key));
    }

    /// <summary>带读数的句子走 <c>Fmt</c>，数字一律走 <see cref="System.IFormatProvider"/> 的不变文化
    /// （这一族在 <c>DisplayDateFormatGateTests</c>／批次 RZ 已经定死：读数不许跟着界面语言变小数点）。
    /// <para><b>覆盖面登记</b>：表里<b>已经</b>有带占位符的行（批次 WI-2j／WI-2k 起，如 trending.emptyNoMatch
    /// 与 time.minutesAgo），所以这一格钉的是"多给的实参不炸、没有占位符时原样返回"；
    /// 配错号那一头由 <see cref="EveryColumnOfARowUsesTheSamePlaceholderNumbers"/> 逐行比对钉住。</para></summary>
    [Fact]
    public void FormattingAnEntryWithoutSlotsReturnsItUnchanged()
    {
        Assert.Equal(Locale.T("nav.tree"), Locale.Fmt("nav.tree"));
        Assert.Equal(Locale.T("nav.tree"), Locale.Fmt("nav.tree", 1234.5));
        Assert.Equal("1234.5", 1234.5.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <b>嵌在句子里的那颗读数跟着语言当场改口</b>（批次 WI-2k 的机制核心；能真跑所以住在这一族）。
    /// <para>热榜状态行那句是"外层句子＋内层读数句子"两层。如果内层在交出前就取完词，换语言那一刻
    /// 外层翻了、内层留在旧语言里——屏幕上就是半中半英，而形状闸门（属性类型那条）看不见它。
    /// 所以这一格钉的是<b>同一颗对象、只换语言</b>：两次渲染必须分别是两种语言。</para>
    /// </summary>
    [Fact]
    public void ANestedReadingIsRenderedInTheLanguageOfTheMoment()
    {
        var composed = LocMessage.Of("trending.statusCached",
            LocMessage.Of("trending.viaTrendingPage"), LocMessage.Of("time.minutesAgo", 10));

        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal("来源：GitHub 热榜页·10 分钟前已缓存", composed.Render());

        Locale.TrySet(AppLanguage.English);
        Assert.Equal("Source: GitHub trending page · 10 minutes ago in cache", composed.Render());

        // 批次 WI-3：同一颗对象第三次阅读必须换第三种语言——嵌套的那半没有第二份主人
        Locale.TrySet(AppLanguage.Japanese);
        Assert.Equal("出所：GitHub 熱榜ページ·10 分前キャッシュ済み", composed.Render());
    }
}
