#nullable enable
using System;
using System.Globalization;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 数字读数的唯一出处 <see cref="NumberText"/>（批次 RZ，P-122 的另一半）。
/// <para>
/// <b>这批的判据面积是量出来的，不是推出来的</b>：先拿七种文化实测（<see cref="WhichShapesActuallyMove"/>），
/// 结论直接决定收谁、不收谁——
/// <list type="table">
/// <item><description><c>de-DE</c>：<c>1234.5</c> → <c>1234,5</c>；<c>1234567</c> 的 <c>N0</c> → <c>1.234.567</c>；<c>P0</c> → <c>15 %</c></description></item>
/// <item><description><c>ar-SA</c>/<c>ar-EG</c>：小数点变 <c>٫</c>、千分位变 <c>٬</c>、百分号变 <c>٪</c></description></item>
/// <item><description><c>hi-IN</c>：<c>N0</c> 按 lakh 分组 → <c>12,34,567</c>（位数对，分组全错）</description></item>
/// <item><description>而 <c>{5:00}</c>、<c>{1234.5:0}</c>、裸 <c>{5}</c> 在<b>七种文化下逐字相同</b> ⇒ <b>不收</b></description></item>
/// </list>
/// 最后一行是这批最容易做错的地方：看见 <c>ToString(格式)</c> 就一律收口，会把 7 处纯补零（闹钟时刻、
/// 倒计时、世界时钟 UTC 偏移、番茄钟）无谓地拖进来，还会让闸门天天红在无害写法上——
/// <b>"净"与"不净"的边界必须由实测给，不能由形状给</b>（这条正是 EM 批当年判"数值格式透镜＝净"时缺的那一步）。
/// </para>
/// </summary>
public sealed class NumberTextTests
{
    private static readonly string[] Cultures = ["zh-CN", "en-US", "de-DE", "ar-SA", "ar-EG", "hi-IN", "th-TH"];

    /// <summary>
    /// 前提测：这些形状<b>确实</b>随文化变（不锁文化的那份写法当场演示给用户看），
    /// 同时钉住<b>反面</b>——纯整数形状不随文化变，所以判据不该接管它们。
    /// </summary>
    [Fact]
    public void WhichShapesActuallyMove()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1234,5", $"{1234.5:0.#}");                       // 小数点
            Assert.Equal("1.234.567", $"{1234567L:N0}");                    // 千分位
            Assert.Equal("15 %", $"{0.15:P0}");                             // 百分号前多一个空格
            Assert.Equal("1234,5", $"{1234.5}");                            // **没有格式也一样**（默认 ToString 走当前文化）

            CultureInfo.CurrentCulture = new CultureInfo("ar-EG");
            Assert.Equal("1234٫5", $"{1234.5:0.##}");                       // 阿拉伯式小数点
            Assert.Contains("٪", $"{0.15:P0}");                             // 阿拉伯式百分号（后面还跟一个方向标记，所以不比全长）

            CultureInfo.CurrentCulture = new CultureInfo("hi-IN");
            Assert.Equal("12,34,567", $"{1234567L:N0}");                    // lakh 分组：位数字都对，读起来是另一个数

            // 反面：纯整数形状七种文化逐字相同 ⇒ 判据不收它们
            foreach (var name in Cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                Assert.Equal("05", $"{5:00}");
                Assert.Equal("1235", $"{1234.5:0}");
                Assert.Equal("5", $"{5L}");
                Assert.Equal("123456781234123412341234567890ab",
                    Guid.Parse("12345678-1234-1234-1234-1234567890ab").ToString("N"));   // 9 处 Guid.ToString("N") 与此同理
            }
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    /// <summary>判据本身免疫：同一颗出口在七种文化下必须给出<b>逐字相同</b>的读数。</summary>
    [Fact]
    public void NumberText_IsImmuneToTheCurrentCulture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in Cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                Assert.Equal("1,234,567", NumberText.Grouped(1234567));
                Assert.Equal("1,234,568", NumberText.Grouped(1234567.5));
                Assert.Equal("1234.5", NumberText.UpTo1(1234.5));
                Assert.Equal("2", NumberText.UpTo1(2.04));
                Assert.Equal("1.25", NumberText.UpTo2(1.25));
                Assert.Equal("1.01", NumberText.UpTo2(1.005m));
                Assert.Equal("1234.5", NumberText.Fixed1(1234.5));
                Assert.Equal("0.85", NumberText.Fixed2(0.85));
                Assert.Equal("52.371", NumberText.Fixed3(52.371));
                Assert.Equal("15%", NumberText.Percent(0.15));
            }
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    /// <summary>逐字形状钉死（这些串就是用户看到的形状，改任何一个字符都要先说明为什么）。</summary>
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(999L, "999")]
    [InlineData(1234L, "1,234")]
    [InlineData(-12345L, "-12,345")]           // 负号也要固定：德式/阿拉伯式的负号位置不一样
    public void Grouped_PinsTheShape(long value, string expected)
        => Assert.Equal(expected, NumberText.Grouped(value));

    [Theory]
    [InlineData(2.0, "2")]                     // 尾零不写：体积不装作有精度
    [InlineData(1.5, "1.5")]
    [InlineData(1.25, "1.3")]                  // 平局实测舍上去：钉的是"哪天变成 1.2 要当场看得见"，不是给算法下结论
    public void UpTo1_PinsTheShape(double value, string expected)
        => Assert.Equal(expected, NumberText.UpTo1(value));

    [Theory]
    [InlineData(1.25, "1.25")]
    [InlineData(1.5, "1.5")]
    [InlineData(2.0, "2")]
    public void UpTo2_PinsTheShape(double value, string expected)
        => Assert.Equal(expected, NumberText.UpTo2(value));

    /// <summary>计算器的结果是 decimal：它必须走 decimal 那颗出口，而不是先降成 double。</summary>
    [Fact]
    public void DecimalPathIsNotSilentlyWidened()
    => Assert.Equal("1.01", NumberText.UpTo2(1.005m));

    [Theory]
    [InlineData(0.153, "15%")]
    [InlineData(1.0, "100%")]
    [InlineData(0.0, "0%")]
    public void Percent_PinsTheShape(double fraction, string expected)
        => Assert.Equal(expected, NumberText.Percent(fraction));

    /// <summary>
    /// 天气缓存键是 <c>Fixed3</c> 唯一的"非显示"读者：它进错形状，切城市的判断就不再可靠。
    /// <para>键的构造是 <c>"纬度,经度"</c>，所以<b>键里只许有那一枚分隔逗号</b>。德式 <c>F3</c> 把小数点打成逗号，
    /// 键会变成 <c>52,371,4,868</c>——四段而不是两段，且再也切不出原来的两个数。</para>
    /// </summary>
    [Fact]
    public void Fixed3_KeepsTheWeatherKeyUnambiguous()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var key = $"{NumberText.Fixed3(52.371)},{NumberText.Fixed3(4.868)}";

            Assert.Equal("52.371,4.868", key);
            var parts = key.Split(',');
            Assert.Equal(2, parts.Length);                    // 两段＝一个分隔符；小数逗号会把它撑成四段
            Assert.Equal(52.371, double.Parse(parts[0], CultureInfo.InvariantCulture));   // 切回来还得是原来那两个数
            Assert.Equal(4.868, double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    /// <summary>
    /// 并梯子的验收：卡片/预览/设置页/诊断页从此共用一把梯子，
    /// 且 <c>ClipAssets.DescribeBytes</c> 与 <c>FileSizeText.Human</c> 从此<b>逐字相等</b>（转发不是第二份判断）。
    /// </summary>
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    [InlineData(-1, "")]                       // 读不出体积时宁可空着，也不写"-1 B"这种假数字
    public void OneByteLadderForEverySurface(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeText.Human(bytes));
        Assert.Equal(expected, StarMark.Abstractions.Clipboard.ClipAssets.DescribeBytes(bytes));
    }
}
