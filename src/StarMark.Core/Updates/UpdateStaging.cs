#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>
/// 把"已经盖章说这是我们发的这一版"的那颗 zip，摊成<b>逐颗对过账</b>的待换文件（批次 UG-1）。
/// <para>
/// 这一层是"能不能装"与"真的去换文件"之间唯一的一座桥：它不联网、不碰安装目录、不起进程，
/// 只把签名的清单说过的每一颗文件摊到一个新目录里，并且<b>每摊一颗就当场算一遍哈希对回去</b>。
/// </para>
/// <para>三件刻意的设计，每一件都是某一种坏情形的堵法：
/// ① <b>入口只认 <see cref="UpdateIntegrity"/> 的判决</b>（第一个参数就是那份判决）。
///    判决不是 <c>Trusted</c> 就不写一个字节——"先验再摊"这件事不能靠调用方记得按顺序，
///    靠顺序的约定总有一天会被重构掉（同一族的教训：信任根不许由调用方传，见 #229）。
/// ② <b>写出目标只出自清单</b>：包里的条目名只用来查表，不进路径拼接。
///    于是条目名里那些 <c>..\</c>、盘符、绝对路径根本到不了 <see cref="Path.Combine"/>；
///    认不出的名字落到 <see cref="StageStatus.UnexpectedEntry"/>——没被签名的字节不许进这棵树。
///    （相对路径的越界判据在 <c>UpdateManifestCodec</c> 那一颗，这里不重复一份：两处判据必漂移，#189/#193。）
/// ③ <b>逐颗重算哈希，而不是"上面已经验过整包了"</b>。临时目录是本机可写的，
///    "先验后摊"之间那颗 zip 完全可以被人换过；整包哈希在那一刻就已经是过去时，
///    而每一颗文件对回清单是现在时。所以这里不再整包重算一遍——那是同一件事的昂贵版本。</para>
/// <para>失败的一侧：<b>半途的树不留</b>。清理写在 <c>finally</c>（不是摊在各 catch 上，#230/#232），
/// 因为"摊了一半"与"摊好了"在磁盘上必须看得出区别——否则下一步会把半棵树当成可换的更新。</para>
/// </summary>
public static class UpdateStaging
{
    /// <summary>
    /// 摊开载荷。<paramref name="stagingRoot"/> 由调用方给（<b>必须是安装目录所在那一块盘</b>，
    /// 跨盘的"挪过去"就不是改名而是复制，而更新器要靠改名才谈得上原子）。
    /// </summary>
    public static async Task<StageResult> PrepareAsync(
        UpdateIntegrity.Result verdict, FetchedPackage? package, string stagingRoot, CancellationToken ct = default)
    {
        if (!verdict.IsTrusted)
            return new StageResult(StageStatus.NotTrusted,
                Detail: $"判决是 {verdict.Outcome}，没有\"可信\"就不许往盘上摊一个字节");
        var manifest = verdict.Manifest!;
        if (package is null)
            return new StageResult(StageStatus.PayloadUnavailable, Detail: "判决说有清单，那颗包却没递过来");

        string? target = null;
        var staged = false;
        try
        {
            target = Path.Combine(stagingRoot, manifest.Version + UpdateAssets.NewTreeSuffix);
            Directory.CreateDirectory(stagingRoot);
            // 上一次失败留下的半截树不算"已经摊好"：先清干净再摊。这是自愈，不是让用户去删目录（P-54 同口径）。
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(target);

            using var zip = OpenPayload(package.PackagePath);
            var byName = Index(zip, out var duplicate);
            if (duplicate is not null)
                return new StageResult(StageStatus.UnexpectedEntry, Detail: $"包里有重名条目：{duplicate}");
            var left = new HashSet<string>(byName.Keys, StringComparer.Ordinal);

            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (!byName.TryGetValue(file.Path, out var source))
                    return new StageResult(StageStatus.FileMissing, Detail: $"清单列了 {file.Path}，包里没有");
                left.Remove(file.Path);

                var final = Path.Combine(target, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(final)!);      // 清单可以带子目录（assets/…、Resources/…）
                var (hash, _) = await ExtractOneAsync(source, final + ".part", ct).ConfigureAwait(false);
                if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                    return new StageResult(StageStatus.FileHashMismatch, Detail: $"{file.Path} 摊出来与清单不是同一颗");
                File.Move(final + ".part", final, overwrite: true);
            }

            // 清单没列而包里有的东西：不知道是谁放的，也就不许跟着进安装目录。
            if (left.Count > 0)
                return new StageResult(StageStatus.UnexpectedEntry, Detail: $"包里有 {left.Count} 颗清单没列出的文件");

            staged = true;
            return new StageResult(StageStatus.Staged, target, $"摊开 {manifest.Files.Count} 颗，逐颗对过账");
        }
        catch (OperationCanceledException) { throw; }        // 用户掐的：原样交回，但半棵树照样删
        catch (InvalidDataException ex)
        {
            return new StageResult(StageStatus.PackageUnreadable, Detail: ex.GetType().Name);
        }
        catch (PayloadGoneException ex)
        {
            return new StageResult(StageStatus.PayloadUnavailable, Detail: ex.Reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                or ArgumentException)
        {
            // ArgumentException 也归这一档：stagingRoot 是外面给的串（%TEMP% 或安装路径被人改过就可能什么都不是），
            // 让它冒到界面上只是把"这台机器写不下去"变成一次崩溃——与传输层同一口径（#230）。
            return new StageResult(StageStatus.DiskWriteFailed, Detail: ex.GetType().Name);
        }
        finally
        {
            if (!staged && target is not null) DeleteQuietly(target);
        }
    }

    /// <summary>
    /// 包里的条目按"清单用的那种名字"建索引：归一规则与发布机<b>共用 <see cref="UpdateAssets"/> 那一颗</b>，
    /// 目录项（不带字节）跳过。
    /// <para>重名条目不当成"后面那颗覆盖前面那颗"：清单对一颗路径只有一个答案，
    /// 包里有两份就是这颗包自己不一致——当场说出来，比挑一颗算哈希诚实。</para>
    /// </summary>
    private static Dictionary<string, ZipArchiveEntry> Index(ZipArchive zip, out string? duplicate)
    {
        var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        duplicate = null;
        foreach (var entry in zip.Entries)
        {
            var name = UpdateAssets.NormalizeEntryName(entry.FullName);
            if (!UpdateAssets.IsFileEntry(name)) continue;
            if (!byName.TryAdd(name, entry))
            {
                duplicate = name;
                return byName;
            }
        }
        return byName;
    }

    /// <summary>
    /// 摊一颗：先写 <c>.part</c>，边写边算哈希，成了才改名。
    /// <para>长度不在这里判：<b>哈希一致就必然长度一致</b>，多判一道只是多一处会说谎的地方；
    /// 清单里的 <c>bytes</c> 留给界面报进度用。</para>
    /// </summary>
    private static async Task<(string Hash, long Bytes)> ExtractOneAsync(
        ZipArchiveEntry source, string part, CancellationToken ct)
    {
        await using var input = source.Open();
        await using var output = new FileStream(part, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 64 * 1024,
        });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (await input.ReadAsync(buffer, ct).ConfigureAwait(false) is var read and > 0)
        {
            total += read;
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        await output.FlushAsync(ct).ConfigureAwait(false);
        return (UpdateHashHex.ToHex(hash.GetHashAndReset()), total);
    }

    /// <summary>
    /// 打开载荷那颗文件。<b>"判决之后它不见了"必须和"本机写不下去"分成两格</b>：
    /// 临时目录是本机可写的，清理程序与杀软把它收走是复杂环境里的常事，而读不到 ≠ 磁盘满／没权限——
    /// 指错方向会让人去查自己的磁盘（同 <c>PackageStreamBrokenException</c> 那一族的口径：<b>刻意不从
    /// <see cref="IOException"/> 派生</b>，否则会被下面那道"盘写不下去"的兜底接走，说得像在说别人的盘）。
    /// </summary>
    private static ZipArchive OpenPayload(string path)
    {
        try { return ZipFile.OpenRead(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new PayloadGoneException($"判决在手，那颗包却读不到（{ex.GetType().Name}）：{Path.GetFileName(path)}");
        }
    }

    /// <summary>载荷那颗文件读不到——<b>不是</b> <see cref="IOException"/>，为的就是不被"盘写不下去"那一档接走。</summary>
    private sealed class PayloadGoneException(string reason) : Exception(reason)
    {
        public string Reason { get; } = reason;
    }

    private static void DeleteQuietly(string target)
    {
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉只是留了个没人会去换的目录：它的结局已经是"失败"，没有任何调用方会拿它继续装。
            // 在这里抛出去只会把"更新失败"变成"更新失败并且崩在收尾"（尺子不许弄崩被量的东西）。
            StarLog.Warn($"[更新] 半截暂存树没删掉：{ex.GetType().Name}");
        }
    }
}
