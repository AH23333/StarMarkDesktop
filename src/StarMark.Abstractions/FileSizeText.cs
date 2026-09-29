#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 字节数的界面表达。1024 进制（与资源管理器同基数），最多两位有效小数。
/// 集中一处，避免卡片 / 预览 / 设置页各写一份而舍入规则互不一致。
/// <para>
/// <b>批次 RZ 补的两句</b>：① 这颗以前把 <c>0.#</c> 写在裸内插里 ⇒ 德式会打成 <c>1,5 MB</c>，
/// 而同仓 <c>ClipAssets.DescribeBytes</c> 早就锁了不变文化 ⇒ <b>同一个体积在两处不同形</b>；现在统一走
/// <see cref="NumberText"/>，分隔符这一件事只剩一个主人。② 全仓曾有<b>四份</b>这样的梯子
/// （这里、<c>ClipAssets</c>、<c>DiagnosticsService</c>、<c>SettingsPage.Engine</c>），后三份现已并进来；
/// 诊断页那一处原先是 <c>F1</c>（"1.0 KB"），并过来后写作 <c>1 KB</c>——形状变了，但"各处舍入不一样"正是这颗判据要消灭的东西。
/// </para>
/// </summary>
public static class FileSizeText
{
    public static string Human(long bytes)
        => bytes switch
        {
            < 0 => string.Empty,
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{NumberText.UpTo1(bytes / 1024.0)} KB",
            < 1024L * 1024 * 1024 => $"{NumberText.UpTo1(bytes / (1024.0 * 1024))} MB",
            _ => $"{NumberText.UpTo2(bytes / (1024.0 * 1024 * 1024))} GB",
        };
}
