using System.IO.Compression;
using System.Text;

namespace PermaDel.Core.Tests.Metadata;

/// <summary>
/// Builds the small files the metadata tests use. Everything is written by code so no binary fixtures are
/// checked in, and the CRC and size fields a real reader checks are computed rather than faked.
/// </summary>
internal static class MetadataFixtures
{
    public const string DocumentPart = "word/document.xml";

    public const string PicturePart = "word/media/image1.jpeg";

    /// <summary>A JPEG with APP0, EXIF (GPS, orientation and a thumbnail), XMP, IPTC, a comment, ICC, an unknown APP5, Adobe, image data and EOI.</summary>
    public static (byte[] Original, byte[] Cleaned) Jpeg(ushort orientation = 1)
    {
        var soi = new byte[] { 0xFF, 0xD8 };
        var app0 = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        var exif = Segment(0xE1, [.. "Exif\0\0"u8, .. Tiff(orientation)]);
        var xmp = Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/"u8, .. "<x:xmpmeta/>"u8]);
        var iptc = Segment(0xED, Iptc());
        var comment = Segment(0xFE, [.. "A comment"u8]);
        var icc = Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. new byte[8]]);
        var unknown = Segment(0xE5, [.. "unknown"u8]);
        var adobe = Segment(0xEE, [.. "Adobe"u8, .. new byte[7]]);
        var quantization = Segment(0xDB, new byte[65]);
        var frame = Segment(0xC0, [8, 0, 8, 0, 8, 1, 1, 0x11, 0]);
        var huffman = Segment(0xC4, new byte[29]);
        var scan = Segment(0xDA, [1, 1, 0, 0, 63, 0]);
        var entropy = new byte[] { 0x12, 0x34, 0xFF, 0x00, 0x56, 0xAB, 0xFF, 0xD0, 0x9A };
        var eoi = new byte[] { 0xFF, 0xD9 };
        var imageData = Join(scan, entropy, eoi);

        return (
            Join(soi, app0, exif, xmp, iptc, comment, icc, unknown, adobe, quantization, frame, huffman, imageData),
            Join(soi, app0, icc, adobe, quantization, frame, huffman, imageData));
    }

    /// <summary>A JPEG with no metadata at all: APP0, ICC, Adobe, image data and EOI.</summary>
    public static byte[] CleanJpeg() => Jpeg().Cleaned;

    /// <summary>The clean JPEG with <paramref name="segments"/> right after SOI and <paramref name="trailer"/> after EOI.</summary>
    public static byte[] JpegWith(byte[][] segments, byte[]? trailer = null)
    {
        var clean = CleanJpeg();
        return Join([clean[..2], .. segments, clean[2..], trailer ?? []]);
    }

    /// <summary>A PNG with IHDR, text, EXIF and time chunks, image data and IEND.</summary>
    public static (byte[] Original, byte[] Cleaned) Png()
    {
        var text = Chunk("tEXt", [.. "Author\0"u8, .. "Jane"u8]);
        var compressedText = Chunk("zTXt", [.. "Copyright\0"u8, 0, .. new byte[] { 0x78, 0x9C }]);
        var internationalText = Chunk("iTXt", [.. "Comment\0"u8, 0, 0, 0, 0, .. "hi"u8]);
        var exif = Chunk("eXIf", Tiff(orientation: 1));
        var time = Chunk("tIME", [0x07, 0xE8, 1, 2, 3, 4, 5]);

        return (PngWith(text, compressedText, internationalText, exif, time), PngWith());
    }

    /// <summary>A one-pixel PNG with <paramref name="chunks"/> between its header and its image data.</summary>
    public static byte[] PngWith(params byte[][] chunks) =>
        Join([PngSignature(), Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]), .. chunks, Chunk("IDAT", [1, 2, 3, 4, 5]), Chunk("IEND", [])]);

    /// <summary>A WebP with a feature chunk, EXIF, XMP and image data.</summary>
    public static (byte[] Original, byte[] Cleaned) Webp()
    {
        var exif = WebpChunk("EXIF", Tiff(orientation: 1));
        var xmp = WebpChunk("XMP ", [.. "<x:xmpmeta/>"u8]);

        return (WebpWith(0x1C, exif, xmp), WebpWith(0x10));
    }

    /// <summary>A WebP whose feature chunk carries <paramref name="flags"/>, then <paramref name="chunks"/> and the image data.</summary>
    public static byte[] WebpWith(byte flags, params byte[][] chunks) =>
        Riff(Join([WebpChunk("VP8X", [flags, 0, 0, 0, 0x17, 0, 0, 0x0D, 0, 0]), .. chunks, WebpChunk("VP8 ", [1, 2, 3, 4, 5])]));

    /// <summary>
    /// An Office Open XML package with core, extended, custom and document parts, or without the personal ones,
    /// and <paramref name="picture"/> at <see cref="PicturePart"/> when one is given.
    /// </summary>
    public static byte[] Docx(bool withMetadata, byte[]? picture = null)
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", ContentTypes(withMetadata));
            Add(archive, "_rels/.rels", Relationships(withMetadata));
            Add(archive, DocumentPart, Document());
            Add(archive, "docProps/core.xml", Core(withMetadata));
            Add(archive, "docProps/app.xml", App(withMetadata));
            if (withMetadata)
                Add(archive, "docProps/custom.xml", Custom());
            if (picture is not null)
                Add(archive, PicturePart, picture);
        }
        return package.ToArray();
    }

    /// <summary>The decompressed bytes of one part of an Office package, or null when the package has no such part.</summary>
    public static byte[]? ReadPart(byte[] package, string name)
    {
        using var stream = new MemoryStream(package);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;
        using var content = entry.Open();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static string ReadPartText(byte[] package, string name) => Encoding.UTF8.GetString(ReadPart(package, name)!);

    /// <summary>A TIFF/EXIF block with camera, GPS, date, author, copyright, software and thumbnail data.</summary>
    public static byte[] Tiff(ushort orientation = 1)
    {
        var main = new List<TiffEntry>
        {
            Ascii(0x010E, "A description"),
            Ascii(0x010F, "PermaDel"),
            Ascii(0x0110, "Test Camera"),
            Short(0x0112, orientation),
            Ascii(0x0131, "Test Editor"),
            Ascii(0x0132, "2026:01:02 03:04:05"),
            Ascii(0x013B, "Jane Doe"),
            Ascii(0x8298, "(c) PermaDel"),
        };
        var exif = new List<TiffEntry>
        {
            Rational(0x829A, 1, 125),
            Short(0x8827, 400),
            Ascii(0x9003, "2026:01:02 03:04:05"),
            Short(0x9209, 0),
            Rational(0x920A, 50, 1),
            Ascii(0x9286, "a comment"),
        };

        var gps = new List<TiffEntry> { Ascii(0x0001, "N"), Rational(0x0002, 44, 1) };
        var thumbnail = new List<TiffEntry> { Long(0x0201, 0), Long(0x0202, 0) };
        return BuildTiff(main, exif, gps, thumbnail);
    }

    private static byte[] PngSignature() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A JPEG marker segment: the marker, its length and the payload.</summary>
    public static byte[] Segment(int marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, (byte)marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    /// <summary>A PNG chunk with its data and a CRC over the type and the data.</summary>
    public static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var crc = Crc32([.. typeBytes, .. data]);
        return [
            (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length,
            .. typeBytes, .. data,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
        ];
    }

    /// <summary>A WebP chunk: the four characters, the size, the data and a pad byte when the size is odd.</summary>
    public static byte[] WebpChunk(string type, byte[] data)
    {
        var padded = data.Length % 2 == 0 ? data : [.. data, 0];
        return [.. Encoding.ASCII.GetBytes(type), (byte)data.Length, (byte)(data.Length >> 8), (byte)(data.Length >> 16), (byte)(data.Length >> 24), .. padded];
    }

    private static byte[] Riff(byte[] body)
    {
        var size = body.Length + 4;
        return [.. "RIFF"u8, (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24), .. "WEBP"u8, .. body];
    }

    private static byte[] Iptc()
    {
        var datasets = Join(
            Dataset(2, 80, "Camera Fan"),
            Dataset(2, 90, "Belgrade"),
            Dataset(2, 116, "(c) PermaDel"));
        var size = datasets.Length;
        var resource = Join([0x04, 0x04], [0x00, 0x00], [(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size], datasets);
        return Join("Photoshop 3.0\0"u8.ToArray(), "8BIM"u8.ToArray(), resource);
    }

    private static byte[] Dataset(byte record, byte dataset, string value)
    {
        var text = Encoding.ASCII.GetBytes(value);
        return [0x1C, record, dataset, (byte)(text.Length >> 8), (byte)text.Length, .. text];
    }

    private static byte[] Join(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static void Add(ZipArchive archive, string name, string content) => Add(archive, name, Encoding.UTF8.GetBytes(content));

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string ContentTypes(bool withMetadata)
    {
        var custom = withMetadata
            ? """<Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>"""
            : string.Empty;
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
              <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
              {custom}
            </Types>
            """;
    }

    private static string Relationships(bool withMetadata)
    {
        var custom = withMetadata
            ? """<Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>"""
            : string.Empty;
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
              <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
              {custom}
            </Relationships>
            """;
    }

    private static string Core(bool withMetadata) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <dc:title>A title</dc:title>
          <dc:creator>{(withMetadata ? "Jane Doe" : string.Empty)}</dc:creator>
          <cp:lastModifiedBy>{(withMetadata ? "Jane Doe" : string.Empty)}</cp:lastModifiedBy>
        </cp:coreProperties>
        """;

    private static string App(bool withMetadata) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
          <Application>PermaDel</Application>
          <Company>{(withMetadata ? "Acme" : string.Empty)}</Company>
          <Manager>{(withMetadata ? "The Boss" : string.Empty)}</Manager>
        </Properties>
        """;

    private static string Custom() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties">
          <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Project">Apollo</property>
        </Properties>
        """;

    private static string Document() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>
        """;

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        return crc ^ 0xFFFFFFFFu;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }

    #region TIFF

    private sealed record TiffEntry(int Tag, int Type, byte[] Value);

    private static byte[] BuildTiff(List<TiffEntry> main, List<TiffEntry> exif, List<TiffEntry> gps, List<TiffEntry> thumbnail)
    {
        var ifd0Offset = 8u;
        var ifd0Size = DirectorySize(main.Count + 2);
        var exifOffset = ifd0Offset + ifd0Size;
        var gpsOffset = exifOffset + DirectorySize(exif.Count);
        var thumbnailOffset = gpsOffset + DirectorySize(gps.Count);
        var heapOffset = thumbnailOffset + DirectorySize(thumbnail.Count);

        main.Add(new TiffEntry(0x8769, 4, UInt32(exifOffset)));
        main.Add(new TiffEntry(0x8825, 4, UInt32(gpsOffset)));
        main.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        exif.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        gps.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        thumbnail.Sort((left, right) => left.Tag.CompareTo(right.Tag));

        var heap = new MemoryStream();
        var offsets = new Dictionary<TiffEntry, uint>();
        foreach (var directory in new[] { main, exif, gps, thumbnail })
        {
            foreach (var entry in directory.Where(entry => entry.Value.Length > 4))
            {
                if (heap.Length % 2 != 0)
                    heap.WriteByte(0);
                offsets[entry] = heapOffset + (uint)heap.Length;
                heap.Write(entry.Value);
            }
        }

        var file = new MemoryStream();
        WriteUInt16(file, 0x4949);
        WriteUInt16(file, 42);
        WriteUInt32(file, ifd0Offset);
        WriteDirectory(file, main, thumbnailOffset, offsets);
        WriteDirectory(file, exif, 0, offsets);
        WriteDirectory(file, gps, 0, offsets);
        WriteDirectory(file, thumbnail, 0, offsets);
        heap.Position = 0;
        heap.CopyTo(file);
        return file.ToArray();
    }

    private static uint DirectorySize(int entries) => (uint)(2 + (12 * entries) + 4);

    private static void WriteDirectory(Stream stream, List<TiffEntry> entries, uint next, Dictionary<TiffEntry, uint> offsets)
    {
        WriteUInt16(stream, (ushort)entries.Count);
        foreach (var entry in entries)
        {
            WriteUInt16(stream, (ushort)entry.Tag);
            WriteUInt16(stream, (ushort)entry.Type);
            WriteUInt32(stream, (uint)(entry.Type switch { 2 => entry.Value.Length, 3 => entry.Value.Length / 2, 4 => entry.Value.Length / 4, _ => 1 }));
            if (entry.Value.Length > 4)
            {
                WriteUInt32(stream, offsets[entry]);
            }
            else
            {
                stream.Write(entry.Value);
                for (var index = entry.Value.Length; index < 4; index++)
                    stream.WriteByte(0);
            }
        }
        WriteUInt32(stream, next);
    }

    private static TiffEntry Ascii(int tag, string text) => new(tag, 2, Encoding.ASCII.GetBytes(text + "\0"));

    private static TiffEntry Short(int tag, params ushort[] values) => new(tag, 3, values.SelectMany(BitConverter.GetBytes).ToArray());

    private static TiffEntry Long(int tag, params uint[] values) => new(tag, 4, values.SelectMany(BitConverter.GetBytes).ToArray());

    private static TiffEntry Rational(int tag, uint numerator, uint denominator) => new(tag, 5, [.. UInt32(numerator), .. UInt32(denominator)]);

    private static byte[] UInt32(uint value) => BitConverter.GetBytes(value);

    private static void WriteUInt16(Stream stream, ushort value) => stream.Write(BitConverter.GetBytes(value));

    private static void WriteUInt32(Stream stream, uint value) => stream.Write(BitConverter.GetBytes(value));

    #endregion
}
