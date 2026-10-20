namespace OricOffice.Core.Model;

public enum FieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
}

/// <summary>A Base field (column) definition.</summary>
public sealed class FieldDef
{
    public FieldDef()
    {
    }

    public FieldDef(string name, FieldType type = FieldType.Text)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; set; } = "Field";

    public FieldType Type { get; set; } = FieldType.Text;

    public string TypeLabel => Type switch
    {
        FieldType.Number => "Number",
        FieldType.Date => "Date",
        _ => "Text",
    };
}
