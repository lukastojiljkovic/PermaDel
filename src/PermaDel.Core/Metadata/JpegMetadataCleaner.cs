using System.Buffers.Binary;

namespace PermaDel.Core.Metadata;

/// <summary>
/// Walks a JPEG marker by marker. Of the application segments and comments only the JFIF header (without its
/// preview image), the ICC profile and the Adobe colour marker are kept, and nothing after the end marker is
/// copied. Every other byte, including the entropy-coded image data, is copied exactly as it was.
/// </summary>
internal static class JpegMetadataCleaner
{
    private const int MarkerSoi = 0xD8;
    private const int MarkerEoi = 0xD9;
    private const int MarkerSos = 0xDA;
    private const int MarkerApp0 = 0xE0;
    private const int MarkerApp1 = 0xE1;
    private const int MarkerApp2 = 0xE2;
    private const int MarkerApp13 = 0xED;
    private const int MarkerApp14 = 0xEE;
    private const int MarkerApp15 = 0xEF;
    private const int MarkerCom = 0xFE;
    private const int JfifLength = 14;

    private static readonly byte[] JfifHeader = "JFIF\0"u8.ToArray();
    private static readonly byte[] JfxxHeader = "JFXX\0"u8.ToArray();
    private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();
    private static readonly byte[] IccHeader = "ICC_PROFILE\0"u8.ToArray();
    private static readonly byte[] PhotoshopHeader = "Photoshop 3.0\0"u8.ToArray();
    private static readonly byte[] AdobeHeader = "Adobe"u8.ToArray();

    /// <summary>Names what the file holds. A file that ends early is read up to the point it stops.</summary>
    public static void Inspect(Stream input, FoundMetadata found)
    {
        try
        {
            Walk(input, found, null);
        }
        catch (InvalidDataException)
        {
            // Whatever was found before the file stopped is still worth offering to remove.
        }
    }

    public static void Clean(Stream input, Stream output) => Walk(input, null, output);

    private static void Walk(Stream input, FoundMetadata? found, Stream? output)
    {
        input.Seek(0, SeekOrigin.Begin);
        var reader = new MarkerReader(input);
        if (ReadRequired(reader) != 0xFF || ReadRequired(reader) != MarkerSoi)
            throw new InvalidDataException("The file does not start with a JPEG image.");

        if (output is not null)
        {
            output.WriteByte(0xFF);
            output.WriteByte(MarkerSoi);
        }

        var hasImageData = false;
        while (true)
        {
            var marker = NextMarker(reader);
            if (marker == MarkerEoi)
            {
                WriteMarker(output, marker);
                break;
            }

            if (marker == MarkerSos)
            {
                hasImageData = true;
                WriteSegment(output, marker, ReadSegment(reader));
                if (CopyEntropyCodedData(reader, output))
                    break;
                continue;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                WriteMarker(output, marker);
                continue;
            }

            var segment = ReadSegment(reader);
            var kept = Kept(marker, segment);
            if (kept != segment)
                Collect(found, marker, segment);
            if (kept is not null)
                WriteSegment(output, marker, kept);
        }

        if (!hasImageData)
            throw new InvalidDataException("The JPEG has no image data.");
        // Phones append motion clips and extra pictures after the end marker; none of it is copied.
        if (found is not null && reader.Read() >= 0)
            found.Add(MetadataCategory.OtherDetails);
    }

    /// <summary>Reads the next marker, skipping fill bytes and stray stuffed bytes.</summary>
    private static int NextMarker(MarkerReader reader)
    {
        while (true)
        {
            if (ReadRequired(reader) != 0xFF)
                continue;

            var marker = ReadRequired(reader);
            if (marker == 0x00)
                continue;
            while (marker == 0xFF)
                marker = ReadRequired(reader);
            return marker;
        }
    }

    /// <summary>Copies the scan's bytes until the image ends. Returns true when it ended at the EOI marker.</summary>
    private static bool CopyEntropyCodedData(MarkerReader reader, Stream? output)
    {
        while (true)
        {
            var current = ReadRequired(reader);
            if (current != 0xFF)
            {
                output?.WriteByte((byte)current);
                continue;
            }

            var next = ReadRequired(reader);
            if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
            {
                output?.WriteByte(0xFF);
                output?.WriteByte((byte)next);
                continue;
            }

            if (next == MarkerEoi)
            {
                output?.WriteByte(0xFF);
                output?.WriteByte(MarkerEoi);
                return true;
            }

            // Another marker segment follows, as in a progressive file; let the main loop read it again.
            reader.Push((byte)next);
            reader.Push(0xFF);
            return false;
        }
    }

    /// <summary>Reads a marker segment's length and payload, throwing when the file ends inside it.</summary>
    private static byte[] ReadSegment(MarkerReader reader)
    {
        var length = (ReadRequired(reader) << 8) | ReadRequired(reader);
        if (length < 2)
            throw new InvalidDataException("A JPEG segment has an invalid length.");

        var payload = new byte[length - 2];
        for (var index = 0; index < payload.Length; index++)
            payload[index] = (byte)ReadRequired(reader);
        return payload;
    }

    private static void WriteSegment(Stream? output, int marker, byte[] payload)
    {
        if (output is null)
            return;

        output.WriteByte(0xFF);
        output.WriteByte((byte)marker);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        output.Write(length);
        output.Write(payload);
    }

    private static void WriteMarker(Stream? output, int marker)
    {
        if (output is null)
            return;

        output.WriteByte(0xFF);
        output.WriteByte((byte)marker);
    }

    /// <summary>
    /// The payload a segment is written with, or null when it is dropped. A trimmed payload is a new array, so the
    /// caller can tell it apart from a segment that was kept whole.
    /// </summary>
    private static byte[]? Kept(int marker, byte[] payload) => marker switch
    {
        MarkerApp0 => IsJfif(payload) ? WithoutThumbnail(payload) : null,
        MarkerApp2 => StartsWith(payload, IccHeader) ? payload : null,
        MarkerApp14 => StartsWith(payload, AdobeHeader) ? payload : null,
        MarkerCom or (>= MarkerApp1 and <= MarkerApp15) => null,
        _ => payload,
    };

    private static bool IsJfif(byte[] payload) => payload.Length >= JfifLength && StartsWith(payload, JfifHeader);

    /// <summary>The JFIF header without the small uncompressed preview it may carry after its first fourteen bytes.</summary>
    private static byte[] WithoutThumbnail(byte[] payload)
    {
        if (payload.Length == JfifLength && payload[12] == 0 && payload[13] == 0)
            return payload;

        var header = payload[..JfifLength];
        header[12] = header[13] = 0;
        return header;
    }

    /// <summary>Names what a dropped or trimmed segment held.</summary>
    private static void Collect(FoundMetadata? found, int marker, byte[] payload)
    {
        if (found is null)
            return;

        switch (marker)
        {
            case MarkerApp0:
                found.Add(StartsWith(payload, JfifHeader) || StartsWith(payload, JfxxHeader)
                    ? MetadataCategory.Thumbnail
                    : MetadataCategory.OtherDetails);
                break;
            case MarkerApp1 when StartsWith(payload, ExifHeader):
                ExifReader.Read(payload.AsSpan(ExifHeader.Length), found);
                break;
            case MarkerApp13:
                ReadIptc(payload, found);
                break;
            default:
                found.Add(MetadataCategory.OtherDetails);
                break;
        }
    }

    /// <summary>Reads the IPTC datasets inside a Photoshop APP13 segment, if the segment is one.</summary>
    private static void ReadIptc(byte[] payload, FoundMetadata found)
    {
        var before = found.Count;
        if (StartsWith(payload, PhotoshopHeader))
        {
            var position = PhotoshopHeader.Length;
            while (position + 12 <= payload.Length && payload.AsSpan(position, 4).SequenceEqual("8BIM"u8))
            {
                var resource = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(position + 4));
                var nameLength = payload[position + 6] + 1;
                if (nameLength % 2 != 0)
                    nameLength++;
                var sizePosition = position + 6 + nameLength;
                if (sizePosition + 4 > payload.Length)
                    break;

                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(sizePosition));
                var dataPosition = sizePosition + 4;
                if (size < 0 || dataPosition + size > payload.Length)
                    break;

                if (resource == 0x0404)
                    ReadIptcDatasets(payload.AsSpan(dataPosition, size), found);
                position = dataPosition + size;
                if (position % 2 != 0)
                    position++;
            }
        }

        if (found.Count == before)
            found.Add(MetadataCategory.OtherDetails);
    }

    private static void ReadIptcDatasets(ReadOnlySpan<byte> data, FoundMetadata found)
    {
        for (var position = 0; position + 5 <= data.Length;)
        {
            if (data[position] != 0x1C)
            {
                position++;
                continue;
            }

            var record = data[position + 1];
            var dataset = data[position + 2];
            var length = (data[position + 3] << 8) | data[position + 4];
            var start = position + 5;
            if (record == 2 && start + length <= data.Length)
            {
                var category = dataset switch
                {
                    55 => MetadataCategory.DateTaken,
                    80 or 85 or 110 => MetadataCategory.AuthorAndComments,
                    90 or 92 or 95 or 101 => MetadataCategory.Location,
                    116 => MetadataCategory.Copyright,
                    _ => MetadataCategory.OtherDetails,
                };
                found.Add(category);
            }

            position = start + length;
        }
    }

    private static bool StartsWith(byte[] payload, byte[] prefix) =>
        payload.Length >= prefix.Length && payload.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static int ReadRequired(MarkerReader reader) =>
        reader.Read() is var value && value >= 0 ? value : throw new InvalidDataException("The JPEG ends in the middle of a marker.");

    /// <summary>A stream reader that can hand the last two bytes back when a marker ends the image data.</summary>
    private sealed class MarkerReader(Stream stream)
    {
        private readonly List<byte> _pushedBack = [];

        public int Read()
        {
            if (_pushedBack.Count == 0)
                return stream.ReadByte();

            var value = _pushedBack[^1];
            _pushedBack.RemoveAt(_pushedBack.Count - 1);
            return value;
        }

        public void Push(byte value) => _pushedBack.Add(value);
    }
}
