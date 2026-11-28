#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// clip-drops <b>写侧</b>的会失败的那一半（✅P-307 第二刀・乙）：物化、幂等、尽力删。
/// <para>
/// 纯函数（命名字典/名册）在 <c>ClipDropAssetsTests</c> 逐值钉过，这里只碰真文件系统——
/// "第二次写同一个名字一个字节都不动"这件事只有真写两遍才验得出来，而它正是
/// "回放不得换掉用户可能正改着的本机件"的全部保障（图片侧靠 MainExists 早退守同一条，
/// 这里靠"已在＝只报名字"）。
/// </para>
/// </summary>
public sealed class ClipboardDropFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_dropstore_{Guid.NewGuid():N}");

    public ClipboardDropFileStoreTests()
    {
        // ⛔ 不改道＝往用户真实 clip-drops 里写测试件（与 ClipAssets.FolderOverride 同一条纪律）。
        ClipDropAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipDropAssets.FolderOverride = null;
        try { Directory.Delete(_dir, true); } catch { /* 临时目录留给系统清 */ }
    }

    [Fact]
    public async Task TryWriteAsync_MaterializesUnderTheDeterministicName()
    {
        var content = Encoding.UTF8.GetBytes("一份虚拟文件的内容");

        var name = await ClipboardDropFileStore.TryWriteAsync("拖源给的名字.txt", content, CancellationToken.None);

        Assert.NotNull(name);
        // 名字由内容哈希决定：UI 那边拿这个名去认领（进 extra），删除侧再按名收——三处必须是同一个名。
        Assert.Equal(ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(content), "拖源给的名字.txt"), name!);
        var path = Path.Combine(_dir, name!);
        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task TryWriteAsync_CreatesTheFolderWhenItIsNotThereYet()
    {
        // 首次拖放前目录不存在（没人拖过就没有 clip-drops）：写侧自己补，不是启动时预建一个空目录。
        Assert.False(Directory.Exists(_dir));

        var name = await ClipboardDropFileStore.TryWriteAsync("第一件.docx", new byte[] { 1 }, CancellationToken.None);

        Assert.NotNull(name);
        Assert.True(File.Exists(Path.Combine(_dir, name!)));
    }

    [Fact]
    public async Task TryWriteAsync_IsIdempotent_SecondTouchDoesNotRewriteTheFile()
    {
        var content = Encoding.UTF8.GetBytes("同一件内容的字节");
        var first = await ClipboardDropFileStore.TryWriteAsync("同一件.虚拟.pdf", content, CancellationToken.None);
        Assert.NotNull(first);
        var path = Path.Combine(_dir, first!);

        // 用户改了本机那一份（或备份还原回一份旧的）：同一件再拖（同内容＋同原始名＝同一个最终名）
        // ＝什么都不改、只报名字——否则回放把用户正改着的件悄悄换回拖进来的旧字节，
        // 而两边日志都说"成功"。
        var edited = Encoding.UTF8.GetBytes("用户后来改过的字节");
        await File.WriteAllBytesAsync(path, edited);

        var second = await ClipboardDropFileStore.TryWriteAsync("同一件.虚拟.pdf", (byte[])content.Clone(),
            CancellationToken.None);

        Assert.Equal(first, second);                                  // 内容哈希＋洗名＝确定性名字
        Assert.Equal(edited, await File.ReadAllBytesAsync(path));     // 用户那份没被换掉
    }

    [Fact]
    public async Task TryWriteAsync_DifferentContentGetsDifferentNames()
    {
        var a = await ClipboardDropFileStore.TryWriteAsync("甲.txt", new byte[] { 1, 2, 3 }, CancellationToken.None);
        var b = await ClipboardDropFileStore.TryWriteAsync("甲.txt", new byte[] { 4, 5, 6 }, CancellationToken.None);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);                                        // 同名不同内容＝第二次拖放顶掉第一次的件
        Assert.True(File.Exists(Path.Combine(_dir, a!)));
        Assert.True(File.Exists(Path.Combine(_dir, b!)));
    }

    [Fact]
    public async Task TryWriteAsync_CancelledToken_ThrowsInsteadOfCountingAsFailure()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        // 关窗取消拖放是正当操作，但它是"没做完"，不是"失败成功"——这里必须抛出去，
        // 不许返回 null 混进回执的 failed 计数（用户分得清"取消"与"没记成"）。
        // SemaphoreSlim.WaitAsync 对已取消的令牌抛的正是 TaskCanceledException
        // （OperationCanceledException 的子类）：产品那句 catch (OperationCanceledException) 原样递出去。
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            ClipboardDropFileStore.TryWriteAsync("取消件.txt", new byte[] { 1 }, cts.Token));
    }

    [Fact]
    public async Task TryDelete_TakesTheFileAway_AndMissingIsAlreadySuccess()
    {
        var name = await ClipboardDropFileStore.TryWriteAsync("要收回的件.txt", new byte[] { 7 }, CancellationToken.None);
        Assert.NotNull(name);

        Assert.True(ClipboardDropFileStore.TryDelete(name));
        Assert.False(File.Exists(Path.Combine(_dir, name!)));
        Assert.True(ClipboardDropFileStore.TryDelete(name));          // 已不在＝目标达成，不当失败重试
    }

    [Fact]
    public async Task TryDelete_UnsafeNameIsRefusedWithoutTouchingTheDisk()
    {
        var victim = Path.Combine(Path.GetTempPath(), $"starmark_dropvictim_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(victim, "目录外的件");
        try
        {
            Assert.False(ClipboardDropFileStore.TryDelete("../" + Path.GetFileName(victim)));
            Assert.True(File.Exists(victim));                         // 越界的名字一个都不许删出去
        }
        finally { try { File.Delete(victim); } catch { } }
    }
}
