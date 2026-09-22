#nullable enable
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「排序」下拉 → Everything 原生排序值的映射，以及体积读数的逐字表达。
/// 排序值取自官方 SDK include/Everything.h 的 EVERYTHING_SORT_*（写错一位就是静默排错序，
/// 故逐字钉住）；无对应语义的排序项必须返回 null（不下发），而不是硬套一个相近值。
/// </summary>
public sealed class EverythingSortAndSizeTests
{
    [Theory]
    [InlineData("recent", EverythingSort.DateModifiedDescending)]
    [InlineData("name", EverythingSort.NameAscending)]
    public void MappedSorts_PinTheSdkEnumValues(string uiSort, uint expected)
        => Assert.Equal(expected, EverythingSort.Map(uiSort));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relevance")]
    [InlineData("stars")]
    [InlineData("starred")]
    [InlineData("collected")]
    [InlineData("whatever")]
    public void UnmappedSorts_LeaveEverythingDefaultOrder(string? uiSort)
        => Assert.Null(EverythingSort.Map(uiSort));

    [Fact]
    public void SortValues_AreNonZero_BecauseZeroMeansDoNotSend()
    {
        // Query() 以 0 作「不下发 SetSort」的哨兵；映射值若为 0 就等于静默失效。
        Assert.Equal(1u, EverythingSort.NameAscending);
        Assert.Equal(14u, EverythingSort.DateModifiedDescending);
        Assert.True(EverythingSort.Map("recent") > 0);
        Assert.True(EverythingSort.Map("name") > 0);
    }

    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(2147483648L, "2 GB")]
    public void FileSizeText_Human_Format(long bytes, string expected)
        => Assert.Equal(expected, FileSizeText.Human(bytes));
}
