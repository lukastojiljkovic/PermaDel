using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace PermaDel.Core.Metadata;

/// <summary>
/// Rewrites an Office Open XML package: the author names in <c>docProps/core.xml</c> and the company, manager
/// and custom properties are emptied or dropped. Document parts are copied exactly as they were.
/// </summary>
/// <remarks>
/// The .NET zip API cannot copy an entry's compressed bytes, so every kept entry is written again with the same
/// compression level and its content is unchanged. Entry timestamps and attributes are carried over.
/// </remarks>
internal static class OoxmlMetadataCleaner
{
    private const string CorePart = "docProps/core.xml";
    private const string AppPart = "docProps/app.xml";
    private const string CustomPart = "docProps/custom.xml";
    private const string ContentTypesPart = "[Content_Types].xml";
    private const string RelationshipsPart = "_rels/.rels";

    private const string CustomPropertiesRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string CustomPropertiesContentType = "application/vnd.openxmlformats-officedocument.custom-properties+xml";

    private static readonly XNamespace CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace ExtendedProperties = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Names what the package holds. Returns false for a file that is not a readable Office document.</summary>
    public static bool Inspect(Stream input, FoundMetadata found)
    {
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (FindEntry(archive, ContentTypesPart) is null)
                return false;

            if (ReadPart(archive, CorePart) is { } core)
            {
                if (IsFilled(Value(core, DublinCore + "creator")))
                    found.Add(MetadataCategory.Author);
                if (IsFilled(Value(core, CoreProperties + "lastModifiedBy")))
                    found.Add(MetadataCategory.LastSavedBy);
            }

            if (ReadPart(archive, AppPart) is { } app)
            {
                if (IsFilled(Value(app, ExtendedProperties + "Company")))
                    found.Add(MetadataCategory.Company);
                if (IsFilled(Value(app, ExtendedProperties + "Manager")))
                    found.Add(MetadataCategory.Manager);
            }

            if (FindEntry(archive, CustomPart) is not null)
                found.Add(MetadataCategory.CustomProperties);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException)
        {
            return false;
        }
    }

    public static void Clean(Stream input, Stream output)
    {
        using var source = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (FindEntry(source, ContentTypesPart) is null)
            throw new InvalidDataException("The file is not an Office Open XML document.");

        var dropCustomPart = FindEntry(source, CustomPart) is not null;
        using var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var entries = source.Entries
            .Where(entry => !IsPart(entry.FullName, CustomPart))
            .OrderByDescending(entry => IsPart(entry.FullName, ContentTypesPart))
            .ToList();

        foreach (var entry in entries)
        {
            var created = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            created.LastWriteTime = Clamp(entry.LastWriteTime);
            created.ExternalAttributes = entry.ExternalAttributes;
            using var sourceStream = entry.Open();
            using var targetStream = created.Open();
            CopyOrEdit(entry.FullName, dropCustomPart, sourceStream, targetStream);
        }
    }

    /// <summary>Opens the cleaned package and reads its content types part, which every Office document must have.</summary>
    public static bool Validates(Stream input)
    {
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (FindEntry(archive, ContentTypesPart) is not { } entry)
                return false;
            using var stream = entry.Open();
            XDocument.Load(stream);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException)
        {
            return false;
        }
    }

    private static void CopyOrEdit(string name, bool dropCustomPart, Stream input, Stream output)
    {
        Func<XDocument, bool>? edit = name switch
        {
            _ when IsPart(name, CorePart) => EmptyCoreNames,
            _ when IsPart(name, AppPart) => EmptyAppNames,
            _ when dropCustomPart && IsPart(name, ContentTypesPart) => RemoveCustomContentType,
            _ when dropCustomPart && IsPart(name, RelationshipsPart) => RemoveCustomRelationship,
            _ => null,
        };
        if (edit is null)
        {
            input.CopyTo(output);
            return;
        }

        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        buffer.Position = 0;
        XDocument document;
        try
        {
            document = XDocument.Load(buffer, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("An Office property part could not be read.", ex);
        }

        if (!edit(document))
        {
            buffer.Position = 0;
            buffer.CopyTo(output);
            return;
        }

        document.Save(output, SaveOptions.DisableFormatting);
    }

    private static bool EmptyCoreNames(XDocument document)
    {
        var changed = Clear(document, DublinCore + "creator");
        if (Clear(document, CoreProperties + "lastModifiedBy"))
            changed = true;
        return changed;
    }

    private static bool EmptyAppNames(XDocument document)
    {
        var changed = Clear(document, ExtendedProperties + "Company");
        if (Clear(document, ExtendedProperties + "Manager"))
            changed = true;
        return changed;
    }

    private static bool RemoveCustomContentType(XDocument document)
    {
        var removed = false;
        foreach (var element in document.Root?.Elements(ContentTypes + "Override").ToList() ?? [])
        {
            if (IsCustomProperties(element.Attribute("ContentType")?.Value)
                || IsCustomPartName(element.Attribute("PartName")?.Value))
            {
                element.Remove();
                removed = true;
            }
        }
        return removed;
    }

    private static bool RemoveCustomRelationship(XDocument document)
    {
        var removed = false;
        foreach (var element in document.Root?.Elements(Relationships + "Relationship").ToList() ?? [])
        {
            var type = element.Attribute("Type")?.Value;
            if (type is not null && type.EndsWith("/custom-properties", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
                removed = true;
            }
        }
        return removed;
    }

    private static bool Clear(XDocument document, XName name)
    {
        var element = document.Root?.Element(name);
        if (element is null || element.Value.Length == 0)
            return false;
        element.Value = string.Empty;
        return true;
    }

    private static XDocument? ReadPart(ZipArchive archive, string name)
    {
        if (FindEntry(archive, name) is not { } entry)
            return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string? Value(XDocument document, XName name) => document.Root?.Element(name)?.Value;

    private static bool IsFilled(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsCustomProperties(string? contentType) => contentType == CustomPropertiesContentType;

    private static bool IsCustomPartName(string? partName) => partName is not null && IsPart(partName, CustomPart);

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name) =>
        archive.Entries.FirstOrDefault(entry => IsPart(entry.FullName, name));

    /// <summary>Compares part names the way a package does: a leading slash and backslashes do not matter.</summary>
    private static bool IsPart(string name, string part) =>
        string.Equals(name.Replace('\\', '/').TrimStart('/'), part, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset Clamp(DateTimeOffset value)
    {
        var minimum = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var maximum = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
