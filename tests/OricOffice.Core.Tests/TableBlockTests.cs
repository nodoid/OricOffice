using OricOffice.Core.Model;

namespace OricOffice.Core.Tests;

public class TableBlockTests
{
    [Fact]
    public void NewTable_StartsThreeByThree_AndEmpty()
    {
        var table = new TableBlock("Table 1", 3, 3);
        Assert.Equal(3, table.Rows);
        Assert.Equal(3, table.Columns);
        Assert.True(table.IsEmpty);
    }

    [Fact]
    public void AddRow_Appends_AndKeepsExistingCells()
    {
        var table = new TableBlock("T", 2, 2);
        table.SetText(0, 0, "a");
        table.SetText(1, 1, "b");
        table.AddRow();

        Assert.Equal(3, table.Rows);
        Assert.Equal("a", table.TextAt(0, 0));
        Assert.Equal("b", table.TextAt(1, 1));
        Assert.Equal(string.Empty, table.TextAt(2, 0));
    }

    [Fact]
    public void RemoveRow_DeletesRequestedRow_AndKeepsOrder()
    {
        var table = new TableBlock("T", 3, 2);
        table.SetText(0, 0, "r0");
        table.SetText(1, 0, "r1");
        table.SetText(2, 0, "r2");

        table.RemoveRow(1);

        Assert.Equal(2, table.Rows);
        Assert.Equal("r0", table.TextAt(0, 0));
        Assert.Equal("r2", table.TextAt(1, 0));
    }

    [Fact]
    public void RemoveRow_NeverGoesBelowOneRow()
    {
        var table = new TableBlock("T", 2, 2);
        table.RemoveRow(0);
        table.RemoveRow(0);
        Assert.Equal(1, table.Rows);
    }

    [Fact]
    public void AddColumn_And_RemoveColumn_PreserveValuesAndFills()
    {
        var table = new TableBlock("T", 2, 2);
        table.SetText(0, 0, "a");
        table.SetText(0, 1, "b");
        table.SetFill(0, 1, "#FFCC00");

        table.AddColumn(); // inserts after last column by default
        Assert.Equal(3, table.Columns);
        Assert.Equal("b", table.TextAt(0, 1));
        Assert.Equal("#FFCC00", table.FillAt(0, 1));

        table.RemoveColumn(0);
        Assert.Equal(2, table.Columns);
        Assert.Equal("b", table.TextAt(0, 0));
        Assert.Equal("#FFCC00", table.FillAt(0, 0));
        Assert.Equal(string.Empty, table.TextAt(0, 1));
    }

    [Fact]
    public void RemoveColumn_NeverGoesBelowOneColumn()
    {
        var table = new TableBlock("T", 2, 2);
        table.RemoveColumn(0);
        table.RemoveColumn(0);
        Assert.Equal(1, table.Columns);
    }

    [Fact]
    public void SetFill_WithNullOrWhitespace_ClearsTheColour()
    {
        var table = new TableBlock("T", 2, 2);
        table.SetFill(0, 0, "#112233");
        Assert.Equal("#112233", table.FillAt(0, 0));
        table.SetFill(0, 0, null);
        Assert.Null(table.FillAt(0, 0));
        table.SetFill(0, 0, "#112233");
        table.SetFill(0, 0, "  ");
        Assert.Null(table.FillAt(0, 0));
    }

    [Fact]
    public void Restore_ReplacesContentAndName()
    {
        var table = new TableBlock("Old", 3, 3);
        var text = new[] { new[] { "1", "2" }, new[] { "3", "4" } };
        var fill = new[] { new string?[] { "#AABBCC", null }, new string?[] { null, null } };
        table.Restore(text, fill, "Restored");

        Assert.Equal("Restored", table.Name);
        Assert.Equal(2, table.Rows);
        Assert.Equal(2, table.Columns);
        Assert.Equal("4", table.TextAt(1, 1));
        Assert.Equal("#AABBCC", table.FillAt(0, 0));
    }
}
