#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;

namespace StarMark.Core.Feed;

/// <summary>
/// 订阅条目与校验符的<b>落盘档</b>（<c>rss-cache.json</c>）。
/// <para>
/// 为什么要有它（用户原话："建议添加缓存机制，每天仅刷新一次，且增量刷新"）：
/// 打开这一页本来是<b>一片空白 + 一次全量抓取</b>——抓六个源要等六个源的网，
/// 而抓回来的东西关掉程序就没了，下一次又从零开始。有了这一档，
/// 进页面先摆上次的条目（不联网），到期的源才去补抓（<see cref="RssFeedCache.IsDue"/>），
/// 抓回来的按链接并进旧的那一份（<see cref="RssFeedCache.Merge"/>）。
/// </para>
/// <para>
/// 放在 Core 而不是 UI 的 SettingsStore 旁边：这一档的"读回来要先清洗、写下去要原子"两条判据
/// 都得能被测试跑到（<c>SettingsStore</c> 在 UI 层，测试项目引用不到）。
/// </para>
/// </summary>
public sealed class RssCacheStore(string? path = null)
{
    private readonly string _path = path ?? DefaultPath();

    /// <summary>与 settings.json / widgets.json 同目录（同一套环境变量优先规则，测试才能整体重定向）。
    /// 规则本体只有一处：<see cref="StarMark.Abstractions.UserDataPaths"/>。</summary>
    public static string DefaultPath() => StarMark.Abstractions.UserDataPaths.Sibling("rss-cache.json");

    public string StorePath => _path;

    /// <summary>
    /// 读缓存。<b>读不出来＝当作没有缓存</b>（返回空档），不抛：
    /// 这一档只是"少跑一次网络"的加速件，坏了最重的后果是多抓一次，不该把整页按住。
    /// <para>但也不能静默——路径/权限/损坏三种情况要能在日志里分开，否则"缓存好像没生效"这种事无从归因。</para>
    /// </summary>
    public RssCacheFile Load()
    {
        try
        {
            if (!File.Exists(_path)) return new RssCacheFile();
            return RssFeedCache.Normalize(JsonSerializer.Deserialize<RssCacheFile>(File.ReadAllText(_path)));
        }
        catch (JsonException ex)
        {
            StarLog.Warn($"[RSS] 缓存档不是合法 JSON，按空缓存处理（会重新抓一次）：{ex.Message}");
            return new RssCacheFile();
        }
        catch (Exception ex)
        {
            // 目录被 OneDrive 同步、被杀软短暂锁定都是常态：这一次按空缓存走，下一次还有机会
            StarLog.Warn($"[RSS] 读取缓存失败，按空缓存处理：{ex.GetType().Name} {ex.Message}");
            return new RssCacheFile();
        }
    }

    /// <summary>
    /// 写缓存：先写 <c>.tmp</c> 再 <see cref="File.Move(string,string,bool)"/> 原子覆盖——
    /// 写盘中途崩溃把这一档截断成非法 JSON 的话，用户丢的不是"缓存"而是"上次抓到的全部条目"。
    /// <para>返回是否真的落盘。<b>失败必须能让调用方说出来</b>（批次 HC 同一口径）：
    /// 静默失败的表现是"我明明刷新了，怎么下次还是旧的"。</para>
    /// </summary>
    public bool Save(RssCacheFile file)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file ?? new RssCacheFile()));
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error($"[RSS] 缓存没能写进磁盘：{_path}", ex);
            return false;
        }
    }

    /// <summary>源被删掉之后清掉它的缓存：留着没有任何地方会再读到它，而这一档是会跟着源数长大的。</summary>
    public RssCacheFile Without(RssCacheFile file, System.Collections.Generic.IEnumerable<int> aliveSourceIds)
    {
        var alive = new HashSet<int>(aliveSourceIds);
        file.Sources = file.Sources.Where(s => alive.Contains(s.SourceId)).ToList();
        return file;
    }
}
