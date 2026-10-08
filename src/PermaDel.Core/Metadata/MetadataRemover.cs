namespace PermaDel.Core.Metadata;

/// <summary>
/// Writes cleaned copies of files, or replaces the originals. Every cleaned file is written to a temporary file
/// in its own folder and checked before it takes the place of the real one, so a file that cannot be cleaned
/// safely is never written.
/// </summary>
public sealed class MetadataRemover
{
    private const string ReadOnlyReason = "This file is read-only.";

    /// <summary>
    /// Cleans the files that have metadata, skipping the ones PermaDel cannot clean or that have nothing to
    /// remove. Failures are reported per file and never stop the rest.
    /// </summary>
    public MetadataRemovalResult Remove(IEnumerable<string> paths, bool replaceOriginals)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copies = new List<string>();
        var failures = new List<MetadataRemovalFailure>();
        var removed = 0;

        foreach (var path in paths)
        {
            try
            {
                var inspection = MetadataCleaner.Inspect(path);
                if (!inspection.CanClean || !inspection.HasMetadata)
                    continue;
                if (IsReadOnly(path))
                {
                    failures.Add(new MetadataRemovalFailure(path, ReadOnlyReason));
                    continue;
                }

                var destination = replaceOriginals ? path : NextCopyPath(path, used);
                Clean(path, destination, inspection.Format, replaceOriginals);
                removed++;
                if (!replaceOriginals)
                    copies.Add(LongPath.ToDisplay(destination));
            }
            catch (FileNotFoundException)
            {
                failures.Add(new MetadataRemovalFailure(path, "The file no longer exists."));
            }
            catch (DirectoryNotFoundException)
            {
                failures.Add(new MetadataRemovalFailure(path, "The file no longer exists."));
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add(new MetadataRemovalFailure(path, "PermaDel is not allowed to change this file."));
            }
            catch (InvalidDataException)
            {
                failures.Add(new MetadataRemovalFailure(path, "The file could not be cleaned safely, so it was left as it was."));
            }
            catch (IOException ex)
            {
                failures.Add(new MetadataRemovalFailure(path, ex.Message));
            }
        }

        return new MetadataRemovalResult(removed, copies, failures);
    }

    /// <summary>The first free <c>{name} (clean){ext}</c> name, then <c>(clean 2)</c>, <c>(clean 3)</c> and so on.</summary>
    internal static string NextCopyPath(string path, ISet<string> reserved)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var candidate = Path.Combine(directory, $"{name} (clean){extension}");
        for (var number = 2; File.Exists(LongPath.ToVerbatim(candidate)) || reserved.Contains(candidate); number++)
            candidate = Path.Combine(directory, $"{name} (clean {number}){extension}");
        reserved.Add(candidate);
        return candidate;
    }

    private static void Clean(string source, string destination, MetadataFormat format, bool replace)
    {
        var directory = Path.GetDirectoryName(destination) ?? string.Empty;
        var temporary = Path.Combine(directory, $".PermaDel-{Path.GetRandomFileName()}.tmp");
        try
        {
            using (var input = OpenRead(source))
            using (var output = File.Create(LongPath.ToVerbatim(temporary)))
                MetadataCleaner.Clean(input, output, format);

            if (!MetadataCleaner.Validate(temporary, format))
                throw new InvalidDataException("The cleaned file did not pass PermaDel's safety check.");
            // Replacing keeps the original's creation time, attributes and permissions; a move would not.
            if (replace)
                File.Replace(LongPath.ToVerbatim(temporary), LongPath.ToVerbatim(destination), null);
            else
                File.Move(LongPath.ToVerbatim(temporary), LongPath.ToVerbatim(destination));
        }
        finally
        {
            if (File.Exists(LongPath.ToVerbatim(temporary)))
                File.Delete(LongPath.ToVerbatim(temporary));
        }
    }

    private static bool IsReadOnly(string path) =>
        (File.GetAttributes(LongPath.ToVerbatim(path)) & FileAttributes.ReadOnly) != 0;

    private static FileStream OpenRead(string path) =>
        new(LongPath.ToVerbatim(path), FileMode.Open, FileAccess.Read, FileShare.Read);
}
