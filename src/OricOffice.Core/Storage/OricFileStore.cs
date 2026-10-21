using System.Text.Json;
using Microsoft.Data.Sqlite;
using OricOffice.Core.Model;

namespace OricOffice.Core.Storage;

/// <summary>
/// Reads and writes Oric Suite documents.
///
/// Every file — regardless of extension (.oau/.oca/.oba) — is a SQLite database with the
/// same schema and all three sections (text, sheet, table). That is what lets Author, Calc
/// and Base load each other's files with zero data loss: each app edits its own section and
/// faithfully rewrites the others.
/// </summary>
public sealed class OricFileStore
{
    private const string SqliteHeader = "SQLite format 3\0";

    /// <summary>Root for drafts and the recents list (private app data; overridable for tests).</summary>
    private readonly string _dataRoot;

    public OricFileStore(string? dataRoot = null)
        => _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OricOffice");

    // ---------------------------------------------------------------- drafts

    /// <summary>
    /// Single-slot draft path for a module: the working copy kept when the user
    /// leaves with unsaved changes, restored on the next start.
    /// </summary>
    public string DraftPath(OricFileKind kind)
        => Path.Combine(_dataRoot, "drafts", kind.For().TrimStart('.'), "draft" + kind.For());

    public bool DraftExists(OricFileKind kind) => File.Exists(DraftPath(kind));

    public void DeleteDraft(OricFileKind kind)
    {
        var path = DraftPath(kind);
        if (File.Exists(path)) File.Delete(path);
    }

    // ---------------------------------------------------------------- recents

    private string RecentsPath => Path.Combine(_dataRoot, "recents.json");

    /// <summary>Loads the recent-documents list; a missing or corrupt file yields an empty list.</summary>
    public List<RecentDocumentEntry> LoadRecents()
    {
        try
        {
            if (!File.Exists(RecentsPath)) return new List<RecentDocumentEntry>();
            var entries = JsonSerializer.Deserialize<List<RecentDocumentEntry>>(
                File.ReadAllText(RecentsPath));
            return entries is null ? new List<RecentDocumentEntry>() : entries;
        }
        catch
        {
            return new List<RecentDocumentEntry>();
        }
    }

    public void SaveRecents(IReadOnlyList<RecentDocumentEntry> entries)
    {
        var directory = Path.GetDirectoryName(RecentsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(RecentsPath, JsonSerializer.Serialize(entries,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private const string SchemaSql = """
        PRAGMA user_version = 1;

        CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS documents (
            id   INTEGER PRIMARY KEY CHECK (id = 1),
            body TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS sheets (
            id   INTEGER PRIMARY KEY CHECK (id = 1),
            name TEXT NOT NULL,
            rows INTEGER NOT NULL,
            cols INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS cells (
            sheet_id INTEGER NOT NULL,
            row      INTEGER NOT NULL,
            col      INTEGER NOT NULL,
            raw      TEXT    NOT NULL,
            value    TEXT    NOT NULL,
            PRIMARY KEY (sheet_id, row, col)
        );

        CREATE TABLE IF NOT EXISTS btables (
            id   INTEGER PRIMARY KEY CHECK (id = 1),
            name TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS fields (
            tbl_id INTEGER NOT NULL,
            ord    INTEGER NOT NULL,
            name   TEXT    NOT NULL,
            type   TEXT    NOT NULL,
            PRIMARY KEY (tbl_id, ord)
        );

        CREATE TABLE IF NOT EXISTS records (
            id      INTEGER PRIMARY KEY,
            tbl_id  INTEGER NOT NULL,
            ord     INTEGER NOT NULL,
            data    TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_records_tbl ON records (tbl_id, ord);

        CREATE TABLE IF NOT EXISTS doc_images (
            doc_id INTEGER NOT NULL,
            ord    INTEGER NOT NULL,
            name   TEXT    NOT NULL,
            data   BLOB    NOT NULL,
            PRIMARY KEY (doc_id, ord)
        );

        CREATE TABLE IF NOT EXISTS doc_tables (
            doc_id INTEGER NOT NULL,
            ord    INTEGER NOT NULL,
            name   TEXT    NOT NULL,
            rows   INTEGER NOT NULL,
            cols   INTEGER NOT NULL,
            data   TEXT    NOT NULL,
            PRIMARY KEY (doc_id, ord)
        );

        CREATE TABLE IF NOT EXISTS sheet_images (
            sheet_id   INTEGER NOT NULL,
            ord        INTEGER NOT NULL,
            name       TEXT    NOT NULL,
            anchor_row INTEGER NOT NULL,
            anchor_col INTEGER NOT NULL,
            off_x      INTEGER NOT NULL,
            off_y      INTEGER NOT NULL,
            width      INTEGER NOT NULL,
            height     INTEGER NOT NULL,
            data       BLOB    NOT NULL,
            PRIMARY KEY (sheet_id, ord)
        );

        CREATE TABLE IF NOT EXISTS doc_runs (
            doc_id INTEGER NOT NULL,
            ord    INTEGER NOT NULL,
            start  INTEGER NOT NULL,
            len    INTEGER NOT NULL,
            style  TEXT    NOT NULL,
            PRIMARY KEY (doc_id, ord)
        );
        """;

    public IReadOnlyList<string> SupportedExtensions => OricFileExtensions.All;

    public static string NormalizeSavePath(string path, OricFileKind kind)
    {
        var expected = kind.For();
        var current = Path.GetExtension(path);
        if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            path = Path.ChangeExtension(path, expected);
        return path;
    }

    public static string SuggestedFileName(string title, OricFileKind kind)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name + kind.For();
    }

    /// <summary>True when the file starts with the SQLite magic header.</summary>
    public bool IsSuiteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[16];
            var read = fs.Read(buffer, 0, 16);
            if (read < 16) return false;
            var header = System.Text.Encoding.ASCII.GetString(buffer);
            return header == SqliteHeader;
        }
        catch
        {
            return false;
        }
    }

    public OricFile CreateNew(OricFileKind kind, string? title = null)
        => OricFile.CreateNew(kind, title);

    /// <summary>Loads any of the three file types. The extension only informs the kind fallback.</summary>
    public OricFile Load(string path)
    {
        if (!File.Exists(path))
            throw new OricFileException($"The file \"{path}\" was not found.");

        if (!IsSuiteFile(path))
            throw new OricFileException(
                $"\"{Path.GetFileName(path)}\" is not an Oric Suite file. " +
                $"Supported extensions are {string.Join(", ", OricFileExtensions.All)}.");

        var file = new OricFile();
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            if (!TableExists(connection, "meta"))
                throw new OricFileException(
                    $"\"{Path.GetFileName(path)}\" is a SQLite file but not an Oric Suite document.");

            // --- meta ---
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM meta";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    meta[reader.GetString(0)] = reader.GetString(1);
            }

            file.FormatVersion = meta.TryGetValue("format", out var fmt) && int.TryParse(fmt, out var v)
                ? v
                : OricFile.CurrentFormatVersion;

            file.Kind = meta.TryGetValue("kind", out var kindText) && ParseKind(kindText) is { } kind
                ? kind
                : OricFileExtensions.KindFromExtension(Path.GetExtension(path)) ?? OricFileKind.Author;

            file.Title = meta.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)
                ? title
                : Path.GetFileNameWithoutExtension(path);

            if (meta.TryGetValue("created_utc", out var created) && DateTime.TryParse(created,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var createdUtc))
                file.CreatedUtc = createdUtc;

            // --- Author section ---
            if (TableExists(connection, "documents"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT body FROM documents WHERE id = 1";
                var body = cmd.ExecuteScalar();
                file.Text.Body = body is string s ? s : string.Empty;
            }

            // Style runs come after the body: the Body setter repairs offsets.
            if (TableExists(connection, "doc_runs"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT start, len, style FROM doc_runs WHERE doc_id = 1 ORDER BY ord";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        TextStyle? style;
                        try
                        {
                            style = JsonSerializer.Deserialize<TextStyle>(reader.GetString(2));
                        }
                        catch (JsonException)
                        {
                            continue; // unreadable style never blocks the document
                        }
                        var run = new TextRun(reader.GetInt32(0), reader.GetInt32(1), style ?? new TextStyle());
                        if (run.Start >= 0 && run.Length > 0 && run.End <= file.Text.Body.Length)
                            file.Text.Runs.Add(run);
                    }
                }
                file.Text.Normalize();
            }

            if (TableExists(connection, "doc_images"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name, data FROM doc_images WHERE doc_id = 1 ORDER BY ord";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    file.Text.Images.Add(new DocumentImage(reader.GetString(0), (byte[])reader[1]));
            }

            if (TableExists(connection, "doc_tables"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name, data FROM doc_tables WHERE doc_id = 1 ORDER BY ord";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var block = new TableBlock();
                    var payload = JsonSerializer.Deserialize<TablePayload>(reader.GetString(1));
                    if (payload?.Text is not null)
                        block.Restore(payload.Text, payload.Fill, reader.GetString(0));
                    else
                        block.Name = reader.GetString(0);

                    if (payload?.Borders is not null)
                    {
                        var borders = new Dictionary<(int Row, int Column, BorderSide Side), CellBorder>();
                        foreach (var b in payload.Borders)
                            borders[(b.Row, b.Column, (BorderSide)b.Side)] =
                                new CellBorder((BorderStyle)b.Style, b.Colour);
                        block.RestoreBorders(borders);
                    }

                    file.Text.Tables.Add(block);
                }
            }

            // --- Calc section ---
            if (TableExists(connection, "sheets"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name, rows, cols FROM sheets WHERE id = 1";
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        file.Sheet.Name = reader.GetString(0);
                        file.Sheet.RowCount = Math.Max(reader.GetInt32(1), SheetDocument.MinRows);
                        file.Sheet.ColumnCount = Math.Max(reader.GetInt32(2), SheetDocument.MinColumns);
                    }
                }
            }

            if (TableExists(connection, "cells"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT row, col, raw, value FROM cells WHERE sheet_id = 1";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cell = new Cell(reader.GetInt32(0), reader.GetInt32(1),
                        reader.GetString(2), reader.GetString(3));
                    file.Sheet.AddOrUpdate(cell);
                }
            }

            file.Sheet.EnsureCapacityForCells();

            if (TableExists(connection, "sheet_images"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name, anchor_row, anchor_col, off_x, off_y, width, height, data " +
                                  "FROM sheet_images WHERE sheet_id = 1 ORDER BY ord";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var image = new SheetImage
                    {
                        Name = reader.GetString(0),
                        AnchorRow = reader.GetInt32(1),
                        AnchorColumn = reader.GetInt32(2),
                        OffsetX = reader.GetInt32(3),
                        OffsetY = reader.GetInt32(4),
                        Width = reader.GetInt32(5),
                        Height = reader.GetInt32(6),
                        Data = (byte[])reader[7],
                    };
                    file.Sheet.Images.Add(image);
                }
            }

            // --- Base section ---
            if (TableExists(connection, "btables"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name FROM btables WHERE id = 1";
                var name = cmd.ExecuteScalar();
                if (name is string n) file.Table.Name = n;
            }

            if (TableExists(connection, "fields"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name, type FROM fields WHERE tbl_id = 1 ORDER BY ord";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    file.Table.Fields.Add(new FieldDef(
                        reader.GetString(0),
                        ParseFieldType(reader.GetString(1))));
                }
            }

            if (TableExists(connection, "records"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT id, ord, data FROM records WHERE tbl_id = 1 ORDER BY ord";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var record = new RecordRow
                    {
                        Id = reader.GetInt64(0),
                        Ordinal = reader.GetInt32(1),
                    };
                    var json = reader.GetString(2);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (values is not null)
                            foreach (var (key, val) in values)
                                record.Values[key] = val ?? string.Empty;
                    }
                    file.Table.Records.Add(record);
                }
            }

            file.Table.EnsureRecordKeys();
            return file;
        }
        catch (OricFileException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            throw new OricFileException(
                $"\"{Path.GetFileName(path)}\" could not be read as an Oric Suite document. " +
                "The file may be damaged.", ex);
        }
        catch (Exception ex)
        {
            throw new OricFileException(
                $"\"{Path.GetFileName(path)}\" could not be opened. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves all three sections to <paramref name="path"/>. The extension is forced to match
    /// <paramref name="saveKind"/>; returns the final path.
    /// </summary>
    public string Save(OricFile file, string path, OricFileKind saveKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        path = NormalizeSavePath(path, saveKind);
        file.Kind = saveKind;
        file.FormatVersion = OricFile.CurrentFormatVersion;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Rewrite from scratch: guarantees a clean, consistent database.
        if (File.Exists(path)) File.Delete(path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = SchemaSql;
            schema.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();

        // --- meta ---
        Execute(connection, "INSERT INTO meta (key, value) VALUES ($k, $v)",
            ("$k", "format"), ("$v", OricFile.CurrentFormatVersion.ToString()));
        Execute(connection, "INSERT INTO meta (key, value) VALUES ($k, $v)",
            ("$k", "kind"), ("$v", saveKind.ToString()));
        Execute(connection, "INSERT INTO meta (key, value) VALUES ($k, $v)",
            ("$k", "title"), ("$v", file.Title ?? "Untitled"));
        Execute(connection, "INSERT INTO meta (key, value) VALUES ($k, $v)",
            ("$k", "created_utc"),
            ("$v", file.CreatedUtc.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture)));
        Execute(connection, "INSERT INTO meta (key, value) VALUES ($k, $v)",
            ("$k", "app"), ("$v", saveKind.DisplayName()));

        // --- Author ---
        Execute(connection, "INSERT INTO documents (id, body) VALUES (1, $b)",
            ("$b", file.Text.Body ?? string.Empty));

        Execute(connection, "DELETE FROM doc_runs WHERE doc_id = 1");
        for (int i = 0; i < file.Text.Runs.Count; i++)
        {
            var run = file.Text.Runs[i];
            if (run.Length <= 0) continue;
            Execute(connection,
                "INSERT INTO doc_runs (doc_id, ord, start, len, style) VALUES (1, $o, $s, $l, $t)",
                ("$o", i.ToString()), ("$s", run.Start.ToString()),
                ("$l", run.Length.ToString()), ("$t", JsonSerializer.Serialize(run.Style)));
        }

        for (int i = 0; i < file.Text.Images.Count; i++)
        {
            var image = file.Text.Images[i];
            if (image.IsEmpty) continue;
            Execute(connection,
                "INSERT INTO doc_images (doc_id, ord, name, data) VALUES (1, $o, $n, $d)",
                ("$o", i.ToString()), ("$n", image.Name), ("$d", image.Data));
        }

        for (int i = 0; i < file.Text.Tables.Count; i++)
        {
            var table = file.Text.Tables[i];
            var payload = new TablePayload
            {
                Text = table.Snapshot().Text,
                Fill = table.Snapshot().Fill,
                Borders = table.Borders
                    .Select(pair => new BorderPayload
                    {
                        Row = pair.Key.Row,
                        Column = pair.Key.Column,
                        Side = (int)pair.Key.Side,
                        Style = (int)pair.Value.Style,
                        Colour = pair.Value.Colour,
                    })
                    .ToList(),
            };
            Execute(connection,
                "INSERT INTO doc_tables (doc_id, ord, name, rows, cols, data) VALUES (1, $o, $n, $r, $c, $d)",
                ("$o", i.ToString()), ("$n", table.Name),
                ("$r", table.Rows.ToString()), ("$c", table.Columns.ToString()),
                ("$d", JsonSerializer.Serialize(payload)));
        }

        // --- Calc ---
        Execute(connection,
            "INSERT INTO sheets (id, name, rows, cols) VALUES (1, $n, $r, $c)",
            ("$n", file.Sheet.Name), ("$r", file.Sheet.RowCount.ToString()),
            ("$c", file.Sheet.ColumnCount.ToString()));

        foreach (var cell in file.Sheet.Cells.Values)
        {
            Execute(connection,
                "INSERT INTO cells (sheet_id, row, col, raw, value) VALUES (1, $r, $c, $raw, $val)",
                ("$r", cell.Row.ToString()), ("$c", cell.Column.ToString()),
                ("$raw", cell.Raw ?? string.Empty), ("$val", cell.Value ?? string.Empty));
        }

        for (int i = 0; i < file.Sheet.Images.Count; i++)
        {
            var image = file.Sheet.Images[i];
            if (image.IsEmpty) continue;
            Execute(connection,
                "INSERT INTO sheet_images " +
                "(sheet_id, ord, name, anchor_row, anchor_col, off_x, off_y, width, height, data) " +
                "VALUES (1, $o, $n, $ar, $ac, $ox, $oy, $w, $h, $d)",
                ("$o", i.ToString()), ("$n", image.Name),
                ("$ar", image.AnchorRow.ToString()), ("$ac", image.AnchorColumn.ToString()),
                ("$ox", image.OffsetX.ToString()), ("$oy", image.OffsetY.ToString()),
                ("$w", image.Width.ToString()), ("$h", image.Height.ToString()),
                ("$d", image.Data));
        }

        // --- Base ---
        Execute(connection, "INSERT INTO btables (id, name) VALUES (1, $n)",
            ("$n", file.Table.Name ?? "Table1"));

        for (int i = 0; i < file.Table.Fields.Count; i++)
        {
            var field = file.Table.Fields[i];
            Execute(connection,
                "INSERT INTO fields (tbl_id, ord, name, type) VALUES (1, $o, $n, $t)",
                ("$o", i.ToString()), ("$n", field.Name), ("$t", field.Type.ToString()));
        }

        for (int i = 0; i < file.Table.Records.Count; i++)
        {
            var record = file.Table.Records[i];
            var json = JsonSerializer.Serialize(record.Values);
            Execute(connection,
                "INSERT INTO records (id, tbl_id, ord, data) VALUES ($id, 1, $o, $d)",
                ("$id", record.Id.ToString()), ("$o", i.ToString()), ("$d", json));
        }

        transaction.Commit();
        return path;
    }

    // ---------------------------------------------------------------- helpers

    private sealed class TablePayload
    {
        public string[][] Text { get; set; } = Array.Empty<string[]>();
        public string?[][]? Fill { get; set; }
        public List<BorderPayload>? Borders { get; set; }
    }

    private sealed class BorderPayload
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int Side { get; set; }
        public int Style { get; set; }
        public string? Colour { get; set; }
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n LIMIT 1";
        cmd.Parameters.AddWithValue("$n", tableName);
        return cmd.ExecuteScalar() is not null;
    }

    private static OricFileKind? ParseKind(string text)
        => Enum.TryParse<OricFileKind>(text, ignoreCase: true, out var kind) ? kind : null;

    private static FieldType ParseFieldType(string text)
        => Enum.TryParse<FieldType>(text, ignoreCase: true, out var type) ? type : FieldType.Text;
}

/// <summary>One entry in the start view's recent-documents list.</summary>
public sealed class RecentDocumentEntry
{
    public string Path { get; set; } = string.Empty;

    public string Title { get; set; } = "Untitled";

    public OricFileKind Kind { get; set; }

    public DateTime LastOpenedUtc { get; set; }
}
