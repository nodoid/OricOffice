namespace OricOffice.Core.Storage;

/// <summary>Raised for any user-facing file problem (not found, not a suite file, corrupt file).</summary>
public sealed class OricFileException : Exception
{
    public OricFileException(string message) : base(message)
    {
    }

    public OricFileException(string message, Exception inner) : base(message, inner)
    {
    }
}
