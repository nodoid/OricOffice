using OricOffice.Core.Calc;

namespace OricOffice.Core.Tests;

public class CellAddressTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ColumnName_FormatsColumns(int column, string expected)
        => Assert.Equal(expected, CellAddress.ColumnName(column));

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("B12", 11, 1)]
    [InlineData("aa1", 0, 26)]
    [InlineData("$C$3", 2, 2)]
    [InlineData("  Z9  ", 8, 25)]
    public void TryParse_AcceptsValidAddresses(string text, int row, int column)
    {
        Assert.True(CellAddress.TryParse(text, out var address));
        Assert.Equal(row, address.Row);
        Assert.Equal(column, address.Column);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("12")]
    [InlineData("A0")]
    [InlineData("AAAA1")]
    [InlineData("A1x")]
    [InlineData("1A")]
    [InlineData("A-1")]
    public void TryParse_RejectsInvalidAddresses(string text)
        => Assert.False(CellAddress.TryParse(text, out _));

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        var address = new CellAddress(41, 26); // AA42
        Assert.Equal("AA42", address.ToString());
        Assert.True(CellAddress.TryParse(address.ToString(), out var parsed));
        Assert.Equal(address, parsed);
    }
}
