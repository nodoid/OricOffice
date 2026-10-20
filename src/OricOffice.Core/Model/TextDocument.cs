namespace OricOffice.Core.Model;

/// <summary>Author section: plain text with word wrap handled by the view.</summary>
public sealed class TextDocument
{
    public string Body { get; set; } = string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Body);
}
