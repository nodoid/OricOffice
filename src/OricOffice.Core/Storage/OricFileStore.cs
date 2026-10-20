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

    private static void Execute(SqliteConnection connection, string sql, params (string Name, string Value)[] args)
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
