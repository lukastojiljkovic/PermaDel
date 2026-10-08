using System.Buffers.Binary;

namespace PermaDel.Core.Metadata;

/// <summary>
/// Reads a TIFF/EXIF block just far enough to name what is inside: which tags are present, whether the
/// orientation tag asks apps to rotate the pixels, and whether a thumbnail image follows. Values other
/// than those are never decoded, so a block with an unknown tag is still reported as removable.
/// </summary>
internal static class ExifReader
{
    private const int MaxEntriesPerDirectory = 512;

    private const int TagImageDescription = 0x010E;
    private const int TagMake = 0x010F;
    private const int TagModel = 0x0110;
    private const int TagOrientation = 0x0112;
    private const int TagSoftware = 0x0131;
    private const int TagDateTime = 0x0132;
    private const int TagArtist = 0x013B;
    private const int TagCopyright = 0x8298;
    private const int TagExifDirectory = 0x8769;
    private const int TagGpsDirectory = 0x8825;
    private const int TagDateTimeOriginal = 0x9003;
    private const int TagDateTimeDigitized = 0x9004;
    private const int TagUserComment = 0x9286;

    /// <summary>Camera and lens settings that only a camera writes.</summary>
    private static readonly int[] CameraTags =
    [
        0x829A, 0x829D, 0x8822, 0x8827, 0x8830, 0x9201, 0x9202, 0x9204, 0x9207, 0x9208, 0x9209,
        0x920A, 0xA001, 0xA402, 0xA403, 0xA405, 0xA406, 0xA430, 0xA431, 0xA432, 0xA433, 0xA434, 0xA435,
    ];

    /// <summary>Adds everything the TIFF block holds. <paramref name="tiff"/> starts at the byte-order marker.</summary>
    public static void Read(ReadOnlySpan<byte> tiff, FoundMetadata found)
    {
        var before = found.Count;
        if (IsTiff(tiff))
        {
            var littleEndian = tiff[0] == 0x49;
            var pointers = ReadDirectory(tiff, ReadUInt32(tiff, 4, littleEndian), littleEndian, found, IfdKind.Main);
            if (pointers.Exif != 0)
                ReadDirectory(tiff, pointers.Exif, littleEndian, found, IfdKind.Exif);
            if (pointers.Gps != 0)
                ReadDirectory(tiff, pointers.Gps, littleEndian, found, IfdKind.Gps);
            if (pointers.Next != 0)
            {
                found.Add(MetadataCategory.Thumbnail);
                ReadDirectory(tiff, pointers.Next, littleEndian, found, IfdKind.Thumbnail);
            }
        }

        // An EXIF block with nothing PermaDel can name, or one that cannot be read, is still worth removing.
        if (found.Count == before)
            found.Add(MetadataCategory.OtherDetails);
    }

    private static bool IsTiff(ReadOnlySpan<byte> tiff) =>
        tiff.Length >= 8
        && ((tiff[0] == 0x49 && tiff[1] == 0x49) || (tiff[0] == 0x4D && tiff[1] == 0x4D))
        && ReadUInt16(tiff, 2, tiff[0] == 0x49) == 0x002A;

    /// <summary>Reads one image file directory and returns the offsets of the directories it points at.</summary>
    private static Pointers ReadDirectory(ReadOnlySpan<byte> tiff, uint offset, bool littleEndian, FoundMetadata found, IfdKind kind)
    {
        // Compared without adding to the offset: a crafted offset near uint.MaxValue would wrap past the check.
        if (offset < 8 || offset > tiff.Length - 2)
            return default;

        var entries = ReadUInt16(tiff, (int)offset, littleEndian);
        var position = (int)offset + 2;
        uint exif = 0, gps = 0;
        for (var index = 0; index < entries && index < MaxEntriesPerDirectory; index++, position += 12)
        {
            if (position + 12 > tiff.Length)
                return default;

            var tag = ReadUInt16(tiff, position, littleEndian);
            var value = ReadUInt32(tiff, position + 8, littleEndian);
            switch (kind)
            {
                case IfdKind.Gps:
                    found.Add(MetadataCategory.Location);
                    break;
                case IfdKind.Main:
                    MapMain(tag, value, found, ref exif, ref gps);
                    break;
                case IfdKind.Exif:
                    MapExif(tag, found);
                    break;
            }

            if (kind == IfdKind.Main && tag == TagOrientation && ReadUInt16(tiff, position + 8, littleEndian) != 1)
                found.NeedsRotation = true;
        }

        if (kind != IfdKind.Main || position + 4 > tiff.Length)
            return new Pointers(0, exif, gps);
        return new Pointers(ReadUInt32(tiff, position, littleEndian), exif, gps);
    }

    private static void MapMain(int tag, uint value, FoundMetadata found, ref uint exif, ref uint gps)
    {
        switch (tag)
        {
            case TagMake or TagModel:
                found.Add(MetadataCategory.CameraDetails);
                break;
            case TagSoftware:
                found.Add(MetadataCategory.EditingSoftware);
                break;
            case TagArtist:
                found.Add(MetadataCategory.AuthorAndComments);
                break;
            case TagCopyright:
                found.Add(MetadataCategory.Copyright);
                break;
            case TagImageDescription:
                found.Add(MetadataCategory.OtherDetails);
                break;
            case TagDateTime:
                found.Add(MetadataCategory.DateTaken);
                break;
            case TagExifDirectory:
                exif = value;
                break;
            case TagGpsDirectory:
                gps = value;
                break;
        }
    }

    private static void MapExif(int tag, FoundMetadata found)
    {
        switch (tag)
        {
            case TagDateTimeOriginal or TagDateTimeDigitized:
                found.Add(MetadataCategory.DateTaken);
                break;
            case TagUserComment:
                found.Add(MetadataCategory.AuthorAndComments);
                break;
            default:
                if (CameraTags.Contains(tag))
                    found.Add(MetadataCategory.CameraDetails);
                break;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    private enum IfdKind
    {
        Main,
        Exif,
        Gps,
        Thumbnail,
    }

    private readonly record struct Pointers(uint Next, uint Exif, uint Gps);
}
