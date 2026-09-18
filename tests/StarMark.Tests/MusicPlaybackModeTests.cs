using StarMark.Core.Media;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 播放模式循环的纯逻辑测试。真正的 SMTC（Windows.Media.Control）在测试宿主里跑不起来，
/// 所以只把"不碰 WinRT 的那一半"下沉到 Core 并覆盖它。
/// </summary>
public class MusicPlaybackModeTests
{
    [Fact]
    public void NextMode_两个能力都有_走完整三态循环()
    {
        Assert.Equal(MusicPlaybackMode.Shuffle, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Normal, true, true));
        Assert.Equal(MusicPlaybackMode.Repeat, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Shuffle, true, true));
        Assert.Equal(MusicPlaybackMode.Normal, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Repeat, true, true));
    }

    [Fact]
    public void NextMode_不支持随机_跳过随机态()
    {
        Assert.Equal(MusicPlaybackMode.Repeat, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Normal, false, true));
        Assert.Equal(MusicPlaybackMode.Normal, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Repeat, false, true));
    }

    [Fact]
    public void NextMode_不支持循环_随机之后回到普通()
    {
        Assert.Equal(MusicPlaybackMode.Shuffle, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Normal, true, false));
        Assert.Equal(MusicPlaybackMode.Normal, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Shuffle, true, false));
    }

    [Fact]
    public void NextMode_两个都不支持_恒为普通()
    {
        // 关键回归：若这里返回非 Normal，模式按钮会一直"切换成功"却毫无变化，
        // 用户会以为组件坏了。
        Assert.Equal(MusicPlaybackMode.Normal, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Normal, false, false));
        Assert.Equal(MusicPlaybackMode.Normal, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Shuffle, false, false));
        Assert.Equal(MusicPlaybackMode.Normal, MusicPlaybackModeMath.NextMode(MusicPlaybackMode.Repeat, false, false));
    }

    [Fact]
    public void Glyph_三态各不相同()
    {
        var glyphs = new[]
        {
            MusicPlaybackModeMath.Glyph(MusicPlaybackMode.Normal),
            MusicPlaybackModeMath.Glyph(MusicPlaybackMode.Shuffle),
            MusicPlaybackModeMath.Glyph(MusicPlaybackMode.Repeat),
        };

        Assert.Equal(3, System.Linq.Enumerable.Distinct(glyphs).Count());
        Assert.All(glyphs, g => Assert.False(string.IsNullOrWhiteSpace(g)));
    }

    [Fact]
    public void Label_三态各不相同且非空()
    {
        var labels = new[]
        {
            MusicPlaybackModeMath.Label(MusicPlaybackMode.Normal),
            MusicPlaybackModeMath.Label(MusicPlaybackMode.Shuffle),
            MusicPlaybackModeMath.Label(MusicPlaybackMode.Repeat),
        };

        Assert.Equal(3, System.Linq.Enumerable.Distinct(labels).Count());
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }
}
