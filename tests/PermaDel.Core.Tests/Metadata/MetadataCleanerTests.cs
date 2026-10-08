using System.Text;
using PermaDel.Core.Metadata;

namespace PermaDel.Core.Tests.Metadata;

public sealed class MetadataCleanerTests
{
    [Theory]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0xFFFFFFFEu)]
    [InlineData(0x7FFFFFFFu)]
    public void Exif_Read_IgnoresADirectoryOffsetPastTheEnd(uint offset)
    {
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00, (byte)offset, (byte)(offset >> 8), (byte)(offset >> 16), (byte)(offset >> 24)];
        var found = new FoundMetadata();

        ExifReader.Read(tiff, found);

        Assert.Equal([MetadataCategory.OtherText], found.ToList());
    }

    [Fact]
    public void Jpeg_Clean_RemovesOnlyTheMetadataSegments()
    {
        var (original, expected) = MetadataFixtures.Jpeg();

        var cleaned = Clean(original, "photo.jpg");

        Assert.Equal(expected, cleaned);
        Assert.Contains("ICC_PROFILE", Text(cleaned));
        Assert.Contains("JFIF", Text(cleaned));
        Assert.DoesNotContain("Exif", Text(cleaned));
        Assert.DoesNotContain("xmpmeta", Text(cleaned));
        Assert.DoesNotContain("Photoshop", Text(cleaned));
        Assert.DoesNotContain("A comment", Text(cleaned));
        Assert.DoesNotContain("unknown", Text(cleaned));
    }

    [Fact]
    public void Jpeg_Clean_KeepsTheEntropyCodedDataByteForByte()
    {
        var (original, _) = MetadataFixtures.Jpeg();
        var entropy = new byte[] { 0x12, 0x34, 0xFF, 0x00, 0x56, 0xAB, 0xFF, 0xD0, 0x9A };

        var cleaned = Clean(original, "photo.jpg");

        Assert.True(Contains(cleaned, entropy));
        Assert.Equal(new byte[] { 0xFF, 0xD9 }, cleaned[^2..]);
    }

    [Fact]
    public void Jpeg_Inspect_NamesEveryCategory()
    {
        var (original, _) = MetadataFixtures.Jpeg();

        var inspection = Inspect(original, "photo.jpg");

        Assert.Equal(MetadataFormat.Jpeg, inspection.Format);
        Assert.Equal(
            [
                MetadataCategory.CameraDetails,
                MetadataCategory.Location,
                MetadataCategory.DateTaken,
                MetadataCategory.EditingSoftware,
                MetadataCategory.AuthorAndComments,
                MetadataCategory.Copyright,
                MetadataCategory.Thumbnail,
                MetadataCategory.OtherText,
            ],
            inspection.Categories);
    }

    [Fact]
    public void Jpeg_Inspect_ReportsNothingToRemoveForACleanFile()
    {
        var inspection = Inspect(MetadataFixtures.CleanJpeg(), "photo.jpg");

        Assert.Equal(MetadataFormat.Jpeg, inspection.Format);
        Assert.True(inspection.CanClean);
        Assert.False(inspection.HasMetadata);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(8, true)]
    public void Jpeg_Inspect_RaisesTheSidewaysNoticeOnlyWhenTheOrientationTagRotatesThePixels(ushort orientation, bool expected)
    {
        var (original, _) = MetadataFixtures.Jpeg(orientation);

        Assert.Equal(expected, Inspect(original, "photo.jpg").MayShowSideways);
    }

    [Fact]
    public void Png_Clean_RemovesOnlyTheTextTimeAndExifChunks()
    {
        var (original, expected) = MetadataFixtures.Png();

        Assert.Equal(expected, Clean(original, "image.png"));
    }

    [Fact]
    public void Png_Inspect_NamesEveryCategory()
    {
        var (original, _) = MetadataFixtures.Png();

        var inspection = Inspect(original, "image.png");

        Assert.Equal(MetadataFormat.Png, inspection.Format);
        Assert.Equal(
            [
                MetadataCategory.CameraDetails,
                MetadataCategory.Location,
                MetadataCategory.DateTaken,
                MetadataCategory.EditingSoftware,
                MetadataCategory.AuthorAndComments,
                MetadataCategory.Copyright,
                MetadataCategory.Thumbnail,
                MetadataCategory.OtherText,
            ],
            inspection.Categories);
    }

    [Fact]
    public void Png_Clean_RejectsAFileThatEndsBeforeItsEndChunk()
    {
        var (original, _) = MetadataFixtures.Png();

        Assert.Throws<InvalidDataException>(() => Clean(original[..^6], "image.png"));
    }

    [Fact]
    public void Webp_Clean_DropsExifAndXmpAndClearsTheirFlags()
    {
        var (original, expected) = MetadataFixtures.Webp();

        var cleaned = Clean(original, "image.webp");

        Assert.Equal(expected, cleaned);
        Assert.Equal(0x10, (int)cleaned[20]);
        Assert.Equal((uint)(cleaned.Length - 8), BitConverter.ToUInt32(cleaned, 4));
    }

    [Fact]
    public void Webp_Inspect_NamesEveryCategory()
    {
        var (original, _) = MetadataFixtures.Webp();

        var inspection = Inspect(original, "image.webp");

        Assert.Equal(MetadataFormat.WebP, inspection.Format);
        Assert.Equal(
            [
                MetadataCategory.CameraDetails,
                MetadataCategory.Location,
                MetadataCategory.DateTaken,
                MetadataCategory.EditingSoftware,
                MetadataCategory.AuthorAndComments,
                MetadataCategory.Copyright,
                MetadataCategory.Thumbnail,
                MetadataCategory.OtherText,
            ],
            inspection.Categories);
    }

    [Fact]
    public void Webp_Inspect_ReportsNothingToRemoveForACleanFile()
    {
        var (_, clean) = MetadataFixtures.Webp();

        Assert.False(Inspect(clean, "image.webp").HasMetadata);
    }

    [Fact]
    public void Docx_Inspect_NamesTheOfficeProperties()
    {
        var inspection = Inspect(MetadataFixtures.Docx(withMetadata: true), "report.docx");

        Assert.Equal(MetadataFormat.OfficeOpenXml, inspection.Format);
        Assert.Equal(
            [
                MetadataCategory.Author,
                MetadataCategory.LastSavedBy,
                MetadataCategory.Company,
                MetadataCategory.Manager,
                MetadataCategory.CustomProperties,
            ],
            inspection.Categories);
    }

    [Fact]
    public void Docx_Clean_EmptiesTheNamesAndDropsTheCustomPart()
    {
        var cleaned = Clean(MetadataFixtures.Docx(withMetadata: true), "report.docx");

        Assert.Null(MetadataFixtures.ReadPart(cleaned, "docProps/custom.xml"));

        var core = MetadataFixtures.ReadPartText(cleaned, "docProps/core.xml");
        Assert.Contains("<dc:title>A title</dc:title>", core);
        Assert.DoesNotContain("Jane Doe", core);

        var app = MetadataFixtures.ReadPartText(cleaned, "docProps/app.xml");
        Assert.Contains("<Application>PermaDel</Application>", app);
        Assert.DoesNotContain("Acme", app);
        Assert.DoesNotContain("The Boss", app);

        var contentTypes = MetadataFixtures.ReadPartText(cleaned, "[Content_Types].xml");
        Assert.DoesNotContain("custom", contentTypes);
        Assert.Contains("/word/document.xml", contentTypes);

        var relationships = MetadataFixtures.ReadPartText(cleaned, "_rels/.rels");
        Assert.DoesNotContain("custom-properties", relationships);
        Assert.Contains("officeDocument", relationships);
    }

    [Fact]
    public void Docx_Clean_LeavesTheDocumentPartByteForByte()
    {
        var original = MetadataFixtures.Docx(withMetadata: true);

        var cleaned = Clean(original, "report.docx");

        Assert.Equal(
            MetadataFixtures.ReadPart(original, MetadataFixtures.DocumentPart),
            MetadataFixtures.ReadPart(cleaned, MetadataFixtures.DocumentPart));
    }

    [Fact]
    public void Docx_Inspect_ReportsNothingToRemoveForACleanFile()
    {
        var inspection = Inspect(MetadataFixtures.Docx(withMetadata: false), "report.docx");

        Assert.Equal(MetadataFormat.OfficeOpenXml, inspection.Format);
        Assert.False(inspection.HasMetadata);
    }

    [Fact]
    public void Inspect_ReportsUnsupportedTypes()
    {
        var inspection = Inspect("just some text"u8.ToArray(), "notes.txt");

        Assert.Equal(MetadataFormat.Unsupported, inspection.Format);
        Assert.False(inspection.CanClean);
        Assert.Empty(inspection.Categories);
    }

    [Fact]
    public void Detect_ReadsTheFormatFromTheContentsNotTheName()
    {
        var (jpeg, _) = MetadataFixtures.Jpeg();
        var (png, _) = MetadataFixtures.Png();
        var (webp, _) = MetadataFixtures.Webp();

        Assert.Equal(MetadataFormat.Jpeg, Detect(jpeg, "photo.txt"));
        Assert.Equal(MetadataFormat.Png, Detect(png, "image"));
        Assert.Equal(MetadataFormat.WebP, Detect(webp, "image.bin"));
        Assert.Equal(MetadataFormat.OfficeOpenXml, Detect(MetadataFixtures.Docx(withMetadata: true), "report.docx"));
        Assert.Equal(MetadataFormat.Unsupported, Detect(MetadataFixtures.Docx(withMetadata: true), "report.zip"));
    }

    [Theory]
    [InlineData("photo.jpg", MetadataFormat.Jpeg)]
    [InlineData("image.png", MetadataFormat.Png)]
    [InlineData("image.webp", MetadataFormat.WebP)]
    [InlineData("report.docx", MetadataFormat.OfficeOpenXml)]
    public void Validate_AcceptsACleanedFile(string name, MetadataFormat format)
    {
        var root = Directory.CreateTempSubdirectory("PermaDelValidate-");
        try
        {
            var original = format switch
            {
                MetadataFormat.Jpeg => MetadataFixtures.Jpeg().Original,
                MetadataFormat.Png => MetadataFixtures.Png().Original,
                MetadataFormat.WebP => MetadataFixtures.Webp().Original,
                _ => MetadataFixtures.Docx(withMetadata: true),
            };
            var path = Path.Combine(root.FullName, name);
            var cleaned = Clean(original, name);
            File.WriteAllBytes(path, cleaned);

            Assert.True(MetadataCleaner.Validate(path, format));

            File.WriteAllBytes(path, cleaned[..^4]);
            Assert.False(MetadataCleaner.Validate(path, format));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static MetadataFormat Detect(byte[] content, string name)
    {
        using var stream = new MemoryStream(content);
        return MetadataCleaner.Detect(stream, name);
    }

    private static MetadataInspection Inspect(byte[] content, string name)
    {
        using var stream = new MemoryStream(content);
        return MetadataCleaner.Inspect(stream, name);
    }

    private static byte[] Clean(byte[] content, string name)
    {
        using var source = new MemoryStream(content);
        using var output = new MemoryStream();
        MetadataCleaner.Clean(source, output, MetadataCleaner.Detect(source, name));
        return output.ToArray();
    }

    private static string Text(byte[] content)
    {
        var text = new StringBuilder();
        foreach (var value in content)
            text.Append(value is >= 32 and < 127 ? (char)value : '.');
        return text.ToString();
    }

    private static bool Contains(byte[] content, byte[] value) => content.AsSpan().IndexOf(value) >= 0;
}
