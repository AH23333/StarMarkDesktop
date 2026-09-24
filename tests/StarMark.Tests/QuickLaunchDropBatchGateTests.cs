#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 一次拖入的"整批只往返一次"守门（批次 PA-6）。
/// <para>
/// 被测的三处都在 <c>StarMark.UI</c>，测试工程刻意不引用它（分层红线），所以这里<b>读源文件</b>比对结构。
/// 为什么值得这么守：<b>逐条 await 的形状一旦回来，功能一点都不会坏</b>——拖 20 个文件照样成功，
/// 只是每次多 20 趟整档读写；只有秒表能看见，而这里没有可信的秒表。
/// </para>
/// <para>每条守门都带"锚点必须扫到"的反空转断言：扫不到结构就抛，免得一条永不执行的检查冒充绿灯。</para>
/// </summary>
public sealed class QuickLaunchDropBatchGateTests
{
    private const string ManagerRelativePath = "src/StarMark.UI/Services/WidgetManager.cs";
    private const string WindowRelativePath = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";

    /// <summary>从测试输出目录往上找仓库根（认 <c>StarMark.sln</c>）。</summary>
    private static string RepoRoot()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "StarMark.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("没找到仓库根（守门失效比红测更危险，故直接抛）");
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// 切出一个方法：<b>块体</b>按大括号配对，<b>表达式体</b>（<c>=> ...;</c>）取到第一个分号行。
    /// 后者必须单独处理——否则"只是转发"的那个小方法会把下一个方法的 <c>{</c> 也算进体内。
    /// 锚点缺失或有歧义都抛。
    /// </summary>
    private static string MethodBody(string source, string signatureFragment)
    {
        var lines = source.Split('\n');
        var hits = lines.Select((line, index) => (line, index))
            .Where(x => x.line.Contains(signatureFragment)).ToList();
        Assert.True(hits.Count == 1,
            $"锚点「{signatureFragment}」命中 {hits.Count} 处（应为 1 处）——方法被改名或复制了，守门要先跟上");

        var start = hits[0].index;
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Contains("=>") && !line.Contains("=> {") && !line.EndsWith("{"))
            {
                var end = i;
                while (end < lines.Length && !lines[end].TrimEnd('\r').TrimEnd().EndsWith(";")) end++;
                return string.Join("\n", lines[start..(end + 1)]);
            }
            if (line.EndsWith("{"))
            {
                var depth = 0;
                for (var j = i; j < lines.Length; j++)
                {
                    foreach (var ch in lines[j])
                    {
                        if (ch == '{') depth++;
                        else if (ch == '}') depth--;
                    }
                    if (depth == 0) return string.Join("\n", lines[start..(j + 1)]);
                }
                break;
            }
        }
        throw new InvalidOperationException($"没能为「{signatureFragment}」切出方法体");
    }

    /// <summary>整批添加的出口必须<b>只读一次档、只写一次盘、只广播一次</b>。</summary>
    [Fact]
    public void AWholeBatchReadsAndWritesTheStoreExactlyOnce()
    {
        var body = MethodBody(ReadRepoFile(ManagerRelativePath), "public async Task<int> AddLinksAsync(");

        Assert.Contains("inst.Links.Add(new LinkItem", body);     // 反空转：确实扫到了写入口的那一段
        Assert.Equal(1, Count(body, "_storage.Load()"));
        Assert.Equal(1, Count(body, "_storage.Save("));
        Assert.Equal(1, Count(body, "LinksChanged?.Invoke("));
        Assert.Contains("LogActivitiesAsync(", body);

        // 去重循环里只许攒列表：在这里 await 一次就是整档读写一遍
        var loop = Between(body, "foreach (var (title, uri) in links)", "if (fresh.Count == 0)");
        Assert.Contains("taken.Add(target)", loop);
        Assert.Equal(0, Count(loop, "await "));
    }

    /// <summary>单个添加入口不许再留第二份判据：<b>它只是长度为 1 的批量调用</b>。
    /// 两处各写一遍去重的话，改一处就留下一条"看起来还在工作"的旧口径。</summary>
    [Fact]
    public void TheSingleLinkExitJustDelegatesToTheBatch()
    {
        var manager = ReadRepoFile(ManagerRelativePath);
        var body = MethodBody(manager, "public async Task<bool> AddLinkAsync(");

        Assert.Contains("=> await AddLinksAsync(", body);
        Assert.Equal(0, Count(body, "_storage."));
        Assert.Equal(1, Count(manager, "inst.Links.Add(new LinkItem"));
    }

    /// <summary>拖放处理器：整批交给两个批量出口，收集循环里不再逐项打库。</summary>
    [Fact]
    public void TheDropHandlerHandsTheWholeBatchToTheBulkExits()
    {
        var body = MethodBody(ReadRepoFile(WindowRelativePath), "private async void QuickLaunch_Drop(");
        var loop = Between(body, "foreach (var item in items)", "await _manager.RecordPathsToLibraryAsync");

        Assert.Contains("paths.Add((item.Name, item.Path));", loop);   // 反空转：确实扫到收集语句
        Assert.Equal(0, Count(loop, "await "));
        Assert.Contains("_manager.RecordPathsToLibraryAsync(paths)", body);
        Assert.Contains("_manager.AddLinksAsync(_instanceId, links)", body);
    }

    /// <summary>被取代的"逐条登记"出口要删净：留着就等于给后来人留一条能走回旧形状的路。</summary>
    [Fact]
    public void ThePerItemRecordExitIsGoneFromEveryCaller()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var hits = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadAllLines(file))
            .Where(line => line.Contains("RecordPathToLibraryAsync("))          // 单数那一个
            .Where(line => !line.Contains("RecordPathsToLibraryAsync("))        // 复数的是新出口
            .ToList();

        Assert.Empty(hits);
        Assert.Contains("RecordPathsToLibraryAsync(", File.ReadAllText(
            Path.Combine(src, "StarMark.UI", "Services", "WidgetManager.cs"))); // 反空转：新出口还在
    }

    private static int Count(string text, string needle)
        => text.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    private static string Between(string text, string from, string to)
    {
        var a = text.IndexOf(from, StringComparison.Ordinal);
        var b = text.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, $"没能从「{from}」定位到「{to}」——结构变了，守门要先跟上");
        return text[a..b];
    }
}
