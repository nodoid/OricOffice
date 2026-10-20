namespace OricOffice.Core.Model;

/// <summary>A Base record. Values are keyed by field name (case-insensitive).</summary>
public sealed class RecordRow
{
    public RecordRow()
    {
        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Stable identifier, persisted in the file.</summary>
    public long Id { get; set; }

    /// <summary>Display order.</summary>
    public int Ordinal { get; set; }

    public Dictionary<string, string> Values { get; }

    public string Get(string field)
        => Values.TryGetValue(field, out var v) ? v : string.Empty;

    public void Set(string field, string value)
        => Values[field] = value ?? string.Empty;

    public RecordRow Clone()
    {
        var copy = new RecordRow { Id = Id, Ordinal = Ordinal };
        foreach (var (k, v) in Values) copy.Values[k] = v;
        return copy;
    }
}
