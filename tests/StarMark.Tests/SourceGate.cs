#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 扫源码用的守门小工具（测试工程不引用 <c>StarMark.UI</c>，UI 层的结构只能这样守）。
/// <para>
/// 每条守门都必须"锚点扫得到"：<b>扫不到就抛，而不是返回空集合让断言白过</b>——
/// 一条永不执行的检查会冒充绿灯，比红测危险得多。
/// </para>
/// </summary>
internal static class SourceGate
{
    internal static string RepoRoot()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "StarMark.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("没找到仓库根（守门失效比红测更危险，故直接抛）");
    }

    internal static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// 切出一个方法：<b>块体</b>按大括号配对，<b>表达式体</b>（<c>=> ...;</c>）取到第一个分号行。
    /// 后者必须单独处理——否则"只是转发"的那个小方法会把下一个方法的 <c>{</c> 也算进体内。
    /// </summary>
    internal static string MethodBody(string source, string signatureFragment)
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
            if (!line.EndsWith("{")) continue;
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
        throw new InvalidOperationException($"没能为「{signatureFragment}」切出方法体");
    }

    internal static int Count(string text, string needle)
        => text.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// 把某个方法的体从源码里挖掉。<b>"某个调用只允许出现在某一个方法里"这类守门就靠它</b>：
    /// 直接数整个文件的出现次数会连那个合法出口一起数进去，判不准。
    /// </summary>
    internal static string WithoutMethod(string source, string signatureFragment)
    {
        var body = MethodBody(source, signatureFragment);
        var at = source.IndexOf(body, StringComparison.Ordinal);
        Assert.True(at >= 0, $"没能从源码里定位到「{signatureFragment}」的方法体");
        return source[..at] + source[(at + body.Length)..];
    }

    internal static string Between(string text, string from, string to)
    {
        var a = text.IndexOf(from, StringComparison.Ordinal);
        var b = text.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, $"没能从「{from}」定位到「{to}」——结构变了，守门要先跟上");
        return text[a..b];
    }
}
