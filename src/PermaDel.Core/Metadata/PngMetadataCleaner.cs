using System.Buffers.Binary;
using System.Text;

namespace PermaDel.Core.Metadata;

/// <summary>
/// Rewrites a PNG chunk by chunk. Text, time and EXIF chunks are dropped; every other chunk is copied whole,
/// together with its CRC, so nothing about the image itself changes.
/// </summary>
internal static class PngMetadataCleaner
{
    private const int InspectLimit = 1024 * 1024;
    private const int BufferSize = 64 * 1024;
    private const string EndChunk = "IEND";

    /// <summary>The eight bytes every PNG file starts with.</summary>
    internal static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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
        var signature = new byte[Signature.Length];
        ReadExactly(input, signature);
        if (!signature.AsSpan().SequenceEqual(Signature))
            throw new InvalidDataException("The file does not start with a PNG signature.");
        output?.Write(signature);

        var buffer = new byte[BufferSize];
        while (true)
        {
            var header = new byte[8];
            ReadExactly(input, header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(header, 4, 4);
            if (IsDropped(type))
            {
                var data = ReadUpTo(input, length, InspectLimit);
                if (found is not null)
                    InspectChunk(type, data, found);
                CopyOrSkip(input, null, length - (uint)data.Length, buffer);
                CopyOrSkip(input, null, 4, buffer);
            }
            else
            {
                output?.Write(header);
                CopyOrSkip(input, output, length, buffer);
                CopyOrSkip(input, output, 4, buffer);
            }

            if (type == EndChunk)
                break;
        }

        if (input.ReadByte() != -1)
            throw new InvalidDataException("The PNG has data after its end chunk.");
    }

    private static bool IsDropped(string type) => type is "tEXt" or "zTXt" or "iTXt" or "eXIf" or "tIME";

    private static void InspectChunk(string type, byte[] data, FoundMetadata found)
    {
        switch (type)
        {
            case "eXIf":
                ExifReader.Read(data, found);
                break;
            case "tIME":
                found.Add(MetadataCategory.DateTaken);
                break;
            case "tEXt" or "zTXt" or "iTXt":
                found.Add(Keyword(data) switch
                {
                    "author" or "artist" => MetadataCategory.AuthorAndComments,
                    "copyright" => MetadataCategory.Copyright,
                    "software" => MetadataCategory.EditingSoftware,
                    "creation time" => MetadataCategory.DateTaken,
                    _ => MetadataCategory.OtherText,
                });
                break;
        }
    }

    /// <summary>The keyword that starts a text chunk, up to the NUL that separates it from the text.</summary>
    private static string Keyword(byte[] data)
    {
        var end = Array.IndexOf(data, (byte)0);
        var keyword = Encoding.ASCII.GetString(data, 0, end < 0 ? data.Length : end);
        return keyword.ToLowerInvariant();
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
                throw new InvalidDataException("The PNG ends inside a chunk.");
            offset += read;
        }
    }

    private static void CopyOrSkip(Stream input, Stream? output, uint count, byte[] buffer)
    {
        while (count > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(count, (uint)buffer.Length));
            if (read <= 0)
                throw new InvalidDataException("The PNG ends inside a chunk.");
            output?.Write(buffer, 0, read);
            count -= (uint)read;
        }
    }
}
