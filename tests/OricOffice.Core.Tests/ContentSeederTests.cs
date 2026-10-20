using OricOffice.Core.Model;
using OricOffice.Core.Seeders;

namespace OricOffice.Core.Tests;

public class ContentSeederTests
{
    // ------------------------------------------------------------ text ↔ sheet

    [Fact]
    public void SheetFromText_SplitsLinesAndCommas()
    {
        var sheet = new SheetDocument();
        ContentSeeder.SheetFromText(sheet, "a,b\nc");

        Assert.Equal("a", sheet.ValueAt(0, 0));
        Assert.Equal("b", sheet.ValueAt(0, 1));
        Assert.Equal("c", sheet.ValueAt(1, 0));
        Assert.Equal(2, sheet.UsedRowCount);
        Assert.Equal(2, sheet.UsedColumnCount);
    }

    [Fact]
    public void SheetFromText_HandlesQuotedCommas()
    {
        var sheet = new SheetDocument();
        ContentSeeder.SheetFromText(sheet, "a,\"b,c\",d");

        Assert.Equal("b,c", sheet.ValueAt(0, 1));
        Assert.Equal("d", sheet.ValueAt(0, 2));
    }

    [Fact]
    public void TextFromSheet_JoinsColumnsAsCsv()
    {
        var sheet = new SheetDocument();
        sheet.Set(0, 0, "10", "10");
        sheet.Set(0, 1, "20", "20");
        sheet.Set(1, 0, "x", "x");

        var text = ContentSeeder.TextFromSheet(sheet);
        var lines = text.Split('\n');
        Assert.Equal("10, 20", lines[0].TrimEnd('\r'));
        Assert.Equal("x", lines[1].TrimEnd('\r'));
    }

    [Fact]
    public void SheetTextRoundTrip_PreservesValues()
    {
        var original = new SheetDocument();
        original.Set(0, 0, "name", "name");
        original.Set(0, 1, "score", "score");
        original.Set(1, 0, "Ann", "Ann");
        original.Set(1, 1, "42", "42");

        var text = ContentSeeder.TextFromSheet(original);
        var restored = new SheetDocument();
        ContentSeeder.SheetFromText(restored, text);

        Assert.Equal("name", restored.ValueAt(0, 0));
        Assert.Equal("Ann", restored.ValueAt(1, 0));
        Assert.Equal("42", restored.ValueAt(1, 1));
    }

    // ------------------------------------------------------------ table ↔ sheet

    [Fact]
    public void SheetFromTable_WritesHeaderAndRows()
    {
        var table = new TableDocument();
        var name = table.AddField("Name");
        var age = table.AddField("Age", FieldType.Number);
        var r1 = table.AddRecord();
        r1.Set(name.Name, "Ann");
        r1.Set(age.Name, "29");
        var r2 = table.AddRecord();
        r2.Set(name.Name, "Bob");
        r2.Set(age.Name, "17");

        var sheet = new SheetDocument();
        ContentSeeder.SheetFromTable(sheet, table);

        Assert.Equal("Name", sheet.ValueAt(0, 0));
        Assert.Equal("Age", sheet.ValueAt(0, 1));
        Assert.Equal("Ann", sheet.ValueAt(1, 0));
        Assert.Equal("17", sheet.ValueAt(2, 1));
    }

    [Fact]
    public void TableFromSheet_CreatesFieldsAndInfersTypes()
    {
        var sheet = new SheetDocument();
        sheet.Set(0, 0, "Name", "Name");
        sheet.Set(0, 1, "Score", "Score");
        sheet.Set(1, 0, "Ann", "Ann");
        sheet.Set(1, 1, "42", "42");
        sheet.Set(2, 0, "Bob", "Bob");
        sheet.Set(2, 1, "7", "7");

        var table = new TableDocument();
        ContentSeeder.TableFromSheet(table, sheet);

        Assert.Equal(2, table.Fields.Count);
        Assert.Equal(FieldType.Text, table.Fields[0].Type);
        Assert.Equal(FieldType.Number, table.Fields[1].Type);
        Assert.Equal(2, table.Records.Count);
        Assert.Equal("Ann", table.Records[0].Get("Name"));
        Assert.Equal("7", table.Records[1].Get("Score"));
    }

    [Fact]
    public void TableFromSheet_GeneratesMissingHeaderNames()
    {
        var sheet = new SheetDocument();
        sheet.Set(1, 0, "Ann", "Ann"); // row 0 (header) empty

        var table = new TableDocument();
        ContentSeeder.TableFromSheet(table, sheet);

        Assert.Single(table.Fields);
        Assert.Equal("Column1", table.Fields[0].Name);
        Assert.Single(table.Records);
    }

    [Fact]
    public void TableFromSheet_UniquifiesDuplicateHeaders()
    {
        var sheet = new SheetDocument();
        sheet.Set(0, 0, "Tag", "Tag");
        sheet.Set(0, 1, "Tag", "Tag");

        var table = new TableDocument();
        ContentSeeder.TableFromSheet(table, sheet);

        Assert.Equal("Tag", table.Fields[0].Name);
        Assert.Equal("Tag2", table.Fields[1].Name);
    }

    // ------------------------------------------------------------ text ↔ table

    [Fact]
    public void TableFromText_CsvHeaderBecomesFields()
    {
        var table = new TableDocument();
        ContentSeeder.TableFromText(table, "Name,Age\nAnn,29\nBob,17");

        Assert.Equal(new[] { "Name", "Age" }, table.Fields.Select(f => f.Name));
        Assert.Equal(2, table.Records.Count);
        Assert.Equal("29", table.Records[0].Get("Age"));
    }

    [Fact]
    public void TableFromText_FreeTextBecomesLineRecords()
    {
        var table = new TableDocument();
        ContentSeeder.TableFromText(table, "first line\nsecond line");

        Assert.Single(table.Fields);
        Assert.Equal("Line", table.Fields[0].Name);
        Assert.Equal(2, table.Records.Count);
        Assert.Equal("first line", table.Records[0].Get("Line"));
    }

    [Fact]
    public void TableToText_RoundTripsCsv()
    {
        var table = new TableDocument();
        var name = table.AddField("Name");
        var note = table.AddField("Note");
        var row = table.AddRecord();
        row.Set(name.Name, "Ann");
        row.Set(note.Name, "hello, \"world\"");

        var text = ContentSeeder.TextFromTable(table);
        var restored = new TableDocument();
        ContentSeeder.TableFromText(restored, text);

        Assert.Equal(2, restored.Fields.Count);
        Assert.Equal("hello, \"world\"", restored.Records[0].Get("Note"));
    }

    // ------------------------------------------------------------ Ensure* semantics

    [Fact]
    public void EnsureText_OnlySeedsWhenEmpty()
    {
        var text = new TextDocument { Body = "keep me" };
        var sheet = new SheetDocument();
        sheet.Set(0, 0, "x", "x");

        ContentSeeder.EnsureText(text, sheet, new TableDocument());
        Assert.Equal("keep me", text.Body);
    }

    [Fact]
    public void EnsureText_SeedsFromSheetWhenEmpty()
    {
        var text = new TextDocument();
        var sheet = new SheetDocument();
        sheet.Set(0, 0, "from sheet", "from sheet");

        ContentSeeder.EnsureText(text, sheet, new TableDocument());
        Assert.Contains("from sheet", text.Body);
    }

    [Fact]
    public void EnsureSheet_SeedsFromTableWhenEmpty()
    {
        var sheet = new SheetDocument();
        var table = new TableDocument();
        var f = table.AddField("City");
        var rec = table.AddRecord();
        rec.Set(f.Name, "York");

        ContentSeeder.EnsureSheet(sheet, new TextDocument(), table);
        Assert.Equal("City", sheet.ValueAt(0, 0));
        Assert.Equal("York", sheet.ValueAt(1, 0));
    }

    [Fact]
    public void EnsureTable_ProducesField1WhenNothingToSeed()
    {
        var table = new TableDocument();
        ContentSeeder.EnsureTable(table, new TextDocument(), new SheetDocument());
        Assert.Single(table.Fields);
        Assert.Equal("Field1", table.Fields[0].Name);
    }

    [Fact]
    public void EnsureTable_DoesNotTouchExistingSchema()
    {
        var table = new TableDocument();
        table.AddField("Keep");

        var sheet = new SheetDocument();
        sheet.Set(0, 0, "nope", "nope");
        ContentSeeder.EnsureTable(table, new TextDocument(), sheet);

        Assert.Single(table.Fields);
        Assert.Equal("Keep", table.Fields[0].Name);
        Assert.Empty(table.Records);
    }
}
