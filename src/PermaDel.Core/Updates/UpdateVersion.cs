using System.Globalization;

namespace PermaDel.Core.Updates;

/// <summary>
/// Parses the release tags PermaDel accepts: exactly
/// <c>v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;</c>. No prerelease suffixes, no
/// build metadata, no two-part versions.
/// </summary>
public static class UpdateVersion
{
    /// <summary>
    /// <c>"v1.2.3"</c> becomes <c>1.2.3</c>. Anything else (no leading <c>v</c>,
    /// missing or extra parts, non-numeric parts, overflow) returns
    /// <see langword="false"/> and never throws.
    /// </summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(tag) || tag[0] != 'v')
            return false;
        return TryParsePlain(tag[1..], out version);
    }

    /// <summary>
    /// The same grammar as <see cref="TryParseTag"/> without the leading
    /// <c>v</c>. Used by the DEBUG build's test-version override.
    /// </summary>
    internal static bool TryParsePlain(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(text))
            return false;

        var parts = text.Split('.');
        if (parts.Length != 3)
            return false;
        if (!TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor)
            || !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new Version(major, minor, patch);
        return true;
    }

    /// <summary>Digits only; the length cap keeps <see cref="int.TryParse"/> from overflowing.</summary>
    private static bool TryParsePart(string text, out int value)
    {
        value = 0;
        if (text.Length is 0 or > 9)
            return false;
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
