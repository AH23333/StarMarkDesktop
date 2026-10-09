#nullable enable
using System;
using System.Collections.Generic;
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
    /// 磁盘普查要跳开的树：<b>产物目录</b>与<b>第三方工具的便携副本</b>。
    /// <para>后一半是批次 VB 逼出来的：造安装包那颗 <c>ISCC.exe</c> 缺失时会下到 <c>tools\InnoSetup6\</c>，
    /// 而那颗目录里带着 Inno 自己的 <c>Examples\</c>（含 <c>*.csproj</c>）——
    /// "磁盘上每颗工程都得被 sln 点名"这条闸门当场把它读成"你新加了一颗工程忘了入解决方案"。
    /// 更糟的是这类红看着像真缺陷，人会去把第三方示例塞进解决方案。</para>
    /// <para>所以这一条住在这里而不是各普查自己写一份：三颗普查（<c>*.csproj</c>／<c>*.props|targets|csproj</c>／<c>*.ps1</c>）
    /// 少改一颗，下一次装完工具就只有一半闸门知道该跳开（#189/#193 那一族）。</para>
    /// </summary>
    internal static bool IsGeneratedOrThirdPartyCopy(string absolutePath)
    {
        var p = absolutePath.Replace(Path.DirectorySeparatorChar, '/');
        return p.Contains("/bin/", StringComparison.Ordinal)
            || p.Contains("/obj/", StringComparison.Ordinal)
            || p.Contains("/publish/", StringComparison.Ordinal)
            || p.Contains("/dist/", StringComparison.Ordinal)
            || p.Contains("/tools/InnoSetup6/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 某个目录<b>之下</b>的全部手写 C# 源码（逐文件返回，红的时候能说出是哪个文件）。
    /// <para>给"某个字面整条消失、全库不许再有"这类普查用：只扫一两个文件的禁项守门，会因为那条出口
    /// 搬去第三个文件而假绿（记忆 ⑦ 讲拆分时的同一形状）。跳过 <c>obj</c>/<c>bin</c> 与生成物
    /// （<c>*.g.cs</c>／<c>*.g.i.cs</c>），否则框架自己生成的代码会把锚点命中数撑歪。</para>
    /// </summary>
    internal static IReadOnlyList<(string RelativePath, string Text)> ReadRepoUnder(string relativeDir)
    {
        var root = Path.Combine(RepoRoot(), relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(root), $"普查目录不存在：{relativeDir}（守门失效比红测更危险，故直接抛）");
        var list = new List<(string, string)>();
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/")) continue;
            if (normalized.EndsWith(".g.cs", StringComparison.Ordinal)
                || normalized.EndsWith(".g.i.cs", StringComparison.Ordinal)) continue;
            // 报"仓根相对路径"而不是拍平的文件名：同一个类拆成多个 partial 之后，
            // Views/ 与 ViewModels/ 里会有同名前缀的文件，拍平了红测指不出该打开哪一个（批次 RZ）
            var repoRoot = RepoRoot().Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');
            list.Add((normalized.StartsWith(repoRoot + "/", StringComparison.Ordinal)
                ? normalized[(repoRoot.Length + 1)..]
                : Path.GetFileName(file), File.ReadAllText(file)));
        }
        Assert.True(list.Count > 0, $"{relativeDir} 下一个源文件都没扫到（普查路径错了）");
        return list;
    }

    /// <summary>
    /// 读<b>同一个类的全部 partial 文件</b>（主文件 + 同目录下的 <c>类名.分段名.cs</c>）拼成一份文本。
    /// <para>
    /// 一个上千行的大类按访问面拆成多个 partial 文件之后，钉在单个文件上的守门会"扫不到锚点"而红——
    /// 这是设计如此（它逼接线跟上）。但如果只是把路径改到方法搬去的那个文件，下一次搬家它又断一次。
    /// 所以凡是在<b>已拆分的类</b>里找方法的守门都走这里：文件怎么搬，判据都不减一分。
    /// </para>
    /// </summary>
    internal static string ReadRepoPartials(string relativePath)
    {
        var full = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"锚点主文件不存在：{relativePath}");
        var dir = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);
        // XAML 的代码侧叫 "Foo.xaml.cs"，GetFileNameWithoutExtension 会把 ".xaml" 也留下——
        // 不剥掉就只会命中主文件自己，那些"不许出现 X"的禁项守门会因为方法搬了家而假绿。
        if (stem.EndsWith(".xaml", StringComparison.Ordinal)) stem = stem[..^".xaml".Length];
        var parts = Directory.GetFiles(dir, stem + ".cs")
            .Concat(Directory.GetFiles(dir, stem + ".*.cs").Where(f => !f.EndsWith(".g.cs", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(parts.Count > 0, $"没找到 {relativePath} 的任何一份 partial（守门要先跟上）");
        return string.Join("\n", parts.Select(File.ReadAllText));
    }

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
    /// 抹掉注释后的源码。<b>"某句话全库只许有一个出处"这类禁项一律读这个</b>：
    /// 注释里引用一句旧话是为了讲清为什么，不是又开一个出口——按原文数会把这种引用算成违规，
    /// 而假失败最坏，因为它教人把闸门改松而不是改对（#123；形状口径见 #195）。
    /// </summary>
    internal static string Code(string text) => FormatScanner.Scan(text).Code;

    /// <summary>
    /// PowerShell 版的 <see cref="Code"/>：抹掉整行注释后的脚本文本。
    /// <para><b>"某句禁项全库只许有一个出处"这类闸门一律读这个</b>——注释里写一句"私钥在 %USERPROFILE%…"
    /// 是为了讲清为什么，不是又开一条出口。批次 VB 起发布与安装器两族闸门共用这一颗：
    /// 这条规矩原本住在 <c>ReleasePackagingGateTests</c> 里，而"同一件事写两遍"正是 #189/#193 那一族。</para>
    /// <para>口径：一颗脚本里<b>不许把 <c>#</c> 写进字符串字面量</b>——现在成立，改脚本时请保持。</para>
    /// </summary>
    internal static string PsCode(string text)
        => string.Join("\n", text.Split('\n')
            .Select(line =>
            {
                var cut = line.IndexOf('#');
                return (cut >= 0 ? line[..cut] : line).TrimEnd('\r');
            })
            .Where(l => !string.IsNullOrWhiteSpace(l)));

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

    /// <summary>
    /// 表里那一行的<b>槽数</b>（三列里出现过的占位符编号去重）。读不到那颗键就当场抛——
    /// 「表里没有这颗键」正是要被看见的那件事，⛔ 让它退回 0。
    /// </summary>
    internal static int RowSlots(string key)
    {
        Assert.True(StarMark.Abstractions.I18n.UiStrings.TryGet(key, out var entry), $"表里没有这颗键：{key}");
        return new[] { entry.Zh, entry.En, entry.Ja }
            .SelectMany(text => System.Text.RegularExpressions.Regex.Matches(text, @"\{(\d+)\}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>
    /// 扫出 <c>LocMessage.Of("字面键"…)</c> 的<b>调用点</b>：键＋代码实际递出的顶层实参数＋那一行的行号。
    /// <para>⛔ 用一条正则切：实参里有 <c>TypeCount(ItemType.Bookmark),</c> 这种"嵌套括号后紧跟逗号"的形状，
    /// 正则会在第一颗嵌套括号处提前收尾，于是"槽数对不上"那格读数是<b>仪器</b>错的（坑表 #393／#406）。
    /// 这里按括号深度走到与 <c>Of(</c> 配对的那颗 <c>)</c>，字符串里的逗号与转义都不算。</para>
    /// <para>批次 WI-57 先把它写在 <c>DiagnosticsWordGateTests</c> 里；批次 WI-58 第二族要用同一把尺
    /// ⇒ 升到这一颗共用件（台架规则 #86：同一动作⛔ 两份定位实现）。</para>
    /// </summary>
    internal static List<(string Key, int Args, int Line)> KeyedCallSites(string code)
    {
        var found = new List<(string, int, int)>();
        const string head = "LocMessage.Of(\"";
        var from = 0;
        while (true)
        {
            var start = code.IndexOf(head, from, StringComparison.Ordinal);
            if (start < 0) return found;
            var keyStart = start + head.Length;
            var keyEnd = code.IndexOf('"', keyStart);
            if (keyEnd < 0) return found;
            var key = code[keyStart..keyEnd];

            var depth = 1;                       // 已经在 Of( 的那颗括号里
            var inString = false;
            var prev = '\0';
            var commas = 0;
            var i = keyEnd + 1;
            for (; i < code.Length; i++)
            {
                var c = code[i];
                if (inString)
                {
                    if (c == '"' && prev != '\\') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (c == ',' && depth == 1) commas++;
                prev = c;
            }
            found.Add((key, commas, code[..i].Count(ch => ch == '\n') + 1));
            from = i;
        }
    }
}
