using System.Text.Json;

namespace StarMark.Abstractions;

/// <summary>
/// <c>items.extra_json</c> 这一列的<b>读法与写法</b>的唯一出处（P-17，批次 SK）。
/// <para>登记时写的是"三处读谓词没守卫"。按<b>后果</b>重扫＝<c>src/</c> 里 <b>9 个</b> JSON 函数的实参位
/// 落在这列上（<c>ItemRepository.Search.cs</c> 五处、<c>ItemRepository.Items.cs</c> 两处、
/// <c>ItemRepository.Local.cs</c> 两处）；其中<b>只有一处</b>早就写了守卫，而那句守卫是<b>手抄</b>的
/// （<c>ClipBucketClause</c> 里一整串 <c>CASE WHEN json_valid(...) THEN ... ELSE '{}' END</c>），
/// 于是"第二份守卫"和"第二份真值"是同一件事——本批把<b>那一句</b>收成一颗，抄本一律改道。</para>
/// <para><b>为什么必须守卫（这是机制，不是风格）</b>：SQLite 的 <c>json_extract(坏串, …)</c> 抛的是
/// <b>语句级</b>错误，不是"这一行给 NULL"——一行被手改或被坏备份塞进来的非法 JSON，就能让
/// "按语言筛选浏览""按最近 Star 排序""语言下拉"整页失败（用户读作"加载失败"）。
/// <c>json_set</c>/<c>json_remove</c> 更狠：不报错，直接把<b>整列写成 NULL</b>，
/// 于是改一个布尔键顺手抹掉用户其余元数据。</para>
/// <para><b>放在 WHERE 里的 <c>AND json_valid(extra_json)</c> 挡不住这件事</b>：SQLite 不保证 AND 各项的
/// 求值次序，被筛掉的那一行仍可能先参与 <c>json_extract</c>。守卫必须包在<b>函数实参</b>上
/// （<c>CASE</c> 的分支是惰性的，这才是逐行护住）。同理，行过滤那种"坏行这次跳过"的语义
/// 与"表达式别抛"是两件事，前者留在调用点，这里只管后者。</para>
/// <para><b>空串也是非法 JSON</b>（<c>json_valid('')=0</c>），而 <c>''</c> 恰是这条列的历史产物
/// （写绑定只把 null 归成 NULL，<c>LanguageDetector</c> 解析失败时又可能原样回吐）。
/// 所以写侧的口径是"<b>读不出对象的值＝没有元数据</b>"：坏串与空串一律归 <c>NULL</c>，
/// 而不是把一颗会炸整页查询的值留在库里。C# 侧的读者（<see cref="LocalItemState"/>、
/// <c>ClipboardEntry.FormatOf</c>）本来就把 null 与坏串同样兜底，所以这一步<b>不改变任何用户可见读数</b>，
/// 只是把"每种语言各自兜底"变成"进库之前就不留坏值"。</para>
/// </summary>
public static class ExtraJsonGuard
{
    /// <summary>守卫兜底用的空对象——坏行按"没有任何键"处理，于是 <c>COALESCE</c> 之类的回落照常生效。
    /// 值里<b>带着 SQL 的单引号</b>：拼出来的必须是字符串字面量 <c>'{}'</c>，裸 <c>{}</c> 是语法错。</summary>
    private const string SqlEmptyObject = "'{}'";

    /// <summary>
    /// 把 <paramref name="columnRef"/>（<c>extra_json</c> 或带别名的 <c>i.extra_json</c>）包成
    /// "坏值当作空对象"的表达式。<b>凡是把这一列喂给 <c>json_extract</c>/<c>json_set</c>/<c>json_remove</c>
    /// 的地方都必须走这里</b>——闸门 <c>ExtraJsonGuardGateTests</c> 钉住了这一条。
    /// </summary>
    public static string Safe(string columnRef)
        => $"(CASE WHEN json_valid({columnRef}) THEN {columnRef} ELSE {SqlEmptyObject} END)";

    /// <summary>
    /// 这串能不能当元数据留着：<b>合法 JSON 对象</b>才留；null 原样留；空串／空白／非法 JSON／合法但非对象
    /// （数组、裸数字、<c>null</c> 字面量）一律 <c>false</c>。
    /// <para>判据取"对象"而不是"合法 JSON"：这一列的每一个键都是 <c>$.xxx</c> 路径取法，
    /// 一个顶层数组（<c>json_valid('[1,2]')=1</c>）在 SQL 侧取不出值、在 C# 侧也读不出键，
    /// 留着只是把"整句崩"换成"整行读不懂"——那正是本批要消掉的两种形态之一。</para>
    /// </summary>
    public static bool IsStorableObject(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 入库／导入前的那一刀：读不出对象的一律归 <c>null</c>（＝这条记录没有元数据），合法对象逐字透传。
    /// <para>用它的地方只有备份还原那道信任边界（<c>BackupService.ValidatePayload</c>）——
    /// 在线生产者（GitHub／书签／Everything／本地状态）交出的都是 <c>JsonSerializer</c> 现拼的串，
    /// 不需要在这里多设一道卡。"坏值要静默归 null 还是整份备份拒收"是宿主的取舍，
    /// 这里只回答"这串还能不能用"。</para>
    /// </summary>
    public static string? SanitizeForStore(string? extraJson)
        => string.IsNullOrWhiteSpace(extraJson) || !IsStorableObject(extraJson) ? null : extraJson;
}
