using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PermaDel.Core;

/// <summary>
/// File metadata that .NET doesn't expose. Handles are opened for attribute access only, which never conflicts with
/// how other processes share the file, and a final symbolic link or junction is opened itself rather than followed.
/// </summary>
internal static class NativeFile
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FlagBackupSemantics = 0x02000000;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const int FileStandardInfoClass = 1;
    private const int MaxPathLength = 32767;

    /// <summary>The number of names (hard links) that refer to the file's data.</summary>
    public static int GetLinkCount(string path)
    {
        using var handle = Open(path);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, FileStandardInfoClass, out var info, (uint)Marshal.SizeOf<FileStandardInfo>()))
            throw new IOException(new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        return (int)info.NumberOfLinks;
    }

    /// <summary>
    /// The path with every junction, symbolic link, mount point, mapped drive and 8.3 short name along it resolved,
    /// or null when it can't be determined.
    /// </summary>
    public static string? GetFinalPath(string path)
    {
        using var handle = Open(path);
        if (handle.IsInvalid)
            return null;

        var buffer = new char[MaxPathLength];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        return length is > 0 and < MaxPathLength ? LongPath.ToDisplay(new string(buffer, 0, (int)length)) : null;
    }

    private static SafeFileHandle Open(string path) =>
        CreateFileW(LongPath.ToVerbatim(path), FileReadAttributes, FileShareAll, 0, OpenExisting, FlagBackupSemantics | FlagOpenReparsePoint, 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileStandardInfo information, uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);
}
