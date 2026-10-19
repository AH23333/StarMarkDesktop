#nullable enable
using System;
using System.Text;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 盘上那两颗档里<b>该拿哪一份递出去</b>（Phase C 片43＝✅P-238；判据住 Core，读盘那一手归存储）。
/// <para><b>这颗坏长在两句判据之间，所以必须用字节摆</b>：存储问的是「主档在⛔ 在」，窗口把读回来的空串折成
/// 「这颗板不存在」⇒ 另起一颗新板，而紧挨着它那颗还能打开的 <c>.bak</c> 一次也没被问过。两句单看都⛔ 错，
/// 合起来才把唯一那条退路整个绕过（真机逐字＝<c>bin/WI91/readings_38b_174633.txt</c>：<c>via=no-scene</c>、
/// 我摆那颗档名⛔ 出现在任何一发里、那颗退路 sha 一字未动）。</para>
/// <para>⚠ 这一族此前只有形状尺（读源码文本那一族），因为测试工程⛔ 引用 <c>StarMark.UI</c>——
/// 判据因此搬进 Core：既让「什么算能打开的一份」只许有一个出处，也让它<b>当场问得到字节</b>（名册 346 那一族：
/// 测不到的分支就⛔ 声称测过；搬进可测的那一层比给不可测的那一层写注释诚实）。</para>
/// </summary>
public sealed class BoardPolicyScenePickTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>一颗形状齐全、内容极简的板档（那颗判据只看首尾那两个括弧）。</summary>
    private static byte[] Doc(string marker) => Utf8.GetBytes(
        "{\"type\":\"excalidraw\",\"version\":2,\"source\":\"starmark\",\"elements\":[],\"note\":\"" + marker + "\"}");

    [Fact]
    public void AWholeMainFileWinsOverTheFallbackAndIsNotMarkedAsRestored()
    {
        var main = Doc("这一份才是现在的那块板");
        Assert.True(BoardPolicy.PickReadableScene(main, Doc("上一版"), out var bytes, out var fromBackup));
        Assert.False(fromBackup, "主档好好的却报「从退路捡回来」＝第一拍会把同一份再写一遍，还可能顶掉退路");
        Assert.Equal(main, bytes);
    }

    [Fact]
    public void AMissingMainFileTakesTheFallbackAndSaysSo()
    {
        var backup = Doc("唯一还能打开的一份");
        Assert.True(BoardPolicy.PickReadableScene(null, backup, out var bytes, out var fromBackup));
        Assert.True(fromBackup);
        Assert.Equal(backup, bytes);
    }

    /// <summary>
    /// 「主档<b>在、却当⛔ 一份档」</b>那三颗形状（本批那颗坏的正身）：0 字节／截在半路／被人塞了别的东西。
    /// <para>⚠ 与 ✅P-234 同一批门路（同步工具的占位档／磁盘满时那次改名／用户从别处拷一颗空档进来），
    /// 那一头管「⛔ 把退路滚成垃圾」，这一头管「<b>退路读得到头</b>」。</para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("{\"type\":\"excalidraw\",\"elements\":[")]
    [InlineData("not a scene at all")]
    [InlineData("{\"a\":1} trailing junk after the doc")]
    public void WhateverIsNotADocShapeNeverBecomesTheBoard(string garbage)
    {
        var backup = Doc("还能打开的那一份");
        Assert.True(BoardPolicy.PickReadableScene(Utf8.GetBytes(garbage), backup, out var bytes, out var fromBackup),
            "垃圾主档 ⇒ 这颗板读不到？那正是「盘上明明还有、却另起一颗新板」那一步");
        Assert.True(fromBackup);
        Assert.Equal(backup, bytes);
    }

    [Fact]
    public void PaddingAroundTheDocStillCountsAsADoc()
    {
        var padded = Utf8.GetBytes("\r\n\t " + "{\"type\":\"excalidraw\",\"elements\":[]}" + "\n");
        Assert.True(BoardPolicy.PickReadableScene(padded, null, out var bytes, out var fromBackup));
        Assert.False(fromBackup);
        Assert.Equal(padded, bytes);   // 原样交回：这颗判据⛔ 改写用户的数据
    }

    [Fact]
    public void ADifferentKeyOrderInTheMainFileIsStillTheBoardThatLoads()
    {
        // ⚠ 对照臂（名册 349 那一族）：把形状判据写成「只认真序列化器吐出的那颗头一个键」就会漏掉这一发。
        var other = Utf8.GetBytes("{\"files\":{},\"type\":\"excalidraw\",\"elements\":[],\"scrollX\":0}");
        Assert.True(BoardPolicy.PickReadableScene(other, Doc("旧的一份"), out var bytes, out var fromBackup));
        Assert.False(fromBackup);
        Assert.Equal(other, bytes);
    }

    [Fact]
    public void NothingIsReadableWhenNeitherCopyIsADoc()
    {
        Assert.False(BoardPolicy.PickReadableScene(Array.Empty<byte>(), Utf8.GetBytes("{}{"), out var bytes, out var fromBackup));
        Assert.Empty(bytes);        // ⛔ 把一颗垃圾当"空=读不到"交回（名册 431："null 折成 0"那一族）
        Assert.False(fromBackup);
        Assert.False(BoardPolicy.PickReadableScene(null, null, out var none, out var flag));
        Assert.Empty(none);
        Assert.False(flag);
    }

    /// <summary>
    /// 两片咬合（本批最要紧的一格）：从退路读回来的那一份<b>再写回去</b>时，滚那一头认得出那颗垃圾主档⛔ 配滚
    /// （✅P-234）⇒ 主档位置被补回来而退路一字未动。少了这一格，本批的修法就只是「换一种方式丢数据」。
    /// </summary>
    [Fact]
    public void RestoringFromTheFallbackDoesNotRollTheFallbackAway()
    {
        var garbageMain = Array.Empty<byte>();
        var backup = Doc("还能打开的那一份");
        Assert.True(BoardPolicy.PickReadableScene(garbageMain, backup, out var restored, out var fromBackup));
        Assert.True(fromBackup);
        Assert.False(BoardPolicy.BackupRollNeeded(garbageMain, restored),
            "这一发真滚＝把刚救回来的那颗退路顶成它自己的同件＝两条线合起来还是丢一份数据");

        // 正件（⛔ 让那颗滚判据被同时写成恒假也绿）：主档是好档而来件不同时，照旧该滚。
        var goodMain = Doc("盘上现在是这一份");
        Assert.True(BoardPolicy.BackupRollNeeded(goodMain, Doc("新的一笔")));
    }
}
