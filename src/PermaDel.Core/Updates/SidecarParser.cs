namespace PermaDel.Core.Updates;

/// <summary>
/// Parses a <c>.sha256</c> sidecar: the SHA-256 hash, whitespace, then the file
/// name. <c>Get-FileHash</c> and most tools write two spaces between the two,
/// but one space is accepted too, as are trailing CR/LF.
/// </summary>
public static class SidecarParser
{
    /// <summary>
    /// Returns the hash lowercased when <paramref name="body"/> is
    /// <c>"&lt;hash&gt;  &lt;expectedFileName&gt;"</c> (one or more spaces).
    /// Empty, malformed, or a sidecar naming another file returns
    /// <see langword="false"/>.
    /// </summary>
    public static bool TryParse(string? body, string expectedFileName, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrEmpty(expectedFileName))
            return false;

        var parts = body.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length == 0)
            return false;
        if (!string.Equals(parts[1], expectedFileName, StringComparison.Ordinal))
            return false;
        foreach (var c in parts[0])
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }
        // SHA-256 is 32 bytes; anything else is not a usable checksum.
        if (parts[0].Length != 64)
            return false;

        hash = parts[0].ToLowerInvariant();
        return true;
    }
}
