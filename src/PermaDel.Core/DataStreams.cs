using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PermaDel.Core;

/// <summary>A data stream of a file: the unnamed default stream (empty name) or an NTFS alternate stream (":name:$DATA").</summary>
internal readonly record struct DataStream(string Name, long Length);

internal static class DataStreams
{
    private const string DefaultStreamName = "::$DATA";

    /// <summary>
    /// Lists every data stream of a file. Volumes without stream support (FAT, exFAT, some network shares)
    /// yield the default stream alone.
    /// </summary>
    public static IReadOnlyList<DataStream> Enumerate(string path)
    {
        using var handle = FindFirstStreamW(LongPath.ToVerbatim(path), 0, out var data, 0);
        if (handle.IsInvalid)
            return [new DataStream(string.Empty, new FileInfo(path).Length)];

        var streams = new List<DataStream>();
        do
            streams.Add(new DataStream(data.StreamName == DefaultStreamName ? string.Empty : data.StreamName, data.StreamSize));
        while (FindNextStreamW(handle, out data));
        return streams;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260 + 36)]
        public string StreamName;
    }

    private sealed class SafeFindHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => FindClose(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFindHandle FindFirstStreamW(string fileName, int infoLevel, out Win32FindStreamData data, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(SafeFindHandle handle, out Win32FindStreamData data);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);
}
