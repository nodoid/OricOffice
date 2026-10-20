namespace OricOffice.Core.Model;

/// <summary>Base section: an ordered field list plus ordered records.</summary>
public sealed class TableDocument
{
    public string Name { get; set; } = "Table1";

    public List<FieldDef> Fields { get; } = new();

    public List<RecordRow> Records { get; } = new();

    public bool IsEmpty => Fields.Count == 0;

    public FieldDef? FindField(string name)
        => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a field with a unique name (Field1, Field2, ... or uniquified given name).</summary>
    public FieldDef AddField(string? name = null, FieldType type = FieldType.Text)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "Field" : name.Trim();
        var unique = baseName;
        int n = 1;
        while (FindField(unique) is not null)
        {
            n++;
            unique = $"{baseName}{n}";
        }
        var field = new FieldDef(unique, type);
        Fields.Add(field);
        return field;
    }

    /// <summary>Removes a field and its values from every record. False if not found.</summary>
    public bool RemoveField(string name)
    {
        var field = FindField(name);
        if (field is null) return false;
        Fields.Remove(field);
        foreach (var record in Records) record.Values.Remove(field.Name);
        return true;
    }

    public RecordRow AddRecord()
    {
        var id = Records.Count == 0 ? 1 : Records.Max(r => r.Id) + 1;
        var record = new RecordRow { Id = id, Ordinal = Records.Count + 1 };
        foreach (var field in Fields) record.Values[field.Name] = string.Empty;
        Records.Add(record);
        return record;
    }

    public bool RemoveRecord(long id)
    {
        var record = Records.FirstOrDefault(r => r.Id == id);
        if (record is null) return false;
        Records.Remove(record);
        Renumber();
        return true;
    }

    public void Renumber()
    {
        for (int i = 0; i < Records.Count; i++) Records[i].Ordinal = i + 1;
    }

    /// <summary>Guarantees at least one field so a table is always editable.</summary>
    public void EnsureUsable()
    {
        if (Fields.Count == 0) AddField("Field1");
        EnsureRecordKeys();
    }

    /// <summary>Every record gets an (empty) value for every field, so lookups never miss.</summary>
    public void EnsureRecordKeys()
    {
        foreach (var record in Records)
        {
            foreach (var field in Fields)
                if (!record.Values.ContainsKey(field.Name))
                    record.Values[field.Name] = string.Empty;
        }
    }
}
