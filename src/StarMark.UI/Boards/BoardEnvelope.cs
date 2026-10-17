#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace StarMark.UI.Boards;

/// <summary>
/// 桥信封 <c>{ v, cmd, id, args }</c> 的<b>解包</b>（UI 侧胶水；形状判据在 Core 的
/// <see cref="StarMark.Core.Boards.BridgeProtocol"/>，这里只把 JSON 变成字段）。
/// ⛔ 抛 JSON 异常：坏形状统一归 <see cref="Invalid"/>，走拒那一档。
/// </summary>
public readonly struct BoardEnvelope
{
    public int Version { get; init; }
    public string? Cmd { get; init; }
    public string? Id { get; init; }
    public string? Bytes { get; init; }
    public string? Kind { get; init; }
    public string? Suggested { get; init; }
    public string? Url { get; init; }
    public string? Shape { get; init; }
    public string? Token { get; init; }

    /// <summary><c>board.loaded</c> 那两颗数：档里读到几颗元素／画布上当场几颗。认不出（缺键、非数）＝null。</summary>
    public int? Elements { get; init; }
    public int? Canvas { get; init; }
    public bool Invalid { get; init; }

    /// <summary>args 的键集（给 <see cref="StarMark.Core.Boards.BridgeProtocol.Check"/> 的字段白名单门）。</summary>
    public IReadOnlyList<string> ArgKeys { get; init; }

    public static BoardEnvelope Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return InvalidEnvelope();
            var keys = new List<string>();
            if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                foreach (var p in args.EnumerateObject()) keys.Add(p.Name);
            string? Get(string name)
                => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? Arg(string name)
                => root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
                    && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int? ArgInt(string name)
                => root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
                    && a.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;
            var version = root.TryGetProperty("v", out var vv) && vv.TryGetInt32(out var i) ? i : int.MinValue;
            return new BoardEnvelope
            {
                Version = version,
                Cmd = Get("cmd"),
                Id = Get("id"),
                Bytes = Arg("bytes"),
                Kind = Arg("kind"),
                Suggested = Arg("suggested"),
                Url = Arg("url"),
                Shape = Arg("shape"),
                Token = Arg("token"),
                Elements = ArgInt("elements"),
                Canvas = ArgInt("canvas"),
                ArgKeys = keys,
            };
        }
        catch (JsonException)
        {
            return InvalidEnvelope();
        }
    }

    private static BoardEnvelope InvalidEnvelope() => new() { Invalid = true, ArgKeys = Array.Empty<string>() };
}
