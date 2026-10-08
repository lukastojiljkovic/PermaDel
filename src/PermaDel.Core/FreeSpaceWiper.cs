using System.Security.Cryptography;

namespace PermaDel.Core;

/// <summary>
/// Writes random data over every byte of a volume's free space and then removes it again, so files that were deleted
/// the ordinary way can't be read back. Existing files are never touched: the wipe only ever creates and deletes its
/// own folder at the root of the drive.
/// </summary>
/// <remarks>Instances are not thread-safe.</remarks>
public sealed class FreeSpaceWiper
{
    /// <summary>The largest file the main phase writes before shrinking its writes to fill the remainder.</summary>
    public const long MaxFileSize = 1024L * 1024 * 1024;

    /// <summary>The most file table entries a single wipe fills.</summary>
    public const int MaxFileTableEntries = 1_000_000;

    /// <summary>Every wipe folder's name starts with this, followed by a GUID.</summary>
    public const string FolderPrefix = "PermaDel free space ";

    private const int BufferSize = 1024 * 1024;
    private const int FileTableEntryMaxLength = 512;
    private const long Fat32MaxFileSize = 4L * 1024 * 1024 * 1024;

    private readonly byte[] _buffer = new byte[BufferSize];

    public FreeSpaceWiper()
    {
    }

    /// <param name="maxFileTableEntries">How many file table entries to fill at most; tests lower it.</param>
    internal FreeSpaceWiper(int maxFileTableEntries) => FileTableCap = Math.Clamp(maxFileTableEntries, 0, MaxFileTableEntries);

    internal int FileTableCap { get; } = MaxFileTableEntries;

    /// <summary>Whether PermaDel offers to wipe free space on a drive like this.</summary>
    public static bool Supports(DriveType type, WipeFileSystem fileSystem, bool isReadOnly) =>
        !isReadOnly
        && type is DriveType.Fixed or DriveType.Removable
        && fileSystem is WipeFileSystem.Ntfs or WipeFileSystem.ExFat or WipeFileSystem.Fat32;

    /// <summary>The file system behind a volume's format name, as <see cref="DriveInfo.DriveFormat"/> reports it.</summary>
    public static WipeFileSystem FromFileSystemName(string? name) => name?.ToUpperInvariant() switch
    {
        "NTFS" => WipeFileSystem.Ntfs,
        "EXFAT" => WipeFileSystem.ExFat,
        "FAT32" => WipeFileSystem.Fat32,
        _ => WipeFileSystem.Other,
    };

    /// <summary>Only NTFS keeps small files inside its file table, so only it has free entries to fill.</summary>
    public static bool CanCleanFileTable(WipeFileSystem fileSystem) => fileSystem == WipeFileSystem.Ntfs;

    /// <summary>Inspects a drive, or returns null when it can't be read.</summary>
    public static WipeDriveInfo? Describe(string driveRoot)
    {
        try
        {
            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady)
                return null;

            var root = drive.RootDirectory.FullName;
            return new WipeDriveInfo(
                drive.DriveType,
                FromFileSystemName(drive.DriveFormat),
                VolumeProbe.IsReadOnly(root),
                VolumeProbe.IncursSeekPenalty(root),
                drive.AvailableFreeSpace,
                drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the wipe folders a crash or a killed process left behind at the root of a drive, and returns how many
    /// were removed. Only folders whose name is <see cref="FolderPrefix"/> followed by a GUID are touched.
    /// </summary>
    public static int RemoveLeftovers(string driveRoot)
    {
        try
        {
            return RemoveLeftovers(new DiskWipeVolume(driveRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>Whether a folder name is one of ours: <see cref="FolderPrefix"/> followed by a GUID.</summary>
    internal static bool IsWipeFolder(string name) =>
        name.StartsWith(FolderPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name[FolderPrefix.Length..], "D", out _);

    internal static int RemoveLeftovers(IWipeVolume volume)
    {
        var removed = 0;
        foreach (var entry in volume.EnumerateRoot())
        {
            if (!entry.IsFolder || !IsWipeFolder(entry.Name))
                continue;

            try
            {
                volume.DeleteFolder(entry.Path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    /// <summary>
    /// Writes over a drive's free space and removes the wipe files whatever happens, including on cancellation and
    /// on failure.
    /// </summary>
    public WipeResult Wipe(string driveRoot, bool cleanFileTable, IProgress<WipeProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Wipe(new DiskWipeVolume(driveRoot), cleanFileTable, progress, cancellationToken);
    }

    internal WipeResult Wipe(IWipeVolume volume, bool cleanFileTable, IProgress<WipeProgress>? progress, CancellationToken cancellationToken)
    {
        var reporter = new WipeReporter(progress);
        var total = volume.FreeBytes;
        var cluster = Math.Max(volume.ClusterSize, 1);
        long written = 0;
        var entries = 0;
        var cleaned = false;
        string? folder = null;

        try
        {
            folder = volume.CreateFolder(FolderPrefix + Guid.NewGuid().ToString("D"));
            written = WriteFreeSpace(volume, folder, cluster, total, reporter, cancellationToken);
            if (cleanFileTable && CanCleanFileTable(volume.FileSystem))
            {
                entries = CleanFileTable(volume, folder, reporter, cancellationToken);
                cleaned = true;
            }
        }
        finally
        {
            if (folder is not null)
            {
                reporter.Force(new WipeProgress(WipePhase.RemovingWipeFiles, written, total, entries));
                TryDelete(volume, folder);
            }
        }

        return new WipeResult(written, entries, cleaned);
    }

    /// <summary>
    /// Writes full-size files until the volume is full, then halving the target down to one cluster so the last
    /// fragments of free space are written over too.
    /// </summary>
    private long WriteFreeSpace(IWipeVolume volume, string folder, long cluster, long total, WipeReporter reporter, CancellationToken cancellationToken)
    {
        var limit = volume.FileSystem == WipeFileSystem.Fat32 ? Fat32MaxFileSize / cluster * cluster : long.MaxValue;
        var target = Math.Min(MaxFileSize, limit);
        var written = 0L;
        var index = 0;

        reporter.Force(new WipeProgress(WipePhase.WritingFreeSpace, written, total, 0));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = false;
            try
            {
                using var file = volume.CreateFile(folder, $"data{index++:D6}");
                try
                {
                    for (var remaining = target; remaining > 0;)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var chunk = (int)Math.Min(_buffer.Length, remaining);
                        var data = _buffer.AsSpan(0, chunk);
                        RandomNumberGenerator.Fill(data);
                        file.Write(data);
                        written += chunk;
                        remaining -= chunk;
                        reporter.Report(new WipeProgress(WipePhase.WritingFreeSpace, written, total, 0));
                    }
                }
                catch (VolumeFullException)
                {
                    // Whatever the file holds stays: it is random data that fills space we still owe.
                    full = true;
                }

                // Also when the volume filled up: Windows may drop the cached data of a deleted file unwritten.
                file.Flush();
            }
            catch (VolumeFullException)
            {
                full = true;
            }

            if (!full)
                continue;
            if (target <= cluster)
                break;

            target = Math.Max(target / 2, cluster);
        }

        return written;
    }

    /// <summary>
    /// On NTFS, fills the file table's free entries with small files whose contents live inside the table itself,
    /// until there is no room for another entry or the cap is reached.
    /// </summary>
    private int CleanFileTable(IWipeVolume volume, string folder, WipeReporter reporter, CancellationToken cancellationToken)
    {
        var count = 0;
        reporter.Force(new WipeProgress(WipePhase.CleaningFileTable, 0, 0, count));
        while (count < FileTableCap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contents = _buffer.AsSpan(0, RandomNumberGenerator.GetInt32(FileTableEntryMaxLength + 1));
            RandomNumberGenerator.Fill(contents);
            if (!volume.TryCreateSmallFile(folder, $"table{count:D8}", contents))
                break;

            count++;
            reporter.Report(new WipeProgress(WipePhase.CleaningFileTable, 0, 0, count));
        }

        return count;
    }

    private static void TryDelete(IWipeVolume volume, string folder)
    {
        try
        {
            volume.DeleteFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start, which removes wipe folders it finds at the root of the drive.
        }
    }

    /// <summary>Reports progress to the UI, but not so often that it floods the UI thread.</summary>
    private sealed class WipeReporter(IProgress<WipeProgress>? progress)
    {
        private const int IntervalMs = 50;

        private long _lastReport = long.MinValue;

        public void Report(WipeProgress value)
        {
            var now = Environment.TickCount64;
            if (progress is null || now - _lastReport < IntervalMs)
                return;

            _lastReport = now;
            progress.Report(value);
        }

        /// <summary>Reports a phase change at once, however recently the last report was sent.</summary>
        public void Force(WipeProgress value)
        {
            _lastReport = Environment.TickCount64;
            progress?.Report(value);
        }
    }
}
