namespace OricOffice.Core.Model;

/// <summary>
/// An image embedded in an Author document. PNG and JPEG only — the two formats
/// every exporter (PDF, ODF) and every MAUI platform handles natively.
/// </summary>
public sealed class DocumentImage
{
    public DocumentImage()
    {
    }

    public DocumentImage(string name, byte[] data)
    {
        Name = name;
        Data = data;
    }

    /// <summary>Original file name (extension included) — used for ODF package entries.</summary>
    public string Name { get; set; } = "image.png";

    public byte[] Data { get; set; } = Array.Empty<byte>();

    public bool IsEmpty => Data is null || Data.Length == 0;

    public ImageFormat Format => ImageDetector.Detect(Data);

    /// <summary>Extension (".png"/".jpg") derived from the actual bytes, falling back to the name.</summary>
    public string Extension => Format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Png => ".png",
        _ => Path.GetExtension(Name).ToLowerInvariant() is { Length: > 0 } ext ? ext : ".png",
    };
}

public enum ImageFormat
{
    Unknown = 0,
    Png = 1,
    Jpeg = 2,
}

public static class ImageDetector
{
    public static ImageFormat Detect(byte[]? data)
    {
        if (data is null || data.Length < 8) return ImageFormat.Unknown;
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return ImageFormat.Png;
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ImageFormat.Jpeg;
        return ImageFormat.Unknown;
    }

    public static bool IsSupported(byte[]? data) => Detect(data) is ImageFormat.Png or ImageFormat.Jpeg;
}
