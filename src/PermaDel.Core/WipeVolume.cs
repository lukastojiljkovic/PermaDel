using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PermaDel.Core;

/// <summary>Thrown when a volume has no room left for the data being written to it.</summary>
public sealed class VolumeFullException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>An entry directly under a volume root, so leftover wipe folders can be found.</summary>
internal readonly record struct WipeEntry(string Path, string Name, bool IsFolder);

/// <summary>A file that a wipe is writing.</summary>
internal interface IWipeFile : IDisposable
{
    /// <summary>Writes data to the file, throwing <see cref="VolumeFullException"/> when the volume is full.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Flushes the file's data to the physical disk.</summary>
    void Flush();
}

/// <summary>
/// The file system operations a free-space wipe needs. Keeping them behind this seam lets the wipe's logic run in
/// tests against a temporary directory that reports "full" after a chosen number of bytes.
/// </summary>
internal interface IWipeVolume
{
    /// <summary>Bytes free on the volume right now.</summary>
    long FreeBytes { get; }

    /// <summary>Bytes per cluster, the smallest amount the file system can allocate.</summary>
    long ClusterSize { get; }

    /// <summary>Which file system the volume is formatted with.</summary>
    WipeFileSystem FileSystem { get; }

    /// <summary>The entries directly under the volume root.</summary>
    IReadOnlyList<WipeEntry> EnumerateRoot();

    /// <summary>Creates the wipe folder, hidden and left out of the search index, and returns its path.</summary>
    string CreateFolder(string name);

    /// <summary>Opens a new file in the folder for writing.</summary>
    IWipeFile CreateFile(string folder, string name);

    /// <summary>
    /// Creates a small file whose contents fit inside the file table itself, returning <see langword="false"/> when
    /// the file system has no room for another entry.
    /// </summary>
    bool TryCreateSmallFile(string folder, string name, ReadOnlySpan<byte> contents);

    /// <summary>Deletes the folder and everything in it.</summary>
    void DeleteFolder(string folder);
}

/// <summary>The file system operations of a real drive.</summary>
internal sealed class DiskWipeVolume : IWipeVolume
{
    private const int RetryAttempts = 5;
    private const FileAttributes WipeFolderAttributes = FileAttributes.Hidden | FileAttributes.NotContentIndexed;
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    private readonly string _root;

    public DiskWipeVolume(string root)
    {
        var full = Path.GetFullPath(root);
        _root = Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
        FreeBytes = new DriveInfo(_root).AvailableFreeSpace;
        ClusterSize = Math.Max(VolumeProbe.ClusterSize(_root), 1);
        FileSystem = FreeSpaceWiper.FromFileSystemName(VolumeProbe.FileSystemName(_root));
    }

    public long FreeBytes { get; }

    public long ClusterSize { get; }

    public WipeFileSystem FileSystem { get; }

    public IReadOnlyList<WipeEntry> EnumerateRoot() =>
        new DirectoryInfo(_root).EnumerateFileSystemInfos("*", AllEntries)
            .Select(info => new WipeEntry(info.FullName, info.Name, info is DirectoryInfo))
            .ToList();

    public string CreateFolder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(LongPath.ToVerbatim(path));
        File.SetAttributes(LongPath.ToVerbatim(path), WipeFolderAttributes);
        return path;
    }

    public IWipeFile CreateFile(string folder, string name) => new DiskWipeFile(Path.Combine(folder, name));

    public bool TryCreateSmallFile(string folder, string name, ReadOnlySpan<byte> contents)
    {
        try
        {
            // Not flushed: the contents live in the file's $MFT record, which Windows always writes back, and deleting
            // the file only marks the record free. A flush per file would make this phase several times slower.
            using var file = new DiskWipeFile(Path.Combine(folder, name));
            file.Write(contents);
            return true;
        }
        catch (VolumeFullException)
        {
            return false;
        }
    }

    public void DeleteFolder(string folder)
    {
        var path = LongPath.ToVerbatim(folder);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < RetryAttempts)
            {
                // An antivirus scanner or the search indexer may briefly hold a handle on a file just written.
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private sealed class DiskWipeFile : IWipeFile
    {
        private const int ErrorHandleDiskFull = 0x27;
        private const int ErrorDiskFull = 0x70;
        private const string FullMessage = "The drive ran out of free space.";

        private readonly FileStream _stream;

        public DiskWipeFile(string path)
        {
            try
            {
                _stream = new FileStream(LongPath.ToVerbatim(path), FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                throw new VolumeFullException(FullMessage, ex);
            }
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            try
            {
                _stream.Write(data);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                throw new VolumeFullException(FullMessage, ex);
            }
        }

        public void Flush()
        {
            try
            {
                _stream.Flush(flushToDisk: true);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                throw new VolumeFullException(FullMessage, ex);
            }
        }

        public void Dispose() => _stream.Dispose();

        private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is ErrorDiskFull or ErrorHandleDiskFull;
    }
}

/// <summary>The facts about a volume that the .NET file system APIs don't expose.</summary>
internal static class VolumeProbe
{
    private const uint FileReadOnlyVolume = 0x00080000;
    private const int ErrorWriteProtect = 19;
    private const uint IoctlDiskIsWritable = 0x00070024;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;

    /// <summary>The file system's name, such as "NTFS", or null when the volume can't be read.</summary>
    public static string? FileSystemName(string root)
    {
        var name = new StringBuilder(64);
        return GetVolumeInformationW(root, null, 0, out _, out _, out _, name, name.Capacity) ? name.ToString() : null;
    }

    /// <summary>The volume's cluster size in bytes, or 0 when it can't be read.</summary>
    public static long ClusterSize(string root) =>
        GetDiskFreeSpaceW(root, out var sectorsPerCluster, out var bytesPerSector, out _, out _)
            ? (long)sectorsPerCluster * bytesPerSector
            : 0;

    /// <summary>Whether the volume refuses writes.</summary>
    public static bool IsReadOnly(string root)
    {
        var flagged = GetVolumeInformationW(root, null, 0, out _, out _, out var flags, null, 0) && (flags & FileReadOnlyVolume) != 0;
        return flagged || ProbeWritable(root) == false;
    }

    /// <summary>
    /// Whether the media behind the volume has to move its heads. Null when the drive won't say, which is treated as
    /// "don't know" rather than either answer.
    /// </summary>
    public static bool? IncursSeekPenalty(string root)
    {
        using var handle = CreateFileW(VolumePath(root), 0, FileShareAll, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
            return null;

        var query = new StoragePropertyQuery { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
        var descriptor = default(DeviceSeekPenaltyDescriptor);
        return DeviceIoControlQuery(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(), ref descriptor, Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(), out _, 0)
            ? descriptor.IncursSeekPenalty != 0
            : null;
    }

    private static bool? ProbeWritable(string root)
    {
        using var handle = CreateFileW(VolumePath(root), 0, FileShareAll, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
            return null;
        if (DeviceIoControlPlain(handle, IoctlDiskIsWritable, 0, 0, 0, 0, out _, 0))
            return true;
        return Marshal.GetLastPInvokeError() == ErrorWriteProtect ? false : null;
    }

    private static string VolumePath(string root) => @"\\.\" + Path.GetPathRoot(root)!.TrimEnd('\\');

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        public byte IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string rootPathName, StringBuilder? volumeName, int volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder? fileSystemName, int fileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceW(string rootPathName, out uint sectorsPerCluster, out uint bytesPerSector, out uint numberOfFreeClusters, out uint totalNumberOfClusters);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlQuery(SafeFileHandle device, uint code, ref StoragePropertyQuery query, int querySize, ref DeviceSeekPenaltyDescriptor descriptor, int descriptorSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlPlain(SafeFileHandle device, uint code, nint inBuffer, int inSize, nint outBuffer, int outSize, out int returned, nint overlapped);
}
