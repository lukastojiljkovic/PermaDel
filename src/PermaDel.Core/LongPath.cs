namespace PermaDel.Core;

/// <summary>
/// Converts between regular and verbatim (<c>\\?\</c>) paths. Windows normalizes regular paths: it trims trailing
/// periods and spaces and resolves "." and ".." segments, so a regular path to an item named "report." or ".. " can
/// lead to a different item. Every file system operation on a shred target therefore uses the verbatim form.
/// </summary>
internal static class LongPath
{
    private const string VerbatimPrefix = @"\\?\";
    private const string VerbatimUncPrefix = @"\\?\UNC\";

    /// <summary>Verbatim, device (<c>\\.\</c>) and NT (<c>\??\</c>) paths, which can address raw devices and volumes.</summary>
    public static bool IsDevicePath(string path) =>
        path.StartsWith(VerbatimPrefix) || path.StartsWith(@"\\.\") || path.StartsWith(@"\??\");

    public static string ToVerbatim(string path) =>
        path.StartsWith(VerbatimPrefix) ? path
        : path.StartsWith(@"\\") ? VerbatimUncPrefix + path[2..]
        : VerbatimPrefix + path;

    public static string ToDisplay(string path) =>
        path.StartsWith(VerbatimUncPrefix, StringComparison.OrdinalIgnoreCase) ? @"\\" + path[VerbatimUncPrefix.Length..]
        : path.StartsWith(VerbatimPrefix) ? path[VerbatimPrefix.Length..]
        : path;
}
