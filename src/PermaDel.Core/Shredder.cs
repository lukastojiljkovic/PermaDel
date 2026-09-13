using System.Security.Cryptography;

namespace PermaDel.Core;

/// <summary>
/// Irrecoverably destroys files and directories. Every data stream is overwritten with cryptographically
/// secure random bytes for the configured number of passes, each pass flushed to disk. Files are then
/// truncated, renamed to random names, stripped of timestamps and deleted.
/// </summary>
/// <remarks>Instances are not thread-safe.</remarks>
public sealed class Shredder
{
    public const int MinPasses = 1;
    public const int MaxPasses = 35;

    private const int BufferSize = 1024 * 1024;
    private const int RenameRounds = 3;
    private const int RetryAttempts = 5;
    private const string NameAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Set on files whose data is held by a cloud or archive provider; opening them downloads the data first.</summary>
    private const FileAttributes NotLocalAttributes = FileAttributes.Offline | (FileAttributes)0x00400000; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS

    private static readonly DateTime ScrubbedTimestamp = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false };
    private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string AppDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>Folders that can't be shredded as a whole, and neither can any folder containing them. Their contents can.</summary>
    private static readonly string[] CriticalDirectories =
        new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
        }
        .Select(Environment.GetFolderPath)
        .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))
        .Where(Path.IsPathFullyQualified)
        .Select(Path.TrimEndingDirectorySeparator)
        .ToArray();

    private readonly byte[] _buffer = new byte[BufferSize];

    public Shredder(int passes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(passes, MinPasses);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(passes, MaxPasses);
        Passes = passes;
    }

    public int Passes { get; }

    /// <summary>
    /// Locations that must never be shredded: device paths, volume roots, the Windows directory and its contents,
    /// PermaDel's own installation, and system and user folders together with their ancestors. The path is checked
    /// both as written and as resolved through junctions, symbolic links, mapped drives and short names.
    /// </summary>
    public static bool IsProtected(string path)
    {
        if (LongPath.IsDevicePath(path))
            return true;

        var literal = Normalize(path);
        return IsProtectedLocation(Path.TrimEndingDirectorySeparator(Path.GetFullPath(literal)))
            || (NativeFile.GetFinalPath(literal) is { } finalPath && IsProtectedLocation(Path.TrimEndingDirectorySeparator(finalPath)));
    }

    /// <summary>Shreds the given files and directories, continuing past items that fail.</summary>
    /// <exception cref="OperationCanceledException">Cancellation was requested; items processed so far are destroyed.</exception>
    public ShredResult Shred(IEnumerable<string> paths, IProgress<ShredProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var failures = new List<ShredFailure>();
        var targets = new List<Target>();
        foreach (var path in RemoveNested(paths))
        {
            if (IsProtected(path))
                failures.Add(new ShredFailure(path, "This is a protected location."));
            else
                Collect(LongPath.ToVerbatim(path), targets, failures);
        }

        var tracker = new ProgressTracker(progress, targets.Sum(target => target.Length) * Passes, Passes);
        int files = 0, directories = 0;

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                switch (target.Kind)
                {
                    case TargetKind.File:
                        ShredFile(target, tracker, cancellationToken);
                        files++;
                        break;
                    case TargetKind.Directory:
                        RemoveDirectory(target.Path);
                        directories++;
                        break;
                    case TargetKind.Link:
                        RemoveLink(target.Path);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(Failure(target.Path, ex.Message));
            }
        }

        return new ShredResult(files, directories, failures);
    }

    /// <summary>Overwrites the whole stream in place once per pass, flushing each pass to the physical disk.</summary>
    internal void Overwrite(FileStream stream, ProgressTracker? tracker, CancellationToken cancellationToken)
    {
        var length = stream.Length;
        for (var pass = 1; pass <= Passes; pass++)
        {
            stream.Position = 0;
            for (var remaining = length; remaining > 0;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = _buffer.AsSpan(0, (int)Math.Min(_buffer.Length, remaining));
                RandomNumberGenerator.Fill(chunk);
                stream.Write(chunk);
                remaining -= chunk.Length;
                tracker?.Advance(pass, chunk.Length);
            }
            stream.Flush(flushToDisk: true);
        }
    }

    private void ShredFile(Target target, ProgressTracker tracker, CancellationToken cancellationToken)
    {
        var attributes = File.GetAttributes(target.Path);
        File.SetAttributes(target.Path, FileAttributes.Normal);
        tracker.BeginFile(LongPath.ToDisplay(target.Path));

        try
        {
            foreach (var dataStream in target.Streams)
            {
                using var stream = new FileStream(target.Path + dataStream.Name, FileMode.Open, FileAccess.Write, FileShare.None, bufferSize: 0);
                Overwrite(stream, tracker, cancellationToken);
                stream.SetLength(0);
            }
        }
        catch
        {
            // The file stays where it is, so put back the attributes cleared above.
            TryRestoreAttributes(target.Path, attributes);
            throw;
        }

        try
        {
            var obfuscated = Obfuscate(target.Path, isDirectory: false);
            Retry(() => File.Delete(obfuscated));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Its contents were destroyed, but the emptied file couldn't be removed. {ex.Message}", ex);
        }
    }

    private static void RemoveDirectory(string path)
    {
        if (Directory.EnumerateFileSystemEntries(path, "*", AllEntries).Any())
            throw new IOException("The folder still contains items that could not be shredded.");

        File.SetAttributes(path, FileAttributes.Normal);
        var obfuscated = Obfuscate(path, isDirectory: true);
        Retry(() => Directory.Delete(obfuscated));
    }

    /// <summary>Symbolic links and junctions are removed without ever touching what they point to.</summary>
    private static void RemoveLink(string path)
    {
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
            Directory.Delete(path);
        else
            File.Delete(path);
    }

    /// <summary>Renames the entry several times so the original name is overwritten in the MFT, then scrubs its timestamps.</summary>
    private static string Obfuscate(string path, bool isDirectory)
    {
        var parent = Path.GetDirectoryName(path)!;
        var nameLength = Math.Max(Path.GetFileName(path).Length, 8);

        for (var round = 0; round < RenameRounds; round++)
        {
            var source = path;
            var renamed = Path.Combine(parent, RandomNumberGenerator.GetString(NameAlphabet, nameLength));
            Retry(() =>
            {
                if (isDirectory)
                    Directory.Move(source, renamed);
                else
                    File.Move(source, renamed);
            });
            path = renamed;
        }

        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        info.CreationTimeUtc = info.LastWriteTimeUtc = info.LastAccessTimeUtc = ScrubbedTimestamp;
        return path;
    }

    /// <summary>
    /// Expands a selection into a children-before-parent list of targets. Iterative, so arbitrarily deep trees can't
    /// overflow the stack, and nothing is modified until the whole selection has been collected.
    /// </summary>
    private static void Collect(string root, List<Target> targets, List<ShredFailure> failures)
    {
        var pending = new Stack<(string Path, bool ChildrenCollected)>();
        pending.Push((root, false));

        while (pending.TryPop(out var item))
        {
            var path = item.Path;
            if (item.ChildrenCollected)
            {
                targets.Add(new Target(path, TargetKind.Directory, []));
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(path);
                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);

                if (attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null)
                {
                    targets.Add(new Target(path, TargetKind.Link, []));
                }
                else if (isDirectory)
                {
                    var children = Directory.EnumerateFileSystemEntries(path, "*", AllEntries).ToList();
                    pending.Push((path, true));
                    children.ForEach(child => pending.Push((child, false)));
                }
                else if ((attributes & NotLocalAttributes) != 0)
                {
                    failures.Add(Failure(path, "The file's data isn't stored on this PC (for example, it's online-only in OneDrive). Delete it through its cloud service instead."));
                }
                else if (NativeFile.GetLinkCount(path) > 1)
                {
                    failures.Add(Failure(path, "The file has other hard links. Overwriting it would also destroy the data they share, so it was left untouched."));
                }
                else
                {
                    targets.Add(new Target(path, TargetKind.File, DataStreams.Enumerate(path)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(Failure(path, ex.Message));
            }
        }
    }

    /// <summary>
    /// Antivirus scanners and the search indexer briefly open files that were just written, which can make an
    /// immediate rename or delete fail with a sharing violation.
    /// </summary>
    private static void Retry(Action operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < RetryAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    /// <summary>Best effort: failing to restore attributes must not hide the error that is already being reported.</summary>
    private static void TryRestoreAttributes(string path, FileAttributes attributes)
    {
        try
        {
            File.SetAttributes(path, attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ShredFailure Failure(string path, string reason) =>
        new(LongPath.ToDisplay(path), reason.Replace(path, LongPath.ToDisplay(path)));

    /// <summary>Fully qualified paths are kept exactly as written; see <see cref="LongPath"/>.</summary>
    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path));

    private static List<string> RemoveNested(IEnumerable<string> paths)
    {
        var fullPaths = paths.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return fullPaths.Where(path => !fullPaths.Any(other => IsDescendant(path, other))).ToList();
    }

    private static bool IsProtectedLocation(string fullPath) =>
        Path.GetDirectoryName(fullPath) is null
        || CriticalDirectories.Any(directory => IsWithin(directory, fullPath))
        || IsWithin(fullPath, WindowsDirectory)
        || IsWithin(fullPath, AppDirectory);

    private static bool IsWithin(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) || IsDescendant(path, directory);

    private static bool IsDescendant(string path, string ancestor) =>
        path.Length > ancestor.Length
        && path.StartsWith(Path.EndsInDirectorySeparator(ancestor) ? ancestor : ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private enum TargetKind { File, Directory, Link }

    private sealed record Target(string Path, TargetKind Kind, IReadOnlyList<DataStream> Streams)
    {
        public long Length => Streams.Sum(stream => stream.Length);
    }
}
