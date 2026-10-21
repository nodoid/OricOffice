using OricOffice.Core;
using OricOffice.Core.Export;
using OricOffice.Core.Model;
using OricOffice.Core.Storage;
using OricOffice.Core.Tape;

// Generates the committed suite fixture files:
//   author.oau — body text, an embedded image, a filled table
//   calc.oca   — literals, a SUM formula, a floating image
//   base.oba   — typed fields and records
// Usage: dotnet run --project tools/FixtureGen -- [outputDir]
//        dotnet run --project tools/FixtureGen -- --ort <capture.ort> <out.tap>

// --ort: convert an Oricutron raw-tape capture (F9 "Save tape output…") into
// a standard .tap image and list what it holds.
if (args.Length == 3 && args[0] == "--ort")
{
    var tap = OricutronOrt.ToTap(File.ReadAllBytes(args[1]));
    File.WriteAllBytes(args[2], tap);
    Console.WriteLine($"wrote {args[2]} ({tap.Length} bytes)");
    foreach (var file in OricTap.Read(tap))
        Console.WriteLine($"  {file.Name} {(file.IsBasic ? "BASIC" : "BLOCK")} " +
            $"${file.LoadAddress:X4}-${file.EndAddress:X4} {file.Length} bytes");
    return;
}

var outDir = args.Length > 0 ? args[0] : Path.Combine("tests", "fixtures");
Directory.CreateDirectory(outDir);
var store = new OricFileStore(Path.GetTempPath()); // data root irrelevant: explicit save paths

// 1×1 transparent PNG.
var png = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

var stamp = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

// ------------------------------------------------------------ Author
var author = OricFile.CreateNew(OricFileKind.Author, "Oric Author Sample");
author.CreatedUtc = stamp;
author.Text.Body =
    "The Oric Atmos word processor sample.\n" +
    "This paragraph proves word wrap and word count import correctly.\n\n" +
    "A second paragraph after a blank line, with punctuation: commas, dashes — and more.";

author.Text.Images.Add(new DocumentImage("logo.png", png));

var people = new TableBlock("People", 4, 3);
people.SetText(0, 0, "Name");
people.SetText(0, 1, "Role");
people.SetText(0, 2, "Year");
people.SetFill(0, 0, "#DDEBF7");
people.SetFill(0, 1, "#DDEBF7");
people.SetFill(0, 2, "#DDEBF7");
people.SetText(1, 0, "Geoff");
people.SetText(1, 1, "Author");
people.SetText(1, 2, "1984");
people.SetText(2, 0, "Chris");
people.SetText(2, 1, "Calculator");
people.SetText(2, 2, "1983");
people.SetText(3, 0, "Anne");
people.SetText(3, 1, "Base");
people.SetText(3, 2, "1983");
author.Text.Tables.Add(people);

var authorPath = Path.Combine(outDir, "author" + OricFileKind.Author.For());
store.Save(author, authorPath, OricFileKind.Author);
Console.WriteLine($"wrote {authorPath}");

// ------------------------------------------------------------ Calc
var calc = OricFile.CreateNew(OricFileKind.Calc, "Oric Calc Sample");
calc.CreatedUtc = stamp;
calc.Sheet.Set(0, 0, "Item", "Item");
calc.Sheet.Set(0, 1, "Price", "Price");
calc.Sheet.Set(1, 0, "Apples", "Apples");
calc.Sheet.Set(1, 1, "12.5", "12.5");
calc.Sheet.Set(2, 0, "Pears", "Pears");
calc.Sheet.Set(2, 1, "30", "30");
calc.Sheet.Set(3, 0, "Total", "Total");
calc.Sheet.Set(3, 1, "=SUM(B2:B3)", "42.5");
calc.Sheet.EnsureCapacity(40, 16);
calc.Sheet.Images.Add(new SheetImage("chart.png", png, 5, 4)
{
    OffsetX = 6,
    OffsetY = 8,
    Width = 160,
    Height = 120,
});

var calcPath = Path.Combine(outDir, "calc" + OricFileKind.Calc.For());
store.Save(calc, calcPath, OricFileKind.Calc);
Console.WriteLine($"wrote {calcPath}");

// ------------------------------------------------------------ Base
var baseFile = OricFile.CreateNew(OricFileKind.Base, "Oric Base Sample");
baseFile.CreatedUtc = stamp;
baseFile.Table.Name = "People";
baseFile.Table.Fields.Add(new FieldDef("Name", FieldType.Text));
baseFile.Table.Fields.Add(new FieldDef("Joined", FieldType.Date));
baseFile.Table.Fields.Add(new FieldDef("Sales", FieldType.Number));

string[] names = { "Geoff Phillips", "Chris Lepine", "Anne Brennan", "Terry 'Tansoft'" };
string[] dates = { "1983-05-01", "1983-09-14", "1984-01-20", "1984-11-02" };
string[] sales = { "1200", "980.5", "1450.25", "760" };
for (int i = 0; i < names.Length; i++)
{
    var record = new RecordRow { Id = i + 1, Ordinal = i };
    record.Set("Name", names[i]);
    record.Set("Joined", dates[i]);
    record.Set("Sales", sales[i]);
    baseFile.Table.Records.Add(record);
}

var basePath = Path.Combine(outDir, "base" + OricFileKind.Base.For());
store.Save(baseFile, basePath, OricFileKind.Base);
Console.WriteLine($"wrote {basePath}");

// ------------------------------------------------------------ ODF exports (LibreOffice-readable)
var odtPath = Path.Combine(outDir, "exported-author.odt");
OdfExporter.ExportOdt(author, odtPath);
Console.WriteLine($"wrote {odtPath}");

var odsPath = Path.Combine(outDir, "exported-calc.ods");
OdfExporter.ExportOds(calc, odsPath);
Console.WriteLine($"wrote {odsPath}");

// ------------------------------------------------------------ tape catalogue
// The real 1984 tapes shipped with the Oricutron emulator: catalogue every
// file they hold into a CSV that Calc and Base can import as sample data.
var tapeDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "Downloads", "Oricutron_win32_v12", "tapes");
var tapes = OricOffice.Core.Tape.TapeCatalog.ReadDirectory(tapeDir);
var catalogPath = Path.Combine(outDir, "tape-catalog.csv");
// Keep the committed catalogue stable: only regenerate rows for the tapes it
// already documents (the emulator folder may collect more .tap files over
// time — our own CSAVE experiments, for instance).
if (File.Exists(catalogPath))
{
    var known = OricOffice.Core.IO.Csv.Parse(File.ReadAllText(catalogPath)).Skip(1)
        .Select(r => r[0])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    tapes = tapes
        .Where(t => known.Contains(t.TapeName, StringComparer.OrdinalIgnoreCase))
        .ToList();
}
if (tapes.Count == 0)
{
    Console.WriteLine($"skip tape-catalog.csv: no tapes at {tapeDir}");
}
else
{
    File.WriteAllText(catalogPath, OricOffice.Core.Tape.TapeCatalog.BuildCsv(tapes),
        new System.Text.UTF8Encoding(false));
    Console.WriteLine($"wrote {catalogPath} ({tapes.Count} tapes)");
}
