#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace StarMark.Abstractions.Text;

/// <summary>
/// CJK 全文检索分词补偿（纯函数，便于单元测试）。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它：</b>SQLite FTS5 的 <c>unicode61</c> 分词器把一段连续的
/// CJK 字符视为<b>一个</b> token（CJK 没有空格，unicode61 依空格/标点断词）。
/// 于是「搜索笔记工具」在索引里只有一个 token，用户搜「笔记」「工具」「收藏」
/// 这类<b>中间子串</b>全部落空——只有「从开头起的连续前缀」能命中。
/// 项目早期 Schema.sql 注释写的「兼顾中文按字分词」是错的。</para>
///
/// <para><b>为什么不用 trigram：</b>实测 <c>tokenize='trigram'</c> 仅对 ≥3 字的
/// 查询生效，「笔记」「搜索」「收藏」「统一」这类 2 字词全部漏召回，
/// 而 2 字词在中文里频率极高。故不采用。</para>
///
/// <para><b>方案（思路对齐 StarMark 扩展 <c>search/indexer.ts</c> 的 <c>cjkAwareTokenize</c>）：</b>
/// 只在<b>写入侧</b>把每段 CJK 连续串展开为「单字 + 相邻二元组」，
/// FTS 表结构、触发器、<c>content='items'</c> 外部内容表配置一概不动。</para>
///
/// <para><b>为什么不发「整串」token：</b>查询侧从不发整串（见下方铁律），
/// 因此整串 token 对召回零贡献，只占索引空间；且对 2 字串会与二元组重复，
/// 虚增词频、轻微扭曲 bm25 排序。故不发送。</para>
///
/// <para><b>查询侧铁律：</b>只发二元组（1 字时发单字），<b>绝不发整串</b>。
/// 若把整串也 AND 进去，查询「笔记工具」会要求索引中存在 token「笔记工具」，
/// 而文档「搜索笔记工具」的整串 token 是「搜索笔记工具」——必然落空，
/// 导致 4 字以上的中文查询自我淘汰。</para>
/// </remarks>
public static class CjkTokenizer
{
    /// <summary>CJK 字符判定（BMP 范围；CJK 扩展 B 区属增补平面，日常罕见，不处理）。</summary>
    public static bool IsCjk(char c) =>
        (c >= 0x3400 && c <= 0x4DBF) ||   // CJK 扩展 A
        (c >= 0x4E00 && c <= 0x9FFF) ||   // CJK 基本区
        (c >= 0xF900 && c <= 0xFAFF) ||   // CJK 兼容表意文字
        (c >= 0x3040 && c <= 0x30FF) ||   // 日文平假名 / 片假名
        (c >= 0xAC00 && c <= 0xD7AF) ||   // 韩文音节
        (c >= 0xFF66 && c <= 0xFF9F);     // 半角片假名

    /// <summary>字符串中是否含有 CJK 字符。</summary>
    public static bool ContainsCjk(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if (IsCjk(c)) return true;
        }
        return false;
    }

    /// <summary>
    /// 写入侧展开：把文本中每段 CJK 连续串展开为「整串 + 单字 + 相邻二元组」，
    /// 非 CJK 部分原样保留。用于构造 <c>items.search_text</c>。
    /// </summary>
    public static string ExpandForIndex(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length * 3);
        int i = 0;
        while (i < text.Length)
        {
            if (!IsCjk(text[i]))
            {
                sb.Append(text[i]);
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && IsCjk(text[i])) i++;
            AppendRun(sb, text.AsSpan(start, i - start));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 查询侧拆分：把关键词拆成 FTS5 词元，调用方以空格（AND）连接。
    /// CJK 连续串 → 相邻二元组（1 字时为单字）；非 CJK 片段原样成词。
    /// </summary>
    /// <remarks>
    /// 返回的是<b>未转义</b>的原始词元，引号与前缀通配符由调用方按 FTS5 规则添加。
    /// 特意不返回整串——见类注释「查询侧铁律」。
    /// </remarks>
    public static List<string> SplitForQuery(string? keyword)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(keyword)) return result;

        foreach (string part in keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var buf = new StringBuilder();
            int i = 0;
            while (i < part.Length)
            {
                if (IsCjk(part[i]))
                {
                    if (buf.Length > 0) { result.Add(buf.ToString()); buf.Clear(); }

                    int start = i;
                    while (i < part.Length && IsCjk(part[i])) i++;
                    AppendRunTokens(result, part.AsSpan(start, i - start));
                    continue;
                }

                buf.Append(part[i]);
                i++;
            }
            if (buf.Length > 0) result.Add(buf.ToString());
        }
        return result;
    }

    private static void AppendRun(StringBuilder sb, ReadOnlySpan<char> run)
    {
        // 单字：支撑 1 字查询
        for (int k = 0; k < run.Length; k++)
        {
            sb.Append(run[k]).Append(' ');
        }

        // 相邻二元组：支撑任意子串查询（含跨词边界，如「搜索笔记」中的「索笔」）
        for (int k = 0; k + 1 < run.Length; k++)
        {
            sb.Append(run[k]).Append(run[k + 1]).Append(' ');
        }
    }

    private static void AppendRunTokens(List<string> result, ReadOnlySpan<char> run)
    {
        if (run.Length == 1)
        {
            result.Add(run.ToString());
            return;
        }
        for (int k = 0; k + 1 < run.Length; k++)
        {
            result.Add(run.Slice(k, 2).ToString());
        }
    }
}
