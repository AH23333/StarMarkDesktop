#nullable enable
using System;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 设置页「图片采集」状态行与图片拒收原因的<b>真行为测</b>（批次 SE，P-123 清单 #7）。
/// <para>这批把那句话从 <c>SettingsPageViewModel.Clipboard</c> 搬进 <see cref="ClipboardPolicy"/>，
/// 有两个理由：① 句子里三个实测值原本写死（改判据 ⇒ 设置页替它撒谎）；
/// ② 更根本的是<b>"哪一格该说什么"在 UI 里 ⇒ 一颗测都出不去（#184），而这三格里最容易误判的那一格
/// （分开关开着、总开关关着＝用户以为在记录、其实一条都没存）从来没有人钉过。</para>
/// </summary>
public sealed class ClipboardImageIntakeTextTests
{
    // ────────── ① 三格各说各的事 ──────────

    [Fact]
    public void Off_saysNothingIsCollected_andMentionsWhatStillWorks()
    {
        var text = ClipboardPolicy.DescribeImageIntake(collecting: true, imageOn: false);
        Assert.Equal("图片采集未开启：只记录文本与文件列表。", text);
    }

    /// <summary>
    /// 最容易误判的一格：分开关开着但总开关没开（或监听没建立）。
    /// 这格必须<b>直说"一条都不会记录"</b>——只说"已打开"就是让人对着空历史页纳闷。
    /// </summary>
    [Fact]
    public void ImageOnWithoutCollecting_statesPlainlyThatNothingIsRecorded()
    {
        var text = ClipboardPolicy.DescribeImageIntake(collecting: false, imageOn: true);
        Assert.Contains("一条图片都不会记录", text, StringComparison.Ordinal);
        Assert.DoesNotContain("已开启：", text, StringComparison.Ordinal);   // 不许把这格写成"看起来在工作"
    }

    /// <summary>两把开关都开着才许说"已开启"，且这一格才是带三个实测值的那一格。</summary>
    [Fact]
    public void OnlyTheBothOnCellCarriesTheNumbers()
    {
        Assert.Contains("已开启：", ClipboardPolicy.DescribeImageIntake(true, true), StringComparison.Ordinal);
        Assert.DoesNotContain("160px", ClipboardPolicy.DescribeImageIntake(false, true), StringComparison.Ordinal);
        Assert.DoesNotContain("160px", ClipboardPolicy.DescribeImageIntake(true, false), StringComparison.Ordinal);
    }

    // ────────── ② 今天的读数逐字钉住（这批只换出处，不换显示） ──────────

    [Fact]
    public void TodaySentence_ReadsWordForWordLikeBeforeTheBatch()
    {
        Assert.Equal(
            "已开启：复制到的图片会存成 PNG，并预生成 160px 缩略图。"
            + "单张超过 20 MB 或短边小于 16px 的不收；密码管理器在前台时一律不收。",
            ClipboardPolicy.DescribeImageIntake(collecting: true, imageOn: true));
    }

    /// <summary>三颗判据的读数（句子里那三个数就是从它们拼出来的）。</summary>
    [Fact]
    public void TheThreeNumbers_LiveInTheJudgesWithTodayValues()
    {
        Assert.Equal(160, ClipAssets.ThumbnailMaxEdge);
        Assert.Equal(20 * 1024 * 1024, ClipboardPolicy.MaxImageBytes);
        Assert.Equal(16, ClipboardPolicy.MinImageEdge);
        Assert.Equal("20 MB", ClipAssets.DescribeBytes(ClipboardPolicy.MaxImageBytes));
    }

    // ────────── ③ 顺手修掉的那条自相矛盾读数 ──────────

    /// <summary>
    /// 旧写法是 <c>bytes / (1024 * 1024)</c>（整数除法）：一张 20.5 MiB 的图会被写成
    /// <b>"图片 20 MB 超过 20 MB 上限"</b>——一句话自己跟自己打架，用户读到的是"上限明明没超却被拒"。
    /// 现在两侧都过那颗"字节→人话"的梯子，实际那侧带上小数。
    /// </summary>
    [Theory]
    [InlineData(21_495_808L, "20.5 MB")]    // 20.5 MiB：旧写法在这里塌成"20 MB 超过 20 MB"
    [InlineData(22_020_096L, "21 MB")]      // 21 MiB
    [InlineData(62_914_560L, "60 MB")]      // 60 MiB
    public void OversizeRejection_ReportsTheActualSizeWithTheSameLadderAsTheCap(long bytes, string expectedActual)
    {
        Assert.False(ClipboardPolicy.ShouldRecordImage(4000, 4000, bytes, null, out var reason));
        Assert.NotNull(reason);
        Assert.Contains($"图片 {expectedActual} 超过", reason!, StringComparison.Ordinal);
        Assert.Contains("20 MB 上限，未记录", reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 这条记的是<b>修完之后剩下的那一点</b>，不是缺陷：梯子只精确到 0.1 MB，
    /// 所以"刚过线 1 字节"仍然会被写成"20 MB 超过 20 MB"。旧写法的错窗口是整整 1 MiB（20～21 MiB），
    /// 现在缩到 0.1 MiB 以内。<b>刻意不把这里"修干净"</b>：为 0.1% 的越界量再细化一位小数，
    /// 代价是所有正常体积的读数都变长（"4.83 MB"这类），不值。
    /// </summary>
    [Fact]
    public void JustOverTheCap_StillCollapsesToTheCapReading_knownLimitOfTheLadder()
    {
        var justOver = 20L * 1024 * 1024 + 1;
        Assert.False(ClipboardPolicy.ShouldRecordImage(4000, 4000, justOver, null, out var reason));
        Assert.Contains("图片 20 MB 超过 20 MB 上限", reason!, StringComparison.Ordinal);
    }

    /// <summary>短边那一格的读数也照今天的措辞钉住（同一句话里的第三个实测值）。</summary>
    [Fact]
    public void TooSmallRejection_KeepsItsReading()
    {
        Assert.False(ClipboardPolicy.ShouldRecordImage(12, 40, 4096, null, out var reason));
        Assert.Equal("图片 12×40 小于 16px，未记录（多半是图标）", reason);
    }
}
