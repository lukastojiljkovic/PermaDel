namespace PermaDel.Models;

public enum EntryKind { Drive, Folder, File }

/// <summary>A drive, folder or file shown in the browser and the shred list.</summary>
public sealed record FileEntry(string Name, string FullPath, EntryKind Kind, string Details, DateTime? Modified)
{
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false };
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    public bool IsContainer => Kind != EntryKind.File;

    public bool IsFolder => Kind == EntryKind.Folder;

    public string Glyph => Kind switch
    {
        EntryKind.Drive => "\uEDA2",
        EntryKind.Folder => "\uE8B7",
        _ => "\uE8A5",
    };

    public string ModifiedText => Modified?.ToString("g") ?? string.Empty;

    public string Location => Path.GetDirectoryName(FullPath) ?? FullPath;

    /// <summary>List items use this as their accessible name, which screen readers announce.</summary>
    public override string ToString() => Name;

    public static IReadOnlyList<FileEntry> EnumerateDrives() =>
        DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(FromDrive).ToList();

    /// <summary>Lists a directory folders-first, hiding protected operating system entries just like File Explorer.</summary>
    public static IReadOnlyList<FileEntry> EnumerateDirectory(string path) =>
        new DirectoryInfo(path).EnumerateFileSystemInfos("*", AllEntries)
            .Where(info => !info.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System))
            .Select(FromInfo)
            .OrderBy(entry => entry.Kind)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static FileEntry? FromPath(string path) =>
        Directory.Exists(path) ? FromInfo(new DirectoryInfo(path))
        : File.Exists(path) ? FromInfo(new FileInfo(path))
        : null;

    public static string FormatBytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} B" : $"{value:0.#} {SizeUnits[unit]}";
    }

    private static FileEntry FromInfo(FileSystemInfo info) => info is FileInfo file
        ? new FileEntry(file.Name, file.FullName, EntryKind.File, FormatBytes(file.Length), file.LastWriteTime)
        : new FileEntry(info.Name, info.FullName, EntryKind.Folder, string.Empty, info.LastWriteTime);

    private static FileEntry FromDrive(DriveInfo drive)
    {
        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? drive.DriveType switch
            {
                DriveType.Removable => "USB Drive",
                DriveType.Network => "Network Drive",
                DriveType.CDRom => "DVD Drive",
                _ => "Local Disk",
            }
            : drive.VolumeLabel;

        return new FileEntry(
            $"{label} ({drive.Name.TrimEnd('\\')})",
            drive.RootDirectory.FullName,
            EntryKind.Drive,
            $"{FormatBytes(drive.AvailableFreeSpace)} free of {FormatBytes(drive.TotalSize)}",
            null);
    }
}
