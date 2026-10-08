namespace PermaDel.Core.Metadata;

/// <summary>The file kinds PermaDel can remove metadata from, or <see cref="Unsupported"/> when it cannot.</summary>
public enum MetadataFormat
{
    Unsupported,
    Jpeg,
    Png,
    WebP,
    OfficeOpenXml,
}

/// <summary>
/// A kind of metadata found in a file. Images use the first eight and Office files the last five; the
/// dialog turns each one into the words it shows.
/// </summary>
public enum MetadataCategory
{
    CameraDetails,
    Location,
    DateTaken,
    EditingSoftware,
    AuthorAndComments,
    Copyright,
    Thumbnail,
    OtherText,
    Author,
    LastSavedBy,
    Company,
    Manager,
    CustomProperties,
}

/// <summary>What an inspection found: the format, the categories present, and whether photos may now look sideways.</summary>
public sealed record MetadataInspection(MetadataFormat Format, IReadOnlyList<MetadataCategory> Categories, bool MayShowSideways)
{
    /// <summary>For a file PermaDel cannot clean, because of its type or because it could not be read as its type.</summary>
    public static MetadataInspection Unsupported { get; } = new(MetadataFormat.Unsupported, [], false);

    public bool CanClean => Format != MetadataFormat.Unsupported;

    public bool HasMetadata => Categories.Count > 0;
}

/// <summary>A file that could not be cleaned, with the reason in the plain words the result shows.</summary>
public sealed record MetadataRemovalFailure(string Path, string Reason);

/// <summary>The outcome of a metadata removal: how many files were cleaned, where the copies are, and what failed.</summary>
public sealed record MetadataRemovalResult(int FilesRemoved, IReadOnlyList<string> Copies, IReadOnlyList<MetadataRemovalFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>
/// Collects what the format readers found. Categories are kept in the order the dialog lists them and only once.
/// </summary>
internal sealed class FoundMetadata
{
    private readonly HashSet<MetadataCategory> _categories = [];

    /// <summary>Set when the file's EXIF orientation tag asks apps to rotate the pixels.</summary>
    public bool NeedsRotation { get; set; }

    public int Count => _categories.Count;

    public void Add(MetadataCategory category) => _categories.Add(category);

    public IReadOnlyList<MetadataCategory> ToList() => _categories.Order().ToList();
}
