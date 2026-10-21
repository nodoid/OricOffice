namespace OricOffice.Core.Model;

/// <summary>
/// An image floating over the Calc grid, anchored to a cell with a pixel
/// offset relative to that cell's top-left corner.
/// </summary>
public sealed class SheetImage
{
    public SheetImage()
    {
    }

    public SheetImage(string name, byte[] data, int anchorRow, int anchorColumn)
    {
        Name = name;
        Data = data;
        AnchorRow = anchorRow;
        AnchorColumn = anchorColumn;
    }

    public string Name { get; set; } = "image.png";

    public byte[] Data { get; set; } = Array.Empty<byte>();

    public int AnchorRow { get; set; }

    public int AnchorColumn { get; set; }

    /// <summary>Pixel offset from the anchor cell's top-left (may be negative or spill across cells).</summary>
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    public int Width { get; set; } = 160;

    public int Height { get; set; } = 120;

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
