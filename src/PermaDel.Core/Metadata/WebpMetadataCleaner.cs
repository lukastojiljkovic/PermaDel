using System.Buffers.Binary;
using System.Text;

namespace PermaDel.Core.Metadata;

/// <summary>
/// Rewrites a WebP chunk by chunk. The EXIF and XMP chunks are dropped and the two matching hint bits in the
/// feature chunk are cleared; the RIFF size is written again so it matches the shorter file.
/// </summary>
internal static class WebpMetadataCleaner
{
    private const int ExifFlag = 0x08;
    private const int XmpFlag = 0x04;
    private const int FeatureChunkSize = 10;
    private const int InspectLimit = 1024 * 1024;
    private const int BufferSize = 64 * 1024;

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

    /// <summary>The cleaned file is only complete once its RIFF size is known, so it is built in memory first.</summary>
    public static void Clean(Stream input, Stream output)
    {
        using var cleaned = new MemoryStream();
        Walk(input, null, cleaned);
        cleaned.Position = 0;
        cleaned.CopyTo(output);
    }

    private static void Walk(Stream input, FoundMetadata? found, MemoryStream? output)
    {
        input.Seek(0, SeekOrigin.Begin);
        if (input.Length < 12)
            throw new InvalidDataException("The file is too short to be a WebP image.");

        var header = new byte[12];
        ReadExactly(input, header);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("The file does not start with a WebP header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != input.Length - 8)
            throw new InvalidDataException("The WebP size does not match the file.");

        var buffer = new byte[BufferSize];
        output?.Write(header);
        var chunk = new byte[8];
        while (input.Position < input.Length)
        {
            ReadExactly(input, chunk);
            var type = Encoding.ASCII.GetString(chunk, 0, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            var padded = size + (size & 1);
            if (padded > input.Length - input.Position)
                throw new InvalidDataException("The WebP ends inside a chunk.");

            switch (type)
            {
                case "EXIF":
                    if (found is null)
                    {
                        CopyOrSkip(input, null, padded, buffer);
                    }
                    else
                    {
                        var data = ReadUpTo(input, size, InspectLimit);
                        ExifReader.Read(data, found);
                        CopyOrSkip(input, null, padded - (uint)data.Length, buffer);
                    }
                    break;
                case "XMP ":
                    found?.Add(MetadataCategory.OtherText);
                    CopyOrSkip(input, null, padded, buffer);
                    break;
                case "VP8X":
                    WriteFeatureChunk(input, chunk, size, output);
                    break;
                default:
                    output?.Write(chunk);
                    CopyOrSkip(input, output, padded, buffer);
                    break;
            }
        }

        if (output is null)
            return;

        output.Position = 4;
        output.Write(BitConverter.GetBytes((uint)(output.Length - 8)), 0, 4);
        output.Position = output.Length;
    }

    private static void WriteFeatureChunk(Stream input, byte[] chunk, uint size, MemoryStream? output)
    {
        if (size != FeatureChunkSize)
            throw new InvalidDataException("The WebP's feature chunk has an unexpected size.");

        var payload = new byte[FeatureChunkSize];
        ReadExactly(input, payload);
        payload[0] &= unchecked((byte)~(ExifFlag | XmpFlag));
        if (output is null)
            return;

        output.Write(chunk);
        output.Write(payload);
    }

    private static byte[] ReadUpTo(Stream input, uint length, int limit)
    {
        var data = new byte[(int)Math.Min(length, (uint)limit)];
        ReadExactly(input, data);
        return data;
    }

    private static void ReadExactly(Stream input, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = input.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
                throw new InvalidDataException("The WebP ends inside a chunk.");
            offset += read;
        }
    }

    private static void CopyOrSkip(Stream input, Stream? output, uint count, byte[] buffer)
    {
        while (count > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(count, (uint)buffer.Length));
            if (read <= 0)
                throw new InvalidDataException("The WebP ends inside a chunk.");
            output?.Write(buffer, 0, read);
            count -= (uint)read;
        }
    }
}
