namespace PermaDel.Core;

/// <summary>The file systems whose free space PermaDel can write over.</summary>
public enum WipeFileSystem
{
    Ntfs,
    ExFat,
    Fat32,
    Other,
}

/// <summary>The step a free-space wipe is in, which is what the progress area names.</summary>
public enum WipePhase
{
    WritingFreeSpace,
    CleaningFileTable,
    RemovingWipeFiles,
}

/// <summary>Snapshot of a running free-space wipe.</summary>
public sealed record WipeProgress(WipePhase Phase, long BytesWritten, long TotalBytes, int FileTableEntries)
{
    /// <summary>How much of the free space measured at the start has been written over.</summary>
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesWritten / TotalBytes, 0, 1) : 0;
}

/// <summary>What a finished free-space wipe wrote.</summary>
public sealed record WipeResult(long BytesWritten, int FileTableEntries, bool FileTableCleaned);

/// <summary>
/// A drive as far as PermaDel could inspect it before offering to wipe its free space.
/// </summary>
/// <param name="IncursSeekPenalty">
/// <see langword="true"/> for media that has to move its heads, <see langword="false"/> for solid-state and flash
/// media, and <see langword="null"/> when the drive would not say.
/// </param>
public sealed record WipeDriveInfo(
    DriveType Type,
    WipeFileSystem FileSystem,
    bool IsReadOnly,
    bool? IncursSeekPenalty,
    long FreeBytes,
    long TotalBytes)
{
    /// <summary>Whether PermaDel offers to wipe free space on this drive.</summary>
    public bool CanWipe => FreeSpaceWiper.Supports(Type, FileSystem, IsReadOnly);
}
