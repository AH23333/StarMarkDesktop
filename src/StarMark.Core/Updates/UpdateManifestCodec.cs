#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>清单读不下去时的那一格：<b>哪一类读不懂</b>决定了要退去哪一个结局。</summary>
public enum ManifestRejectKind
{
    /// <summary>形状就不是清单（不是 JSON、缺根字段、字段类型不对）。</summary>
    Unreadable,

    /// <summary><c>schema</c> 比这个程序认识的还新：先升级程序，再谈更新。</summary>
    SchemaTooNew,

    /// <summary>读得懂是哪一欄，但那一欄的内容不该被认（越界路径、非小写十六进制、空表…）。</summary>
    FieldRejected,
}

/// <summary>拒绝的理由。<see cref="Detail"/> 只用来写日志，<b>不许把外部内容原样拼进界面</b>。</summary>
public readonly record struct ManifestReject(ManifestRejectKind Kind, string? Detail)
{
    public static readonly ManifestReject None = default;

    public UpdateIntegrity.Outcome ToOutcome() => Kind switch
    {
        ManifestRejectKind.SchemaTooNew => UpdateIntegrity.Outcome.SchemaUnsupported,
        ManifestRejectKind.FieldRejected => UpdateIntegrity.Outcome.FieldRejected,
        _ => UpdateIntegrity.Outcome.ManifestUnreadable,
    };
}

/// <summary>
/// 更新清单的<b>读法与写法</b>（批次 UF）。
/// <para>两件事放在同一颗类里是有原因的：签名盖的就是"写出来的那串字节"，
/// 读写两边一旦各写一份字段表，下一次加一欄就会出现"签名方盖了、验签方不认"或者反过来
/// ——同一族的坑在 #189/#193 反复写过（两处实现必漂移）。发布工具与验签走的是同一颗 <see cref="Write"/>。</para>
/// <para><b>写是确定的</b>：字段顺序、缩进、转义都由 <see cref="JsonWriterOptions"/> 钉死，
/// 不依赖反射给出的属性顺序（那个换版本就可能变，而签名一漂移就是"更新全服不可用"）。</para>
/// </summary>
public static class UpdateManifestCodec
{
    private const int MaxDepth = 32;
    private const int MaxVersionLength = 100;
    private const int MaxPackageNameLength = 120;
    private const int MaxPathLength = 260;

    /// <summary>把清单写成那串"要被签名、也要被原样上传"的字节。</summary>
    public static byte[] Write(ReleaseManifest manifest)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", manifest.Schema);
            writer.WriteString("version", manifest.Version);
            writer.WriteString("package", manifest.Package);
            writer.WriteString("packageSha256", manifest.PackageSha256);
            writer.WriteNumber("packageBytes", manifest.PackageBytes);
            writer.WriteStartArray("files");
            foreach (var file in manifest.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteNumber("bytes", file.Bytes);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// 读一份清单。<b>读不懂 ⇒ false + 理由</b>，绝不返回一个"字段空着但对象非空"的清单：
    /// 那份东西会被后面每一步当成"清单说没有文件要换"处理，那正是最坏的假成功。
    /// </summary>
    public static bool TryRead(byte[] bytes, out ReleaseManifest? manifest, out ManifestReject reject)
    {
        manifest = null;
        reject = ManifestReject.None;
        if (bytes is null || bytes.Length == 0)
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, "空的");
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            { MaxDepth = MaxDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException ex)
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, ex.GetBaseException().GetType().Name);
            return false;
        }

        using (document)
        {
            return TryReadRoot(document.RootElement, out manifest, out reject);
        }
    }

    private static bool TryReadRoot(JsonElement root, out ReleaseManifest? manifest, out ManifestReject reject)
    {
        manifest = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, "根不是对象");
            return false;
        }

        // schema 先判：比这程序新的清单里，字段含义可能已经变了，逐欄校验就成了"按半懂的规则放行"。
        if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number
            || !schema.TryGetInt32(out var schemaValue))
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, "schema 读不出整数");
            return false;
        }
        if (schemaValue > UpdateAssets.SupportedSchema)
        {
            reject = new ManifestReject(ManifestRejectKind.SchemaTooNew, schemaValue.ToString(CultureInfo.InvariantCulture));
            return false;
        }
        if (schemaValue != UpdateAssets.SupportedSchema)
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, $"schema {schemaValue} 不是这一档");
            return false;
        }

        if (!TryReadString(root, "version", out var version, MaxVersionLength, out reject)) return false;
        if (!AppVersion.TryParse(version, out _))
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, "version 不像版本号");
            return false;
        }

        if (!TryReadString(root, "package", out var package, MaxPackageNameLength, out reject)) return false;
        if (!IsSafePackageName(package))
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, "package 不是安全的 .zip 文件名");
            return false;
        }

        if (!TryReadString(root, "packageSha256", out var packageSha, 64, out reject)) return false;
        if (!IsLowercaseHex64(packageSha))
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, "packageSha256 不是 64 位小写十六进制");
            return false;
        }

        if (!root.TryGetProperty("packageBytes", out var packageBytes) || packageBytes.ValueKind != JsonValueKind.Number
            || !packageBytes.TryGetInt64(out var packageSize) || packageSize <= 0
            || packageSize > UpdateAssets.MaxPackageBytes)
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, "packageBytes 不在这档");
            return false;
        }

        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, "files 不是数组");
            return false;
        }
        if (!TryReadFiles(files, out var list, out reject)) return false;

        manifest = new ReleaseManifest(schemaValue, version!, package!, packageSha!, packageSize, list);
        return true;
    }

    private static bool TryReadFiles(JsonElement array, out IReadOnlyList<ReleaseManifestFile> list,
        out ManifestReject reject)
    {
        list = Array.Empty<ReleaseManifestFile>();
        reject = ManifestReject.None;
        if (array.GetArrayLength() is 0 or > UpdateAssets.MaxManifestFiles)
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, $"files 颗数不在这档（{array.GetArrayLength()}）");
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);      // Windows 上同名不同大小写也是同一颗文件
        var read = new List<ReleaseManifestFile>();
        var hasEntryExe = false;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                reject = new ManifestReject(ManifestRejectKind.Unreadable, "files 里有一项不是对象");
                return false;
            }
            if (!TryReadString(element, "path", out var path, MaxPathLength, out reject)) return false;
            if (!IsSafeRelativePath(path) || !seen.Add(path!))
            {
                reject = new ManifestReject(ManifestRejectKind.FieldRejected, "path 越界、重复或不是相对路径");
                return false;
            }
            if (!TryReadString(element, "sha256", out var sha, 64, out reject)) return false;
            if (!IsLowercaseHex64(sha))
            {
                reject = new ManifestReject(ManifestRejectKind.FieldRejected, "sha256 不是 64 位小写十六进制");
                return false;
            }
            if (!element.TryGetProperty("bytes", out var bytes) || bytes.ValueKind != JsonValueKind.Number
                || !bytes.TryGetInt64(out var size) || size < 0 || size > UpdateAssets.MaxPackageBytes)
            {
                reject = new ManifestReject(ManifestRejectKind.FieldRejected, "bytes 不在这档");
                return false;
            }

            if (string.Equals(path, UpdateAssets.EntryExeName, StringComparison.OrdinalIgnoreCase)) hasEntryExe = true;
            read.Add(new ReleaseManifestFile(path!, sha!, size));
        }

        // 载荷里认不出主程序 ⇒ 这份清单换不出一个能启动的应用。宁可不装。
        if (!hasEntryExe)
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, $"files 里没有 {UpdateAssets.EntryExeName}");
            return false;
        }

        list = read;
        return true;
    }

    private static bool TryReadString(JsonElement owner, string name, out string? value, int maxLength,
        out ManifestReject reject)
    {
        value = null;
        reject = ManifestReject.None;
        if (!owner.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            reject = new ManifestReject(ManifestRejectKind.Unreadable, $"缺 {name}");
            return false;
        }
        var text = element.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > maxLength)
        {
            reject = new ManifestReject(ManifestRejectKind.FieldRejected, $"{name} 长度不在这档");
            return false;
        }
        foreach (var c in text)
            if (c < ' ' || c == '\u007f')
            {
                reject = new ManifestReject(ManifestRejectKind.FieldRejected, $"{name} 里有控制字符");
                return false;
            }
        value = text;
        return true;
    }

    /// <summary>载荷那颗：必须是个光秃秃的 <c>.zip</c> 文件名——不带路径、不带查询串、不带百分号。</summary>
    public static bool IsSafePackageName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name!.Length > MaxPackageNameLength) return false;
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.IndexOfAny(PackageForbiddenChars) >= 0) return false;
        if (name.StartsWith('.') || name.EndsWith('.') || name.Contains(' ')) return false;
        return !IsReservedDeviceName(name);
    }

    private static readonly char[] PackageForbiddenChars =
        { '/', '\\', '?', '#', '%', ':', '*', '|', '<', '>', '"', '\'' };

    /// <summary>
    /// 清单里的相对路径：<b>挡住"写到安装目录之外"的每一条路</b>。
    /// <para>这一條不是形式检查：解包时写出目标是"安装根目录 + 这个路径"，而 Zip Slip 的经典形状就是
    /// 包里的名字带 <c>../</c> 或绝对路径（BZ/CA 那两批普查 Everything 解包时判过它"恰好躲过"，
    /// 因为那里按叶名挑文件；这里必须自己挡，因为我们就是要按名字摆文件）。</para>
    /// </summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path!.Length > MaxPathLength) return false;
        if (path!.Contains(':') || path.Contains('\\')) return false;        // 盘符、UNC、NTFS 交替数据流
        if (path.StartsWith('/') || path.EndsWith('/') || path.Contains("//")) return false;
        var leaf = path[(path.LastIndexOf('/') + 1)..];
        if (leaf.Length == 0 || leaf.EndsWith(".") || leaf.EndsWith(" ") || leaf.StartsWith('.')) return false;
        if (IsReservedDeviceName(leaf.Split('.')[0])) return false;

        foreach (var segment in path!.Split('/'))
        {
            if (segment.Length == 0) return false;
            if (segment is "." or "..") return false;
        }
        return true;
    }

    /// <summary>Windows 的设备名不能当文件名，否则解出来就是一颗永远打不开的东西。</summary>
    private static bool IsReservedDeviceName(string stem)
    {
        var upper = stem.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$") return true;
        if (upper.Length is not (4 or 5) || (upper[0] != 'C' && upper[0] != 'L')) return false;
        var head = upper[0] == 'C' ? "COM" : "LPT";
        return upper.StartsWith(head, StringComparison.Ordinal)
            && upper.Length == head.Length + 1
            && upper[3] is >= '1' and <= '9';
    }

    /// <summary>只认<b>小写</b>十六进制：期望值与算出来的值都由 <see cref="UpdateHashHex"/> 生成，
    /// 允许两种大小写只是多留一处比较口径。</summary>
    public static bool IsLowercaseHex64(string? hex)
    {
        if (hex is null || hex.Length != 64) return false;
        foreach (var c in hex)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        return true;
    }
}
