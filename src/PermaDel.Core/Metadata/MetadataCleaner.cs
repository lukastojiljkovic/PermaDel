namespace PermaDel.Core.Metadata;

/// <summary>
/// Finds the metadata in a file and writes a cleaned copy without decoding or re-encoding anything: JPEG, PNG,
/// WebP and Office Open XML files are rewritten byte for byte apart from the parts that hold metadata.
/// </summary>
public static class MetadataCleaner
{
    private static readonly string[] OfficeExtensions =
    [
        ".docx", ".docm", ".dotx", ".dotm",
        ".xlsx", ".xlsm", ".xltx", ".xltm",
        ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm",
    ];

    /// <summary>Names what the file at <paramref name="path"/> holds.</summary>
    public static MetadataInspection Inspect(string path)
    {
        using var input = OpenRead(path);
        return Inspect(input, Path.GetFileName(path));
    }

    /// <summary>Names what the stream holds. The stream must be seekable and is read from its start.</summary>
    public static MetadataInspection Inspect(Stream input, string? fileName)
    {
        var found = new FoundMetadata();
        var format = Detect(input, fileName);
        input.Seek(0, SeekOrigin.Begin);
        switch (format)
        {
            case MetadataFormat.Jpeg:
                JpegMetadataCleaner.Inspect(input, found);
                return new MetadataInspection(MetadataFormat.Jpeg, found.ToList(), found.NeedsRotation);
            case MetadataFormat.Png:
                PngMetadataCleaner.Inspect(input, found);
                return new MetadataInspection(MetadataFormat.Png, found.ToList(), found.NeedsRotation);
            case MetadataFormat.WebP:
                WebpMetadataCleaner.Inspect(input, found);
                return new MetadataInspection(MetadataFormat.WebP, found.ToList(), found.NeedsRotation);
            case MetadataFormat.OfficeOpenXml:
                return OoxmlMetadataCleaner.Inspect(input, found)
                    ? new MetadataInspection(MetadataFormat.OfficeOpenXml, found.ToList(), MayShowSideways: false)
                    : MetadataInspection.Unsupported;
            default:
                return MetadataInspection.Unsupported;
        }
    }

    /// <summary>Tells the kind of file from its contents, and from the name for Office documents, which are zips.</summary>
    public static MetadataFormat Detect(Stream input, string? fileName)
    {
        input.Seek(0, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[12];
        var read = ReadUpTo(input, header);
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return MetadataFormat.Jpeg;
        if (read >= PngMetadataCleaner.Signature.Length && header[..PngMetadataCleaner.Signature.Length].SequenceEqual(PngMetadataCleaner.Signature))
            return MetadataFormat.Png;
        if (read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return MetadataFormat.WebP;
        if (read >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K' && header[2] is 3 or 5 or 7 && IsOfficeDocument(fileName))
            return MetadataFormat.OfficeOpenXml;
        return MetadataFormat.Unsupported;
    }

    /// <summary>Writes <paramref name="input"/> to <paramref name="output"/> without its metadata.</summary>
    public static void Clean(Stream input, Stream output, MetadataFormat format)
    {
        switch (format)
        {
            case MetadataFormat.Jpeg:
                JpegMetadataCleaner.Clean(input, output);
                break;
            case MetadataFormat.Png:
                PngMetadataCleaner.Clean(input, output);
                break;
            case MetadataFormat.WebP:
                WebpMetadataCleaner.Clean(input, output);
                break;
            case MetadataFormat.OfficeOpenXml:
                OoxmlMetadataCleaner.Clean(input, output);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "PermaDel cannot clean this kind of file.");
        }
    }

    /// <summary>Reads a freshly written file again to make sure it is complete and sound.</summary>
    public static bool Validate(string path, MetadataFormat format)
    {
        try
        {
            using var input = OpenRead(path);
            switch (format)
            {
                case MetadataFormat.Jpeg:
                    JpegMetadataCleaner.Clean(input, Stream.Null);
                    return true;
                case MetadataFormat.Png:
                    PngMetadataCleaner.Clean(input, Stream.Null);
                    return true;
                case MetadataFormat.WebP:
                    WebpMetadataCleaner.Clean(input, Stream.Null);
                    return true;
                case MetadataFormat.OfficeOpenXml:
                    return OoxmlMetadataCleaner.Validates(input);
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsOfficeDocument(string? fileName) =>
        fileName is not null && OfficeExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    private static FileStream OpenRead(string path) =>
        new(LongPath.ToVerbatim(path), FileMode.Open, FileAccess.Read, FileShare.Read);

    private static int ReadUpTo(Stream input, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = input.Read(buffer[offset..]);
            if (read <= 0)
                break;
            offset += read;
        }
        return offset;
    }
}
