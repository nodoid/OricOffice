using OricOffice.Core.Model;
using OricOffice.Core.Storage;

namespace OricOffice.Core.Tests;

public class TableBorderTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly OricFileStore _store;

    public TableBorderTests() => _store = new OricFileStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ApplyBorder_StylesChosenSidesOfARegion()
    {
        var table = new TableBlock("T", 3, 3);

        table.ApplyBorder(0, 0, 2, 2, BorderSides.All, BorderStyle.Thick, "#C00000");

        Assert.Equal(BorderStyle.Thick, table.BorderAt(0, 0, BorderSide.Top)!.Style);
        Assert.Equal("#C00000", table.BorderAt(1, 1, BorderSide.Left)!.Colour);
        Assert.Null(table.BorderAt(0, 2, BorderSide.Top)); // outside the region
        Assert.Null(table.BorderAt(2, 0, BorderSide.Top));
        Assert.Equal(2 * 2 * 4, table.Borders.Count);
    }

    [Fact]
    public void ApplyBorder_OnlyTheChosenSides()
    {
        var table = new TableBlock("T", 2, 2);

        table.ApplyBorder(0, 0, 1, 1, BorderSides.Bottom | BorderSides.Right, BorderStyle.Double, null);

        Assert.NotNull(table.BorderAt(0, 0, BorderSide.Bottom));
        Assert.NotNull(table.BorderAt(0, 0, BorderSide.Right));
        Assert.Null(table.BorderAt(0, 0, BorderSide.Top));
        Assert.Null(table.BorderAt(0, 0, BorderSide.Left));
    }

    [Fact]
    public void ApplyBorder_None_ClearsTheSides()
    {
        var table = new TableBlock("T", 2, 2);
        table.ApplyBorder(0, 0, 2, 2, BorderSides.All, BorderStyle.Dashed, "#000000");

        table.ApplyBorder(0, 0, 2, 2, BorderSides.All, BorderStyle.None, null);

        Assert.Empty(table.Borders);
    }

    [Fact]
    public void RemovingAndInsertingMovesBordersWithTheirCells()
    {
        var table = new TableBlock("T", 3, 3);
        table.SetBorder(2, 2, BorderSide.Top, new CellBorder(BorderStyle.Thick, "#123456"));

        table.RemoveRow(0); // (2,2) → (1,2)
        table.RemoveColumn(0); // → (1,1)
        Assert.Equal(BorderStyle.Thick, table.BorderAt(1, 1, BorderSide.Top)!.Style);
        Assert.Single(table.Borders);

        table.AddRow(); // appended at the end → unchanged
        table.AddColumn(0); // inserted before column 0 → (1,2)
        Assert.Equal(BorderStyle.Thick, table.BorderAt(1, 2, BorderSide.Top)!.Style);
        Assert.Single(table.Borders);
    }

    [Fact]
    public void Borders_PersistWithTheDocument()
    {
        var file = OricFile.CreateNew(OricFileKind.Author, "Doc");
        file.Text.Body = "hello";
        var table = new TableBlock("Data", 2, 2);
        table.SetText(0, 0, "a");
        table.ApplyBorder(0, 0, 2, 1, BorderSides.All, BorderStyle.Dashed, "#0070C0");
        file.Text.Tables.Add(table);

        var path = _store.Save(file, _dir.File("doc"), OricFileKind.Author);
        var loaded = _store.Load(path);

        var t = Assert.Single(loaded.Text.Tables);
        Assert.Equal(BorderStyle.Dashed, t.BorderAt(1, 0, BorderSide.Bottom)!.Style);
        Assert.Equal("#0070C0", t.BorderAt(0, 0, BorderSide.Left)!.Colour);
        Assert.Equal(8, t.Borders.Count); // 2 cells × 4 sides
    }

    [Fact]
    public void OldFilesWithoutBorders_StillLoad()
    {
        // A save from before borders existed simply has no border payload.
        var file = OricFile.CreateNew(OricFileKind.Author, "Old");
        var table = new TableBlock("Plain", 2, 2);
        table.SetText(1, 1, "x");
        file.Text.Tables.Add(table);

        var path = _store.Save(file, _dir.File("old"), OricFileKind.Author);
        var loaded = _store.Load(path);

        Assert.Single(loaded.Text.Tables);
        Assert.Empty(loaded.Text.Tables[0].Borders);
    }
}
